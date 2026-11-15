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
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsVisible);
        Assert.Contains("  Foo1o :=", doc.Buffer.Text, StringComparison.Ordinal);
    }
}
