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
        var popup = editor.FindAncestorOfType<CodeEditorView>()!.CompletionList;
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
        var popup = view.CompletionList;
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
        var lineTopOnScreen = textView.PointToScreen(new Avalonia.Point(0, lineTop)).Y;
        var height = popup.Bounds.Height;
        Assert.True(height > 0);
        var popupTop = popup.PointToScreen(default).Y;
        Assert.Equal(lineTopOnScreen - 2 - height, popupTop, 1.0); // directly above the line, gap 2
        Assert.True(popupTop >= view.PointToScreen(default).Y);
    }

    /// <summary>
    /// UI-ED-117 (I-198): 300 ms after a row is selected the documentation side panel shows the declaration and the doc
    /// comment to the right of the list, as high as the list (min 110), width 300; UI-ED-113: every row starts with its
    /// symbol icon (16); UI-ED-115: the matched characters are highlighted.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-113")]
    [Trait("Req", "UI-ED-115")]
    [Trait("Req", "UI-ED-117")]
    public async Task DocumentationPanelIconsAndHighlight()
    {
        await using var h = await BuildScenario.StartAsync(LiveDiagnosticsTests.AddRealRtl);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        var var = doc.Buffer.Text.IndexOf("var Foo1", StringComparison.Ordinal);
        doc.Buffer.ApplyEdits([new TextEdit(var + 4, 0, "\r\n  /// Counts the runs.\r\n  ")]);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var list = view.CompletionList;
        var session = doc.Completion!;
        var foo = doc.Buffer.Text.IndexOf("Foo :=", StringComparison.Ordinal);
        doc.Selection = TextSelection.At(foo + 2);
        h.Press("Ctrl+Space");
        await session.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(list.IsVisible);
        Assert.Equal("Foo1", session.SelectedItem!.Label);

        // UI-ED-113: icon (16) · name (highlighted, UI-ED-115) · detail.
        var row = h.Find<Avalonia.Controls.Border>("Editor.Completion.Item.Foo1");
        var cells = ((Avalonia.Controls.DockPanel)row.Child!).Children;
        var icon = Assert.IsType<SymbolKindIcon>(cells[0]);
        Assert.Equal(FreexComp.LanguageService.CompletionItemKind.Variable, icon.Kind);
        Assert.Equal(16, icon.Bounds.Width);
        Assert.False(string.IsNullOrEmpty(icon.Glyph));
        var name = cells.OfType<Avalonia.Controls.TextBlock>().First();
        var runs = name.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Equal("Fo", string.Concat(runs.Where(r => r.Classes.Contains(CompletionListView.MatchClass)).Select(r => r.Text)));
        Assert.All(h.FindAll("Editor.Completion.Item."), r =>
            Assert.Single(((Avalonia.Controls.DockPanel)((Avalonia.Controls.Border)r).Child!).Children.OfType<SymbolKindIcon>()));

        // UI-ED-117: nothing before 300 ms; then the declaration and the doc comment, right of the list.
        var panel = view.CompletionDoc;
        h.Clock.Advance(TimeSpan.FromMilliseconds(299));
        await session.DescriptionPending;
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.IsVisible);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await session.DescriptionPending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsVisible);
        Assert.Same(panel, h.Find<CompletionDocView>("Editor.Completion.Doc"));
        Assert.Contains("Foo1", panel.Declaration, StringComparison.Ordinal);
        Assert.Contains("Integer", panel.Declaration, StringComparison.Ordinal);
        Assert.Equal("Counts the runs.", panel.Documentation);
        Assert.Equal(300, panel.Bounds.Width);
        Assert.Equal(Math.Max(110, list.Bounds.Height), panel.Bounds.Height, 0.5);
        Assert.Equal(list.PointToScreen(new Avalonia.Point(list.Bounds.Width, 0)), panel.PointToScreen(default));
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);

        // Another row: the panel goes at once; back on Foo1 it returns 300 ms later.
        h.Press("Down");
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.IsVisible);
        h.Press("Up");
        h.Clock.Advance(TimeSpan.FromMilliseconds(300));
        await session.DescriptionPending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsVisible);

        // Escape closes both.
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(list.IsVisible);
        Assert.False(panel.IsVisible);
    }

    /// <summary>UI-ED-110 (I-198): the popup is a real popup: in a short editor the full list extends past the editor's bottom edge.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-110")]
    [Trait("Req", "UI-ED-112")]
    public async Task ExtendsBeyondTheEditor()
    {
        await using var h = await BuildScenario.StartAsync(LiveDiagnosticsTests.AddRealRtl);
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        view.Height = 140; // room for the code, not for a list of 10 rows below or above the caret line
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.Focus());
        var list = view.CompletionList;
        var session = doc.Completion!;
        var foo = doc.Buffer.Text.IndexOf("Foo :=", StringComparison.Ordinal);
        doc.Selection = TextSelection.At(foo); // nothing typed: the whole context set
        h.Press("Ctrl+Space");
        await session.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(list.IsVisible);
        // A popup host, not the editor (headless: the window's overlay layer; on Windows a popup window).
        Assert.Null(list.FindAncestorOfType<CodeEditorView>());
        Assert.Contains(list.GetVisualAncestors(), a => a is Avalonia.Controls.Primitives.OverlayPopupHost or Avalonia.Controls.Primitives.PopupRoot);
        Assert.True(session.Items.Count >= CompletionListView.VisibleRows);
        Assert.Equal(CompletionListView.RowHeight * CompletionListView.VisibleRows + 2, list.Bounds.Height, 0.5); // not squeezed
        var textView = editor.TextArea.TextView;
        var line = doc.Buffer.GetPosition(foo).Line;
        var lineBottom = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(line, 1), AvaloniaEdit.Rendering.VisualYPosition.LineBottom).Y
            - textView.ScrollOffset.Y;
        var top = list.PointToScreen(default);
        Assert.Equal(textView.PointToScreen(new Avalonia.Point(0, lineBottom)).Y + 2, top.Y, 1.0); // below the line (no room above either)
        Assert.True(top.Y + list.Bounds.Height > view.PointToScreen(new Avalonia.Point(0, view.Bounds.Height)).Y); // past the editor
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(list.IsVisible);
    }
}
