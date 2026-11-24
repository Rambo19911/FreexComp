using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreexComp.Tests.Shared;

namespace FreexComp.Policy.Tests;

/// <summary>PLAT-020…024: what the three built fixture programs carry and report.</summary>
public sealed partial class PlatformTests
{
    private const ushort MachineX64 = 0x8664;
    private const ushort LargeAddressAware = 0x0020;
    private const ushort DynamicBase = 0x0040;
    private const ushort NxCompatible = 0x0100;

    /// <summary>A line of <c>tests/policy/platform-dlls.txt</c>: the library, then the first Windows 10 release that ships it.</summary>
    [GeneratedRegex(@"^([a-z0-9][a-z0-9._-]*\.dll) # (\d{4})$")]
    private static partial Regex SystemLibraryLine();

    private static string Golden(string name) =>
        File.ReadAllText(RepoPaths.Combine("compiler", "tests", "FreexComp.Compiler.Tests", "Toolchain", "Golden", name));

    [Fact]
    [Trait("Req", "PLAT-004")]
    [Trait("Req", "PLAT-020")]
    public async Task BuiltProgramsHaveExpectedPeHeaders()
    {
        foreach (var (name, subsystem) in ((string, ushort)[])[(PlatformPrograms.Console, 3), (PlatformPrograms.Gui, 2)])
        {
            var image = new PeImage(await programs.ImageAsync(name));

            Assert.True(image.Magic == 0x20B, $"{name}: not a PE32+ image");
            Assert.True(image.Machine == MachineX64, $"{name}: machine 0x{image.Machine:X4}");
            Assert.True(image.Subsystem == subsystem, $"{name}: subsystem {image.Subsystem}");
            Assert.True((image.Characteristics & LargeAddressAware) != 0, $"{name}: not large address aware");
            Assert.True((image.DllCharacteristics & DynamicBase) != 0, $"{name}: no dynamic base");
            Assert.True((image.DllCharacteristics & NxCompatible) != 0, $"{name}: not NX compatible");

            // PLAT-004: no load-time enforcement of the minimum; the linker's default versions stay
            Assert.Equal(((ushort)6, (ushort)0), image.OperatingSystemVersion);
            Assert.Equal(((ushort)6, (ushort)0), image.SubsystemVersion);
        }
    }

    /// <summary>PLAT-021. The fixture with the execution level <c>requireAdministrator</c> is read, never started.</summary>
    [Fact]
    [Trait("Req", "PLAT-021")]
    public async Task BuiltProgramsEmbedTheGeneratedManifest()
    {
        var console = Assert.IsType<string>(PeManifest.Read(await programs.ExeAsync(PlatformPrograms.Console)));
        var gui = Assert.IsType<string>(PeManifest.Read(await programs.ExeAsync(PlatformPrograms.Gui)));
        var admin = Assert.IsType<string>(PeManifest.Read(await programs.ExeAsync(PlatformPrograms.Admin)));

        Assert.Equal(PeManifest.Canonical(Golden("app.console.manifest")), PeManifest.Canonical(console));
        Assert.Equal(PeManifest.Canonical(Golden("app.gui.manifest")), PeManifest.Canonical(gui));
        Assert.NotEqual(PeManifest.Canonical(console), PeManifest.Canonical(gui));

        foreach (var manifest in (string[])[console, gui, admin])
        {
            var supported = Assert.Single(XDocument.Parse(manifest).Descendants(), e => e.Name.LocalName == "supportedOS");
            Assert.Equal(PlatformMatrix.SupportedOsId, supported.Attribute("Id")?.Value);
        }

        Assert.NotEqual(PeManifest.Canonical(console), PeManifest.Canonical(admin));
        Assert.Equal(
            PeManifest.Canonical(console),
            PeManifest.Canonical(admin).Replace("level=\"requireAdministrator\"", "level=\"asInvoker\"", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PLAT-022")]
    public async Task BuiltProgramsImportOnlyBaselineSystemLibraries()
    {
        var baseline = new HashSet<string>(StringComparer.Ordinal);
        var lines = File.ReadAllText(RepoPaths.Combine("tests", "policy", "platform-dlls.txt")).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        foreach (var line in lines)
        {
            var match = SystemLibraryLine().Match(line);
            Assert.True(match.Success, $"platform-dlls.txt: '{line}' is not '<name>.dll # <first release>'");
            var release = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.True(
                release <= PlatformMatrix.MinimumVersion,
                $"platform-dlls.txt: {match.Groups[1].Value} first ships with {release}, later than the minimum {PlatformMatrix.MinimumVersion}");
            Assert.True(baseline.Add(match.Groups[1].Value), $"platform-dlls.txt: {match.Groups[1].Value} is listed twice");
        }

        foreach (var name in (string[])[PlatformPrograms.Console, PlatformPrograms.Gui])
        {
            var imported = new PeImage(await programs.ImageAsync(name)).ImportedLibraries();

            Assert.Contains("kernel32.dll", imported);
            Assert.DoesNotContain(imported, l => l.StartsWith("api-ms-", StringComparison.Ordinal) || l.StartsWith("ext-ms-", StringComparison.Ordinal));
            var unknown = imported.Where(l => !baseline.Contains(l)).ToList();
            Assert.True(unknown.Count == 0, $"{name} imports libraries that platform-dlls.txt does not list: {string.Join(", ", unknown)}");
        }

        // the reader sees more than kernel32: the window fixture calls two functions of user32
        Assert.Contains("user32.dll", new PeImage(await programs.ImageAsync(PlatformPrograms.Gui)).ImportedLibraries());
    }

    [Fact]
    [Trait("Req", "PLAT-023")]
    public async Task ConsoleProgramReportsTheHostVersion()
    {
        var exe = await programs.ExeAsync(PlatformPrograms.Console);

        var (exit, output) = await PlatformPrograms.RunAsync(exe, [], TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken);

        // a .NET 10 process is told the true version of the system
        var v = Environment.OSVersion.Version;
        var numbers = string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Build}");
        var lines = output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        Assert.Equal(0, exit);
        Assert.Equal(2, lines.Length);
        Assert.Equal(numbers, lines[0]);
        Assert.StartsWith("Windows ", lines[1], StringComparison.Ordinal);
        Assert.EndsWith($" ({numbers}, x64)", lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// PLAT-024. The window fixture creates no window: it writes its answer to the file it is given and ends. It is stopped
    /// when it has not ended after 30 seconds.
    /// </summary>
    [Fact]
    [Trait("Req", "PLAT-024")]
    public async Task GuiProgramRunsPerMonitorV2Aware()
    {
        var perMonitor = await programs.ExeAsync(PlatformPrograms.Gui);
        var system = await programs.BuildVariantAsync(PlatformPrograms.Gui, "dpi-system", new JsonObject { ["dpiAwareness"] = "system" });

        Assert.Equal("dpi=1", await AnswerAsync(perMonitor));
        Assert.Equal("dpi=0", await AnswerAsync(system));

        static async Task<string> AnswerAsync(string exe)
        {
            var answer = Path.Combine(Path.GetDirectoryName(exe)!, "answer.txt");
            var (exit, output) = await PlatformPrograms.RunAsync(exe, [answer], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.True(exit == 0, $"{Path.GetFileName(exe)}: exit code {exit}\n{output}");
            return (await File.ReadAllTextAsync(answer, TestContext.Current.CancellationToken)).TrimEnd('\r', '\n');
        }
    }
}
