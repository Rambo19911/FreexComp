using System.Collections.Immutable;
using FreexComp.Designer.Catalog;

namespace FreexComp.Designer.Tests.Catalog;

public sealed class CatalogTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-002")]
    public void IsAssignableFollowsAncestors()
    {
        var catalog = new ComponentCatalog(
        [
            Component("TControl", "TComponent", isAbstract: true),
            Component("TWinControl", "TControl", isAbstract: true),
            Component("TButtonControl", "TWinControl", isAbstract: true),
            Component("TButton", "TButtonControl"),
            Component("TLabel", "TControl"),
        ], [], new Dictionary<string, string>(), []);

        Assert.True(catalog.IsAssignable("TButton", "TButton"));          // a class is assignable to itself
        Assert.True(catalog.IsAssignable("TButton", "TWinControl"));      // through an abstract ancestor
        Assert.True(catalog.IsAssignable("TButton", "TControl"));
        Assert.True(catalog.IsAssignable("TButton", "TComponent"));       // the built-in root of every chain
        Assert.True(catalog.IsAssignable("tbutton", "TWINCONTROL"));      // ASCII case-insensitive
        Assert.True(catalog.IsAssignable("StdCtrls.TButton", "TControl")); // unit qualifier ignored
        Assert.False(catalog.IsAssignable("TLabel", "TWinControl"));      // a sibling branch
        Assert.False(catalog.IsAssignable("TControl", "TButton"));        // never downwards
        Assert.False(catalog.IsAssignable("TUnknown", "TComponent"));     // unknown class
        Assert.False(catalog.IsAssignable("TButton", "TUnknown"));

        Assert.Same(catalog.Find("TButton"), catalog.Find("stdctrls.tbutton"));
        Assert.NotNull(catalog.Find("TComponent"));
        Assert.Contains(catalog.All, c => c.ClassName == "TComponent");
        Assert.Equal("Tag", Assert.Single(catalog.Find("TComponent")!.Properties).Name);
        Assert.Null(catalog.Find("TUnknown"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-002")]
    public void LooksUpTypesAndEventSignatures()
    {
        var align = new TypeInfo("TAlign", TypeKind.Enum, ["alNone", "alTop"], null, null, []);
        var catalog = new ComponentCatalog([], [align],
            new Dictionary<string, string> { ["TNotifyEvent"] = "procedure(Sender: TObject)" }, ["Standard"]);

        Assert.Same(align, catalog.FindType("talign"));
        Assert.Null(catalog.FindType("TAnchors"));
        Assert.Equal("procedure(Sender: TObject)", catalog.FindEventSignature("tnotifyevent"));
        Assert.Null(catalog.FindEventSignature("TKeyEvent"));
        Assert.Equal(["Standard"], catalog.PalettePages);
        Assert.Equal("TComponent", Assert.Single(ComponentCatalog.Empty.All).ClassName);
    }

    private static ComponentInfo Component(string name, string ancestor, bool isAbstract = false) =>
        new(name, ancestor, "Test", isAbstract, ComponentKind.Control, null, new Size(75, 25), name[1..], false,
            "generic", [], [], [], [], ImmutableArray<DesignerVerb>.Empty, null);
}
