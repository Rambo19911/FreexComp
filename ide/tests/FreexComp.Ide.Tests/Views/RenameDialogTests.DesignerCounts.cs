using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class RenameDialogTests
{
    /// <summary>
    /// UI-DLG-384 (I-410): a rename that goes through the form designer (IDE-REFAC-059) shows no plan counts — the designer
    /// changes more than the plan lists — the summary line is hidden and the designer note takes its place; after
    /// <c>Rename</c> the status line reports the designer's own count of code references (DSG-CS-134). A component no form
    /// line refers to keeps the summary with the plan's counts.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-384")]
    [Trait("Req", "IDE-REFAC-060")]
    public async Task DesignerRenameHidesPlanCounts()
    {
        const string formUnit = "unit Unit1;\r\ninterface\r\nuses Classes, Controls, Forms, StdCtrls;\r\ntype\r\n  TForm1 = class(TForm)\r\n"
            + "    Button1: TButton;\r\n    Button2: TButton;\r\n    procedure FormCreate(Sender: TObject);\r\n  end;\r\nvar\r\n  Form1: TForm1;\r\n"
            + "implementation\r\n{$FORM Unit1.form}\r\nprocedure TForm1.FormCreate(Sender: TObject);\r\nbegin\r\n  Button1.Caption := 'Go';\r\n"
            + "  Button2.Caption := 'Stop';\r\nend;\r\nend.\r\n";
        const string form = "object Form1: TForm1\r\n  Caption = 'Form1'\r\n  ActiveControl = Button1\r\n  OnCreate = FormCreate\r\n"
            + "  object Button1: TButton\r\n    Caption = 'Go'\r\n  end\r\n  object Button2: TButton\r\n    Top = 40\r\n    Caption = 'Stop'\r\n  end\r\nend\r\n";
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.Settings["toolchain.rtlDir"] = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
            o.Settings["toolchain.frameworkDir"] = Path.Combine(SpecTables.RepoRootPath(), "ui-framework"); // the compiler searches <framework>/src
            o.InitialFiles[@"Hello\Hello.pproj"] = """
                { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas",
                  "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas", "form": "Unit1.form" } ] }
                """;
            o.InitialFiles[@"Hello\Unit1.pas"] = formUnit;
            o.InitialFiles[@"Hello\Unit1.form"] = form;
        }, implementedPhase: null); // form documents need phase 5 (I-218)
        await TrustDialogTests.Guarded(h, async () =>
        {
            var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var code = Assert.IsAssignableFrom<TextDocumentViewModel>(document.VisibleText); // the Code view
            var editor = h.Find<TextEditor>("Editor.Unit1.pas");

            // Button2: nothing refers to it in a form — the summary shows the plan's counts, no note.
            CaretOn(editor, code, "Button2.Caption");
            var (plain, plainModel) = await F2Async(h);
            Replace(plain, "StopButton");
            await h.WaitForAsync(() => plainModel.Checking.IsCompleted && Text(plain, "Summary").Length > 0
                && Text(plain, "Summary") != Strings.Msg_Rename_Checking, "the check ended");
            await plainModel.Checking;
            Assert.True(plain.Find<TextBlock>("Summary").IsVisible);
            Assert.False(plain.Find<TextBlock>("Notes").IsVisible);
            await CancelAsync(h, plain, plainModel);

            // Button1: the form's root refers to it — the designer case: no counts, the note in their place.
            CaretOn(editor, code, "Button1.Caption");
            var (dlg, model) = await F2Async(h);
            Replace(dlg, "OkButton");
            await h.WaitForAsync(() => model.Checking.IsCompleted && dlg.Find<TextBlock>("Notes").IsVisible, "the designer note");
            await model.Checking;
            Assert.False(dlg.Find<TextBlock>("Summary").IsVisible);
            Assert.Equal("", Text(dlg, "Summary"));
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_ThroughDesigner, "Unit1.form"), Text(dlg, "Notes"));
            Assert.True(dlg.Find<Button>("Ok").IsEnabled);

            // Rename: the designer renames it; the status line reports the designer's count (one reference in the code).
            dlg.Click("Ok");
            var expected = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_RenameCodeRefsOne, "Button1", "OkButton");
            await h.WaitForAsync(() => !dlg.Window.IsVisible && h.Status.Message == expected, "the designer's count in the status line");
            Assert.Contains("  OkButton.Caption := 'Go';", document.Buffer.Text, StringComparison.Ordinal);
            if (document.Designer.RenameWork is { } work)
                await work;
            await SettleAsync(h, model);
        });
    }
}
