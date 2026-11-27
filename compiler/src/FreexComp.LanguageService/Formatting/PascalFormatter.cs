using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// The Object Pascal source formatter (formatter.md, F11-101): re-lays out the whitespace between tokens and never changes a
/// token, comment or directive (CMP-1437). A file with a syntax error is refused (CMP-1438).
/// </summary>
public static class PascalFormatter
{
    /// <summary>The name of a style profile file (CMP-1452).</summary>
    internal const string ProfileFileName = ".pasformat";

    /// <summary>
    /// Formats <paramref name="text"/> with <paramref name="profile"/>. Returns the edits that turn the text into its formatted
    /// form, or a refusal (syntax error; CMP-1438). <paramref name="range"/> formatting is not available yet (C-405): a non-null
    /// range throws <see cref="ArgumentException"/>.
    /// </summary>
    public static FormatResult Format(SourceText text, FormatProfile profile, TextSpan? range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);
        if (range is not null)
        {
            throw new ArgumentException("Range formatting is not available yet.", nameof(range));
        }

        var tree = SyntaxTree.Parse(text);
        ct.ThrowIfCancellationRequested();
        var error = tree.Diagnostics.FirstOrDefault(IsSyntaxError);
        if (error is not null)
        {
            var at = error.Location.LineSpan.Start;
            return Refuse(string.Create(CultureInfo.InvariantCulture, $"({at.Line + 1},{at.Column + 1}): error {error.Code}: {error.Message}"));
        }

        var tokens = tree.Root.OwnTokens().Where(t => !t.IsMissing).ToArray();
        var plan = new LayoutPlan(tokens);
        LayoutPlanner.Plan(plan, tree.Root, profile, ct);
        var gaps = new LayoutWriter(text.Text, plan, profile, ct).Write(out var formatted);
        ct.ThrowIfCancellationRequested();

        if (!TokenStreamsMatch(tokens, SyntaxTree.Parse(text with { Text = formatted })))
        {
            return Refuse("the formatted text would not keep the tokens and comments as they are");
        }

        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        foreach (var gap in gaps)
        {
            var oldText = text.Text.AsSpan(gap.OldStart, gap.OldEnd - gap.OldStart);
            var newText = formatted.AsSpan(gap.NewStart, gap.NewEnd - gap.NewStart);
            if (oldText.SequenceEqual(newText))
            {
                continue;
            }

            var prefix = oldText.CommonPrefixLength(newText);
            var suffix = 0;
            while (suffix < oldText.Length - prefix && suffix < newText.Length - prefix && oldText[^(suffix + 1)] == newText[^(suffix + 1)])
            {
                suffix++;
            }

            edits.Add(new TextEdit(
                new TextSpan(gap.OldStart + prefix, oldText.Length - prefix - suffix),
                newText[prefix..^suffix].ToString()));
        }

        return new FormatResult(false, null, edits.ToImmutable());
    }

    /// <summary>
    /// CMP-1452: the <see cref="ProfileFileName"/> in the directory of <paramref name="sourceFilePath"/> or the nearest parent
    /// directory; none, or one that is not a JSON object → <see cref="FormatProfile.Default"/>.
    /// </summary>
    public static FormatProfile LoadProfile(string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath)); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, ProfileFileName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(candidate);
            }
            catch (IOException)
            {
                return FormatProfile.Default;
            }
            catch (UnauthorizedAccessException)
            {
                return FormatProfile.Default;
            }

            return FormatProfile.Parse(json, candidate) ?? FormatProfile.Default;
        }

        return FormatProfile.Default;
    }

    /// <summary>Applies sorted, non-overlapping <paramref name="edits"/> to <paramref name="text"/>.</summary>
    internal static string Apply(string text, ImmutableArray<TextEdit> edits)
    {
        var sb = new System.Text.StringBuilder(text.Length + 64);
        var pos = 0;
        foreach (var edit in edits)
        {
            sb.Append(text, pos, edit.Span.Start - pos).Append(edit.NewText);
            pos = edit.Span.End;
        }

        return sb.Append(text, pos, text.Length - pos).ToString();
    }

    private static FormatResult Refuse(string reason) => new(true, reason, []);

    /// <summary>CMP-1438: a lexical, directive or syntax error (E1xxx).</summary>
    private static bool IsSyntaxError(Diagnostic d) => d.Severity == DiagnosticSeverity.Error && d.Code.StartsWith("E1", StringComparison.Ordinal);

    /// <summary>
    /// CMP-1437, checked on the result: the same tokens (text compared ignoring case, string literals exactly) and the same
    /// comments, directives and disabled text, in the same order relative to the tokens.
    /// </summary>
    private static bool TokenStreamsMatch(SyntaxToken[] original, SyntaxTree formatted) =>
        !formatted.Diagnostics.Any(IsSyntaxError)
        && Stream(original).SequenceEqual(Stream(formatted.Root.OwnTokens().Where(t => !t.IsMissing)));

    /// <summary>The tokens (other than string literals in upper case) and the kept trivia, in order.</summary>
    private static IEnumerable<(SyntaxKind Kind, string Text)> Stream(IEnumerable<SyntaxToken> tokens)
    {
        foreach (var token in tokens)
        {
            foreach (var t in Kept(token.LeadingTrivia))
            {
                yield return t;
            }

            yield return (token.Kind, token.Kind == SyntaxKind.StringLiteralToken ? token.Text : token.Text.ToUpperInvariant());
            foreach (var t in Kept(token.TrailingTrivia))
            {
                yield return t;
            }
        }
    }

    /// <summary>Comments, directives and the core of disabled text (without the whitespace the formatter may change).</summary>
    private static IEnumerable<(SyntaxKind Kind, string Text)> Kept(ImmutableArray<SyntaxTrivia> trivia)
    {
        foreach (var t in trivia)
        {
            switch (t.Kind)
            {
                case SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia:
                    break;
                case SyntaxKind.DisabledTextTrivia:
                    var core = LayoutWriter.DisabledCore(t.Text);
                    if (core.Length > 0)
                    {
                        yield return (t.Kind, core);
                    }

                    break;
                default:
                    yield return (t.Kind, t.Text);
                    break;
            }
        }
    }
}
