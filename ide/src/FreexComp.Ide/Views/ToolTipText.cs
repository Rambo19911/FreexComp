using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace FreexComp.Ide.Views;

/// <summary>
/// The content template of every <c>ToolTip</c> (<c>App.axaml</c>): a tip that is a control is shown as it is (the
/// editor's error and fold tooltips), any other tip as <see cref="ToolTipText"/> (I-246: a template for strings only
/// replaced a control by its type name).
/// </summary>
public sealed class ToolTipTemplate : Avalonia.Controls.Templates.IDataTemplate
{
    public bool Match(object? data) => true;

    public Control? Build(object? param) => param as Control ?? new ToolTipText { Source = param?.ToString() };
}

/// <summary>
/// The text of every tooltip whose tip is a string (built by <see cref="ToolTipTemplate"/>). It wraps
/// inside the tooltip's maximum width (design-tokens.md §8) like a text block, except that a line that is a full path
/// (UI-TW-111, UI-ED-075, UI-TW-016, UI-MENU-009 …) is broken only after a path separator, which stays at the end of its
/// line — never after the drive's colon or inside a name (I-245). The tip itself stays the unchanged string.
/// </summary>
public sealed class ToolTipText : TextBlock
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<ToolTipText, string?>(nameof(Source));

    private double _brokenFor = double.NaN;

    public ToolTipText() => TextWrapping = TextWrapping.Wrap;

    /// <summary>The tip as given; <see cref="TextBlock.Text"/> is this text with the line breaks of its paths.</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _brokenFor = double.NaN;
            Text = Source;
        }
        else if (change.Property == FontFamilyProperty || change.Property == FontSizeProperty || change.Property == FontWeightProperty
                 || change.Property == FontStyleProperty || change.Property == FontStretchProperty)
        {
            _brokenFor = double.NaN;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width - Padding.Left - Padding.Right;
        if (Source is { Length: > 0 } source && width != _brokenFor)
        {
            _brokenFor = width;
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            Text = BreakPaths(source, width, line => new TextLayout(line, typeface, FontSize, null).WidthIncludingTrailingWhitespace);
        }

        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    /// <paramref name="text"/> with a line break after a path separator wherever a line that is a full path would
    /// become wider than <paramref name="maxWidth"/>: each line takes as many whole folders as fit. Other lines are
    /// unchanged. A single name wider than the limit stays on a line of its own.
    /// </summary>
    internal static string BreakPaths(string text, double maxWidth, Func<string, double> measure)
    {
        if (double.IsInfinity(maxWidth) || maxWidth <= 0)
            return text;
        var result = new StringBuilder(text.Length + 8);
        foreach (var line in text.Split('\n'))
        {
            if (result.Length > 0)
                result.Append('\n');
            if (!Path.IsPathFullyQualified(line.TrimEnd('\r')) || measure(line) <= maxWidth)
            {
                result.Append(line);
                continue;
            }

            var current = "";
            var start = 0;
            while (start < line.Length)
            {
                var end = line.IndexOfAny(['\\', '/'], start);
                var segment = end < 0 ? line[start..] : line[start..(end + 1)];
                if (current.Length > 0 && measure(current + segment) > maxWidth)
                {
                    result.Append(current).Append('\n');
                    current = "";
                }

                current += segment;
                start += segment.Length;
            }

            result.Append(current);
        }

        return result.ToString();
    }
}
