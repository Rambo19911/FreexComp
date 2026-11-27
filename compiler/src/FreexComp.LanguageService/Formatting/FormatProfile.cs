using System.Collections.Immutable;
using System.Text.Json;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Formatting;

/// <summary>Where a <c>begin</c> that opens a controlled statement goes (CMP-1441).</summary>
public enum BeginStyle
{
    /// <summary><c>begin</c> starts its own line (default).</summary>
    NextLine,

    /// <summary><c>begin</c> stays after <c>then</c> / <c>else</c> / <c>do</c> / a case label's <c>:</c>.</summary>
    SameLine,
}

/// <summary>Letter case of keywords or directive names (CMP-1448).</summary>
public enum KeywordCase
{
    Lower,
    Upper,
    AsIs,
}

/// <summary>Layout of <c>uses</c> clauses (CMP-1450).</summary>
public enum UsesWrap
{
    OnePerLine,
    Fill,
    AsIs,
}

/// <summary>Replace <see cref="Span"/> of the formatted text with <see cref="NewText"/>.</summary>
public readonly record struct TextEdit(TextSpan Span, string NewText);

/// <summary>The outcome of <see cref="PascalFormatter.Format"/>: refused (with the reason) or the edits, sorted and non-overlapping.</summary>
public sealed record FormatResult(bool Refused, string? RefusalReason, ImmutableArray<TextEdit> Edits);

/// <summary>
/// The style options of the formatter (formatter.md §1–§3). <see cref="SourcePath"/> is the <c>.pasformat</c> file the profile
/// was read from, null for the built-in default.
/// </summary>
public sealed record FormatProfile(int IndentSize, BeginStyle Begin, KeywordCase Keywords, KeywordCase Directives,
    bool IdentifiersAsDeclared, int MaxLineLength, UsesWrap Uses, int MaxBlankLines, bool SpaceAroundOperators,
    bool SpaceAfterComma, bool SpaceAroundAssign, bool SpaceBeforeColonInDecl, string? SourcePath)
{
    /// <summary>The built-in style: the defaults of formatter.md §2 and §3.</summary>
    public static FormatProfile Default { get; } = new(
        IndentSize: 2, Begin: BeginStyle.NextLine, Keywords: KeywordCase.Lower, Directives: KeywordCase.Upper,
        IdentifiersAsDeclared: true, MaxLineLength: 120, Uses: UsesWrap.OnePerLine, MaxBlankLines: 1,
        SpaceAroundOperators: true, SpaceAfterComma: true, SpaceAroundAssign: true, SpaceBeforeColonInDecl: false, SourcePath: null);

    /// <summary>
    /// CMP-1452: a <c>.pasformat</c> JSON object; an unknown key is ignored, a wrong value keeps the default of its key; text that
    /// is not a JSON object gives null.
    /// </summary>
    internal static FormatProfile? Parse(string json, string? sourcePath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var p = Default with { SourcePath = sourcePath };
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var v = property.Value;
                p = property.Name switch
                {
                    "indentSize" when Int(v) is { } n && n is >= 1 and <= 8 => p with { IndentSize = n },
                    "begin" when Str(v) is "nextLine" => p with { Begin = BeginStyle.NextLine },
                    "begin" when Str(v) is "sameLine" => p with { Begin = BeginStyle.SameLine },
                    "keywordCase" when Case(v) is { } c => p with { Keywords = c },
                    "directiveCase" when Case(v) is { } c => p with { Directives = c },
                    "identifiersAsDeclared" when Bool(v) is { } b => p with { IdentifiersAsDeclared = b },
                    "maxLineLength" when Int(v) is { } n && (n == 0 || n is >= 40 and <= 400) => p with { MaxLineLength = n },
                    "uses" when Str(v) is "onePerLine" => p with { Uses = UsesWrap.OnePerLine },
                    "uses" when Str(v) is "fill" => p with { Uses = UsesWrap.Fill },
                    "uses" when Str(v) is "asIs" => p with { Uses = UsesWrap.AsIs },
                    "maxBlankLines" when Int(v) is { } n && n is >= 0 and <= 5 => p with { MaxBlankLines = n },
                    "spaceAroundOperators" when Bool(v) is { } b => p with { SpaceAroundOperators = b },
                    "spaceAfterComma" when Bool(v) is { } b => p with { SpaceAfterComma = b },
                    "spaceAroundAssign" when Bool(v) is { } b => p with { SpaceAroundAssign = b },
                    "spaceBeforeColonInDecl" when Bool(v) is { } b => p with { SpaceBeforeColonInDecl = b },
                    _ => p,
                };
            }

            return p;
        }
    }

    private static int? Int(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string? Str(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static KeywordCase? Case(JsonElement v) => Str(v) switch
    {
        "lower" => KeywordCase.Lower,
        "upper" => KeywordCase.Upper,
        "asIs" => KeywordCase.AsIs,
        _ => null,
    };
}
