using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.CodeModel;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>The Structure tree from the syntax tree (I-124, tool-windows.md §3, IDE-ED-130).</summary>
[Trait("Phase", "3")]
public sealed class StructureTests
{
    private const string Path = @"C:\p\Main.pas";

    private const string Unit = """
        unit Main;
        interface
        uses SysUtils, Forms;
        type
          TMainForm = class(TForm)
          private
            NameEdit: TEdit;
          public
            procedure GreetButtonClick(Sender: TObject);
            property Caption2: string read FCap;
          end;
          TColor = (Red, Green);
          TPoint = record X, Y: Integer; end;
        const
          Limit = 10;
        var
          MainForm: TMainForm;
        function FormatGreeting(const AName: string): string;
        implementation
        uses Classes;
        procedure Foo; begin end;
        procedure Foo(A: Integer); begin end;
        function FormatGreeting(const AName: string): string; begin Result := AName; end;
        end.
        """;

    private static IReadOnlyList<StructureNode> Build(string text, bool sorted = false) =>
        StructureBuilder.Build(SyntaxTree.Parse(SourceText.From(Path, text)), sorted);

    private static StructureNode Node(IReadOnlyList<StructureNode> nodes, string key) =>
        nodes.Select(n => n.Key == key ? n : Find(n, key)).FirstOrDefault(n => n is not null) ?? throw new KeyNotFoundException(key);

    private static StructureNode? Find(StructureNode node, string key) =>
        node.Key == key ? node : node.Children.Select(c => Find(c, key)).FirstOrDefault(n => n is not null);

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-201")]
    [Trait("Req", "UI-TW-202")]
    [Trait("Req", "UI-TW-206")]
    [Trait("Req", "UI-TW-209")]
    [Trait("Req", "IDE-ED-130")]
    public void TreeFromSyntaxAndErrorsNode()
    {
        // Fixed group order, empty groups omitted, no Errors group for a clean unit.
        var tree = Build(Unit);
        Assert.Equal(["Uses", "Types", "Constants", "Variables", "Routines"], tree.Select(n => n.Key));
        Assert.Equal(["SysUtils", "Forms", "Classes"], Node(tree, "Uses").Children.Select(n => n.Text)); // both clauses, source order

        // Row texts and keys.
        var form = Node(tree, "Types/TMainForm");
        Assert.Equal("TMainForm = class(TForm)", form.Text);
        Assert.Equal(StructureKind.Class, form.Kind);
        Assert.Equal(["NameEdit: TEdit", "GreetButtonClick(Sender: TObject)", "Caption2: string"], form.Children.Select(n => n.Text));
        Assert.Equal(["private", null, null], form.Children.Select(n => n.Visibility));
        Assert.Equal("Types/TMainForm/GreetButtonClick", form.Children[1].Key);
        Assert.Equal(StructureKind.Enum, Node(tree, "Types/TColor").Kind);
        Assert.Equal(StructureKind.Record, Node(tree, "Types/TPoint").Kind);
        Assert.Equal("Limit = 10", Node(tree, "Constants/Limit").Text);
        Assert.Equal("MainForm: TMainForm", Node(tree, "Variables/MainForm").Text);
        var routines = Node(tree, "Routines").Children;
        Assert.Equal(["Routines/FormatGreeting", "Routines/Foo", "Routines/Foo#1", "Routines/FormatGreeting#1"], routines.Select(n => n.Key));
        Assert.Equal("FormatGreeting(const AName: string): string", routines[0].Text);
        Assert.Equal(Unit.IndexOf("FormatGreeting(const", StringComparison.Ordinal), routines[0].NameOffset);

        // Caret tracking finds the innermost declaration.
        var inMethod = Unit.IndexOf("GreetButtonClick", StringComparison.Ordinal) + 3;
        Assert.Equal("Types/TMainForm/GreetButtonClick", StructureBuilder.FindInnermost(tree, inMethod)!.Key);

        // Sorting: children by name, groups keep their order.
        var sorted = Build(Unit, sorted: true);
        Assert.Equal(["Uses", "Types", "Constants", "Variables", "Routines"], sorted.Select(n => n.Key));
        Assert.Equal(["Classes", "Forms", "SysUtils"], Node(sorted, "Uses").Children.Select(n => n.Text));
        Assert.Equal(["TColor", "TMainForm", "TPoint"], Node(sorted, "Types").Children.Select(n => n.Name));

        // A syntax error: the Errors group comes first with message and line:col.
        var broken = Build(Unit.Replace("Limit = 10;", "Limit = 10", StringComparison.Ordinal));
        Assert.Equal("Errors", broken[0].Key);
        var error = Assert.Single(broken[0].Children);
        Assert.Equal(("Errors/0", StructureKind.Error), (error.Key, error.Kind));
        Assert.Matches(@"^\d+:\d+$", error.Position);
        Assert.Equal(string.Format(Core.Resources.Strings.Structure_Group_Errors, 1), broken[0].Text);
    }
}
