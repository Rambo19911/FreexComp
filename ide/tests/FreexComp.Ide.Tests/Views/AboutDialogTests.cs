using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class AboutDialogTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-150")]
    [Trait("Req", "UI-DLG-160")]
    public async Task ShowsVersionAndPaths()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("help.about");
        var dlg = await h.WaitForDialogAsync("About");

        Assert.Equal("About FreexComp", dlg.Window.Title);
        Assert.Equal((520.0, false), (dlg.Window.Width, dlg.Window.CanResize));
        string Text(string field) => dlg.Find<SelectableTextBlock>(field).Text ?? "";
        var informational = typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal("FreexComp", Text("ProductName"));
        Assert.StartsWith("Version " + informational, Text("Version"), StringComparison.Ordinal);
        Assert.Equal(Strings.Msg_About_License, Text("License"));
        Assert.StartsWith(".NET " + Environment.Version + ", ", Text("Runtime"), StringComparison.Ordinal);
        Assert.EndsWith(", x64", Text("Runtime"), StringComparison.Ordinal);
        Assert.Equal(Strings.Msg_About_ToolchainNotFound, Text("Toolchain"));
        Assert.Equal(h.Services.UserPaths.RoamingDirectory, Text("Settings"));
        Assert.NotNull(dlg.Find<Control>("Icon"));

        // UI-DLG-150: no online links, no vendor names; everything is plain selectable text.
        Assert.Empty(dlg.Window.GetLogicalDescendants().OfType<HyperlinkButton>());
        var all = string.Join("\n", dlg.FindAll("Dialog.About.").OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain("http", all, StringComparison.OrdinalIgnoreCase);

        // Copy Info: one "Label: value" line per field; the dialog stays open.
        dlg.Click("CopyInfo");
        Assert.True(dlg.Window.IsVisible);
        var copied = ((FakeClipboard)h.Services.Clipboard).Text!.Split(Environment.NewLine);
        Assert.Equal("Product: FreexComp", copied[0]);
        Assert.Equal("Version: " + Text("Version")["Version ".Length..], copied[1]);
        Assert.Contains("Runtime: " + Text("Runtime"), copied);
        Assert.Contains("Toolchain: " + Text("Toolchain"), copied);
        Assert.Contains("Settings: " + h.Services.UserPaths.RoamingDirectory, copied);

        // Third-Party Licenses... closes About and opens the Licenses dialog (help.licenses).
        Assert.Equal("Close", AutomationProperties.GetName(dlg.Find<Button>("Close")));
        dlg.Click("Licenses");
        var licenses = await h.WaitForDialogAsync("Licenses");
        Assert.False(dlg.Window.IsVisible);
        Assert.Equal("Third-Party Licenses", licenses.Window.Title);
        Assert.Equal((760.0, 520.0, true), (licenses.Window.Width, licenses.Window.Height, licenses.Window.CanResize));
        var rows = licenses.FindAll("Dialog.Licenses.Component.").Select(AutomationIds.Get).ToList();
        Assert.Contains("Dialog.Licenses.Component.Avalonia", rows);
        Assert.Contains("Dialog.Licenses.Component.LLVM", rows);
        Assert.Equal(rows.Order(StringComparer.CurrentCultureIgnoreCase), rows);
        var first = ThirdPartyNotices.LoadEmbedded().OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).First();
        Assert.StartsWith($"{first.Name} {first.Version} \u2014 {first.License}", licenses.Find<SelectableTextBlock>("Text").Text, StringComparison.Ordinal);

        licenses.Click("Component.LLVM");
        Assert.StartsWith("LLVM 23.1.5 \u2014 Apache-2.0 WITH LLVM-exception", licenses.Find<SelectableTextBlock>("Text").Text, StringComparison.Ordinal);
        Assert.Contains("Apache License", licenses.Find<SelectableTextBlock>("Text").Text, StringComparison.Ordinal);
        licenses.Press("Escape");
        await running;
        Assert.Empty(h.Window.OwnedWindows);
    }
}
