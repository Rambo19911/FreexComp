using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace FreexComp.Setup.Tests;

/// <summary>
/// The end-to-end tests of the offline installer (installer.md §4, <c>Category=Installer</c>): the real product, built by
/// <c>scripts/build-installer.ps1</c>, installed by its setup program or unzipped from the portable zip, and used. They
/// take minutes, need no network and no elevation, and write only the test's scratch folder under <c>%TEMP%</c>, the
/// registry root <c>HKCU\Software\FreexComp-SetupTest\&lt;guid&gt;</c> (test switches of INS-005) and the script's output
/// folder <c>artifacts\installer</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Category", "Installer")]
public sealed class EndToEndTests
{
    private static readonly TimeSpan SetupLimit = TimeSpan.FromMinutes(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Card acceptance 1: silent install into a scratch folder, hello program compiled, run and debugged with the
    /// installed tools under the reduced environment, layout with licences and network policy, silent uninstall that
    /// leaves nothing.
    /// </summary>
    [Fact]
    [Trait("Req", "INS-001")]
    [Trait("Req", "INS-002")]
    [Trait("Req", "LIC-042")]
    [Trait("Req", "NET-040")]
    [Trait("Req", "LSRC-006")]
    public async Task InstalledProductCompilesAndRunsHello()
    {
        var product = ProductBuild.Artifacts;
        using var box = new SetupSandbox();

        // /S install through the test switches: scratch target, registry root and Start-menu folder
        Assert.Equal(0, SetupProgramBuild.Run(product.Setup, box.Switches(), box.Variables(), SetupLimit));
        var folder = new ProductFolder(box.Home, box);
        folder.AssertLayout(portable: false);
        Assert.True(File.Exists(Path.Combine(box.StartMenu, "FreexComp.lnk")));
        Assert.Equal(product.Version, box.Value(Installer.UninstallKey, "DisplayVersion"));
        Assert.Equal(box.Home, box.Value(Installer.UninstallKey, "InstallLocation"));
        using (var record = JsonDocument.Parse(File.ReadAllText(folder.Combine(InstallRecord.FileName))))
            Assert.Equal(product.Version, record.RootElement.GetProperty("version").GetString());
        var installed = folder.Files();

        // the installed tools: compile, run, debug; then the library source layout checks against the folder
        var (source, program) = folder.BuildAndRunHello(box.In("work"));
        Report(folder, await folder.DebugHelloAsync(source, program, Ct));
        folder.AssertLibrarySourceLayout();

        // using the product wrote nothing into its folder and nothing into the profile folders
        Assert.Equal(installed, folder.Files());
        AssertNoProductData(box.AppData, box.LocalAppData, box.ProgramData, box.ProgramFiles);

        // /S uninstall by the registered command: the folder, the shortcut and every registry entry are gone
        var uninstaller = folder.Combine(InstallRecord.UninstallProgram);
        Assert.Equal($"\"{uninstaller}\" /uninstall /S", box.Value(Installer.UninstallKey, "QuietUninstallString"));
        Assert.Equal(0, SetupProgramBuild.Run(uninstaller, ["/uninstall", "/S"], box.Variables(), SetupLimit));
        var watch = Stopwatch.StartNew();
        while (Directory.Exists(box.Home) || Directory.EnumerateFiles(box.Temp, "FreexComp-uninstall-*.exe").Any())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(120),
                "the uninstall did not finish within 120 s; left: " + string.Join(", ", Directory.Exists(box.Home) ? Directory.EnumerateFileSystemEntries(box.Home).Take(20) : []));
            await Task.Delay(200, Ct);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(box.StartMenu));
        using (var root = box.OpenKey(""))
            Assert.True(root is null || root is { SubKeyCount: 0, ValueCount: 0 }, "registry entries are left under the test root");
        AssertNoProductData(box.AppData, box.LocalAppData, box.ProgramData, box.ProgramFiles);
    }

    /// <summary>
    /// Card acceptance 3: the portable zip unpacked into a path with spaces and letters outside ASCII; its tools compile,
    /// run and debug the hello program from there, and the IDE's installation check keeps the user data inside the folder.
    /// </summary>
    [Fact]
    [Trait("Req", "INS-001")]
    [Trait("Req", "INS-002")]
    [Trait("Req", "INS-020")]
    [Trait("Req", "INS-021")]
    public async Task PortableZipRunsFromSpacesAndNonAscii()
    {
        var product = ProductBuild.Artifacts;
        using var box = new SetupSandbox();
        var home = box.In(SetupSandbox.SpacesAndNonAscii);
        ZipFile.ExtractToDirectory(product.PortableZip, home);
        var folder = new ProductFolder(home, box);
        folder.AssertLayout(portable: true);
        Assert.Equal("{ \"formatVersion\": 1 }\n", File.ReadAllText(folder.Combine(PackageWriter.PortableMarkerName)));
        var unzipped = folder.Files();

        // the tools work from there, on a program that also lies in a folder with spaces and letters outside ASCII
        var (source, program) = folder.BuildAndRunHello(box.In("work in " + SetupSandbox.SpacesAndNonAscii));
        Report(folder, await folder.DebugHelloAsync(source, program, Ct));

        // INS-021: the IDE's installation check finds the marker and creates the user directory inside the folder
        Assert.False(Directory.Exists(folder.Combine("userdata")));
        var smoke = folder.Run("FreexComp.Ide.exe", home, "--smoke-dpi");
        Assert.Equal((0, "per-monitor-v2\n", ""), (smoke.ExitCode, smoke.Output.ReplaceLineEndings("\n"), smoke.Error));
        Assert.True(Directory.Exists(folder.Combine("userdata")), "the IDE did not create userdata next to its executable");
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Combine("userdata")));

        // nothing else changed in the folder, and nothing was written to the profile folders
        Assert.Equal(unzipped, folder.Files());
        Assert.Equal(["userdata"], Directory.EnumerateDirectories(home).Select(Path.GetFileName).Except(
            unzipped.Where(f => f.Contains('\\')).Select(f => f[..f.IndexOf('\\')]), StringComparer.OrdinalIgnoreCase));
        AssertNoProductData(box.AppData, box.LocalAppData, box.ProgramData, box.ProgramFiles);
    }

    /// <summary>Writes what the debug adapter loaded from the product folder, and its error stream, to the test output.</summary>
    private static void Report(ProductFolder folder, (IReadOnlyList<string> AdapterModules, IReadOnlyList<string> AdapterErrors) debug)
    {
        var output = TestContext.Current.TestOutputHelper;
        var own = debug.AdapterModules.Where(m => m.StartsWith(folder.Home, StringComparison.OrdinalIgnoreCase)).Select(m => Path.GetRelativePath(folder.Home, m)).ToList();
        output?.WriteLine($"debug adapter modules from the product folder ({own.Count} of {debug.AdapterModules.Count}; the rest are Windows'): {string.Join(", ", own)}");
        output?.WriteLine($"debug adapter error stream ({debug.AdapterErrors.Count} lines): {string.Join(" | ", debug.AdapterErrors)}");
    }

    /// <summary>
    /// The product wrote nothing into the profile folders of the run: they hold nothing but what Windows itself puts
    /// there for any program that uses the shell (<c>Microsoft\Windows\Caches</c> under the local application data).
    /// </summary>
    private static void AssertNoProductData(params string[] folders)
    {
        foreach (var folder in folders)
        {
            var entries = Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Select(e => Path.GetRelativePath(folder, e)).ToList();
            var foreign = entries.Where(e => e.Contains("FreexComp", StringComparison.OrdinalIgnoreCase)
                                             || !(e.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || e.StartsWith("Microsoft" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.True(foreign.Count == 0, folder + " was written to: " + string.Join(", ", foreign.Take(20)));
        }
    }
}
