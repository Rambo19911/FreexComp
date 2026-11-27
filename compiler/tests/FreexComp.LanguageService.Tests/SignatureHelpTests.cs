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

    /// <summary>Signature help at the <c>$$</c> of a whole program <paramref name="markup"/>.</summary>
    private static SignatureHelp? HelpIn(string markup)
    {
        var parsed = MarkupTestFile.Parse(markup);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", parsed.Text));
        return SignatureHelpService.GetSignatureHelp(doc, parsed.Caret, TestContext.Current.CancellationToken);
    }

    private const string Classes = """
        program P;
        type
          TBase = class
          strict private
            constructor Create(P: Pointer); overload;
          public
            constructor Create(A: Integer); overload;
            constructor Create(const S: string); overload;
            procedure Run(A: Integer); virtual;
          end;
          TFoo = class(TBase)
          public
            class constructor Create;
            constructor Create(A, B: Integer); overload;
            procedure Run(A: Integer); override;
          end;
          TBar = class(TBase)
          end;
          TBaz = class(TBase)
          public
            constructor Create(X: Double);
          end;
          TUtil = class
          public
            class function Pick<T>(A: T; B: Integer): T;
            class procedure Show<T>(A: T);
          end;
        constructor TBase.Create(P: Pointer); begin end;
        constructor TBase.Create(A: Integer); begin end;
        constructor TBaz.Create(X: Double); begin end;
        constructor TBase.Create(const S: string); begin end;
        procedure TBase.Run(A: Integer); begin end;
        class constructor TFoo.Create; begin end;
        constructor TFoo.Create(A, B: Integer); begin CTOR_BODY end;
        procedure TFoo.Run(A: Integer); begin RUN_BODY end;
        class function TUtil.Pick<T>(A: T; B: Integer): T; begin Result := A end;
        class procedure TUtil.Show<T>(A: T); begin end;
        var F: TBase; N: Integer;
        begin
          MAIN_BODY
        end.
        """;

    private static SignatureHelp? HelpInClasses(string main = "", string ctor = "", string run = "") =>
        HelpIn(Classes.Replace("MAIN_BODY", main, StringComparison.Ordinal).Replace("CTOR_BODY", ctor, StringComparison.Ordinal)
            .Replace("RUN_BODY", run, StringComparison.Ordinal));

    private static string[] Labels(SignatureHelp? help)
    {
        Assert.NotNull(help);
        return [.. help.Signatures.Select(s => s.Label).Order(StringComparer.Ordinal)];
    }

    /// <summary>CMP-729 (a): <c>TFoo.Create(</c> lists the accessible constructors named Create of the class and its ancestors (LANG-441), never the class constructor.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-729")]
    public void ConstructorCall_ListsTheConstructorOverloads()
    {
        var foo = HelpInClasses(main: "F := TFoo.Create($$1, 2);");
        Assert.Equal(
            ["constructor TBase.Create(A: Integer)", "constructor TBase.Create(const S: string)", "constructor TFoo.Create(A: Integer; B: Integer)",
             "constructor TObject.Create"],
            Labels(foo));
        Assert.Equal("constructor TFoo.Create(A: Integer; B: Integer)", foo!.Signatures[foo.ActiveSignature].Label);

        var bar = HelpInClasses(main: "F := TBar.Create('x$$');");
        Assert.Equal(["constructor TBase.Create(A: Integer)", "constructor TBase.Create(const S: string)", "constructor TObject.Create"], Labels(bar));
        Assert.Equal("constructor TBase.Create(const S: string)", bar!.Signatures[bar.ActiveSignature].Label);

        // without overload the class's own constructor hides the ancestors' (LANG-441)
        Assert.Equal(["constructor TBaz.Create(X: Double)"], Labels(HelpInClasses(main: "F := TBaz.Create($$);")));
    }

    /// <summary>CMP-729 (b): <c>inherited Name(</c> lists the inherited method(s) the call binds to; a bare <c>inherited</c> gives no help.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-729")]
    public void InheritedCall_TheAncestorMethod()
    {
        var run = HelpInClasses(run: "inherited Run($$A);");
        Assert.Equal(["procedure TBase.Run(A: Integer); virtual"], Labels(run));
        Assert.Equal(0, run!.ActiveParameter);

        var create = HelpInClasses(ctor: "inherited Create($$'x');");
        Assert.Equal(["constructor TBase.Create(A: Integer)", "constructor TBase.Create(const S: string)", "constructor TObject.Create"], Labels(create));
        Assert.Equal("constructor TBase.Create(const S: string)", create!.Signatures[create.ActiveSignature].Label);

        Assert.Null(HelpInClasses(run: "inherited;$$"));
    }

    /// <summary>CMP-729 (c), (d): explicit type arguments are substituted; an inferred call is generic until the arguments infer T.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-729")]
    public void GenericMethodCall_ExplicitAndInferred()
    {
        Assert.Equal(["class procedure TUtil.Show<Integer>(A: Integer)"], Labels(HelpInClasses(main: "TUtil.Show<Integer>($$N);")));
        Assert.Equal(["class procedure TUtil.Show<Integer>(A: Integer)"], Labels(HelpInClasses(main: "TUtil.Show<Integer>($$);")));
        Assert.Equal(["class function TUtil.Pick<string>(A: string; B: Integer): string"], Labels(HelpInClasses(main: "TUtil.Pick<string>('a', $$1);")));

        Assert.Equal(["class procedure TUtil.Show<T>(A: T)"], Labels(HelpInClasses(main: "TUtil.Show($$);")));
        Assert.Equal(["class procedure TUtil.Show<Integer>(A: Integer)"], Labels(HelpInClasses(main: "TUtil.Show(N$$);")));
        Assert.Equal(["class function TUtil.Pick<T>(A: T; B: Integer): T"], Labels(HelpInClasses(main: "TUtil.Pick($$);")));
        var inferred = HelpInClasses(main: "TUtil.Pick(N, $$);");
        Assert.Equal(["class function TUtil.Pick<Integer>(A: Integer; B: Integer): Integer"], Labels(inferred));
        Assert.Equal("B: Integer", Active(inferred!));
    }

    /// <summary>CMP-733 (C-393): the signature shows the summary only; each parameter the text of its <c>param</c> element (name case-insensitive).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-733")]
    public void DocComment_SummaryAndParameterDocumentation()
    {
        var help = HelpIn("""
            program P;
            /// <summary>Returns the greeting for <paramref name="AName"/>.</summary>
            /// <param name="aname">The name; <c>''</c> greets the world.</param>
            /// <returns>The text, never empty.</returns>
            /// <remarks>Pure.</remarks>
            function Greet(const AName: string; Times: Integer): string;
            begin
              Result := AName;
            end;
            begin
              Greet('x', $$1);
            end.
            """);

        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("Returns the greeting for AName.", signature.Documentation);
        Assert.Equal(["The name; '' greets the world.", null], signature.Parameters.Select(p => p.Documentation));
    }
}
