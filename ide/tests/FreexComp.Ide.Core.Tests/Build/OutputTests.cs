using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>The Output channels (IDE-P3-070, I-213).</summary>
[Trait("Phase", "5")]
public sealed class OutputTests
{
    private const string Meta = @"C:\meta";

    /// <summary>
    /// IDE-P3-070, DSG-TP-005, tool-palette.md (Output channel <c>designer</c>, shown as "Designer"): from P5 the Output
    /// has a <c>designer</c> channel; every metadata problem is written to it as <c>Msg.Designer.MetadataError</c>.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-070")]
    [Trait("Req", "DSG-TP-005")]
    public Task DesignerChannel() => TestUiThread.RunAsync(DesignerChannelOnUiThread);

    private static async Task DesignerChannelOnUiThread()
    {
        var clock = new ManualClock();
        var f = new DesignerFixture(o =>
        {
            o.Clock = clock;
            o.ImplementedPhase = 5; // the channels of Phase 5; Phase 6 adds "debugger" (DebugServiceTests.OutputCategoriesGoToChannels)
        });
        var output = f.Services.Output;
        Assert.Equal([OutputChannels.Build, OutputChannels.Program, OutputChannels.Designer], output.Channels);
        Assert.Equal("designer", OutputChannels.Designer);
        Assert.Equal("Designer", Strings.ResourceManager.GetString("Output_Channel_designer", System.Globalization.CultureInfo.CurrentCulture));

        // A component without "class" is skipped and reported on the designer channel.
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "metadata"), "*.json"))
        {
            var text = File.ReadAllText(file);
            if (Path.GetFileName(file) == "ActnList.components.json")
                text = text.Replace("\"components\": [", "\"components\": [\n    { \"kind\": \"control\" },", StringComparison.Ordinal);
            f.File(Path.Combine(Meta, Path.GetFileName(file)), text);
        }

        f.Services.Settings.Set(SettingsCatalog.ToolchainFrameworkDesignDir, Meta);
        await f.Services.Metadata!.LoadAsync();
        clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output (IDE-P3-070)
        var line = Assert.Single(output.GetLines(OutputChannels.Designer));
        Assert.StartsWith(Path.Combine(Meta, "ActnList.components.json") + "(6,", line.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("component metadata error", line.Text, StringComparison.Ordinal);
        Assert.Equal(OutputKind.Error, line.Kind);
        Assert.Empty(output.GetLines(OutputChannels.Build));

        // Before P5 there is no designer channel.
        var phase4 = TestComposition.Build(o => o.ImplementedPhase = 4);
        Assert.Equal([OutputChannels.Build, OutputChannels.Program], phase4.Output.Channels);
    }
}
