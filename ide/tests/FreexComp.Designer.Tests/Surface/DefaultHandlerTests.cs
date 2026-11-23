using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The default event handler command (design-surface.md §11.4; D-054), against the fake code sync.</summary>
public sealed class DefaultHandlerTests
{
    private const string Notify = "procedure(Sender: TObject)";

    private static string? Handler(DesignerHarness h, string component, string eventName) =>
        h.Object(component).FindProperty(eventName)?.Value is IdentValue { Name: var method } ? method : null;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-160")]
    [Trait("Req", "DSG-SUR-161")]
    [Trait("Req", "DSG-SUR-042")]
    public void PicksDefaultEventAndCallsCodeSync()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var code = h.Host.CodeSync;
        var original = h.Text;

        // The default event already has a handler: only navigation, no unit.
        h.Select("GreetButton");
        Assert.True(h.Controller.CanExecute("designer.createDefaultHandler"));
        h.Execute("designer.createDefaultHandler");
        Assert.Equal(["GreetButtonClick"], code.Navigations);
        Assert.Empty(code.CreatedHandlers);
        Assert.Equal(0, h.Undo.UndoCount);

        // No handler yet: the event marked default (TEdit: OnChange), named <Component><Event without On>, created through
        // code sync, assigned and navigated to — one "Event" unit holding the code edits. Enter runs the command.
        h.Select("NameEdit");
        Assert.True(h.Key(DesignKey.Enter));
        Assert.Equal([("NameEditChange", Notify)], code.CreatedHandlers);
        Assert.Equal("NameEditChange", Handler(h, "NameEdit", "OnChange"));
        Assert.Equal("NameEditChange", code.Navigations[^1]);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Event", h.Undo.UndoTitleKey);
        Assert.Equal(code.LastEdits, h.Undo.Peek!.CodeEdits);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["NameEdit"], h.Selected);

        // The name is taken by a method with another signature: digits are appended.
        code.Methods["NameEditChange"] = "TKeyPressEvent";
        h.Execute("designer.createDefaultHandler");
        Assert.Equal(("NameEditChange2", Notify), code.CreatedHandlers[^1]);
        Assert.Equal("NameEditChange2", Handler(h, "NameEdit", "OnChange"));
        h.ExecuteUndo();

        // A compatible method of that name is reused: linked, no new stub, still one unit.
        code.Methods["NameEditChange"] = "TNotifyEvent";
        var created = code.CreatedHandlers.Count;
        var units = h.Undo.UndoCount;
        h.Execute("designer.createDefaultHandler");
        Assert.Equal(created, code.CreatedHandlers.Count);
        Assert.Equal("NameEditChange", Handler(h, "NameEdit", "OnChange"));
        Assert.Equal("NameEditChange", code.Navigations[^1]);
        Assert.Equal(units + 1, h.Undo.UndoCount);
        Assert.Empty(h.Undo.Peek!.CodeEdits);
        h.ExecuteUndo();

        // The form root: the literal "Form" (OnCreate = FormCreate exists: navigation).
        h.Select("MainForm");
        h.Execute("designer.createDefaultHandler");
        Assert.Equal("FormCreate", code.Navigations[^1]);

        // Not for a multi-selection.
        h.Select("NameEdit", "GreetButton");
        Assert.False(h.Controller.CanExecute("designer.createDefaultHandler"));
        Assert.Equal(original, h.Text);

        // A double-click on a component runs the command for it (also on the form client area: the form).
        using var e = DesignerHarness.FromFile("forms/canonical/empty-form.form");
        var point = e.Controller.Geometry.FormClientToSurface((50, 50));
        e.ClickAt(point);
        e.PressAt(point, clickCount: 2);
        e.Release();
        Assert.Equal([("FormCreate", Notify)], e.Host.CodeSync.CreatedHandlers);
        Assert.Equal("FormCreate", Handler(e, "EmptyForm", "OnCreate"));
        Assert.Equal(1, e.Undo.UndoCount);

        e.Host.Arm("TButton");
        e.ClickAt(e.Controller.Geometry.FormClientToSurface((16, 16)));
        var button = e.SurfacePoint("Button1");
        e.ClickAt(button);
        e.PressAt(button, clickCount: 2);
        e.Release();
        Assert.Equal(("Button1Click", Notify), e.Host.CodeSync.CreatedHandlers[^1]);
        Assert.Equal(["Button1"], e.Selected);
        Assert.Equal(new Rect(16, 16, 75, 25), e.Bounds("Button1")); // the double-click moved nothing

        // A class without events: disabled.
        e.Controller.PlaceAtDefaultPosition("TOpenDialog");
        Assert.Equal(["OpenDialog1"], e.Selected);
        Assert.False(e.Controller.CanExecute("designer.createDefaultHandler"));
    }
}
