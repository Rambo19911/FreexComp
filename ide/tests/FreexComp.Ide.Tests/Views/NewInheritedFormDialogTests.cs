using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The New Inherited Form dialog (form-inheritance-designer.md §12.2, UI-DLG-316…322; D-132).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class NewInheritedFormDialogTests
{
    private const string Item = "Dialog.NewInheritedForm.Item.";

    private static string Unit(string name, string className, string ancestor, bool frame = false) => string.Join("\r\n",
        [$"unit {name};", "", "interface", "", "uses", "  Classes, Controls, Forms;", "", "type", $"  {className} = class({ancestor})", "  end;", "",
            "implementation", "", $"{{$FORM {name}.form}}", "", .. frame ? new[] { "initialization", $"  RegisterClass({className});", "" } : [], "end.", ""]);

    private static Control? Focused(DialogDriver dlg) => dlg.Window.FocusManager?.GetFocusedElement() as Control;

    private static ListBoxItem? FocusedRow(DialogDriver dlg) => Focused(dlg) as ListBoxItem ?? Focused(dlg)?.FindAncestorOfType<ListBoxItem>();

    private static IReadOnlyList<string?> Rows(DialogDriver dlg) =>
        [.. dlg.FindAll(Item).Where(c => c.IsEffectivelyVisible).Select(AutomationIds.Get)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-316")]
    [Trait("Req", "UI-DLG-317")]
    [Trait("Req", "UI-DLG-318")]
    [Trait("Req", "UI-DLG-319")]
    [Trait("Req", "UI-DLG-320")]
    [Trait("Req", "UI-DLG-321")]
    [Trait("Req", "UI-DLG-322")]
    [Trait("Req", "DSG-CS-126")]
    public async Task ListsFormsThenFrames_FilterTypeAhead_Placeholders_OkCreates()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["Hello.pproj"] = """
                { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
                  { "path": "Hello.pas" },
                  { "path": "Frames/AddressFrameUnit.pas", "form": "Frames/AddressFrameUnit.form" },
                  { "path": "Forms/Customer.pas", "form": "Forms/Customer.form" },
                  { "path": "BaseDialogUnit.pas", "form": "BaseDialogUnit.form" } ] }
                """;
            o.InitialFiles["Hello.pas"] = "program Hello;\r\nbegin\r\nend.\r\n";
            o.InitialFiles["BaseDialogUnit.pas"] = InheritedDesign.BaseUnit;
            o.InitialFiles["BaseDialogUnit.form"] = InheritedDesign.BaseDialog;
            o.InitialFiles[Path.Combine("Forms", "Customer.pas")] = Unit("Customer", "TCustomerForm", "TForm");
            o.InitialFiles[Path.Combine("Forms", "Customer.form")] = "object CustomerForm: TCustomerForm\n  Left = 200\n  Top = 120\n  ClientWidth = 300\n  ClientHeight = 200\n  PixelsPerInch = 96\nend\n";
            o.InitialFiles[Path.Combine("Frames", "AddressFrameUnit.pas")] = Unit("AddressFrameUnit", "TAddressFrame", "TFrame", frame: true);
            o.InitialFiles[Path.Combine("Frames", "AddressFrameUnit.form")] = FrameDesign.AddressFrame;
            o.InitialFiles["Empty.pproj"] = """
                { "formatVersion": 1, "name": "Empty", "appType": "gui", "mainSource": "Empty.pas", "files": [ { "path": "Empty.pas" } ] }
                """;
            o.InitialFiles["Empty.pas"] = "program Empty;\r\nbegin\r\nend.\r\n";
        });
        ((DocumentService)h.Services.Documents).DesignCatalog = FrameDesign.Catalog;
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Hello.pproj")));
        Dispatcher.UIThread.RunJobs();

        // UI-DLG-316, 321: the dialog, its size, its ids; forms then frames by class name; names, kinds, unit paths.
        var running = h.Services.Commands.ExecuteAsync("file.newInheritedForm");
        var dlg = await h.WaitForDialogAsync("NewInheritedForm");
        Assert.Equal(Strings.Dialog_NewInheritedForm_Title, dlg.Window.Title);
        Assert.Equal((520.0, 380.0, true, 400.0, 280.0), (dlg.Window.Width, dlg.Window.Height, dlg.Window.CanResize, dlg.Window.MinWidth, dlg.Window.MinHeight));
        Assert.Equal([Item + "TBaseDialog", Item + "TCustomerForm", Item + "TAddressFrame"], Rows(dlg));
        var list = dlg.Find<ListBox>("Ancestors");
        Assert.Equal("Inherit from:", AutomationProperties.GetName(list)); // UI-DLG-322: the label text
        Assert.Equal("AddressFrame, Frame, " + Path.Combine("Frames", "AddressFrameUnit.pas"),
            AutomationProperties.GetName(dlg.Find<ListBoxItem>("Item.TAddressFrame")));
        var texts = dlg.Find<ListBoxItem>("Item.TCustomerForm").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(["CustomerForm", Strings.Dialog_NewInheritedForm_KindForm, Path.Combine("Forms", "Customer.pas")], texts);
        Assert.Equal(Path.Combine(h.TempDir, "Forms", "Customer.pas"),
            ToolTip.GetTip(dlg.Find<ListBoxItem>("Item.TCustomerForm").GetVisualDescendants().OfType<TextBlock>().Last()));

        // First focus on the list, on the first row (no form is active); the preview names DSG-CS-125's names (a live region).
        Assert.Same(dlg.Find<ListBoxItem>("Item.TBaseDialog"), FocusedRow(dlg));
        Assert.True(dlg.Find<ListBoxItem>("Item.TBaseDialog").IsSelected);
        var preview = dlg.Find<TextBlock>("Preview");
        Assert.Equal("New class: TBaseDialog2    Unit: BaseDialog2.pas", preview.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(preview));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);

        // UI-DLG-317: typing in the list goes on in the filter; the selection follows the first match; Down returns to the list.
        dlg.Window.KeyTextInput("c");
        Dispatcher.UIThread.RunJobs();
        dlg.Window.KeyTextInput("u");
        Dispatcher.UIThread.RunJobs();
        var filter = dlg.Find<TextBox>("Filter");
        Assert.Equal("cu", filter.Text);
        Assert.Same(filter, Focused(dlg));
        Assert.Equal([Item + "TCustomerForm"], Rows(dlg));
        Assert.True(dlg.Find<ListBoxItem>("Item.TCustomerForm").IsSelected);
        Assert.Equal("New class: TCustomerForm2    Unit: CustomerForm2.pas", preview.Text);
        dlg.Press("Down");
        Assert.Same(dlg.Find<ListBoxItem>("Item.TCustomerForm"), FocusedRow(dlg));

        // No match: the placeholder, OK disabled; an empty filter brings every row back.
        dlg.Type("Filter", "zz");
        Assert.Empty(Rows(dlg));
        var placeholder = dlg.Find<TextBlock>("Placeholder");
        Assert.True(placeholder.IsEffectivelyVisible);
        Assert.Equal(Strings.Msg_NewInheritedForm_NoMatch, placeholder.Text);
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        Assert.Equal("", preview.Text);
        filter.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, Rows(dlg).Count);
        Assert.False(placeholder.IsEffectivelyVisible);

        // UI-DLG-318: a frame chosen; OK creates the unit and form, closes, and opens the new frame; the size is remembered.
        dlg.Click("Item.TAddressFrame");
        Assert.Equal("New class: TAddressFrame2    Unit: AddressFrame2.pas", preview.Text);
        dlg.Window.Width = 560;
        dlg.Click("Ok");
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed");
        await running;
        Assert.Empty(h.Window.OwnedWindows);
        Assert.True(File.Exists(Path.Combine(h.TempDir, "AddressFrame2.pas")));
        Assert.Equal("inherited AddressFrame2: TAddressFrame2\nend\n", File.ReadAllText(Path.Combine(h.TempDir, "AddressFrame2.form")));
        Assert.Equal(Path.Combine(h.TempDir, "AddressFrame2.pas"), h.Services.Documents.ActiveDocument!.FilePath);
        Assert.Equal("560,380", h.Services.Layout.GetDialogValue("NewInheritedForm", "size"));

        // UI-DLG-316: the active form's class is preselected; a double-click on a row is OK.
        running = h.Services.Commands.ExecuteAsync("project.addNewInheritedForm");
        dlg = await h.WaitForDialogAsync("NewInheritedForm");
        Assert.Equal(560, dlg.Window.Width);
        Assert.True(dlg.Find<ListBoxItem>("Item.TAddressFrame2").IsSelected);
        var row = dlg.Find<ListBoxItem>("Item.TBaseDialog");
        var point = row.TranslatePoint(new Avalonia.Point(row.Bounds.Width / 2, row.Bounds.Height / 2), dlg.Window)!.Value;
        for (var i = 0; i < 2; i++)
        {
            dlg.Window.MouseDown(point, MouseButton.Left);
            if (dlg.Window.IsVisible) // the second press closes the dialog
                dlg.Window.MouseUp(point, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed by the double-click");
        await running;
        Assert.True(File.Exists(Path.Combine(h.TempDir, "BaseDialog2.pas")));

        // A project without forms: the other placeholder, focus on the filter, OK disabled; Escape creates nothing.
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Empty.pproj")));
        Dispatcher.UIThread.RunJobs();
        var files = Directory.GetFiles(h.TempDir).Length;
        running = h.Services.Commands.ExecuteAsync("file.newInheritedForm");
        dlg = await h.WaitForDialogAsync("NewInheritedForm");
        Assert.Empty(Rows(dlg));
        Assert.Equal(Strings.Msg_NewInheritedForm_NoAncestors, dlg.Find<TextBlock>("Placeholder").Text);
        Assert.Same(dlg.Find<TextBox>("Filter"), Focused(dlg));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);
        dlg.Press("Escape");
        await h.WaitForAsync(() => running.IsCompleted, "dialog cancelled");
        await running;
        Assert.Equal(files, Directory.GetFiles(h.TempDir).Length);
    }
}
