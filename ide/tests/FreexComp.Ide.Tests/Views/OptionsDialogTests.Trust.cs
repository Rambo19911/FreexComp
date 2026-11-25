using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Options page Environment › Trusted Locations (dialogs.md §9.10; phase8 trust-prompt.md §3; card F09-008).</summary>
public sealed partial class OptionsDialogTests
{
    private const string Hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string TrustFile(HeadlessHarness h) => Path.Combine(h.Services.UserPaths.RoamingDirectory, "trust.json");

    private static async Task<DialogDriver> OpenTrustPageAsync(HeadlessHarness h)
    {
        _ = h.Services.Commands.ExecuteAsync("tools.options", OptionsViewModel.EnvironmentTrustPage);
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Environment › Trusted Locations", PageTitle(dlg));
        return dlg;
    }

    /// <summary>The rows of the list as (AutomationId suffix, kind cell, path cell).</summary>
    private static List<(string Id, string Kind, string Path)> TrustRows(DialogDriver dlg) =>
    [
        .. dlg.Find<ListBox>("Trust.Entries").Items.Cast<ListBoxItem>().Select(item =>
        {
            var cells = ((Grid)item.Content!).Children.OfType<TextBlock>().ToList();
            return (AutomationIds.Get(item)!["Dialog.Options.Trust.".Length..], cells[0].Text!, cells[1].Text!);
        }),
    ];

    private static async Task CloseAsync(HeadlessHarness h, DialogDriver dlg, string button)
    {
        dlg.Click(button);
        await h.WaitForAsync(() => dlg.Window.Model.IsClosed, "Options closed");
    }

    /// <summary>
    /// IDE-TRUST-014…017, UI-DLG-297: the page under Environment after Files, delivered; the entries of the trust list
    /// (folders, then projects); Remove and the Delete key take the selected rows out at once; Cancel keeps the file,
    /// OK and Apply remove exactly the removed entries from it; Remove All; a failed write keeps the dialog open.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TRUST-014")]
    [Trait("Req", "IDE-TRUST-015")]
    [Trait("Req", "IDE-TRUST-016")]
    [Trait("Req", "IDE-TRUST-017")]
    [Trait("Req", "UI-DLG-297")]
    public async Task TrustedLocationsPageListsAndRemoves()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var store = h.Services.Trust.Store;
        store.TrustProject(@"C:\Samples\Demo\Demo.pproj", Hash);
        store.TrustFolder(@"C:\Work");
        store.TrustProject(@"C:\Samples\Lib\Lib.pproj", Hash);
        store.TrustFolder(@"D:\Shared\Projects");
        var original = File.ReadAllBytes(TrustFile(h));

