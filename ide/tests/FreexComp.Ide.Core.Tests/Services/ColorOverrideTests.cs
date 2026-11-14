using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

/// <summary>User colour overrides reach the theme resources (IDE-SET-040).</summary>
[Trait("Phase", "1")]
public sealed class ColorOverrideTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-040")]
    public void OverrideReplacesThemeResource()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\users\test\roaming");
        fs.WriteAllBytes(@"C:\users\test\roaming\settings.json", """
            { "formatVersion": 1,
              "colors.dark.syntax.keyword": "#112233",
              "colors.light.syntax.keyword.style": "italic",
              "colors.dark.bg.editor": "#000000" }
            """u8.ToArray());
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.UserDir = @"C:\users\test";
        });
        var host = (FakeThemeHost)services.ThemeHost;
        var settings = services.Settings;

        // At start-up: only the overridden colours are applied; styles and chrome tokens never reach the theme.
        Assert.Equal(new Dictionary<(ThemeKind, string), string> { [(ThemeKind.Dark, "syntax.keyword")] = "#112233" }, host.Colors);

        // A change replaces the resource of that theme at once; a reset puts the theme's default back.
        settings.Set(SettingsCatalog.ColorKey("light", "editor.caret"), "#abcdef");
        Assert.Equal("#ABCDEF", host.Colors[(ThemeKind.Light, "editor.caret")]);
        settings.Reset("colors.dark.syntax.keyword");
        Assert.Equal("#6CB6F0", host.Colors[(ThemeKind.Dark, "syntax.keyword")]);
        settings.Set(SettingsCatalog.StyleKey("dark", "syntax.comment"), "bold");
        Assert.DoesNotContain((ThemeKind.Dark, "syntax.comment"), host.Colors.Keys);

        // Batched changes (Options OK/Apply, Reset All) arrive too.
        settings.ResetAll("colors.light.");
        Assert.Equal("#1C211E", host.Colors[(ThemeKind.Light, "editor.caret")]);
        Assert.False(host.Colors.ContainsKey((ThemeKind.Dark, "bg.editor")));
    }
}
