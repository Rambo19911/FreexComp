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
        var target = FunctionResult(assignment.Target) ?? _expressions.BindExpression(assignment.Target);
        var value = _expressions.BindExpression(assignment.Value);
        if (target.HasErrors)
        {
            return new BoundAssignmentStatement(assignment, target, value);
        }

        switch (target)
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(assignment.Target, "E2053");                        // LANG-361 rule 6
                return new BoundAssignmentStatement(assignment, target, value);
            case BoundVariableExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            case BoundConversionExpression { IsExplicit: true }:
                _diagnostics.Report(assignment.Target, "E2900", "variable typecast");     // LANG-290, C-057
                return new BoundAssignmentStatement(assignment, target, value);
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

    /// <summary>LANG-452: control variables of enclosing routines' <c>for</c> loops are read-only here.</summary>
    public void AddReadOnlyVariables(IEnumerable<Symbol> variables) => ForVariables.UnionWith(variables);

    /// <summary>LANG-352: a designator statement must be a call; control intrinsics.</summary>
    private BoundStatement BindExpressionStatement(ExpressionStatementSyntax statement)
    {
        var name = CalledName(statement.Expression);
        var symbol = name is null ? null : _scope.Lookup(name.Identifier.Value as string ?? name.Identifier.Text);
        if (symbol is IntrinsicSymbol intrinsic)
        {
            return BindControlIntrinsic(statement, intrinsic);
        }

        if (symbol is RoutineSymbol)
        {
            var (routine, arguments) = _expressions.BindCall(statement.Expression, name!, (statement.Expression as InvocationExpressionSyntax)?.ArgumentList);
            return routine is null ? new BoundErrorStatement(statement) : new BoundCallStatement(statement, routine, arguments);
        }

        var expression = _expressions.BindExpression(statement.Expression);
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
        var count = arguments.Count;
        var maxArguments = intrinsic.Intrinsic switch
        {
            IntrinsicKind.Halt => 1,
            IntrinsicKind.Exit when _routine?.ReturnType != null => 1,                // LANG-413
            _ => 0,
        };
        if (count > maxArguments)
        {
            _diagnostics.Report(arguments[maxArguments], "E2111", intrinsic.Name);
            return new BoundErrorStatement(statement);
        }

        if (count == 1 && arguments[0].WidthColonToken is { } colon)
        {
            _diagnostics.Report(colon, "E2915");
            return new BoundErrorStatement(statement);
        }

        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.Break or IntrinsicKind.Continue when _loopDepth == 0:
                _diagnostics.Report(statement.Expression, "E2066", intrinsic.Name);
                return new BoundErrorStatement(statement);
            case IntrinsicKind.Break:
                return new BoundBreakStatement(statement);
            case IntrinsicKind.Continue:
                return new BoundContinueStatement(statement);
            case IntrinsicKind.Exit when count == 1:
                return new BoundExitStatement(statement,
                    _expressions.ConvertForAssignment(_expressions.BindExpression(arguments[0].Expression), _routine!.ReturnType!, arguments[0].Expression));
            case IntrinsicKind.Exit:
                return new BoundExitStatement(statement, value: null);
            default:
                var code = count == 1
                    ? _expressions.ConvertForAssignment(_expressions.BindExpression(arguments[0].Expression), _types.Integer, arguments[0].Expression)
                    : null;
                return new BoundHaltStatement(statement, code);
        }
    }
}
