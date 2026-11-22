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

    private DesignerHarness(FormDocument document)
    {
        Document = document;
        Editor = new FormEditor(document, TestCatalog.Instance);
        Undo = new DesignerUndoStack();
        Engine = new LayoutEngine(TestCatalog.Instance, RendererRegistry.CreateDefault());
        Controller = new DesignSurfaceController(document, Editor, Undo, TestCatalog.Instance, Engine, Settings, Host);
    }

    /// <summary>A form of the shared test data (<c>forms/canonical/hello.form</c>).</summary>
    public static DesignerHarness FromFile(string relativePath) => FromText(File.ReadAllText(TestPaths.Combine(relativePath)), relativePath);

    public static DesignerHarness FromText(string text, string path = "test.form")
    {
        var result = FormParser.Parse(text.Replace("\r\n", "\n"), path, TestCatalog.Instance);
        var document = result.Document ?? throw new InvalidOperationException("form did not parse:\n" + text);
        Assert.True(document.Diagnostics.Count == 0, string.Join("\n", document.Diagnostics));
        return new DesignerHarness(document);
    }

    public FakeDesignerSettings Settings { get; } = new();

    public FakeDesignerHost Host { get; } = new();

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

    /// <summary>A control-relative point (default: the centre) of <paramref name="on"/> in surface coordinates.</summary>
    public (int X, int Y) SurfacePoint(string on, (int X, int Y)? at = null)
    {
        var bounds = Controller.Geometry.GetBounds(Object(on));
        var (x, y) = at ?? (bounds.Width / 2, bounds.Height / 2);
        return (bounds.Left + x, bounds.Top + y);
    }

    public void Press(string on, (int X, int Y)? at = null, InputModifiers modifiers = InputModifiers.None) =>
        PressAt(SurfacePoint(on, at), modifiers);

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
        var reparsed = FormParser.Parse(Text, "check.form", TestCatalog.Instance);
        Assert.True(reparsed.Document is { Diagnostics.Count: 0 }, string.Join("\n", reparsed.Document?.Diagnostics ?? []));
    }
}

/// <summary>In-memory <see cref="IDesignerSettings"/> (testing.md DSG-TEST-005).</summary>
internal sealed class FakeDesignerSettings : IDesignerSettings
{
    public bool ShowComponentCaptions { get; set; } = true;

    public int HandleSize { get; set; } = 7;
}

/// <summary>Records what the controller asks of the IDE.</summary>
internal sealed class FakeDesignerHost : IDesignerHost
{
    public List<(string Id, object[] Args)> Statuses { get; } = [];

    public List<(double X, double Y)> ContextMenus { get; } = [];

    public void SetStatus(string messageId, params object[] args) => Statuses.Add((messageId, args));

    public void ShowContextMenu(double x, double y) => ContextMenus.Add((x, y));
}