        var dlg = await OpenTrustPageAsync(h);
        try
        {
            // The page: in the tree under Environment, after Files; delivered (no later-version note, content enabled).
            var pages = VisiblePages(dlg);
            Assert.Equal(pages.IndexOf("Environment.Files") + 1, pages.IndexOf("Environment.Trust"));
            Assert.Equal("Trusted Locations", Avalonia.Automation.AutomationProperties.GetName(dlg.Find<ListBoxItem>("Page.Environment.Trust")));
            Assert.False(dlg.Find<TextBlock>("LaterVersion").IsEffectivelyVisible);
            Assert.Equal(Strings.Msg_Options_TrustHint, dlg.Find<TextBlock>("Trust.Hint").Text);
            Assert.False(dlg.Find<TextBlock>("Trust.Warning").IsEffectivelyVisible);
            Assert.False(dlg.Find<TextBlock>("Trust.Empty").IsEffectivelyVisible);
            var list = dlg.Find<ListBox>("Trust.Entries");
            Assert.Equal("Trusted folders and projects", Avalonia.Automation.AutomationProperties.GetName(list));
            Assert.Equal(SelectionMode.Multiple, list.SelectionMode);
            Assert.Same(list, dlg.Window.GetVisualDescendants().OfType<Label>().Single(l => Equals(l.Content, "_Trusted folders and projects:")).Target);

            // Snapshot order: folders, then projects, each in file order.
            Assert.Equal(
            [
                ("Entry.0", "Folder", @"C:\Work"),
                ("Entry.1", "Folder", @"D:\Shared\Projects"),
                ("Entry.2", "Project", @"C:\Samples\Demo\Demo.pproj"),
                ("Entry.3", "Project", @"C:\Samples\Lib\Lib.pproj"),
            ], TrustRows(dlg));
            Assert.Equal(@"C:\Samples\Demo\Demo.pproj", ToolTip.GetTip(dlg.Find<ListBoxItem>("Trust.Entry.2")));
            Assert.Equal(80, ((Grid)dlg.Find<ListBoxItem>("Trust.Entry.2").Content!).ColumnDefinitions[0].Width.Value);

            // The buttons: Remove needs a selection; no reset buttons on this page (UI-DLG-297).
            var remove = dlg.Find<Button>("Trust.Remove");
            var removeAll = dlg.Find<Button>("Trust.RemoveAll");
            Assert.Equal(("_Remove", "Remove _All"), (remove.Content, removeAll.Content));
            Assert.Equal((false, true), (remove.IsEnabled, removeAll.IsEnabled));
            Assert.Equal([remove, removeAll],
                list.FindAncestorOfType<StackPanel>()!.GetVisualDescendants().OfType<Button>().Where(b => AutomationIds.Get(b) is { Length: > 0 }));
            Assert.False(dlg.Find<Button>("Apply").IsEnabled);

            // Remove: the row disappears at once; nothing is written; Cancel brings it back.
            dlg.Click("Trust.Entry.1");
            Assert.True(remove.IsEnabled);
            dlg.Click("Trust.Remove");
            Assert.Equal([@"C:\Work", @"C:\Samples\Demo\Demo.pproj", @"C:\Samples\Lib\Lib.pproj"], TrustRows(dlg).Select(r => r.Path));
            Assert.Equal(["Entry.0", "Entry.1", "Entry.2"], TrustRows(dlg).Select(r => r.Id));
            Assert.True(dlg.Find<Button>("Apply").IsEnabled);
            Assert.Equal(original, File.ReadAllBytes(TrustFile(h)));
            await CloseAsync(h, dlg, "Cancel");
            Assert.Equal(original, File.ReadAllBytes(TrustFile(h)));

            // Delete key on a multi-selection, then OK: exactly those entries leave the file; an entry another instance
            // added meanwhile is untouched.
            dlg = await OpenTrustPageAsync(h);
            Assert.Equal(4, TrustRows(dlg).Count);
            dlg.Click("Trust.Entry.1");
            dlg.Press("Shift+Down");
            Assert.Equal(2, dlg.Find<ListBox>("Trust.Entries").Selection.Count);
            dlg.Press("Delete");
            Assert.Equal([@"C:\Work", @"C:\Samples\Lib\Lib.pproj"], TrustRows(dlg).Select(r => r.Path));
            store.TrustFolder(@"E:\Added\Meanwhile");
            await CloseAsync(h, dlg, "Ok");
            Assert.Equal(
            [
                new TrustEntry(TrustEntryKind.Folder, @"C:\Work", null),
                new TrustEntry(TrustEntryKind.Folder, @"E:\Added\Meanwhile", null),
                new TrustEntry(TrustEntryKind.Project, @"C:\Samples\Lib\Lib.pproj", Hash),
            ], store.Read().Entries);

            // A write that fails: Msg.Trust.SaveFailed, the dialog stays open; nothing was removed.
            dlg = await OpenTrustPageAsync(h);
            File.SetAttributes(TrustFile(h), FileAttributes.ReadOnly);
            h.Messages.Answer("Msg.Trust.SaveFailed", "OK");
            dlg.Click("Trust.RemoveAll");
            Assert.Empty(TrustRows(dlg));
            dlg.Click("Ok");
            await h.WaitForAsync(() => h.Messages.Requests.Any(r => r.MessageId == "Msg.Trust.SaveFailed"), "the save failure message");
            Assert.False(dlg.Window.Model.IsClosed);
            Assert.Equal(3, store.Read().Entries.Length);
            File.SetAttributes(TrustFile(h), FileAttributes.Normal);

            // Remove All + Apply: the list is empty, both buttons are disabled, the dialog stays open, the file holds no entry.
            Assert.True(dlg.Find<TextBlock>("Trust.Empty").IsEffectivelyVisible);
            Assert.Equal(Strings.Msg_Options_TrustEmpty, dlg.Find<TextBlock>("Trust.Empty").Text);
            Assert.Equal((false, false), (dlg.Find<Button>("Trust.Remove").IsEnabled, dlg.Find<Button>("Trust.RemoveAll").IsEnabled));
            dlg.Click("Apply");
            await h.WaitForAsync(() => !dlg.Find<Button>("Apply").IsEnabled, "applied");
            Assert.False(dlg.Window.Model.IsClosed);
            Assert.Empty(store.Read().Entries);
            await CloseAsync(h, dlg, "Cancel");
            Assert.Empty(store.Read().Entries);
        }
        finally
        {
            if (File.Exists(TrustFile(h)))
                File.SetAttributes(TrustFile(h), FileAttributes.Normal);
            if (!dlg.Window.Model.IsClosed)
                await dlg.Window.Model.PressCancelAsync(); // after a failure, so that the failure is what the test reports
        }
    }

    /// <summary>
    /// IDE-TRUST-015, 018: without a trust list the page shows the empty text; an unreadable list shows the warning with the
    /// reason and no row; the Options filter finds the page by its title and by its labels.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "IDE-TRUST-015")]
    [Trait("Req", "IDE-TRUST-018")]
    public async Task TrustedLocationsPageEmptyAndUnreadable()
    {
        await using var h = await HeadlessHarness.StartAsync();
        Assert.False(File.Exists(TrustFile(h)));

        var dlg = await OpenTrustPageAsync(h);
        try
        {
            Assert.Empty(TrustRows(dlg));
            Assert.True(dlg.Find<TextBlock>("Trust.Empty").IsEffectivelyVisible);
            Assert.Equal("No trusted folders or projects", dlg.Find<TextBlock>("Trust.Empty").Text);
            Assert.False(dlg.Find<TextBlock>("Trust.Warning").IsEffectivelyVisible);
            Assert.Equal((false, false), (dlg.Find<Button>("Trust.Remove").IsEnabled, dlg.Find<Button>("Trust.RemoveAll").IsEnabled));
            Assert.False(File.Exists(TrustFile(h))); // looking at the page creates nothing

            // The filter: the page title, the tree label and the page's labels.
            foreach (var text in (string[])["trusted loc", "Trusted folders and projects", "remove all"])
            {
                dlg.Find<TextBox>("Filter").Text = "";
                dlg.Type("Filter", text);
                Assert.Contains("Environment.Trust", VisiblePages(dlg));
                Assert.Contains("Environment", VisiblePages(dlg));
                Assert.DoesNotContain("Environment.Files", VisiblePages(dlg));
            }

            dlg.Find<TextBox>("Filter").Text = "";
            dlg.Type("Filter", "trusted loc");
            Assert.Equal(["Environment", "Environment.Trust"], VisiblePages(dlg));
            Assert.Equal("Environment › Trusted Locations", PageTitle(dlg));
            await CloseAsync(h, dlg, "Cancel");

            // An unreadable list: the warning with the reason of the store, no row, nothing to remove.
            Directory.CreateDirectory(Path.GetDirectoryName(TrustFile(h))!);
            File.WriteAllText(TrustFile(h), "{ \"formatVersion\": 1, \"folders\": [ \"C:\\\\Work\" ");
            var problem = h.Services.Trust.Store.Read().ReadProblem;
            Assert.False(string.IsNullOrEmpty(problem));
            dlg = await OpenTrustPageAsync(h);
            var warning = dlg.Find<TextBlock>("Trust.Warning");
            Assert.True(warning.IsEffectivelyVisible);
            Assert.Equal($"The list of trusted locations cannot be read ({problem}). No project is trusted.", warning.Text);
            Assert.Empty(TrustRows(dlg));
            Assert.True(dlg.Find<TextBlock>("Trust.Empty").IsEffectivelyVisible);
            Assert.Equal((false, false), (dlg.Find<Button>("Trust.Remove").IsEnabled, dlg.Find<Button>("Trust.RemoveAll").IsEnabled));
            var list = dlg.Find<ListBox>("Trust.Entries");
            var parts = list.FindAncestorOfType<StackPanel>()!.Children;
            Assert.True(parts.IndexOf(warning) < parts.IndexOf(list.FindAncestorOfType<Border>()!), "the warning is above the list");
            await CloseAsync(h, dlg, "Cancel");
        }
        finally
        {
            if (!dlg.Window.Model.IsClosed)
                await dlg.Window.Model.PressCancelAsync();
        }
    }
}
