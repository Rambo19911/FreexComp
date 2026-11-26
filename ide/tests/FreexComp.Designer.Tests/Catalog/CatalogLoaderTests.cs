using System.Collections.Immutable;
using System.Text;
using FreexComp.Designer.Catalog;

namespace FreexComp.Designer.Tests.Catalog;

public sealed class CatalogLoaderTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-002")]
    [Trait("Req", "DSG-TP-006")]
    public void LoadsIndexAndFlattensInheritance()
    {
        var result = CatalogLoader.Load([TestPaths.MetadataIndex]);
        Assert.Empty(result.Problems);
        var catalog = result.Catalog;

        Assert.Equal(["Standard", "Additional", "Win32", "System", "Dialogs"], catalog.PalettePages);
        // Load order: the built-in root, then the files in index order, components in file order.
        Assert.Equal(["TComponent", "TControl", "TWinControl", "TGraphicControl", "TCustomControl", "TMenu", "TMainMenu"],
            catalog.All.Take(7).Select(c => c.ClassName));

        // Properties: ancestor-first; the built-in TComponent contributes Tag.
        var edit = catalog.Find("TEdit")!;
        Assert.Equal("StdCtrls", edit.Unit);
        Assert.Equal(
            ["Tag", "Left", "Top", "Width", "Height", "Align", "Anchors", "AlignWithMargins", "Margins", "Constraints", "Cursor",
             "Enabled", "Hint", "ParentShowHint", "ShowHint", "Visible", "PopupMenu", "AccessibleName", "AccessibleDescription",
             "AccessibleRole"],
            edit.Properties.Take(20).Select(p => p.Name));
        Assert.Equal("TComponent", edit.Properties[0].DeclaringClass);
        Assert.Equal("TControl", edit.FindProperty("Anchors")!.DeclaringClass);
        Assert.Equal(PropertyKind.Set, edit.FindProperty("Anchors")!.Kind);
        Assert.Equal(["akLeft", "akTop"], (ImmutableArray<string>)edit.FindProperty("Anchors")!.Default!);
        Assert.Equal(PropertyKind.Enum, edit.FindProperty("charcase")!.Kind);
        Assert.Equal(PropertyKind.Object, edit.FindProperty("Font")!.Kind);
        Assert.Equal(PropertyKind.Component, edit.FindProperty("PopupMenu")!.Kind);
        // TControl's accessibility properties (U-155), category Misc; the role is an enum with its default.
        Assert.Equal("TControl", edit.FindProperty("AccessibleRole")!.DeclaringClass);
        Assert.Equal(PropertyKind.Enum, edit.FindProperty("AccessibleRole")!.Kind);
        Assert.Equal("arDefault", edit.FindProperty("AccessibleRole")!.Default);
        Assert.All(["AccessibleName", "AccessibleDescription", "AccessibleRole"], n => Assert.Equal("Misc", edit.FindProperty(n)!.Category));
        Assert.False(edit.FindProperty("Left")!.HasDefault);
        Assert.Equal(-1L, edit.FindProperty("TabOrder")!.Default);
        Assert.Equal("", edit.FindProperty("PasswordChar")!.Default);
        Assert.Equal("clWindow", edit.FindProperty("Color")!.Default);
        Assert.Equal(["OnChange", "OnClick", "OnKeyPress"], edit.Events.Select(e => e.Name));
        Assert.True(edit.FindEvent("OnChange")!.IsDefault);
        Assert.Equal("procedure(Sender: TObject; var Key: Char)", catalog.FindEventSignature(edit.FindEvent("OnKeyPress")!.Signature));

        // A redeclaration replaces the inherited entry in place, merging keys.
        var form = catalog.Find("TForm")!;
        var width = form.FindProperty("Width")!;
        Assert.Equal(3, form.Properties.IndexOf(width));
        Assert.False(width.Stored);
        Assert.Equal("Layout", width.Category);         // inherited key
        Assert.True(width.AffectsLayout);               // inherited key
        Assert.Equal("TControl", width.DeclaringClass); // the position (first declaration) owner
        var align = form.FindProperty("Align")!;
        Assert.True(align.Hidden);
        Assert.Equal("alNone", align.Default);
        Assert.Equal(["Left", "Top", "Caption", "ClientWidth", "ClientHeight", "PixelsPerInch"],
            form.Properties.Select(p => p.Name).Where(n => n is "Left" or "Top" or "Caption" or "ClientWidth" or "ClientHeight" or "PixelsPerInch"));
        Assert.Equal(ComponentKind.Form, form.Kind);
        Assert.Equal(new Size(400, 300), form.DefaultSize);
        Assert.Null(form.Palette);

        // Inherited scalars (kind, renderer, acceptsChildren, defaultSize) and not inherited ones (palette, namePrefix, abstract).
        var save = catalog.Find("TSaveDialog")!;
        Assert.Equal(ComponentKind.NonVisual, save.Kind);
        Assert.Equal("nonvisual", save.RendererId);
        Assert.False(save.IsAbstract);
        Assert.Equal("SaveDialog", save.NamePrefix);
        Assert.Equal(new PaletteInfo("Dialogs", 20, null), save.Palette);
        Assert.Equal(catalog.Find("TOpenDialog")!.Properties.Select(p => (p.Name, p.DeclaringClass)), save.Properties.Select(p => (p.Name, p.DeclaringClass)));
        Assert.True(catalog.Find("TCommonDialog")!.IsAbstract);
        var memo = catalog.Find("TMemo")!;
        Assert.Equal(ComponentKind.WinControl, memo.Kind); // from TWinControl through two abstract bases
        Assert.True(catalog.IsAssignable("TMemo", "TCustomEdit"));

        // Object classes are flattened too.
        var padding = catalog.FindType("TPadding")!;
        Assert.Equal("TMargins", padding.Ancestor);
        Assert.Equal(["Left", "Top", "Right", "Bottom"], padding.Properties.Select(p => p.Name));
        Assert.All(padding.Properties, p => Assert.Equal(0L, p.Default));
        Assert.All(padding.Properties, p => Assert.Equal(32767, p.Max)); // inherited key
        Assert.Equal(TypeKind.Set, catalog.FindType("TFontStyles")!.Kind);
        Assert.Equal("TFontStyle", catalog.FindType("TFontStyles")!.ElementType);
        Assert.Equal(["fsBold", "fsItalic", "fsUnderline", "fsStrikeOut"], catalog.FindType("TFontStyles")!.Values);
        Assert.False(catalog.FindType("TFont")!.Properties.Single(p => p.Name == "Size").Stored);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-007")]
    [Trait("Req", "DSG-TP-006")]
    public void AppliesRootDefaults()
    {
        var files = new MemoryFiles
        {
            [@"C:\meta\index.json"] = """{ "formatVersion": 1, "files": ["Things.components.json"] }""",
            [@"C:\meta\Things.components.json"] = """
                {
                  "formatVersion": 1,
                  "unit": "Things",
                  "components": [
                    { "class": "TThing", "ancestor": "TComponent" },
                    { "class": "Widget" },
                    { "class": "TChild", "ancestor": "TThing", "kind": "wincontrol", "palette": { "page": "Extra", "order": 5, "icon": "icons/TChild.svg" } },
                    { "class": "TGrandChild", "ancestor": "TChild", "renderer": "panel", "acceptsChildren": true, "defaultSize": { "width": 10, "height": 20 } },
                    { "class": "TGreatGrandChild", "ancestor": "TGrandChild", "abstract": true }
                  ]
                }
                """,
        };

        var result = CatalogLoader.Load([@"C:\meta\index.json"], files.Read);
        Assert.Empty(result.Problems);
        var catalog = result.Catalog;

        var thing = catalog.Find("TThing")!;
        Assert.Equal(ComponentKind.Control, thing.Kind);
        Assert.False(thing.AcceptsChildren);
        Assert.Equal("generic", thing.RendererId);
        Assert.Equal(new Size(75, 25), thing.DefaultSize);
        Assert.Equal("Thing", thing.NamePrefix);
        Assert.False(thing.IsAbstract);
        Assert.Null(thing.Palette);
        Assert.Equal("TComponent", thing.Ancestor);
        Assert.Equal("Things", thing.Unit);
        Assert.Equal("Tag", Assert.Single(thing.Properties).Name);
        Assert.Empty(thing.Events);

        Assert.Equal("Widget", catalog.Find("Widget")!.NamePrefix); // no leading T: the class name itself
        Assert.Equal("TComponent", catalog.Find("Widget")!.Ancestor); // ancestor omitted = the built-in root

        var child = catalog.Find("TChild")!;
        Assert.Equal(ComponentKind.WinControl, child.Kind);
        Assert.Equal("generic", child.RendererId);
        Assert.Equal(new Size(75, 25), child.DefaultSize);
        Assert.Equal(new PaletteInfo("Extra", 5, @"C:\meta\icons\TChild.svg"), child.Palette);

        var greatGrandChild = catalog.Find("TGreatGrandChild")!;
        Assert.Equal(ComponentKind.WinControl, greatGrandChild.Kind);
        Assert.Equal("panel", greatGrandChild.RendererId);
        Assert.True(greatGrandChild.AcceptsChildren);
        Assert.Equal(new Size(10, 20), greatGrandChild.DefaultSize);
        Assert.Null(greatGrandChild.Palette);
        Assert.True(greatGrandChild.IsAbstract);
        Assert.False(catalog.Find("TGrandChild")!.IsAbstract);
    }

    /// <summary>
    /// DSG-TP-005: each file of <c>metadata/broken</c> breaks one validation rule. It is loaded after the test metadata;
    /// the offending item is skipped and reported (golden <c>*.diag.txt</c>), everything else is used.
    /// </summary>
    [Theory]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-005")]
    [InlineData("format-version.json", new string[0], new[] { "TFutureThing" })]
    [InlineData("no-class.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("duplicate-class.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("unknown-ancestor.json", new[] { "TSurvivor" }, new[] { "TOrphan", "TOrphanChild" })]
    [InlineData("declares-tcomponent.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("member-without-name.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("unknown-property-type.json", new[] { "TSurvivor", "TSurvivorChild" }, new string[0])]
    [InlineData("set-of-undeclared.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("palette-without-page.json", new[] { "TSurvivor" }, new string[0])]
    [InlineData("invalid-kind.json", new[] { "TSurvivor" }, new[] { "TStrange" })]
    [InlineData("json-syntax.json", new string[0], new[] { "TSurvivor" })]
    public void SkipsInvalidItemsAndReports(string file, string[] survivors, string[] skipped)
    {
        var index = Path.Combine(TestPaths.Metadata, "broken-index.json");
        var files = File.ReadAllLines(TestPaths.MetadataIndex)
            .Single(l => l.Contains("\"files\"", StringComparison.Ordinal))
            .Replace("]", $", \"broken/{file}\"]", StringComparison.Ordinal);
        var indexText = "{ \"formatVersion\": 1, " + files.Trim() + " }";
        var result = CatalogLoader.Load([index], path => path == index ? Encoding.UTF8.GetBytes(indexText) : File.Exists(path) ? File.ReadAllBytes(path) : null);

        var actual = string.Concat(result.Problems.Select(p => $"{Path.GetFileName(p.File)}({p.Line},{p.Column}): {p.Message}\n"));
        var golden = Path.Combine(TestPaths.Metadata, "broken", Path.ChangeExtension(file, ".diag.txt"));
        Golden.Check(golden, actual);
        Assert.NotEmpty(result.Problems);
        Assert.All(result.Problems, p => Assert.Equal(file, Path.GetFileName(p.File))); // the test metadata itself is clean

        var catalog = result.Catalog;
        Assert.All(survivors, c => Assert.NotNull(catalog.Find(c)));
        Assert.All(skipped, c => Assert.Null(catalog.Find(c)));
        Assert.Equal(ComponentKind.WinControl, catalog.Find("TButton")!.Kind); // the first declaration wins
        Assert.Equal("Tag", catalog.Find("TComponent")!.Properties.Single().Name);
        if (catalog.Find("TSurvivor") is { } survivor && file == "unknown-property-type.json")
        {
            var mystery = survivor.FindProperty("Mystery")!; // kept, as a read-only string
            Assert.Equal(("string", true), (mystery.Type, mystery.ReadOnly));
            Assert.Equal(("string", true), (catalog.Find("TSurvivorChild")!.FindProperty("Mystery")!.Type, catalog.Find("TSurvivorChild")!.FindProperty("Mystery")!.ReadOnly));
        }

        if (file == "member-without-name.json")
        {
            Assert.Equal("Kept", catalog.Find("TSurvivor")!.Properties[^1].Name);
            Assert.Equal("OnKept", Assert.Single(catalog.Find("TSurvivor")!.Events).Name);
        }

        if (file == "palette-without-page.json")
        {
            Assert.Null(catalog.Find("TSurvivor")!.Palette);
        }
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-001")]
    public void ResolvesDesignFolderInOrder()
    {
        const string Exe = @"C:\repo\ide\src\FreexComp.Ide\bin\Debug\net10.0";
        const string Home = @"D:\fxc";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\fxc\ui-framework\design\index.json",
            Exe + @"\ui-framework\design\index.json",
            @"C:\repo\ui-framework\design\index.json",
            @"C:\repo\ide\ui-framework\design\index.json",
        };
        string? Resolve(string? setting, string? home) =>
            DesignFolderResolver.Resolve(setting, home, Exe, DesignFolderResolver.FrameworkFolder, existing.Contains);

        // (1) a non-empty setting wins, even when it has no index (MetadataMissing then names it).
        Assert.Equal(@"E:\custom", Resolve(@"E:\custom", Home));
        // (2) FREEXCOMP_HOME.
        Assert.Equal(@"D:\fxc\ui-framework\design", Resolve("", Home));
        Assert.Equal(@"D:\fxc\ui-framework\design", Resolve("  ", Home));
        // (3) beside the executable.
        Assert.Equal(Exe + @"\ui-framework\design", Resolve(null, null));
        existing.Remove(@"D:\fxc\ui-framework\design\index.json");
        Assert.Equal(Exe + @"\ui-framework\design", Resolve(null, Home)); // a home without the folder is skipped
        // (4) the nearest ancestor of the exe directory with the folder.
        existing.Remove(Exe + @"\ui-framework\design\index.json");
        Assert.Equal(@"C:\repo\ide\ui-framework\design", Resolve(null, null));
        existing.Remove(@"C:\repo\ide\ui-framework\design\index.json");
        Assert.Equal(@"C:\repo\ui-framework\design", Resolve(null, null));
        // Nothing found.
        existing.Clear();
        Assert.Null(Resolve(null, Home));

        // The data folder is resolved the same way (toolchain.dataDesignDir).
        existing.Add(@"C:\repo\data\design\index.json");
        Assert.Equal(@"C:\repo\data\design",
            DesignFolderResolver.Resolve(null, null, Exe, DesignFolderResolver.DataFolder, existing.Contains));
        Assert.Null(Resolve(null, null));
    }

    /// <summary>In-memory files for loader tests (paths compared ignoring case).</summary>
    internal sealed class MemoryFiles : Dictionary<string, string>
    {
        public MemoryFiles()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public byte[]? Read(string path) => TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")) : null;
    }
}
