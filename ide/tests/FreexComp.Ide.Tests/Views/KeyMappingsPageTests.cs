using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Key Mappings page of the Options dialog (dialogs.md §9.5, shortcuts.md UI-KEY-008/021/022).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class KeyMappingsPageTests
{
    private const string RowPrefix = "Dialog.Options.KeyMappings.Row.";

    private static void SetText(DialogDriver dlg, string field, string text)
    {
        dlg.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The visible rows' command ids, in display order.</summary>
    private static List<string> Rows(DialogDriver dlg) =>
        [.. dlg.FindAll(RowPrefix).Where(c => c.IsEffectivelyVisible).Select(c => AutomationIds.Get(c)![RowPrefix.Length..])];

    /// <summary>The cell texts of a row: command, shortcut, scope.</summary>
    private static List<string> Cells(DialogDriver dlg, string commandId) =>
        [.. dlg.Find<Control>("KeyMappings.Row." + commandId).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    private static FontWeight ShortcutWeight(DialogDriver dlg, string commandId) =>
        dlg.Find<Control>("KeyMappings.Row." + commandId).GetVisualDescendants().OfType<TextBlock>().ElementAt(1).FontWeight;

    /// <summary>Filters the grid down to one command and selects its row.</summary>
    private static void Select(DialogDriver dlg, string filter, string commandId)
    {
        SetText(dlg, "KeyMappings.Filter", filter);
        dlg.Click("KeyMappings.Row." + commandId);
    }

    /// <summary>The selected command's chips (gesture texts).</summary>
    private static List<string> Chips(DialogDriver dlg) =>
        [.. dlg.FindAll("Dialog.Options.KeyMappings.Current.").Where(c => c.IsEffectivelyVisible && c is TextBlock).Select(c => ((TextBlock)c).Text!)];

    /// <summary>Focuses the capture box and presses <paramref name="gesture"/> there.</summary>
    private static void Capture(DialogDriver dlg, string gesture)
    {
        dlg.Find<TextBox>("NewShortcut").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        dlg.Press(gesture);
    }

    private static string? Visible(DialogDriver dlg, string field) =>
        dlg.TryFind<TextBlock>(field) is { IsEffectivelyVisible: true } text ? text.Text : null;

    private static List<string> Gestures(IKeymapService keymap, string commandId) =>
        [.. keymap.GetGestures(commandId).Select(g => g.ToString())];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-092")]
    [Trait("Req", "UI-DLG-093")]
    [Trait("Req", "UI-KEY-021")]
    [Trait("Req", "UI-KEY-022")]
    public async Task AssignResolvesConflict()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var keymap = h.Services.Keymap;
        var keymapFile = Path.Combine(h.UserDir, "roaming", "keymap.json");

        // tools.keyMappings opens the page (UI-DLG-092).
        var running = h.Services.Commands.ExecuteAsync("tools.keyMappings");
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Key Mappings", dlg.Find<TextBlock>("PageTitle").Text);

        // Grid: "<Category>: <Title>", gestures joined with ", ", scope; category order of commands.md, then title.
        SetText(dlg, "KeyMappings.Filter", "file: save");
        Assert.Equal(["file.save", "file.saveAll", "file.saveAs", "file.saveProjectAs"], Rows(dlg));
        Assert.Equal(["File: Save", "Ctrl+S", "Global"], Cells(dlg, "file.save"));
        SetText(dlg, "KeyMappings.Filter", "edit.redo"); // the command id matches too
        Assert.Equal(["edit.redo"], Rows(dlg));
        Assert.Equal(["Edit: Redo", "Ctrl+Y, Ctrl+Shift+Z", "Focus"], Cells(dlg, "edit.redo"));
        SetText(dlg, "KeyMappings.Filter", "Open Recent"); // parameterized commands are not listed
        Assert.Empty(Rows(dlg));

        // Search by shortcut: the filter box captures a gesture and lists the commands bound to it.
        dlg.Click("ByShortcut");
        dlg.Find<TextBox>("KeyMappings.Filter").Focus(NavigationMethod.Tab);
        dlg.Press("Ctrl+S");
        Assert.Equal("Ctrl+S", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Equal(["file.save"], Rows(dlg));
        dlg.Click("ByShortcut");

        // A gesture bound in an overlapping scope: the warning names the other command; Assign moves it (pending).
        Select(dlg, "Save All", "file.saveAll");
        Assert.Equal("Shortcuts for \"File: Save All\":", Visible(dlg, "KeyMappings.CurrentLabel"));
        Assert.Equal(["Ctrl+Shift+S"], Chips(dlg));
        Assert.False(dlg.Find<Button>("Assign").IsEnabled);
        Capture(dlg, "Ctrl+S");
        Assert.Equal("Ctrl+S", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Equal("Ctrl+S is already assigned to File: Save. Assigning it here removes it there.", Visible(dlg, "Conflict"));
        Assert.True(dlg.Find<Button>("Assign").IsEnabled);
        dlg.Click("Assign");
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+S"], Chips(dlg));
        Assert.Equal("", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Null(Visible(dlg, "Conflict"));
        Assert.Equal(["File: Save All", "Ctrl+Shift+S, Ctrl+S", "Global"], Cells(dlg, "file.saveAll"));
        Assert.Equal(FontWeight.SemiBold, ShortcutWeight(dlg, "file.saveAll")); // user-changed binding in type.bodyStrong
        Assert.Equal(["Ctrl+S"], Gestures(keymap, "file.save")); // nothing applied yet
        Assert.True(dlg.Find<Button>("Apply").IsEnabled);

        // A different, non-overlapping scope keeps its binding: F2 moves from refactor.rename (Editor) only,
        // project.renameFile (Project Manager) keeps it.
        Select(dlg, "Upper", "edit.upperCase");
        Capture(dlg, "F2");
        Assert.Equal("F2 is already assigned to Refactor: Rename Symbol…. Assigning it here removes it there.", Visible(dlg, "Conflict"));
        dlg.Click("Assign");
        Assert.Equal(["Ctrl+Shift+U", "F2"], Chips(dlg));

        // A chip's × removes that gesture.
        Select(dlg, "file.closeAll", "file.closeAll");
        dlg.Click("KeyMappings.Current.Ctrl+Shift+W.Remove");
        Assert.Empty(Chips(dlg));

        // Apply writes through IKeymapService and flushes keymap.json.
        dlg.Click("Apply");
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+S"], Gestures(keymap, "file.saveAll"));
        Assert.Empty(Gestures(keymap, "file.save"));
        Assert.Equal(["Ctrl+Shift+U", "F2"], Gestures(keymap, "edit.upperCase"));
        Assert.Empty(Gestures(keymap, "refactor.rename"));
        Assert.Equal(["F2"], Gestures(keymap, "project.renameFile"));
        Assert.Empty(Gestures(keymap, "file.closeAll"));
        // I-169: never read keymap.json while the background save may be replacing it (a read overlapping the replace
        // fails with a sharing violation, or makes the replace fail). Apply started the save (recorded MoveFile); once
        // that save has finished, the file holds the new binding.
        await h.WaitForAsync(() => h.FileSystem.Calls.Any(c => c.StartsWith("MoveFile ", StringComparison.Ordinal) &&
            c.EndsWith(" -> " + keymapFile, StringComparison.OrdinalIgnoreCase)), "keymap flushed");
        await keymap.FlushAsync();
        Assert.Contains("\"file.saveAll\"", File.ReadAllText(keymapFile), StringComparison.Ordinal);
        Assert.False(dlg.Find<Button>("Apply").IsEnabled);

        // Reset Command restores the defaults and takes them back from other commands.
        Select(dlg, "file: save", "file.save");
        Assert.Empty(Chips(dlg));
        dlg.Click("ResetCommand");
        Assert.Equal(["Ctrl+S"], Chips(dlg));
        Assert.Equal(["File: Save All", "Ctrl+Shift+S", "Global"], Cells(dlg, "file.saveAll"));

        // Reset All asks first; Cancel keeps everything, Reset restores every default (pending until OK).
        h.Messages.Answer("Msg.Options.ResetKeysConfirm", "Cancel");
        dlg.Click("ResetAll");
        SetText(dlg, "KeyMappings.Filter", "file.closeAll");
        Assert.Equal(["File: Close All", "", "Global"], Cells(dlg, "file.closeAll"));
        h.Messages.Answer("Msg.Options.ResetKeysConfirm", "Reset");
        dlg.Click("ResetAll");
        Assert.Equal(["File: Close All", "Ctrl+Shift+W", "Global"], Cells(dlg, "file.closeAll"));
        Assert.Equal(FontWeight.Normal, ShortcutWeight(dlg, "file.closeAll"));
        Assert.Empty(Gestures(keymap, "file.closeAll"));
        dlg.Click("Ok");
        await running;
        Assert.Equal(["Ctrl+Shift+W"], Gestures(keymap, "file.closeAll"));
        Assert.Equal(["F2"], Gestures(keymap, "refactor.rename"));
        Assert.Equal(["Ctrl+S"], Gestures(keymap, "file.save"));
        Assert.Equal(["Ctrl+Shift+S"], Gestures(keymap, "file.saveAll"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-008")]
    [Trait("Req", "UI-A11Y-015")]
    public async Task ReservedAndUnmodifiedGesturesRejected()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options", "KeyMappings");
        var dlg = await h.WaitForDialogAsync("Options");

        // A reserved gesture has no × on its chip.
        Select(dlg, "file.exit", "file.exit");
        Assert.Equal(["Alt+F4"], Chips(dlg));
        Assert.Null(dlg.TryFind<Button>("KeyMappings.Current.Alt+F4.Remove"));

        // Reserved gestures cannot be assigned.
        Select(dlg, "edit.duplicateLine", "edit.duplicateLine");
        foreach (var reserved in new[] { "Alt+F4", "Alt+Space", "Ctrl+Escape" })
        {
            Capture(dlg, reserved);
            Assert.Equal(reserved, dlg.Find<TextBox>("NewShortcut").Text);
            Assert.Equal($"{reserved} is reserved by the system and cannot be assigned.", dlg.ErrorText("NewShortcut"));
            Assert.False(dlg.Find<Button>("Assign").IsEnabled);
        }

        // Printable keys, Enter and Space without Ctrl/Alt would prevent typing in an Editor/Focus command.
        foreach (var unmodified in new[] { "K", "Shift+K", "5", "Space", "Enter", "Shift+/" })
        {
            Capture(dlg, unmodified);
            Assert.Equal(unmodified, dlg.Find<TextBox>("NewShortcut").Text);
            Assert.Equal($"{unmodified} would prevent typing. Combine it with Ctrl or Alt, or use a function key.", dlg.ErrorText("NewShortcut"));
            Assert.False(dlg.Find<Button>("Assign").IsEnabled);
        }

        Assert.True(dlg.Window.IsVisible); // Enter in the capture box did not press OK

        // Accepted: function keys, Ctrl/Alt combinations.
        foreach (var accepted in new[] { "F7", "Ctrl+K", "Alt+K", "Shift+F7" })
        {
            Capture(dlg, accepted);
            Assert.Null(dlg.ErrorText("NewShortcut"));
            Assert.True(dlg.Find<Button>("Assign").IsEnabled, accepted);
        }

        // Backspace clears the capture box.
        Capture(dlg, "Backspace");
        Assert.Equal("", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.False(dlg.Find<Button>("Assign").IsEnabled);

        // An unmodified letter is fine where no typing happens (Designer scope).
        Select(dlg, "designer.lockControls", "designer.lockControls");
        Capture(dlg, "K");
        Assert.Null(dlg.ErrorText("NewShortcut"));
        Assert.True(dlg.Find<Button>("Assign").IsEnabled);

        // Tab is not captured: it moves the focus on.
        Capture(dlg, "Ctrl+K");
        dlg.Press("Tab");
        Assert.Equal("Ctrl+K", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.NotEqual("Dialog.Options.NewShortcut", dlg.FocusedAutomationId);

        // Escape is not captured: it cancels the dialog; nothing was assigned.
        dlg.Find<TextBox>("NewShortcut").Focus(NavigationMethod.Tab);
        dlg.Press("Escape");
        await running;
        Assert.False(dlg.Window.IsVisible);
        Assert.Equal(["Ctrl+Shift+L"], Gestures(h.Services.Keymap, "designer.lockControls"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-092")]
    public async Task ColumnsSort()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.keyMappings");
        var dlg = await h.WaitForDialogAsync("Options");
        SetText(dlg, "KeyMappings.Filter", "copy");
        var natural = Rows(dlg);
        Assert.True(natural.Count >= 3, string.Join(", ", natural));

        // Rows top to bottom by screen position (containers are recycled), with their Command and Scope texts.
        List<(string Id, string Command, string Scope)> Shown()
        {
            Dispatcher.UIThread.RunJobs();
            return [.. dlg.FindAll(RowPrefix).Where(c => c.IsEffectivelyVisible)
                .OrderBy(c => c.TranslatePoint(default, dlg.Window)!.Value.Y)
                .Select(c => AutomationIds.Get(c)![RowPrefix.Length..])
                .Select(id => (id, Cells(dlg, id)[0], Cells(dlg, id)[2]))];
        }

        var comparer = StringComparer.CurrentCultureIgnoreCase;
        Assert.True(Shown().Select(r => r.Scope).Distinct().Count() >= 2, "the filter shows several scopes");
        Assert.Equal(natural, Shown().Select(r => r.Id));

        // Command: ascending, a second click descending.
        dlg.Click("KeyMappings.Column.Command");
        var byCommand = Shown();
        Assert.Equal(byCommand.OrderBy(r => r.Command, comparer).Select(r => r.Id), byCommand.Select(r => r.Id));
        Assert.NotEqual(natural, byCommand.Select(r => r.Id));
        dlg.Click("KeyMappings.Column.Command");
        Assert.Equal(byCommand.Select(r => r.Id).Reverse(), Shown().Select(r => r.Id));

        // Scope: ascending (ties by command), a second click descending; the filter keeps the order.
        dlg.Click("KeyMappings.Column.Scope");
        var byScope = Shown();
        Assert.Equal(byScope.OrderBy(r => r.Scope, comparer).ThenBy(r => r.Command, comparer).Select(r => r.Id), byScope.Select(r => r.Id));
        SetText(dlg, "KeyMappings.Filter", "cop");
        var refiltered = Shown();
        Assert.Equal(refiltered.OrderBy(r => r.Scope, comparer).ThenBy(r => r.Command, comparer).Select(r => r.Id), refiltered.Select(r => r.Id));
        dlg.Click("KeyMappings.Column.Scope");
        var descending = Shown();
        Assert.Equal(refiltered.Select(r => r.Id).Reverse(), descending.Select(r => r.Id));

        dlg.Press("Escape");
        await running;
    }
}