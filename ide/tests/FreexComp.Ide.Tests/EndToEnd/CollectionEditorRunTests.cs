using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// D-160 (card F05-087a; DSG-OI-135): the status-bar panels made and reordered in the collection editor are the panels of
/// the built program, in the same order. A new gui project with a status bar from the palette, the panels edited through the
/// verb <c>designer.editCollection</c> on <c>Panels</c>, an <c>OnCreate</c> handler whose body — written by this test —
/// prints the panels and the native parts; saved, built with the real toolchain and run with <c>--dump-layout</c>, twice.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class CollectionEditorRunTests
{
    private const string OutlineFile = "panels-outline.txt";

    private const string HandlerHead = "procedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n";

    /// <summary>The outline of DSG-OI-135: the panels as the program holds them, then the native parts (<c>SB_GETPARTS</c>, <c>SB_GETTEXTW</c>).</summary>
    private const string HandlerBody = """
        var
          Lines: TStringList;
          Edges: array[0..15] of Integer;
          Buffer: array[0..255] of WideChar;
          I, N: Integer;
          S: string;
        begin
          Lines := TStringList.Create;
          try
            Lines.Add('count=' + IntToStr(StatusBar1.Panels.Count));
            for I := 0 to StatusBar1.Panels.Count - 1 do
              Lines.Add(IntToStr(I) + '|' + StatusBar1.Panels[I].Text + '|' + IntToStr(StatusBar1.Panels[I].Width));
            StatusBar1.HandleNeeded;
            N := Integer(SendMessageW(StatusBar1.Handle, SB_GETPARTS, 16, LPARAM(@Edges[0])));
            Lines.Add('parts=' + IntToStr(N));
            S := '';
            for I := 0 to N - 1 do
            begin
              if I > 0 then
                S := S + ',';
              S := S + IntToStr(Edges[I]);
            end;
            Lines.Add('edges=' + S);
            for I := 0 to N - 1 do
            begin
              FillChar(Buffer, SizeOf(Buffer), 0);
              SendMessageW(StatusBar1.Handle, SB_GETTEXTW, WPARAM(I), LPARAM(@Buffer[0]));
              S := PWideChar(@Buffer[0]);
              Lines.Add('text' + IntToStr(I) + '=' + S);
            end;
            Lines.SaveToFile(ExtractFilePath(ParamStr(0)) + 'panels-outline.txt', TEncoding.UTF8);
          finally
            Lines.Free;
          end;

        """;

    /// <summary>
    /// The collection outline of DSG-OI-135 computed from the form document: <c>count</c>, one line per item with the effective
    /// <c>Text</c> and <c>Width</c> (default 50), then the native view — the parts with cumulative right edges, the last −1
    /// (UIF-780), and the text of every part.
    /// </summary>
    private static IReadOnlyList<string> DocumentOutline(FormObject statusBar)
    {
        var items = statusBar.FindProperty("Panels")?.Value is CollectionValue { Items: var stored } ? stored : [];
        var panels = items.Select(item => (
            Text: item.Properties.FirstOrDefault(p => p.Path == "Text").Value is StringValue { Value: var text } ? text : "",
            Width: item.Properties.FirstOrDefault(p => p.Path == "Width").Value is IntegerValue { Value: var width } ? width : 50)).ToList();
        var lines = new List<string> { "count=" + Number(panels.Count) };
        lines.AddRange(panels.Select((p, i) => $"{Number(i)}|{p.Text}|{Number(p.Width)}"));
        lines.Add("parts=" + Number(panels.Count));
        var edge = 0L;
        lines.Add("edges=" + string.Join(",", panels.Select((p, i) => i == panels.Count - 1 ? "-1" : Number(edge += p.Width))));
        lines.AddRange(panels.Select((p, i) => $"text{Number(i)}={p.Text}"));
        return lines;
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The verb <c>designer.editCollection</c> with the argument <c>Panels</c> on the selected status bar; <paramref name="drive"/> ends with OK.</summary>
    private static async Task EditPanelsAsync(Phase5Scenario.Session s, Action<DialogDriver> drive)
    {
        var h = s.H;
        s.Surface.Selection.Select(s.Object("StatusBar1"));
        Dispatcher.UIThread.RunJobs();
        var undo = s.Document.Designer.Undo.UndoCount;
        var command = h.Services.Commands.ExecuteAsync("designer.editCollection", "Panels");
        var dlg = await h.WaitForDialogAsync("CollectionEditor");
        try
        {
            Assert.Equal("Edit StatusBar1.Panels", dlg.Window.Title);
            drive(dlg);
            Assert.False(dlg.Window.IsClosed());
        }
        catch
        {
            dlg.Window.Close(); // the failure itself is reported, not the dialog left open
            throw;
        }

        dlg.Click("Ok");
        await h.WaitForAsync(() => command.IsCompleted, "collection editor closed");
        await command;
        Assert.Equal(undo + 1, s.Document.Designer.Undo.UndoCount); // OK is one undo unit (DSG-OI-114)
    }

    /// <summary>
    /// The value typed into the row <paramref name="property"/> of the embedded grid for the selected item: a click on the
    /// item in the list, F4 to the grid, the row, F2, the text, Enter.
    /// </summary>
    private static void SetProperty(DialogDriver dlg, string property, string value)
    {
        const string Rows = "Dialog.CollectionEditor.Grid.Row.";
        string? Current() => dlg.FindAll(Rows).FirstOrDefault(c => c.Classes.Contains("current")) is { } row ? AutomationIds.Get(row)![Rows.Length..] : null;
        dlg.Click("Item." + Number(dlg.Find<ListBox>("Items").SelectedIndex));
        dlg.Press("F4");
        Assert.Equal("Dialog.CollectionEditor.Grid", dlg.FocusedAutomationId);
        dlg.Press("Home");
        for (var i = 0; i < 20 && Current() != property; i++)
        {
            dlg.Press("Down");
        }

        Assert.Equal(property, Current());
        dlg.Press("F2");
        var editor = dlg.Find<TextBox>("Grid.Row." + property + ".Editor");
        editor.SelectAll();
        dlg.Window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Enter");
        Assert.False(dlg.Window.IsClosed()); // Enter in an editor commits the value only
        Assert.Equal(value, dlg.Find<TextBlock>("Grid.Row." + property + ".Value").Text);
    }

    private static IReadOnlyList<string> Items(DialogDriver dlg) =>
        [.. Enumerable.Range(0, dlg.Find<ListBox>("Items").ItemCount).Select(i => (string)dlg.Find<ListBoxItem>("Item." + Number(i)).Content!)];

    /// <summary>Saves, builds with the real toolchain and runs with <c>--dump-layout</c>; the outline the handler wrote beside the program.</summary>
    private static async Task<string[]> SaveBuildRunAsync(Phase5Scenario.Session s)
    {
        var h = s.H;
        await Phase5Scenario.SaveAsync(s);
        var exe = await Phase5Scenario.BuildAsync(h);
        var outline = Path.Combine(Path.GetDirectoryName(exe)!, OutlineFile);
        File.Delete(outline);
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
        Assert.Equal([Phase5Scenario.FormName], runtime.Forms.Select(f => f.Name));
        Assert.True(File.Exists(outline), "the OnCreate handler wrote no " + OutlineFile);
        return File.ReadAllLines(outline, Encoding.UTF8);
    }

    private static void AssertOutline(IReadOnlyList<string> expected, string[] native, string form) =>
        Assert.True(expected.SequenceEqual(native),
            "collection outline of the document:\n" + string.Join("\n", expected) + "\noutline of the built program:\n" + string.Join("\n", native) + "\n--- form\n" + form);

    private static string[] ItemBlocks(string form)
    {
        var lines = form.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim() == "Panels = <");
        var end = Array.FindIndex(lines, start + 1, l => l.Trim() == ">");
        Assert.True(start >= 0 && end > start, "no Panels collection in the form:\n" + form);
        return [.. lines[(start + 1)..end].Select(l => l.Trim())];
    }

    [AvaloniaFact(Timeout = 300000)]
    [Trait("Task", "D-160")]
    [Trait("Req", "DSG-OI-135")]
    public async Task StatusPanelsReachRuntimeInOrder()
    {
        await using var h = await Phase5Scenario.StartAsync();
        var s = await Phase5Scenario.CreateProjectAndDesignAsync(h);
        await h.ExecuteAsync("view.toolPalette");
        s.Place("TStatusBar", 40, 40);
        Assert.Equal(["StatusBar1"], s.Selected);
        Assert.Null(s.Object("StatusBar1").FindProperty("Panels"));

        // The collection editor: Add three items — Ready / 120, Line 1 / 80, Last (default width) — OK.
        await EditPanelsAsync(s, dlg =>
        {
            Assert.Empty(Items(dlg));
            dlg.Click("Add");
            SetProperty(dlg, "Text", "Ready");
            SetProperty(dlg, "Width", "120");
            dlg.Click("Add");
            SetProperty(dlg, "Text", "Line 1");
            SetProperty(dlg, "Width", "80");
            dlg.Click("Add");
            SetProperty(dlg, "Text", "Last");
            Assert.Equal(["0 – Ready", "1 – Line 1", "2 – Last"], Items(dlg));
            Assert.Null(s.Object("StatusBar1").FindProperty("Panels")); // the form is untouched while the dialog is open
        });
        var expected = DocumentOutline(s.Object("StatusBar1"));
        Assert.Equal(["count=3", "0|Ready|120", "1|Line 1|80", "2|Last|50", "parts=3", "edges=120,200,-1", "text0=Ready", "text1=Line 1", "text2=Last"], expected);

        // The OnCreate handler: made by a double-click on the form's empty client area, its body written here.
        var client = s.Surface.Geometry.FormClientRect;
        s.DoubleClick(client.Left + 200, client.Top + 120);
        Assert.Equal(DocumentView.Code, s.Document.ActiveView);
        var text = s.Document.Buffer.Text;
        Assert.Contains(HandlerHead, text, StringComparison.Ordinal);
        Assert.Contains("implementation\r\n", text, StringComparison.Ordinal);
        text = text.Replace("implementation\r\n", "implementation\r\n\r\nuses\r\n  Windows, SysUtils, CommCtrl;\r\n", StringComparison.Ordinal)
            .Replace(HandlerHead, "procedure TMainForm.FormCreate(Sender: TObject);\r\n" + HandlerBody.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        s.Document.Buffer.ReplaceAll(text, resetUndo: false);
        Dispatcher.UIThread.RunJobs();

        var native = await SaveBuildRunAsync(s);
        var form = File.ReadAllText(Phase5Scenario.FormPath(h), Encoding.UTF8);
        Assert.Equal(
        [
            "item", "Text = 'Ready'", "Width = 120", "end",
            "item", "Text = 'Line 1'", "Width = 80", "end",
            "item", "Text = 'Last'", "end",
        ], ItemBlocks(form));
        AssertOutline(expected, native, form);

        // Move Up on Line 1, OK: the item blocks of the .form, the panels of the program and the native parts follow.
        h.Press("F7");
        Assert.Equal(DocumentView.Design, s.Document.ActiveView);
        await EditPanelsAsync(s, dlg =>
        {
            Assert.Equal(["0 – Ready", "1 – Line 1", "2 – Last"], Items(dlg));
            dlg.Click("Item.1");
            Assert.Equal(1, dlg.Find<ListBox>("Items").SelectedIndex);
            dlg.Click("MoveUp");
            Assert.Equal(["0 – Line 1", "1 – Ready", "2 – Last"], Items(dlg));
        });
        expected = DocumentOutline(s.Object("StatusBar1"));
        Assert.Equal(["count=3", "0|Line 1|80", "1|Ready|120", "2|Last|50", "parts=3", "edges=80,200,-1", "text0=Line 1", "text1=Ready", "text2=Last"], expected);

        native = await SaveBuildRunAsync(s);
        form = File.ReadAllText(Phase5Scenario.FormPath(h), Encoding.UTF8);
        Assert.Equal(
        [
            "item", "Text = 'Line 1'", "Width = 80", "end",
            "item", "Text = 'Ready'", "Width = 120", "end",
            "item", "Text = 'Last'", "end",
        ], ItemBlocks(form));
        AssertOutline(expected, native, form);
    }
}
