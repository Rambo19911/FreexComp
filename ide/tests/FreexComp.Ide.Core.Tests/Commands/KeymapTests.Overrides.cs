using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary><c>keymap.json</c> overrides (settings.md §5, I-011).</summary>
public sealed partial class KeymapTests
{
    private const string KeymapFile = @"C:\u\roaming\keymap.json";

    private readonly InMemoryFileSystem _disk = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 9, 15, 0, TimeSpan.Zero));
    private readonly RecordingLog _log = new();

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-060")]
    [Trait("Req", "IDE-SET-061")]
    [Trait("Req", "UI-KEY-020")]
    [Trait("Req", "UI-KEY-021")]
    public async Task StoresOnlyDifferencesFromDefaults()
    {
        var keymap = CreateKeymapService();
        keymap.Load();
        Assert.Equal(DefaultKeymap.Bindings.Count, keymap.Current.Bindings.Count); // missing file = defaults
        var changed = 0;
        keymap.Changed += (_, _) => changed++;

        keymap.SetGestures("file.saveAll", [G("Ctrl+Shift+S"), G("Ctrl+Alt+S")]);   // added
        keymap.SetGestures("edit.redo", [G("Ctrl+Y")]);                              // default removed
        keymap.SetGestures("project.rebuild", [G("Shift+F9"), G("Ctrl+Shift+R")]);  // Shift+F9 is Editor-scoped elsewhere: kept
        keymap.SetGestures("view.wordWrap", [G("Alt+Z")]);                           // equal to defaults: nothing stored
        await keymap.FlushAsync();

        Assert.Equal(4, changed);
        Assert.Equal(
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"bindings\": [\n" +
            "    { \"command\": \"file.saveAll\", \"keys\": [\"Ctrl+Alt+S\"] },\n" +
            "    { \"command\": \"project.rebuild\", \"keys\": [\"Shift+F9\", \"Ctrl+Shift+R\"] }\n" +
            "  ],\n" +
            "  \"removed\": [\n" +
            "    { \"command\": \"edit.redo\", \"keys\": [\"Ctrl+Shift+Z\"] }\n" +
            "  ]\n" +
            "}\n",
            ReadKeymapFile());
        Assert.Equal("project.rebuild", keymap.FindCommand(G("Shift+F9"), CommandScope.Global, null));
        Assert.Equal("run.addWatch", keymap.FindCommand(G("Shift+F9"), CommandScope.Editor, null));
        Assert.Null(keymap.FindCommand(G("Ctrl+Shift+Z"), CommandScope.Editor, null));

        // Assigning a gesture bound in the same scope moves it; the loser's default is stored as removed.
        keymap.SetGestures("file.saveAll", [G("Ctrl+S")]);
        Assert.Empty(keymap.GetGestures("file.save"));
        Assert.Equal("file.saveAll", keymap.FindCommand(G("Ctrl+S"), CommandScope.Global, null));

        // A second instance reads the differences back.
        await keymap.FlushAsync();
        var reloaded = CreateKeymapService();
        reloaded.Load();
        Assert.Equal(["Ctrl+S"], reloaded.GetGestures("file.saveAll").Select(g => g.ToString()));
        Assert.Empty(reloaded.GetGestures("file.save"));
        Assert.Equal(["Ctrl+Y"], reloaded.GetGestures("edit.redo").Select(g => g.ToString()));
        Assert.Equal(["Shift+F9", "Ctrl+Shift+R"], reloaded.GetGestures("project.rebuild").Select(g => g.ToString()));

        // Reset All: defaults again, file with empty arrays.
        reloaded.ResetAll();
        await reloaded.FlushAsync();
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"bindings\": [],\n  \"removed\": []\n}\n", ReadKeymapFile());
        Assert.Equal(["Ctrl+S"], reloaded.GetGestures("file.save").Select(g => g.ToString()));

        // Reserved gestures cannot be bound (UI-KEY-008).
        Assert.Throws<ArgumentException>(() => reloaded.SetGestures("file.save", [G("Alt+F4")]));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-064")]
    public async Task ConflictResolutionOnLoad()
    {
        WriteKeymapFile("""
            {
              "formatVersion": 1,
              "bindings": [
                { "command": "future.command", "keys": ["Ctrl+Q"] },
                { "command": "file.saveAll", "keys": ["Ctrl+K, Ctrl+S", "Ctrl+Nope", "Ctrl+Alt+Delete", "Ctrl+E"] },
                { "command": "file.closeAll", "keys": ["Ctrl+E"] },
                { "command": "file.revert", "keys": ["Ctrl+O"] }
              ],
              "removed": []
            }
            """);
        var keymap = CreateKeymapService();

        keymap.Load();

        // Later binding wins over an earlier one, and a binding wins over a default.
        Assert.Equal("file.closeAll", keymap.FindCommand(G("Ctrl+E"), CommandScope.Global, null));
        Assert.Equal("file.revert", keymap.FindCommand(G("Ctrl+O"), CommandScope.Global, null));
        Assert.Empty(keymap.GetGestures("file.open"));
        Assert.Equal(["Ctrl+Shift+S"], keymap.GetGestures("file.saveAll").Select(g => g.ToString()));
        // Chord, unparsable, reserved, Ctrl+E lost by file.saveAll, Ctrl+O lost by file.open.
        Assert.Equal(5, _log.Lines.Count(l => l.StartsWith("WARN keymap: ", StringComparison.Ordinal)));
        Assert.Contains(_log.Lines, l => l.Contains("Ctrl+K, Ctrl+S", StringComparison.Ordinal));
        Assert.Contains(_log.Lines, l => l.Contains("Ctrl+Alt+Delete", StringComparison.Ordinal));

        // Unknown command ids are preserved on rewrite; dropped gestures are not.
        keymap.SetGestures("file.newUnit", [G("Ctrl+Shift+F12")]);
        await keymap.FlushAsync();
        var text = ReadKeymapFile();
        Assert.Contains("{ \"command\": \"future.command\", \"keys\": [\"Ctrl+Q\"] }", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+Nope", text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-065")]
    public void UnreadableKeymapIsRenamed()
    {
        WriteKeymapFile("{ not json");
        var keymap = CreateKeymapService();

        keymap.Load();

        Assert.True(_disk.FileExists(@"C:\u\roaming\keymap.corrupt-20261113-091500.json"));
        Assert.False(_disk.FileExists(KeymapFile));
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR keymap: ", StringComparison.Ordinal));
        Assert.Equal(DefaultKeymap.Bindings.Count, keymap.Current.Bindings.Count);
    }

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
