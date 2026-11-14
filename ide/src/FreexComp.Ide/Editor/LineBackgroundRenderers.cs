using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Editor;

/// <summary>Brushes of colour tokens from the current theme of a control.</summary>
internal static class ThemeBrushes
{
    public static IBrush? Find(Control themeSource, string token) =>
        themeSource.TryFindResource(IconCatalog.BrushKey(token), themeSource.ActualThemeVariant, out var value) ? value as IBrush : null;
}

/// <summary>
/// Current-line highlight (UI-ED-050), replacing AvaloniaEdit's built-in one: the caret's visual line gets
/// <c>editor.currentLine.bg</c> across the text area, plus 1 px <c>editor.currentLine.border</c> lines at its top and
/// bottom while the editor has keyboard focus. Not drawn while a selection spans more than one line, while disabled,
/// or on a line that has a line-state background (UI-ED-020).
/// </summary>
public sealed class CurrentLineRenderer(TextArea area, Control themeSource) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary><c>editor.highlightCurrentLine</c>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>The document's decorations (lines with a line-state background are left to that renderer).</summary>
    public EditorDecorations? Decorations { get; set; }

    /// <summary>The 1-based line that is highlighted, or null.</summary>
    public int? HighlightedLine
    {
        get
        {
            if (!IsEnabled)
                return null;
            var selection = area.Selection;
            if (!selection.IsEmpty && selection.StartPosition.Line != selection.EndPosition.Line)
                return null;
            var line = area.Caret.Line;
            return Decorations?.LineBackgroundAt(line) is null ? line : null;
        }
    }

    /// <summary>The top and bottom border lines are drawn (the editor has keyboard focus).</summary>
    public bool DrawsBorder => area.IsKeyboardFocusWithin;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (HighlightedLine is not { } number || textView.GetVisualLine(number) is not { } line)
            return;
        var rect = new Rect(0, line.VisualTop - textView.VerticalOffset, textView.Bounds.Width, line.Height);
        if (ThemeBrushes.Find(themeSource, "editor.currentLine.bg") is { } background)
            drawingContext.FillRectangle(background, rect);
        if (DrawsBorder && ThemeBrushes.Find(themeSource, "editor.currentLine.border") is { } border)
        {
            drawingContext.FillRectangle(border, new Rect(rect.X, rect.Y, rect.Width, 1));
            drawingContext.FillRectangle(border, new Rect(rect.X, rect.Bottom - 1, rect.Width, 1));
        }
    }
}

/// <summary>Line-state backgrounds (UI-ED-020): breakpoint, execution and call-frame lines across the text area.</summary>
public sealed class LineStateBackgroundRenderer(Control themeSource) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public EditorDecorations? Decorations { get; set; }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Decorations is not { } decorations || !textView.VisualLinesValid)
            return;
        foreach (var line in textView.VisualLines)
        {
            if (decorations.LineBackgroundAt(line.FirstDocumentLine.LineNumber) is { } token && ThemeBrushes.Find(themeSource, token) is { } brush)
                drawingContext.FillRectangle(brush, new Rect(0, line.VisualTop - textView.VerticalOffset, textView.Bounds.Width, line.Height));
        }
    }
}
