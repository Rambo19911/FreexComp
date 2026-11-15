namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §5 (CMP-728, C-135): overloads, active parameter with nested calls, intrinsics, array property.</summary>
[Trait("Phase", "3")]
public sealed class SignatureHelpTests
{
    private const string Declarations = """
        program P;
        type
          TList = class
            function GetItem(I: Integer; const Name: string): Integer;
            property Items[I: Integer; const Name: string]: Integer read GetItem; default;
          end;
        /// Integer flavour.
        procedure Over(A: Integer); overload;
        begin end;
        procedure Over(S: string; B: Integer); overload;
        begin end;
        function Inner(X, Y: Integer): Integer;
        begin Result := X + Y end;
        function TList.GetItem(I: Integer; const Name: string): Integer;
        begin Result := I end;
        var L: TList;
        """;

    private static (SignatureHelp? Help, string Text) Help(string body)
    {
        var markup = MarkupTestFile.Parse(Declarations + "\n" + body);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", markup.Text));
        return (SignatureHelpService.GetSignatureHelp(doc, markup.Caret, TestContext.Current.CancellationToken), markup.Text);
    }

    private static string Active(SignatureHelp help)
    {
        var signature = help.Signatures[help.ActiveSignature];
        var span = signature.Parameters[help.ActiveParameter].LabelSpanInSignature;
        return signature.Label.Substring(span.Start, span.Length);
    }

    [Fact]
    [Trait("Req", "CMP-728")]
    public void Overloads_AllCandidatesListed_BestByArgumentsTyped()
    {
        var (help, _) = Help("begin\n  Over('x', $$1);\nend.");

        Assert.NotNull(help);
        Assert.Equal(["procedure P.Over(A: Integer)", "procedure P.Over(S: string; B: Integer)"], help.Signatures.Select(s => s.Label).Order(StringComparer.Ordinal));
        Assert.Equal("procedure P.Over(S: string; B: Integer)", help.Signatures[help.ActiveSignature].Label);
        Assert.Equal(1, help.ActiveParameter);
        Assert.Equal("B: Integer", Active(help));
        Assert.Equal("Integer flavour.", help.Signatures.Single(s => s.Label == "procedure P.Over(A: Integer)").Documentation);
    }

    [Fact]
    [Trait("Req", "CMP-728")]
    public void Overloads_FirstApplicableWithOneArgument()
    {
        var (help, _) = Help("begin\n  Over(4$$);\nend.");

        Assert.Equal("procedure P.Over(A: Integer)", help!.Signatures[help.ActiveSignature].Label);
        Assert.Equal("A: Integer", Active(help));
    }

    [Fact]
    [Trait("Req", "CMP-728")]
    public void NestedCall_TheInnermostInvocationAndItsCommas()
    {
        var (help, text) = Help("begin\n  Over('x', Inner(1, $$2));\nend.");

        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("function P.Inner(X: Integer; Y: Integer): Integer", signature.Label);
        Assert.Equal(1, help.ActiveParameter);
        Assert.Equal("Y: Integer", Active(help));
        Assert.Equal("1, 2", text.Substring(help.ApplicableSpan.Start, help.ApplicableSpan.Length));

        var (outer, _) = Help("begin\n  Over('x', Inner(1, 2)$$);\nend.");
        Assert.Equal(1, outer!.ActiveParameter);
        Assert.Equal(2, outer.Signatures.Length);
    }

    [Fact]
    [Trait("Req", "CMP-728")]
    public void Intrinsic_TheInformalSignature()
    {
        var (help, _) = Help("var S: string;\nbegin\n  S := Copy('abc', 1, $$2);\nend.");

        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("function Copy(S: string; Index[, Count]: Integer): string", signature.Label);
        Assert.Equal(2, help.ActiveParameter);
        Assert.Equal(3, signature.Parameters.Length);
        Assert.StartsWith("Count", Active(help), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-728")]
    public void ArrayProperty_InsideTheBrackets()
    {
        var (help, _) = Help("begin\n  Writeln(L[1, $$'a']);\nend.");

        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("property TList.Items[I: Integer; const Name: string]: Integer read", signature.Label);
        Assert.Equal("const Name: string", Active(help));

        var (named, _) = Help("begin\n  Writeln(L.Items[$$1, 'a']);\nend.");
        Assert.Equal(0, named!.ActiveParameter);
        Assert.Equal("property TList.Items[I: Integer; const Name: string]: Integer read", Assert.Single(named.Signatures).Label);
    }

    [Theory]
    [Trait("Req", "CMP-728")]
    [InlineData("begin\n  $$Over(1);\nend.")]
    [InlineData("begin\n  Over(1);$$\nend.")]
    [InlineData("begin\n  // Over($$1)\nend.")]
    public void OutsideTheParentheses_Null(string body)
    {
        Assert.Null(Help(body).Help);
    }
}
