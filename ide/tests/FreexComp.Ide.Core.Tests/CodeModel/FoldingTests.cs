using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>P3 folding from the syntax tree (I-113, IDE-P1-060).</summary>
[Trait("Phase", "3")]
public sealed class FoldingTests
{
    private static string Format(IEnumerable<FoldRegion> regions) => string.Join("\n", regions.Select(r => $"{r} {r.Title}"));

    /// <summary>Valid code beyond the golden files: try/except, nested blocks, helpers, a programme's uses clause.</summary>
    private const string Extra = """
        program Extra;

        uses
          SysUtils,
          Classes;

        type
          THelper = record helper for Integer
            function Twice: Integer;
          end;

        function THelper.Twice: Integer;
        begin
          Result := Self * 2;
        end;

        var
          I: Integer;
        begin
          try
            for I := 1 to 3 do
            begin
              Writeln(I);
            end;
          except
            on E: Exception do
              Writeln(E.Message);
          end;
          {$REGION 'Tail'}
          Writeln;
          {$ENDREGION}
        end.
        """;

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-060")]
    public void TokenAndTreeProvidersAgree()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestData", "folding");
        var sources = Directory.GetFiles(directory, "*.pas").Order(StringComparer.Ordinal).Select(File.ReadAllText).Append(Extra).ToList();
        var valid = 0;
        var failures = new List<string>();
        foreach (var text in sources)
        {
            var tree = SyntaxTree.Parse(SourceText.From(@"C:\Untitled.pas", text));
            if (tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                continue; // the agreement is required for valid code only
            valid++;
            var buffer = new StringTextBuffer(text);
            var expected = Format(new PascalFoldingProvider().GetRegions(buffer));
            var actual = Format(new PascalTreeFoldingProvider().GetRegions(buffer));
            if (expected != actual)
                failures.Add($"--- {text[..Math.Min(text.Length, 20)]}\ntokens:\n{expected}\ntree:\n{actual}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(valid >= 6, $"only {valid} sources parse without errors");

        // P3: the Pascal language folds from the tree; code with syntax errors falls back to the token pairing.
        Assert.IsType<PascalTreeFoldingProvider>(new PascalLanguage().CreateFoldingProvider());
        var unbalanced = new StringTextBuffer(File.ReadAllText(Path.Combine(directory, "unbalanced.pas")));
        Assert.Equal(Format(new PascalFoldingProvider().GetRegions(unbalanced)), Format(new PascalTreeFoldingProvider().GetRegions(unbalanced)));
    }
}
