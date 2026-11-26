namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §4 (CMP-720…724): one case per row of CMP-721 (C-134).</summary>
[Trait("Phase", "3")]
public sealed class CompletionTests
{
    private const string Unit1 = """
        unit U1;
        interface
        type
          TBase = class
          private
            FHidden: Integer;
          protected
            procedure Guarded;
          public
            Shown: Integer;
            procedure Run; virtual;
            procedure Stop; virtual;
            class function Make: TBase;
          end;
          TBaseHelper = class helper for TBase
            procedure Helped;
          end;
          TColor = (Red, Green);
        const UnitConst = 1;
        procedure Exported;
        implementation
        procedure TBase.Guarded; begin end;
        procedure TBase.Run; begin end;
        procedure TBase.Stop; begin end;
        class function TBase.Make: TBase; begin Result := nil end;
        procedure TBaseHelper.Helped; begin end;
        procedure Exported; begin end;
        end.
        """;

    private static (CompletionList List, string Text) Complete(string markup, char? typed = null, params (string Name, string Text)[] extra)
    {
        markup = markup.ReplaceLineEndings("\n");
        var caret = markup.LastIndexOf("$$", StringComparison.Ordinal);       // the last one: "{$$$}" is "{$" + caret
        var text = markup.Remove(caret, 2);
        var workspace = new Workspace(TestFiles.Files([("P.pas", text), ("U1.pas", Unit1.ReplaceLineEndings("\n")), .. extra]));
        workspace.SetProject(TestFiles.Options());
        var trigger = typed is { } c ? new CompletionTrigger(CompletionTriggerKind.Character, c) : new CompletionTrigger(CompletionTriggerKind.Invoked, '\0');
        return (CompletionService.GetCompletions(workspace.GetDocument(TestFiles.PathOf("P.pas")), caret, trigger, TestContext.Current.CancellationToken), text);
    }

    private static string[] Labels(string markup, char? typed = null, params (string Name, string Text)[] extra) =>
        [.. Complete(markup, typed, extra).List.Items.Select(i => i.Label)];

