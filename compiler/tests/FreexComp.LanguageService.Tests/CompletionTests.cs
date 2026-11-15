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
}
