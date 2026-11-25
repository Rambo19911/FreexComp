using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>The built-in key maps <c>default</c> and <c>classic</c> (phase8-ui-additions.md D3, I-253).</summary>
[Trait("Phase", "8")]
public sealed class KeyMapTests
{
    private const string KeymapFile = @"C:\u\roaming\keymap.json";

    private static readonly CommandScope[] FocusScopes =
        [CommandScope.Editor, CommandScope.Designer, CommandScope.ProjectManager, CommandScope.ToolList];

    private readonly InMemoryFileSystem _disk = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 25, 9, 15, 0, TimeSpan.Zero));
    private readonly RecordingLog _log = new();

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    public void ClassicPreset_MatchesSpecTable()
    {
        var table = SpecRows();
        var defaults = DefaultKeymap.Bindings.Select(Describe).ToList();
        var classic = KeymapPresets.Bindings(KeymapPresets.Classic).Select(Describe).ToList();
        var keymap = KeymapPresets.Create(KeymapPresets.Classic);

        Assert.Equal(18, table.Count);
        foreach (var row in table)
        {
            Assert.Contains(Describe(row), classic);

            // The row is what the key does while the editor has the focus (no inner binding shadows a Global row)
            // and it is the gesture menus show for the command.
            Assert.Equal(row.CommandId, keymap.FindCommand(row.Gesture, CommandScope.Editor, null));
            Assert.Equal(row.Gesture, keymap.GetGestures(row.CommandId)[0]);
        }

        // What Classic adds to Default is the table and nothing else.
        Assert.Equal(table.Select(Describe).Order(StringComparer.Ordinal), classic.Except(defaults, StringComparer.Ordinal).Order(StringComparer.Ordinal));

        // A listed command gives up its Default function-key gestures; its other gestures stay (Alt+3, Ctrl+Shift+B).
        Assert.Equal(["F9"], Gestures(keymap, "run.start"));
        Assert.Equal(["Ctrl+F2"], Gestures(keymap, "run.stop"));
        Assert.Equal(["F11", "Alt+3"], Gestures(keymap, "view.objectInspector"));
        Assert.Equal(["Ctrl+F9", "Ctrl+Shift+B"], Gestures(keymap, "project.build"));

        // Of the commands the table does not list only run.deleteAllBreakpoints changes (unbound); run.restart keeps
        // Ctrl+Shift+F5, which no row takes.
        var changed = table.Select(r => r.CommandId).ToHashSet(StringComparer.Ordinal);
        var displaced = CommandCatalog.All.Select(c => c.Id)
            .Where(id => !changed.Contains(id) && !Of(classic, id).SequenceEqual(Of(defaults, id)))
            .Order(StringComparer.Ordinal);
        Assert.Equal(["run.deleteAllBreakpoints"], displaced);
        Assert.Empty(keymap.GetGestures("run.deleteAllBreakpoints"));
        Assert.Equal(["Ctrl+Shift+F5"], Gestures(keymap, "run.restart"));

        // Default is the map of shortcuts.md, unchanged.
        Assert.Equal(defaults, KeymapPresets.Bindings(KeymapPresets.Default).Select(Describe));
        Assert.Equal([KeymapPresets.Default, KeymapPresets.Classic], KeymapPresets.All);
        Assert.Equal(KeymapPresets.All, SettingsCatalog.KeyboardPreset.AllowedValues);
        Assert.Equal(KeymapPresets.Default, SettingsCatalog.KeyboardPreset.Default);
    }

    [Fact(Timeout = 30000)]
    public void NoGestureConflictsInAnyPreset()
    {
        var problems = new List<string>();
        foreach (var preset in KeymapPresets.All)
        {
            var bindings = KeymapPresets.Bindings(preset);

            // At most one command per gesture and scope (Focus and Global are the same, outermost level).
            problems.AddRange(bindings
                .GroupBy(b => (b.Gesture, Level: b.Scope == CommandScope.Focus ? CommandScope.Global : b.Scope, b.ToolWindow))
                .Where(g => g.Select(b => b.CommandId).Distinct().Count() > 1)
                .Select(g => $"{preset}: {g.Key.Gesture} in {g.Key.Level}/{g.Key.ToolWindow}: {string.Join(", ", g.Select(b => b.CommandId))}"));

            // A Global gesture must stay reachable in at least one focus scope.
            problems.AddRange(bindings
                .Where(outer => outer.Scope is CommandScope.Global or CommandScope.Focus
                                && FocusScopes.All(s => bindings.Any(b => b.Gesture == outer.Gesture && b.Scope == s && b.ToolWindow is null)))
                .Select(outer => $"{preset}: {outer.Gesture} ({outer.CommandId}) is shadowed in every focus scope"));

            // UI-KEY-003 (no Ctrl+Alt+<character>), UI-KEY-008 (reserved gestures stay where Default has them).
            problems.AddRange(bindings
                .Where(b => b.Gesture.Modifiers.HasFlag(KeyModifiers.Ctrl) && b.Gesture.Modifiers.HasFlag(KeyModifiers.Alt) && b.Gesture.Key.Length == 1)
                .Select(b => $"{preset}: {b.Gesture} ({b.CommandId}) is an AltGr combination"));
            problems.AddRange(bindings
                .Where(b => KeymapService.IsReserved(b.Gesture) && !DefaultKeymap.Bindings.Contains(b))
                .Select(b => $"{preset}: {b.Gesture} ({b.CommandId}) is reserved"));

            // Every binding names a catalog command whose scopes allow it.
            foreach (var b in bindings)
            {
                var command = CommandCatalog.All.FirstOrDefault(c => c.Id == b.CommandId);
                if (command is null)
                    problems.Add($"{preset}: {b.CommandId} is not in CommandCatalog");
                else if (!command.Scopes.Contains(b.Scope))
                    problems.Add($"{preset}: {b.CommandId} bound in {b.Scope}, command scopes {string.Join(", ", command.Scopes)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(Timeout = 30000)]
    public Task PresetSwitch_KeepsUserOverrides() => TestUiThread.RunAsync(PresetSwitchKeepsUserOverridesOnUiThread);

    private async Task PresetSwitchKeepsUserOverridesOnUiThread()
    {
        var keymap = CreateKeymapService();
        keymap.Load();
        Assert.Equal(KeymapPresets.Default, keymap.Preset);
        keymap.SetGestures("file.saveAll", [G("Ctrl+Shift+S"), G("Ctrl+Alt+S")]); // added
        keymap.SetGestures("edit.redo", [G("Ctrl+Y")]);                            // a default taken away
        keymap.SetGestures("tools.options", [G("Ctrl+,"), G("F9")]);               // the key Classic gives to run.start
        await keymap.FlushAsync();
        var file = ReadKeymapFile();
        var changed = 0;
        keymap.Changed += (_, _) => changed++;

        keymap.SetPreset(KeymapPresets.Classic);

        Assert.Equal(KeymapPresets.Classic, keymap.Preset);
        Assert.Equal(1, changed);
        Assert.Equal(["F8"], Gestures(keymap.Current, "run.stepOver"));            // the preset's gestures at once
        Assert.Equal("run.toggleBreakpoint", keymap.FindCommand(G("F5"), CommandScope.Editor, null));
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+Alt+S"], Gestures(keymap.Current, "file.saveAll"));
        Assert.Equal(["Ctrl+Y"], Gestures(keymap.Current, "edit.redo"));
        Assert.Equal("tools.options", keymap.FindCommand(G("F9"), CommandScope.Global, null)); // the user's assignment wins
        Assert.Equal("tools.options", keymap.FindCommand(G("F9"), CommandScope.Editor, null));
        Assert.Empty(keymap.GetGestures("run.start"));

        // The switch itself changes nothing in keymap.json: the file holds the user's assignments only.
        await keymap.FlushAsync();
        Assert.Equal(file, ReadKeymapFile());

        // An assignment made under Classic is stored against Classic and is kept in Default as well.
        keymap.SetGestures("run.stepOver", [G("F8"), G("Ctrl+Alt+F8")]);
        keymap.SetPreset(KeymapPresets.Default);
        Assert.Equal(["F10", "Ctrl+Alt+F8"], Gestures(keymap.Current, "run.stepOver"));
        Assert.Equal(["F5"], Gestures(keymap.Current, "run.start"));
        Assert.Equal(["Ctrl+,", "F9"], Gestures(keymap.Current, "tools.options"));
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+Alt+S"], Gestures(keymap.Current, "file.saveAll"));
        Assert.Equal(["Ctrl+Y"], Gestures(keymap.Current, "edit.redo"));

        // Reset All gives the chosen preset back, not Default.
        keymap.SetPreset(KeymapPresets.Classic);
        keymap.ResetAll();
        Assert.Equal(
            KeymapPresets.Bindings(KeymapPresets.Classic).Select(Describe),
            keymap.Current.Bindings.Select(Describe));

        // A second instance started with the preset reads the same file on top of it; an unknown name means Default.
        await keymap.FlushAsync();
        WriteKeymapFile(file);
        var second = CreateKeymapService();
        second.SetPreset(KeymapPresets.Classic);
        second.Load();
        Assert.Equal("tools.options", second.FindCommand(G("F9"), CommandScope.Global, null));
        Assert.Equal(["F7"], Gestures(second.Current, "run.stepInto"));
        Assert.Equal(["Ctrl+Y"], Gestures(second.Current, "edit.redo"));
        second.SetPreset("no-such-map");
        Assert.Equal(KeymapPresets.Default, second.Preset);
        Assert.Equal(["F11"], Gestures(second.Current, "run.stepInto"));
    }

    /// <summary>The rows of the D3 table as bindings.</summary>
    private static List<KeyBinding> SpecRows()
    {
        var rows = new List<KeyBinding>();
        foreach (var row in SpecTables.Read(SpecTables.DocPath("spec/ui/phase8-ui-additions.md"), "D3. "))
        {
            var scope = row["Scope"] switch
            {
                "G" => CommandScope.Global,
                "E" => CommandScope.Editor,
                var other => throw new InvalidOperationException($"unknown scope '{other}'"),
            };
            rows.Add(new KeyBinding(KeyGesture.Parse(row["Gesture"]), scope, null, CommandId(row["Command"])));
        }

        return rows;
    }

    /// <summary>A command id, or the two rows the table words: Build (by its Default gesture) and Rebuild.</summary>
    private static string CommandId(string cell)
    {
        if (CommandCatalog.All.Any(c => c.Id == cell))
            return cell;
        if (cell.StartsWith("Build (the command bound to Ctrl+Shift+B in Default)", StringComparison.Ordinal))
            return DefaultKeymap.Bindings.Single(b => b.Gesture == KeyGesture.Parse("Ctrl+Shift+B")).CommandId;
        return cell == "Rebuild" ? "project.rebuild" : throw new InvalidOperationException($"unknown command '{cell}'");
    }

    private static string Describe(KeyBinding b) =>
        $"{b.Gesture} | {b.Scope}{(b.ToolWindow is null ? "" : "/" + b.ToolWindow)} | {b.CommandId}{(b.Argument is null ? "" : " " + b.Argument)}";

    private static List<string> Of(List<string> described, string commandId) =>
        [.. described.Where(d => d.Split(" | ")[2].Split(' ')[0] == commandId)];

    private static List<string> Gestures(Keymap keymap, string commandId) => [.. keymap.GetGestures(commandId).Select(g => g.ToString())];

    private static KeyGesture G(string text) => KeyGesture.Parse(text);

    private KeymapService CreateKeymapService() =>
        new(UserPaths.Create(@"C:\u", _ => null, _ => throw new InvalidOperationException()), _disk, _clock, _log);

    private void WriteKeymapFile(string text)
    {
        _disk.CreateDirectory(@"C:\u\roaming");
        _disk.WriteAllBytes(KeymapFile, Encoding.UTF8.GetBytes(text));
    }

    private string ReadKeymapFile() => Encoding.UTF8.GetString(_disk.ReadAllBytes(KeymapFile));
}
