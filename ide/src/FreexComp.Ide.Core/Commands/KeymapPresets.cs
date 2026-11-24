using static FreexComp.Ide.Core.Commands.CommandScope;

namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// The built-in key maps the user's own assignments are applied on top of (setting <c>keyboard.preset</c>):
/// <c>default</c> (shortcuts.md §3) and <c>classic</c> (phase8-ui-additions.md D3).
/// </summary>
public static class KeymapPresets
{
    public const string Default = "default";

    public const string Classic = "classic";

    /// <summary>The preset names, in the order of the <c>Key map:</c> drop-down.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Classic];

    /// <summary>
    /// What <c>classic</c> binds differently: the table of D3 in its order, then the gestures of the commands that
    /// table displaces. A command named here gives up its function-key gestures of <c>default</c> and keeps its
    /// other ones; every other command keeps its <c>default</c> gestures.
    /// </summary>
    private static readonly IReadOnlyList<KeyBinding> ClassicRows =
    [
        new(KeyGesture.Parse("F9"), Global, null, "run.start"),
        new(KeyGesture.Parse("Ctrl+Shift+F9"), Global, null, "run.startWithoutDebugging"),
        new(KeyGesture.Parse("Ctrl+F2"), Global, null, "run.stop"),
        new(KeyGesture.Parse("F8"), Global, null, "run.stepOver"),
        new(KeyGesture.Parse("F7"), Global, null, "run.stepInto"),
        new(KeyGesture.Parse("Shift+F8"), Global, null, "run.stepOut"),
        new(KeyGesture.Parse("F4"), Editor, null, "run.runToCursor"),
        new(KeyGesture.Parse("F5"), Editor, null, "run.toggleBreakpoint"),
        new(KeyGesture.Parse("Ctrl+F5"), Editor, null, "run.addWatch"),
        new(KeyGesture.Parse("Ctrl+F9"), Global, null, "project.build"),
        new(KeyGesture.Parse("Shift+F9"), Global, null, "project.rebuild"),
        new(KeyGesture.Parse("F12"), Global, null, "view.toggleFormCode"),
        new(KeyGesture.Parse("F11"), Global, null, "view.objectInspector"),

        // Displaced by the rows above: the same key with Alt, and the breakpoint key with Shift.
        new(KeyGesture.Parse("Alt+F8"), Global, null, "search.nextMessage"),
        new(KeyGesture.Parse("Shift+Alt+F8"), Global, null, "search.previousMessage"),
        new(KeyGesture.Parse("Alt+F2"), Editor, null, "search.bookmark.toggle"),
        new(KeyGesture.Parse("Alt+F12"), Editor, null, "search.goToDeclaration"),
        new(KeyGesture.Parse("Shift+F5"), Editor, null, "run.toggleBreakpointEnabled"),
    ];

    /// <summary>Displaced commands without a sensible free gesture: unbound in <c>classic</c> (menu and palette remain).</summary>
    private static readonly string[] ClassicUnbound = ["run.deleteAllBreakpoints"];

    private static readonly IReadOnlyList<KeyBinding> ClassicBindings = BuildClassic();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> ClassicDisplayed = ClassicRows
        .GroupBy(b => b.CommandId, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<KeyGesture>)[.. g.Select(b => b.Gesture)], StringComparer.Ordinal);

    /// <summary><paramref name="preset"/> when it names a built-in key map, else <see cref="Default"/>.</summary>
    public static string Normalize(string? preset) => preset == Classic ? Classic : Default;

    /// <summary>The bindings of a preset (an unknown name means <see cref="Default"/>).</summary>
    public static IReadOnlyList<KeyBinding> Bindings(string preset) =>
        Normalize(preset) == Classic ? ClassicBindings : DefaultKeymap.Bindings;

    /// <summary>The preset as a key map with no user assignments.</summary>
    public static Keymap Create(string preset) => Create(preset, Bindings(preset));

    /// <summary><paramref name="bindings"/> as a key map that shows the gestures of <paramref name="preset"/> first (UI-KEY-005).</summary>
    internal static Keymap Create(string preset, IEnumerable<KeyBinding> bindings) =>
        new(bindings, Normalize(preset) == Classic ? ClassicDisplayed : null);

    private static List<KeyBinding> BuildClassic()
    {
        var replaced = ClassicRows.Select(b => b.CommandId).Concat(ClassicUnbound).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. DefaultKeymap.Bindings.Where(b => !(replaced.Contains(b.CommandId) && IsFunctionKey(b.Gesture))),
            .. ClassicRows,
        ];
    }

    private static bool IsFunctionKey(KeyGesture gesture) =>
        gesture.Key.Length is 2 or 3 && gesture.Key[0] == 'F' && gesture.Key.Skip(1).All(char.IsAsciiDigit);
}
