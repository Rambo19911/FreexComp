using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §6a, inline-variables.md §12 (C-385, CMP-1414): inlay hints for inferred inline variables.</summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1414")]
public sealed class InlayHintTests
{
    private static System.Collections.Immutable.ImmutableArray<InlayHint> Hints(TextSpan range) =>
        InlayHintService.GetInlayHints(InlineInferenceFixture.Open(), range, TestContext.Current.CancellationToken);

    private static TextSpan Whole => new(0, InlineInferenceFixture.Text.Length);

    /// <summary>The hint expected just after the identifier that ends <paramref name="declaration"/>.</summary>
    private static InlayHint After(string declaration, string label) =>
        new(InlineInferenceFixture.Offset(declaration) + declaration.Length, label, InlayHintKind.Type);

    [Fact]
    public void InferredInlineVariables_OneHintEach()
    {
        Assert.Equal(
            [
                After("var N", ": Integer"),
                After("var L", ": TBox<Integer>"),
                After("for var I", ": Integer"),
                After("for var Q", ": UInt64"),
                After("for var B", ": Byte"),
            ],
            Hints(Whole));
    }

    [Fact]
    public void WrittenType_NoHint()
    {
        var hints = Hints(Whole);

        Assert.DoesNotContain(hints, h => h.Position == InlineInferenceFixture.Offset("var W") + "var W".Length);
        Assert.DoesNotContain(hints, h => h.Position == InlineInferenceFixture.Offset("for var J") + "for var J".Length);
        Assert.DoesNotContain(hints, h => h.Position == InlineInferenceFixture.Offset("for var C") + "for var C".Length);
        Assert.DoesNotContain(hints, h => h.Position == InlineInferenceFixture.Offset("const K") + "const K".Length);   // an inline constant
    }

    [Fact]
    public void RejectedDeclaration_NoHint()
    {
        var start = InlineInferenceFixture.Offset("procedure Rejected");
        var end = InlineInferenceFixture.Offset("X := A;");
        Assert.Contains(InlineInferenceFixture.Open().SemanticModel.GetDiagnostics(ct: TestContext.Current.CancellationToken), d => d.Code == "E2075");

        Assert.Empty(Hints(TextSpan.FromBounds(start, end)));
    }

    [Fact]
    public void OnlyInsideTheRange()
    {
        var identifier = InlineInferenceFixture.Offset("var L") + "var ".Length;

        Assert.Equal([After("var L", ": TBox<Integer>")], Hints(new TextSpan(identifier, 1)));
        Assert.Equal([After("var L", ": TBox<Integer>"), After("for var I", ": Integer")],
            Hints(TextSpan.FromBounds(identifier, InlineInferenceFixture.Offset("for var I") + "for var ".Length + 1)));
        Assert.Empty(Hints(TextSpan.FromBounds(identifier + 1, InlineInferenceFixture.Offset("for var I") + "for var ".Length)));   // starts must be inside
        Assert.Empty(Hints(new TextSpan(identifier, 0)));
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => InlayHintService.GetInlayHints(InlineInferenceFixture.Open(), Whole, source.Token));
    }
}
