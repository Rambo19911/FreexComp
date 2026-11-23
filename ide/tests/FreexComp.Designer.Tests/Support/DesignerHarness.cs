using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Support;

/// <summary>
/// Controller-level interaction tests (testing.md §3): a form loaded with the test metadata, a
/// <see cref="DesignSurfaceController"/> over it with fake settings and host, and pointer helpers that take
/// control-relative points. <see cref="Dispose"/> asserts the layout invariant (DSG-LAY-040) and that the form text
/// parses without diagnostics (DSG-TEST-020) unless <see cref="SkipFinalChecks"/> is set.
/// </summary>
internal sealed class DesignerHarness : IDisposable
{
    private (int X, int Y) _pointer;
    private InputModifiers _pressModifiers;

    private DesignerHarness(FormDocument document, FreexComp.Designer.Catalog.ComponentCatalog catalog)
    {
        Document = document;
        _catalog = catalog;
        Editor = new FormEditor(document, catalog);
        Undo = new DesignerUndoStack();
        Engine = new LayoutEngine(catalog, RendererRegistry.CreateDefault());
        Controller = new DesignSurfaceController(document, Editor, Undo, catalog, Engine, Settings, Host, Time);
    }

    private readonly FreexComp.Designer.Catalog.ComponentCatalog _catalog;

    /// <summary>A form of the shared test data (<c>forms/canonical/hello.form</c>).</summary>
    public static DesignerHarness FromFile(string relativePath) => FromText(File.ReadAllText(TestPaths.Combine(relativePath)), relativePath);

    /// <param name="allowDiagnostics">The form may have diagnostics (preserved items: unknown classes, inherited objects).</param>
    /// <param name="catalog">The metadata: the test metadata when null (the framework metadata is <c>LayoutHarness.Catalog</c>).</param>
    public static DesignerHarness FromText(string text, string path = "test.form", bool allowDiagnostics = false,
        FreexComp.Designer.Catalog.ComponentCatalog? catalog = null)
    {
        catalog ??= TestCatalog.Instance;
        var result = FormParser.Parse(text.Replace("\r\n", "\n"), path, catalog);
        var document = result.Document ?? throw new InvalidOperationException("form did not parse:\n" + text);
        Assert.True(allowDiagnostics || document.Diagnostics.Count == 0, string.Join("\n", document.Diagnostics));
        return new DesignerHarness(document, catalog) { AllowDiagnostics = allowDiagnostics };
    }

    /// <summary>The form has diagnostics by design: <see cref="Dispose"/> checks that the text still parses, not that it is clean.</summary>
    private bool AllowDiagnostics { get; init; }

    public FakeDesignerSettings Settings { get; } = new();

    public FakeDesignerHost Host { get; } = new();

    /// <summary>The controller's clock (testing.md DSG-TEST-004): advanced explicitly, never the wall clock.</summary>
    public ManualTimeProvider Time { get; } = new();

    public FormDocument Document { get; }

    public FormEditor Editor { get; }

    public DesignerUndoStack Undo { get; }

    public LayoutEngine Engine { get; }

    public DesignSurfaceController Controller { get; }

    public Selection Selection => Controller.Selection;

    public LayoutResult Layout => Controller.Layout;

    /// <summary>The selected names in selection order (last = primary).</summary>
    public IReadOnlyList<string> Selected => Selection.Items.Select(o => o.Name).ToList();

    /// <summary>The form text (<see cref="FormWriter"/>).</summary>
    public string Text => FormWriter.Write(Document);

    /// <summary>Set to skip the checks of <see cref="Dispose"/> (tests about invalid states).</summary>
    public bool SkipFinalChecks { get; set; }

    public FormObject Object(string name) => Document.FindByName(name) ?? throw new ArgumentException("no component " + name, nameof(name));

    /// <summary>Stored-as-computed position in parent client coordinates.</summary>
    public (int X, int Y) Position(string name)
    {
        var b = Layout.GetBounds(Object(name));
        return (b.Left, b.Top);
    }

    public Rect Bounds(string name) => Layout.GetBounds(Object(name));

    /// <summary>Stored-as-computed size.</summary>
    public (int Width, int Height) Size(string name)
    {
        var b = Layout.GetBounds(Object(name));
        return (b.Width, b.Height);
    }

    /// <summary>The name of the component's parent.</summary>
    public string Parent(string name) => Object(name).Parent?.Name ?? throw new ArgumentException(name + " is the root", nameof(name));

