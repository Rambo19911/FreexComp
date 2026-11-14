using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Flow analysis of bound bodies (CMP-115, LANG-390): W1040 (for variable read after its loop), W1041 (local read
/// before any assignment on some path), W1042 (function result undefined on some exit), H2001 (local never
/// referenced), H2002 (assigned value never read). Only bodies without binder errors are analysed. Locals referenced
/// by nested routines are left out of W1041/W1042/H2002 (a call may read or assign them). Warning switches
/// (CMP-505) apply at each reported token.
/// </summary>
internal sealed class FlowAnalyzer
{
    private readonly RoutineSymbol? _routine;
    private readonly HashSet<VariableSymbol> _tracked = [];          // W1041/W1042 candidates (simple non-managed locals, Result)
    private readonly HashSet<VariableSymbol> _deadStoreTracked = []; // H2002 candidates (simple non-managed locals)
    private readonly HashSet<VariableSymbol> _referenced = [];
    private readonly Dictionary<(VariableSymbol Variable, string Code), SyntaxNode> _flagged = [];
    private readonly List<(VariableSymbol Variable, SyntaxNode Target)> _stores = [];
    private readonly HashSet<SyntaxNode> _readStores = [];
    private readonly List<State> _exits = [];
    private readonly Stack<LoopContext> _loops = new();
    private readonly List<Diagnostic> _diagnostics = [];

    private FlowAnalyzer(RoutineSymbol? routine) => _routine = routine;

    /// <summary>Every routine and unit-level body of <paramref name="unit"/>, by file and position.</summary>
    public static ImmutableArray<Diagnostic> Analyze(BindingResult unit)
    {
        var all = new List<Diagnostic>();
        var routineErrors = unit.Routines.Sum(r => r.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
        if (unit.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) == routineErrors)
        {
            foreach (var body in unit.Bodies)
            {
                all.AddRange(new FlowAnalyzer(null).Run(body, [], body.Syntax.LastToken()));
            }
        }

        foreach (var body in unit.Routines)
        {
            all.AddRange(Analyze(unit, body));
        }

        return [.. all.OrderBy(d => d.Location.Path, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Location.Span.Start)];
    }

    /// <summary>One routine body (nothing when the body has binder errors).</summary>
    public static ImmutableArray<Diagnostic> Analyze(BindingResult unit, BoundRoutineBody body)
    {
        if (body.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return [];
        }

        var analyzer = new FlowAnalyzer(body.Routine);
        var captured = unit.GetCapturedVariables(body.Routine).ToHashSet();
        var locals = body.Scope.Symbols.OfType<VariableSymbol>().Distinct()
            .Where(v => ReferenceEquals(v.ContainingSymbol, body.Routine) && !ReferenceEquals(v, body.Routine.ResultVariable)).ToList();
        foreach (var local in locals.Where(v => IsSimple(v.Type) && !captured.Contains(v)))
        {
            analyzer._tracked.Add(local);
            analyzer._deadStoreTracked.Add(local);
        }

        if (body.Routine.ResultVariable is { } result && IsSimple(result.Type) && !captured.Contains(result))
        {
            analyzer._tracked.Add(result);
        }

        var definition = body.Routine.Definition!;
        var diagnostics = analyzer.Run(body.Body, locals.Where(v => !captured.Contains(v)), definition.Block!.Body.LastToken());
        return [.. diagnostics.OrderBy(d => d.Location.Span.Start)];
    }

    /// <summary>Scalar types that are assigned as a whole and are not initialised (LANG-391).</summary>
    private static bool IsSimple(TypeSymbol type) =>
        !type.IsManaged && type.TypeKind is TypeKind.Integer or TypeKind.Boolean or TypeKind.Char or TypeKind.Real or TypeKind.Enum
            or TypeKind.Subrange or TypeKind.Set or TypeKind.Pointer or TypeKind.Class or TypeKind.ClassRef or TypeKind.Procedural;

    private List<Diagnostic> Run(BoundBlock body, IEnumerable<VariableSymbol> locals, SyntaxToken endToken)
    {
        var entry = new State();
        entry.Unassigned.UnionWith(_tracked);
        var end = Join(Visit(body, entry), _exits);

        foreach (var ((variable, code), node) in _flagged)
        {
            Report(node.FirstToken(), DiagnosticBag.LocationOf(node), code, variable.Name);
        }

        if (_routine?.ResultVariable is { } result && _tracked.Contains(result) && end != null && end.Unassigned.Contains(result))
        {
            Report(endToken, DiagnosticBag.LocationOf(endToken), "W1042", _routine.Name);
        }

        foreach (var local in locals.Where(v => !_referenced.Contains(v)))
        {
            if (DeclarationToken(local) is { } token)
            {
                Report(token, DiagnosticBag.LocationOf(token), "H2001", local.Name);
            }
        }

        foreach (var (variable, target) in _stores.Where(s => _deadStoreTracked.Contains(s.Variable) && !_readStores.Contains(s.Target)).DistinctBy(s => s.Target))
        {
            Report(target.FirstToken(), DiagnosticBag.LocationOf(target), "H2002", variable.Name);
        }

        return _diagnostics;
    }

    private SyntaxToken? DeclarationToken(VariableSymbol local)
    {
        if (local.Locations.IsEmpty || _routine?.Definition?.Block is not { } block)
        {
            return null;
        }

        var location = local.Locations[0];
        foreach (var declaration in block.Declarations)
        {
            foreach (var token in declaration.DescendantTokens())
            {
                var candidate = DiagnosticBag.LocationOf(token);
                if (candidate.Span == location.Span && string.Equals(candidate.Path, location.Path, StringComparison.Ordinal))
                {
                    return token;
                }
            }
        }

        return null;
    }

    /// <summary>CMP-505: <c>{$WARN code …}</c>, then <c>{$WARNINGS}</c>/<c>{$HINTS}</c>, in force at <paramref name="token"/>.</summary>
    private void Report(SyntaxToken token, Location location, string code, params object[] args)
    {
        var diagnostic = Diagnostic.Create(code, location, args);
        var state = token.DirectiveState;
        if (state.WarnOverrides.TryGetValue(code, out var setting))
        {
            if (setting == WarnSetting.Off)
            {
                return;
            }

            if (setting == WarnSetting.Error)
            {
                diagnostic = diagnostic with { Severity = DiagnosticSeverity.Error };
            }
        }
        else if ((diagnostic.Severity == DiagnosticSeverity.Warning && !state.WarningsOn) || (diagnostic.Severity == DiagnosticSeverity.Hint && !state.HintsOn))
        {
            return;
        }

        _diagnostics.Add(diagnostic);
    }

    // ------------------------------------------------------------------ state

    /// <summary>What may hold at a program point; null (no state) = unreachable.</summary>
    private sealed class State
    {
        /// <summary>Tracked variables that may be unassigned here.</summary>
        public HashSet<VariableSymbol> Unassigned { get; } = [];

        /// <summary>For control variables that may be undefined here (after their loop).</summary>
        public HashSet<VariableSymbol> ForUndefined { get; } = [];

        /// <summary>Per H2002 variable: the assignments whose value may still be read from here.</summary>
        public Dictionary<VariableSymbol, HashSet<SyntaxNode>> Pending { get; } = [];

        public State Clone()
        {
            var copy = new State();
            copy.UnionWith(this);
            return copy;
        }

        public void UnionWith(State other)
        {
            Unassigned.UnionWith(other.Unassigned);
            ForUndefined.UnionWith(other.ForUndefined);
            foreach (var (variable, stores) in other.Pending)
            {
                if (!Pending.TryGetValue(variable, out var mine))
                {
                    Pending.Add(variable, mine = []);
                }

                mine.UnionWith(stores);
            }
        }

        public bool SameAs(State other) =>
            Unassigned.SetEquals(other.Unassigned) && ForUndefined.SetEquals(other.ForUndefined)
            && Pending.Count == other.Pending.Count
            && Pending.All(p => other.Pending.TryGetValue(p.Key, out var o) && o.SetEquals(p.Value));
    }

    private sealed class LoopContext
    {
        public List<State> Breaks { get; } = [];

        public List<State> Continues { get; } = [];
    }

    private static State? Clone(State? state) => state?.Clone();

    /// <summary>A fresh state: the union of the reachable inputs (null if none is reachable).</summary>
    private static State? Join(State? first, IEnumerable<State?> others)
    {
        State? result = Clone(first);
        foreach (var other in others)
        {
            if (other is null)
            {
                continue;
            }

            if (result is null)
            {
                result = other.Clone();
            }
            else
            {
                result.UnionWith(other);
            }
        }

        return result;
    }

    private static State? Join(State? a, State? b) => Join(a, [b]);

    private static bool Same(State? a, State? b) => a is null ? b is null : b is not null && a.SameAs(b);

    private void Read(VariableSymbol variable, SyntaxNode node, State? state)
    {
        _referenced.Add(variable);
        if (state is null)
        {
            return;
        }

        if (state.ForUndefined.Contains(variable))
        {
            Flag(variable, "W1040", node);
        }
        else if (state.Unassigned.Contains(variable))
        {
            Flag(variable, "W1041", node);
        }

        if (state.Pending.Remove(variable, out var stores))
        {
            _readStores.UnionWith(stores);
        }
    }

    private void Flag(VariableSymbol variable, string code, SyntaxNode node)
    {
        if (!_flagged.TryGetValue((variable, code), out var earlier) || node.Span.Start < earlier.Span.Start)
        {
            _flagged[(variable, code)] = node;
        }
    }

    /// <summary>An assignment; <paramref name="store"/> is the target of a <c>:=</c> that H2002 may report.</summary>
    private void Assign(VariableSymbol variable, State? state, SyntaxNode? store)
    {
        _referenced.Add(variable);
        if (state is null)
        {
            return;
        }

        state.Unassigned.Remove(variable);
        state.ForUndefined.Remove(variable);
        state.Pending.Remove(variable);
        if (store != null && _deadStoreTracked.Contains(variable))
        {
            state.Pending.Add(variable, [store]);
            _stores.Add((variable, store));
        }
    }

    // ------------------------------------------------------------------ statements

    private State? Visit(BoundStatement statement, State? state)
    {
        switch (statement)
        {
            case BoundBlock block:
                foreach (var s in block.Statements)
                {
                    state = Visit(s, state);
                }

                return state;

            case BoundAssignmentStatement assignment:
                // a variable typecast of the same size overwrites the whole variable (LANG-290)
                if ((assignment.Target as BoundVariableExpression ?? (assignment.Target as BoundVariableTypecastExpression)?.Operand as BoundVariableExpression) is { } target)
                {
                    state = Eval(assignment.Value, state);
                    Assign(target.Variable, state, target.Syntax);
                    return state;
                }

                return Eval(assignment.Value, Eval(assignment.Target, state));

            case BoundExpressionStatement expression:
                return Eval(expression.Expression, state);

            case BoundErrorStatement:
                return state;

            case BoundIfStatement @if:
            {
                var (whenTrue, whenFalse) = Condition(@if.Condition, state);
                var then = Visit(@if.Then, whenTrue);
                var @else = @if.Else is null ? whenFalse : Visit(@if.Else, whenFalse);
                return Join(then, @else);
            }

            case BoundWhileStatement @while:
                return VisitWhile(@while, state);

            case BoundRepeatStatement repeat:
                return VisitRepeat(repeat, state);

            case BoundForStatement @for:
                return VisitFor(@for, state);

            case BoundCaseStatement @case:
            {
                state = Eval(@case.Selector, state);
                var arms = @case.Arms.Select(a => Visit(a.Body, Clone(state))).ToList();
                return Join(@case.Else is null ? state : Visit(@case.Else, Clone(state)), arms);
            }

            case BoundBreakStatement:
                if (state != null && _loops.Count > 0)
                {
                    _loops.Peek().Breaks.Add(state);
                }

                return null;

            case BoundContinueStatement:
                if (state != null && _loops.Count > 0)
                {
                    _loops.Peek().Continues.Add(state);
                }

                return null;

            case BoundExitStatement exit:
                if (exit.Value != null)
                {
                    state = Eval(exit.Value, state);
                    if (_routine?.ResultVariable is { } result)
                    {
                        Assign(result, state, null);
                    }
                }

                if (state != null)
                {
                    _exits.Add(state);
                }

                return null;

            case BoundHaltStatement halt:
                Eval(halt.ExitCode, state);
                return null;

            case BoundCallStatement call:
                state = Call(call.Routine, call.Arguments, state);
                return call.Routine.IsNoReturn ? null : state;

            case BoundIntrinsicCallStatement intrinsic:
                return IntrinsicStatement(intrinsic, state);

            case BoundWriteStatement write:
                state = Eval(write.File, state);
                foreach (var argument in write.Arguments)
                {
                    state = Eval(argument.Decimals, Eval(argument.Width, Eval(argument.Value, state)));
                }

                return state;

            default:
                throw new InvalidOperationException($"Flow analysis does not handle {statement.Kind}.");
        }
    }

    private State? IntrinsicStatement(BoundIntrinsicCallStatement statement, State? state)
    {
        switch (statement.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.Inc or IntrinsicKind.Dec or IntrinsicKind.Include or IntrinsicKind.Exclude:
                state = Eval(statement.Arguments[0], state);                      // reads, then writes the variable
                foreach (var argument in statement.Arguments.Skip(1))
                {
                    state = Eval(argument, state);
                }

                if (statement.Arguments[0] is BoundVariableExpression target)
                {
                    Assign(target.Variable, state, null);
                }

                return state;

            case IntrinsicKind.New or IntrinsicKind.GetMem or IntrinsicKind.FillChar:
                // the first argument is only written (FillChar: Count and Value are read first)
                foreach (var argument in statement.Arguments.Skip(1))
                {
                    state = Eval(argument, state);
                }

                return AssignOrEval(statement.Arguments[0], state);

            case IntrinsicKind.Move:
                return AssignOrEval(statement.Arguments[1], Eval(statement.Arguments[2], Eval(statement.Arguments[0], state)));

            case IntrinsicKind.SetLength or IntrinsicKind.Delete or IntrinsicKind.Insert:
            {
                // the string is read and written (C-062)
                foreach (var argument in statement.Arguments)
                {
                    state = Eval(argument, state);
                }

                if (statement.Arguments[statement.Intrinsic.Intrinsic == IntrinsicKind.Insert ? 1 : 0] is BoundVariableExpression edited)
                {
                    Assign(edited.Variable, state, null);
                }

                return state;
            }

            case IntrinsicKind.Str:
                // [x, s, w?, d?]: the value and widths are read, the string only written
                state = Eval(statement.Arguments[0], state);
                foreach (var argument in statement.Arguments.Skip(2))
                {
                    state = Eval(argument, state);
                }

                return AssignOrEval(statement.Arguments[1], state);

            case IntrinsicKind.Val:
                return AssignOrEval(statement.Arguments[2], AssignOrEval(statement.Arguments[1], Eval(statement.Arguments[0], state)));

            case IntrinsicKind.Dispose or IntrinsicKind.FreeMem or IntrinsicKind.ReallocMem:
                foreach (var argument in statement.Arguments)
                {
                    state = Eval(argument, state);
                }

                return state;

            case IntrinsicKind.Assert:
                return Eval(statement.Arguments[0], state);                       // the failing branch never returns

            case IntrinsicKind.RunError:
                foreach (var argument in statement.Arguments)
                {
                    state = Eval(argument, state);
                }

                return null;

            default:
                throw new InvalidOperationException($"Flow analysis does not handle intrinsic {statement.Intrinsic.Intrinsic}.");
        }
    }

    /// <summary>A written argument: a variable becomes assigned; other variable references are evaluated (their parts are read).</summary>
    private State? AssignOrEval(BoundExpression target, State? state)
    {
        if (target is BoundVariableExpression variable)
        {
            Assign(variable.Variable, state, null);
            return state;
        }

        return Eval(target, state);
    }

    private State? VisitWhile(BoundWhileStatement statement, State? entry)
    {
        var head = Clone(entry);
        while (true)
        {
            var loop = new LoopContext();
            var (whenTrue, whenFalse) = Condition(statement.Condition, Clone(head));
            _loops.Push(loop);
            var bodyEnd = Visit(statement.Body, whenTrue);
            _loops.Pop();
            var next = Join(entry, loop.Continues.Prepend(bodyEnd));
            if (Same(next, head))
            {
                return Join(whenFalse, loop.Breaks);
            }

            head = next;
        }
    }

    private State? VisitRepeat(BoundRepeatStatement statement, State? entry)
    {
        var head = Clone(entry);
        while (true)
        {
            var loop = new LoopContext();
            _loops.Push(loop);
            var bodyEnd = Visit(statement.Body, Clone(head));
            _loops.Pop();
            var (whenTrue, whenFalse) = Condition(statement.Condition, Join(bodyEnd, loop.Continues));
            var next = Join(entry, whenFalse);                                  // the loop repeats while the condition is False
            if (Same(next, head))
            {
                return Join(whenTrue, loop.Breaks);
            }

            head = next;
        }
    }

    private State? VisitFor(BoundForStatement statement, State? entry)
    {
        entry = Eval(statement.Finish, Eval(statement.Start, entry));
        var variable = (statement.Variable as BoundVariableExpression)?.Variable;
        var head = Clone(entry);
        while (true)
        {
            var loop = new LoopContext();
            var bodyStart = Clone(head);
            if (variable != null)
            {
                Assign(variable, bodyStart, null);
            }
            else
            {
                bodyStart = Eval(statement.Variable, bodyStart);
            }

            _loops.Push(loop);
            var bodyEnd = Visit(statement.Body, bodyStart);
            _loops.Pop();
            var next = Join(entry, loop.Continues.Prepend(bodyEnd));
            if (Same(next, head))
            {
                // normal end (also after zero iterations): the control variable is undefined (LANG-390, W1040)
                if (variable != null && head != null)
                {
                    Assign(variable, head, null);
                    head.ForUndefined.Add(variable);
                }

                return Join(head, loop.Breaks);
            }

            head = next;
        }
    }

    // ------------------------------------------------------------------ expressions

    /// <summary>A condition: the states when it is True and when it is False (short-circuit operators, constants).</summary>
    private (State? WhenTrue, State? WhenFalse) Condition(BoundExpression condition, State? state)
    {
        switch (condition)
        {
            case { ConstantValue.Value: bool value }:
                Eval(condition, null);                                          // references only
                return value ? (state, null) : (null, state);

            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.ConditionalAnd } and:
            {
                var (leftTrue, leftFalse) = Condition(and.Left, state);
                var (rightTrue, rightFalse) = Condition(and.Right, leftTrue);
                return (rightTrue, Join(leftFalse, rightFalse));
            }

            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.ConditionalOr } or:
            {
                var (leftTrue, leftFalse) = Condition(or.Left, state);
                var (rightTrue, rightFalse) = Condition(or.Right, leftFalse);
                return (Join(leftTrue, rightTrue), rightFalse);
            }

            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Not, Type.TypeKind: TypeKind.Boolean } not:
            {
                var (whenTrue, whenFalse) = Condition(not.Operand, state);
                return (whenFalse, whenTrue);
            }

            default:
                state = Eval(condition, state);
                return (state, Clone(state));
        }
    }

    private State? Eval(BoundExpression? expression, State? state)
    {
        switch (expression)
        {
            case null:
            case BoundLiteral:
            case BoundConstantExpression:
            case BoundParameterExpression:
                return state;

            case BoundVariableExpression variable:
                Read(variable.Variable, variable.Syntax, state);
                return state;

            case BoundConversionExpression conversion:
                return Eval(conversion.Operand, state);

            case BoundVariableTypecastExpression typecast:
                return Eval(typecast.Operand, state);

            case BoundUnaryExpression unary:
                return Eval(unary.Operand, state);

            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.ConditionalAnd or BoundBinaryOperatorKind.ConditionalOr }:
            {
                var (whenTrue, whenFalse) = Condition(expression, state);
                return Join(whenTrue, whenFalse);
            }

            case BoundBinaryExpression binary:
                return Eval(binary.Right, Eval(binary.Left, state));

            case BoundCallExpression call:
                return Call(call.Routine, call.Arguments, state);

            case BoundIntrinsicCallExpression intrinsic:
                if (intrinsic.Intrinsic.Intrinsic is IntrinsicKind.Low or IntrinsicKind.High or IntrinsicKind.SizeOf or IntrinsicKind.Length)
                {
                    foreach (var argument in intrinsic.Arguments)
                    {
                        Eval(argument, null);                                   // never evaluated (LANG-903): references only
                    }

                    return state;
                }

                foreach (var argument in intrinsic.Arguments)
                {
                    state = Eval(argument, state);
                }

                if (intrinsic.Intrinsic.IsAtomic && intrinsic.Arguments[0] is BoundVariableExpression target)
                {
                    Assign(target.Variable, state, null);                       // read, then written (C-168)
                }

                return state;

            case BoundIndexExpression index:
                return Eval(index.Index, Eval(index.Array, state));

            case BoundFieldAccessExpression field:
                return Eval(field.Record, state);

            case BoundAddressOfExpression { Operand: BoundVariableExpression variable }:
                // C-177: the pointer may read any store, before or after this point: no H2002 for the variable at all
                _deadStoreTracked.Remove(variable.Variable);
                Assign(variable.Variable, state, null);                         // may be written through the pointer (W1041)
                return state;

            case BoundAddressOfExpression addressOf:
                return addressOf.Operand is null ? state : Eval(addressOf.Operand, state);

            case BoundDereferenceExpression dereference:
                return Eval(dereference.Pointer, state);

            case BoundPointerArithmeticExpression arithmetic:
                return Eval(arithmetic.Offset, Eval(arithmetic.Pointer, state));

            case BoundSetConstructor constructor:
                foreach (var element in constructor.Elements)
                {
                    state = Eval(element.High, Eval(element.Low, state));
                }

                return state;

            case BoundErrorExpression error:
                foreach (var child in error.Children.IsDefault ? [] : error.Children)
                {
                    state = Eval(child, state);
                }

                return state;

            default:
                throw new InvalidOperationException($"Flow analysis does not handle {expression.Kind}.");
        }
    }

    /// <summary>
    /// Arguments in order; a variable passed as <c>var</c> is read and assigned, as <c>out</c> assigned only (LANG-390).
    /// </summary>
    private State? Call(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, State? state)
    {
        var assigned = new List<VariableSymbol>();
        for (var i = 0; i < arguments.Length; i++)
        {
            var mode = i < routine.Parameters.Length ? routine.Parameters[i].Mode : ParameterMode.Value;
            if (mode is ParameterMode.Var or ParameterMode.Out && arguments[i] is BoundVariableExpression reference)
            {
                _referenced.Add(reference.Variable);
                if (mode == ParameterMode.Var && state != null && state.Pending.Remove(reference.Variable, out var stores))
                {
                    _readStores.UnionWith(stores);                              // the callee may read the value
                }

                assigned.Add(reference.Variable);
                continue;
            }

            state = Eval(arguments[i], state);
        }

        foreach (var variable in assigned)
        {
            Assign(variable, state, null);
        }

        return state;
    }
}
