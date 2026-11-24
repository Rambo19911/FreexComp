using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The library-source bar and the lock glyph of a protected library document (phase8-ui-additions.md §2, I-232).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class LibrarySourceViewTests
{
    internal const string LibraryUnit = "unit SysUtils;\r\ninterface\r\nfunction UpperCase(const S: string): string;\r\nimplementation\r\nfunction UpperCase(const S: string): string;\r\nbegin\r\n  Result := S;\r\nend;\r\nend.\r\n";

    internal static string[] BarsTopToBottom(HeadlessHarness h) =>
        [.. h.FindAll("InfoBar.")
            .Where(c => c is Ide.Views.InfoBarView && c.IsEffectivelyVisible)
            .OrderBy(c => c.TranslatePoint(default, h.Window)!.Value.Y)
            .Select(c => AutomationProperties.GetAutomationId(c)!)];

    internal static IdeIcon LockOf(HeadlessHarness h, string fileName) =>
        h.Find<Control>(AutomationIds.DocumentTab(fileName)).GetVisualDescendants().OfType<IdeIcon>().Single(i => i.IconName == "lock");

    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string relativePath)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, relativePath));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-144")]
    [Trait("Req", "UI-ED-145")]
    [Trait("Req", "IDE-LIB-003")]
    [Trait("Req", "IDE-LIB-004")]
    public async Task BarAndLockGlyphRender()
    {
        await using var h = await BuildScenario.StartAsync(o =>
        {
            // Mixed line endings: the load shows its own bar; the library bar is above it.
            o.InitialFiles[@"tc\rtl\SysUtils.pas"] = LibraryUnit.Replace("interface\r\n", "interface\n", StringComparison.Ordinal);
            o.InitialFiles[@"tc\rtl\Classes.pas"] = LibraryUnit.Replace("SysUtils", "Classes", StringComparison.Ordinal);
        });
        var document = await OpenAsync(h, @"tc\rtl\SysUtils.pas");
        var editor = h.Find<TextEditor>("Editor.SysUtils.pas");
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();

        // The bar: info severity, its text, the one action, no dismiss button; top-most (UI-ED-144).
        Assert.Equal(["InfoBar.LibrarySource", "InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        var bar = h.Find<Control>("InfoBar.LibrarySource");
        Assert.Equal("info", bar.GetVisualDescendants().OfType<IdeIcon>().Single().IconName);
        Assert.Equal("This file is part of the library source installed with FreexComp. It is protected because a change affects every project that uses it.",
            h.Find<TextBlock>("InfoBar.LibrarySource.Text").Text);
        var action = h.Find<Button>("InfoBar.LibrarySource.Action.EditAnyway");
        Assert.Equal(("Edit Anyway", "Edit Anyway"), (action.Content, AutomationProperties.GetName(action)));
        Assert.Null(h.TryFind<Control>("InfoBar.LibrarySource.Dismiss"));
        Assert.Equal([action], bar.GetVisualDescendants().OfType<Button>());
        Assert.Empty(VisualSnapshot.LayoutDefects(bar));
        Assert.Null(VisualSnapshot.HiddenPart(action, h.Window));

        // The existing read-only presentation: lock glyph on the tab, the tooltip line, typing changes nothing (UI-ED-145).
        var tab = h.Find<Control>(AutomationIds.DocumentTab("SysUtils.pas"));
        Assert.True(LockOf(h, "SysUtils.pas").IsEffectivelyVisible);
        Assert.Equal(document.FilePath + Environment.NewLine + Strings.Msg_Editor_Tip_ReadOnly, ToolTip.GetTip(tab));
        Assert.True(editor.IsReadOnly);
        var text = document.Buffer.Text;
        h.Type("x");
        Assert.Equal(text, document.Buffer.Text);

        // Keyboard: Shift+Tab reaches the action; Enter runs it. Bar and glyph go in the same turn and the keyboard
        // focus is in the text area again; the other bar stays.
        h.Press("Shift+Tab");
        Assert.Equal("InfoBar.LibrarySource.Action.EditAnyway", h.FocusedAutomationId);
        h.Press("Enter");
        Assert.Equal(["InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        Assert.False(LockOf(h, "SysUtils.pas").IsVisible);
        Assert.Equal(document.FilePath, ToolTip.GetTip(tab));
        Assert.Equal("Editor.SysUtils.pas", h.FocusedAutomationId);
        Assert.False(editor.IsReadOnly);
        Assert.False(document.IsLibraryProtected);
        h.Type("x");
        Assert.Equal("x" + text, document.Buffer.Text);

        // The same with the mouse, and with the command, on other protected documents.
        var second = await OpenAsync(h, @"tc\rtl\Classes.pas");
        Assert.Equal(["InfoBar.LibrarySource"], BarsTopToBottom(h));
        Assert.True(LockOf(h, "Classes.pas").IsEffectivelyVisible);
        h.Click("InfoBar.LibrarySource.Action.EditAnyway");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Classes.pas").IsVisible);
        Assert.Equal("Editor.Classes.pas", h.FocusedAutomationId);
        Assert.False(second.IsReadOnly);

        Assert.True(await h.Services.Documents.CloseAsync(second));
        second = await OpenAsync(h, @"tc\rtl\Classes.pas");
        Assert.True(LockOf(h, "Classes.pas").IsEffectivelyVisible); // closing and reopening protects it again
        Assert.True(h.Find<TextEditor>("Editor.Classes.pas").TextArea.Focus());
        await h.ExecuteAsync("file.editLibrarySource");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Classes.pas").IsVisible);
        Assert.Equal("Editor.Classes.pas", h.FocusedAutomationId);
        Assert.False(second.IsReadOnly);

        // A project member never shows the bar.
        await OpenAsync(h, @"Hello\Unit1.pas");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Unit1.pas").IsVisible);
    }
}