    /// <summary>The names of the direct children in order (creation / z-order).</summary>
    public IReadOnlyList<string> ChildNames(string name) => Object(name).Children.Select(c => c.Name).ToList();

    /// <summary>The guidelines currently shown (DSG-SUR-074).</summary>
    public IReadOnlyList<Guideline> Guidelines => Controller.Guidelines;

    /// <summary>A control-relative point (default: the centre) of <paramref name="on"/> in surface coordinates.</summary>
    public (int X, int Y) SurfacePoint(string on, (int X, int Y)? at = null)
    {
        var bounds = Controller.Geometry.GetBounds(Object(on));
        var (x, y) = at ?? (bounds.Width / 2, bounds.Height / 2);
        return (bounds.Left + x, bounds.Top + y);
    }

    public void Press(string on, (int X, int Y)? at = null, InputModifiers modifiers = InputModifiers.None) =>
        PressAt(SurfacePoint(on, at), modifiers);

    /// <summary>Left button down on a resize handle of <paramref name="on"/>.</summary>
    public void PressHandle(string on, HandleKind handle, InputModifiers modifiers = InputModifiers.None) =>
        PressAt(HandlePoint(on, handle), modifiers);

    /// <summary>The centre of a handle in surface coordinates (centred on the outline pixels: L, L + W − 1, L + (W − 1) div 2).</summary>
    public (int X, int Y) HandlePoint(string on, HandleKind handle)
    {
        var b = Controller.Geometry.GetBounds(Object(on));
        var (l, t, r, bottom) = (b.Left, b.Top, b.Right - 1, b.Bottom - 1);
        var (mx, my) = (b.Left + (b.Width - 1) / 2, b.Top + (b.Height - 1) / 2);
        return handle switch
        {
            HandleKind.NW => (l, t),
            HandleKind.N => (mx, t),
            HandleKind.NE => (r, t),
            HandleKind.E => (r, my),
            HandleKind.SE => (r, bottom),
            HandleKind.S => (mx, bottom),
            HandleKind.SW => (l, bottom),
            _ => (l, my),
        };
    }

    /// <summary>Left button down at a surface point.</summary>
    public void PressAt((int X, int Y) surface, InputModifiers modifiers = InputModifiers.None, int clickCount = 1)
    {
        _pointer = surface;
        _pressModifiers = modifiers;
        Controller.OnPointer(new PointerInput(PointerKind.Down, surface.X, surface.Y, PointerButton.Left, modifiers, clickCount));
    }

    /// <summary>Pointer move relative to the last pointer position; <paramref name="modifiers"/> default to those of the press.</summary>
    public void MoveBy(int dx, int dy, InputModifiers? modifiers = null) => MoveTo((_pointer.X + dx, _pointer.Y + dy), modifiers);

    public void MoveTo((int X, int Y) surface, InputModifiers? modifiers = null)
    {
        _pointer = surface;
        Controller.OnPointer(new PointerInput(PointerKind.Move, surface.X, surface.Y, PointerButton.None, modifiers ?? _pressModifiers));
    }

    /// <summary>Left button up at the last pointer position; <paramref name="modifiers"/> default to those of the press.</summary>
    public void Release(InputModifiers? modifiers = null) =>
        Controller.OnPointer(new PointerInput(PointerKind.Up, _pointer.X, _pointer.Y, PointerButton.Left, modifiers ?? _pressModifiers));

    public void CaptureLost() =>
        Controller.OnPointer(new PointerInput(PointerKind.CaptureLost, _pointer.X, _pointer.Y, PointerButton.None, InputModifiers.None));

    /// <summary>A key in the Designer scope; true = handled.</summary>
    public bool Key(DesignKey key, InputModifiers modifiers = InputModifiers.None) => Controller.OnKey(new KeyInput(key, modifiers));

    public void Execute(string commandId, string? argument = null) => Controller.Execute(commandId, argument);

    public void ExecuteUndo() => Execute("edit.undo");

    public void ExecuteRedo() => Execute("edit.redo");

    public void Click(string on, (int X, int Y)? at = null, InputModifiers modifiers = InputModifiers.None) =>
        ClickAt(SurfacePoint(on, at), modifiers);

    public void ClickAt((int X, int Y) surface, InputModifiers modifiers = InputModifiers.None)
    {
        PressAt(surface, modifiers);
        Release();
    }

    public void RightClick(string on, (int X, int Y)? at = null)
    {
        var (x, y) = SurfacePoint(on, at);
        Controller.OnPointer(new PointerInput(PointerKind.Down, x, y, PointerButton.Right, InputModifiers.None));
        Controller.OnPointer(new PointerInput(PointerKind.Up, x, y, PointerButton.Right, InputModifiers.None));
    }

