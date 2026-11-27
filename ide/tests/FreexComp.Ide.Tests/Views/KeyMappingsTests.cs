using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Two-key chords on the Key Mappings page (dialogs.md §9.5, shortcuts.md UI-KEY-012 and UI-KEY-024; I-342).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class KeyMappingsTests
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

    /// <summary>Filters the grid down to one command (by its id) and selects its row.</summary>
    private static void Select(DialogDriver dlg, string commandId)
    {
        SetText(dlg, "KeyMappings.Filter", commandId);
        dlg.Click("KeyMappings.Row." + commandId);
    }

    /// <summary>The selected command's chips (gesture texts).</summary>
    private static List<string> Chips(DialogDriver dlg) =>
        [.. dlg.FindAll("Dialog.Options.KeyMappings.Current.").Where(c => c.IsEffectivelyVisible && c is TextBlock).Select(c => ((TextBlock)c).Text!)];

    /// <summary>Focuses <paramref name="box"/> and presses the keys there, one after the other.</summary>
    private static void PressIn(DialogDriver dlg, string box, params string[] keys)
    {
        dlg.Find<TextBox>(box).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        foreach (var key in keys)
        {
            dlg.Press(key);
        }
    }

    private static string? Visible(DialogDriver dlg, string field) =>
        dlg.TryFind<TextBlock>(field) is { IsEffectivelyVisible: true } text ? text.Text : null;

    private static List<string> Gestures(IKeymapService keymap, string commandId) =>
        [.. keymap.GetGestures(commandId).Select(g => g.ToString())];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-024")]
    [Trait("Req", "UI-KEY-006")]
    public async Task ChordShownAndEditable()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var keymap = h.Services.Keymap;
        var keymapFile = Path.Combine(h.UserDir, "roaming", "keymap.json");
        var running = h.Services.Commands.ExecuteAsync("tools.options", "KeyMappings");
        var dlg = await h.WaitForDialogAsync("Options");

        // Shown: a chord is one gesture; a list with a chord in it is separated by "; ", the chips show one gesture each.
        Select(dlg, "edit.formatSelection");
        Assert.Equal(["Edit: Format Selection", "Shift+Alt+S; Ctrl+K, Ctrl+F", "Editor"], Cells(dlg, "edit.formatSelection"));
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Chips(dlg));
        Assert.Equal(FontWeight.Normal, ShortcutWeight(dlg, "edit.formatSelection"));
        Assert.NotNull(dlg.TryFind<Button>("KeyMappings.Current.Ctrl+K, Ctrl+F.Remove"));

        // Assigned by pressing two keys in the shortcut box: the first is shown alone, the second completes the chord.
        Select(dlg, "edit.duplicateLine");
        Assert.Equal(["Ctrl+D"], Chips(dlg));
        PressIn(dlg, "NewShortcut", "Ctrl+E");
        Assert.Equal("Ctrl+E", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.True(dlg.Find<Button>("Assign").IsEnabled); // one key is a shortcut of its own
        PressIn(dlg, "NewShortcut", "Ctrl+D");
        Assert.Equal("Ctrl+E, Ctrl+D", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Null(dlg.ErrorText("NewShortcut"));
        Assert.Null(Visible(dlg, "Conflict")); // the second key is free to be another command's own shortcut
        Assert.True(dlg.Find<Button>("Assign").IsEnabled);

        // A third key starts over; a second key without Ctrl or Alt is fine (only the first key could prevent typing).
        PressIn(dlg, "NewShortcut", "F7");
        Assert.Equal("F7", dlg.Find<TextBox>("NewShortcut").Text);
        PressIn(dlg, "NewShortcut", "Backspace", "Ctrl+E", "D");
        Assert.Equal("Ctrl+E, D", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Null(dlg.ErrorText("NewShortcut"));
        PressIn(dlg, "NewShortcut", "Backspace");
        Assert.Equal("", dlg.Find<TextBox>("NewShortcut").Text);

        PressIn(dlg, "NewShortcut", "Ctrl+E", "Ctrl+D");
        dlg.Click("Assign");
        Assert.Equal("", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Equal(["Ctrl+D", "Ctrl+E, Ctrl+D"], Chips(dlg));
        Assert.Equal(["Edit: Duplicate Line or Selection", "Ctrl+D; Ctrl+E, Ctrl+D", "Editor"], Cells(dlg, "edit.duplicateLine"));
        Assert.Equal(FontWeight.SemiBold, ShortcutWeight(dlg, "edit.duplicateLine"));
        Assert.False(dlg.Find<Button>("Assign").IsEnabled);

        // Searched by shortcut: the first key lists every chord that starts with it, the second key the chord itself.
        dlg.Click("ByShortcut");
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+K");
        Assert.Equal("Ctrl+K", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Equal(["edit.formatSelection"], Rows(dlg));
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+F");
        Assert.Equal("Ctrl+K, Ctrl+F", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Equal(["edit.formatSelection"], Rows(dlg));
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+E"); // a third key starts over; the pending chord is found too
        Assert.Equal("Ctrl+E", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Equal(["edit.duplicateLine"], Rows(dlg));
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+W");
        Assert.Equal("Ctrl+E, Ctrl+W", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Empty(Rows(dlg)); // no such chord
        // A key that starts no chord is replaced by the next key, as before chords existed.
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+S");
        Assert.Equal(["file.save"], Rows(dlg));
        PressIn(dlg, "KeyMappings.Filter", "Ctrl+F");
        Assert.Equal("Ctrl+F", dlg.Find<TextBox>("KeyMappings.Filter").Text);
        Assert.Equal(["search.find"], Rows(dlg)); // the single key only — not the chord that ends with it
        dlg.Click("ByShortcut");

        // A chord's chip is removed like any other.
        Select(dlg, "edit.formatSelection");
        dlg.Click("KeyMappings.Current.Ctrl+K, Ctrl+F.Remove");
        Assert.Equal(["Shift+Alt+S"], Chips(dlg));
        Assert.Equal(["Edit: Format Selection", "Shift+Alt+S", "Editor"], Cells(dlg, "edit.formatSelection"));

        // A chord can be a command's only (and so its displayed) gesture.
        Select(dlg, "edit.formatDocument");
        dlg.Click("KeyMappings.Current.Shift+Alt+F.Remove");
        PressIn(dlg, "NewShortcut", "Ctrl+K", "Ctrl+D");
        dlg.Click("Assign");
        Assert.Equal(["Ctrl+K, Ctrl+D"], Chips(dlg));
        Assert.Equal(["Ctrl+D"], Gestures(keymap, "edit.duplicateLine")); // nothing applied yet

        // Apply writes the chords through the key map into keymap.json; menus and tooltips get the chord text.
        dlg.Click("Apply");
        Assert.Equal(["Ctrl+D", "Ctrl+E, Ctrl+D"], Gestures(keymap, "edit.duplicateLine"));
        Assert.Equal(["Shift+Alt+S"], Gestures(keymap, "edit.formatSelection"));
        Assert.Equal(["Ctrl+K, Ctrl+D"], Gestures(keymap, "edit.formatDocument"));
        Assert.Equal("Ctrl+K, Ctrl+D", h.Services.Commands.Get("edit.formatDocument").GestureText);
        await h.WaitForAsync(() => h.FileSystem.Calls.Any(c => c.StartsWith("MoveFile ", StringComparison.Ordinal) &&
            c.EndsWith(" -> " + keymapFile, StringComparison.OrdinalIgnoreCase)), "keymap flushed");
        await keymap.FlushAsync();
        var json = File.ReadAllText(keymapFile);
        Assert.Contains("{ \"command\": \"edit.duplicateLine\", \"keys\": [\"Ctrl+E, Ctrl+D\"] }", json, StringComparison.Ordinal);
        Assert.Contains("{ \"command\": \"edit.formatDocument\", \"keys\": [\"Ctrl+K, Ctrl+D\"] }", json, StringComparison.Ordinal);
        Assert.Contains("{ \"command\": \"edit.formatSelection\", \"keys\": [\"Ctrl+K, Ctrl+F\"] }", json, StringComparison.Ordinal); // removed

        // Reset Command brings the default chord back.
        Select(dlg, "edit.formatSelection");
        dlg.Click("ResetCommand");
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Chips(dlg));
        dlg.Click("Ok");
        await running;
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Gestures(keymap, "edit.formatSelection"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-012")]
    [Trait("Req", "UI-KEY-024")]
    [Trait("Req", "UI-KEY-008")]
    public async Task ChordConflictsReported()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var keymap = h.Services.Keymap;
        var running = h.Services.Commands.ExecuteAsync("tools.options", "KeyMappings");
        var dlg = await h.WaitForDialogAsync("Options");

        // A single key that is the first key of another command's chord: reported; Assign removes the chord there.
        Select(dlg, "edit.deleteLine");
        PressIn(dlg, "NewShortcut", "Ctrl+K");
        Assert.Equal(
            "Ctrl+K is the first key of a two-key shortcut of Edit: Format Selection. Assigning it here removes that shortcut there.",
            Visible(dlg, "Conflict"));
        Assert.True(dlg.Find<Button>("Assign").IsEnabled); // a warning, not an error
        dlg.Click("Assign");
        Assert.Null(Visible(dlg, "Conflict"));
        Assert.Equal(["Ctrl+Shift+K", "Ctrl+K"], Chips(dlg));
        SetText(dlg, "KeyMappings.Filter", "edit.formatSelection");
        Assert.Equal(["Edit: Format Selection", "Shift+Alt+S", "Editor"], Cells(dlg, "edit.formatSelection"));
        Assert.Equal(FontWeight.SemiBold, ShortcutWeight(dlg, "edit.formatSelection"));

        // A chord whose first key is another command's single key: reported; Assign removes the single key there.
        dlg.Click("KeyMappings.Row.edit.formatSelection");
        PressIn(dlg, "NewShortcut", "Ctrl+K");
        Assert.Equal("Ctrl+K is already assigned to Edit: Delete Line. Assigning it here removes it there.", Visible(dlg, "Conflict"));
        PressIn(dlg, "NewShortcut", "Ctrl+F");
        Assert.Equal(
            "Ctrl+K, Ctrl+F starts with Ctrl+K, which is already assigned to Edit: Delete Line. Assigning it here removes Ctrl+K there.",
            Visible(dlg, "Conflict"));
        dlg.Click("Assign");
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Chips(dlg));
        Assert.Equal(FontWeight.Normal, ShortcutWeight(dlg, "edit.formatSelection")); // the defaults again

        // The command's own chord gives way when its first key is assigned to the same command: no dead chord is left.
        PressIn(dlg, "NewShortcut", "Ctrl+K");
        Assert.Null(Visible(dlg, "Conflict"));
        dlg.Click("Assign");
        Assert.Equal(["Shift+Alt+S", "Ctrl+K"], Chips(dlg));
        dlg.Click("ResetCommand");
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Chips(dlg));
        SetText(dlg, "KeyMappings.Filter", "edit.deleteLine");
        Assert.Equal(["Edit: Delete Line", "Ctrl+Shift+K", "Editor"], Cells(dlg, "edit.deleteLine"));

        // Two chords may share their first key; the same chord on another command moves like a single gesture.
        dlg.Click("KeyMappings.Row.edit.deleteLine");
        PressIn(dlg, "NewShortcut", "Ctrl+K", "Ctrl+L");
        Assert.Null(Visible(dlg, "Conflict"));
        PressIn(dlg, "NewShortcut", "Ctrl+K", "Ctrl+F"); // the box held a chord: Ctrl+K starts over
        Assert.Equal("Ctrl+K, Ctrl+F", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Equal("Ctrl+K, Ctrl+F is already assigned to Edit: Format Selection. Assigning it here removes it there.", Visible(dlg, "Conflict"));

        // A scope that overlaps (Global) conflicts with the Editor chord too, as for single gestures …
        Select(dlg, "file.saveAll");
        PressIn(dlg, "NewShortcut", "Backspace", "Ctrl+K");
        Assert.Equal(
            "Ctrl+K is the first key of a two-key shortcut of Edit: Format Selection. Assigning it here removes that shortcut there.",
            Visible(dlg, "Conflict"));

        // … a scope that does not overlap (Designer) does not.
        Select(dlg, "designer.lockControls");
        Assert.Equal("Ctrl+K", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Null(Visible(dlg, "Conflict"));

        // A reserved key is refused in either place of a chord; the refused text is replaced by the next key.
        Select(dlg, "edit.deleteLine");
        PressIn(dlg, "NewShortcut", "Backspace", "Ctrl+K", "Alt+F4");
        Assert.Equal("Ctrl+K, Alt+F4", dlg.Find<TextBox>("NewShortcut").Text);
        Assert.Equal("Alt+F4 is reserved by the system and cannot be assigned.", dlg.ErrorText("NewShortcut"));
        Assert.False(dlg.Find<Button>("Assign").IsEnabled);
        PressIn(dlg, "NewShortcut", "Alt+F4");
        Assert.Equal("Alt+F4", dlg.Find<TextBox>("NewShortcut").Text);
        PressIn(dlg, "NewShortcut", "Ctrl+L");
        Assert.Equal("Ctrl+L", dlg.Find<TextBox>("NewShortcut").Text); // not "Alt+F4, Ctrl+L"

        // A first key that would prevent typing is refused, and no chord starts with it.
        PressIn(dlg, "NewShortcut", "Backspace", "K");
        Assert.Equal("K would prevent typing. Combine it with Ctrl or Alt, or use a function key.", dlg.ErrorText("NewShortcut"));
        PressIn(dlg, "NewShortcut", "Ctrl+L");
        Assert.Equal("Ctrl+L", dlg.Find<TextBox>("NewShortcut").Text);
        PressIn(dlg, "NewShortcut", "Backspace");

        // OK applies: both commands as shown.
        dlg.Click("Ok");
        await running;
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Gestures(keymap, "edit.formatSelection"));
        Assert.Equal(["Ctrl+Shift+K"], Gestures(keymap, "edit.deleteLine"));
        Assert.True(keymap.IsChordPrefix(Core.Commands.KeyGesture.Parse("Ctrl+K"), CommandScope.Editor, null));
    }
}
