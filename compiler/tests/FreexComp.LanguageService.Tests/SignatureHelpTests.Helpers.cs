namespace FreexComp.LanguageService.Tests;

// CMP-1494 (C-446; phase8/helpers-completion.md §9, §15.9): signature help at calls of helper methods.
public sealed partial class SignatureHelpTests
{
    /// <summary>
    /// CMP-1494: at a call of a helper method the whole overload set of LANG-992 step 3 is listed, helper methods first — a helper
    /// method and the helped class's method of one name on a value of a descendant, a literal receiver, a type-qualified static call.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1494")]
    public void HelperMethod_ListsTheWholeOverloadSet()
    {
        static string[] InOrder(string body)
        {
            var parsed = MarkupTestFile.Parse(HelpersFixture.Program(body));
            var doc = MarkupTestFile.Open("P.pas", ("P.pas", parsed.Text), HelpersFixture.UnitFile);
            var help = SignatureHelpService.GetSignatureHelp(doc, parsed.Caret, TestContext.Current.CancellationToken);
            Assert.NotNull(help);
            return [.. help.Signatures.Select(s => s.Label)];
        }

        Assert.Equal(["procedure TCounterHelper.Put(const S: string)", "procedure TCounter.Put(A: Integer)"], InOrder("  D.Put($$);"));
        Assert.Equal(["function TIntH.Plus(A: Integer): Integer", "function TIntH.Plus(A: Integer; B: Integer): Integer"], InOrder("  X := 5.Plus($$);"));
        Assert.Equal(["function TIntH.Plus(A: Integer): Integer", "function TIntH.Plus(A: Integer; B: Integer): Integer"], InOrder("  X := K.Plus(1, $$);"));
        var join = Assert.Single(InOrder("  S := string.Join($$);"));
        Assert.StartsWith("class function TStrH.Join(const A: string; const B: string): string", join, StringComparison.Ordinal);
        Assert.Equal(["procedure TCounterHelper.Put(const S: string)", "procedure TCounter.Put(A: Integer)"], InOrder("  with D do Put($$);"));
    }
}
