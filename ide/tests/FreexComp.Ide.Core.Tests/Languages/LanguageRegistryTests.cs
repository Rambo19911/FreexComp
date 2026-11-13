using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Languages;

[Trait("Phase", "1")]
public sealed class LanguageRegistryTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-055")]
    [Trait("Req", "UI-WIN-040")]
    public void ExtensionsMapToLanguages()
    {
        var registry = TestComposition.Build().Languages;

        Assert.Equal("form", registry.ForPath(@"C:\p\Main.form").Id);
        Assert.Equal("form", registry.ForPath(@"C:\p\MAIN.FORM").Id);   // extensions are case-insensitive
        Assert.Equal("json", registry.ForPath("App.pproj").Id);
        Assert.Equal("json", registry.ForPath("All.pgroup").Id);
        Assert.Equal("json", registry.ForPath("uistate.json").Id);
        Assert.Equal("plaintext", registry.ForPath("readme.txt").Id);
        Assert.Equal("plaintext", registry.ForPath("noextension").Id);
        Assert.Same(registry.PlainText, registry.ForPath("x.unknown"));
        Assert.Same(registry.ForPath("a.json"), registry.GetById("json"));
        Assert.Null(registry.GetById("nope"));

        // R8 of the status bar shows the display name (main-window.md §5).
        Assert.Equal("Form", registry.GetById("form")!.DisplayName);
        Assert.Equal("JSON", registry.GetById("json")!.DisplayName);
        Assert.Equal("Plain Text", registry.PlainText.DisplayName);

        // Comment and bracket data (IDE-ED-034: Form, JSON and Plain Text have no line comment).
        Assert.All(registry.Languages.Where(l => l.Id != "pascal"), l => Assert.Null(l.LineComment));
        Assert.Equal("//", registry.GetById("pascal")!.LineComment);
        Assert.Equal("pascal", registry.ForPath(@"C:\p\Unit1.PAS").Id);   // I-031
        Assert.Equal([('{', '}'), ('[', ']')], registry.GetById("json")!.Brackets);
    }
}
