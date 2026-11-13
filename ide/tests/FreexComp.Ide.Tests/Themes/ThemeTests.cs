using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Themes;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ThemeTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-021")]
    [Trait("Req", "UI-TOK-003")]
    [Trait("Req", "UI-TOK-004")]
    public async Task SwitchThemeChangesResources()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = "light");
        Assert.Equal(Color.Parse("#FFFFFF"), EditorBackground(h));

        await h.ExecuteAsync("view.theme.dark");

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal(ThemeKind.Dark, h.Services.Theme.ActualTheme);
        Assert.Equal(Color.Parse("#171A19"), EditorBackground(h)); // no restart

        await h.ExecuteAsync("view.theme.system");
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-003")]
    [Trait("Req", "UI-TOK-003")]
    public async Task SwitchThemeUpdatesOpenEditors()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = "begin end.\r\n";
        });
        var editor = await Editor.SyntaxColorizerTests.OpenAsync(h, "A.pas");
        Assert.Equal(Color.Parse("#FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(editor.Background).Color);
        Assert.Equal(Color.Parse("#0B5C8A"), Editor.SyntaxColorizerTests.ColorAt(editor, 0));

        await h.ExecuteAsync("view.theme.dark");

        // The open editor repaints without reopening the document.
        Assert.Equal(Color.Parse("#171A19"), Assert.IsAssignableFrom<ISolidColorBrush>(editor.Background).Color);
        Assert.Equal(Color.Parse("#6CB6F0"), Editor.SyntaxColorizerTests.ColorAt(editor, 0));
        Assert.Same(editor, h.Find<AvaloniaEdit.TextEditor>("Editor.A.pas"));
    }

    private static Color EditorBackground(HeadlessHarness h)
    {
        Assert.True(h.Window.TryFindResource("FxcBgEditorBrush", h.Window.ActualThemeVariant, out var value));
        return Assert.IsType<SolidColorBrush>(value).Color;
    }
}
