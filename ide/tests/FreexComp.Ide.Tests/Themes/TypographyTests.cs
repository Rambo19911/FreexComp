using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Themes;

/// <summary>design-tokens.md §4: font families and sizes.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class TypographyTests
{
    private static object Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var value), key);
        return value!;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-020")]
    public async Task UiFontTokenIsTheShellFont()
    {
        var ui = (FontFamily)Resource("FxcFontUi");
        Assert.Equal(["Segoe UI Variable Text", "Segoe UI", "Inter", "sans-serif"], ui.FamilyNames);
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings.Clear()); // Welcome page, menus, status bar
        Assert.Equal(ui, h.Window.FontFamily);
        Assert.True(Application.Current!.TryGetResource("FxcFontMono", null, out var mono));
        var others = h.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && !Equals(t.FontFamily, ui) && !Equals(t.FontFamily, mono))
            .Select(t => $"{t.Text}: {t.FontFamily}").ToList();
        Assert.Empty(others);
        // No font file is bundled: the families are system font names, not resource URIs.
        Assert.DoesNotContain(ui.FamilyNames, n => n.Contains('#', StringComparison.Ordinal));
        var src = Path.Combine(Core.Tests.Infrastructure.SpecTables.RepoRootPath(), "ide", "src");
        Assert.DoesNotContain(Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories),
            f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf" or ".woff" or ".woff2");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-022")]
    public void EditorFontSizeDefaultAndRange()
    {
        var key = SettingsCatalog.EditorFontSize;
        Assert.Equal(13, key.Default);
        Assert.False(key.IsValid(5));
        Assert.True(key.IsValid(6));
        Assert.True(key.IsValid(72));
        Assert.False(key.IsValid(73));
        Assert.Equal(13d, (double)Resource("FxcTypeCodeSize"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-044")]
    public async Task NoTextBelowElevenDip()
    {
        foreach (var token in new[] { "Caption", "Body", "Subtitle", "Title", "Code", "CodeSmall" })
            Assert.True((double)Resource($"FxcType{token}Size") >= 11, token);

        await using var h = await HeadlessHarness.StartAsync(o => o.Settings.Clear()); // Welcome page, tool windows, status bar
        var small = h.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.FontSize < 11)
            .Select(t => $"{t.Text} ({t.FontSize})").ToList();
        Assert.Empty(small);
    }
}
