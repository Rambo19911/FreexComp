using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DocumentTabTests
{
    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string relativePath)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, relativePath));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private static IReadOnlyList<string?> Texts(Control tab) =>
        [.. tab.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-072")]
    [Trait("Req", "UI-ED-073")]
    [Trait("Req", "UI-ED-075")]
    public async Task ModifiedDotReplacesCloseGlyph()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;";
            o.InitialFiles["B.pas"] = "unit B;";
        });
        var a = await OpenAsync(h, "A.pas");
        await OpenAsync(h, "B.pas");

        // Unmodified: the inactive tab shows its × only on hover, the active one always.
        Assert.False(h.Find<Control>("DocumentTab.A.pas.Close").IsVisible);
        Assert.True(h.Find<Control>("DocumentTab.B.pas.Close").IsVisible);
        Assert.Null(h.TryFind<Control>("DocumentTab.A.pas.Modified"));
        Assert.Equal(Path.Combine(h.TempDir, "A.pas"), ToolTip.GetTip(h.Find<Control>("DocumentTab.A.pas")));

        // Modified: a dot in the close slot instead of the ×.
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Dispatcher.UIThread.RunJobs();
        var dot = h.Find<Control>("DocumentTab.A.pas.Modified");
        Assert.True(dot.IsVisible);
        Assert.Equal(Strings.DocumentTab_Modified, AutomationProperties.GetName(dot));
        Assert.False(h.Find<Control>("DocumentTab.A.pas.Close").IsVisible);

        // Hovering the tab shows the × again.
        h.Hover("DocumentTab.A.pas");
        Assert.False(dot.IsVisible);
        Assert.True(h.Find<Control>("DocumentTab.A.pas.Close").IsVisible);
        h.Hover("DocumentTab.B.pas");
        Assert.True(dot.IsVisible);

        // Undo back to the save point: the dot is gone.
        a.Buffer.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>("DocumentTab.A.pas.Modified"));

        // The close button closes; a modified document asks first (IDocumentService.CloseAsync).
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Dispatcher.UIThread.RunJobs();
        h.Messages.Answer("Msg.File.SaveChangesOne", "DontSave");
        h.Hover("DocumentTab.A.pas");
        h.Click("DocumentTab.A.pas.Close");
        await h.WaitForAsync(() => h.TryFind<Control>("DocumentTab.A.pas") is null, "tab A closed");
        Assert.Equal("Msg.File.SaveChangesOne", Assert.Single(h.Messages.Requests).MessageId);
        h.Click("DocumentTab.B.pas.Close");
        await h.WaitForAsync(() => h.Services.Documents.Documents.Count == 0, "tab B closed");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-071")]
    [Trait("Req", "UI-ED-074")]
    [Trait("Req", "UI-ED-075")]
    public async Task DuplicateNamesGetFolderSuffix()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"lib\Unit1.pas"] = "unit Unit1;";
            o.InitialFiles[@"src\Unit1.pas"] = "unit Unit1;";
        });
        var lib = await OpenAsync(h, @"lib\Unit1.pas");
        Assert.Equal(["Unit1.pas"], Texts(h.Find<Control>("DocumentTab.Unit1.pas")));

        // Same names: both get the distinguishing folder; the second one's id gets @2 (open order).
        await OpenAsync(h, @"src\Unit1.pas");
        Assert.Equal(["Unit1.pas", "[lib]"], Texts(h.Find<Control>("DocumentTab.Unit1.pas")));
        var second = h.Find<Control>("DocumentTab.Unit1.pas@2");
        Assert.Equal(["Unit1.pas", "[src]"], Texts(second));
        Assert.Equal(Path.Combine(h.TempDir, "src", "Unit1.pas"), ToolTip.GetTip(second));
        Assert.NotNull(h.TryFind<Control>("DocumentTab.Unit1.pas@2.Close"));

        // Closing one: the other is unique again.
        h.Services.Documents.Close(lib);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>("DocumentTab.Unit1.pas@2"));
        Assert.Equal(["Unit1.pas"], Texts(h.Find<Control>("DocumentTab.Unit1.pas")));

        // A read-only file: the tooltip's second line says so.
        File.SetAttributes(Path.Combine(h.TempDir, "lib", "Unit1.pas"), FileAttributes.ReadOnly);
        await OpenAsync(h, @"lib\Unit1.pas");
        Assert.Equal(Path.Combine(h.TempDir, "lib", "Unit1.pas") + Environment.NewLine + Strings.Msg_Editor_Tip_ReadOnly,
            ToolTip.GetTip(h.Find<Control>("DocumentTab.Unit1.pas@2")));
        File.SetAttributes(Path.Combine(h.TempDir, "lib", "Unit1.pas"), FileAttributes.Normal);

        // An untitled document's tooltip is its title.
        h.Services.Documents.NewUntitledUnit();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Unit1.pas", ToolTip.GetTip(h.Find<Control>("DocumentTab.Unit1.pas@3")));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-076")]
    public async Task MiddleClickCloses()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
            o.InitialFiles["C.pas"] = "c";
        });
        var a = await OpenAsync(h, "A.pas");
        var b = await OpenAsync(h, "B.pas");
        await OpenAsync(h, "C.pas");
        var documents = h.Services.Documents;

        // Left click activates.
        h.Click("DocumentTab.A.pas");
        Assert.Same(a, documents.ActiveDocument);

        // Middle click closes, without activating first.
        h.MiddleClick("DocumentTab.C.pas");
        await h.WaitForAsync(() => h.TryFind<Control>("DocumentTab.C.pas") is null, "tab C closed");
        Assert.Same(a, documents.ActiveDocument);

        // A modified document asks; Cancel keeps it open.
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        h.Messages.Answer("Msg.File.SaveChangesOne", "Cancel");
        h.MiddleClick("DocumentTab.B.pas");
        await h.WaitForAsync(() => h.Messages.Requests.Count == 1, "save prompt");
        Assert.Equal([a, b], documents.Documents);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-078")]
    public async Task OverflowMenuListsAllDocuments()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
            o.InitialFiles["C.pas"] = "c";
        });
        Assert.False(h.Find<Button>("DocumentArea.TabOverflow").IsVisible); // nothing open

        var a = await OpenAsync(h, "A.pas");
        var b = await OpenAsync(h, "B.pas");
        await OpenAsync(h, "C.pas");
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Dispatcher.UIThread.RunJobs();
        var overflow = h.Find<Button>("DocumentArea.TabOverflow");
        Assert.True(overflow.IsVisible);

        // Tab order, a check mark on the active one, modified ones prefixed "● ".
        h.Click("DocumentArea.TabOverflow");
        var items = Assert.IsType<ContextMenu>(overflow.ContextMenu).Items.Cast<MenuItem>().ToList();
        Assert.Equal(["A.pas", "● B.pas", "C.pas"], items.Select(i => i.Header as string));
        Assert.Equal([false, false, true], items.Select(i => i.IsChecked));
        Assert.Equal(["DocumentArea.TabOverflow.A.pas", "DocumentArea.TabOverflow.B.pas", "DocumentArea.TabOverflow.C.pas"],
            items.Select(i => AutomationIds.Get(i)));

        // Choosing one runs window.activateDocument.
        items[0].Command!.Execute(items[0].CommandParameter);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument == a, "A active");
        overflow.ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-079")]
    public async Task ContextMenuActsOnClickedTab()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
        });
        var a = await OpenAsync(h, "A.pas");
        await OpenAsync(h, "B.pas");

        // The right-clicked tab is activated first, so the commands act on it.
        h.RightClick("DocumentTab.A.pas");
        Assert.Same(a, h.Services.Documents.ActiveDocument);
        var menu = Assert.IsType<ContextMenu>(h.Find<Control>("DocumentTab.A.pas").ContextMenu);
        Assert.Equal(
            ["Context.file.close", "Context.file.closeOthers", "Context.file.closeAll", null, "Context.file.save", "Context.file.saveAs",
             "Context.file.revert", null, "Context.file.copyPath", "Context.file.showInExplorer", null, "Context.window.splitRight",
             "Context.window.splitDown"],
            menu.Items.Cast<Control>().Select(c => c is Separator ? null : AutomationIds.Get(c)));
        menu.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-038")]
    public async Task CtrlTabUsesMruOrder()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "a";
            o.InitialFiles["B.pas"] = "b";
            o.InitialFiles["C.pas"] = "c";
        });
        var a = await OpenAsync(h, "A.pas");
        var b = await OpenAsync(h, "B.pas");
        var c = await OpenAsync(h, "C.pas");
        var documents = h.Services.Documents;
        documents.Activate(a);                              // MRU: A, C, B; tabs: A, B, C

        h.Press("Ctrl+Tab");                                // MRU: the previously used one
        Assert.Same(c, documents.ActiveDocument);
        h.Press("Ctrl+Tab");
        Assert.Same(a, documents.ActiveDocument);
        h.Press("Ctrl+PageDown");                           // tab order
        Assert.Same(b, documents.ActiveDocument);
        h.Press("Ctrl+PageUp");
        Assert.Same(a, documents.ActiveDocument);
        h.Press("Ctrl+Shift+Tab");                          // MRU: the least recently used one
        Assert.Same(c, documents.ActiveDocument);

        // The active document's view is shown in the document area.
        Assert.Same(c, h.Find<Control>("DocumentArea").GetVisualDescendants().OfType<FreexComp.Ide.Views.TextDocumentView>().Single().DataContext);
    }
}
