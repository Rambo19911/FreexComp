using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The command lists of configurations in the dialog <c>TrustProject</c> (UI-DLG-298; card F09-007).</summary>
public sealed partial class TrustDialogTests
{
    /// <summary>A configuration name of 32 characters (UI-DLG-298).</summary>
    internal const string LongConfiguration = "ReleaseWithDebugSymbolsAndChecks";

    /// <summary>Hello.pproj with project-level commands and two configurations that have their own.</summary>
    internal static void UseConfigurationEvents(HarnessOptions o) =>
        o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release",
                                               "events": { "postBuild": [ "tools\\sign.cmd \"$(OutputPath)\"" ] } },
                                  "{{LongConfiguration}}": { "optimization": 2, "debugInfo": true, "defines": [], "outputDir": "bin/Symbols",
                                               "events": { "preBuild": [ "echo symbols" ], "postBuild": [ "echo packed" ] } } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ],
              "events": { "preBuild": [ "tools\\gen.exe --out src\\Generated.pas" ], "postBuild": [ "copy /y bin\\Debug\\Hello.exe ..\\deploy\\" ] } }
            """;

    /// <summary>
    /// UI-DLG-298: the list names of configurations are shown in full — the name column is as wide as the widest name, at
    /// least 96 — and every command starts at the same x.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-298")]
    [Trait("Req", "IDE-EVT-007")]
    public async Task ConfigurationListNamesAreShownInFull()
    {
        Assert.Equal(32, LongConfiguration.Length);
        await using var h = await BuildScenario.StartAsync(UseConfigurationEvents);
        await Guarded(h, async () =>
        {
            var building = h.Services.Build.BuildAsync(BuildKind.Build);
            var dlg = await h.WaitForDialogAsync("TrustProject");
            var list = dlg.Find<ListBox>("Commands");
            var rows = list.Items.Cast<ListBoxItem>().Where(i => AutomationIds.Get(i)!.Contains(".Command.", StringComparison.Ordinal)).ToList();
            Assert.Equal(
            [
                (Id("Command.0.0"), @"Before build: tools\gen.exe --out src\Generated.pas"),
                (Id("Command.0.1"), @"After build: copy /y bin\Debug\Hello.exe ..\deploy\"),
                (Id("Command.0.2"), @"After build (Release): tools\sign.cmd ""$(OutputPath)"""),
                (Id("Command.0.3"), $"Before build ({LongConfiguration}): echo symbols"),
                (Id("Command.0.4"), $"After build ({LongConfiguration}): echo packed"),
            ], rows.Select(r => (AutomationIds.Get(r)!, Avalonia.Automation.AutomationProperties.GetName(r)!)));

            var widest = 0d;
            var commandX = new List<double>();
            foreach (var row in rows)
            {
                Assert.True(row.IsEffectivelyVisible);
                var cells = row.GetVisualDescendants().OfType<TextBlock>().ToList();
                var (name, command) = (cells[0], cells[^1]);
                // Not cut: no trimming, no wrapping, and the cell is wider than its text needs.
                Assert.Equal((Avalonia.Media.TextWrapping.NoWrap, Avalonia.Media.TextTrimming.None), (name.TextWrapping, name.TextTrimming));
                var needed = name.TextLayout.WidthIncludingTrailingWhitespace;
                Assert.True(name.Bounds.Width >= needed, $"'{name.Text}' needs {needed}, the cell has {name.Bounds.Width}");
                Assert.True(name.Bounds.Width >= 96);
                widest = Math.Max(widest, needed);
                commandX.Add(command.TranslatePoint(default, list)!.Value.X);
                Assert.True(commandX[^1] > name.TranslatePoint(new Point(needed, 0), list)!.Value.X, "the command starts after the list name");
            }

            Assert.True(widest > 96, "the long name is wider than the former fixed column");
            Assert.All(commandX, x => Assert.Equal(commandX[0], x)); // one column: every command starts at the same x
            Assert.Equal($"After build ({LongConfiguration})", rows[^1].GetVisualDescendants().OfType<TextBlock>().First().Text);

            dlg.Press("Escape");
            Assert.Null(await building);
        });
    }
}
