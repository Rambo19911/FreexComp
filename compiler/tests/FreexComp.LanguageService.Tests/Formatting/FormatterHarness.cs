using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>Runs <see cref="PascalFormatter.Format"/> and applies its edits, checking the edits are sorted and do not overlap.</summary>
internal static class FormatterHarness
{
    public const string Path = @"C:\p\Unit1.pas";

    public static FormatResult Run(string text, FormatProfile? profile = null, string path = Path) =>
        PascalFormatter.Format(SourceText.From(path, text), profile ?? FormatProfile.Default, null, TestContext.Current.CancellationToken);

    /// <summary>The formatted text; fails when the formatter refuses.</summary>
    public static string Format(string text, FormatProfile? profile = null, string path = Path)
    {
        var result = Run(text, profile, path);
        Assert.False(result.Refused, result.RefusalReason);
        return Apply(text, result);
    }

    public static string Apply(string text, FormatResult result)
    {
        var end = -1;
        foreach (var edit in result.Edits)
        {
            Assert.True(edit.Span.Start >= end, $"edits overlap or are unsorted at {edit.Span.Start}");
            Assert.True(edit.Span.Length > 0 || edit.NewText.Length > 0, "empty edit");
            end = edit.Span.End;
        }

        return PascalFormatter.Apply(text, result.Edits);
    }

    /// <summary>
    /// CMP-1437: kind and text of every token (string literals exactly, other text in upper case), every comment and directive,
    /// and the lines of disabled text (without the blank part of its first and last line, which belong to the directive lines).
    /// </summary>
    public static List<string> Stream(string text, string path = Path)
    {
        var list = new List<string>();
        foreach (var token in SyntaxTree.Parse(SourceText.From(path, text)).Root.OwnTokens())
        {
            foreach (var trivia in token.LeadingTrivia.Concat([default]).Concat(token.TrailingTrivia))
            {
                switch (trivia.Kind)
                {
                    case SyntaxKind.None:
                        list.Add(token.Kind + ":" + (token.Kind == SyntaxKind.StringLiteralToken ? token.Text : token.Text.ToUpperInvariant()));
                        break;
                    case SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia:
                        break;
                    case SyntaxKind.DisabledTextTrivia:
                        var lines = trivia.Text.Split('\n');
                        var from = lines.Length > 1 && string.IsNullOrWhiteSpace(lines[0]) ? 1 : 0;
                        var to = lines.Length > 1 && string.IsNullOrWhiteSpace(lines[^1]) ? lines.Length - 1 : lines.Length;
                        var core = string.Join("\n", lines[from..to]);
                        if (core.Trim().Length > 0)
                        {
                            list.Add("disabled:" + core.TrimEnd('\r'));
                        }

                        break;
                    default:
                        list.Add(trivia.Kind + ":" + trivia.Text);
                        break;
                }
            }
        }

        return list;
    }
}
