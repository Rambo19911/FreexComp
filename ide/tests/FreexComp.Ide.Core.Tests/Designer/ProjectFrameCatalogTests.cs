using System.Globalization;
using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The project frame catalog (frames-designer.md §2, DSG-FD-103…105; D-120).</summary>
[Trait("Phase", "8")]
public sealed class ProjectFrameCatalogTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string FrameUnitPath = Dir + @"\AddressFrameUnit.pas";
    private const string FrameFormPath = Dir + @"\AddressFrameUnit.form";

    /// <summary>The unit of <see cref="FrameDesign.AddressFrame"/>: a class with the direct ancestor TFrame.</summary>
    internal static readonly string FrameUnit = string.Join("\r\n",
        "unit AddressFrameUnit;", "", "interface", "", "uses", "  Classes, Controls, Forms, StdCtrls, ExtCtrls;", "", "type",
        "  TAddressFrame = class(TFrame)", "    StreetLabel: TLabel;", "    StreetEdit: TEdit;", "    CityPanel: TPanel;",
        "    CityLabel: TLabel;", "    procedure StreetEditChange(Sender: TObject);", "  end;", "", "implementation", "",
        "{$FORM AddressFrameUnit.form}", "", "procedure TAddressFrame.StreetEditChange(Sender: TObject);", "begin", "end;", "",
        "initialization", "  RegisterClass(TAddressFrame);", "end.", "");

    private readonly InMemoryFileSystem _fs = new();
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;
    private readonly List<IReadOnlyList<string>> _changes = [];

    public ProjectFrameCatalogTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
        });
        ((DocumentService)_services.Documents).DesignCatalog = FrameDesign.Catalog;
    }

    private ProjectFrameCatalog Frames => _services.Frames!;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private List<string> Lines()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output (IDE-P3-070)
        return [.. _services.Output.GetLines(OutputChannels.Designer).Select(l => l.Text)];
    }

    private static string Caption(FrameDefinition frame) =>
        ((StringValue)PropertyAccess.GetEffective(frame.Document.FindByName("StreetLabel")!, "Caption")).Value;

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-103")]
    [Trait("Req", "DSG-FD-104")]
    [Trait("Req", "DSG-FD-105")]
    public Task ListsFrameUnits_SkipsUnreadable_RefreshesOnSaveAndEdit() => TestUiThread.RunAsync(ListsOnUiThread);

    private async Task ListsOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" },
              { "path": "Broken.pas", "form": "Broken.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(Dir + @"\MainForm.pas", FrameDesign.Unit);
        File(Dir + @"\MainForm.form", FrameDesign.Form);
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        File(Dir + @"\Broken.pas", FrameUnit.Replace("AddressFrame", "BrokenFrame", StringComparison.Ordinal)); // its form is missing
        Assert.NotNull(_services.Frames);
        Assert.Same(Frames, ((DocumentService)_services.Documents).DesignFrames);
        Assert.Empty(Frames.All); // no project: no frames
        Frames.Changed += (_, e) => _changes.Add(e.ClassNames);

        // DSG-FD-103: the project's units with a form entry whose class derives directly from TFrame; the form unit is no frame.
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var frame = Assert.Single(Frames.All);
        Assert.Equal(("TAddressFrame", "AddressFrameUnit", FrameFormPath), (frame.ClassName, frame.UnitName, frame.FormPath));
        Assert.Same(frame, Frames.Find("taddressframe"));
        Assert.Equal(FrameUnitPath, Frames.UnitPathOf("TAddressFrame"));
        Assert.Null(Frames.Find("TMainForm"));
        Assert.Equal("Street", Caption(frame));
        Assert.Equal([["TAddressFrame"]], _changes);

        // A unit whose form cannot be read is left out with one line on the Output channel designer, once per reason.
        var line = Assert.Single(Lines());
        Assert.StartsWith(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FrameUnreadable, Dir + @"\Broken.pas", ""), line, StringComparison.Ordinal);
        Assert.Null(Frames.Find("TBrokenFrame"));

        // A refresh without a difference keeps the definitions and raises nothing (window activation: DSG-FD-104).
        _changes.Clear();
        Frames.Refresh();
        Assert.Same(frame, Frames.Find("TAddressFrame"));
        Assert.Empty(_changes);
        Assert.Single(Lines());

        // An edit on disk of a closed frame unit is read at the next refresh.
        File(FrameFormPath, FrameDesign.AddressFrame.Replace("'Street'", "'Road'", StringComparison.Ordinal));
        Frames.Refresh();
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.Equal("Road", Caption(Frames.Find("TAddressFrame")!));

        // Opened in the IDE: the definition is the open designer model; an applied edit raises Changed for its class.
        _changes.Clear();
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(FrameUnitPath)).Value);
        var designer = document.Designer;
        Assert.Same(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Equal([["TAddressFrame"]], _changes);
        _changes.Clear();
        var change = new SetProperty("StreetLabel", "Caption", new StringValue("Lane"));
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.Equal("Lane", Caption(Frames.Find("TAddressFrame")!));

        // Saving keeps the open model's definition; closing reads the saved form from disk again.
        _changes.Clear();
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.Same(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Empty(_changes);
        Assert.True(await _services.Documents.CloseAsync(document));
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.NotSame(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Equal("Lane", Caption(Frames.Find("TAddressFrame")!));

        // DSG-FD-105: only the loaded project; closing it empties the list.
        _changes.Clear();
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(Frames.All);
        Assert.Equal([["TAddressFrame"]], _changes);
    }

    /// <summary>
    /// DSG-OI-120 / DSG-CS-117 (D-120): a double-click on a handler a frame assigns opens the frame's unit (found through the
    /// project catalog) in the Code view at that method; the form is not changed.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-117")]
    [Trait("Req", "DSG-OI-120")]
    public Task InheritedHandlerRequest_OpensFrameUnitAtMethod() => TestUiThread.RunAsync(OpensFrameUnitOnUiThread);

    private async Task OpensFrameUnitOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(Dir + @"\MainForm.pas", FrameDesign.Unit);
        File(Dir + @"\MainForm.form", FrameDesign.Form);
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var form = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\MainForm.pas")).Value);
        Assert.True(form.ShowView(DocumentView.Design));
        Assert.True(form.Designer.Model!.FindByName("BillingAddress")!.IsFrameInstance);
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(form.Designer);
        inspector.Page = InspectorPage.Events;
        form.Designer.Surface!.Selection.Set(["BillingAddress.StreetEdit"]);
        var text = form.Designer.CurrentFormText();

        inspector.ActivateEvent("OnChange");
        await ((DocumentService)_services.Documents).FrameHandlerOpening;

        var frame = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(FrameUnitPath, frame.FilePath);
        Assert.Equal(DocumentView.Code, frame.ActiveView);
        var lines = frame.Buffer.Text.Split("\r\n");
        var procedure = Array.IndexOf(lines, "procedure TAddressFrame.StreetEditChange(Sender: TObject);") + 1;
        Assert.InRange(frame.ViewState.Caret.Line, procedure, procedure + 2); // in the method (DSG-CS-064)
        Assert.Equal(text, form.Designer.CurrentFormText());
        Assert.False(form.IsModified);
    }
}
