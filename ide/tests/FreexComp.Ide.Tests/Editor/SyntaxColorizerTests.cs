using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Syntax colours in the code editor (I-044, UI-ED-060, IDE-P1-057).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class SyntaxColorizerTests
{
    internal static async Task<TextEditor> OpenAsync(HeadlessHarness h, string fileName)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, fileName));
        Assert.True(result.Success, result.MessageId);
        Assert.IsType<TextDocumentViewModel>(result.Value);
        Dispatcher.UIThread.RunJobs();
        return h.Find<TextEditor>("Editor." + fileName);
    }

    /// <summary>The run properties the editor draws the character at <paramref name="offset"/> with.</summary>
    internal static VisualLineElementTextRunProperties RunAt(TextEditor editor, int offset)
    {
        var line = editor.Document.GetLineByOffset(offset);
        var visual = editor.TextArea.TextView.GetOrConstructVisualLine(line);
        var relative = offset - line.Offset;
        return visual.Elements.Last(e => e.RelativeTextOffset <= relative).TextRunProperties;
    }

    internal static Color ColorAt(TextEditor editor, int offset) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(RunAt(editor, offset).ForegroundBrush).Color;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-060")]
    [Trait("Req", "IDE-P1-057")]
    public async Task AppliesTokenBrushesAndStyles()
    {
        const string text = "begin { note } X := 'abc'; end.\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = text;
        });
        var editor = await OpenAsync(h, "A.pas");

        // design-tokens.md §3.1 (light): keyword semi-bold, comment italic, string and text regular.
        Assert.Equal(Color.Parse("#0B5C8A"), ColorAt(editor, text.IndexOf("begin", StringComparison.Ordinal)));
        Assert.Equal(FontWeight.SemiBold, RunAt(editor, 0).Typeface.Weight);
        var comment = text.IndexOf('{');
        Assert.Equal(Color.Parse("#5E6E63"), ColorAt(editor, comment));
        Assert.Equal(FontStyle.Italic, RunAt(editor, comment).Typeface.Style);
        var str = text.IndexOf('\'');
        Assert.Equal(Color.Parse("#A0431A"), ColorAt(editor, str));
        Assert.Equal(FontWeight.Normal, RunAt(editor, str).Typeface.Weight);
        Assert.Equal(FontStyle.Normal, RunAt(editor, str).Typeface.Style);
        Assert.Equal(Color.Parse("#1C211E"), ColorAt(editor, text.IndexOf('X'))); // unclassified: syntax.text

        // An edit recolours: "begin" → "beginx" is an identifier.
        editor.Document.Insert(5, "x");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#1C211E"), ColorAt(editor, 0));
        Assert.Equal(FontWeight.Normal, RunAt(editor, 0).Typeface.Weight);
    }
}
