using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Editor;

/// <summary>
/// Syntax colours (UI-ED-060, IDE-P1-057): for each drawn line, the document's <see cref="ITokenClassifier"/> spans
/// get the brush of their colour token (<c>syntax.keyword</c> → <c>FxcSyntaxKeywordBrush</c> of the current theme)
/// and the token's weight/style (design-tokens.md §3.1). Unclassified text keeps the editor foreground
/// (<c>syntax.text</c>). Brushes are looked up on <paramref name="themeSource"/>; <see cref="ThemeChanged"/> drops them.
/// </summary>
public sealed class SyntaxColorizer(ITextBuffer buffer, ITokenClassifier classifier, Control themeSource) : DocumentColorizingTransformer
{
    private static readonly Dictionary<string, string> Styles =
        SettingsCatalog.EditorColorTokens.Where(t => t.Style is not null).ToDictionary(t => t.Token, t => t.Style!, StringComparer.Ordinal);

    private readonly Dictionary<string, (IBrush? Brush, FontWeight Weight, FontStyle Style)> _looks = new(StringComparer.Ordinal);

    public ITokenClassifier Classifier => classifier;

    /// <summary>The theme changed: brushes are looked up again on the next draw.</summary>
    public void ThemeChanged() => _looks.Clear();

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var span in classifier.Classify(buffer, new LineRange(line.LineNumber, line.LineNumber)))
        {
            int start = Math.Max(span.Offset, line.Offset);
            int end = Math.Min(span.Offset + span.Length, line.EndOffset);
            if (start >= end)
                continue;
            var look = Look(span.TokenClass);
            ChangeLinePart(start, end, element =>
            {
                var run = element.TextRunProperties;
                if (look.Brush is not null)
                    run.SetForegroundBrush(look.Brush);
                if (look.Weight != run.Typeface.Weight || look.Style != run.Typeface.Style)
                    run.SetTypeface(new Typeface(run.Typeface.FontFamily, look.Style, look.Weight, run.Typeface.Stretch));
            });
        }
    }

    private (IBrush? Brush, FontWeight Weight, FontStyle Style) Look(string token)
    {
        if (_looks.TryGetValue(token, out var look))
            return look;
        var brush = themeSource.TryFindResource(IconCatalog.BrushKey(token), themeSource.ActualThemeVariant, out var value) ? value as IBrush : null;
        // Colour/style overrides from settings join with I-078; "bold" is the token's semi-bold (600).
        var style = Styles.GetValueOrDefault(token, "regular");
        look = (brush,
            style is "bold" or "bolditalic" ? FontWeight.SemiBold : FontWeight.Normal,
            style is "italic" or "bolditalic" ? FontStyle.Italic : FontStyle.Normal);
        _looks[token] = look;
        return look;
    }
}
