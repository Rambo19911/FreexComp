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

    /// <summary>CMP-732 (C-393): the completion description renders the XML tags of the doc comment as hover does.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-732")]
    public void GetDescription_RendersXmlDocComment()
    {
        var markup = "program P;\n/// <summary>Adds <c>1</c>.</summary>\n/// <param name=\"A\">The start.</param>\n/// <returns>A + 1.</returns>\n"
            + "function Next(A: Integer): Integer; begin Result := A + 1 end;\nbegin\n  $$\nend.";
        var caret = markup.IndexOf("$$", StringComparison.Ordinal);
        var text = markup.Remove(caret, 2);
        var workspace = new Workspace(TestFiles.Files(("P.pas", text)));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        var item = CompletionService.GetCompletions(document, caret, default, TestContext.Current.CancellationToken).Items.First(i => i.Label == "Next");

        var description = CompletionService.GetDescription(document, item, TestContext.Current.CancellationToken);

        Assert.Equal("Adds 1.\n\nParameters:\n  A – The start.\n\nReturns: A + 1.", description.Documentation);
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

    /// <summary>
    /// C-358: in a generic nothing instantiates, a value of a type parameter with a class or interface constraint offers the members of
    /// that constraint (hover shows them), and a <c>constructor</c> constraint offers <c>Create</c> on the type parameter.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_ConstrainedParameterMembers()
    {
        var text = GenericNamesFixture.Constrained;
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        string[] After(string piece)
        {
            var caret = GenericNamesFixture.Offset(text, piece) + piece.Length;
            return [.. CompletionService.GetCompletions(doc, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), TestContext.Current.CancellationToken)
                .Items.Select(i => i.Label)];
        }

        string[] item = After("    Item.");
        string[] inner = After("Rename(Item.");
        string[] shape = After("if Shape.");
        string[] made = After("Result := C.");
        Assert.Contains("Name", item);
        Assert.Contains("Rename", item);
        Assert.Contains("Free", item);                                          // inherited from the constraint's ancestor
        Assert.DoesNotContain("Area", item);
        Assert.DoesNotContain("Larger", item);
        Assert.Contains("Name", inner);
        Assert.Contains("Area", shape);
        Assert.DoesNotContain("Name", shape);
        Assert.Contains("Create", made);

        var rename = GenericNamesFixture.Offset(text, "Item.Rename") + "Item.".Length + 1;
        Assert.Equal("procedure TThing.Rename(const S: string)", QuickInfoService.GetQuickInfo(doc, rename, TestContext.Current.CancellationToken)?.Signature);
    }
    /// <summary>
    /// C-364: in a generic nothing instantiates, a member of a value of a constrained type parameter is bound through the constraint,
    /// so a member of that member offers its own type's members (<c>Item.Owner.</c>, <c>Item.Parent.</c>, <c>Shape.Bounds.</c>,
    /// <c>Larger(Item, Item).Owner.</c>) and hover shows it.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1394")]
    public void UninstantiatedGeneric_ChainedConstraintMembers()
    {
        var text = GenericNamesFixture.Chained;
        var doc = GenericNamesFixture.OpenUninstantiated(text);
        string[] After(string piece)
        {
            var caret = GenericNamesFixture.Offset(text, piece) + piece.Length;
            return [.. CompletionService.GetCompletions(doc, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), TestContext.Current.CancellationToken)
                .Items.Select(i => i.Label)];
        }

        string[] owner = After("  Item.Owner.");
        string[] parent = After("Rename(Item.Parent.");
        string[] bounds = After("  Shape.Bounds.");
        string[] larger = After("Larger(Item, Item).Owner.");
        Assert.Contains("Rename", owner);
        Assert.Contains("Title", owner);
        Assert.DoesNotContain("Owner", owner);
        Assert.Contains("Title", parent);
        Assert.Contains("Rename", bounds);
        Assert.Contains("Title", larger);

        var rename = GenericNamesFixture.Offset(text, "Item.Owner.Rename") + "Item.Owner.".Length + 1;
        Assert.Equal("procedure TOwner.Rename(const S: string)", QuickInfoService.GetQuickInfo(doc, rename, TestContext.Current.CancellationToken)?.Signature);
    }

    // ------------------------------------------------------------------ CMP-1400 (C-377): class constructors and destructors

    /// <summary>
    /// CMP-1400 (C-377), LANG-787: completion never offers a class constructor or class destructor — after the class, record,
    /// nested-type or instance qualifier, at a statement start in a descendant's method or after <c>inherited</c> — and signature
    /// help on <c>TA.Create(</c> shows only the ordinary constructor.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void ClassConstructor_NeverOffered()
    {
        var unit = ("UC.pas", ClassConstructorFixture.Unit);
        CompletionItem[] Items(string markup, char? typed = null) => [.. Complete(markup, typed, unit).List.Items];
        string[] LabelsIn(string markup, char? typed = null) => [.. Items(markup, typed).Select(i => i.Label)];
        string[] hidden = ["Teardown", "Done", "Init"];

        var afterClass = Items("program P;\nuses UC;\nbegin\n  TA.$$\nend.", '.');
        Assert.Contains(afterClass, i => i.Label == "Create");
        Assert.All(afterClass.Where(i => i.Label == "Create"), i => Assert.DoesNotContain("class constructor", i.Detail ?? "", StringComparison.Ordinal));
        Assert.Contains(afterClass, i => i.Label == "Value");
        Assert.DoesNotContain(afterClass, i => hidden.Contains(i.Label));
        Assert.DoesNotContain("Init", LabelsIn("program P;\nuses UC;\nbegin\n  TR.$$\nend.", '.'));
        Assert.DoesNotContain("Done", LabelsIn("program P;\nuses UC;\nbegin\n  TA.TN.$$\nend.", '.'));
        var instance = LabelsIn("program P;\nuses UC;\nvar A: TA;\nbegin\n  A.$$\nend.", '.');
        Assert.Contains("Run", instance);
        Assert.DoesNotContain(instance, hidden.Contains);

        const string Descendant = "program P;\nuses UC;\ntype TD = class(TA) procedure Go; end;\nprocedure TD.Go;\nbegin\n  {@}\nend;\nbegin end.";
        var statement = LabelsIn(Descendant.Replace("{@}", "$$", StringComparison.Ordinal));
        Assert.Contains("Run", statement);
        Assert.DoesNotContain(statement, hidden.Contains);
        var inherited = LabelsIn(Descendant.Replace("{@}", "inherited $$", StringComparison.Ordinal));
        Assert.Contains("Run", inherited);
        Assert.DoesNotContain(inherited, hidden.Contains);

        var markup = MarkupTestFile.Parse("program P;\nuses UC;\nvar A: TA;\nbegin\n  A := TA.Create($$1);\nend.");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", markup.Text), unit);
        var help = SignatureHelpService.GetSignatureHelp(doc, markup.Caret, TestContext.Current.CancellationToken);
        Assert.NotNull(help);
        Assert.Contains(help.Signatures, s => s.Label.Contains("N: Integer", StringComparison.Ordinal));
        Assert.DoesNotContain(help.Signatures, s => s.Label.Contains("class constructor", StringComparison.Ordinal));
    }

    /// <summary>
    /// CMP-1400 (C-377), CMP-1399: a type imported from a <c>.pcu</c> carries its class constructor and destructor for display only
    /// (no declaration site): completion does not offer them, hover and go-to-definition on <c>TA.Create(1)</c> never reach them.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1400")]
    public void ClassConstructor_FromPcu_NeverOffered()
    {
        var source = TestFiles.Files(("P.pas", ClassConstructorFixture.Program), ("UC.pas", ClassConstructorFixture.Unit));
        var pcu = Compiler.Compilation.Create(TestFiles.Options(), [], source).PcuTexts().Single(t => t.Unit == "UC").Text;
        Assert.Contains("\"classConstructor\"", pcu, StringComparison.Ordinal);
        Workspace Open(string program)
        {
            var fs = TestFiles.Files(("UC.pas", ClassConstructorFixture.Unit));
            fs.Add(@"C:\lib\UC.pcu", pcu).Add(@"C:\lib\UC.obj", "").Add(@"C:\q\P.pas", program);
            var workspace = new Workspace(fs);
            workspace.SetProject(TestFiles.Options() with { MainFile = @"C:\q\P.pas", SearchPaths = [@"C:\lib"], ObjectDirectory = @"C:\q\obj" });
            return workspace;
        }

        var markup = MarkupTestFile.Parse("program P;\nuses UC;\nbegin\n  TA.$$\nend.");
        var completion = Open(markup.Text).GetDocument(@"C:\q\P.pas");
        Assert.DoesNotContain(completion.Compilation.SourceUnitPaths, p => p.EndsWith("UC.pas", StringComparison.OrdinalIgnoreCase));
        var labels = CompletionService.GetCompletions(completion, markup.Caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'),
                                                      TestContext.Current.CancellationToken).Items.Select(i => i.Label).ToArray();
        Assert.Contains("Create", labels);
        Assert.DoesNotContain("Teardown", labels);

        var doc = Open(ClassConstructorFixture.Program).GetDocument(@"C:\q\P.pas");
        var call = ClassConstructorFixture.IndexOf(ClassConstructorFixture.Program, "TA.Create(1)") + 4;
        Assert.Equal("constructor TA.Create(N: Integer)", QuickInfoService.GetQuickInfo(doc, call, TestContext.Current.CancellationToken)?.Signature);
        // a .pcu records declaration sites of unit-level symbols only, so a method of an imported type has none yet; never the class constructor
        Assert.All(NavigationService.GoToDefinition(doc, call, TestContext.Current.CancellationToken),
                   l => Assert.Equal(ClassConstructorFixture.IndexOf(ClassConstructorFixture.Unit, "Create(N: Integer);"), l.Span.Start));
    }

    /// <summary>
    /// C-416 (CMP-1476): the four type queries are offered wherever <c>SizeOf</c> and <c>Default</c> are, with the same item kind,
    /// group and origin, and the signature texts of LANG-972.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    [InlineData("program P;\nvar B: Boolean;\nbegin\n  B := $$\nend.")]                                     // an expression
    [InlineData("program P;\nvar B: Boolean;\nbegin\n  $$\nend.")]                                          // a statement start
    [InlineData("program P;\nconst K = $$;\nbegin\nend.")]                                                  // a constant expression
    [InlineData("program P;\ntype TBox<T> = class procedure Run; end;\nprocedure TBox<T>.Run;\nvar B: Boolean;\nbegin\n  if $$ then B := True;\nend;\nbegin\nend.")]
    public void TypeQueryIntrinsics_AreOffered(string markup)
    {
        var items = Complete(markup).List.Items;

        var sizeOf = Assert.Single(items, i => i.Label == "SizeOf");
        var @default = Assert.Single(items, i => i.Label == "Default");
        Assert.Equal("function Default(T: Type): T", @default.Detail);
        (string Name, string Signature)[] queries =
        [
            ("IsManagedType", "IsManagedType(T: Type): Boolean"), ("HasWeakRef", "HasWeakRef(T: Type): Boolean"),
            ("GetTypeKind", "GetTypeKind(T: Type): TTypeKind"), ("IsConstValue", "IsConstValue(const X): Boolean"),
        ];
        foreach (var (name, signature) in queries)
        {
            var item = Assert.Single(items, i => i.Label == name);
            Assert.Equal(
                (sizeOf.Kind, "function " + signature, sizeOf.SortText[..1], sizeOf.Origin, name, false),
                (item.Kind, item.Detail, item.SortText[..1], item.Origin, item.InsertText, item.IsDeprecated));
            Assert.Equal((@default.Kind, @default.SortText[..1], @default.Origin), (item.Kind, item.SortText[..1], item.Origin));
        }

        Assert.Equal(items.Select(i => i.SortText).Order(StringComparer.Ordinal), items.Select(i => i.SortText));   // CMP-723: the order contract
    }

    // ------------------------------------------------------------------ CMP-1476 (C-417): both parts of a type-query condition

    private const string TypeQueryUnit = """
        unit UO;
        interface
        type
          TItem = class
            Name: string;
            procedure Rename(const S: string);
          end;
          TPoint = record
            X, Y: Integer;
          end;
          TStore<T> = class
          public
            Left: TItem;
            Right: TPoint;
            procedure Put(const V: T);
            procedure Pick(const V: T);
            procedure ByConstant(const V: T);
          end;
        implementation
        procedure TItem.Rename(const S: string);
        begin
          Name := S;
        end;
        procedure TStore<T>.Put(const V: T);
        begin
          if IsManagedType(T) then
          begin
            var Managed := Left;
            Managed.Rename('managed');
          end
          else
          begin
            var Plain := Right;
            Plain.X := 1;
          end;
        end;
        procedure TStore<T>.Pick(const V: T);
        begin
          case GetTypeKind(T) of
            tkInteger:
              begin
                var Number := Left;
                Number.Rename('integer');
              end;
            tkUString:
              begin
                var Text := Right;
                Text.Y := 2;
              end;
          else
            var Other := Left;
            Other.Name := 'else';
          end;
        end;
        procedure TStore<T>.ByConstant(const V: T);
        const
          Raw = not IsManagedType(T);
        begin
          if Raw then
          begin
            var Fast := Left;
            Fast.Rename('raw');
          end
          else
          begin
            var Slow := Right;
            Slow.X := 3;
          end;
          if IsManagedType(string) then
            Right.Y := 4
          else
          begin
            var Never := Left;
            Never.Rename('never');
          end;
        end;
        end.
        """;

    private static readonly string[] ItemMembers = ["Name", "Rename"];

    private static readonly string[] PointMembers = ["X", "Y"];

    /// <summary>The members offered after each <c>Variable.</c> of <paramref name="text"/>, and the hover of that variable.</summary>
    private static void AssertBothParts(Document doc, string text, params (string Piece, string[] Members)[] expected)
    {
        foreach (var (piece, members) in expected)
        {
            var caret = GenericNamesFixture.Offset(text, piece) + piece.Length;
            var labels = CompletionService.GetCompletions(doc, caret, new CompletionTrigger(CompletionTriggerKind.Character, '.'), TestContext.Current.CancellationToken)
                .Items.Select(i => i.Label).ToList();
            Assert.All(members, m => Assert.True(labels.Contains(m), $"{piece}: {m} is not offered ({string.Join(", ", labels.Take(8))})"));
            var hover = QuickInfoService.GetQuickInfo(doc, caret - 2, TestContext.Current.CancellationToken);    // the inline variable before the dot
            Assert.True(hover?.Signature.Contains(piece.Trim().TrimEnd('.'), StringComparison.Ordinal), $"{piece}: hover is '{hover?.Signature}'");
        }
    }

    /// <summary>
    /// CMP-1476 (LANG-976): in a generic nothing instantiates (the open instance) nothing is pruned — both parts of an <c>if</c> on a
    /// type query, every arm and the <c>else</c> list of a <c>case</c> on one, both parts of an <c>if</c> on a constant whose value is a
    /// type query on the type parameter (it has no value there), and both parts of an <c>if</c> on a type query that is a constant even
    /// there (<c>IsManagedType(string)</c>) have their inline variables, member lists and hover.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    public void UninstantiatedGeneric_BothBranchesOfATypeQueryIf()
    {
        var text = TypeQueryUnit.ReplaceLineEndings("\n");
        var doc = GenericNamesFixture.OpenUninstantiated(text);

        AssertBothParts(doc, text,
            ("    Managed.", ItemMembers), ("    Plain.", PointMembers),
            ("        Number.", ItemMembers), ("        Text.", PointMembers), ("    Other.", ItemMembers),
            ("    Fast.", ItemMembers), ("    Slow.", PointMembers), ("    Never.", ItemMembers));

        // the constant itself: declared, shown and found from its use although it has no value in the open instance
        var use = GenericNamesFixture.Offset(text, "if Raw then") + "if ".Length;
        Assert.Equal("const Raw = ?", QuickInfoService.GetQuickInfo(doc, use, TestContext.Current.CancellationToken)?.Signature);
        var declaration = GenericNamesFixture.Offset(text, "  Raw = not");
        Assert.Equal(
            [declaration + 2, use],
            NavigationService.FindReferences(doc, use, TestContext.Current.CancellationToken).Select(r => r.Location.Span.Start));
        Assert.DoesNotContain(
            DiagnosticService.GetSemanticDiagnostics(doc, TestContext.Current.CancellationToken), d => d.Severity == Compiler.DiagnosticSeverity.Error);
    }

    /// <summary>
    /// CMP-1476 with LANG-977 rule 3: an instance in the same file prunes the part its type argument does not take and does not bind
    /// it; code insight in that part comes from the open instance — inline variables, member lists, hover, and the references of a
    /// local that both parts use.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    public void InstantiatedGeneric_PrunedBranchKeepsCodeInsight()
    {
        var text = TypeQueryUnit.ReplaceLineEndings("\n")
            .Replace("unit UO;\ninterface\n", "program P;\n", StringComparison.Ordinal)
            .Replace("implementation\n", "", StringComparison.Ordinal)
            .Replace("procedure TStore<T>.Put(const V: T);\nbegin\n", "procedure TStore<T>.Put(const V: T);\nvar\n  Shared: Integer;\nbegin\n  Shared := 0;\n", StringComparison.Ordinal)
            .Replace("    Managed.Rename('managed');\n", "    Managed.Rename('managed');\n    Shared := Shared + 1;\n", StringComparison.Ordinal)
            .Replace("    Plain.X := 1;\n", "    Plain.X := Shared;\n", StringComparison.Ordinal)
            .Replace("end.", "var\n  S: TStore<Integer>;\nbegin\n  S := TStore<Integer>.Create;\n  S.Put(1);\n  S.Pick(2);\n  S.ByConstant(3);\n  S.Free;\nend.", StringComparison.Ordinal);
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", text));
        Assert.Empty(DiagnosticService.GetSemanticDiagnostics(doc, TestContext.Current.CancellationToken).Where(d => d.Severity == Compiler.DiagnosticSeverity.Error));

        AssertBothParts(doc, text,
            ("    Managed.", ItemMembers), ("    Plain.", PointMembers),                 // Integer keeps Plain
            ("        Number.", ItemMembers), ("        Text.", PointMembers), ("    Other.", ItemMembers),
            ("    Fast.", ItemMembers), ("    Slow.", PointMembers), ("    Never.", ItemMembers));

        var declaration = GenericNamesFixture.Offset(text, "  Shared: Integer;") + 2;
        int[] uses =
        [
            declaration, GenericNamesFixture.Offset(text, "  Shared := 0;") + 2, GenericNamesFixture.Offset(text, "    Shared := Shared + 1;") + 4,
            GenericNamesFixture.Offset(text, ":= Shared + 1;") + 3, GenericNamesFixture.Offset(text, "Plain.X := Shared;") + "Plain.X := ".Length,
        ];
        Assert.Equal(uses, NavigationService.FindReferences(doc, declaration, TestContext.Current.CancellationToken).Select(r => r.Location.Span.Start));
        Assert.Equal(uses, NavigationService.FindReferences(doc, uses[2], TestContext.Current.CancellationToken).Select(r => r.Location.Span.Start));   // from the pruned part
        var renamed = RenameService.Rename(doc, declaration, "Common", TestContext.Current.CancellationToken);
        Assert.True(renamed.Success, renamed.Error);
        Assert.Equal(uses, Assert.Single(renamed.Edits).Changes.Select(c => c.Span.Start));
    }
}
