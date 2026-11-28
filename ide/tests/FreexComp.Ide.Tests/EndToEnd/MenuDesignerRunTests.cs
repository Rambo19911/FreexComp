using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// D-158 (card F05-086, acceptance 1; DSG-SUR-260): the menu made in the Menu Designer is the menu the built program
/// creates. A new gui project, a main menu placed from the palette and set as the form's <c>Menu</c>, the items typed in
/// the Menu Designer, an <c>OnCreate</c> handler whose body — written by this test — prints the native menu outline;
/// saved, built with the real toolchain and run with <c>--dump-layout</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class MenuDesignerRunTests
{
    internal const string OutlineFile = "menu-outline.txt";

    /// <summary>The native outline of DSG-SUR-260: by position, <c>MIIM_FTYPE</c>, <c>MIIM_STRING</c>, <c>MIIM_SUBMENU</c>, the text in two calls.</summary>
    private const string OutlineProcedure = """
        procedure WriteMenuOutline(Lines: TStrings; Menu: HMENU; const Prefix: string);
        var
          I: Integer;
          Info: TMenuItemInfoW;
          Text, Path: string;
        begin
          for I := 0 to GetMenuItemCount(Menu) - 1 do
          begin
            FillChar(Info, SizeOf(Info), 0);
            Info.cbSize := SizeOf(Info);
            Info.fMask := MIIM_FTYPE or MIIM_STRING or MIIM_SUBMENU;
            Info.dwTypeData := nil;
            GetMenuItemInfoW(Menu, I, True, Info);
            Text := '';
            if ((Info.fType and MFT_SEPARATOR) = 0) and (Info.cch > 0) then
            begin
              SetLength(Text, Info.cch);
              Info.cch := Info.cch + 1;
              Info.dwTypeData := PWideChar(Text);
              GetMenuItemInfoW(Menu, I, True, Info);
            end;
            Text := StringReplace(Text, #9, '\t', [rfReplaceAll]);
            Path := Prefix + IntToStr(I);
            if (Info.fType and MFT_SEPARATOR) <> 0 then
              Lines.Add(Path + '|separator|')
            else if Info.hSubMenu <> 0 then
            begin
              Lines.Add(Path + '|popup|' + Text);
              WriteMenuOutline(Lines, Info.hSubMenu, Path + '.');
            end
            else
              Lines.Add(Path + '|item|' + Text);
          end;
        end;


        """;

    private const string HandlerHead = "procedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n";

    private const string HandlerBody = """
        var
          Lines: TStringList;
        begin
          Lines := TStringList.Create;
          try
            WriteMenuOutline(Lines, MainMenu1.Handle, '');
            Lines.SaveToFile(ExtractFilePath(ParamStr(0)) + 'menu-outline.txt', TEncoding.UTF8);
          finally
            Lines.Free;
          end;

        """;

    /// <summary>
    /// The menu outline of DSG-SUR-260 computed from the form document: one line per item with stored <c>Visible</c> other
    /// than <c>False</c>, in pre-order, <c>&lt;path&gt;|&lt;kind&gt;|&lt;text&gt;</c>; the shortcut text is the designer's.
    /// </summary>
    internal static IReadOnlyList<string> DocumentOutline(FormObject menu)
    {
        var lines = new List<string>();
        Walk(menu, "");
        return lines;

        void Walk(FormObject parent, string prefix)
        {
            var position = 0;
            foreach (var item in parent.Children)
            {
                if (item.FindProperty("Visible")?.Value is IdentValue { Name: var visible } && visible.Equals("False", StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = prefix + position++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var caption = item.FindProperty("Caption")?.Value is StringValue { Value: var text } ? text : "";
                if (caption == "-")
                {
                    lines.Add(path + "|separator|");
                }
                else if (item.Children.Count > 0)
                {
                    lines.Add(path + "|popup|" + caption);
                    Walk(item, path + ".");
                }
                else
                {
                    var shortCut = item.FindProperty("ShortCut")?.Value is IntegerValue { Value: not 0 and var value } ? "\\t" + ShortCutText.Format(value) : "";
                    lines.Add(path + "|item|" + caption + shortCut);
                }
            }
        }
    }

    /// <summary>One press on a node's own header, near its start.</summary>
    private static void ClickNode(DialogDriver dlg, string name)
    {
        var header = (Control)dlg.Find<TreeViewItem>("Node." + name).Header!;
        var point = header.TranslatePoint(new Point(Math.Min(header.Bounds.Width / 2, 12), header.Bounds.Height / 2), dlg.Window)!.Value;
        dlg.Window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        dlg.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The caption typed over the selected text of the focused Caption field, Enter; returns the node that then has the focus.</summary>
    private static string TypeCaption(DialogDriver dlg, string caption)
    {
        Assert.Equal("Dialog.MenuDesigner.Caption", dlg.FocusedAutomationId);
        dlg.Window.KeyTextInput(caption);
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Enter");
        var focused = dlg.FocusedAutomationId ?? "";
        Assert.StartsWith("Dialog.MenuDesigner.Node.", focused, StringComparison.Ordinal); // DSG-SUR-259 (a)
        return focused["Dialog.MenuDesigner.Node.".Length..];
    }

    [AvaloniaFact(Timeout = 240000)]
    [Trait("Task", "D-158")]
    [Trait("Req", "DSG-SUR-260")]
    public async Task CompiledAppShowsDesignedMenu()
    {
        await using var h = await Phase5Scenario.StartAsync();
        var s = await Phase5Scenario.CreateProjectAndDesignAsync(h);
        await h.ExecuteAsync("view.toolPalette");

        // A main menu from the palette, set as the form's Menu in the Object Inspector.
        s.Place("TMainMenu", 40, 40);
        Assert.Equal(["MainMenu1"], s.Selected);
        if (s.Object(Phase5Scenario.FormName).FindProperty("Menu") is null)
        {
            s.Surface.Selection.Select(s.Object(Phase5Scenario.FormName));
            Dispatcher.UIThread.RunJobs();
            s.Inspect("Menu", "MainMenu1");
        }

        Assert.Equal(new IdentValue("MainMenu1"), s.Object(Phase5Scenario.FormName).FindProperty("Menu")?.Value);

        // The Menu Designer: File > Open… (Ctrl+O), separator, Exit — typed as Insert / text / Enter.
        s.Surface.Selection.Select(s.Object("MainMenu1"));
        Dispatcher.UIThread.RunJobs();
        var command = h.Services.Commands.ExecuteAsync("designer.editComponent");
        var dlg = await h.WaitForDialogAsync("MenuDesigner");
        string file, open, separator, exit;
        try
        {
            dlg.Press("Insert");
            file = TypeCaption(dlg, "&File");
            dlg.Press("Ctrl+Right");
            open = TypeCaption(dlg, "&Open…");
            dlg.Press("Insert");
            separator = TypeCaption(dlg, "-");
            dlg.Press("Insert");
            exit = TypeCaption(dlg, "E&xit");
            ClickNode(dlg, open);
            dlg.Type("ShortCut", "Ctrl+O");
            dlg.Press("Enter");
            Assert.False(dlg.Window.IsClosed());
        }
        catch
        {
            dlg.Window.Close(); // the failure itself is reported, not the dialog left open
            throw;
        }

        dlg.Press("Escape");
        await h.WaitForAsync(() => command.IsCompleted, "Menu Designer closed");
        await command;

        var menu = s.Object("MainMenu1");
        Assert.Equal([file], menu.Children.Select(c => c.Name));
        Assert.Equal([open, separator, exit], s.Object(file).Children.Select(c => c.Name));
        Assert.Equal(16463, ((IntegerValue)s.Object(open).FindProperty("ShortCut")!.Value).Value); // Ctrl+O (UIF-930)
        var expected = DocumentOutline(menu);
        Assert.Equal(["0|popup|&File", "0.0|item|&Open…\\tCtrl+O", "0.1|separator|", "0.2|item|E&xit"], expected);

        // The OnCreate handler: made by a double-click on the form's empty client area, its body written here.
        var client = s.Surface.Geometry.FormClientRect;
        s.DoubleClick(client.Left + 200, client.Top + 200);
        Assert.Equal(DocumentView.Code, s.Document.ActiveView);
        var text = s.Document.Buffer.Text;
        Assert.Contains(HandlerHead, text, StringComparison.Ordinal);
        Assert.Contains("implementation\r\n", text, StringComparison.Ordinal);
        text = text.Replace("implementation\r\n", "implementation\r\n\r\nuses\r\n  Windows, SysUtils;\r\n", StringComparison.Ordinal)
            .Replace(HandlerHead, OutlineProcedure.ReplaceLineEndings("\r\n") + "procedure TMainForm.FormCreate(Sender: TObject);\r\n" + HandlerBody.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        s.Document.Buffer.ReplaceAll(text, resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        await Phase5Scenario.SaveAsync(s);

        // The .form has the nested objects, the unit the fields of the menu and its four items.
        var form = File.ReadAllText(Phase5Scenario.FormPath(h), Encoding.UTF8).ReplaceLineEndings("\n");
        Assert.Contains("  Menu = MainMenu1\n", form, StringComparison.Ordinal);
        Assert.Contains("  OnCreate = FormCreate\n", form, StringComparison.Ordinal);
        var objects = form.Split('\n').Where(l => l.TrimStart().StartsWith("object ", StringComparison.Ordinal)).Skip(1).ToList();
        Assert.Equal(
        [
            "  object MainMenu1: TMainMenu",
            $"    object {file}: TMenuItem",
            $"      object {open}: TMenuItem",
            $"      object {separator}: TMenuItem",
            $"      object {exit}: TMenuItem",
        ], objects);
        Assert.Contains("        Caption = '&Open", form, StringComparison.Ordinal);
        Assert.Contains("        ShortCut = 16463\n", form, StringComparison.Ordinal);
        var unit = File.ReadAllText(Phase5Scenario.UnitPath(h), Encoding.UTF8);
        foreach (var field in (string[])["MainMenu1: TMainMenu;", $"{file}: TMenuItem;", $"{open}: TMenuItem;", $"{separator}: TMenuItem;", $"{exit}: TMenuItem;"])
        {
            Assert.Contains("    " + field + "\r\n", unit, StringComparison.Ordinal);
        }

        // Build with the real toolchain; run with --dump-layout: the handler writes the native outline beside the program.
        var exe = await Phase5Scenario.BuildAsync(h);
        var outline = Path.Combine(Path.GetDirectoryName(exe)!, OutlineFile);
        Assert.False(File.Exists(outline));
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal([Phase5Scenario.FormName], runtime.Forms.Select(f => f.Name));
        Assert.True(File.Exists(outline), "the OnCreate handler wrote no " + OutlineFile);
        var native = File.ReadAllLines(outline, Encoding.UTF8);
        Assert.True(expected.SequenceEqual(native),
            "menu outline of the document:\n" + string.Join("\n", expected) + "\nnative outline of the built program:\n" + string.Join("\n", native)
            + "\n--- form\n" + form);
    }
}
