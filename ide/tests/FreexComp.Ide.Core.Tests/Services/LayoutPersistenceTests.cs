using System.Text;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

[Trait("Phase", "1")]
public sealed class LayoutPersistenceTests
{
    private const string UserDir = @"C:\u";
    private const string UiStateFile = @"C:\u\roaming\uistate.json";

    private static readonly WorkArea Primary = new(0, 0, 1920, 1040);
    private static readonly WorkArea Second = new(1920, 0, 1280, 984);

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-052")]
    [Trait("Req", "UI-WIN-053")]
    public void OffScreenBoundsAreRecenterd()
    {
        var centered = new WindowPlacement((1920 - 1440) / 2, (1040 - 900) / 2, 1440, 900, Maximized: false);

        // First start: min(1440, 90 % of the work area) × min(900, 90 %), centerd on the primary monitor.
        Assert.Equal(centered, WindowPlacementRules.Restore(null, [Primary, Second], Primary));
        Assert.Equal(new WindowPlacement(64, 36, 1152, 648, false),
            WindowPlacementRules.Restore(null, [new WorkArea(0, 0, 1280, 720)], new WorkArea(0, 0, 1280, 720)));

        // Bounds on any current monitor are kept, also on the second one.
        var onSecond = new WindowPlacement(2000, 100, 1000, 700, false);
        Assert.Equal(onSecond, WindowPlacementRules.Restore(onSecond, [Primary, Second], Primary));

        // That monitor is gone: centerd on the primary monitor at the default size.
        Assert.Equal(centered, WindowPlacementRules.Restore(onSecond, [Primary], Primary));

        // Less than 100 × 100 DIP of the title-bar area on a work area: only 70 DIP wide …
        Assert.Equal(centered, WindowPlacementRules.Restore(new WindowPlacement(1850, 10, 900, 600, false), [Primary], Primary));
        // … or only 40 DIP high (the title bar is above the top edge).
        Assert.Equal(centered, WindowPlacementRules.Restore(new WindowPlacement(100, -60, 900, 600, false), [Primary], Primary));
        // Exactly 100 × 100 is enough.
        var corner = new WindowPlacement(1820, 940, 900, 600, false);
        Assert.Equal(corner, WindowPlacementRules.Restore(corner, [Primary], Primary));

        // Maximized stays maximized, on the primary monitor when its monitor is gone.
        Assert.Equal(centered with { Maximized = true },
            WindowPlacementRules.Restore(onSecond with { Maximized = true }, [Primary], Primary));

        // Sizes below the window minimum (800 × 500) are raised to it.
        Assert.Equal(new WindowPlacement(100, 100, 800, 500, false),
            WindowPlacementRules.Restore(new WindowPlacement(100, 100, 300, 200, false), [Primary], Primary));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-061")]
    [Trait("Req", "IDE-ARCH-063")]
    [Trait("Req", "UI-WIN-054")]
    public void InvalidSectionsFallBackIndividually()
    {
        var disk = new InMemoryFileSystem();
        disk.CreateDirectory(@"C:\u\roaming");
        disk.WriteAllBytes(UiStateFile, Encoding.UTF8.GetBytes("""
            {
              "formatVersion": 1,
              "window": { "x": 10, "y": 20, "width": "wide", "height": 700, "state": "normal" },
              "activeProfile": "Edit",
              "profiles": {
                "Edit": {
                  "left": { "size": 40, "tools": ["Bogus", "Structure", "ProjectManager", "Structure"], "hidden": [], "active": "Structure" },
                  "right": { "size": 5000, "split": 0.25,
                             "upper": { "tools": ["Messages"], "hidden": [], "active": null },
                             "lower": { "tools": [], "hidden": [], "active": null } },
                  "bottom": { "size": 10, "tools": ["Output"], "hidden": [], "active": "Output" }
                },
                "Future": { "left": 1 }
              },
              "toolWindows": { "Messages": { "columns": { "Code": 90, "File": "x" }, "sort": { "column": "File", "direction": "ascending" } } },
              "find": { "matchCase": true }
            }
            """));
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 13, 9, 15, 0, TimeSpan.Zero));
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = disk;
            o.Clock = clock;
            o.UserDir = UserDir;
        });
        var layout = services.Layout;
        var edit = layout.Layout.State.Profiles[LayoutProfiles.Edit];

        Assert.Null(layout.WindowPlacement); // invalid window section → first-start placement
        Assert.Null(services.Status.Message); // valid JSON: no corrupt-file message (IDE-ARCH-063)

        // Unknown names are ignored, duplicates keep the first occurrence, missing tool windows return to their built-in place.
        Assert.Equal(["Structure", "ProjectManager"], edit.Left.Tools);
        Assert.Equal(["DataExplorer"], edit.Left.Hidden);
        Assert.Equal(["Messages"], edit.RightUpper.Tools);
        Assert.Equal(["Output", "SearchResults"], edit.Bottom.Tools);
        Assert.Equal(["ToDo"], edit.Bottom.Hidden);
        Assert.Equal("Structure", edit.Left.Active);

        // Sizes below the dock minimums are clamped (UI-WIN-005); the split is kept.
        Assert.Equal((160, 5000, 128, 0.25), (edit.LeftSize, edit.RightSize, edit.BottomSize, edit.RightSplit));

        Assert.Equal(90, layout.GetColumnWidth("Messages", "Code"));
        Assert.Null(layout.GetColumnWidth("Messages", "File"));

        // Members this version does not know are written back unchanged.
        layout.SetColumnWidth("Messages", "File", 10); // below the 32 DIP column minimum (UI-TW-011)
        layout.Save();
        var saved = Encoding.UTF8.GetString(disk.ReadAllBytes(UiStateFile));
        Assert.Contains("\"Future\": {\n      \"left\": 1\n    }", saved, StringComparison.Ordinal);
        Assert.Contains("\"find\": {\n    \"matchCase\": true\n  }", saved, StringComparison.Ordinal);
        Assert.Contains("\"sort\": {", saved, StringComparison.Ordinal);
        Assert.Contains("\"File\": 32", saved, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"formatVersion\": 1,\n", saved, StringComparison.Ordinal);
        Assert.EndsWith("}\n", saved, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-051")]
    public void ChangesAreSavedTwoSecondsAfterTheLast()
    {
        var disk = new InMemoryFileSystem();
        var clock = new ManualClock();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = disk;
            o.Clock = clock;
            o.UserDir = UserDir;
        });
        var layout = services.Layout.Layout;

        layout.LeftSize = 300;
        clock.Advance(TimeSpan.FromSeconds(1.5));
        layout.BottomSize = 250; // restarts the delay
        clock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(disk.FileExists(UiStateFile));

        clock.Advance(TimeSpan.FromSeconds(0.5));
        var saved = Encoding.UTF8.GetString(disk.ReadAllBytes(UiStateFile));
        Assert.Contains("\"size\": 300", saved, StringComparison.Ordinal);
        Assert.Contains("\"size\": 250", saved, StringComparison.Ordinal);
        Assert.False(disk.FileExists(UiStateFile + ".tmp"));
    }
}
