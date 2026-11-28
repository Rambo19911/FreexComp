using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Find References in the running shell (I-353, F13-016a): <c>Shift+F12</c> lists the references to the symbol at the
/// caret in Search Results as a result set of kind References (tool-windows.md §8, UI-TW-609…614) — file, container
/// and reference rows with kind badges — without moving the keyboard focus; rows navigate, F8 steps.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ReferencesViewTests
{
    internal const string Main =
        "program Hello;\r\nuses Unit1, Unit2;\r\nvar\r\n  C: TCounter;\r\nbegin\r\n  C := TCounter.Create;\r\n  C.Step;\r\n  Twice(C);\r\n  C.Draw;\r\n  Writeln(C.Count);\r\nend.\r\n";

    internal const string Unit1 =
        "unit Unit1;\r\ninterface\r\nuses LibShapes;\r\ntype\r\n  TCounter = class(TShape)\r\n  public\r\n    Count: Integer;\r\n    procedure Step;\r\n    procedure Draw; override;\r\n  end;\r\n"
        + "implementation\r\nprocedure TCounter.Step;\r\nbegin\r\n  Count := Count + 1;\r\nend;\r\nprocedure TCounter.Draw;\r\nbegin\r\n  inherited Draw;\r\n  Step;\r\nend;\r\nend.\r\n";

    internal const string Unit2 =
        "unit Unit2;\r\ninterface\r\nuses Unit1;\r\nprocedure Twice(C: TCounter);\r\nimplementation\r\nprocedure Twice(C: TCounter);\r\nbegin\r\n"
        + "  C.Step;\r\n  C.Step;\r\n  if C.Count > 3 then\r\n    C.Count := 0;\r\nend;\r\nend.\r\n";

    /// <summary>A unit of the run-time library folder: a library source (IDE-LIB-001).</summary>
    internal const string Library =
        "unit LibShapes;\r\ninterface\r\ntype\r\n  TShape = class\r\n  public\r\n    procedure Draw; virtual;\r\n    procedure Paint;\r\n  end;\r\n"
        + "implementation\r\nprocedure TShape.Draw;\r\nbegin\r\nend;\r\nprocedure TShape.Paint;\r\nbegin\r\n  Draw;\r\nend;\r\nend.\r\n";

    /// <summary>
    /// The project Hello (program, Unit1, Unit2) over the real system unit: a field that is declared, read and written, a
    /// method called from both other files, and a virtual method of a library class that the project overrides.
    /// </summary>
    internal static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) => BuildScenario.StartAsync(o =>
    {
        LiveDiagnosticsTests.AddRealRtl(o);
        o.InitialFiles[@"tc\rtl\LibShapes.pas"] = Library;
        o.InitialFiles[@"Hello\Hello.pproj"] = """
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" } ] }
            """;
        o.InitialFiles[@"Hello\Hello.pas"] = Main;
        o.InitialFiles[@"Hello\Unit1.pas"] = Unit1;
        o.InitialFiles[@"Hello\Unit2.pas"] = Unit2;
        configure?.Invoke(o);
    }, implementedPhase: null);

    internal static string PathOf(HeadlessHarness h, string file) => Path.Combine(h.TempDir, "Hello", file);

    /// <summary>Opens <paramref name="file"/> of the project and puts the caret, with the keyboard focus, at <paramref name="offset"/>.</summary>
    internal static async Task<TextEditor> OpenAtAsync(HeadlessHarness h, string file, int offset)
    {
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(PathOf(h, file))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor." + file);
        doc.Selection = TextSelection.At(offset);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(offset, editor.TextArea.Caret.Offset);
        return editor;
    }

    /// <summary>Shift+F12 with the caret inside <paramref name="text"/> of <paramref name="file"/>; waits for the header of the result.</summary>
    internal static async Task<TextEditor> FindAsync(HeadlessHarness h, string file, string source, string text, string header)
    {
        var editor = await OpenAtAsync(h, file, source.IndexOf(text, StringComparison.Ordinal) + text.Length - 2);
        h.Press("Shift+F12");
        await WaitHeaderAsync(h, header);
        return editor;
    }

    internal static async Task WaitHeaderAsync(HeadlessHarness h, string header)
    {
        await h.WaitForAsync(() => h.TryFind<TextBlock>("Tool.SearchResults.Header") is { IsEffectivelyVisible: true } block && block.Text == header,
            "the header " + header);
        await h.Services.FindReferences.Running;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The keys of the list's rows (the list virtualizes: rows outside the viewport have no control).</summary>
    internal static List<string> RowKeys(HeadlessHarness h) =>
        [.. (h.Find<ListBox>("Tool.SearchResults.List").ItemsSource ?? Array.Empty<object>()).OfType<SearchResultRow>().Select(r => r.Key)];

    /// <summary>Scrolls the row <paramref name="key"/> into view and returns the control <c>Tool.SearchResults.Row.&lt;key&gt;&lt;cell&gt;</c>.</summary>
    internal static T Cell<T>(HeadlessHarness h, string key, string cell = "") where T : Control
    {
        var list = h.Find<ListBox>("Tool.SearchResults.List");
        list.ScrollIntoView(list.ItemsSource!.OfType<SearchResultRow>().First(r => r.Key == key));
        Dispatcher.UIThread.RunJobs();
        return h.Find<T>("Tool.SearchResults.Row." + key + cell);
    }

    /// <summary>Scrolls the row into view and clicks it (twice: the default action).</summary>
    private static void ClickRow(HeadlessHarness h, string key, bool twice = false)
    {
        Cell<Control>(h, key);
        if (twice)
            h.DoubleClick("Tool.SearchResults.Row." + key);
        else
            h.Click("Tool.SearchResults.Row." + key);
    }

    private static string? SelectedKey(HeadlessHarness h) => (h.Find<ListBox>("Tool.SearchResults.List").SelectedItem as SearchResultRow)?.Key;

    /// <summary>The text before the highlighted run and the highlighted text of a reference row's <c>.Text</c> cell.</summary>
    private static (string Before, string Match) Highlight(TextBlock text)
    {
        var runs = text.Inlines!.OfType<Run>().ToList();
        var index = runs.FindIndex(r => r.Background is not null);
        Assert.True(index >= 0, "no highlighted run");
        return (string.Concat(runs.Take(index).Select(r => r.Text)), runs[index].Text!);
    }

    private static string TextOf(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";

    /// <summary>The colour of a design token in the window's theme.</summary>
    internal static Color Token(Window window, string token) =>
        window.TryFindResource(IconCatalog.BrushKey(token), window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException("No brush for " + token);

    private static (int Start, int Length) Selection(TextEditor editor) => (editor.SelectionStart, editor.SelectionLength);

    /// <summary>A navigation has arrived: the editor of <paramref name="file"/> has the keyboard focus and this selection.</summary>
    private static async Task<TextEditor> WaitSelectionAsync(HeadlessHarness h, string file, int start, int length)
    {
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor." + file && h.TryFind<TextEditor>("Editor." + file) is { } editor
            && Selection(editor) == (start, length), $"{file} has the focus with the selection ({start}, {length})");
        return h.Find<TextEditor>("Editor." + file);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-FREF-003")]
    [Trait("Req", "IDE-FREF-007")]
    [Trait("Req", "UI-TW-609")]
    [Trait("Req", "UI-TW-614")]
    public async Task ShiftF12ListsAndEnterNavigates()
    {
        await using var h = await StartAsync();
        Assert.Null(h.TryFind<Control>("Tool.SearchResults.List")); // the window is not shown before
        var caret = Unit2.IndexOf("Count >", StringComparison.Ordinal) + 5;
        var unit2 = await OpenAtAsync(h, "Unit2.pas", caret); // at the end of the identifier
        Assert.True(h.Services.Commands.CanExecute("search.findReferences"));

        h.Press("Shift+F12");
        await WaitHeaderAsync(h, "References to Count — 6 references in 3 files");

        // The window is shown with its tab selected; the keyboard focus has not moved.
        Assert.Equal("Editor.Unit2.pas", h.FocusedAutomationId);
        Assert.Equal(SearchResultsViewModel.ToolName, h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.True(h.Find<ListBox>("Tool.SearchResults.List").IsEffectivelyVisible);
        Assert.Equal(caret, unit2.TextArea.Caret.Offset);

        // Three levels: file, container, reference; the reference under the caret is the selected row.
        Assert.Equal(
        [
            "Hello.pas", "Hello.pas#(unit level)", "Hello.pas:10:13",
            "Unit1.pas", "Unit1.pas#TCounter", "Unit1.pas:7:5", "Unit1.pas#TCounter.Step", "Unit1.pas:14:3", "Unit1.pas:14:12",
            "Unit2.pas", "Unit2.pas#Twice", "Unit2.pas:10:8", "Unit2.pas:11:7",
        ], RowKeys(h));
        Assert.Equal("Unit2.pas:10:8", SelectedKey(h));
        var file = Cell<Control>(h, "Unit1.pas");
        var container = Cell<Control>(h, "Unit1.pas#TCounter.Step");
        var reference = Cell<Control>(h, "Unit1.pas:14:3");
        Assert.True(file.Margin.Left < container.Margin.Left && container.Margin.Left < reference.Margin.Left, "each level is further in");
        Assert.Equal("Unit1.pas", AutomationProperties.GetName(file));
        Assert.Equal("TCounter.Step", AutomationProperties.GetName(container));

        // The cells of a reference row: kind badge, line number and the line with the identifier highlighted.
        var kind = Cell<Border>(h, "Unit1.pas:14:3", ".Kind");
        Assert.Equal("W", Assert.IsType<TextBlock>(kind.Child).Text);
        var line = Cell<TextBlock>(h, "Unit1.pas:14:3", ".Line");
        Assert.Equal("14", line.Text);
        var row = Cell<Control>(h, "Unit1.pas:14:3");
        Assert.True(kind.TranslatePoint(default, row)!.Value.X < line.TranslatePoint(default, row)!.Value.X, "the badge comes before the line number");
        Assert.Equal(("", "Count"), Highlight(Cell<TextBlock>(h, "Unit1.pas:14:3", ".Text")));
        Assert.Equal(("Count := ", "Count"), Highlight(Cell<TextBlock>(h, "Unit1.pas:14:12", ".Text")));
        Assert.Equal(("if C.", "Count"), Highlight(Cell<TextBlock>(h, "Unit2.pas:10:8", ".Text")));

        // Enter on a reference row: the other unit opens with the identifier selected and the keyboard focus.
        ClickRow(h, "Unit1.pas:14:3");
        Assert.Equal("Tool.SearchResults.List", h.FocusedAutomationId);
        h.Press("Enter");
        await WaitSelectionAsync(h, "Unit1.pas", Unit1.IndexOf("Count :=", StringComparison.Ordinal), 5);

        // The departures are in the navigation history (IDE-ED-100): where Find References was started, and the top of the
        // unit the jump opened. Alt+Left goes back through them.
        Assert.Equal([(PathOf(h, "Unit2.pas"), new TextPosition(10, 13)), (PathOf(h, "Unit1.pas"), new TextPosition(1, 1))],
            h.Services.Navigation.History.TakeLast(2).Select(l => (l.FilePath!, l.Position)));
        h.Press("Alt+Left");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas" && h.Find<TextEditor>("Editor.Unit1.pas").TextArea.Caret.Offset == 0, "the top of Unit1");
        h.Press("Alt+Left");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit2.pas" && h.Find<TextEditor>("Editor.Unit2.pas").TextArea.Caret.Offset == caret, "back in Unit2, where the caret was");

        // A double-click navigates as well; on a container row and on a file row the default action toggles the row.
        // (A click elsewhere first: the row lies where the row clicked before lay, and for the headless pointer no time
        // passes between clicks — it would count this press as the second click of that earlier one.)
        h.Click("Tool.SearchResults.Header");
        ClickRow(h, "Hello.pas:10:13", twice: true);
        await WaitSelectionAsync(h, "Hello.pas", Main.IndexOf("Count)", StringComparison.Ordinal), 5);
        ClickRow(h, "Unit1.pas#TCounter.Step");
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Unit1.pas:14:3", RowKeys(h));
        Assert.Contains("Unit1.pas:7:5", RowKeys(h));
        h.Press("Right"); // Right / Left expand and collapse a container row as a file row
        Assert.Contains("Unit1.pas:14:3", RowKeys(h));
        Assert.Equal("Unit1.pas#TCounter.Step", SelectedKey(h));
        ClickRow(h, "Unit1.pas");
        h.Press("Left");
        Assert.Equal(["Hello.pas", "Hello.pas#(unit level)", "Hello.pas:10:13", "Unit1.pas", "Unit2.pas", "Unit2.pas#Twice", "Unit2.pas:10:8", "Unit2.pas:11:7"], RowKeys(h));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-609")]
    [Trait("Req", "UI-TW-610")]
    [Trait("Req", "UI-TW-611")]
    [Trait("Req", "UI-TW-612")]
    public async Task KindBadgesAndCounts()
    {
        await using var h = await StartAsync();

        void Badge(string key, ReferenceKind kind, string letter, string name, string token)
        {
            var badge = Cell<Border>(h, key, ".Kind");
            var text = Assert.IsType<TextBlock>(badge.Child);
            Assert.Equal(letter, text.Text);
            Assert.Equal(FontWeight.Bold, text.FontWeight);
            Assert.Contains("caption", text.Classes);
            Assert.Equal(Token(h.Window, token), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal((SearchResultsView.BadgeSize, SearchResultsView.BadgeSize), (badge.Bounds.Width, badge.Bounds.Height));
            Assert.Equal(name, AutomationProperties.GetName(badge));
            Assert.Equal(name, ToolTip.GetTip(badge));
            Assert.Equal(kind, h.Services.SearchResults.Rows.Single(r => r.Key == key).ReferenceKind);
        }

        // The field: declaration, write, read.
        await FindAsync(h, "Unit2.pas", Unit2, "Count >", "References to Count — 6 references in 3 files");
        Badge("Unit1.pas:7:5", ReferenceKind.Declaration, "D", Strings.Msg_References_KindDeclaration, "accent.default");
        Badge("Unit1.pas:14:3", ReferenceKind.Write, "W", Strings.Msg_References_KindWrite, "status.warning");
        Badge("Unit1.pas:14:12", ReferenceKind.Read, "R", Strings.Msg_References_KindRead, "text.secondary");

        // Counts on the file row and on the container rows; no library part in the header.
        Assert.Equal("Unit1.pas  (3)", TextOf(Cell<Control>(h, "Unit1.pas").GetVisualDescendants().OfType<TextBlock>().First(t => t.Inlines is { Count: > 0 })));
        Assert.Equal("TCounter  (1)", Cell<Control>(h, "Unit1.pas#TCounter").GetVisualDescendants().OfType<TextBlock>().Single().Text);
        var step = Cell<Control>(h, "Unit1.pas#TCounter.Step").GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal("TCounter.Step  (2)", step.Text);
        Assert.Contains("body", step.Classes);
        Assert.Equal("(unit level)  (1)", Cell<Control>(h, "Hello.pas#(unit level)").GetVisualDescendants().OfType<TextBlock>().Single().Text);

        // The method: declaration in the class, implementation, calls.
        await FindAsync(h, "Hello.pas", Main, "C.Step", "References to Step — 6 references in 3 files");
        Badge("Unit1.pas:8:15", ReferenceKind.Declaration, "D", Strings.Msg_References_KindDeclaration, "accent.default");
        Badge("Unit1.pas:12:20", ReferenceKind.Implementation, "I", Strings.Msg_References_KindImplementation, "accent.default");
        Badge("Unit2.pas:8:5", ReferenceKind.Call, "C", Strings.Msg_References_KindCall, "text.secondary");

        // The type.
        await FindAsync(h, "Unit2.pas", Unit2, "TCounter", "References to TCounter — 7 references in 3 files");
        Badge("Hello.pas:4:6", ReferenceKind.TypeReference, "T", Strings.Msg_References_KindTypeReference, "text.secondary");
        Badge("Unit1.pas:5:3", ReferenceKind.Declaration, "D", Strings.Msg_References_KindDeclaration, "accent.default");

        // A virtual method of a library class with its override in the project: the library file comes last, collapsed,
        // with its suffix; the header counts its references.
        await FindAsync(h, "Hello.pas", Main, "C.Draw", "References to Draw — 7 references in 3 files (3 in library sources)");
        var library = Path.Combine(h.TempDir, @"tc\rtl\LibShapes.pas");
        Assert.Equal(
        [
            "Hello.pas", "Hello.pas#(unit level)", "Hello.pas:9:5",
            "Unit1.pas", "Unit1.pas#TCounter", "Unit1.pas:9:15", "Unit1.pas#TCounter.Draw", "Unit1.pas:16:20", "Unit1.pas:18:13",
            library,
        ], RowKeys(h));
        var row = Cell<Control>(h, library);
        var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Inlines is { Count: > 0 });
        Assert.Equal("LibShapes.pas  (3)   " + Strings.Msg_References_Library, TextOf(name));
        var suffix = name.Inlines!.OfType<Run>().Last();
        Assert.Equal(Token(h.Window, "text.tertiary"), Assert.IsAssignableFrom<ISolidColorBrush>(suffix.Foreground).Color);
        Assert.Equal("chevron-right", row.GetVisualDescendants().OfType<IdeIcon>().First().IconName);
        Assert.Equal("chevron-down", Cell<Control>(h, "Unit1.pas").GetVisualDescendants().OfType<IdeIcon>().First().IconName);

        // Expanded by Right: its containers and references, the library sources as protected places.
        ClickRow(h, library);
        h.Press("Right");
        Assert.Equal([library, library + "#TShape", library + ":6:15", library + "#TShape.Draw", library + ":10:18", library + "#TShape.Paint", library + ":15:3"],
            RowKeys(h).Skip(9));
        ClickRow(h, library + ":15:3");
        h.Press("Enter");
        await WaitSelectionAsync(h, "LibShapes.pas", Library.LastIndexOf("Draw", StringComparison.Ordinal), 4);
        var opened = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.True(opened.IsLibraryProtected); // IDE-LIB-002
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-FREF-003")]
    [Trait("Req", "IDE-FREF-007")]
    public async Task F8StepsThroughReferences()
    {
        await using var h = await StartAsync();
        await FindAsync(h, "Unit2.pas", Unit2, "Count >", "References to Count — 6 references in 3 files");
        Assert.Equal("Editor.Unit2.pas", h.FocusedAutomationId);

        // F8 from the editor: the reference after the one under the caret; the focus stays in the editor.
        h.Press("F8");
        await WaitSelectionAsync(h, "Unit2.pas", Unit2.IndexOf("Count :=", StringComparison.Ordinal), 5);
        Assert.Equal("Unit2.pas:11:7", SelectedKey(h));

        // After the last one the first: the program opens.
        h.Press("F8");
        await WaitSelectionAsync(h, "Hello.pas", Main.IndexOf("Count)", StringComparison.Ordinal), 5);
        Assert.Equal("Hello.pas:10:13", SelectedKey(h));

        // Shift+F8 goes back.
        h.Press("Shift+F8");
        await WaitSelectionAsync(h, "Unit2.pas", Unit2.IndexOf("Count :=", StringComparison.Ordinal), 5);

        // An edit above a reference: the row still navigates to the identifier (the place is tracked, UI-TW-604).
        h.Press("F8");
        await WaitSelectionAsync(h, "Hello.pas", Main.IndexOf("Count)", StringComparison.Ordinal), 5);
        h.Press("F8");
        var unit1 = await WaitSelectionAsync(h, "Unit1.pas", Unit1.IndexOf("Count: Integer", StringComparison.Ordinal), 5);
        const string inserted = "// a note above\r\n";
        unit1.Document.Insert(0, inserted);
        Dispatcher.UIThread.RunJobs();
        h.Press("F8");
        await WaitSelectionAsync(h, "Unit1.pas", inserted.Length + Unit1.IndexOf("Count :=", StringComparison.Ordinal), 5);
        Assert.Equal("Count", unit1.SelectedText);
        Assert.Equal("Unit1.pas:14:3", SelectedKey(h)); // the row keeps its key: positions at search time
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-613")]
    public async Task FilterAndCopy()
    {
        await using var h = await StartAsync();
        await FindAsync(h, "Unit2.pas", Unit2, "Count >", "References to Count — 6 references in 3 files");
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        var unit1 = PathOf(h, "Unit1.pas");

        void Filter(string text)
        {
            h.Find<TextBox>("Tool.SearchResults.Filter").Text = text;
            h.Clock.Advance(ToolWindowViewModel.FilterDelay);
            Dispatcher.UIThread.RunJobs();
        }

        async Task<string> CopyAsync(string key)
        {
            clipboard.Text = null;
            ClickRow(h, key);
            h.Press("Ctrl+C");
            await h.WaitForAsync(() => clipboard.Text is not null, "copied " + key);
            return clipboard.Text!;
        }

        // A container name: the container stays with all its references, although their lines do not hold the text.
        Filter("counter.step");
        Assert.Equal(["Unit1.pas", "Unit1.pas#TCounter.Step", "Unit1.pas:14:3", "Unit1.pas:14:12"], RowKeys(h));

        // A line text: the references whose line holds it, under their containers.
        Filter(":= 0");
        Assert.Equal(["Unit2.pas", "Unit2.pas#Twice", "Unit2.pas:11:7"], RowKeys(h));

        // A file name: everything of the file.
        Filter("unit1");
        Assert.Equal(["Unit1.pas", "Unit1.pas#TCounter", "Unit1.pas:7:5", "Unit1.pas#TCounter.Step", "Unit1.pas:14:3", "Unit1.pas:14:12"], RowKeys(h));
        Filter("");
        Assert.Equal(13, RowKeys(h).Count);

        // Copy: a reference row names its kind, a container row its file and its name, a file row its path.
        Assert.Equal(unit1 + "(14,3): " + Strings.Msg_References_KindWrite + ": Count := Count + 1;", await CopyAsync("Unit1.pas:14:3"));
        Assert.Equal(unit1 + "(7,5): " + Strings.Msg_References_KindDeclaration + ": Count: Integer;", await CopyAsync("Unit1.pas:7:5"));
        Assert.Equal(unit1 + ": TCounter.Step", await CopyAsync("Unit1.pas#TCounter.Step"));
        Assert.Equal(unit1, await CopyAsync("Unit1.pas"));
    }
}
