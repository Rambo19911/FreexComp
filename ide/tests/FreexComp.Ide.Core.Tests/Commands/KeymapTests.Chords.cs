using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>Two-key chords (shortcuts.md UI-KEY-006, UI-KEY-010…013; I-342).</summary>
public sealed partial class KeymapTests
{
    private static readonly FocusState EditorFocus = new(CommandScope.Editor, null, null, IsTextInput: false);

    [Theory(Timeout = 30000)]
    [Trait("Req", "UI-KEY-006")]
    [Trait("Req", "IDE-SET-062")]
    [InlineData("Ctrl+K, Ctrl+F", "Ctrl+K, Ctrl+F")]
    [InlineData("ctrl+k,   CTRL+f", "Ctrl+K, Ctrl+F")]
    [InlineData("Shift+Ctrl+K, F", "Ctrl+Shift+K, F")]
    [InlineData("Ctrl+,, Ctrl+F", "Ctrl+,, Ctrl+F")] // the key named "," as the first key
    [InlineData("Ctrl+K, ,", "Ctrl+K, ,")]           // and as the second
    [InlineData("F8, Ctrl+Shift+Enter", "F8, Ctrl+Shift+Enter")]
    public void ChordGestureParsesAndFormats(string text, string canonical)
    {
        var chord = KeyGesture.Parse(text);

        Assert.True(chord.IsChord);
        Assert.Equal(canonical, chord.ToString());
        Assert.Equal(chord, KeyGesture.Parse(canonical));
        Assert.Equal(chord, KeyGesture.Chord(chord.First, chord.Second!.Value));
        Assert.False(chord.First.IsChord);
        Assert.Null(chord.First.Second);
        Assert.True(chord.StartsWith(chord.First));
        Assert.NotEqual(chord, chord.First); // a chord is not its first key
    }

