namespace FreexComp.LanguageService.Tests;

// CMP-1494 (C-446; phase8/helpers-completion.md §9, §15.9): hover over helper members and helper types.
public sealed partial class QuickInfoTests
{
    /// <summary>
    /// CMP-1494: a reference to a helper member shows its declaration qualified with the helper's name and the helper's unit as origin,
    /// for every form of reference — a value of a descendant, a literal, a character constant (also falling back to the helper for
    /// string), <c>string.</c>, a type name, the helper's name, <c>with</c> and implicit <c>Self</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1494")]
    public void HelperMember_IsQualifiedWithTheHelper()
    {
        static QuickInfo Info(string markup)
        {
            var info = HoverAt(markup, HelpersFixture.UnitFile);
            Assert.NotNull(info);
            return info;
        }

        static string Signature(string body) => Info(HelpersFixture.Program(body)).Signature;

        var show = Info(HelpersFixture.Program("  D.Sh$$ow;"));
        Assert.Equal("procedure TCounterHelper.Show", show.Signature);
        Assert.Equal("UH.TCounterHelper", show.DeclaredIn);
        Assert.Equal("property TCounterHelper.Size: Integer read", Signature("  X := D.Si$$ze;"));
        Assert.Equal("function TStrH.Len: Integer", Signature("  X := 'abc'.L$$en;"));
        Assert.Equal("function TCharH.Len: Integer", Signature("  X := Ch.L$$en;"));
        Assert.Equal("function TStrH.Upper: string", Signature("  S := 'x'.Up$$per;"));
        Assert.Equal("function TIntH.Plus(A: Integer): Integer", Signature("  X := 5.Pl$$us(1);"));
        Assert.Equal("function TIntH.Plus(A: Integer; B: Integer): Integer", Signature("  X := K.Pl$$us(1, 2);"));
        Assert.Equal("function TDblH.Half: Double", Signature("  E := 1.5.Ha$$lf;"));
        Assert.StartsWith("class function TStrH.Join(const A: string; const B: string): string", Signature("  S := string.Jo$$in('a', 'b');"), StringComparison.Ordinal);
        Assert.Equal("class function TCounterHelper.Make: TCounter", Signature("  D := TChild(TChild.Ma$$ke);"));
        Assert.Equal("class function TCounterHelper.Make: TCounter", Signature("  D := TChild(TCounterHelper.Ma$$ke);"));
        Assert.Equal("procedure TCounterHelper.Show", Signature("  with D do Sh$$ow;"));

        var inMethod = Info("program P;\nuses UH;\ntype\n  TGrand = class(TChild)\n    procedure Go;\n  end;\nprocedure TGrand.Go;\nbegin\n  Sh$$ow;\nend;\nbegin\nend.");
        Assert.Equal("procedure TCounterHelper.Show", inMethod.Signature);
        Assert.Equal("UH.TCounterHelper", inMethod.DeclaredIn);
    }

    /// <summary>CMP-1494: a helper type shows its helped type, with an ancestor helper <c>class helper(TBase) for T</c>; a record helper too.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1494")]
    public void HelperType_ShowsHelpedTypeAndAncestor()
    {
        const string text = """
            program P;
            type
              TFoo = class
              end;
              TFooHelper = class helper for TFoo
              end;
              TFooMoreHelper = class helper(TFooHelper) for TFoo
              end;
              TIntHelper = record helper for Integer
              end;
            var
              H: TFooMoreHelper;
            begin
            end.
            """;
        var program = text.ReplaceLineEndings("\n");
        var doc = MarkupTestFile.Open("P.pas", ("P.pas", program));
        string? Signature(string piece, int skip = 0)
        {
            var at = program.IndexOf(piece, StringComparison.Ordinal);
            for (var i = 0; i < skip; i++)
            {
                at = program.IndexOf(piece, at + 1, StringComparison.Ordinal);
            }

            return QuickInfoService.GetQuickInfo(doc, at + 1, TestContext.Current.CancellationToken)?.Signature;
        }

        Assert.Equal("type TFooHelper = class helper for TFoo", Signature("TFooHelper ="));
        Assert.Equal("type TFooMoreHelper = class helper(TFooHelper) for TFoo", Signature("TFooMoreHelper ="));
        Assert.Equal("type TFooMoreHelper = class helper(TFooHelper) for TFoo", Signature("TFooMoreHelper;"));
        Assert.Equal("type TFooHelper = class helper for TFoo", Signature("TFooHelper)"));
        Assert.Equal("type TIntHelper = record helper for Integer", Signature("TIntHelper ="));
    }
}
