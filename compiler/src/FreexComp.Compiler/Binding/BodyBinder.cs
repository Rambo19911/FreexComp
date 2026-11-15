using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Binds the statements of one body (CMP-114: a program block, an initialization/finalization section, later a
/// routine body) against its scope.
/// </summary>
internal sealed partial class BodyBinder
{
    private readonly BuiltInTypes _types;
    private Scope _scope;
    private readonly DiagnosticBag _diagnostics;
    private readonly ExpressionBinder _expressions;
    private readonly Conversions _conversions = new();
    private readonly RoutineSymbol? _routine;
    private int _loopDepth;

    /// <summary>Exception handlers lexically enclosing the statement being bound (LANG-807: <c>raise;</c>).</summary>
    private int _handlerDepth;

    /// <summary>The loop depth at the innermost enclosing <c>finally</c> part, or -1 outside any (LANG-812, E2067).</summary>
    private int _finallyLoopDepth = -1;

    /// <param name="routine">The routine whose body this is; null for a unit-level body.</param>
    public BodyBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics, RoutineSymbol? routine = null)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
        _routine = routine;
        _expressions = new ExpressionBinder(types, scope, diagnostics, routine);
    }

    /// <summary>The expression binder of this body (its anonymous methods and closure, C-120).</summary>
    public ExpressionBinder Expressions => _expressions;

    /// <summary>Control variables of the enclosing <c>for</c> loops (LANG-361 rule 6).</summary>
    private HashSet<Symbol> ForVariables => _expressions.ForVariables;

    public BoundBlock BindBlock(SyntaxNode syntax, SeparatedSyntaxList<StatementSyntax> statements) => new(syntax, BindStatements(statements, 0));

    /// <summary>
    /// The statements from <paramref name="start"/> on; an inline <c>var</c>/<c>const</c> (LANG-385, C-125) opens a block scope for the
    /// rest of the list, which becomes the body of its <see cref="BoundLocalsStatement"/>.
    /// </summary>
    private ImmutableArray<BoundStatement> BindStatements(SeparatedSyntaxList<StatementSyntax> statements, int start)
    {
        var bound = ImmutableArray.CreateBuilder<BoundStatement>();
        for (var i = start; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (statement is InlineVarStatementSyntax or InlineConstStatementSyntax)
            {
                var (locals, initialization, scope) = BindInlineDeclaration(statement);
                var next = i + 1;
                var rest = InScope(scope, () => BindStatements(statements, next));
                bound.Add(new BoundLocalsStatement(statement, locals, new BoundBlock(statement, [.. initialization, .. rest])));
                break;
            }

            if (statement is not EmptyStatementSyntax)
            {
                bound.Add(BindStatement(statement));
            }
        }

        return bound.ToImmutable();
    }

    public BoundStatement BindStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case EmptyStatementSyntax:
                return new BoundBlock(statement, []);
            case CompoundStatementSyntax compound:
                return BindBlock(compound, compound.Statements);
            case AssignmentStatementSyntax assignment:
                return BindAssignment(assignment);
            case ExpressionStatementSyntax expression:
                return BindExpressionStatement(expression);
            case IfStatementSyntax ifStatement:
                return BindIf(ifStatement);
            case WhileStatementSyntax whileStatement:
                return BindWhile(whileStatement);
            case RepeatStatementSyntax repeat:
                return BindRepeat(repeat);
            case ForStatementSyntax forStatement:
                return BindFor(forStatement);
            case CaseStatementSyntax caseStatement:
                return BindCase(caseStatement);
            case InheritedStatementSyntax inherited:
                return _expressions.BindMethodCallStatement(inherited, inherited.Expression)!;      // LANG-624
            case RaiseStatementSyntax raise:
                return BindRaise(raise);
            case TryExceptStatementSyntax tryExcept:
                return BindTryExcept(tryExcept);
            case TryFinallyStatementSyntax tryFinally:
                return BindTryFinally(tryFinally);
            case WithStatementSyntax with:
                return BindWith(with, 0);
            case ForInStatementSyntax forIn:
                return BindForIn(forIn);
            default:
                if (!statement.ContainsDiagnostics)
                {
                    _diagnostics.Report(statement, "E2900", DeclarationBinder.FeatureName(statement.Kind));
                }

                return new BoundErrorStatement(statement);
        }
    }

    /// <summary>LANG-350/351: the target must be a variable reference; the value is converted to its type.</summary>
    private BoundStatement BindAssignment(AssignmentStatementSyntax assignment)
    {
        var target = FunctionResult(assignment.Target) ?? _expressions.BindAssignmentTarget(assignment.Target);
        if (target is BoundPropertyAccessExpression property)
        {
            return _expressions.BindPropertyAssignment(assignment, property);           // LANG-641
        }

        var value = _expressions.BindExpression(assignment.Value, target.Type);           // a set constructor takes the target's type
        if (target.HasErrors)
        {
            return new BoundAssignmentStatement(assignment, target, value);
        }

        switch (BoundIndexExpression.RootOf(target))                                    // an array element is assignable like its array
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(assignment.Target, "E2053");                        // LANG-361 rule 6
                return new BoundAssignmentStatement(assignment, target, value);
            case BoundVariableExpression { Variable.IsReadOnly: true }:
            case BoundDereferenceExpression { Pointer: BoundVariableExpression { Variable: var with } } when _expressions.ReadOnlyWith.Contains(with):
                _diagnostics.Report(assignment.Target, "E2053");                        // a typed constant (LANG-344), a const record via with (C-198)
                return new BoundAssignmentStatement(assignment, target, value);
            case BoundVariableExpression:
            case BoundDereferenceExpression:
            case BoundFieldAccessExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            default:
                _diagnostics.Report(assignment.Target, "E2053");
                return new BoundAssignmentStatement(assignment, target, value);
        }

        if (target.Type is OpenArrayTypeSymbol && !value.HasErrors)
        {
            _diagnostics.Report(assignment.Value, "E2010", value.Type.Name, target.Type.Name);  // LANG-431: never assigned as a whole (C-106)
            return new BoundAssignmentStatement(assignment, target, value);
        }

        return new BoundAssignmentStatement(assignment, target, _expressions.ConvertForAssignment(value, target.Type, assignment.Value));
    }

    /// <summary>
    /// LANG-308: the name of the current function, or of an enclosing one (LANG-450), on the left of <c>:=</c> is that
    /// function's <c>Result</c>.
    /// </summary>
    private BoundVariableExpression? FunctionResult(ExpressionSyntax target)
    {
        if (target is not IdentifierNameSyntax { Identifier: { IsMissing: false } token } name || _routine is null)
        {
            return null;
        }

        var candidates = _scope.LookupCandidates(token.Value as string ?? token.Text);
        for (var routine = _routine; routine != null; routine = routine.ContainingSymbol as RoutineSymbol)
        {
            if (routine.ResultVariable is { } result && candidates.Contains(routine))
            {
                _expressions.NoteReference(result);
                return new BoundVariableExpression(name, result);
            }
        }

        return null;
    }

    /// <summary>Outer symbols referenced by this body (CMP-345).</summary>
    public IReadOnlyList<Symbol> CapturedVariables => _expressions.CapturedVariables;

    public IReadOnlyCollection<Symbol> AddressTakenVariables => _expressions.AddressTakenVariables;

    /// <summary>LANG-452: control variables of enclosing routines' <c>for</c> loops are read-only here.</summary>
    public void AddReadOnlyVariables(IEnumerable<Symbol> variables) => ForVariables.UnionWith(variables);

    /// <summary>LANG-352: a designator statement must be a call; control intrinsics.</summary>
    private BoundStatement BindExpressionStatement(ExpressionStatementSyntax statement)
    {
        var name = CalledName(statement.Expression);
        var symbol = name is null ? null : _scope.For(name).Lookup(name.Identifier.Value as string ?? name.Identifier.Text);
        if (symbol is IntrinsicSymbol { IsFunction: false } intrinsic)
        {
            return BindControlIntrinsic(statement, intrinsic);
        }

        if (_expressions.BindMethodCallStatement(statement, statement.Expression) is { } methodCall)
        {
            return methodCall;                                                  // M(x), o.M(x), TFoo.Create (C-081)
        }

        if (symbol is RoutineSymbol)
        {
            var (routine, arguments) = _expressions.BindCall(statement.Expression, name!, (statement.Expression as InvocationExpressionSyntax)?.ArgumentList);
            return routine is null ? new BoundErrorStatement(statement) : new BoundCallStatement(statement, routine, arguments);
        }

        if (_expressions.BindIndirectCallStatement(statement) is { } indirect)
        {
            return indirect;                                                    // p; p(x); r.F(x) (LANG-484)
        }

        var expression = _expressions.BindExpression(statement.Expression);
        if (symbol is IntrinsicSymbol { IsAtomic: true } && statement.Expression is InvocationExpressionSyntax)
        {
            return new BoundExpressionStatement(statement, expression);       // C-168: the result may be discarded
        }

        if (!expression.HasErrors)
        {
            _diagnostics.Report(statement.Expression, "E2060", expression.Type.Name);
        }

        return new BoundExpressionStatement(statement, expression);
    }

    // ------------------------------------------------------------------ if, while, repeat (LANG-354, 360)

    private BoundIfStatement BindIf(IfStatementSyntax syntax)
    {
        var condition = _expressions.BindCondition(syntax.Condition);
        var then = BindStatement(syntax.Statement);
        var otherwise = syntax.ElseStatement is { } elseStatement ? BindStatement(elseStatement) : null;
        return new BoundIfStatement(syntax, condition, then, otherwise);
    }

    private BoundWhileStatement BindWhile(WhileStatementSyntax syntax)
    {
        var condition = _expressions.BindCondition(syntax.Condition);
        _loopDepth++;
        var body = BindStatement(syntax.Statement);
        _loopDepth--;
        return new BoundWhileStatement(syntax, condition, body);
    }

    private BoundRepeatStatement BindRepeat(RepeatStatementSyntax syntax)
    {
        _loopDepth++;
        var body = BindBlock(syntax, syntax.Statements);
        _loopDepth--;
        var condition = _expressions.BindCondition(syntax.Condition);
        return new BoundRepeatStatement(syntax, body, condition);
    }

    // ------------------------------------------------------------------ for (LANG-361)

    private BoundStatement BindFor(ForStatementSyntax syntax)
    {
        if (syntax.VarKeyword is not null)
        {
            return BindInlineFor(syntax);                                               // LANG-386 (C-125)
        }

        var variable = _expressions.BindExpression(syntax.Variable);
        var start = _expressions.BindExpression(syntax.Start);
        var finish = _expressions.BindExpression(syntax.Finish);
        return BindForLoop(syntax, variable, start, finish);
    }

    private BoundStatement BindForLoop(ForStatementSyntax syntax, BoundExpression variable, BoundExpression start, BoundExpression finish)
    {
        Symbol? control = null;
        if (!variable.HasErrors)
        {
            if (ControlVariable(variable) is { } v)
            {
                control = v;
                if (ForVariables.Contains(v))
                {
                    _diagnostics.Report(syntax.Variable, "E2053");                      // an inner loop would assign it
                }

                start = _expressions.ConvertForAssignment(start, variable.Type, syntax.Start);
                finish = _expressions.ConvertForAssignment(finish, variable.Type, syntax.Finish);
            }
            else
            {
                _diagnostics.Report(syntax.Variable, "E2065");
                variable = new BoundErrorExpression(syntax.Variable, _types.Error, [variable]);
            }
        }

        var added = control != null && ForVariables.Add(control);
        _loopDepth++;
        var body = BindStatement(syntax.Statement);
        _loopDepth--;
        if (added)
        {
            ForVariables.Remove(control!);
        }

        return new BoundForStatement(syntax, variable, start, finish, syntax.DirectionKeyword.Kind == SyntaxKind.DownToKeyword, body);
    }

    private int _hiddenCount;

    /// <summary>
    /// LANG-362 (C-111): <c>for x in e do s</c> over a static, dynamic or open array (element order), a string (code units) or a
    /// set (ascending ordinals) as an ordinary counting loop: <c>e</c> is evaluated once into a hidden local (an addressable static
    /// array by address; a dynamic array or string as a counted reference held for the loop; an open array parameter is used
    /// directly), then <c>for $i := lo to hi do begin x := e[$i]; s end</c> (sets: <c>if $i in $c then …</c>). <c>x</c> is a simple
    /// local/global like a <c>for</c> control variable (E2065) but of any type assignment-compatible with the element (E2010) and
    /// read-only in <c>s</c> (E2053); E2069 for a collection of another type.
    /// </summary>
    private BoundStatement BindForIn(ForInStatementSyntax syntax)
    {
        if (syntax.VarKeyword is not null)
        {
            return BindInlineForIn(syntax, _expressions.BindExpression(syntax.Collection));     // LANG-386 (C-125)
        }

        var variable = _expressions.BindExpression(syntax.Variable);
        return BindForInLoop(syntax, variable, _expressions.BindExpression(syntax.Collection));
    }

    private BoundStatement BindForInLoop(ForInStatementSyntax syntax, BoundExpression variable, BoundExpression collection)
    {
        if (variable.HasErrors || collection.HasErrors)
        {
            return new BoundErrorStatement(syntax);
        }

        if (ControlVariable(variable, ordinal: false) is not { } control)
        {
            _diagnostics.Report(syntax.Variable, "E2065");
            return new BoundErrorStatement(syntax);
        }

        if (ForVariables.Contains(control))
        {
            _diagnostics.Report(syntax.Variable, "E2053");
        }

        if (collection.Type is ClassTypeSymbol or RecordTypeSymbol or InterfaceTypeSymbol)
        {
            return BindEnumeratorLoop(syntax, variable, control, collection);           // LANG-362 enumerator pattern (C-125)
        }

        var at = syntax.Collection;
        TypeSymbol? elementType = collection.Type switch
        {
            ArrayTypeSymbol array => array.ElementType,
            DynamicArrayTypeSymbol dynamic => dynamic.ElementType,
            OpenArrayTypeSymbol open => open.ElementType,
            StringTypeSymbol text => text.Variant == StringVariant.Unicode ? _types.Char : _types.AnsiChar,
            SetTypeSymbol { ElementType: { } member } => member,
            _ => null,
        };
        if (elementType is null)
        {
            _diagnostics.Report(at, "E2069", collection.Type.Name);
            return new BoundErrorStatement(syntax);
        }

        var locals = ImmutableArray.CreateBuilder<VariableSymbol>();
        VariableSymbol Hidden(string kind, TypeSymbol type)
        {
            var hidden = new VariableSymbol("$" + kind + ++_hiddenCount, type, isGlobal: false, _routine, []);
            locals.Add(hidden);
            return hidden;
        }

        BoundExpression Ref(VariableSymbol hidden) => new BoundVariableExpression(at, hidden);
        var prologue = new List<BoundStatement>();
        BoundExpression source;
        if (collection.Type is OpenArrayTypeSymbol)
        {
            source = collection;
        }
        else if (collection.Type is ArrayTypeSymbol staticArray && ExpressionBinder.IsAddressable(collection))
        {
            var pointer = new PointerTypeSymbol("^" + staticArray.Name, staticArray, allowsPointerMath: false);
            var reference = Hidden("in", pointer);
            prologue.Add(new BoundAssignmentStatement(at, Ref(reference), new BoundAddressOfExpression(at, collection, routine: null, pointer)));
            source = new BoundDereferenceExpression(at, Ref(reference), staticArray);
            if (BoundIndexExpression.RootOf(collection) is BoundVariableExpression { Variable: var root })
            {
                _expressions.AddressTakenVariables.Add(root);
            }
        }
        else
        {
            var copy = Hidden("in", collection.Type);
            prologue.Add(new BoundAssignmentStatement(at, Ref(copy), collection));
            source = Ref(copy);
        }

        // the index range: a static array's index type, a set's element range within its capacity, else 0..High / 1..Length
        var (indexType, low, high) = collection.Type switch
        {
            ArrayTypeSymbol array => (array.IndexType, (BoundExpression)Ordinal(at, array.Low, array.IndexType), (BoundExpression)Ordinal(at, array.High, array.IndexType)),
            SetTypeSymbol set => (elementType, Ordinal(at, BigInteger.Max(Ordinals.Bounds(elementType)!.Value.Low, 0), elementType),
                Ordinal(at, BigInteger.Min(Ordinals.Bounds(elementType)!.Value.High, set.Capacity - 1), elementType)),
            StringTypeSymbol => (_types.Integer, Ordinal(at, BigInteger.One, _types.Integer), Intrinsic(at, "Length", source)),
            _ => (_types.Integer, Ordinal(at, BigInteger.Zero, _types.Integer), Intrinsic(at, "High", source)),
        };
        var index = Hidden("i", indexType);
        var element = collection.Type is SetTypeSymbol ? Ref(index) : new BoundIndexExpression(at, source, Ref(index), elementType);
        var store = new BoundAssignmentStatement(syntax.Variable, variable, _expressions.ConvertForAssignment(element, variable.Type, at));
        if (store.Value.HasErrors)
        {
            return new BoundErrorStatement(syntax);
        }

        var added = ForVariables.Add(control);
        _loopDepth++;
        var body = BindStatement(syntax.Statement);
        _loopDepth--;
        if (added)
        {
            ForVariables.Remove(control);
        }

        BoundStatement step = new BoundBlock(syntax, [store, body]);
        if (collection.Type is SetTypeSymbol setType)
        {
            var member = new BoundBinaryExpression(at, BoundBinaryOperatorKind.In, Ref(index), source, setType, _types.Boolean, constantValue: null);
            step = new BoundIfStatement(syntax, member, step, @else: null);
        }

        var loop = new BoundForStatement(syntax, Ref(index), low, high, isDownTo: false, step);
        return new BoundLocalsStatement(syntax, locals.ToImmutable(), new BoundBlock(syntax, [.. prologue, loop]));
    }

    /// <summary>The ordinal <paramref name="value"/> of <paramref name="type"/> as a literal (Booleans as True/False).</summary>
    private static BoundLiteral Ordinal(SyntaxNode syntax, BigInteger value, TypeSymbol type) =>
        new(syntax, type is BooleanTypeSymbol ? new ConstantValue(!value.IsZero, type) : new ConstantValue(value, type));

    /// <summary><c>Length(e)</c> / <c>High(e)</c> of a string or array value at run time (an <c>Integer</c>).</summary>
    private BoundExpression Intrinsic(SyntaxNode syntax, string name, BoundExpression operand) =>
        new BoundIntrinsicCallExpression(syntax, (IntrinsicSymbol)_types.Scope.LookupLocal(name)!, [operand], _types.Integer, constantValue: null);

    /// <summary>
    /// Rule 1: ordinal (<paramref name="ordinal"/>); a local (or value parameter) of this routine, or a global variable in a
    /// unit-level body; null when <paramref name="variable"/> is not one.
    /// </summary>
    private Symbol? ControlVariable(BoundExpression variable, bool ordinal = true) => variable switch
    {
        _ when ordinal && !variable.Type.IsOrdinal => null,
        BoundVariableExpression { Variable: var v } when _inlineVariables.Contains(v) => v,                       // C-125
        BoundVariableExpression { Variable: var v } when (_routine is null ? v.IsGlobal : !v.IsGlobal && ReferenceEquals(v.ContainingSymbol, _routine)) => v,
        BoundParameterExpression { Parameter: { Mode: ParameterMode.Value } p } when ReferenceEquals(p.ContainingSymbol, _routine) => p,
        _ => null,
    };

    // ------------------------------------------------------------------ case (LANG-355…357)

    private BoundCaseStatement BindCase(CaseStatementSyntax syntax)
    {
        var selector = _expressions.BindExpression(syntax.Selector);
        var checkLabels = !selector.HasErrors;
        if (checkLabels && !selector.Type.IsOrdinal)
        {
            _diagnostics.Report(syntax.Selector, "E2062");
            selector = new BoundErrorExpression(syntax.Selector, _types.Error, [selector]);
            checkLabels = false;
        }

        var seen = new List<(BigInteger Low, BigInteger High)>();
        var arms = ImmutableArray.CreateBuilder<BoundCaseArm>();
        foreach (var arm in syntax.Arms)
        {
            var labels = ImmutableArray.CreateBuilder<BoundCaseLabel>();
            foreach (var label in arm.Labels)
            {
                labels.Add(BindCaseLabel(label, checkLabels ? selector.Type : null, seen));
            }

            arms.Add(new BoundCaseArm(arm, labels.ToImmutable(), BindStatement(arm.Statement)));
        }

        var otherwise = syntax.ElseKeyword is null ? null : BindBlock(syntax, syntax.ElseStatements);
        return new BoundCaseStatement(syntax, selector, arms.ToImmutable(), otherwise);
    }

    /// <summary>
    /// Constants converted to the selector type (<paramref name="selectorType"/> null: selector has errors, labels are
    /// only bound); E2063 for an empty range, E2064 for a label overlapping an earlier one.
    /// </summary>
    private BoundCaseLabel BindCaseLabel(CaseLabelSyntax label, TypeSymbol? selectorType, List<(BigInteger Low, BigInteger High)> seen)
    {
        var low = CaseLabelValue(label.Value, selectorType);
        var high = label.UpperBound is { } upper ? CaseLabelValue(upper, selectorType) : null;
        if (low.ConstantValue is not { } lowValue || (high != null && high.ConstantValue is null))
        {
            return new BoundCaseLabel(label, low, high);
        }

        var lo = Ordinal(lowValue);
        var hi = high?.ConstantValue is { } highValue ? Ordinal(highValue) : lo;
        if (lo > hi)
        {
            _diagnostics.Report(label, "E2063");
        }
        else if (seen.Any(s => lo <= s.High && s.Low <= hi))
        {
            var text = high is null ? lowValue.ToDisplayString() : $"{lowValue.ToDisplayString()}..{high.ConstantValue!.ToDisplayString()}";
            _diagnostics.Report(label, "E2064", text);
        }
        else
        {
            seen.Add((lo, hi));
        }

        return new BoundCaseLabel(label, low, high);
    }

    private BoundExpression CaseLabelValue(ExpressionSyntax syntax, TypeSymbol? selectorType)
    {
        var value = _expressions.BindExpression(syntax);
        if (value.HasErrors || selectorType is null)
        {
            return value;
        }

        if (value.ConstantValue is null)
        {
            _diagnostics.Report(syntax, "E2050");
            return new BoundErrorExpression(syntax, _types.Error, [value]);
        }

        return _expressions.ConvertForAssignment(value, selectorType, syntax);
    }

    // ------------------------------------------------------------------ exceptions (LANG-805…826)

    /// <summary>LANG-805…807: <c>raise E [at A]</c> needs a class instance (E2800); <c>raise;</c> only inside a handler (E2801).</summary>
    private BoundStatement BindRaise(RaiseStatementSyntax syntax)
    {
        if (syntax.Exception is null)
        {
            if (_handlerDepth == 0)
            {
                _diagnostics.Report(syntax, "E2801");
                return new BoundErrorStatement(syntax);
            }

            return new BoundRaiseStatement(syntax, exception: null, address: null);
        }

        var exception = _expressions.BindExpression(syntax.Exception);
        if (!exception.HasErrors && exception.Type is not ClassTypeSymbol && !ReferenceEquals(exception.Type, _types.Nil))
        {
            _diagnostics.Report(syntax.Exception, "E2800");
            exception = new BoundErrorExpression(syntax.Exception, _types.Error, [exception]);
        }

        var address = syntax.Address is { } at
            ? _expressions.ConvertForAssignment(_expressions.BindExpression(at), _types.Pointer, at)
            : null;
        return exception.HasErrors || address?.HasErrors == true ? new BoundErrorStatement(syntax) : new BoundRaiseStatement(syntax, exception, address);
    }

    /// <summary>
    /// LANG-820…822: the handlers are bound in order, each in a block scope holding its read-only variable; a handler type
    /// must be a class (E2802); one that an earlier handler's class (or an ancestor) already catches gives W1080.
    /// </summary>
    private BoundStatement BindTryExcept(TryExceptStatementSyntax syntax)
    {
        var body = BindBlock(syntax, syntax.Statements);
        var handlers = ImmutableArray.CreateBuilder<BoundExceptionHandler>();
        var hasErrors = false;
        _handlerDepth++;
        foreach (var handler in syntax.Handlers)
        {
            var type = HandlerType(handler.Type);
            if (type is null)
            {
                hasErrors = true;
                continue;
            }

            if (handlers.Any(h => type.SelfAndAncestors().Contains(h.Type)))
            {
                _diagnostics.Report(handler.Type, "W1080", type.Name);
            }

            VariableSymbol? variable = null;
            var scope = _scope;
            if (handler.Identifier is { IsMissing: false } identifier)
            {
                variable = new VariableSymbol(identifier.Value as string ?? identifier.Text, type, isGlobal: false, _routine,
                    [DiagnosticBag.LocationOf(identifier)]) { IsReadOnly = true };
                scope = new Scope(ScopeKind.Block, _scope, _routine);
                scope.TryDeclare(variable, out _);
            }

            var statement = InScope(scope, () => BindStatement(handler.Statement));
            handlers.Add(new BoundExceptionHandler(handler, variable, type, statement));
        }

        var otherwise = syntax.ElseKeyword is not null || syntax.Handlers.Count == 0 ? BindBlock(syntax, syntax.ExceptStatements) : null;
        _handlerDepth--;
        return hasErrors ? new BoundErrorStatement(syntax) : new BoundTryExceptStatement(syntax, body, handlers.ToImmutable(), otherwise);
    }

    /// <summary>LANG-810, 812: <c>Exit</c>, and <c>Break</c>/<c>Continue</c> of loops outside, may not leave the finally part (E2067).</summary>
    private BoundTryFinallyStatement BindTryFinally(TryFinallyStatementSyntax syntax)
    {
        var body = BindBlock(syntax, syntax.Statements);
        var outer = _finallyLoopDepth;
        _finallyLoopDepth = _loopDepth;
        var final = BindBlock(syntax, syntax.FinallyStatements);
        _finallyLoopDepth = outer;
        return new BoundTryFinallyStatement(syntax, body, final);
    }

    /// <summary>The class named after <c>on [E:]</c>; null after E2003 or E2802.</summary>
    private ClassTypeSymbol? HandlerType(NamedTypeSyntax syntax)
    {
        var name = syntax.Name switch
        {
            IdentifierNameSyntax identifier => identifier,
            QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified when UnitQualifiers.Qualifier(_scope, qualified.Left) is not null => right,
            _ => null,
        };
        if (name is { Identifier.IsMissing: true })
        {
            return null;
        }

        if (name is not null)
        {
            var text = name.Identifier.Value as string ?? name.Identifier.Text;
            switch (_scope.For(name).Lookup(text))
            {
                case null:
                    _diagnostics.ReportUndeclared(name, text);
                    return null;
                case ClassTypeSymbol type:
                    return type;
            }
        }

        _diagnostics.Report(syntax, "E2802");
        return null;
    }

    private int _withCount;

    /// <summary>
    /// LANG-375 (C-109): <c>with d1, d2 do s</c> = <c>with d1 do with d2 do s</c>. Each designator (record, class: E2068 otherwise) is
    /// evaluated once into a hidden local <c>$withN</c> — the address of an addressable record (so changes reach the original),
    /// else the value — and a scope of its members (an object's inherited ones too, nearest first) is innermost while binding
    /// the rest.
    /// </summary>
    private BoundStatement BindWith(WithStatementSyntax syntax, int index)
    {
        if (index == syntax.Expressions.Count)
        {
            return BindStatement(syntax.Statement);
        }

        var designatorSyntax = syntax.Expressions[index];
        var designator = _expressions.BindExpression(designatorSyntax);
        if (designator.HasErrors)
        {
            return new BoundErrorStatement(syntax);
        }

        var scope = new Scope(ScopeKind.With, _scope, _routine);
        var name = "$with" + ++_withCount;
        VariableSymbol variable;
        BoundExpression value;
        switch (designator.Type)
        {
            case RecordTypeSymbol record when ExpressionBinder.IsAddressable(designator):
            {
                var pointer = new PointerTypeSymbol("^" + record.Name, record, allowsPointerMath: false);
                variable = new VariableSymbol(name, pointer, isGlobal: false, _routine, []);
                value = new BoundAddressOfExpression(designatorSyntax, designator, routine: null, pointer);
                switch (BoundIndexExpression.RootOf(designator))
                {
                    case BoundVariableExpression { Variable.IsReadOnly: true } or BoundParameterExpression { Parameter.Mode: ParameterMode.Const }:
                        _expressions.ReadOnlyWith.Add(variable);           // C-198: a const record stays read-only through $with
                        break;
                    case BoundVariableExpression { Variable: var root }:
                        _expressions.AddressTakenVariables.Add(root);      // CMP-325 rule 4: the body may write it through $with
                        break;
                }

                foreach (var member in record.Members)                          // C-122: methods, properties, nested names too
                {
                    scope.TryDeclare(member, out _);
                }

                break;
            }

            case RecordTypeSymbol record:
                variable = new VariableSymbol(name, record, isGlobal: false, _routine, []);
                value = designator;
                foreach (var member in record.Members)                          // C-122: methods, properties, nested names too
                {
                    scope.TryDeclare(member, out _);
                }

                break;
            case ClassTypeSymbol type:
                variable = new VariableSymbol(name, type, isGlobal: false, _routine, []);
                value = designator;
                foreach (var member in type.SelfAndAncestors().SelectMany(t => t.Members))
                {
                    scope.TryDeclare(member, out _);                            // the nearest declaration of a name wins
                }

                break;
            default:
                _diagnostics.Report(designatorSyntax, "E2068");
                return new BoundErrorStatement(syntax);
        }

        var with = new ExpressionBinder.WithContext(scope, variable, designator.Type);
        var outer = _scope;
        _scope = scope;
        try
        {
            return new BoundWithStatement(syntax, variable, value, _expressions.InWith(with, () => BindWith(syntax, index + 1)));
        }
        finally
        {
            _scope = outer;
        }
    }

    private T InScope<T>(Scope scope, Func<T> bind)
    {
        var outer = _scope;
        _scope = scope;
        try
        {
            return _expressions.InScope(scope, bind);
        }
        finally
        {
            _scope = outer;
        }
    }

    private static BigInteger Ordinal(ConstantValue value) => value.Value switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        _ => throw new InvalidOperationException($"Not an ordinal constant: {value.ToDisplayString()}"),
    };

    // ------------------------------------------------------------------ Break, Continue, Exit, Halt (LANG-363, 364)

    /// <summary>The name a statement <c>Name</c> or <c>Name(…)</c> calls; null for other expressions.</summary>
    private IdentifierNameSyntax? CalledName(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            InvocationExpressionSyntax invocation => _scope.Unqualified(invocation.Expression) as IdentifierNameSyntax,
            _ => _scope.Unqualified(expression) as IdentifierNameSyntax,          // also U.Name (LANG-311)
        };
        return name is null || name.Identifier.IsMissing ? null : name;
    }

    private BoundStatement BindControlIntrinsic(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic)
    {
        var arguments = statement.Expression is InvocationExpressionSyntax invocation ? invocation.ArgumentList.Arguments : default;
        if (intrinsic.Intrinsic is IntrinsicKind.Write or IntrinsicKind.Writeln)
        {
            return BindWrite(statement, intrinsic, arguments);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Read or IntrinsicKind.Readln)
        {
            return BindRead(statement, intrinsic, arguments);
        }

        var count = arguments.Count;
        var (minArguments, maxArguments) = intrinsic.Intrinsic switch
        {
            IntrinsicKind.Halt or IntrinsicKind.RunError => (0, 1),
            IntrinsicKind.Exit when _routine?.ReturnType != null => (0, 1),          // LANG-413
            IntrinsicKind.Inc or IntrinsicKind.Dec => (1, 2),
            IntrinsicKind.Include or IntrinsicKind.Exclude => (2, 2),
            IntrinsicKind.New or IntrinsicKind.Dispose or IntrinsicKind.FreeMem => (1, 1),
            IntrinsicKind.GetMem or IntrinsicKind.ReallocMem => (2, 2),
            IntrinsicKind.FillChar or IntrinsicKind.Move => (3, 3),
            IntrinsicKind.Assert => (1, 2),
            IntrinsicKind.SetLength => (2, int.MaxValue),
            IntrinsicKind.Str => (2, 2),
            IntrinsicKind.Delete or IntrinsicKind.Insert or IntrinsicKind.Val => (3, 3),
            _ => (0, 0),
        };
        if (count < minArguments)
        {
            _diagnostics.Report(statement.Expression, "E2110", intrinsic.Name);
            return new BoundErrorStatement(statement);
        }

        if (count > maxArguments)
        {
            _diagnostics.Report(arguments[maxArguments], "E2111", intrinsic.Name);
            return new BoundErrorStatement(statement);
        }

        // the value of Str may carry :w[:d] (LANG-951); everything else rejects widths
        foreach (var argument in intrinsic.Intrinsic == IntrinsicKind.Str ? arguments.Skip(1) : arguments)
        {
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                return new BoundErrorStatement(statement);
            }
        }

        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.SetLength or IntrinsicKind.Delete or IntrinsicKind.Insert:
                return BindStringEdit(statement, intrinsic, arguments);
            case IntrinsicKind.Str:
                return BindStr(statement, intrinsic, arguments);
            case IntrinsicKind.Val:
                return BindVal(statement, intrinsic, arguments);
            case IntrinsicKind.Inc or IntrinsicKind.Dec:
                return BindIncDec(statement, intrinsic, arguments);
            case IntrinsicKind.RunError:
                return new BoundIntrinsicCallStatement(statement, intrinsic, count == 0 ? []
                    : [_expressions.ConvertForAssignment(_expressions.BindExpression(arguments[0].Expression), _types.Integer, arguments[0].Expression)]);
            case IntrinsicKind.Break or IntrinsicKind.Continue when _loopDepth == 0:
                _diagnostics.Report(statement.Expression, "E2066", intrinsic.Name);
                return new BoundErrorStatement(statement);
            case IntrinsicKind.Break or IntrinsicKind.Continue when _loopDepth == _finallyLoopDepth:
            case IntrinsicKind.Exit when _finallyLoopDepth >= 0:
                _diagnostics.Report(statement.Expression, "E2067", intrinsic.Name);         // LANG-812
                return new BoundErrorStatement(statement);
            case IntrinsicKind.Break:
                return new BoundBreakStatement(statement);
            case IntrinsicKind.Continue:
                return new BoundContinueStatement(statement);
            case IntrinsicKind.Exit when count == 1:
                return new BoundExitStatement(statement, _expressions.ConvertForAssignment(
                    _expressions.BindExpression(arguments[0].Expression, _routine!.ReturnType), _routine.ReturnType!, arguments[0].Expression));
            case IntrinsicKind.Include or IntrinsicKind.Exclude:
                return BindIncludeExclude(statement, intrinsic, arguments);
            case IntrinsicKind.New or IntrinsicKind.Dispose or IntrinsicKind.GetMem or IntrinsicKind.FreeMem or IntrinsicKind.ReallocMem:
                return BindMemory(statement, intrinsic, arguments);
            case IntrinsicKind.FillChar or IntrinsicKind.Move:
                return BindFillMove(statement, intrinsic, arguments);
            case IntrinsicKind.Assert:
                return BindAssert(statement, intrinsic, arguments);
            case IntrinsicKind.Exit:
                return new BoundExitStatement(statement, value: null);
            default:
                var code = count == 1
                    ? _expressions.ConvertForAssignment(_expressions.BindExpression(arguments[0].Expression), _types.Integer, arguments[0].Expression)
                    : null;
                return new BoundHaltStatement(statement, code);
        }
    }

    /// <summary>
    /// intrinsics.md <c>Assert(Cond[, Msg])</c>: <c>Cond</c> converted to <c>Boolean</c> (E2010), <c>Msg</c> a string or character
    /// constant, kept as a <c>string</c> constant (string variables wait for the string tasks: E2900). Bound also under
    /// <c>{$C-}</c>; the lowering drops it there.
    /// </summary>
    private BoundStatement BindAssert(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var condition = _expressions.ConvertForAssignment(_expressions.BindExpression(arguments[0].Expression), _types.Boolean, arguments[0].Expression);
        if (arguments.Count == 1)
        {
            return condition.HasErrors ? new BoundErrorStatement(statement) : new BoundIntrinsicCallStatement(statement, intrinsic, [condition]);
        }

        var syntax = arguments[1].Expression;
        var message = _expressions.BindExpression(syntax);
        if (condition.HasErrors || message.HasErrors)
        {
            return new BoundErrorStatement(statement);
        }

        switch (message)
        {
            case { ConstantValue.Value: string text }:
                message = new BoundLiteral(syntax, new ConstantValue(text, _types.String));
                break;
            case { Type: CharTypeSymbol, ConstantValue.Value: BigInteger code }:
                message = new BoundLiteral(syntax, new ConstantValue(((char)(int)code).ToString(), _types.String));
                break;
            case { Type: StringTypeSymbol or CharTypeSymbol }:
                // C-060: any string value (a character converted, LANG-245; other string kinds wait for C-063)
                message = _expressions.ConvertForAssignment(message, _types.String, syntax);
                if (message.HasErrors)
                {
                    return new BoundErrorStatement(statement);
                }

                break;
            default:
                _conversions.ReportIncompatible(message.Type, _types.String, syntax, _diagnostics);
                return new BoundErrorStatement(statement);
        }

        return new BoundIntrinsicCallStatement(statement, intrinsic, [condition, message]);
    }

    // ------------------------------------------------------------------ string procedures (intrinsics.md §1, §4; C-062)

    /// <summary>
    /// <c>SetLength(var s; n)</c>, <c>Delete(var s; index, count)</c>, <c>Insert(const sub; var s; index)</c> on a string
    /// variable <c>s</c> (every string kind, C-067; <c>SetLength</c> also of a dynamic array; other types E2010); <c>sub</c> a string or character; the
    /// integers converted to <c>Integer</c>. Arguments in parameter order.
    /// </summary>
    private BoundStatement BindStringEdit(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var targetIndex = intrinsic.Intrinsic == IntrinsicKind.Insert ? 1 : 0;
        var bound = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Count);
        for (var k = 0; k < arguments.Count; k++)
        {
            var syntax = arguments[k].Expression;
            var value = k == targetIndex ? StringVariable(syntax, intrinsic)
                : k == 0 ? Operand(syntax, v => _expressions.StringOperand(v, syntax, intrinsic.Name))
                : Operand(syntax, v => _expressions.ConvertForAssignment(v, _types.Integer, syntax));
            if (value is null)
            {
                return new BoundErrorStatement(statement);
            }

            bound.Add(value);
            // SetLength: one length for a string, up to one per nesting level for a dynamic array (intrinsics.md, C-064)
            var maxLengths = intrinsic.Intrinsic != IntrinsicKind.SetLength ? int.MaxValue : bound[0].Type is DynamicArrayTypeSymbol d ? d.Depth : 1;
            if (k == 0 && arguments.Count - 1 > maxLengths)
            {
                _diagnostics.Report(arguments[maxLengths + 1], "E2111", intrinsic.Name);
                return new BoundErrorStatement(statement);
            }
        }

        return new BoundIntrinsicCallStatement(statement, intrinsic, bound.ToImmutable());
    }

    /// <summary>An expression bound and then checked/converted by <paramref name="check"/>; null after any error.</summary>
    private BoundExpression? Operand(ExpressionSyntax syntax, Func<BoundExpression, BoundExpression> check)
    {
        var value = _expressions.BindExpression(syntax);
        if (value.HasErrors)
        {
            return null;
        }

        value = check(value);
        return value.HasErrors ? null : value;
    }

    /// <summary>The <c>var s: string</c> argument: an assignable string variable (or a dynamic array for <c>SetLength</c>); null after E2112/E2113/E2010.</summary>
    private BoundExpression? StringVariable(ExpressionSyntax syntax, IntrinsicSymbol intrinsic)
    {
        var target = _expressions.BindVariable(syntax);
        if (target.HasErrors || !_expressions.CheckVariableReference(target, syntax))
        {
            return null;
        }

        switch (target.Type)
        {
            // UnicodeString: its helpers; SetLength: _AStrSetLen (C-063), the short-string length byte (C-067); Delete/Insert/Str of
            // the byte-string kinds edit a UnicodeString copy (C-067)
            case StringTypeSymbol:
            case DynamicArrayTypeSymbol when intrinsic.Intrinsic == IntrinsicKind.SetLength:                               // _DynArraySetLen (C-064)
                return target;
            default:
                _diagnostics.Report(syntax, "E2010", target.Type.Name, _types.String.Name);
                return null;
        }
    }

    /// <summary>
    /// LANG-951 <c>Str(x[:w[:d]]; var s)</c>: <c>x</c> an integer or real value (E2010 otherwise; <c>:d</c> for reals only, E2912), kept
    /// unconverted; arguments <c>[x, s, w?, d?]</c> with the widths converted to <c>Integer</c>.
    /// </summary>
    private BoundStatement BindStr(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var argument = arguments[0];
        var value = _expressions.BindExpression(argument.Expression);
        if (value.HasErrors)
        {
            return new BoundErrorStatement(statement);
        }

        if (value.Type is not (IntegerTypeSymbol or RealTypeSymbol or BooleanTypeSymbol or CharTypeSymbol
            or StringTypeSymbol) && !Strings.IsCharPointer(value.Type, wide: true) && !Strings.IsCharPointer(value.Type, wide: false))
        {
            _diagnostics.Report(argument.Expression, "E2010", value.Type.Name, _types.Integer.Name);
            return new BoundErrorStatement(statement);
        }

        if (value.Type is StringTypeSymbol or CharTypeSymbol or PointerTypeSymbol)
        {
            value = _expressions.StringOperand(value, argument.Expression, intrinsic.Name);   // LANG-951 like Write: as a string (C-063)
        }
        else if (value.Type is BooleanTypeSymbol)
        {
            value = _expressions.ConvertForAssignment(value, _types.Boolean, argument.Expression);
        }

        if (argument.DecimalsColonToken is { } decimalsColon && value.Type is not RealTypeSymbol)
        {
            _diagnostics.Report(decimalsColon, "E2912");
            return new BoundErrorStatement(statement);
        }

        var width = argument.Width is { } w ? Operand(w, v => _expressions.ConvertForAssignment(v, _types.Integer, w)) : null;
        var decimals = argument.Decimals is { } d ? Operand(d, v => _expressions.ConvertForAssignment(v, _types.Integer, d)) : null;
        var target = StringVariable(arguments[1].Expression, intrinsic);
        if ((argument.Width != null && width is null) || (argument.Decimals != null && decimals is null) || target is null)
        {
            return new BoundErrorStatement(statement);
        }

        var bound = ImmutableArray.CreateBuilder<BoundExpression>(4);
        bound.Add(value);
        bound.Add(target);
        if (width != null)
        {
            bound.Add(width);
        }

        if (decimals != null)
        {
            bound.Add(decimals);
        }

        return new BoundIntrinsicCallStatement(statement, intrinsic, bound.ToImmutable());
    }

    /// <summary>
    /// LANG-950 <c>Val(const s; var v; var code)</c>: <c>s</c> a string or character, <c>v</c> an assignable integer or real
    /// (not <c>Currency</c>) variable, <c>code</c> an assignable integer variable (E2010 otherwise).
    /// </summary>
    private BoundStatement BindVal(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var sourceSyntax = arguments[0].Expression;
        var source = Operand(sourceSyntax, v => _expressions.StringOperand(v, sourceSyntax, intrinsic.Name));
        var value = NumericVariable(arguments[1].Expression, real: true);
        var code = NumericVariable(arguments[2].Expression, real: false);
        return source is null || value is null || code is null
            ? new BoundErrorStatement(statement)
            : new BoundIntrinsicCallStatement(statement, intrinsic, [source, value, code]);
    }

    private BoundExpression? NumericVariable(ExpressionSyntax syntax, bool real)
    {
        var variable = _expressions.BindVariable(syntax);
        if (variable.HasErrors || !_expressions.CheckVariableReference(variable, syntax))
        {
            return null;
        }

        if (variable.Type is IntegerTypeSymbol || (real && variable.Type is RealTypeSymbol { RealKind: not RealKind.Currency }))
        {
            return variable;
        }

        _diagnostics.Report(syntax, "E2010", variable.Type.Name, _types.Integer.Name);
        return null;
    }

    // ------------------------------------------------------------------ Write, Writeln (LANG-910…922)

    /// <summary>
    /// LANG-910/911: an optional leading <c>Text</c> variable, then <c>e[:w[:d]]</c> arguments. Written: integers,
    /// Booleans, chars, string constants and reals (<c>:d</c> only for reals, E2912); string variables and <c>PChar</c>
    /// arrive with M2.2 (E2900); other types are E2913. One diagnostic per argument; the statement is an error statement after any.
    /// </summary>
    private BoundStatement BindWrite(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        BoundExpression? file = null;
        var bound = ImmutableArray.CreateBuilder<BoundWriteArgument>(arguments.Count);
        var ok = true;
        for (var k = 0; k < arguments.Count; k++)
        {
            var argument = arguments[k];
            var value = _expressions.BindExpression(argument.Expression);
            if (value.HasErrors)
            {
                ok = false;
                continue;
            }

            if (k == 0 && value.Type is TextTypeSymbol && argument.WidthColonToken is null)
            {
                ok &= _expressions.CheckVariableReference(value, argument.Expression);
                file = value;
                continue;
            }

            if (WriteTypeError(value) is { } error)
            {
                if (error.Length == 0)
                {
                    _diagnostics.Report(argument.Expression, "E2913", value.Type.Name);
                }
                else
                {
                    _diagnostics.Report(argument.Expression, "E2900", error);
                }

                ok = false;
                continue;
            }

            if (argument.DecimalsColonToken is { } decimalsColon && value.Type is not RealTypeSymbol)
            {
                _diagnostics.Report(decimalsColon, "E2912");                        // reals only (LANG-911)
                ok = false;
                continue;
            }

            var width = argument.Width is { } w
                ? _expressions.ConvertForAssignment(_expressions.BindExpression(w), _types.Integer, w)
                : null;
            var decimals = argument.Decimals is { } d
                ? _expressions.ConvertForAssignment(_expressions.BindExpression(d), _types.Integer, d)
                : null;
            ok &= width?.HasErrors != true && decimals?.HasErrors != true;
            bound.Add(new BoundWriteArgument(argument, value, width, decimals));
        }

        return ok ? new BoundWriteStatement(statement, intrinsic, file, bound.ToImmutable()) : new BoundErrorStatement(statement);
    }

    // ------------------------------------------------------------------ Read, Readln (LANG-940…946, C-066)

    /// <summary>
    /// LANG-940: an optional leading <c>Text</c> variable, then assignable variables of an integer type, a real type (not
    /// <c>Currency</c>), <c>Char</c> or <c>string</c> (E2914 for any other type; widths E2915). Bound as an intrinsic statement
    /// whose arguments are the file (when given) followed by the variables.
    /// </summary>
    private BoundStatement BindRead(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var bound = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Count);
        var ok = true;
        for (var k = 0; k < arguments.Count; k++)
        {
            var argument = arguments[k];
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                ok = false;
                continue;
            }

            var variable = _expressions.BindVariable(argument.Expression);
            if (variable.HasErrors || !_expressions.CheckVariableReference(variable, argument.Expression))
            {
                ok = false;
                continue;
            }

            if (k == 0 && variable.Type is TextTypeSymbol)
            {
                bound.Add(variable);
                continue;
            }

            if (variable.Type is not (IntegerTypeSymbol or RealTypeSymbol { RealKind: not RealKind.Currency } or CharTypeSymbol { IsWide: true }
                or StringTypeSymbol { Variant: StringVariant.Unicode }))
            {
                _diagnostics.Report(argument.Expression, "E2914", variable.Type.Name);
                ok = false;
                continue;
            }

            bound.Add(variable);
        }

        return ok ? new BoundIntrinsicCallStatement(statement, intrinsic, bound.ToImmutable()) : new BoundErrorStatement(statement);
    }

    /// <summary>Null when <paramref name="value"/> can be written; "" for E2913; else the E2900 feature name.</summary>
    private static string? WriteTypeError(BoundExpression value) => value.Type switch
    {
        IntegerTypeSymbol or BooleanTypeSymbol or CharTypeSymbol => null,
        StringTypeSymbol => null,                                                    // C-060, C-063
        RealTypeSymbol => null,
        var type when Strings.IsCharPointer(type, wide: true) || Strings.IsCharPointer(type, wide: false) || Strings.IsCharArray(type) => null,
        _ => "",
    };

    /// <summary>
    /// LANG-253: <c>Include</c>/<c>Exclude(var S; x)</c>: <c>S</c> an assignable set variable (E2014 for another type), <c>x</c> an
    /// element as in a set constructor (E2010 / E2012 for constants, unconverted otherwise).
    /// </summary>
    private BoundStatement BindIncludeExclude(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var syntax = arguments[0].Expression;
        var target = _expressions.BindVariable(syntax);
        var element = _expressions.BindExpression(arguments[1].Expression);
        if (target.HasErrors || element.HasErrors || !_expressions.CheckVariableReference(target, syntax))
        {
            return new BoundErrorStatement(statement);
        }

        if (target.Type is not SetTypeSymbol { ElementType: not null } set)
        {
            _diagnostics.Report(syntax, "E2014", "set");
            return new BoundErrorStatement(statement);
        }

        var ok = element.ConstantValue is { } value
            ? _expressions.CheckSetElement(set, value, arguments[1].Expression)
            : _expressions.CheckSetElement(set, element, arguments[1].Expression);
        return ok ? new BoundIntrinsicCallStatement(statement, intrinsic, [target, element]) : new BoundErrorStatement(statement);
    }

    /// <summary>
    /// LANG-901: <c>Inc</c>/<c>Dec(var x[; n])</c>: <c>x</c> an assignable ordinal variable (typed pointers arrive with
    /// M2.2), <c>n</c> any integer expression.
    /// </summary>
    private BoundStatement BindIncDec(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var syntax = arguments[0].Expression;
        var target = _expressions.BindVariable(syntax);
        var step = arguments.Count == 2 ? _expressions.BindExpression(arguments[1].Expression) : null;
        if (target.HasErrors || step?.HasErrors == true || !_expressions.CheckVariableReference(target, syntax))
        {
            return new BoundErrorStatement(statement);
        }

        if (target.Type is RecordTypeSymbol && step is null
            && _expressions.UserIncDec(syntax, target, intrinsic.Intrinsic == IntrinsicKind.Inc ? "Inc" : "Dec") is { } stepped)
        {
            return new BoundAssignmentStatement(statement, target, stepped);          // LANG-731 (C-123): a := TRec.Inc(a)
        }

        // intrinsics.md: a typed pointer moves by n elements; the untyped Pointer by n bytes under {$POINTERMATH ON} (LANG-247)
        var isPointer = target.Type is PointerTypeSymbol { PointedType: not null }
            || (target.Type is PointerTypeSymbol && syntax.FirstToken().DirectiveState.PointerMath);
        if (!target.Type.IsOrdinal && !isPointer)
        {
            _diagnostics.Report(syntax, "E2013");
            return new BoundErrorStatement(statement);
        }

        if (step != null && step.Type is not IntegerTypeSymbol)
        {
            _diagnostics.Report(arguments[1].Expression, "E2010", step.Type.Name, _types.Integer.Name);
            return new BoundErrorStatement(statement);
        }

        return new BoundIntrinsicCallStatement(statement, intrinsic, step is null ? [target] : [target, step]);
    }

    /// <summary>
    /// LANG-268 and intrinsics.md: <c>New(p)</c>/<c>Dispose(p)</c> on a typed pointer variable (E2018 otherwise); <c>GetMem(p, n)</c>,
    /// <c>ReallocMem(p, n)</c> on a pointer variable (E2010 otherwise), <c>n</c> converted to <c>NativeInt</c>; <c>FreeMem(p)</c> on
    /// any pointer value.
    /// </summary>
    private BoundStatement BindMemory(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var syntax = arguments[0].Expression;
        var pointer = _expressions.BindExpression(syntax);
        var size = arguments.Count == 2 ? _expressions.BindExpression(arguments[1].Expression) : null;
        if (pointer.HasErrors || size?.HasErrors == true)
        {
            return new BoundErrorStatement(statement);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.New or IntrinsicKind.Dispose)
        {
            if (pointer.Type is not PointerTypeSymbol { PointedType: not null } || ReferenceEquals(pointer.Type, _types.Nil))
            {
                _diagnostics.Report(syntax, "E2018");
                return new BoundErrorStatement(statement);
            }
        }
        else if (pointer.Type is not PointerTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2010", pointer.Type.Name, _types.Pointer.Name);
            return new BoundErrorStatement(statement);
        }

        if (intrinsic.Intrinsic is not (IntrinsicKind.FreeMem or IntrinsicKind.Dispose) && !_expressions.CheckVariableReference(pointer, syntax))
        {
            return new BoundErrorStatement(statement);
        }

        if (size is null)
        {
            return new BoundIntrinsicCallStatement(statement, intrinsic, [pointer]);
        }

        size = _expressions.ConvertForAssignment(size, _types.NativeInt, arguments[1].Expression);
        return size.HasErrors ? new BoundErrorStatement(statement) : new BoundIntrinsicCallStatement(statement, intrinsic, [pointer, size]);
    }

    /// <summary>
    /// intrinsics.md: <c>FillChar(var X; Count; Value)</c> with an ordinal <c>Value</c> (E2010 otherwise) and
    /// <c>Move(const Source; var Dest; Count)</c>; the untyped arguments are variable references of any type (also <c>p^</c> of an
    /// untyped pointer); <c>Count</c> is converted to <c>NativeInt</c>.
    /// </summary>
    private BoundStatement BindFillMove(ExpressionStatementSyntax statement, IntrinsicSymbol intrinsic, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var fill = intrinsic.Intrinsic == IntrinsicKind.FillChar;
        var first = _expressions.BindUntypedArgument(arguments[0].Expression);
        var second = fill ? _expressions.BindExpression(arguments[2].Expression) : _expressions.BindUntypedArgument(arguments[1].Expression);
        var count = _expressions.BindExpression(arguments[fill ? 1 : 2].Expression);
        if (first.HasErrors || second.HasErrors || count.HasErrors)
        {
            return new BoundErrorStatement(statement);
        }

        var target = fill ? first : second;
        var targetSyntax = arguments[fill ? 0 : 1].Expression;
        if (!_expressions.CheckVariableReference(target, targetSyntax))
        {
            return new BoundErrorStatement(statement);
        }

        count = _expressions.ConvertForAssignment(count, _types.NativeInt, arguments[fill ? 1 : 2].Expression);
        if (count.HasErrors)
        {
            return new BoundErrorStatement(statement);
        }

        if (fill && !second.Type.IsOrdinal)
        {
            _diagnostics.Report(arguments[2].Expression, "E2010", second.Type.Name, _types.Byte.Name);
            return new BoundErrorStatement(statement);
        }

        // arguments in parameter order: FillChar(X, Count, Value), Move(Source, Dest, Count)
        return new BoundIntrinsicCallStatement(statement, intrinsic, fill ? [first, count, second] : [first, second, count]);
    }
}