    [Theory]
    [Trait("Req", "CMP-721")]
    [InlineData("program P;\n// comment $$\nbegin end.")]
    [InlineData("program P;\n{ block $$ }\nbegin end.")]
    [InlineData("program P;\nbegin Writeln('str$$ing') end.")]
    [InlineData("program P;\n{$IFDEF NEVER}\nfoo $$\n{$ENDIF}\nbegin end.")]
    public void InCommentStringOrDisabledText_None(string markup)
    {
        Assert.Empty(Labels(markup));
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterBraceDollar_DirectiveNames()
    {
        var items = Complete("program P;\n{$$$}\nbegin end.", '$').List.Items;

        Assert.Contains(items, i => i.Label == "IFDEF" && i.Kind == CompletionItemKind.Directive);
        Assert.Contains(items, i => i.Label == "REGION");
    }

    [Fact]
    [Trait("Req", "CMP-223")]
    public void DirectiveNamesComeFromSyntaxFacts()
    {
        var items = Complete("program P;\n{$$$}\nbegin end.", '$').List.Items;

        Assert.Equal(Compiler.Syntax.SyntaxFacts.DirectiveNames, items.Select(i => i.Label));
        Assert.Contains("MINSTACKSIZE", Compiler.Syntax.SyntaxFacts.DirectiveNames);   // directives.md §5, all of its table
        Assert.Contains("FORM", Compiler.Syntax.SyntaxFacts.DirectiveNames);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterDot_OnAUnit_TheUnitsInterface()
    {
        var labels = Labels("program P;\nuses U1;\nbegin\n  U1.$$\nend.", '.');

        Assert.Contains("TBase", labels);
        Assert.Contains("UnitConst", labels);
        Assert.Contains("Exported", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterDot_OnAValue_AccessibleMembersInheritedAndHelpers()
    {
        var labels = Labels("program P;\nuses U1;\nvar B: TBase;\nbegin\n  B.$$\nend.", '.');

        Assert.Contains("Shown", labels);
        Assert.Contains("Run", labels);
        Assert.Contains("Free", labels);                 // inherited from TObject
        Assert.Contains("Helped", labels);               // LANG-752: helper members
        Assert.DoesNotContain("FHidden", labels);        // private in another unit
        Assert.DoesNotContain("Guarded", labels);        // protected, outside a descendant
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterDot_OnAClassName_ClassMembersAndConstructors()
    {
        var labels = Labels("program P;\nuses U1;\nvar B: TBase;\nbegin\n  B := TBase.$$\nend.", '.');

        Assert.Contains("Make", labels);
        Assert.Contains("Create", labels);
        Assert.DoesNotContain("Run", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterDot_OnAnEnumType_ItsMembers()
    {
        Assert.Equal(["Green", "Red"], Labels("program P;\nuses U1;\nvar C: TColor;\nbegin\n  C := TColor.$$\nend.", '.').Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void InAUsesClause_UnitsOnTheSearchPathNotListed()
    {
        var labels = Labels("program P;\nuses U1, $$;\nbegin end.", null, ("U2.pas", "unit U2; interface implementation end."));

        Assert.Contains("U2", labels);
        Assert.Contains("System", labels);
        Assert.DoesNotContain("U1", labels);
        Assert.DoesNotContain("P", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void TypePosition_TypesUnitsAndTypeKeywords()
    {
        var labels = Labels("program P;\nuses U1;\nvar X: $$\nbegin end.");

        Assert.Contains("TBase", labels);
        Assert.Contains("Integer", labels);
        Assert.Contains("U1", labels);
        Assert.Contains("array", labels);
        Assert.Contains("record", labels);
        Assert.DoesNotContain("Exported", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterInherited_AncestorMethods()
    {
        var labels = Labels("program P;\nuses U1;\ntype TDerived = class(TBase) procedure Run; override; end;\nprocedure TDerived.Run;\nbegin\n  inherited $$\nend;\nbegin end.");

        Assert.Contains("Run", labels);
        Assert.Contains("Guarded", labels);              // protected: visible from a descendant
        Assert.DoesNotContain("Shown", labels);          // not a method
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void AfterRaise_ClassTypes()
    {
        var labels = Labels("program P;\nuses U1;\nbegin\n  raise $$\nend.");

        Assert.Contains("TBase", labels);
        Assert.Contains("TObject", labels);
        Assert.DoesNotContain("Integer", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void DirectivePosition_AfterARoutineHeading()
    {
        var labels = Labels("unit P;\ninterface\ntype TFoo = class\n  procedure Bar; $$\nend;\nimplementation\nend.");

        Assert.Contains("virtual", labels);
        Assert.Contains("override", labels);
        Assert.Contains("overload", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void PropertySpecifierPosition()
    {
        var labels = Labels("unit P;\ninterface\ntype TFoo = class\n  FX: Integer;\n  property X: Integer $$;\nend;\nimplementation\nend.");

        Assert.Equal(["default", "implements", "index", "nodefault", "read", "stored", "write"], labels.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void DeclarationSectionStart()
    {
        var labels = Labels("program P;\nvar X: Integer;\n$$\nbegin end.");

        Assert.Contains("const", labels);
        Assert.Contains("procedure", labels);
        Assert.Contains("begin", labels);
        Assert.DoesNotContain("private", labels);

        var inClass = Labels("unit P;\ninterface\ntype TFoo = class\n  X: Integer;\n  $$\nend;\nimplementation\nend.");
        Assert.Contains("private", inClass);
        Assert.Contains("property", inClass);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void StatementStart_SymbolsKeywordsAndSnippets()
    {
        var items = Complete("program P;\nvar Counter: Integer;\nbegin\n  $$\nend.").List.Items;

        Assert.Contains(items, i => i.Label == "Counter" && i.Kind == CompletionItemKind.Variable);
        Assert.Contains(items, i => i.Label == "Writeln");
        Assert.Contains(items, i => i.Label == "while" && i.Kind == CompletionItemKind.Keyword);
        Assert.Contains(items, i => i.Kind == CompletionItemKind.Snippet && i.InsertText == "snippet.tryfinally");
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void Expression_SymbolsAndExpressionKeywords()
    {
        var labels = Labels("program P;\nvar Counter: Integer;\nbegin\n  Counter := $$\nend.");

        Assert.Contains("Counter", labels);
        Assert.Contains("nil", labels);
        Assert.Contains("True", labels);
        Assert.DoesNotContain("while", labels);
    }

    [Fact]
    [Trait("Req", "CMP-721")]
    public void OverrideCompletion_VirtualAncestorMethodsNotYetOverridden()
    {
        var items = Complete("program P;\nuses U1;\ntype TDerived = class(TBase)\n  procedure Run; override;\n  procedure $$\nend;\nbegin end.").List.Items;

        var stop = Assert.Single(items, i => i.Label == "Stop");
        Assert.Equal("Stop; override;", stop.InsertText);
        Assert.DoesNotContain(items, i => i.Label == "Run");
    }

    [Fact]
    [Trait("Req", "CMP-723")]
    public void SortText_GroupsLocalsMembersUnitUsedSystemKeywordsSnippets()
    {
        var items = Complete("program P;\nuses U1;\nvar G: Integer;\nprocedure Q(A: Integer);\nvar L: Integer;\nbegin\n  $$\nend;\nbegin end.").List.Items;

        string SortOf(string label) => items.First(i => i.Label == label).SortText;
        Assert.StartsWith("0", SortOf("L"), StringComparison.Ordinal);
        Assert.StartsWith("0", SortOf("A"), StringComparison.Ordinal);
        Assert.StartsWith("2", SortOf("G"), StringComparison.Ordinal);
        Assert.StartsWith("3", SortOf("Exported"), StringComparison.Ordinal);
        Assert.StartsWith("4", SortOf("Writeln"), StringComparison.Ordinal);
        Assert.StartsWith("5", SortOf("while"), StringComparison.Ordinal);
        Assert.StartsWith("6", items.First(i => i.Kind == CompletionItemKind.Snippet).SortText, StringComparison.Ordinal);
        Assert.Equal(items.Select(i => i.SortText).Order(StringComparer.Ordinal), items.Select(i => i.SortText));
    }

    [Fact]
    [Trait("Req", "CMP-722")]
    public void ReplacementSpan_IsTheWordAtTheCaret()
    {
        var (list, text) = Complete("program P;\nvar Counter: Integer;\nbegin\n  Cou$$\nend.");

        Assert.Equal("Cou", text.Substring(list.ReplacementSpan.Start, list.ReplacementSpan.Length));
        Assert.Contains(list.Items, i => i.Label == "Counter");
    }

    [Fact]
    [Trait("Req", "CMP-720")]
    public void OpenParenAndComma_DoNotTriggerCompletion()
    {
        Assert.Empty(Labels("program P;\nbegin\n  Writeln($$\nend.", '('));
    }

    [Fact]
    [Trait("Req", "UI-ED-113")]
    public void ItemsCarryTheirDeclaringUnit()
    {
        var markup = "program P;\nuses U1;\nvar G: Integer;\nprocedure Q(A: Integer);\nvar L: Integer;\nbegin\n  $$\nend;\nbegin end.";
        var caret = markup.IndexOf("$$", StringComparison.Ordinal);
        var text = markup.Remove(caret, 2);
        var workspace = new Workspace(TestFiles.Files(("P.pas", text), ("U1.pas", Unit1.ReplaceLineEndings("\n"))));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        var items = CompletionService.GetCompletions(document, caret, default, TestContext.Current.CancellationToken).Items;

        string OriginOf(string label) => items.First(i => i.Label == label).Origin;
        Assert.Equal("U1", OriginOf("Exported"));
        Assert.Equal("U1", OriginOf("TBase"));
        Assert.Equal("System", OriginOf("Writeln"), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("P", OriginOf("G"));
        Assert.Equal("", OriginOf("L"));                                   // locals and parameters
        Assert.Equal("", OriginOf("A"));
        Assert.Equal("", OriginOf("while"));                               // keywords
        Assert.All(items.Where(i => i.Kind == CompletionItemKind.Snippet), i => Assert.Equal("", i.Origin));

        var exported = items.First(i => i.Label == "Exported");
        Assert.Equal("U1", CompletionService.GetDescription(document, exported, TestContext.Current.CancellationToken).Origin);
        var keyword = items.First(i => i.Label == "while");
        Assert.Equal("", CompletionService.GetDescription(document, keyword, TestContext.Current.CancellationToken).Origin);

        var memberItems = Complete("program P;\nuses U1;\nvar B: TBase;\nbegin\n  B.$$\nend.", '.').List.Items;
        Assert.Equal("U1", memberItems.First(i => i.Label == "Shown").Origin);
        Assert.Equal("System", memberItems.First(i => i.Label == "Free").Origin, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDescription_GivesTheSignatureAndDocumentation()
    {
        var markup = "program P;\n/// Adds one.\nfunction Next(A: Integer): Integer; begin Result := A + 1 end;\nbegin\n  $$\nend.";
        var caret = markup.IndexOf("$$", StringComparison.Ordinal);
        var text = markup.Remove(caret, 2);
        var workspace = new Workspace(TestFiles.Files(("P.pas", text)));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        var item = CompletionService.GetCompletions(document, caret, default, TestContext.Current.CancellationToken).Items.First(i => i.Label == "Next");

        var description = CompletionService.GetDescription(document, item, TestContext.Current.CancellationToken);

        Assert.Contains("Next(A: Integer): Integer", description.Signature, StringComparison.Ordinal);
        Assert.Equal("Adds one.", description.Documentation?.Trim());
    }

    /// <summary>
    /// LANG-572 (C-307): an inline name is offered only inside its declaration scope — not before the declaration (there the
    /// outer entity of that name is), not in its own initialiser and not after the statement list; the <c>until</c> condition
    /// of a <c>repeat</c> is inside the scope (LANG-560), a <c>for var</c> variable is offered in its loop only.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-572")]
    public void InlineVariable_OfferedOnlyInsideScope()
    {
        const string Source = """
            program P;
            var Name: string;
            procedure Run;
            begin
              {@before}
              begin
                {@inner-before}
                var Inner := 1;
                var Name := Inner + {@initialiser}0;
                const Limit = 3;
                {@inside}
              end;
              {@after}
              repeat
                var Line := 1;
              until {@until}Line = 1;
              for var Index := 0 to 2 do
                {@loop};
              {@end}
            end;
            begin
              {@main-before}
              var Top := 1;
              {@main-inside}
            end.
            """;

        CompletionItem[] At(string marker) =>
            [.. Complete(Source.Replace("{@" + marker + "}", "$$", StringComparison.Ordinal)).List.Items];
        string[] Inline(string marker) =>
            [.. At(marker).Where(i => i.Label is "Inner" or "Limit" or "Line" or "Index" or "Top").Select(i => i.Label).Order(StringComparer.Ordinal)];
        string OriginOfName(string marker) => At(marker).Single(i => i.Label == "Name").Origin;

        Assert.Empty(Inline("before"));
        Assert.Empty(Inline("inner-before"));
        Assert.Equal("P", OriginOfName("inner-before"));                   // the global: the inline Name is declared below
        Assert.Equal(["Inner"], Inline("initialiser"));
        Assert.Equal("P", OriginOfName("initialiser"));
        Assert.Equal(["Inner", "Limit"], Inline("inside"));
        Assert.Equal("", OriginOfName("inside"));                          // the inline variable hides the global
        Assert.Contains(At("inside"), i => i.Label == "Inner" && i.Kind == CompletionItemKind.Variable && i.SortText.StartsWith('0'));
        Assert.Empty(Inline("after"));
        Assert.Equal("P", OriginOfName("after"));
        Assert.Equal(["Line"], Inline("until"));
        Assert.Equal(["Index"], Inline("loop"));
        Assert.Empty(Inline("end"));
        Assert.Empty(Inline("main-before"));
        Assert.Equal(["Top"], Inline("main-inside"));
    }

    // ------------------------------------------------------------------ CMP-1394 (C-347): qualified generic and nested type names

    /// <summary>The items after <paramref name="qualifier"/> (ending in a dot) in a statement of a program using <c>UG</c>.</summary>
    private static CompletionItem[] AfterQualifier(string qualifier) =>
        [.. Complete("program P;\nuses UG;\nvar O: TObject;\nbegin\n  O := " + qualifier + "$$\nend.", '.', ("UG.pas", GenericNamesFixture.Unit)).List.Items];

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UnitQualifier_OffersGenericAndNestedTypes()
    {
        var unit = AfterQualifier("UG.");
        Assert.Contains(unit, i => i.Label == "TOuter" && i.Kind == CompletionItemKind.Class);
        Assert.Contains(unit, i => i.Label == "TList" && i.Detail == "type TList<T>");      // a generic with its type parameter list

        var nested = AfterQualifier("UG.TOuter.");
        Assert.Contains(nested, i => i.Label == "TInner" && i.Kind == CompletionItemKind.Class);
        Assert.Contains(nested, i => i.Label == "TBox" && i.Detail == "type TBox<T>");
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void TypeQualifier_OffersNestedGeneric()
    {
        var items = AfterQualifier("TOuter.");

        Assert.Contains(items, i => i.Label == "TBox" && i.Detail == "type TBox<T>");
        Assert.Contains(items, i => i.Label == "TInner");
        Assert.Contains(items, i => i.Label == "Create");
        Assert.DoesNotContain(items, i => i.Label.Contains('<'));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void InstanceQualifier_OffersNestedTypesAndClassMembers()
    {
        foreach (var qualifier in new[] { "TList<Integer>.", "UG.TList<Integer>." })
        {
            string[] labels = [.. AfterQualifier(qualifier).Select(i => i.Label)];

            Assert.Contains("TEnumerator", labels);
            Assert.Contains("Make", labels);
            Assert.Contains("Count", labels);
            Assert.Contains("Create", labels);
            Assert.DoesNotContain("Add", labels);              // instance members need an object
            Assert.DoesNotContain("Items", labels);
        }

        string[] nested = [.. AfterQualifier("TList<Integer>.TEnumerator.").Select(i => i.Label)];
        Assert.Contains("Create", nested);
        Assert.DoesNotContain("MoveNext", nested);
    }

    /// <summary>LANG-780 amendment (C-354): a derived class qualifier offers its ancestors' nested types (an instance ancestor's too).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-780")]
    public void DerivedQualifier_OffersInheritedNestedTypes()
    {
        static string[] After(string qualifier) =>
            [.. Complete("program P;\nuses UG;\ntype\n  TDerived = class(TOuter) end;\n  TMyList = class(TList<Integer>) end;\nvar O: TObject;\nbegin\n  O := "
                + qualifier + "$$\nend.", '.', ("UG.pas", GenericNamesFixture.Unit)).List.Items.Select(i => i.Label)];

        string[] derived = After("TDerived.");
        Assert.Contains("TInner", derived);
        Assert.Contains("TBox", derived);
        Assert.Contains("TEnumerator", After("TMyList."));
        string[] inner = After("TDerived.TInner.");
        Assert.Contains("Create", inner);
        Assert.DoesNotContain("Go", inner);
    }

    /// <summary>C-355: in the method bodies of a generic nothing instantiates, Self offers the generic's (or nested type's) members.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_SelfMembers()
    {
        string[] After(string piece)
        {
            var text = GenericNamesFixture.Uninstantiated;
            var caret = GenericNamesFixture.Offset(text, piece) + piece.Length;
            var doc = GenericNamesFixture.OpenUninstantiated();
            return [.. CompletionService.GetCompletions(doc, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), TestContext.Current.CancellationToken)
                .Items.Select(i => i.Label)];
        }

        string[] stack = After("\n  Self.");
        string[] node = After("  Result := Self.");
        Assert.Contains("Top", stack);
        Assert.Contains("Count", stack);
        Assert.Contains("Push", stack);
        Assert.Contains("Peek", stack);
        Assert.Contains("TNode", stack);
        Assert.Contains("Value", node);
        Assert.Contains("Next", node);
        Assert.Contains("IsLast", node);
        Assert.DoesNotContain("Push", node);
    }
}
