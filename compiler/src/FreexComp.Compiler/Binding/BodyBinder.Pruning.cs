using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Binding;

// LANG-977 (C-417): type-query conditions — the statement an `if` or `case` on a type query does not take is pruned.
internal sealed partial class BodyBinder
{
    /// <summary>
    /// Rule 3: this body belongs to a generic instance (a method of an instance type, a generic method instance, or a routine or
    /// anonymous method nested in one), so a pruned statement is not bound. False for a body that is not generic (rule 4).
    /// </summary>
    private readonly bool _inInstance;

    /// <summary>Rule 5: the labels that statements pruned from this body place (they count as defined, E2073).</summary>
    private HashSet<string>? _prunedLabels;

    private static bool InGenericInstance(RoutineSymbol? routine)
    {
        for (var r = routine; r is not null; r = ClosureInfo.LexicalParent(r))
        {
            if (GenericUses.InstanceOf(r) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The definition: <paramref name="condition"/> (the condition of an <c>if</c>, the selector of a <c>case</c>) is a constant
    /// expression and the binder met a type query, or a constant whose declared value depends on one, since
    /// <paramref name="queriesBefore"/> was read — its syntax contains one. In the open instance the language service binds nothing is
    /// a type-query condition (LANG-976: nothing is pruned there, both parts keep their code insight).
    /// </summary>
    private bool IsTypeQueryCondition(BoundExpression condition, int queriesBefore) =>
        condition is { HasErrors: false, ConstantValue: not null } && _expressions.TypeQueryUses != queriesBefore && !DeclarationBinder.IsOpenBinding;

    /// <summary>Rule 1 for <c>case</c>: the label (a constant or a range of constants; one with errors contains nothing) contains <paramref name="value"/>.</summary>
    private static bool LabelContains(BoundCaseLabel label, BigInteger value)
    {
        static BigInteger? Bound(BoundExpression? bound) =>
            bound is { HasErrors: false, ConstantValue: { Value: BigInteger or bool } constant } ? Ordinal(constant) : null;

        return Bound(label.Low) is { } low && (label.High is null ? low == value : Bound(label.High) is { } high && low <= value && value <= high);
    }

    /// <summary>Collects what a type-query condition does not take.</summary>
    private sealed class Pruning
    {
        public ImmutableArray<BoundStatement>.Builder Checked { get; } = ImmutableArray.CreateBuilder<BoundStatement>();

        public bool HasUnbound { get; set; }
    }

    /// <summary>
    /// A statement the condition does not take (<paramref name="list"/>: the <c>else</c> list of a <c>case</c>). In a generic instance
    /// body it is not bound — no diagnostic, no instantiation, no declaration, no capture (rule 3). Elsewhere it is bound and checked
    /// like every statement (rule 4) inside a region no <c>goto</c> enters from outside: it is never compiled, so a label in it does
    /// not exist for the kept code (LANG-380, E2072). Either way its identifiers count as used for the unused-symbol hints (rule 5).
    /// </summary>
    private void Prune(Pruning pruning, SyntaxNode at, StatementSyntax? statement, SeparatedSyntaxList<StatementSyntax>? list = null)
    {
        IEnumerable<SyntaxNode> nodes = statement is not null ? [statement] : list!.Value;
        var names = new List<string>();
        foreach (var node in nodes)
        {
            foreach (var token in node.DescendantTokens())
            {
                if (token.Kind == SyntaxKind.IdentifierToken && !token.IsMissing)
                {
                    names.Add(token.Value as string ?? token.Text);
                }
            }

            if (_inInstance)
            {
                foreach (var labelled in node.DescendantNodes().Prepend(node).OfType<LabeledStatementSyntax>())
                {
                    if (LabelName(labelled.Label) is { } label)
                    {
                        (_prunedLabels ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(label);
                    }
                }
            }
        }

        if (names.Count > 0)
        {
            for (var routine = _routine; routine is not null; routine = ClosureInfo.LexicalParent(routine))
            {
                routine.NotePrunedNames(names);                                 // also for the bodies this one is nested in
            }
        }

        if (_inInstance)
        {
            pruning.HasUnbound = true;
            return;
        }

        pruning.Checked.Add(InRegion(isTryPart: false, () => statement is not null ? BindStatement(statement) : BindBlock(at, list!.Value)));
    }

    /// <summary>
    /// Rule 1 for <c>if</c>: <c>True</c> keeps the <c>then</c> statement and prunes the <c>else</c> statement, <c>False</c> the reverse;
    /// without an <c>else</c> part a <c>False</c> condition leaves nothing. The parts are visited in source order.
    /// </summary>
    private BoundPrunedStatement BindPrunedIf(IfStatementSyntax syntax, BoundExpression condition, bool taken)
    {
        var pruning = new Pruning();
        BoundStatement? kept = null;
        if (taken)
        {
            kept = BindStatement(syntax.Statement);
        }
        else
        {
            Prune(pruning, syntax, syntax.Statement);
        }

        if (syntax.ElseStatement is { } otherwise)
        {
            if (taken)
            {
                Prune(pruning, syntax, otherwise);
            }
            else
            {
                kept = BindStatement(otherwise);
            }
        }

        return new BoundPrunedStatement(syntax, condition, [], kept, pruning.Checked.ToImmutable(), pruning.HasUnbound);
    }
}
