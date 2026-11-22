using FreexComp.Designer.Catalog;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>
/// Layout tests run on the real framework metadata (<c>ui-framework/design/</c>, DSG-LAY-055): the C# engine must agree
/// with the run-time engine, whose class ancestry and defaults come from there.
/// </summary>
internal static class LayoutHarness
{
    private static readonly Lazy<string> RepoRootValue = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("repository root (FreexComp.sln) not found above " + AppContext.BaseDirectory);
    });

    private static readonly Lazy<ComponentCatalog> FrameworkCatalog = new(() =>
    {
        var index = Path.Combine(RepoRoot, "ui-framework", "design", "index.json");
        Assert.True(File.Exists(index), "Framework metadata not found: " + index);
        var result = CatalogLoader.Load([index]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    public static string RepoRoot => RepoRootValue.Value;

    public static string CorpusFolder => Path.Combine(RepoRoot, "tests", "layout-corpus");

    public static ComponentCatalog Catalog => FrameworkCatalog.Value;

    public static LayoutEngine Engine => new(Catalog, FreexComp.Designer.Rendering.RendererRegistry.CreateDefault());

    /// <summary>Parses a form (LF, two-space indent) with the framework catalog; fails on any diagnostic.</summary>
    public static FormDocument Parse(string text, string path = "layout.form")
    {
        var result = FormParser.Parse(text.Replace("\r\n", "\n"), path, Catalog);
        var document = result.Document ?? throw new InvalidOperationException("form did not parse:\n" + text);
        Assert.True(document.Diagnostics.Count == 0, string.Join("\n", document.Diagnostics));
        return document;
    }

    /// <summary>
    /// A root <c>LayoutForm: TForm</c> of client <paramref name="width"/> × <paramref name="height"/> whose body is
    /// <paramref name="body"/> (object blocks indented relative to the root, i.e. starting at column 0).
    /// </summary>
    public static FormDocument Form(int width, int height, string body) =>
        Parse($"object LayoutForm: TForm\n  ClientWidth = {width}\n  ClientHeight = {height}\n" + Indent(body, 2) + "end\n");

    /// <summary>An object block: <c>object Name: Class</c> with the given bounds, extra property lines and child blocks.</summary>
    public static string Obj(string name, string className, int left, int top, int width, int height, string properties = "", string children = "") =>
        $"object {name}: {className}\n  Left = {left}\n  Top = {top}\n  Width = {width}\n  Height = {height}\n"
        + Indent(properties, 2) + Indent(children, 2) + "end\n";

    public static Rect Bounds(LayoutResult result, FormDocument form, string name) => result.GetBounds(form.FindByName(name)!);

    public static string Indent(string text, int spaces)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Concat(lines.Where(l => l.Trim().Length > 0).Select(l => new string(' ', spaces) + l.TrimEnd() + "\n"));
    }
}
