using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The completion popup (I-115, editor.md §11.1): opened with Ctrl+Space, driven from the editor's keys.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class CompletionViewTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-110")]
    [Trait("Req", "UI-ED-116")]
    [Trait("Req", "UI-ED-118")]
    [Trait("Req", "UI-ED-115")]
    public async Task PopupKeyboardFlow()
    {
        await using var h = await BuildScenario.StartAsync(LiveDiagnosticsTests.AddRealRtl);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        var popup = editor.FindAncestorOfType<CodeEditorView>()!.GetVisualDescendants().OfType<CompletionListView>().Single();
        var session = doc.Completion!;
        var foo = doc.Buffer.Text.IndexOf("Foo :=", StringComparison.Ordinal);

        async Task OpenAsync()
        {
            h.Press("Ctrl+Space");
            await session.Pending;
            Dispatcher.UIThread.RunJobs();
        }

        // Ctrl+Space after "Fo": the popup with Foo1 selected; the editor keeps focus.
        doc.Selection = TextSelection.At(foo + 2);
        await OpenAsync();
        Assert.True(popup.IsVisible);
        Assert.Same(popup, h.Find<CompletionListView>("Editor.Completion"));
        Assert.Contains("Foo1", popup.Labels);
        Assert.NotNull(h.Find<Avalonia.Controls.Control>("Editor.Completion.Item.Foo1"));
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);

        // Down / Up move the selection; Escape closes without inserting.
        var count = session.Items.Count;
        h.Press("Down");
        Assert.Equal(Math.Min(1, count - 1), session.SelectedIndex);
        h.Press("Up");
        Assert.Equal(0, session.SelectedIndex);
        var before = doc.Buffer.Text;
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsVisible);
        Assert.Equal(before, doc.Buffer.Text);

        // Typing filters; Enter accepts the selected item in place of the typed word (the rest of the word stays).
        await OpenAsync();
        h.Type("o1");
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsVisible);
        Assert.Equal("Foo1", session.SelectedItem!.Label);

        // UI-ED-115: the matched characters of the name are highlighted (here the typed prefix).
        var row = h.Find<Avalonia.Controls.Border>("Editor.Completion.Item.Foo1");
        var name = ((Avalonia.Controls.DockPanel)row.Child!).Children.OfType<Avalonia.Controls.TextBlock>().First();
        var runs = name.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Equal("Foo1", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(session.TypedText, string.Concat(runs.Where(r => r.Classes.Contains(CompletionListView.MatchClass)).Select(r => r.Text)));
        Assert.All(runs.Where(r => r.Classes.Contains(CompletionListView.MatchClass)), r => Assert.Equal(Avalonia.Media.FontWeight.SemiBold, r.FontWeight));
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsVisible);
        Assert.Contains("  Foo1o :=", doc.Buffer.Text, StringComparison.Ordinal);
    }

    /// <summary>UI-ED-111 (I-198): without room below the caret line the popup opens above it, never covering the line.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-111")]
    public async Task FlipsAboveNearTheBottom()
    {
        await using var h = await BuildScenario.StartAsync(LiveDiagnosticsTests.AddRealRtl);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        var begin = doc.Buffer.Text.IndexOf("begin", StringComparison.Ordinal);
        doc.Buffer.ApplyEdits([new TextEdit(begin, 0, string.Concat(Enumerable.Repeat("\r\n", 200)))]); // blank lines: the code moves down
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var popup = view.GetVisualDescendants().OfType<CompletionListView>().Single();
        var session = doc.Completion!;
        var foo = doc.Buffer.Text.IndexOf("Foo :=", StringComparison.Ordinal);
        var line = doc.Buffer.GetPosition(foo).Line;

        // Scroll so that the caret line is the last fully visible line of the view.
        var textView = editor.TextArea.TextView;
        var scroller = editor.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First();
        var lineHeight = textView.DefaultLineHeight;
        scroller.Offset = new Avalonia.Vector(0, line * lineHeight - scroller.Viewport.Height);
        doc.Selection = TextSelection.At(foo + 2);
        Dispatcher.UIThread.RunJobs();

        h.Press("Ctrl+Space");
        await session.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsVisible);
        var lineTop = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(line, 1), AvaloniaEdit.Rendering.VisualYPosition.LineTop).Y
            - textView.ScrollOffset.Y;
        var lineTopInView = textView.TranslatePoint(new Avalonia.Point(0, lineTop), view)!.Value.Y;
        var height = popup.Bounds.Height;
        Assert.True(height > 0);
        Assert.Equal(lineTopInView - 2 - height, popup.Margin.Top, 0.5); // directly above the line, gap 2
        Assert.True(popup.Margin.Top >= 0);
    }
}
