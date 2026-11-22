using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>Builds forms from text and renders one object on a <see cref="RecordingCanvas"/>.</summary>
internal static class RenderHarness
{
    public static readonly RendererRegistry Registry = RendererRegistry.CreateDefault();

    /// <summary>Parses a form (LF, two-space indent) with the test catalog.</summary>
    public static FormDocument Parse(string text) =>
        FormParser.Parse(text, "render.form", TestCatalog.Instance).Document
        ?? throw new InvalidOperationException("form did not parse:\n" + text);

    /// <summary>
    /// A form <c>F</c> (client 400 × 300, <paramref name="formProperties"/>) with one child <c>X</c> of
    /// <paramref name="className"/> at (8, 8) of the given size and <paramref name="properties"/> (one per line).
    /// </summary>
    public static FormObject Control(string className, int width, int height, string properties = "", string formProperties = "")
    {
        var text = "object F: TForm\n  ClientWidth = 400\n  ClientHeight = 300\n" + Indent(formProperties, 2)
            + $"  object X: {className}\n    Left = 8\n    Top = 8\n    Width = {width}\n    Height = {height}\n"
            + Indent(properties, 4) + "  end\nend\n";
        return Parse(text).FindByName("X")!;
    }

    /// <summary>The context of <paramref name="obj"/> sized by its stored bounds (the form: its client size).</summary>
    public static RenderContext Context(FormObject obj)
    {
        var bounds = PropertyAccess.GetBounds(obj);
        return new RenderContext(obj, bounds.Width, bounds.Height);
    }

    public static RecordingCanvas Render(RenderContext ctx)
    {
        var canvas = new RecordingCanvas();
        Registry.Resolve(ctx.Info.RendererId).Render(ctx, canvas);
        return canvas;
    }

    private static string Indent(string lines, int spaces) =>
        string.Concat(lines.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new string(' ', spaces) + l.Trim() + "\n"));
}
