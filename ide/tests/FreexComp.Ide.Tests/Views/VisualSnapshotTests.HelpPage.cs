using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the help document view (I-265).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>The page of <see cref="HelpPage_WrapsAndAlignsTables"/>: a long paragraph, a small table, a table with a long cell at the end of the file.</summary>
    private static string WrapAndTablesPage()
    {
        var paragraph = string.Join(' ', Enumerable.Range(1, 150).Select(i => i % 7 == 0 ? $"`unit{i}`," : $"word{i}"));
        var longCell = string.Join(' ', Enumerable.Range(1, 80).Select(i => $"cell{i}"));
        return "# Wrapping and tables\n\n" + paragraph + "\n\n## A small table\n\n"
            + "| Name | Kind | Notes |\n|---|:-:|--:|\n| `Left` | Integer | distance from the left edge |\n"
            + "| `Caption` | string | the text; may hold a \\| bar |\n| `Visible` | Boolean |\n\n"
            + "| not a table: no delimiter row follows\n\nText after the table.\n\n"
            + "| Option | Meaning |\n| --- | --- |\n| `-Wide` | " + longCell + " |\n| `-Keep` | short |";
    }

    /// <summary>
    /// UI-MENU-032, light and dark, the main window at 800 × 500 and 1920 × 1040 with a help page open
    /// (<c>help-page-wrap</c>). Rules: word wrap is on and there is no horizontal scrolling — no drawn line is wider than
    /// the text view and a wrapped line breaks after a space; the text shown is the page with its tables aligned (no
    /// delimiter row, no <c>|</c> marks of a table) and the file is untouched; the cells of a table column start at the
    /// same x in every row; headings are <c>type.bodyStrong</c> <c>text.primary</c>, body lines <c>type.body</c>, table
    /// lines <c>type.code</c> with a strong header row; the text keeps its left padding and is drawn inside the window.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-032")]
    public async Task HelpPage_WrapsAndAlignsTables()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string Subject = "help-page-wrap";
        const string PageId = "wrap-and-tables";
        Size[] windowSizes = [new(800, 500), new(1920, 1040)];
        if (Directory.Exists(VisualSnapshot.FolderOf(Subject)))
            Directory.Delete(VisualSnapshot.FolderOf(Subject), recursive: true);

        var source = Encoding.UTF8.GetBytes(WrapAndTablesPage());
        var shown = HelpPageFormatter.Format(WrapAndTablesPage());
        var lines = shown.Text.Split("\r\n");
        Assert.DoesNotContain(lines, l => l.Contains("---", StringComparison.Ordinal));
        Assert.Equal(["| not a table: no delimiter row follows"], lines.Where(l => l.StartsWith('|')));

        // The small table: its header and rows, and where each column starts in the text of every row.
        var header = Array.IndexOf(lines, "Name       Kind     Notes");
        Assert.True(header > 0, shown.Text);
        Assert.Equal("`Caption`  string   the text; may hold a | bar", lines[header + 2]);
        Assert.Equal("`Visible`  Boolean", lines[header + 3]);
        int[] columns = [0, 11, 20];

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.UserFiles["ide/help/" + PageId + ".md"] = source;
            });
            var file = Path.Combine(h.UserDir, "ide", "help", PageId + ".md");
            h.Services.Help.OpenPage(Assert.Single(h.Services.Help.Pages));
            Dispatcher.UIThread.RunJobs();
            var document = Assert.IsType<GeneratedTextDocumentViewModel>(h.Services.Documents.ActiveDocument);
            Assert.Equal(("Wrapping and tables", null, true), (document.Title, document.FilePath, document.IsReadOnly));
            Assert.Equal(shown.Text, document.Text);
            Assert.Equal(source, File.ReadAllBytes(file)); // derived on open; the file is only read

            T Resource<T>(string key) => Assert.IsAssignableFrom<T>(h.Window.FindResource(h.Window.ActualThemeVariant, key));
            var uiFont = Resource<FontFamily>("FxcFontUi").Name;
            var monoFont = Resource<FontFamily>("FxcFontMono").Name;
            var primary = Resource<ISolidColorBrush>("FxcTextPrimaryBrush").Color;
            Assert.NotEqual(uiFont, monoFont);

            foreach (var size in windowSizes)
            {
                VisualSnapshot.Arrange(h.Window, size, 1.0);
                var variant = $"{Subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                Assert.Equal(size, h.Window.ClientSize);
                var tab = h.Find<Control>(AutomationIds.DocumentTab("Wrapping and tables"));
                var editor = h.Find<TextEditor>("Document.Help." + PageId);
                Assert.True(editor.IsReadOnly);
                Assert.False(editor.ShowLineNumbers);
                Assert.Equal(shown.Text, editor.Text);
                foreach (var control in (Control[])[tab, editor])
                {
                    Assert.True(control.IsEffectivelyVisible, $"{variant}: {AutomationIds.Get(control)} is not shown");
                    if (VisualSnapshot.HiddenPart(control, h.Window) is { } hidden)
                        defects.Add((variant, $"cut: {AutomationIds.Get(control)} — {hidden}"));
                }

                // Word wrap, no horizontal scrolling.
                Assert.True(editor.WordWrap, $"{variant}: word wrap is off");
                Assert.Equal(ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
                var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.DoesNotContain(editor.GetVisualDescendants().OfType<ScrollBar>(), b => b.Orientation == Avalonia.Layout.Orientation.Horizontal && b.IsEffectivelyVisible);
                var view = editor.TextArea.TextView;
                Assert.Equal(0, view.ScrollOffset.X);

                // Not even the caret behind the last space of a wrapped line (the only thing a wrapped line may have
                // beyond the view's width) scrolls the text sideways.
                foreach (var offset in (int[])[editor.Document.GetLineByNumber(3).EndOffset, editor.Document.TextLength, 0])
                {
                    editor.TextArea.Caret.Offset = offset;
                    editor.TextArea.Caret.BringCaretToView();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(0, view.ScrollOffset.X);
                }

                Assert.Equal(0, view.ScrollOffset.Y);
                var textLeft = view.TranslatePoint(default, editor)!.Value.X;
                if (textLeft < 8)
                    defects.Add((variant, $"the text starts {textLeft} px from the left edge"));

                view.EnsureVisualLines();
                var wrapped = 0;
                for (var number = 1; number <= editor.Document.LineCount; number++)
                {
                    var text = lines[number - 1];
                    var visual = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(number));
                    foreach (var textLine in visual.TextLines)
                    {
                        // No drawn line is wider than the view …
                        if (textLine.Width > view.Bounds.Width + 0.5)
                            defects.Add((variant, $"line {number} is {textLine.Width} px wide in a view of {view.Bounds.Width} px"));

                        // … and a line continues after a space, never inside a word (or a cell's word).
                        var start = visual.GetTextLineVisualStartColumn(textLine);
                        if (start > 0 && text[start - 1] != ' ')
                            defects.Add((variant, $"line {number} is broken inside a word, before column {start + 1}"));
                    }

                    wrapped += visual.TextLines.Count > 1 ? 1 : 0;
                    if (text.Length == 0)
                        continue;

                    // The style of the line: every run of it.
                    var kind = shown.Lines[number - 1];
                    var table = kind is HelpLineKind.TableHeader or HelpLineKind.TableRow;
                    var strong = kind is HelpLineKind.Heading or HelpLineKind.TableHeader;
                    foreach (var run in visual.Elements.Select(e => e.TextRunProperties))
                    {
                        Assert.True(run.Typeface.FontFamily.Name == (table ? monoFont : uiFont), $"{variant}: line {number} ({kind}) is drawn in {run.Typeface.FontFamily.Name}");
                        Assert.True(run.FontRenderingEmSize == (table ? 13 : 12), $"{variant}: line {number} ({kind}) is drawn at {run.FontRenderingEmSize} px");
                        Assert.True(run.Typeface.Weight == (strong ? FontWeight.SemiBold : FontWeight.Normal), $"{variant}: line {number} ({kind}) has weight {run.Typeface.Weight}");
                        if (kind == HelpLineKind.Heading)
                            Assert.Equal(primary, Assert.IsAssignableFrom<ISolidColorBrush>(run.ForegroundBrush).Color);
                    }
                }

                Assert.True(wrapped >= 2, $"{variant}: only {wrapped} lines wrap (the paragraph and the wide table row are longer than the view)");
                Assert.True(view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(3)).TextLines.Count > 1, $"{variant}: the paragraph is not wrapped");

                // The small table fits the view: the cells of a column start at the same x in every row.
                double X(int line, int column) => view.GetVisualPosition(new TextViewPosition(line, column + 1), VisualYPosition.LineTop).X;
                for (var row = header; row <= header + 3; row++)
                {
                    Assert.Single(view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(row + 1)).TextLines);
                    foreach (var column in columns.Where(c => c < lines[row].Length))
                    {
                        Assert.True(column == 0 || lines[row][column - 1] == ' ', $"line {row + 1}: no column starts at {column}");
                        Assert.NotEqual(' ', lines[row][column]);
                        if (Math.Abs(X(row + 1, column) - X(header + 1, column)) > 0.01)
                            defects.Add((variant, $"line {row + 1}: the cell at column {column} starts at x = {X(row + 1, column)}, the header's at {X(header + 1, column)}"));
                    }
                }

                Assert.True(X(header + 1, columns[0]) < X(header + 1, columns[1]) && X(header + 1, columns[1]) < X(header + 1, columns[2]));

                if (VisualSnapshot.DistinctColors(h.Window, editor) < 3)
                    defects.Add((variant, "the page text is not drawn"));
                defects.AddRange(VisualSnapshot.LayoutDefects(tab).Select(d => (variant, d)));
                files.Add((Subject, theme, size, VisualSnapshot.Capture(h.Window, Subject, theme)));
            }

            // Another generated document is not a help page: no wrap, the code font, a horizontal scroll bar when needed.
            await h.ExecuteAsync("help.keyboardShortcuts");
            Dispatcher.UIThread.RunJobs();
            var shortcuts = h.Find<TextEditor>("Document.KeyboardShortcuts");
            Assert.False(shortcuts.WordWrap);
            Assert.Equal(ScrollBarVisibility.Auto, shortcuts.HorizontalScrollBarVisibility);
            Assert.Equal(monoFont, shortcuts.FontFamily.Name);
            Assert.Equal(13, shortcuts.FontSize);
            Assert.Equal(source, File.ReadAllBytes(file));
        }

        AssertSnapshots(files, [Subject], 4, defects);
    }
}
