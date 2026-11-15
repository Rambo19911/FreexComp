using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §9 CMP-760: one case per range kind (C-140).</summary>
[Trait("Phase", "3")]
public sealed class FoldingTests
{
    private const string Path = @"C:\p\Unit1.pas";

    private static (string Text, FoldingKind Kind, string Collapsed)[] Ranges(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(Path, text));
        var ranges = FoldingService.GetFoldingRanges(tree);
        for (var i = 1; i < ranges.Length; i++)
        {
            Assert.True(ranges[i - 1].Span.Start <= ranges[i].Span.Start, "sorted by start");
        }

        return [.. ranges.Select(r => (text.Substring(r.Span.Start, r.Span.Length), r.Kind, r.CollapsedText))];
    }

    private const string UnitText = """
        unit Unit1;
        interface
        uses
          SysUtils;
        type
          TFoo = class
            X: Integer;
          end;
        implementation
        const
          A = 1;
          B = 2;
        var
          V: Integer;
        procedure P;
        begin
          try
            V := 1;
          finally
            V := 2;
          end;
        end;
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-760")]
    public void SectionsUsesDeclarationSectionsTypeRoutineAndBlocks()
    {
        var text = UnitText.ReplaceLineEndings("\n");
        var ranges = Ranges(text);

        Assert.Contains(ranges, r => r.Kind == FoldingKind.Section && r.Text.StartsWith("interface\nuses", StringComparison.Ordinal) && r.Text.EndsWith("end;", StringComparison.Ordinal)
                                     && r.Collapsed == "interface …");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Section && r.Text.StartsWith("implementation", StringComparison.Ordinal));
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Uses && r.Text == "uses\n  SysUtils;" && r.Collapsed == "uses …");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Declarations && r.Text.StartsWith("type\n  TFoo", StringComparison.Ordinal));
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Declarations && r.Text == "const\n  A = 1;\n  B = 2;");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Declarations && r.Text == "var\n  V: Integer;");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Type && r.Text == "class\n    X: Integer;\n  end" && r.Collapsed == "class …");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Routine && r.Text.StartsWith("procedure P;\nbegin", StringComparison.Ordinal) && r.Text.EndsWith("end;\nend;", StringComparison.Ordinal)
                                     && r.Collapsed == "procedure P; …");
        Assert.Contains(ranges, r => r.Kind == FoldingKind.Block && r.Text.StartsWith("try", StringComparison.Ordinal) && r.Text.EndsWith("end", StringComparison.Ordinal));
        Assert.DoesNotContain(ranges, r => r.Kind == FoldingKind.Block && r.Text.StartsWith("begin\n  try", StringComparison.Ordinal));   // the routine body is the Routine range
    }

    [Fact]
    [Trait("Req", "CMP-760")]
    public void BlockStatements()
    {
        const string text = "program P;\nvar I: Integer;\nbegin\n  begin\n    I := 1;\n  end;\n  case I of\n    1: ;\n  end;\n  repeat\n    Inc(I)\n  until I > 3;\nend.";

        var blocks = Ranges(text).Where(r => r.Kind == FoldingKind.Block).Select(r => r.Text.Split('\n')[0]).ToArray();

        Assert.Equal(["begin", "begin", "case I of", "repeat"], blocks);
    }

    [Fact]
    [Trait("Req", "CMP-760")]
    public void SingleLineConstructsAreNotFolded()
    {
        Assert.DoesNotContain(Ranges("program P; begin begin end; end."), r => r.Kind == FoldingKind.Block);
    }

    [Fact]
    [Trait("Req", "CMP-760")]
    public void Region()
    {
        const string text = "program P;\n{$REGION 'Helpers'}\nprocedure Q; begin end;\n{$ENDREGION}\nbegin end.";

        var region = Assert.Single(Ranges(text), r => r.Kind == FoldingKind.Region);

        Assert.Equal("{$REGION 'Helpers'}\nprocedure Q; begin end;\n{$ENDREGION}", region.Text);
        Assert.Equal("Helpers", region.Collapsed);
    }

    [Fact]
    [Trait("Req", "CMP-760")]
    public void Comments()
    {
        const string text = "program P;\n{ multi\n  line }\n// one\n// two\n// three\n// x\n\n// only\n// two\n(* also\n multi *)\nbegin end.";

        var comments = Ranges(text).Where(r => r.Kind == FoldingKind.Comment).Select(r => r.Text).ToArray();

        Assert.Equal(["{ multi\n  line }", "// one\n// two\n// three\n// x", "(* also\n multi *)"], comments);
    }

    [Fact]
    [Trait("Req", "CMP-760")]
    public void DisabledRegion()
    {
        const string text = "program P;\n{$IFDEF NEVER}\nx\ny\n{$ENDIF}\nbegin end.";

        var disabled = Assert.Single(Ranges(text), r => r.Kind == FoldingKind.Disabled);

        Assert.Equal("x\ny", disabled.Text.Trim('\n'));
    }
}