    public void Select(params string[] names) => Selection.Set(names);

    /// <summary>DSG-LAY-040: every visual component's stored bounds equal its computed bounds.</summary>
    public void AssertInvariant()
    {
        var differences = Engine.Compute(Document).Differences();
        Assert.True(differences.Count == 0,
            string.Join("\n", differences.Select(d => $"{d.Object.Name}: stored {d.Stored}, computed {d.Computed}")));
    }

    public void Dispose()
    {
        if (SkipFinalChecks)
        {
            return;
        }

        AssertInvariant();
        var reparsed = FormParser.Parse(Text, "check.form", _catalog);
        Assert.True(reparsed.Document is { } doc && (AllowDiagnostics || doc.Diagnostics.Count == 0),
            string.Join("\n", reparsed.Document?.Diagnostics ?? []));
    }
}

/// <summary>In-memory <see cref="IDesignerSettings"/> (testing.md DSG-TEST-005).</summary>
internal sealed class FakeDesignerSettings : IDesignerSettings
{
    public bool ShowComponentCaptions { get; set; } = true;

    public int HandleSize { get; set; } = 7;

    public bool ShowGrid { get; set; } = true;

    public bool SnapToGrid { get; set; } = true;

    public int GridSizeX { get; set; } = 8;

    public int GridSizeY { get; set; } = 8;

    public bool UseGuidelines { get; set; } = true;

    public int GuidelineSnapDistance { get; set; } = 4;

    public int GuidelineMargin { get; set; } = 8;

    public bool LockControls { get; set; }

    /// <summary>No grid, no guidelines: pointer deltas apply unchanged.</summary>
    public void NoSnapping()
    {
        SnapToGrid = false;
        UseGuidelines = false;
    }
}

/// <summary>Records what the controller asks of the IDE.</summary>
internal sealed class FakeDesignerHost : IDesignerHost
{
    public List<(string Id, object[] Args)> Statuses { get; } = [];

    public List<(double X, double Y)> ContextMenus { get; } = [];

    /// <summary>Object Inspector edits started by typing on the surface (DSG-SUR-105): property and typed text.</summary>
    public List<(string Property, string Text)> PropertyEdits { get; } = [];

    public void SetStatus(string messageId, params object[] args) => Statuses.Add((messageId, args));

    /// <summary>The error dialogs the controller asked for (DSG-CS-004): message id and arguments.</summary>
    public List<(string Id, object[] Args)> ErrorDialogs { get; } = [];

    public void ShowErrorDialog(string messageId, params object[] args) => ErrorDialogs.Add((messageId, args));

    public void ShowContextMenu(double x, double y) => ContextMenus.Add((x, y));

    public void BeginPropertyEdit(string propertyName, string text) => PropertyEdits.Add((propertyName, text));

    /// <summary>The armed palette class (tool-palette.md DSG-TP-040, 041); set by <see cref="Arm"/>.</summary>
    public string? ArmedPaletteClass { get; private set; }

    public bool ArmedSticky { get; private set; }

    /// <summary>How often the controller disarmed the palette.</summary>
    public int DisarmCount { get; private set; }

    /// <summary>The palette arms <paramref name="className"/> (click; Shift+click = <paramref name="sticky"/>).</summary>
    public void Arm(string className, bool sticky = false) => (ArmedPaletteClass, ArmedSticky) = (className, sticky);

    public void Disarm()
    {
        (ArmedPaletteClass, ArmedSticky) = (null, false);
        DisarmCount++;
    }

    /// <summary>The in-memory clipboard (testing.md §3).</summary>
    public FakeClipboard Clipboard { get; } = new();

    IDesignerClipboard IDesignerHost.Clipboard => Clipboard;

    /// <summary>The fake code sync (testing.md §3).</summary>
    public FakeCodeSync CodeSync { get; } = new();

    /// <summary>The collection editors the surface asked for (DSG-SUR-195): component and property.</summary>
    public List<(string Component, string Property)> CollectionEdits { get; } = [];

    /// <summary>The component editors the surface asked for (DSG-SUR-196).</summary>
    public List<string> ComponentEdits { get; } = [];

    public void EditCollection(FormObject component, string propertyName) => CollectionEdits.Add((component.Name, propertyName));

    public void EditComponent(FormObject component) => ComponentEdits.Add(component.Name);

    ICodeSync IDesignerHost.CodeSync => CodeSync;
}

