using System.Collections.Immutable;
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
    private readonly DiagnosticBag _diagnostics;
    private readonly ExpressionBinder _expressions;

    public BodyBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
        _expressions = new ExpressionBinder(types, scope, diagnostics);
    }

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
            case CompoundStatementSyntax compound:
                return BindBlock(compound, compound.Statements);
            case AssignmentStatementSyntax assignment:
                return BindAssignment(assignment);
            case ExpressionStatementSyntax expression:
                return BindExpressionStatement(expression);
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
        var target = _expressions.BindExpression(assignment.Target);
        var value = _expressions.BindExpression(assignment.Value);
        if (target.HasErrors)
        {
            return new BoundAssignmentStatement(assignment, target, value);
        }

        switch (target)
        {
            case BoundVariableExpression:
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

    /// <summary>LANG-352: a designator statement must be a call (calls are bound from C-027 on).</summary>
    private BoundExpressionStatement BindExpressionStatement(ExpressionStatementSyntax statement)
    {
        var expression = _expressions.BindExpression(statement.Expression);
        if (!expression.HasErrors)
        {
            _diagnostics.Report(statement.Expression, "E2060", expression.Type.Name);
        }

        return new BoundExpressionStatement(statement, expression);
    }
}
