using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.CodeModel;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>
/// UI-TW-1615 (D-150): the code outline of <c>Editing/Structure/Sample.pas</c> (copied to the output) printed as indented
/// lines <c>&lt;key&gt; | &lt;row text&gt; | &lt;icon id&gt;</c> (two spaces per level; the icon id is the symbol-kind icon,
/// followed by <c>+&lt;overlay&gt;</c> when the row has a visibility overlay) equals <c>Sample.outline</c>. On a mismatch the
/// actual outline is written beside the copy as <c>Sample.outline.actual</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
public sealed class StructureGoldenTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Editing", "Structure");

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1615")]
    public void SampleUnitOutline()
    {
        var source = File.ReadAllText(Path.Combine(Folder, "Sample.pas")).ReplaceLineEndings("\n");
        var expected = File.ReadAllText(Path.Combine(Folder, "Sample.outline")).ReplaceLineEndings("\n");
        var tree = SyntaxTree.Parse(SourceText.From(Path.Combine(Folder, "Sample.pas"), source));

        var actual = Print(StructureBuilder.Build(tree, sorted: false));

        if (actual != expected)
            File.WriteAllText(Path.Combine(Folder, "Sample.outline.actual"), actual);
        Assert.Equal(expected, actual);
    }

    private static string Print(IReadOnlyList<StructureNode> nodes)
    {
        var text = new StringBuilder();
        void Add(StructureNode node, int depth)
        {
            var icon = SymbolIcons.For(node.Kind);
            if (SymbolIcons.VisibilityOverlay(node.Visibility) is { } overlay)
                icon += "+" + overlay;
            text.Append(' ', 2 * depth).Append(node.Key).Append(" | ").Append(node.Text).Append(" | ").Append(icon).Append('\n');
            foreach (var child in node.Children)
                Add(child, depth + 1);
        }

        foreach (var node in nodes)
            Add(node, 0);
        return text.ToString();
    }
}
