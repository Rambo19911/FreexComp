using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Views;

/// <summary>
/// I-257: a help page is shown as its Markdown text; its heading lines (<c>#</c> … <c>######</c> followed by a space)
/// are drawn bold in the keyword colour (<c>syntax.keyword</c>). Nothing else of the text is interpreted.
/// </summary>
internal sealed class HelpHeadingColorizer(Func<IBrush?> brush) : DocumentColorizingTransformer
{
    public static bool IsHeading(string line)
    {
        var marks = 0;
        while (marks < line.Length && line[marks] == '#')
            marks++;
        return marks is >= 1 and <= 6 && marks < line.Length && line[marks] == ' ';
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || !IsHeading(CurrentContext.Document.GetText(line)))
            return;
        var foreground = brush();
        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            var face = element.TextRunProperties.Typeface;
            element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily, face.Style, FontWeight.Bold, face.Stretch));
            if (foreground is not null)
                element.TextRunProperties.SetForegroundBrush(foreground);
        });
    }
}
