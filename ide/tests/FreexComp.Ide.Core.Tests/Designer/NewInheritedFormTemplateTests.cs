using System.Text;
using FreexComp.Designer.CodeSync;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// The new inherited form and frame (form-inheritance-designer.md §7, §11; DSG-CS-124…126; D-132): the template texts, the
/// names, <c>file.newInheritedForm</c> / <c>project.addNewInheritedForm</c> through the New Inherited Form dialog.
/// </summary>
[Trait("Phase", "8")]
public sealed class NewInheritedFormTemplateTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";

    /// <summary>DSG-CS-124: the form unit of DSG-CS-110 with <c>class(T&lt;A&gt;)</c> and the ancestor's unit in <c>uses</c>.</summary>
    private static string FormUnit(string name, string ancestor, string uses, string newLine) => string.Join(newLine,
        $"unit {name};", "", "interface", "", "uses", $"  {uses};", "", "type", $"  T{name} = class({ancestor})", "  private", "  public",
        "  end;", "", "var", $"  {name}: T{name};", "", "implementation", "", $"{{$FORM {name}.form}}", "", "end.", "");

    /// <summary>DSG-CS-124: the frame unit of DSG-CS-114 with <c>class(T&lt;A&gt;)</c>, the <c>uses</c> entry and the registration.</summary>
    private static string FrameUnit(string name, string ancestor, string uses, string newLine) => string.Join(newLine,
        $"unit {name};", "", "interface", "", "uses", $"  {uses};", "", "type", $"  T{name} = class({ancestor})", "  private", "  public",
        "  end;", "", "implementation", "", $"{{$FORM {name}.form}}", "", "initialization", $"  RegisterClass(T{name});", "", "end.", "");

    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public NewInheritedFormTemplateTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        ((DocumentService)_services.Documents).DesignCatalog = FrameDesign.Catalog;
    }

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    /// <summary>The scripted dialog: the row of <paramref name="className"/> chosen, then OK (or Cancel when null).</summary>
    private void Choose(string? className, List<(NewInheritedFormViewModel Model, string? Initial)> shown)
    {
        Dialogs.Script(NewInheritedFormViewModel.DialogName, async dialog =>
        {
            var model = (NewInheritedFormViewModel)dialog;
            shown.Add((model, model.Selected?.ClassName));
            if (className is null)
            {
                await model.CancelButton!.PressAsync();
                return;
            }

            model.Selected = model.Rows.Single(r => r.ClassName == className);
            await model.Ok.PressAsync();
        });
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-124")]
    [Trait("Req", "DSG-CS-125")]
    [Trait("Req", "DSG-CS-126")]
    public Task FormAndFrame_UnitFormAndProjectEntry_AncestorUnitInUses_OpensResolved() => TestUiThread.RunAsync(CreatesOnUiThread);

    private async Task CreatesOnUiThread()
    {
        // The texts and names (the caller applies the line-ending setting to the unit; the form is UTF-8 + LF).
        Assert.Equal(FormUnit("BaseDialog2", "TBaseDialog", "Classes, Controls, Forms, BaseDialogUnit", "\n"),
            NewInheritedFormTemplate.UnitText("BaseDialog2", "TBaseDialog", "BaseDialogUnit", isFrame: false));
        Assert.Equal(FormUnit("Form3", "TForm2", "Classes, Controls, Forms", "\n"),
            NewInheritedFormTemplate.UnitText("Form3", "TForm2", "forms", isFrame: false)); // the unit is in uses already
        Assert.Equal(FrameUnit("AddressFrame2", "TAddressFrame", "Classes, Controls, Forms, AddressFrameUnit", "\n"),
            NewInheritedFormTemplate.UnitText("AddressFrame2", "TAddressFrame", "AddressFrameUnit", isFrame: true));
        Assert.Equal("inherited BaseDialog2: TBaseDialog2\nend\n", NewInheritedFormTemplate.FormText("BaseDialog2"));
        Assert.Equal("BaseDialog2", NewInheritedFormTemplate.NextName("TBaseDialog", _ => false));
        Assert.Equal("BaseDialog4", NewInheritedFormTemplate.NextName("TBaseDialog", n => n is "BaseDialog2" or "BaseDialog3"));
        Assert.Equal("Dialog2", NewInheritedFormTemplate.NextName("Dialog", _ => false)); // no leading T: the name as it is

        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "BaseDialogUnit.pas", "form": "BaseDialogUnit.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(Dir + @"\BaseDialogUnit.pas", InheritedDesign.BaseUnit);
        File(Dir + @"\BaseDialogUnit.form", InheritedDesign.BaseDialog);
        File(Dir + @"\AddressFrameUnit.pas", ProjectFrameCatalogTests.FrameUnit);
        File(Dir + @"\AddressFrameUnit.form", FrameDesign.AddressFrame);
        File(Dir + @"\BaseDialog2.pas", "unit BaseDialog2;\r\n"); // a file of that name in the folder: the name is taken

        // Both commands need a project (DSG-CS-126).
        Assert.False(_services.Commands.CanExecute("file.newInheritedForm"));
        Assert.False(_services.Commands.CanExecute("project.addNewInheritedForm"));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(_services.Commands.CanExecute("file.newInheritedForm"));
        Assert.True(_services.Commands.CanExecute("project.addNewInheritedForm"));

        // Cancel creates nothing.
        var shown = new List<(NewInheritedFormViewModel Model, string? Initial)>();
        var files = _fs.AllFiles.Count;
        Choose(null, shown);
        await _services.Commands.ExecuteAsync("file.newInheritedForm");
        var (dialog, initial) = Assert.Single(shown);
        Assert.Equal(["TBaseDialog", "TAddressFrame"], dialog.Rows.Select(r => r.ClassName)); // forms, then frames
        Assert.Equal("TBaseDialog", initial); // no active form document: the first row
        Assert.Equal(files, _fs.AllFiles.Count);
        Assert.Equal(3, _services.Projects.Project!.Files.Count);

        // A form: BaseDialog3 (BaseDialog2.pas exists), the ancestor's unit in uses, the entry saved, opened resolved.
        Choose("TBaseDialog", shown);
        await _services.Commands.ExecuteAsync("file.newInheritedForm");
        Assert.Equal("New class: TBaseDialog3    Unit: BaseDialog3.pas", shown[^1].Model.Preview);
        Assert.Equal(FormUnit("BaseDialog3", "TBaseDialog", "Classes, Controls, Forms, BaseDialogUnit", "\r\n"), Disk(Dir + @"\BaseDialog3.pas"));
        Assert.Equal("inherited BaseDialog3: TBaseDialog3\nend\n", Disk(Dir + @"\BaseDialog3.form"));
        var entry = _services.Projects.Project!.Files[^1];
        Assert.Equal(("BaseDialog3.pas", "BaseDialog3.form"), (entry.Path, entry.Form));
        var form = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\BaseDialog3.pas", form.FilePath);
        Assert.Equal(DocumentView.Design, form.ActiveView);
        Assert.False(form.IsModified);
        var model = form.Designer.Model!;
        Assert.Empty(model.Diagnostics);
        Assert.True(model.Root.IsInheritedRoot);
        Assert.Equal("TBaseDialog", model.Ancestor!.ClassName);
        Assert.Equal(["TitleLabel", "ButtonPanel"], model.Root.Children.Select(c => c.Name));
        Assert.Equal(CodeSyncState.Ok, form.Designer.CodeSync.State);
        Assert.Empty(form.Designer.CodeSync.Reconcile(model, ReconcileMode.CheckOnly).Issues);
        Assert.Same(model, _services.Frames!.Ancestors.Find("TBaseDialog3")!.Document);
        Assert.Null(_services.Frames.Find("TBaseDialog3"));

        // The dialog preselects the active document's class; a frame from the Project Manager's selected folder.
        ((ProjectService)_services.Projects).SelectedFolder = () => Dir + @"\frames";
        _fs.CreateDirectory(Dir + @"\frames");
        Choose("TAddressFrame", shown);
        await _services.Commands.ExecuteAsync("project.addNewInheritedForm");
        Assert.Equal(["TBaseDialog", "TBaseDialog3", "TAddressFrame"], shown[^1].Model.Rows.Select(r => r.ClassName));
        Assert.Equal("TBaseDialog3", shown[^1].Initial);
        Assert.Equal(FrameUnit("AddressFrame2", "TAddressFrame", "Classes, Controls, Forms, AddressFrameUnit", "\r\n"),
            Disk(Dir + @"\frames\AddressFrame2.pas"));
        Assert.Equal("inherited AddressFrame2: TAddressFrame2\nend\n", Disk(Dir + @"\frames\AddressFrame2.form"));
        entry = _services.Projects.Project!.Files[^1];
        Assert.Equal(("frames/AddressFrame2.pas", "frames/AddressFrame2.form"), (entry.Path, entry.Form));
        var frame = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(DocumentView.Design, frame.ActiveView);
        Assert.True(frame.Designer.Model!.Root.IsInheritedRoot);
        Assert.Empty(frame.Designer.Model.Diagnostics);
        Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel"], frame.Designer.Model.Root.Children.Select(c => c.Name));
        Assert.Equal(["TAddressFrame", "TAddressFrame2"], _services.Frames.All.Select(f => f.ClassName)); // a frame
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-316")]
    [Trait("Req", "UI-DLG-317")]
    public Task Dialog_PreselectsActiveClass_FilterAndPreview() => TestUiThread.RunAsync(DialogOnUiThread);

    private Task DialogOnUiThread()
    {
        var rows = new[]
        {
            new NewInheritedFormRow("TBaseDialog", false, @"C:\p\BaseDialogs.pas", "BaseDialogs.pas"),
            new NewInheritedFormRow("TAddressFrame", true, @"C:\p\Frames\Address.pas", @"Frames\Address.pas"),
            new NewInheritedFormRow("TCustomerForm", false, @"C:\p\Forms\Customer.pas", @"Forms\Customer.pas"),
        };
        var created = new List<string>();
        var dialog = new NewInheritedFormViewModel(rows, "TCustomerForm", c => c[1..] + "2", (row, _) => { created.Add(row.ClassName); return Task.FromResult(true); });

        // Forms, then frames, each by class name; the active document's class preselected.
        Assert.Equal(["TBaseDialog", "TCustomerForm", "TAddressFrame"], dialog.Rows.Select(r => r.ClassName));
        Assert.Equal("TCustomerForm", dialog.Selected!.ClassName);
        Assert.Equal("New class: TCustomerForm2    Unit: CustomerForm2.pas", dialog.Preview);
        Assert.Equal(("CustomerForm", "Form", @"Forms\Customer.pas"), (dialog.Rows[1].Name, dialog.Rows[1].Kind, dialog.Rows[1].UnitPath));
        Assert.Equal(@"AddressFrame, Frame, Frames\Address.pas", dialog.Rows[2].AccessibleName);
        Assert.Null(dialog.Placeholder);

        // The filter: class name without T or unit path, case-insensitive; the selection moves to the first match.
        dialog.Filter = "frames\\";
        Assert.Equal(["TAddressFrame"], dialog.VisibleRows.Select(r => r.ClassName));
        Assert.Equal("TAddressFrame", dialog.Selected!.ClassName);
        dialog.Filter = "t";
        Assert.Equal(["TCustomerForm"], dialog.VisibleRows.Select(r => r.ClassName)); // "T" itself is not part of the name
        dialog.Filter = "nothing";
        Assert.Empty(dialog.VisibleRows);
        Assert.Null(dialog.Selected);
        Assert.Equal("", dialog.Preview);
        Assert.False(dialog.Ok.IsEnabled);
        Assert.Equal(FreexComp.Ide.Core.Resources.Strings.Msg_NewInheritedForm_NoMatch, dialog.Placeholder);
        dialog.Filter = "";
        Assert.Equal("TBaseDialog", dialog.Selected!.ClassName);

        // Without rows: the other placeholder.
        var empty = new NewInheritedFormViewModel([], null, c => c, (_, _) => Task.FromResult(true));
        Assert.Equal(FreexComp.Ide.Core.Resources.Strings.Msg_NewInheritedForm_NoAncestors, empty.Placeholder);
        Assert.False(empty.Ok.IsEnabled);
        Assert.Empty(created);
        return Task.CompletedTask;
    }
}