    [Theory(Timeout = 30000)]
    [Trait("Req", "UI-KEY-006")]
    [InlineData("Ctrl+K, Ctrl+Nope")]
    [InlineData("Nope, Ctrl+K")]
    [InlineData("Ctrl+K,Ctrl+F")]          // the separator is a comma and a space
    [InlineData("Ctrl+K, Ctrl+F, Ctrl+G")] // two keys, not three
    [InlineData("Ctrl+K, ")]
    [InlineData(", Ctrl+K")]
    public void InvalidChordIsRejected(string text)
    {
        Assert.False(KeyGesture.TryParse(text, out _));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-006")]
    [Trait("Req", "UI-KEY-010")]
    public void ChordGestureRunsCommand()
    {
        var (service, _, status) = CreateChordService(out var keymap);
        var format = new CountingHandler();
        var find = new CountingHandler();
        service.RegisterHandler("edit.formatSelection", format);
        service.RegisterHandler("search.find", find);
        Assert.Equal("edit.formatSelection", keymap.FindCommand(G("Ctrl+K, Ctrl+F"), CommandScope.Editor, null));

        // First key: consumed, nothing runs. Second key: the chord's command runs — not the second key's own command.
        Assert.True(service.HandleKey(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.Equal(0, format.Calls);
        Assert.True(service.HandleKey(G("Ctrl+F"), CommandScope.Editor, null));

        Assert.Equal(1, format.Calls);
        Assert.Equal(0, find.Calls);
        Assert.False(service.IsChordPending);
        Assert.Null(status.Message); // the waiting text is gone

        // Outside the chord the second key is itself again; where the chord's scope does not apply, the first key is unbound.
        Assert.True(service.HandleKey(G("Ctrl+F"), CommandScope.Editor, null));
        Assert.Equal(1, find.Calls);
        Assert.False(service.HandleKey(G("Ctrl+K"), CommandScope.ProjectManager, null));
        Assert.False(service.IsChordPending);

        // A disabled chord command is consumed like a disabled single key (UI-KEY-007).
        format.Enabled = false;
        Assert.True(service.HandleKey(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.True(service.HandleKey(G("Ctrl+F"), CommandScope.Editor, null));
        Assert.Equal(1, format.Calls);
        Assert.Equal(1, find.Calls);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-010")]
    public void ChordPrefixWaitsForSecondKey()
    {
        var (service, _, status) = CreateChordService(out _);
        var format = new CountingHandler();
        var save = new CountingHandler();
        service.RegisterHandler("edit.formatSelection", format);
        service.RegisterHandler("file.save", save);

        Assert.False(service.IsChordPending);
        Assert.True(service.HandleKey(G("Ctrl+K"), CommandScope.Editor, null));

        // The IDE waits, and says so for as long as it waits (sticky: no expiry).
        Assert.True(service.IsChordPending);
        Assert.Equal("Ctrl+K was pressed. Waiting for the second key…", status.Message);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("Ctrl+K was pressed. Waiting for the second key…", status.Message);
        Assert.True(service.IsChordPending);

        // A second key that completes no chord cancels: it is consumed, its own command does not run, a message says why.
        Assert.True(service.HandleKey(G("Ctrl+S"), CommandScope.Editor, null));
        Assert.Equal(0, save.Calls);
        Assert.Equal(0, format.Calls);
        Assert.False(service.IsChordPending);
        Assert.Equal("Ctrl+K, Ctrl+S is not a shortcut.", status.Message);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(status.Message);

        // Afterwards keys are themselves again.
        Assert.True(service.HandleKey(G("Ctrl+S"), CommandScope.Editor, null));
        Assert.Equal(1, save.Calls);

        // The same holds for a key no command uses: consumed, not passed on to the focused control.
        Assert.True(service.HandleKey(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.True(service.HandleKey(G("X"), CommandScope.Editor, null));
        Assert.Equal("Ctrl+K, X is not a shortcut.", status.Message);
        Assert.False(service.HandleKey(G("X"), CommandScope.Editor, null));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-011")]
    public void ChordIsCancelledByEscapeFocusChangeAndClick()
    {
        var (service, focus, status) = CreateChordService(out _);
        var format = new CountingHandler();
        var find = new CountingHandler();
        service.RegisterHandler("edit.formatSelection", format);
        service.RegisterHandler("search.find", find);
        focus.Update(EditorFocus);

        // Escape: consumed (not passed on), no message, the next key is not a second key.
        Assert.True(service.HandleKey(G("Ctrl+K")));
        Assert.True(service.HandleKey(G("Escape")));
        Assert.False(service.IsChordPending);
        Assert.Null(status.Message);
        Assert.True(service.HandleKey(G("Ctrl+F")));
        Assert.Equal((0, 1), (format.Calls, find.Calls));
        Assert.False(service.HandleKey(G("Escape"))); // without a pending chord Escape is the control's again

        // Focus moves elsewhere.
        Assert.True(service.HandleKey(G("Ctrl+K")));
        Assert.True(service.IsChordPending);
        focus.Update(new FocusState(CommandScope.ToolList, "Messages", null, IsTextInput: false));
        Assert.False(service.IsChordPending);
        Assert.Null(status.Message);
        focus.Update(EditorFocus);
        Assert.True(service.HandleKey(G("Ctrl+F")));
        Assert.Equal((0, 2), (format.Calls, find.Calls));

        // A mouse click (the view reports it through CancelChord); cancelling twice or with nothing pending is harmless.
        Assert.True(service.HandleKey(G("Ctrl+K")));
        service.CancelChord();
        service.CancelChord();
        Assert.False(service.IsChordPending);
        Assert.Null(status.Message);
        Assert.True(service.HandleKey(G("Ctrl+F")));
        Assert.Equal((0, 3), (format.Calls, find.Calls));

        // A message that replaced the waiting text is not cleared by the cancel.
        Assert.True(service.HandleKey(G("Ctrl+K")));
        status.ShowMessage("Build succeeded", TimeSpan.FromSeconds(5));
        service.CancelChord();
        Assert.Equal("Build succeeded", status.Message);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-012")]
    [Trait("Req", "UI-KEY-004")]
    public void SingleKeyBindingWinsOverChordPrefix()
    {
        // Same scope level: the single key is the command; the chord cannot be reached.
        var same = new Keymap(
        [
            new KeyBinding(G("Ctrl+K"), CommandScope.Editor, null, "edit.deleteLine"),
            new KeyBinding(G("Ctrl+K, Ctrl+F"), CommandScope.Editor, null, "edit.formatSelection"),
        ]);
        Assert.False(same.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.Equal("edit.deleteLine", same.FindCommand(G("Ctrl+K"), CommandScope.Editor, null));

        // Focus and Global are one level.
        var outer = new Keymap(
        [
            new KeyBinding(G("Ctrl+K"), CommandScope.Focus, null, "edit.copy"),
            new KeyBinding(G("Ctrl+K, Ctrl+S"), CommandScope.Global, null, "file.saveAll"),
        ]);
        Assert.False(outer.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

        // Different levels: the innermost one decides (UI-KEY-004), whichever kind it holds.
        var chordInside = new Keymap(
        [
            new KeyBinding(G("Ctrl+K"), CommandScope.Global, null, "file.save"),
            new KeyBinding(G("Ctrl+K, Ctrl+F"), CommandScope.Editor, null, "edit.formatSelection"),
        ]);
        Assert.True(chordInside.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.False(chordInside.IsChordPrefix(G("Ctrl+K"), CommandScope.Designer, null));
        Assert.False(chordInside.IsChordPrefix(G("Ctrl+K"), CommandScope.Global, null));
        Assert.Equal("file.save", chordInside.FindCommand(G("Ctrl+K"), CommandScope.Designer, null));

        var singleInside = new Keymap(
        [
            new KeyBinding(G("Ctrl+K"), CommandScope.ToolList, "Messages", "messages.goToSource"),
            new KeyBinding(G("Ctrl+K, Ctrl+S"), CommandScope.Global, null, "file.saveAll"),
        ]);
        Assert.False(singleInside.IsChordPrefix(G("Ctrl+K"), CommandScope.ToolList, "Messages"));
        Assert.True(singleInside.IsChordPrefix(G("Ctrl+K"), CommandScope.ToolList, "Output"));
        Assert.True(singleInside.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

        // Not a prefix: a key no chord starts with, and a chord itself.
        Assert.False(chordInside.IsChordPrefix(G("Ctrl+F"), CommandScope.Editor, null));
        Assert.False(chordInside.IsChordPrefix(G("Ctrl+K, Ctrl+F"), CommandScope.Editor, null));

        // The command service follows the key map: with the single key bound in the same level the chord never starts.
        var service = new CommandService(new FixedKeymap(same), new ImmediateDispatcher(), _clock, _log, new FakeDialogService(), new StatusService(_clock), new FocusTracker());
        var format = new CountingHandler();
        var delete = new CountingHandler();
        service.RegisterHandler("edit.formatSelection", format);
        service.RegisterHandler("edit.deleteLine", delete);
        Assert.True(service.HandleKey(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.False(service.IsChordPending);
        Assert.Equal((1, 0), (delete.Calls, format.Calls));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-012")]
    [Trait("Req", "UI-KEY-021")]
    public void AssigningAcrossAChordPrefixMovesTheKey()
    {
        var keymap = CreateKeymapService();
        keymap.Load();
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Texts(keymap.GetGestures("edit.formatSelection")));

        // A single key takes the first key away from the chords of the same scope level …
        keymap.SetGestures("edit.deleteLine", [G("Ctrl+Shift+K"), G("Ctrl+K")]);
        Assert.Equal(["Shift+Alt+S"], Texts(keymap.GetGestures("edit.formatSelection")));
        Assert.False(keymap.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

        // … and a chord takes its first key away from the single-key binding there.
        keymap.SetGestures("edit.formatSelection", [G("Shift+Alt+S"), G("Ctrl+K, Ctrl+F")]);
        Assert.Equal(["Ctrl+Shift+K"], Texts(keymap.GetGestures("edit.deleteLine")));
        Assert.True(keymap.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

        // Two chords share a first key without conflict; the same chord moves like any gesture.
        keymap.SetGestures("edit.deleteLine", [G("Ctrl+K, Ctrl+D")]);
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Texts(keymap.GetGestures("edit.formatSelection")));
        keymap.SetGestures("edit.deleteLine", [G("Ctrl+K, Ctrl+F")]);
        Assert.Equal(["Shift+Alt+S"], Texts(keymap.GetGestures("edit.formatSelection")));

        // Another scope level keeps its binding (innermost wins at run time).
        keymap.ResetAll();
        keymap.SetGestures("file.saveAll", [G("Ctrl+Shift+S"), G("Ctrl+K")]); // Global
        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Texts(keymap.GetGestures("edit.formatSelection")));
        Assert.True(keymap.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));
        Assert.Equal("file.saveAll", keymap.FindCommand(G("Ctrl+K"), CommandScope.Designer, null));

        // Reserved keys stay reserved inside a chord (UI-KEY-008); Escape cannot be a second key (it cancels).
        Assert.Throws<ArgumentException>(() => keymap.SetGestures("file.save", [G("Ctrl+K, Alt+F4")]));
        Assert.Throws<ArgumentException>(() => keymap.SetGestures("file.save", [G("Alt+F4, Ctrl+K")]));
        Assert.Throws<ArgumentException>(() => keymap.SetGestures("file.save", [G("Ctrl+K, Escape")]));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-006")]
    [Trait("Req", "IDE-SET-062")]
    [Trait("Req", "IDE-SET-064")]
    public Task ChordsRoundTripThroughKeymapFile() => TestUiThread.RunAsync(() => ChordsRoundTripThroughKeymapFileOnUiThread());

    private async Task ChordsRoundTripThroughKeymapFileOnUiThread()
    {
        WriteKeymapFile("""
            {
              "formatVersion": 1,
              "bindings": [
                { "command": "file.saveAll", "keys": ["ctrl+e,  CTRL+w", "Ctrl+E, Nope", "Ctrl+E, Alt+F4", "Ctrl+E, Escape", "Ctrl+E, Ctrl+W, Ctrl+X"] },
                { "command": "future.command", "keys": ["Ctrl+Q, Ctrl+Q"] }
              ],
              "removed": [
                { "command": "edit.formatSelection", "keys": ["Ctrl+K, Ctrl+F"] }
              ]
            }
            """);
        var keymap = CreateKeymapService();

        keymap.Load();

        // The chord is honoured; the default chord named in "removed" is gone.
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+E, Ctrl+W"], Texts(keymap.GetGestures("file.saveAll")));
        Assert.Equal("file.saveAll", keymap.FindCommand(G("Ctrl+E, Ctrl+W"), CommandScope.Editor, null));
        Assert.True(keymap.IsChordPrefix(G("Ctrl+E"), CommandScope.Global, null));
        Assert.Equal(["Shift+Alt+S"], Texts(keymap.GetGestures("edit.formatSelection")));
        Assert.False(keymap.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

        // Invalid chords are reported like other invalid gestures: unparsable twice, reserved twice.
        var warnings = _log.Lines.Where(l => l.StartsWith("WARN keymap: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, l => l.Contains("'Ctrl+E, Nope'", StringComparison.Ordinal) && l.Contains("not a valid gesture", StringComparison.Ordinal));
        Assert.Contains(warnings, l => l.Contains("'Ctrl+E, Ctrl+W, Ctrl+X'", StringComparison.Ordinal) && l.Contains("not a valid gesture", StringComparison.Ordinal));
        Assert.Contains(warnings, l => l.Contains("'Ctrl+E, Alt+F4'", StringComparison.Ordinal) && l.Contains("reserved", StringComparison.Ordinal));
        Assert.Contains(warnings, l => l.Contains("'Ctrl+E, Escape'", StringComparison.Ordinal) && l.Contains("reserved", StringComparison.Ordinal));

        // Written back in canonical form, in user order; unknown commands keep their text.
        keymap.SetGestures("file.closeAll", [G("Ctrl+Shift+W"), G("Ctrl+E, Ctrl+A")]);
        await keymap.FlushAsync();
        Assert.Equal(
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"bindings\": [\n" +
            "    { \"command\": \"file.closeAll\", \"keys\": [\"Ctrl+E, Ctrl+A\"] },\n" +
            "    { \"command\": \"file.saveAll\", \"keys\": [\"Ctrl+E, Ctrl+W\"] },\n" +
            "    { \"command\": \"future.command\", \"keys\": [\"Ctrl+Q, Ctrl+Q\"] }\n" +
            "  ],\n" +
            "  \"removed\": [\n" +
            "    { \"command\": \"edit.formatSelection\", \"keys\": [\"Ctrl+K, Ctrl+F\"] }\n" +
            "  ]\n" +
            "}\n",
            ReadKeymapFile());

        var reloaded = CreateKeymapService();
        reloaded.Load();
        Assert.Equal(["Ctrl+Shift+W", "Ctrl+E, Ctrl+A"], Texts(reloaded.GetGestures("file.closeAll")));
        Assert.Equal(["Ctrl+Shift+S", "Ctrl+E, Ctrl+W"], Texts(reloaded.GetGestures("file.saveAll")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-013")]
    [Trait("Req", "UI-KEY-005")]
    [Trait("Req", "UI-MENU-005")]
    public void ChordIsShownInGestureTexts()
    {
        var (service, _, _) = CreateChordService(out var keymap);
        service.RegisterHandler("edit.formatSelection", new CountingHandler());
        service.RegisterHandler("edit.formatDocument", new CountingHandler());
        var selection = service.Get("edit.formatSelection");
        var document = service.Get("edit.formatDocument");

        // The first gesture is the displayed one: the chord is the second default of edit.formatSelection.
        Assert.Equal("Shift+Alt+S", selection.GestureText);

        // Lists of gestures: ", " between single keys, "; " as soon as one of them is a chord.
        Assert.Equal("Ctrl+Y, Ctrl+Shift+Z", KeyGesture.Join([G("Ctrl+Y"), G("Ctrl+Shift+Z")]));
        Assert.Equal("Shift+Alt+S; Ctrl+K, Ctrl+F", KeyGesture.Join([G("Shift+Alt+S"), G("Ctrl+K, Ctrl+F")]));
        Assert.Equal("Ctrl+K, Ctrl+F", KeyGesture.Join([G("Ctrl+K, Ctrl+F")]));
        Assert.Equal("", KeyGesture.Join([]));

        // Help > Keyboard Shortcuts lists them the same way.
        var lines = KeyboardShortcutsDocument.Build(keymap).Split(Environment.NewLine);
        Assert.Contains(lines, l => l.Contains("Format Selection", StringComparison.Ordinal) && l.EndsWith("  Shift+Alt+S; Ctrl+K, Ctrl+F", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Redo", StringComparison.Ordinal) && l.EndsWith("  Ctrl+Y, Ctrl+Shift+Z", StringComparison.Ordinal));

        // A chord as the first gesture is shown in full, as written in keymap.json; the text follows the key map (UI-KEY-023).
        keymap.SetGestures("edit.formatDocument", [G("Ctrl+K, Ctrl+D")]);
        Assert.Equal("Ctrl+K, Ctrl+D", document.GestureText);
        keymap.SetGestures("edit.formatSelection", [G("Ctrl+K, Ctrl+F")]);
        Assert.Equal("Ctrl+K, Ctrl+F", selection.GestureText);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-KEY-006")]
    public void FormatSelectionChordIsInBothKeyMaps()
    {
        foreach (var preset in KeymapPresets.All)
        {
            var keymap = KeymapPresets.Create(preset);

            Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], Texts(keymap.GetGestures("edit.formatSelection")));
            Assert.Equal("edit.formatSelection", keymap.FindCommand(G("Ctrl+K, Ctrl+F"), CommandScope.Editor, null));
            Assert.Null(keymap.FindCommand(G("Ctrl+K, Ctrl+F"), CommandScope.Designer, null)); // Editor scope only
            Assert.True(keymap.IsChordPrefix(G("Ctrl+K"), CommandScope.Editor, null));

            // Ctrl+K is free as a single key in every scope, so nothing hides the chord.
            Assert.DoesNotContain(keymap.Bindings, b => b.Gesture == G("Ctrl+K"));
        }

        Assert.Equal(["Shift+Alt+S", "Ctrl+K, Ctrl+F"], CommandCatalog.Get("edit.formatSelection").DefaultGestures);
    }

    private static List<string> Texts(IEnumerable<KeyGesture> gestures) => [.. gestures.Select(g => g.ToString())];

    /// <summary>A command service over a real key map (defaults), a real status service on the manual clock and a focus tracker.</summary>
    private (CommandService Service, FocusTracker Focus, StatusService Status) CreateChordService(out KeymapService keymap)
    {
        keymap = CreateKeymapService();
        var focus = new FocusTracker();
        var status = new StatusService(_clock);
        return (new CommandService(keymap, new ImmediateDispatcher(), _clock, _log, new FakeDialogService(), status, focus), focus, status);
    }

    /// <summary>A key map as given, for combinations the key map service never produces.</summary>
    private sealed class FixedKeymap(Keymap keymap) : IKeymapService
    {
        public event EventHandler? Changed { add { } remove { } }

        public Keymap Current => keymap;

        public string Preset => KeymapPresets.Default;

        public void SetPreset(string preset) => throw new NotSupportedException();

        public IReadOnlyList<KeyGesture> GetGestures(string commandId) => keymap.GetGestures(commandId);

        public string? FindCommand(KeyGesture gesture, CommandScope focusScope, string? toolWindow) => keymap.FindCommand(gesture, focusScope, toolWindow);

        public KeyBinding? FindBinding(KeyGesture gesture, CommandScope focusScope, string? toolWindow) => keymap.FindBinding(gesture, focusScope, toolWindow);

        public bool IsChordPrefix(KeyGesture gesture, CommandScope focusScope, string? toolWindow) => keymap.IsChordPrefix(gesture, focusScope, toolWindow);

        public void SetGestures(string commandId, IReadOnlyList<KeyGesture> gestures) => throw new NotSupportedException();

        public void ResetAll() => throw new NotSupportedException();

        public Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class CountingHandler : ICommandHandler
    {
        public bool Enabled { get; set; } = true;

        public int Calls { get; private set; }

        public bool CanExecute(string? argument) => Enabled;

        public Task ExecuteAsync(string? argument)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
