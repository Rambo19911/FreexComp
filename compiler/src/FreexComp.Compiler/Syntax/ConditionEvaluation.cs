using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// The binder callback of LANG-862: evaluates a <c>{$IF}</c> / <c>{$ELSEIF}</c> condition in the scope at the
/// directive. Without one (pure syntax operations) every such condition counts as True.
/// </summary>
internal interface IConditionEvaluator
{
    /// <summary>The condition's value; false after an error (reported through <see cref="ConditionRequest.Report"/>).</summary>
    bool Evaluate(ConditionRequest request);
}

/// <param name="Condition">The parsed directive argument (its tokens carry the directive file's positions).</param>
/// <param name="PrecedingTokens">The active tokens of the unit before the directive, includes expanded.</param>
/// <param name="IsDefined">The conditional symbols defined at the directive (<c>Defined(NAME)</c>).</param>
internal sealed record ConditionRequest(
    ExpressionSyntax Condition,
    IReadOnlyList<SyntaxToken> PrecedingTokens,
    Func<string, bool> IsDefined,
    Action<Diagnostic> Report);
