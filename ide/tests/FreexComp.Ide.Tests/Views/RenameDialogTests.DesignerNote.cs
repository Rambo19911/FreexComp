using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class RenameDialogTests
{
    /// <summary>
    /// IDE-REFAC-059 (I-384): F2 on a component of a form whose root names it in <c>ActiveControl</c> — once the check for the
    /// typed name has found the designer case, <c>Notes</c> says that the form designer renames it and that there is no
    /// preview; a component no line of the form refers to shows no note (it goes through the engine with preview).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-059")]
    [Trait("Req", "UI-DLG-384")]
    public async Task DesignerNoteIsShown()
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

            // Button1: the form's root refers to it — the designer case.
            CaretOn(editor, code, "Button1.Caption");
            var (dlg, model) = await F2Async(h);
            Replace(dlg, "OkButton");
            const string note = "This component is renamed by the form designer of Unit1.form, because other form files or properties refer to it. There is no preview.";
            await h.WaitForAsync(() => dlg.Find<TextBlock>("Notes").IsVisible && Text(dlg, "Notes") == note, "the designer note");
            Assert.Equal("Dialog.RenameSymbol.Notes", AutomationIds.Get(dlg.Find<TextBlock>("Notes")));
            Assert.True(dlg.Find<Button>("Ok").IsEnabled);
            await CancelAsync(h, dlg, model);

            // Button2: nothing refers to it in a form — no note.
            CaretOn(editor, code, "Button2.Caption");
            var (second, secondModel) = await F2Async(h);
            Replace(second, "StopButton");
            await h.WaitForAsync(() => secondModel.Checking.IsCompleted && Text(second, "Summary").Length > 0
                && Text(second, "Summary") != Core.Resources.Strings.Msg_Rename_Checking, "the check ended");
            await secondModel.Checking;
            Assert.False(second.Find<TextBlock>("Notes").IsVisible);
            await CancelAsync(h, second, secondModel);
        });
    }
}
