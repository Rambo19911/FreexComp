using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Tests.Themes;

/// <summary>I-212: the symbol-kind icons of completion (UI-ED-114) and Structure (UI-TW-203).</summary>
[Trait("Kind", "Ui")]
public sealed class SymbolIconTests
{
    /// <summary>Every symbol icon is original 16 × 16 outline geometry with a brush for each of its tokens, in both themes.</summary>
    private static void AssertDrawable(string name)
    {
        Assert.True(IconCatalog.TryGet(name, out var icon), $"no icon '{name}'");
        Assert.NotEmpty(icon.Layers);
        foreach (var layer in icon.Layers)
        {
            var bounds = layer.Geometry.Bounds;
            Assert.True(bounds.Width > 0 || bounds.Height > 0, $"{name}: empty geometry");
            Assert.True(bounds.X >= -0.01 && bounds.Y >= -0.01 && bounds.Right <= 16.01 && bounds.Bottom <= 16.01, $"{name}: {bounds} outside the 16 x 16 grid");
            foreach (var theme in (Avalonia.Styling.ThemeVariant[])[Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark])
                Assert.True(Application.Current!.TryFindResource(IconCatalog.BrushKey(layer.ColorToken), theme, out _), $"{name}: no brush for {layer.ColorToken}");
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-ED-114")]
    public void CompletionKindsHaveTheirOutlineIcons()
    {
        // One icon per kind of the brief; kinds of the same brief entry share it.
        var expected = new Dictionary<CompletionItemKind, (string Icon, string Token)>
        {
            [CompletionItemKind.Keyword] = ("symbol-keyword", "text.primary"),
            [CompletionItemKind.Directive] = ("symbol-keyword", "text.primary"),
            [CompletionItemKind.Snippet] = ("symbol-snippet", "text.primary"),
            [CompletionItemKind.Unit] = ("symbol-unit", "text.primary"),
            [CompletionItemKind.Class] = ("symbol-class", "accent.default"),
            [CompletionItemKind.Interface] = ("symbol-interface", "text.primary"),
            [CompletionItemKind.Enum] = ("symbol-enum", "text.primary"),
            [CompletionItemKind.EnumMember] = ("symbol-constant", "syntax.number"),
            [CompletionItemKind.Constant] = ("symbol-constant", "syntax.number"),
            [CompletionItemKind.Type] = ("symbol-type", "text.primary"),
            [CompletionItemKind.Routine] = ("symbol-routine", "status.info"),
            [CompletionItemKind.Method] = ("symbol-routine", "status.info"),
            [CompletionItemKind.Property] = ("symbol-property", "text.primary"),
            [CompletionItemKind.Field] = ("symbol-field", "text.primary"),
            [CompletionItemKind.Variable] = ("symbol-field", "text.primary"),
            [CompletionItemKind.Parameter] = ("symbol-field", "text.primary"),
            [CompletionItemKind.Label] = ("symbol-field", "text.primary"),
        };
        Assert.Equal(Enum.GetValues<CompletionItemKind>().Order(), expected.Keys.Order());
        foreach (var (kind, (name, token)) in expected)
        {
            Assert.Equal(name, SymbolIcons.For(kind));
            AssertDrawable(name);
            Assert.Equal(token, IconCatalog.Get(name).ColorToken);
        }

        // Shapes of the brief: the class diamond is filled, the record diamond (Structure) is its outline; fields are a small filled square.
        Assert.All(IconCatalog.Get("symbol-class").Layers, l => Assert.True(l.IsFilled));
        Assert.All(IconCatalog.Get("symbol-record").Layers, l => Assert.False(l.IsFilled));
        var field = Assert.Single(IconCatalog.Get("symbol-field").Layers);
        Assert.True(field.IsFilled);
        Assert.True(field.Geometry.Bounds.Width <= 8);
        Assert.All(IconCatalog.Get("symbol-routine").Layers, l => Assert.False(l.IsFilled));

        // The symbol icons are their own set: the toolbar catalogue (toolbars.md §4) stays as listed.
        Assert.DoesNotContain(IconCatalog.Names, n => n.StartsWith("symbol-", StringComparison.Ordinal));
        Assert.Equal(13, IconCatalog.SymbolNames.Count);
        foreach (var name in IconCatalog.SymbolNames)
            AssertDrawable(name);

        // The completion row draws the icon as vector geometry (16 × 16), not a letter.
        var cell = new SymbolKindIcon { Kind = CompletionItemKind.Method };
        var window = new Window { Content = cell, Width = 100, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var drawn = Assert.IsType<IdeIcon>(cell.Child);
        Assert.Equal("symbol-routine", drawn.IconName);
        Assert.Equal("symbol-routine", cell.IconName);
        Assert.Equal(new Size(16, 16), drawn.Bounds.Size);
        cell.Kind = CompletionItemKind.Class;
        Assert.Equal("symbol-class", drawn.IconName);
        window.Close();
    }

    private const string Unit = "unit Main;\r\ninterface\r\nuses SysUtils;\r\ntype\r\n  TKind = (kA, kB);\r\n  TPoint = record\r\n    X: Integer;\r\n  end;\r\n" +
        "  IThing = interface\r\n  end;\r\n  TAlias = Integer;\r\n  TMainForm = class\r\n  private\r\n    FCount: Integer;\r\n  protected\r\n    procedure Greet;\r\n" +
        "  public\r\n    property Count: Integer read FCount;\r\n  end;\r\nconst\r\n  Max = 10;\r\nvar\r\n  MainForm: TMainForm;\r\nprocedure Run;\r\n" +
        "implementation\r\nprocedure TMainForm.Greet; begin end;\r\nprocedure Run; begin end;\r\nend.\r\n";

    private static (string? Kind, string? Overlay) Icons(HeadlessHarness h, string key)
    {
        var row = h.Find<DockPanel>("Tool.Structure.Row." + key);
        var icons = row.Children.OfType<IdeIcon>().ToList();
        var kind = icons.SingleOrDefault(i => i.Classes.Contains("symbol"));
        Assert.NotNull(kind);
        Assert.Equal(16, kind.Width);
        var overlay = icons.SingleOrDefault(i => i.Classes.Contains("visibility"));
        return (kind.IconName, overlay?.IconName);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "3")]
    [Trait("Req", "UI-TW-203")]
    public async Task StructureRowsShowTheirKindIcon()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 3;
            o.InitialFiles["Main.pas"] = Unit;
        });
        await h.ExecuteAsync("view.structure");
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Main.pas"))).Success);
        await h.WaitForAsync(() => h.FindAll("Tool.Structure.Row.").Count > 0, "structure built");
        Assert.Equal(("folder", null), Icons(h, "Uses"));
        Assert.Equal(("symbol-unit", null), Icons(h, "Uses/SysUtils"));
        Assert.Equal(("folder", null), Icons(h, "Types"));
        Assert.Equal(("symbol-enum", null), Icons(h, "Types/TKind"));
        Assert.Equal(("symbol-record", null), Icons(h, "Types/TPoint"));
        Assert.Equal(("symbol-interface", null), Icons(h, "Types/IThing"));
        Assert.Equal(("symbol-type", null), Icons(h, "Types/TAlias"));
        Assert.Equal(("symbol-class", null), Icons(h, "Types/TMainForm"));
        Assert.Equal(("symbol-field", "lock"), Icons(h, "Types/TMainForm/FCount"));
        Assert.Equal(("symbol-routine", "symbol-protected"), Icons(h, "Types/TMainForm/Greet"));
        Assert.Equal(("symbol-property", null), Icons(h, "Types/TMainForm/Count"));
        Assert.Equal(("symbol-constant", null), Icons(h, "Constants/Max"));
        Assert.Equal(("symbol-field", null), Icons(h, "Variables/MainForm"));
        Assert.Equal(("symbol-routine", null), Icons(h, "Routines/Run"));
        Assert.Equal("symbol-protected", SymbolIcons.VisibilityOverlay("strict protected"));
        Assert.Equal("lock", SymbolIcons.VisibilityOverlay("strict private"));
        Assert.Null(SymbolIcons.VisibilityOverlay(null));

        // An error row: the status.error octagon.
        var editor = h.Find<AvaloniaEdit.TextEditor>("Editor.Main.pas");
        editor.Document.Insert(editor.Document.Text.IndexOf("var", StringComparison.Ordinal), "const Y = ;\r\n");
        Dispatcher.UIThread.RunJobs();
        h.Clock.Advance(Core.ViewModels.StructureViewModel.RefreshDelay);
        await h.WaitForAsync(() => h.FindAll("Tool.Structure.Row.Errors/0").Count > 0, "error row");
        Assert.Equal(("error", null), Icons(h, "Errors/0"));
        Assert.Equal("status.error", IconCatalog.Get("error").ColorToken);
        Assert.Equal("Errors/0", AutomationProperties.GetAutomationId(h.Find<DockPanel>("Tool.Structure.Row.Errors/0"))!["Tool.Structure.Row.".Length..]);
    }
}
