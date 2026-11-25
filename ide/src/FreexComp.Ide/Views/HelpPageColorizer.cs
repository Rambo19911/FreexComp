using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Views;

/// <summary>
/// UI-MENU-032: the line styles of a help document over its body text (<c>type.body</c>). Heading lines are
/// <c>type.bodyStrong</c> in <c>text.primary</c>; the lines of an aligned table use <c>type.code</c> so that their
/// columns line up, the header row strong. The kind of every line comes from <see cref="HelpPageFormatter"/>.
/// </summary>
internal sealed class HelpPageColorizer(
    Func<IReadOnlyList<HelpLineKind>?> lines, Func<IBrush?> headingBrush, Func<FontFamily?> codeFont, Func<double> codeSize)
    : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || lines() is not { } kinds || line.LineNumber > kinds.Count)
            return;
        var kind = kinds[line.LineNumber - 1];
        if (kind == HelpLineKind.Text)
            return;
        var table = kind is HelpLineKind.TableHeader or HelpLineKind.TableRow;
        var strong = kind is HelpLineKind.Heading or HelpLineKind.TableHeader;
        var foreground = kind == HelpLineKind.Heading ? headingBrush() : null;
        var family = table ? codeFont() : null;
        ChangeLinePart(line.Offset, line.EndOffset, element =>
        {
            var face = element.TextRunProperties.Typeface;
            element.TextRunProperties.SetTypeface(
                new Typeface(family ?? face.FontFamily, face.Style, strong ? FontWeight.SemiBold : face.Weight, face.Stretch));
            if (table)
                element.TextRunProperties.SetFontRenderingEmSize(codeSize());
            if (foreground is not null)
                element.TextRunProperties.SetForegroundBrush(foreground);
        });
    }
}
