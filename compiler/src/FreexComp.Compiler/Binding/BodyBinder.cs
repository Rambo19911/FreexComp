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
internal sealed class BodyBinder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly DiagnosticBag _diagnostics;
    private readonly ExpressionBinder _expressions;
    private readonly Conversions _conversions = new();
    private readonly RoutineSymbol? _routine;
    private int _loopDepth;

    /// <param name="routine">The routine whose body this is; null for a unit-level body.</param>
    public BodyBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics, RoutineSymbol? routine = null)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
        _routine = routine;
        _expressions = new ExpressionBinder(types, scope, diagnostics, routine);
    }

    /// <summary>Control variables of the enclosing <c>for</c> loops (LANG-361 rule 6).</summary>
    private HashSet<Symbol> ForVariables => _expressions.ForVariables;

    public BoundBlock BindBlock(SyntaxNode syntax, SeparatedSyntaxList<StatementSyntax> statements)
    {
        var bound = ImmutableArray.CreateBuilder<BoundStatement>();
        foreach (var statement in statements)
        {
            if (statement is not EmptyStatementSyntax)
            {
                bound.Add(BindStatement(statement));
            }
        }

        return new BoundBlock(syntax, bound.ToImmutable());
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
            default:
                if (!statement.ContainsDiagnostics)
                {
                    _diagnostics.Report(statement, "E2900", DeclarationBinder.FeatureName(statement.Kind));
                }

                return new BoundErrorStatement(statement);
        }
    }

    /// <summary>LANG-350/351: the target must be a variable reference; the value is converted to its type.</summary>
    private BoundAssignmentStatement BindAssignment(AssignmentStatementSyntax assignment)
    {
        var target = FunctionResult(assignment.Target) ?? _expressions.BindVariable(assignment.Target);
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
                _diagnostics.Report(assignment.Target, "E2053");                        // a typed constant (LANG-344)
                return new BoundAssignmentStatement(assignment, target, value);
            case BoundVariableExpression:
            case BoundDereferenceExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            default:
                _diagnostics.Report(assignment.Target, "E2053");
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
        var symbol = name is null ? null : _scope.Lookup(name.Identifier.Value as string ?? name.Identifier.Text);
        if (symbol is IntrinsicSymbol { IsFunction: false } intrinsic)
        {
            return BindControlIntrinsic(statement, intrinsic);
        }

        if (symbol is RoutineSymbol)
        {
            var (routine, arguments) = _expressions.BindCall(statement.Expression, name!, (statement.Expression as InvocationExpressionSyntax)?.ArgumentList);
            return routine is null ? new BoundErrorStatement(statement) : new BoundCallStatement(statement, routine, arguments);
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
        if (syntax.VarKeyword is { } varKeyword)
        {
            _diagnostics.Report(varKeyword, "E2900", "inline variable");               // LANG-386, M2.6
            return new BoundErrorStatement(syntax);
        }

        var variable = _expressions.BindExpression(syntax.Variable);
        var start = _expressions.BindExpression(syntax.Start);
        var finish = _expressions.BindExpression(syntax.Finish);
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

    /// <summary>
    /// Rule 1: ordinal; a local (or value parameter) of this routine, or a global variable in a unit-level body; null
    /// when <paramref name="variable"/> is not one.
    /// </summary>
    private Symbol? ControlVariable(BoundExpression variable) => variable switch
    {
        _ when !variable.Type.IsOrdinal => null,
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

    private static BigInteger Ordinal(ConstantValue value) => value.Value switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        _ => throw new InvalidOperationException($"Not an ordinal constant: {value.ToDisplayString()}"),
    };

    // ------------------------------------------------------------------ Break, Continue, Exit, Halt (LANG-363, 364)

    /// <summary>The name a statement <c>Name</c> or <c>Name(…)</c> calls; null for other expressions.</summary>
    private static IdentifierNameSyntax? CalledName(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            IdentifierNameSyntax n => n,
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax n } => n,
            _ => null,
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
            IntrinsicKind.SetLength or IntrinsicKind.Str => (2, 2),
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
    /// <c>SetLength(var s; n)</c>, <c>Delete(var s; index, count)</c>, <c>Insert(const sub; var s; index)</c> on a UnicodeString
    /// variable <c>s</c> (other string kinds and dynamic arrays: E2900; other types E2010); <c>sub</c> a string or character; the
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

    /// <summary>The <c>var s: string</c> argument: an assignable UnicodeString variable; null after E2112/E2113/E2010/E2900.</summary>
    private BoundExpression? StringVariable(ExpressionSyntax syntax, IntrinsicSymbol intrinsic)
    {
        var target = _expressions.BindVariable(syntax);
        if (target.HasErrors || !_expressions.CheckVariableReference(target, syntax))
        {
            return null;
        }

        switch (target.Type)
        {
            case StringTypeSymbol { Variant: StringVariant.Unicode }:
                return target;
            case StringTypeSymbol:
                _diagnostics.Report(syntax, "E2900", intrinsic.Name + " of a " + target.Type.Name);   // C-063
                return null;
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

        if (value.Type is not (IntegerTypeSymbol or RealTypeSymbol { RealKind: not RealKind.Currency }))
        {
            _diagnostics.Report(argument.Expression, "E2010", value.Type.Name, _types.Integer.Name);
            return new BoundErrorStatement(statement);
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

    /// <summary>Null when <paramref name="value"/> can be written; "" for E2913; else the E2900 feature name.</summary>
    private static string? WriteTypeError(BoundExpression value) => value.Type switch
    {
        IntegerTypeSymbol or BooleanTypeSymbol or CharTypeSymbol => null,
        StringTypeSymbol when value.ConstantValue != null => null,
        StringTypeSymbol { Variant: StringVariant.Unicode } => null,                 // C-060
        StringTypeSymbol => "Write of a string variable",
        RealTypeSymbol { RealKind: RealKind.Currency } => "Write of a Currency value",
        RealTypeSymbol => null,
        PointerTypeSymbol { AllowsPointerMath: true } => "Write of a PChar",
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
