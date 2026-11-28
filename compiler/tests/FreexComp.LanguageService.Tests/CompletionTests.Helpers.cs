namespace FreexComp.LanguageService.Tests;

// CMP-1492, CMP-1493 (C-446; phase8/helpers-completion.md §9, §15.9): completion of the members of class and record helpers — on
// descendants, type names, string., class references, helper names, literals, constants, through implicit Self and with.
public sealed partial class CompletionTests
{
    /// <summary>A class of the program derived from <c>TChild</c>; <c>$$</c> is at the start of a statement of its method.</summary>
    private const string InAMethodOfADescendant = """
        program P;
        uses UH;
        type
          TGrand = class(TChild)
            procedure Go;
          end;
        procedure TGrand.Go;
        var
          L: Integer;
        begin
          L := 0;
          $$
        end;
        begin
        end.
        """;

    /// <summary>The items at the <c>$$</c> of <paramref name="body"/> in <see cref="HelpersFixture.Program"/> (<c>.</c> typed by default).</summary>
    private static CompletionItem[] HelperItems(string body, char? typed = '.') =>
        [.. Complete(HelpersFixture.Program(body), typed, HelpersFixture.UnitFile).List.Items];

    private static string[] HelperLabels(string body, char? typed = '.') => [.. HelperItems(body, typed).Select(i => i.Label)];

