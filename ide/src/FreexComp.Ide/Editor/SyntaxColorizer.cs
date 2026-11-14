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
/// get the brush and style of their colour token (<c>syntax.keyword</c> → <c>FxcSyntaxKeywordBrush</c> of the current
/// theme, style from <c>colors.&lt;theme&gt;.&lt;token&gt;.style</c>; design-tokens.md §3.1, settings.md §4.3). Unclassified
/// text keeps the editor foreground (<c>syntax.text</c>). Looks are cached; <see cref="ThemeChanged"/> drops them.
/// </summary>
public sealed class SyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly Dictionary<string, string> Styles =
        SettingsCatalog.EditorColorTokens.Where(t => t.Style is not null).ToDictionary(t => t.Token, t => t.Style!, StringComparer.Ordinal);

    private readonly ITextBuffer _buffer;
    private readonly ITokenClassifier _classifier;
    private readonly Func<string, (IBrush? Brush, string Style)> _lookOf;
    private readonly Dictionary<string, (IBrush? Brush, FontWeight Weight, FontStyle Style)> _looks = new(StringComparer.Ordinal);

    /// <summary>Brushes from <paramref name="themeSource"/>'s theme resources; styles from <paramref name="styleOf"/> (dark?, token), else the catalog default.</summary>
    public SyntaxColorizer(ITextBuffer buffer, ITokenClassifier classifier, Control themeSource, Func<bool, string, string>? styleOf = null)
        : this(buffer, classifier, token =>
        {
            var dark = themeSource.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                       || themeSource.ActualThemeVariant.InheritVariant == Avalonia.Styling.ThemeVariant.Dark;
            var brush = themeSource.TryFindResource(IconCatalog.BrushKey(token), themeSource.ActualThemeVariant, out var value) ? value as IBrush : null;
            return (brush, styleOf?.Invoke(dark, token) ?? Styles.GetValueOrDefault(token, "regular"));
        })
    {
    }

    /// <summary>Any source of looks (the Colors options preview: the selected theme with pending overrides).</summary>
    public SyntaxColorizer(ITextBuffer buffer, ITokenClassifier classifier, Func<string, (IBrush? Brush, string Style)> lookOf)
    {
        _buffer = buffer;
        _classifier = classifier;
        _lookOf = lookOf;
    }

    public ITokenClassifier Classifier => _classifier;

    /// <summary>The theme, a colour or a style changed: looks are computed again on the next draw.</summary>
    public void ThemeChanged() => _looks.Clear();

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var span in _classifier.Classify(_buffer, new LineRange(line.LineNumber, line.LineNumber)))
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
        var (brush, style) = _lookOf(token);
        // "bold" is the token's semi-bold (600, design-tokens.md §3.1).
        look = (brush,
            style is "bold" or "bolditalic" ? FontWeight.SemiBold : FontWeight.Normal,
            style is "italic" or "bolditalic" ? FontStyle.Italic : FontStyle.Normal);
        _looks[token] = look;
        return look;
    }
}
