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

    /// <summary>C-416 (CMP-1476): signature help inside a type query shows the signature text of LANG-972.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    [InlineData("IsManagedType($$string)", "function IsManagedType(T: Type): Boolean")]
    [InlineData("HasWeakRef($$Integer)", "function HasWeakRef(T: Type): Boolean")]
    [InlineData("GetTypeKind($$Integer) = tkInteger", "function GetTypeKind(T: Type): TTypeKind")]
    [InlineData("IsConstValue($$1)", "function IsConstValue(const X): Boolean")]
    public void TypeQueryIntrinsic_TheInformalSignature(string call, string label)
    {
        var (help, _) = Help("var B: Boolean;\nbegin\n  B := " + call + ";\nend.");

        Assert.Equal(label, Assert.Single(help!.Signatures).Label);
        Assert.Equal(0, help.ActiveParameter);
    }

    /// <summary>C-418: generic methods whose type arguments reach the call through <c>var</c>, open-array, dynamic-array and instance-typed parameters.</summary>
    internal const string InferenceProgram = """
        program P;
        type
          TBox<T> = class
            Value: T;
          end;
          TUtil = class
          public
            class procedure Swap<T>(var A, B: T);
            class function IndexOf<T>(const Values: array of T; const Item: T): Integer;
            class function First<T>(const Values: TArray<T>; const Fallback: T): T;
            class function Unbox<T>(const Box: TBox<T>; const Fallback: T): T;
          end;
        class procedure TUtil.Swap<T>(var A, B: T); begin end;
        class function TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer; begin Result := 0 end;
        class function TUtil.First<T>(const Values: TArray<T>; const Fallback: T): T; begin Result := Fallback end;
        class function TUtil.Unbox<T>(const Box: TBox<T>; const Fallback: T): T; begin Result := Fallback end;
        var
          N, M: Integer;
          L: Int64;
          S: string;
          Names: array[0..2] of string;
          Reals: TArray<Double>;
          Box: TBox<string>;
        begin
          MAIN_BODY
        end.
        """;

    /// <summary>
    /// CMP-1477 (1), CMP-1478 (C-418): the semantic model infers as the compiler does (LANG-979) — while the call is typed, the
    /// arguments so far give the instance through a <c>var</c> parameter, an open array (a variable or a bracket constructor), a
    /// dynamic array and an instance of a generic class; a finished call shows the instance it binds to; arguments that disagree
    /// leave the method generic, as the compiler reports them (E2711).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1477")]
    [Trait("Req", "CMP-1478")]
    public void GenericMethodCall_InferredThroughVarAndOpenArray()
    {
        static SignatureHelp? Help(string main) => HelpIn(InferenceProgram.Replace("MAIN_BODY", main, StringComparison.Ordinal));

        var swap = Help("TUtil.Swap(N, $$);");
        Assert.Equal(["class procedure TUtil.Swap<Integer>(var A: Integer; var B: Integer)"], Labels(swap));
        Assert.Equal("var B: Integer", Active(swap!));
        Assert.Equal(["class procedure TUtil.Swap<Integer>(var A: Integer; var B: Integer)"], Labels(Help("TUtil.Swap(N, $$M);")));
        Assert.Equal(["class procedure TUtil.Swap<string>(var A: string; var B: string)"], Labels(Help("TUtil.Swap(Names[0], $$S);")));

        var indexOf = Help("N := TUtil.IndexOf(Names, $$);");
        Assert.Equal(["class function TUtil.IndexOf<string>(const Values: array of string; const Item: string): Integer"], Labels(indexOf));
        Assert.Equal("const Item: string", Active(indexOf!));
        Assert.Equal(
            ["class function TUtil.IndexOf<Integer>(const Values: array of Integer; const Item: Integer): Integer"],
            Labels(Help("N := TUtil.IndexOf([N, M], $$);")));
        Assert.Equal(
            ["class function TUtil.IndexOf<Integer>(const Values: array of Integer; const Item: Integer): Integer"],
            Labels(Help("N := TUtil.IndexOf([N, M], $$7);")));

        Assert.Equal(
            ["class function TUtil.First<Double>(const Values: TArray<Double>; const Fallback: Double): Double"],
            Labels(Help("TUtil.First(Reals, $$);")));
        Assert.Equal(
            ["class function TUtil.Unbox<string>(const Box: TBox<string>; const Fallback: string): string"],
            Labels(Help("S := TUtil.Unbox(Box, $$S);")));

        Assert.Equal(["class procedure TUtil.Swap<T>(var A: T; var B: T)"], Labels(Help("TUtil.Swap($$);")));
        Assert.Equal(["class procedure TUtil.Swap<T>(var A: T; var B: T)"], Labels(Help("TUtil.Swap(N, L$$);")));        // Integer and Int64
    }

    /// <summary>
    /// CMP-1477 (1), CMP-1478 (C-426): a literal argument adapts in the semantic model as in the compiler (LANG-979) — the signature
    /// shown is the instance the typed argument gives, with the literal's parameter in that type; literals alone give their natural
    /// type; two typed arguments that disagree leave the method generic.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1477")]
    [Trait("Req", "CMP-1478")]
    public void GenericMethodCall_LiteralArgumentAdapts()
    {
        static SignatureHelp? Help(string main) => HelpIn(InferenceProgram.Replace("MAIN_BODY", main, StringComparison.Ordinal));

        var indexOf = Help("N := TUtil.IndexOf(Names, 'x'$$);");
        Assert.Equal(["class function TUtil.IndexOf<string>(const Values: array of string; const Item: string): Integer"], Labels(indexOf));
        Assert.Equal("const Item: string", Active(indexOf!));
        Assert.Equal(
            ["class function TUtil.First<Double>(const Values: TArray<Double>; const Fallback: Double): Double"],
            Labels(Help("TUtil.First(Reals, 1$$);")));
        Assert.Equal(
            ["class function TUtil.Unbox<string>(const Box: TBox<string>; const Fallback: string): string"],
            Labels(Help("S := TUtil.Unbox(Box, $$'x');")));
        Assert.Equal(                                                           // a constructor of literals adapts to the typed item
            ["class function TUtil.IndexOf<Int64>(const Values: array of Int64; const Item: Int64): Integer"],
            Labels(Help("N := TUtil.IndexOf([1, 2], L$$);")));

        Assert.Equal(                                                           // literals alone: their natural type
            ["class function TUtil.IndexOf<Integer>(const Values: array of Integer; const Item: Integer): Integer"],
            Labels(Help("N := TUtil.IndexOf([1, 2], 3$$);")));
        Assert.Equal(                                                           // two typed arguments that disagree
            ["class function TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer"],
            Labels(Help("N := TUtil.IndexOf(Names, N$$);")));
    }

    /// <summary>A class whose method name <c>Show</c> has a non-generic and two generic methods (LANG-980).</summary>
    internal const string MixedProgram = """
        program P;
        type
          TOut = class
          public
            class procedure Show(I: Integer); overload;
            class procedure Show<T>(const V: T); overload;
            class procedure Show<K, V>(const A: K; const B: V); overload;
          end;
        class procedure TOut.Show(I: Integer); begin end;
        class procedure TOut.Show<T>(const V: T); begin end;
        class procedure TOut.Show<K, V>(const A: K; const B: V); begin end;
        var
          N: Integer;
          B: Byte;
          S: string;
        begin
          MAIN_BODY
        end.
        """;

    /// <summary>
    /// CMP-1478 (C-419): signature help on a call lists the non-generic and the generic methods of the name together — a generic one
    /// with its type-parameter list, or with the type arguments the arguments typed so far infer; the active signature is the
    /// candidate LANG-980 chooses (the non-generic method only as an equally good match), else the first that takes the arguments.
    /// A type-argument list names generic methods only.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1478")]
    [Trait("Req", "LANG-980")]
    public void GenericAndNonGenericOverloads_AllListed()
    {
        static SignatureHelp Help(string main) => HelpIn(MixedProgram.Replace("MAIN_BODY", main, StringComparison.Ordinal))!;
        static string ActiveSignature(SignatureHelp help) => help.Signatures[help.ActiveSignature].Label;
        const string plain = "class procedure TOut.Show(I: Integer)";
        const string openPair = "class procedure TOut.Show<K, V>(const A: K; const B: V)";

        var empty = Help("TOut.Show($$);");
        Assert.Equal([plain, openPair, "class procedure TOut.Show<T>(const V: T)"], Labels(empty));
        Assert.Equal(plain, ActiveSignature(empty));

        var integer = Help("TOut.Show(N$$);");
        Assert.Equal([plain, "class procedure TOut.Show<Integer>(const V: Integer)", openPair], Labels(integer));
        Assert.Equal(plain, ActiveSignature(integer));                                       // both cost the same: the non-generic one
        Assert.Equal(plain, ActiveSignature(Help("TOut.Show(5$$);")));

        var text = Help("TOut.Show(S$$);");
        Assert.Equal([plain, openPair, "class procedure TOut.Show<string>(const V: string)"], Labels(text));
        Assert.Equal("class procedure TOut.Show<string>(const V: string)", ActiveSignature(text));

        var narrow = Help("TOut.Show(B$$);");
        Assert.Equal([plain, "class procedure TOut.Show<Byte>(const V: Byte)", openPair], Labels(narrow));
        Assert.Equal("class procedure TOut.Show<Byte>(const V: Byte)", ActiveSignature(narrow));   // exact against the widening to Integer

        var pair = Help("TOut.Show(N, $$S);");
        Assert.Equal(
            [plain, "class procedure TOut.Show<Integer, string>(const A: Integer; const B: string)", "class procedure TOut.Show<Integer>(const V: Integer)"],
            Labels(pair));
        Assert.Equal("class procedure TOut.Show<Integer, string>(const A: Integer; const B: string)", ActiveSignature(pair));
        Assert.Equal("const B: string", Active(pair));

        Assert.Equal(["class procedure TOut.Show<Integer>(const V: Integer)"], Labels(Help("TOut.Show<Integer>($$N);")));
        Assert.Equal(["class procedure TOut.Show<Byte, string>(const A: Byte; const B: string)"], Labels(Help("TOut.Show<Byte, string>(B, $$S);")));
    }

    /// <summary>
    /// CMP-1473 (C-415): the scope at a caret inside an anonymous method is that method's, so a routine declared in it is a
    /// candidate of a call written there (it is named after the routine around the anonymous method), as an outer routine is.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1473")]
    public void AnonymousBody_RoutineDeclaredInTheAnonymousMethod()
    {
        var markup = MarkupTestFile.Parse("""
            program P;
            type
              TProc = reference to procedure;
            function Twice(N: Integer): Integer;
            begin
              Result := N * 2;
            end;
            procedure Work(Limit: Integer);
            var
              Run: TProc;
              Total: Integer;
            begin
              Run := procedure
                function Bump(Step: Integer; Scale: Integer): Integer;
                begin
                  Result := Step * Scale;
                end;
                begin
                  Total := Bump(Limit, $$Twice(2));
                end;
              Run();
            end;
            begin
            end.
            """);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", markup.Text));

        var help = SignatureHelpService.GetSignatureHelp(doc, markup.Caret, TestContext.Current.CancellationToken);
        Assert.NotNull(help);
        Assert.Equal("function P.Work.Bump(Step: Integer; Scale: Integer): Integer", Assert.Single(help.Signatures).Label);
        Assert.Equal("Scale: Integer", Active(help));

        var inner = SignatureHelpService.GetSignatureHelp(doc, markup.Text.IndexOf("Twice(2)", StringComparison.Ordinal) + "Twice(".Length, TestContext.Current.CancellationToken);
        Assert.Equal("function P.Twice(N: Integer): Integer", Assert.Single(inner!.Signatures).Label);
    }
}
