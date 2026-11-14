using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Themes;
using FreexComp.Ide.Tests.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Colors page of the Options dialog (dialogs.md §9.4, UI-DLG-095/096) and user colour overrides (IDE-SET-040).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ColorsPageTests
{
    private static void SetText(DialogDriver dlg, string field, string text)
    {
        dlg.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Resource(Visual anchor, string key) =>
        anchor.TryFindResource(key, anchor is StyledElement s ? s.ActualThemeVariant : null, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException(key);

    private static string RowText(DialogDriver dlg, string token) =>
        string.Concat(dlg.Find<Control>("Colors.Element." + token).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static FontWeight RowWeight(DialogDriver dlg, string token) =>
        dlg.Find<Control>("Colors.Element." + token).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.Length > 0).FontWeight;

    /// <summary>Scrolls an element row into view and clicks it.</summary>
    private static void ClickElement(DialogDriver dlg, string token)
    {
        dlg.Find<Control>("Colors.Element." + token).BringIntoView();
        Dispatcher.UIThread.RunJobs();
        dlg.Click("Colors.Element." + token);
    }

    /// <summary>Clicks the preview text at (line, column), 1-based.</summary>
    private static void ClickPreview(DialogDriver dlg, TextEditor preview, int line, int column)
    {
        var view = preview.TextArea.TextView;
        var local = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(line, column), VisualYPosition.LineMiddle) - view.ScrollOffset;
        var point = view.TranslatePoint(local + new Point(2, 0), dlg.Window)!.Value;
        dlg.Window.MouseDown(point, MouseButton.Left);
        dlg.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-095")]
    [Trait("Req", "UI-DLG-096")]
    [Trait("Req", "IDE-SET-040")]
    [Trait("Req", "IDE-SET-041")]
    public async Task OverrideAndReset()
    {
        const string source = "begin end.\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            o.InitialFiles["A.pas"] = source;
        });
        var settings = h.Services.Settings;
        var editor = await SyntaxColorizerTests.OpenAsync(h, "A.pas");
        Assert.Equal(Color.Parse("#0B5C8A"), SyntaxColorizerTests.ColorAt(editor, 0));

        var running = h.Services.Commands.ExecuteAsync("tools.options", "Colors");
        var dlg = await h.WaitForDialogAsync("Options");
        Assert.Equal("Colors", dlg.Find<TextBlock>("PageTitle").Text);

        // Theme: the one in effect. Elements: every syntax.* then every editor.* token, labelled (dialogs.md §9.4).
        var theme = dlg.Find<ComboBox>("Theme");
        Assert.Equal("Light", theme.SelectionBoxItem?.ToString());
        var rows = dlg.FindAll("Dialog.Options.Colors.Element.").Select(c => AutomationIds.Get(c)!["Dialog.Options.Colors.Element.".Length..]).ToList();
        Assert.Equal(SettingsCatalog.EditorColorTokens.Select(t => t.Token), rows);
        Assert.Equal("Keyword", RowText(dlg, "syntax.keyword"));
        Assert.Equal("Compiler directive", RowText(dlg, "syntax.directive"));
        Assert.Equal("Selection", RowText(dlg, "editor.selection.bg"));

        // An element shows its colour, style and contrast; nothing is overridden yet.
        ClickElement(dlg, "syntax.keyword");
        var color = dlg.Find<TextBox>("Color");
        Assert.Equal("#0B5C8A", color.Text);
        Assert.True(dlg.Find<CheckBox>("Bold").IsChecked);
        Assert.False(dlg.Find<CheckBox>("Italic").IsChecked);
        Assert.True(dlg.Find<CheckBox>("Bold").IsEffectivelyEnabled);
        Assert.Equal(ColorContrast.Ratio("#0B5C8A", "#FFFFFF").ToString("0.0", CultureInfo.CurrentCulture) + " : 1", dlg.Find<TextBlock>("Contrast").Text);
        Assert.False(dlg.Find<Button>("ResetElement").IsEnabled);
        Assert.Equal(FontWeight.Normal, RowWeight(dlg, "syntax.keyword"));

        // Invalid hex: inline message, OK disabled.
        SetText(dlg, "Color", "#12");
        Assert.Equal("Enter a color as # followed by six hexadecimal digits, such as #0F7B6C.", dlg.ErrorText("Color"));
        Assert.False(dlg.Find<Button>("Ok").IsEnabled);

        // A valid colour and a new style are pending: the preview shows them, settings and the open editor do not.
        SetText(dlg, "Color", "#123456");
        Assert.Null(dlg.ErrorText("Color"));
        Assert.True(dlg.Find<Button>("Ok").IsEnabled);
        dlg.Click("Bold");
        dlg.Click("Italic");
        var preview = (TextEditor)dlg.Find<Control>("Preview");
        Assert.Equal(10, preview.Document.LineCount);
        Assert.Equal(Color.Parse("#123456"), SyntaxColorizerTests.ColorAt(preview, 0)); // "unit"
        Assert.Equal(FontStyle.Italic, SyntaxColorizerTests.RunAt(preview, 0).Typeface.Style);
        Assert.Equal(FontWeight.Normal, SyntaxColorizerTests.RunAt(preview, 0).Typeface.Weight);
        Assert.True(dlg.Find<Button>("ResetElement").IsEnabled);
        Assert.Equal(FontWeight.SemiBold, RowWeight(dlg, "syntax.keyword")); // overridden rows in type.bodyStrong
        Assert.True(settings.IsDefault("colors.light.syntax.keyword"));
        Assert.Equal(Color.Parse("#0B5C8A"), SyntaxColorizerTests.ColorAt(editor, 0));

        // Editor elements: no style; contrast only for foreground tokens.
        ClickElement(dlg, "editor.selection.bg");
        Assert.Equal("#BFE3DC", color.Text);
        Assert.False(dlg.Find<CheckBox>("Bold").IsEffectivelyEnabled);
        Assert.False(dlg.Find<TextBlock>("Contrast").IsEffectivelyVisible);
        ClickElement(dlg, "editor.caret");
        Assert.True(dlg.Find<TextBlock>("Contrast").IsEffectivelyVisible);

        // The Dark set is edited separately; the IDE theme does not change; the preview uses the selected theme.
        theme.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        ClickElement(dlg, "syntax.keyword");
        Assert.Equal("#6CB6F0", color.Text);
        Assert.Equal(Color.Parse("#6CB6F0"), SyntaxColorizerTests.ColorAt(preview, 0));
        Assert.Equal(ThemeKind.Light, h.Services.Theme.ActualTheme);
        theme.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("#123456", color.Text);

        // Colour popup (UI-DLG-095): 60 swatches plus Default; a click picks; keys move, Enter picks, Escape closes.
        dlg.Click("Color.Swatch");
        Assert.Equal(60, dlg.Window.GetVisualDescendants().Concat(PopupDescendants(dlg)).OfType<Control>().Distinct()
            .Count(c => AutomationIds.Get(c)?.StartsWith("Dialog.Options.Colors.Palette.Swatch.", StringComparison.Ordinal) == true));
        ClickPopup(dlg, "Dialog.Options.Colors.Palette.Swatch.000000");
        Assert.Equal("#000000", color.Text);
        Assert.False(IsPaletteOpen(dlg));
        dlg.Click("Color.Swatch");
        Assert.True(IsPaletteOpen(dlg));
        PressInPopup(dlg, Key.Right);
        PressInPopup(dlg, Key.Enter);
        Assert.Equal("#1C1C1C", color.Text);
        Assert.False(IsPaletteOpen(dlg));
        Assert.True(dlg.Window.IsVisible);
        dlg.Click("Color.Swatch");
        PressInPopup(dlg, Key.Escape);
        Assert.False(IsPaletteOpen(dlg));
        Assert.True(dlg.Window.IsVisible); // Escape closed only the popup
        dlg.Click("Color.Swatch");
        ClickPopup(dlg, "Dialog.Options.Colors.Palette.Default");
        Assert.Equal("#0B5C8A", color.Text);
        SetText(dlg, "Color", "#123456");

        // Clicking a token in the preview selects its element.
        ClickPreview(dlg, preview, 1, 16); // "// line comment"
        Assert.Equal("syntax.comment", ((Core.Dialogs.OptionsViewModel)dlg.Window.Model).Colors.SelectedElement?.Token);
        Assert.Equal("#5E6E63", color.Text);

        // Apply: settings, the theme resource and the open editor (colour and style override) all change.
        dlg.Click("Apply");
        Assert.Equal("#123456", settings.Get(SettingsCatalog.ColorKey("light", "syntax.keyword")));
        Assert.Equal("italic", settings.Get(SettingsCatalog.StyleKey("light", "syntax.keyword")));
        Assert.Equal(Color.Parse("#123456"), Resource(h.Window, "FxcSyntaxKeywordBrush"));
        Assert.Equal(Color.Parse("#123456"), SyntaxColorizerTests.ColorAt(editor, 0));
        Assert.Equal(FontStyle.Italic, SyntaxColorizerTests.RunAt(editor, 0).Typeface.Style);
        Assert.Equal(FontWeight.Normal, SyntaxColorizerTests.RunAt(editor, 0).Typeface.Weight);

        // Reset Element removes the element's override keys (pending until Apply).
        ClickElement(dlg, "syntax.keyword");
        dlg.Click("ResetElement");
        Assert.Equal("#0B5C8A", color.Text);
        Assert.True(dlg.Find<CheckBox>("Bold").IsChecked);
        Assert.False(dlg.Find<CheckBox>("Italic").IsChecked);
        Assert.False(dlg.Find<Button>("ResetElement").IsEnabled);
        Assert.Equal("#123456", settings.Get(SettingsCatalog.ColorKey("light", "syntax.keyword")));
        dlg.Click("Apply");
        Assert.True(settings.IsDefault("colors.light.syntax.keyword"));
        Assert.True(settings.IsDefault("colors.light.syntax.keyword.style"));
        Assert.Equal(Color.Parse("#0B5C8A"), Resource(h.Window, "FxcSyntaxKeywordBrush"));
        Assert.Equal(Color.Parse("#0B5C8A"), SyntaxColorizerTests.ColorAt(editor, 0));
        Assert.Equal(FontWeight.SemiBold, SyntaxColorizerTests.RunAt(editor, 0).Typeface.Weight);

        // Reset All asks first (Cancel keeps everything), then resets the selected theme's set, pending until OK.
        ClickElement(dlg, "editor.caret");
        SetText(dlg, "Color", "#FF0000");
        dlg.Click("Apply");
        Assert.Equal("#FF0000", settings.Get(SettingsCatalog.ColorKey("light", "editor.caret")));
        h.Messages.Answer("Msg.Options.ResetColorsConfirm", "Cancel");
        dlg.Click("ResetAll");
        Assert.Equal("#FF0000", color.Text);
        Assert.Equal(["Light"], h.Messages.Requests.Last(r => r.MessageId == "Msg.Options.ResetColorsConfirm").Args);
        h.Messages.Answer("Msg.Options.ResetColorsConfirm", "Reset");
        dlg.Click("ResetAll");
        Assert.Equal("#1C211E", color.Text);
        Assert.Equal("#FF0000", settings.Get(SettingsCatalog.ColorKey("light", "editor.caret")));
        dlg.Click("Ok");
        await running;
        Assert.True(SettingsCatalog.All.Where(k => k.Name.StartsWith("colors.light.", StringComparison.Ordinal)).All(k => settings.IsDefault(k.Name)));
        Assert.Equal(Color.Parse("#1C211E"), Resource(h.Window, "FxcEditorCaretBrush"));
    }

    private static IEnumerable<Visual> PopupDescendants(DialogDriver dlg) =>
        dlg.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>()
            .Where(p => p.IsOpen && p.Child is not null)
            .SelectMany(p => new Visual[] { p.Child! }.Concat(p.Child!.GetVisualDescendants()));

    private static bool IsPaletteOpen(DialogDriver dlg) =>
        PopupDescendants(dlg).OfType<Control>().Any(c => AutomationIds.Get(c) == "Dialog.Options.Colors.Palette");

    private static void ClickPopup(DialogDriver dlg, string automationId)
    {
        var target = PopupDescendants(dlg).OfType<Button>().First(c => AutomationIds.Get(c) == automationId);
        target.Focus(NavigationMethod.Tab);
        target.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressInPopup(DialogDriver dlg, Key key)
    {
        var palette = PopupDescendants(dlg).OfType<Control>().First(c => AutomationIds.Get(c) == "Dialog.Options.Colors.Palette");
        palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = palette });
        Dispatcher.UIThread.RunJobs();
    }
}