/// <summary>An in-memory clipboard: every <see cref="SetData"/> (or <see cref="SetText"/>) replaces all formats and bumps the sequence number.</summary>
internal sealed class FakeClipboard : IDesignerClipboard
{
    private Dictionary<string, string> _formats = [];

    public long SequenceNumber { get; private set; }

    public string? GetData(string format) => _formats.GetValueOrDefault(format);

    public void SetData(IReadOnlyDictionary<string, string> formats)
    {
        _formats = new Dictionary<string, string>(formats);
        SequenceNumber++;
    }

    /// <summary>Another application put plain text on the clipboard.</summary>
    public void SetText(string text) => SetData(new Dictionary<string, string> { [ClipboardFormats.Text] = text });

    /// <summary>The formats present now.</summary>
    public IReadOnlyCollection<string> Formats => _formats.Keys;
}

/// <summary>
/// A fake form class: <see cref="Members"/> maps member names to their kind; <see cref="Methods"/> maps method names to the
/// event signature type they are compatible with (DSG-CS-050).
/// </summary>
internal sealed class FakeCodeSync : ICodeSync
{
    public Dictionary<string, MemberLookup> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Methods { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MemberLookup LookupMember(string name) =>
        Members.TryGetValue(name, out var member) ? member
        : Methods.ContainsKey(name) ? MemberLookup.Method : MemberLookup.None;

    public IReadOnlyList<string> GetCompatibleMethods(string eventSignature) =>
        Methods.Where(m => string.Equals(m.Value, eventSignature, StringComparison.OrdinalIgnoreCase)).Select(m => m.Key).Order().ToList();

    /// <summary>The handlers created (<see cref="CreateHandler"/>): method name and signature text.</summary>
    public List<(string Name, string Signature)> CreatedHandlers { get; } = [];

    /// <summary>The methods navigated to (<see cref="NavigateToHandler"/>).</summary>
    public List<string> Navigations { get; } = [];

    /// <summary>The text edits the last <see cref="CreateHandler"/> reported.</summary>
    public IReadOnlyList<TextEditRecord> LastEdits { get; private set; } = [];

    public IReadOnlyList<TextEditRecord> CreateHandler(string methodName, string signatureText)
    {
        CreatedHandlers.Add((methodName, signatureText));
        LastEdits = [new TextEditRecord(100 * CreatedHandlers.Count, "", "procedure " + methodName + ";")];
        return LastEdits;
    }

    public void NavigateToHandler(string methodName) => Navigations.Add(methodName);

    /// <summary>The field lists <see cref="AddFields"/> was called with (DSG-TP-053, DSG-SUR-127).</summary>
    public List<IReadOnlyList<CodeSyncField>> AddedFields { get; } = [];

    /// <summary>What <see cref="AddFields"/> reports as applied: none by default (the fields exist already).</summary>
    public Func<IReadOnlyList<CodeSyncField>, IReadOnlyList<TextEditRecord>> AddFieldsEdits { get; set; } = _ => [];

    public IReadOnlyList<TextEditRecord> AddFields(IReadOnlyList<CodeSyncField> fields)
    {
        AddedFields.Add(fields);
        return AddFieldsEdits(fields);
    }

    /// <summary>The name lists <see cref="RemoveFields"/> was called with (DSG-SUR-131).</summary>
    public List<IReadOnlyList<string>> RemovedFields { get; } = [];

    public IReadOnlyList<TextEditRecord> RemoveFields(IReadOnlyList<string> names)
    {
        RemovedFields.Add(names);
        return [];
    }

    /// <summary>The record lists <see cref="RevertEdits"/> was called with.</summary>
    public List<IReadOnlyList<TextEditRecord>> Reverted { get; } = [];

    /// <summary>What <see cref="RevertEdits"/> answers (false: the user changed the code, DSG-CS-101).</summary>
    public bool RevertSucceeds { get; set; } = true;

    public bool RevertEdits(IReadOnlyList<TextEditRecord> edits)
    {
        Reverted.Add(edits);
        return RevertSucceeds;
    }

    /// <summary>The operation lists <see cref="Execute"/> ran.</summary>
    public List<IReadOnlyList<CodeSyncOp>> Executed { get; } = [];

    public IReadOnlyList<TextEditRecord> Execute(IReadOnlyList<CodeSyncOp> operations)
    {
        Executed.Add(operations);
        return [new TextEditRecord(1000 * Executed.Count, "", "redone")];
    }
}

/// <summary>A <see cref="TimeProvider"/> whose time moves only through <see cref="Advance"/> (testing.md DSG-TEST-004).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