    /// <summary>The items at the <c>$$</c> of a whole program <paramref name="markup"/> that uses <c>UH</c> (invoked).</summary>
    private static CompletionItem[] ItemsWithHelpers(string markup) => [.. Complete(markup, null, HelpersFixture.UnitFile).List.Items];

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1492")]
    public void HelperMembers_OnDescendantTypeNameAndHelperName()
    {
        // (a) a value of a descendant: the members of the ancestor level's helper, the class's own and inherited ones; a name once
        var onValue = HelperItems("  D.$$");
        Assert.Superset(new HashSet<string> { "Show", "Size", "Put", "GetSize", "Work", "N", "Run", "Free" }, onValue.Select(i => i.Label).ToHashSet());
        Assert.Equal("procedure TCounterHelper.Bump", Assert.Single(onValue, i => i.Label == "Bump").Detail);    // the helper's hides the class's
        Assert.Single(onValue, i => i.Label == "Put");                                                            // one overload set, one item
        Assert.DoesNotContain(onValue, i => i.Label == "Hidden");                                                 // strict private in the helper
        Assert.DoesNotContain(onValue, i => i.Label == "Init");                                                   // (e) a class constructor never

        // (b) a type name: the static members, those of the helper of an ancestor level included
        var onType = HelperLabels("  X := TChild.$$");
        Assert.Superset(new HashSet<string> { "Make", "Count", "Limit", "TKind", "Zero", "Create" }, onType.ToHashSet());
        Assert.DoesNotContain("Show", onType);
        Assert.DoesNotContain("N", onType);
        Assert.DoesNotContain("Size", onType);
        Assert.DoesNotContain("Init", onType);

        // string.: the static members of the helper for string
        var onString = HelperLabels("  S := string.$$");
        Assert.Contains("Join", onString);
        Assert.DoesNotContain("Len", onString);
        Assert.DoesNotContain("Upper", onString);

        // a class-reference value: the static members of its class
        var onReference = HelperLabels("  D := R.$$");
        Assert.Superset(new HashSet<string> { "Make", "Zero", "Create", "Count" }, onReference.ToHashSet());
        Assert.DoesNotContain("Show", onReference);
        Assert.DoesNotContain("N", onReference);

        // a helper's name: the static members of the helper and its chain only
        var onHelper = HelperLabels("  X := TCounterHelper.$$");
        Assert.Superset(new HashSet<string> { "Make", "Count", "Limit", "TKind" }, onHelper.ToHashSet());
        Assert.DoesNotContain("Show", onHelper);
        Assert.DoesNotContain("Zero", onHelper);
        Assert.DoesNotContain("Init", onHelper);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1492")]
    public void HelperMembers_OnLiterals()
    {
        var onString = HelperLabels("  S := 'abc'.$$");
        Assert.Contains("Upper", onString);
        Assert.DoesNotContain("IsDigit", onString);
        Assert.Contains("Half", HelperLabels("  E := 1.5.$$"));
        Assert.Contains("IsEven", HelperLabels("  B := (1 + 2).$$"));
        Assert.Contains("IsEven", HelperLabels("  B := K.$$"));                 // an integer constant

        // a character constant: the members of the helper for Char, then those of the helper for string it does not supply
        foreach (var receiver in new[] { "Ch", "'x'" })
        {
            var onChar = HelperItems($"  B := {receiver}.$$");
            Assert.Contains(onChar, i => i.Label == "IsDigit");
            Assert.Equal("function TStrH.Upper: string", Assert.Single(onChar, i => i.Label == "Upper").Detail);
            Assert.Equal("function TCharH.Len: Integer", Assert.Single(onChar, i => i.Label == "Len").Detail);
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1492")]
    public void AfterIntegerLiteralDot_OnlyWhenInvoked()
    {
        Assert.Empty(HelperItems("  B := 42.$$", '.'));                          // a real literal may be being typed
        Assert.Contains("IsEven", HelperLabels("  B := 42.$$", null));          // invoked: the members of Integer

        // after a real literal, a string literal, ')' and a constant a typed '.' opens the list as usual
        Assert.Contains("Half", HelperLabels("  E := 1.5.$$", '.'));
        Assert.Contains("Len", HelperLabels("  X := 'abc'.$$", '.'));
        Assert.Contains("IsEven", HelperLabels("  B := (42).$$", '.'));
        Assert.Contains("IsEven", HelperLabels("  B := K.$$", '.'));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1492")]
    public void HelperMembers_ImplicitSelfAndWith()
    {
        static void AssertInGroupOne(CompletionItem[] items)
        {
            foreach (var label in new[] { "Show", "Size", "Bump", "N" })
            {
                Assert.StartsWith("1", Assert.Single(items, i => i.Label == label).SortText, StringComparison.Ordinal);
            }

            Assert.Equal("procedure TCounterHelper.Bump", Assert.Single(items, i => i.Label == "Bump").Detail);
            Assert.DoesNotContain(items, i => i.Label is "Hidden" or "Init");
        }

        // a statement start in a method of a descendant of the helped class
        AssertInGroupOne(ItemsWithHelpers(InAMethodOfADescendant));

        // inside with
        AssertInGroupOne(ItemsWithHelpers(HelpersFixture.Program("  with D do\n    $$")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1493")]
    public void HelperMembers_CarryTheHelperName()
    {
        static string HelperOf(CompletionItem[] items, string label) => Assert.Single(items, i => i.Label == label).Helper;

        var onValue = HelperItems("  D.$$");
        Assert.Equal("TCounterHelper", HelperOf(onValue, "Show"));               // method
        Assert.Equal("TCounterHelper", HelperOf(onValue, "Size"));               // property
        Assert.Equal("", HelperOf(onValue, "N"));                                // members of the type itself
        Assert.Equal("", HelperOf(onValue, "Run"));
        Assert.Equal("", HelperOf(onValue, "Free"));

        var onType = HelperItems("  X := TChild.$$");
        Assert.Equal("TCounterHelper", HelperOf(onType, "Count"));               // class var
        Assert.Equal("TCounterHelper", HelperOf(onType, "Limit"));               // constant
        Assert.Equal("TCounterHelper", HelperOf(onType, "TKind"));               // nested type
        Assert.Equal("TCounterHelper", HelperOf(onType, "Make"));
        Assert.Equal("", HelperOf(onType, "Zero"));

        Assert.Equal("TStrH", HelperOf(HelperItems("  S := 'abc'.$$"), "Upper"));

        // a statement start: helper members (implicit Self), locals and keywords
        var inMethod = ItemsWithHelpers(InAMethodOfADescendant);
        Assert.Equal("TCounterHelper", HelperOf(inMethod, "Show"));
        Assert.Equal("", HelperOf(inMethod, "L"));
        Assert.Equal("", HelperOf(inMethod, "Go"));
        Assert.Equal("", Assert.Single(inMethod, i => i.Label == "if" && i.Kind == CompletionItemKind.Keyword).Helper);
    }

    /// <summary>
    /// CMP-1492 (C-446; finding (c) of C-442): after <c>G.</c> on a value of a parameterless function type the members of the helper for
    /// the function type are offered, as well as those for the function's result.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1492")]
    public void HelperMembers_OnAFunctionValue()
    {
        const string markup = """
            program P;
            type
              TFunc = function: Integer;
              TFuncHelper = record helper for TFunc
                function Twice: Integer;
              end;
              TIntH = record helper for Integer
                function Neg: Integer;
              end;
            function TFuncHelper.Twice: Integer; begin Result := Self() * 2 end;
            function TIntH.Neg: Integer; begin Result := -Self end;
            var
              G: TFunc;
            begin
              Writeln(G.$$);
            end.
            """;
        var items = Complete(markup, '.').List.Items;

        Assert.Equal("TFuncHelper", Assert.Single(items, i => i.Label == "Twice").Helper);
        Assert.Equal("TIntH", Assert.Single(items, i => i.Label == "Neg").Helper);
    }
}
