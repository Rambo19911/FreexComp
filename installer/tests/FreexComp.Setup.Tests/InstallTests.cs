using System.Diagnostics;
using System.Text.Json;

namespace FreexComp.Setup.Tests;

[Trait("Phase", "8")]
public sealed class InstallTests
{
    private static readonly (string Extension, string ProgId)[] Associations =
        [(".pproj", "FreexComp.Project"), (".pgroup", "FreexComp.ProjectGroup"), (".pas", "FreexComp.Source"), (".form", "FreexComp.Form")];

    [Fact]
    [Trait("Req", "INS-007")]
    [Trait("Req", "INS-008")]
    [Trait("Req", "INS-009")]
    [Trait("Req", "INS-010")]
    public void InstallWritesFilesShortcutAndRegistry()
    {
        using var box = new SetupSandbox();
        var program = box.FakeProgram();
        var package = box.Package(box.Payload("v1", ManifestTests.SmallPayload), program);

        var result = box.Run(package, box.Switches(), version: "1.2.3");
        Assert.Equal(new SetupResult(ExitCode.Ok, $"FreexComp 1.2.3 was installed in {box.Home}."), result);

        // files: the payload, its manifest, uninstall.exe = the program without payload
        var home = box.Home;
        foreach (var (path, content) in ManifestTests.SmallPayload)
            Assert.Equal(content, File.ReadAllText(Path.Combine(home, path)));
        Assert.True(File.Exists(Path.Combine(home, "payload.sha256")));
        Assert.Equal(File.ReadAllBytes(program), File.ReadAllBytes(Path.Combine(home, "uninstall.exe")));

        // shortcut FreexComp -> H\FreexComp.Ide.exe
        var ide = Path.Combine(home, "FreexComp.Ide.exe");
        var shortcut = Path.Combine(box.StartMenu, "FreexComp.lnk");
        Assert.True(File.Exists(shortcut));
        Assert.Equal(ide, Shortcut.ReadTarget(shortcut), ignoreCase: true);

        // INS-008: ProgIds with icon and open command; .pproj / .pgroup defaults (they had none); OpenWithProgids for all four
        foreach (var (extension, progId) in Associations)
        {
            Assert.Equal($"{ide},0", box.Value($@"Software\Classes\{progId}\DefaultIcon", ""));
            Assert.Equal($"\"{ide}\" \"%1\"", box.Value($@"Software\Classes\{progId}\shell\open\command", ""));
            Assert.Equal(extension is ".pproj" or ".pgroup" ? progId : null, box.Value($@"Software\Classes\{extension}", ""));
            using var openWith = box.OpenKey($@"Software\Classes\{extension}\OpenWithProgids");
            Assert.Equal([progId], openWith!.GetValueNames());
        }

        // nothing else under Software\Classes
        using (var classes = box.OpenKey(@"Software\Classes"))
        {
            Assert.Equal(Associations.SelectMany(a => (string[])[a.Extension, a.ProgId]).Order(StringComparer.OrdinalIgnoreCase),
                classes!.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase));
        }

        // INS-009: the uninstall entry
        const string entry = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreexComp";
        var uninstaller = Path.Combine(home, "uninstall.exe");
        Assert.Equal("FreexComp", box.Value(entry, "DisplayName"));
        Assert.Equal("1.2.3", box.Value(entry, "DisplayVersion"));
        Assert.Equal("FreexComp project", box.Value(entry, "Publisher"));
        Assert.Equal(home, box.Value(entry, "InstallLocation"));
        Assert.Equal($"\"{uninstaller}\" /uninstall", box.Value(entry, "UninstallString"));
        Assert.Equal($"\"{uninstaller}\" /uninstall /S", box.Value(entry, "QuietUninstallString"));
        Assert.Equal($"{ide},0", box.Value(entry, "DisplayIcon"));
        Assert.Equal(1, box.Value(entry, "EstimatedSize"));   // a few hundred bytes round up to 1 KB
        Assert.Equal(1, box.Value(entry, "NoModify"));
        Assert.Equal(1, box.Value(entry, "NoRepair"));

        // INS-010: install.json
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "install.json")));
        var record = json.RootElement;
        Assert.Equal("1.2.3", record.GetProperty("version").GetString());
        Assert.Equal("user", record.GetProperty("scope").GetString());
        Assert.Equal(@"HKEY_CURRENT_USER\" + box.RegistryRoot, record.GetProperty("registryRoot").GetString());
        Assert.Equal(shortcut, record.GetProperty("shortcut").GetString());
        Assert.Equal(
            ManifestTests.SmallPayload.Keys.Append("payload.sha256").Append("uninstall.exe").Order(StringComparer.OrdinalIgnoreCase),
            record.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).Order(StringComparer.OrdinalIgnoreCase));
        var keys = record.GetProperty("keys").EnumerateArray().Select(k => (k.GetProperty("key").GetString(), k.GetProperty("tree").GetBoolean())).ToList();
        Assert.Contains((entry, true), keys);
        Assert.Contains((@"Software\Classes\FreexComp.Project", true), keys);
        Assert.Contains((@"Software\Classes\.pproj", false), keys);
        Assert.Contains((@"Software\Classes\.pproj\OpenWithProgids", false), keys);
        var values = record.GetProperty("values").EnumerateArray()
            .Select(v => (v.GetProperty("key").GetString(), v.GetProperty("name").GetString(), v.GetProperty("data").GetString(), v.GetProperty("previous").ValueKind)).ToList();
        Assert.Contains((@"Software\Classes\.pproj", "", "FreexComp.Project", JsonValueKind.Null), values);
        Assert.Contains((@"Software\Classes\.pgroup", "", "FreexComp.ProjectGroup", JsonValueKind.Null), values);
        Assert.DoesNotContain(values, v => v.Item1 == @"Software\Classes\.pas" && v.Item2 == "");
        Assert.Equal(6, values.Count);   // two defaults + four OpenWithProgids entries

        // /noshortcuts /noassoc: only files and the uninstall entry
        using var bare = new SetupSandbox();
        Assert.Equal(ExitCode.Ok, bare.Run(package, bare.Switches("/noshortcuts", "/noassoc")).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(bare.StartMenu));
        Assert.Null(bare.OpenKey(@"Software\Classes"));
        Assert.Equal("FreexComp", bare.Value(entry, "DisplayName"));
    }

    [Fact]
    [Trait("Req", "INS-011")]
    public void UninstallRemovesExactlyWhatWasInstalled()
    {
        using var box = new SetupSandbox();
        var program = SetupProgramBuild.ProgramPath;   // a runnable uninstall.exe
        var package = box.Package(box.Payload("v1", ManifestTests.SmallPayload), program);

        // a foreign default for .pproj, user data in an %APPDATA%-like folder and a user project inside H
        box.SetValue(@"Software\Classes\.pproj", "", "Other.Editor.Project");
        var settings = Path.Combine(box.AppData, "FreexComp", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "{}");
        var localCache = Path.Combine(box.LocalAppData, "FreexComp", "cache.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(localCache)!);
        File.WriteAllText(localCache, "cache");

        Assert.Equal(ExitCode.Ok, box.Run(package, box.Switches()).Code);
        Assert.Equal("Other.Editor.Project", box.Value(@"Software\Classes\.pproj", ""));
        var project = Path.Combine(box.Home, "projects", "Hello.pas");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "program Hello; begin end.");

        // the registered uninstall command, as a real process with %TEMP% in the sandbox
        var uninstaller = Path.Combine(box.Home, "uninstall.exe");
        Assert.Equal(0, SetupProgramBuild.Run(uninstaller, ["/uninstall", "/S"], box.Variables(), TimeSpan.FromSeconds(60)));
        var watch = Stopwatch.StartNew();
        while (File.Exists(uninstaller) || box.OpenKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreexComp") is not null)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "the uninstall did not finish within 30 s");
            Thread.Sleep(100);
        }

        // the temporary copy is gone within 10 s
        watch.Restart();
        while (Directory.EnumerateFiles(box.Temp, "FreexComp-uninstall-*.exe").Any())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "the temporary uninstall copy is still there after 10 s");
            Thread.Sleep(100);
        }

        // exactly what was installed is gone
        foreach (var path in ManifestTests.SmallPayload.Keys.Append("payload.sha256").Append("uninstall.exe").Append("install.json"))
            Assert.False(File.Exists(Path.Combine(box.Home, path)), path);
        Assert.False(Directory.Exists(Path.Combine(box.Home, "rtl")));
        Assert.False(Directory.Exists(Path.Combine(box.Home, "third_party")));
        Assert.False(File.Exists(Path.Combine(box.StartMenu, "FreexComp.lnk")));
        foreach (var (extension, progId) in Associations)
        {
            Assert.Null(box.OpenKey($@"Software\Classes\{progId}"));
            Assert.Null(box.OpenKey($@"Software\Classes\{extension}\OpenWithProgids"));
        }

        Assert.Null(box.OpenKey(@"Software\Classes\.pas"));   // created by the install, empty again

        // and nothing else: the user's project (so H stays), user data, the foreign default
        Assert.Equal(["Hello.pas"], Directory.EnumerateFiles(Path.Combine(box.Home, "projects")).Select(Path.GetFileName));
        Assert.Equal(["projects"], Directory.EnumerateFileSystemEntries(box.Home).Select(Path.GetFileName));
        Assert.Equal("{}", File.ReadAllText(settings));
        Assert.Equal("cache", File.ReadAllText(localCache));
        Assert.Equal("Other.Editor.Project", box.Value(@"Software\Classes\.pproj", ""));

        // an install without user files leaves no H behind (uninstall run in-process, from a copy)
        using var clean = new SetupSandbox();
        Assert.Equal(ExitCode.Ok, clean.Run(package, clean.Switches()).Code);
        Assert.Equal("FreexComp.Project", clean.Value(@"Software\Classes\.pproj", ""));
        Assert.Equal(ExitCode.Ok, clean.Run(program, ["/S", "/uninstall", "/D=" + clean.Home]).Code);
        Assert.False(Directory.Exists(clean.Home));
        Assert.False(File.Exists(Path.Combine(clean.StartMenu, "FreexComp.lnk")));
        using (var root = clean.OpenKey(""))
            Assert.True(root is null || root.SubKeyCount == 0 && root.ValueCount == 0, "registry entries are left under the test root");
    }

    [Fact]
    [Trait("Req", "INS-007")]
    public void ReinstallReplacesOldVersion()
    {
        using var box = new SetupSandbox();
        var v1 = box.Package(box.Payload("v1", new Dictionary<string, string>
        {
            ["FreexComp.Ide.exe"] = "IDE 1",
            [@"rtl\src\Old.pas"] = "unit Old;",
            [@"help\index.md"] = "help 1",
        }), name: "v1.exe");
        var v2 = box.Package(box.Payload("v2", new Dictionary<string, string>
        {
            ["FreexComp.Ide.exe"] = "IDE 2",
            [@"rtl\src\New.pas"] = "unit New;",
            [@"help\index.md"] = "help 2",
        }), name: "v2.exe");

        Assert.Equal(ExitCode.Ok, box.Run(v1, box.Switches(), version: "1.0.0").Code);
        Assert.Equal(ExitCode.Ok, box.Run(v2, box.Switches(), version: "2.0.0").Code);

        Assert.Equal("IDE 2", File.ReadAllText(Path.Combine(box.Home, "FreexComp.Ide.exe")));
        Assert.Equal("help 2", File.ReadAllText(Path.Combine(box.Home, "help", "index.md")));
        Assert.True(File.Exists(Path.Combine(box.Home, "rtl", "src", "New.pas")));
        Assert.False(File.Exists(Path.Combine(box.Home, "rtl", "src", "Old.pas")));
        Assert.Equal("2.0.0", box.Value(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreexComp", "DisplayVersion"));
        var record = InstallRecord.Read(box.Home)!;
        Assert.Equal("2.0.0", record.Version);
        Assert.Equal(["FreexComp.Ide.exe", @"help\index.md", "payload.sha256", @"rtl\src\New.pas", "uninstall.exe"],
            record.Files.Order(StringComparer.OrdinalIgnoreCase));

        // the .pproj default is still ours (and recorded as having had none), so uninstall removes it
        Assert.Equal("FreexComp.Project", box.Value(@"Software\Classes\.pproj", ""));
        Assert.Contains(new RegistryValueRecord(@"Software\Classes\.pproj", "", "FreexComp.Project", null), record.Values);
        Assert.Equal(ExitCode.Ok, box.Run(v2, ["/S", "/uninstall", "/D=" + box.Home]).Code);
        Assert.False(Directory.Exists(box.Home));
        Assert.Null(box.OpenKey(@"Software\Classes\.pproj"));
    }

    [Fact]
    [Trait("Req", "INS-007")]
    public void ForeignNonEmptyTargetIsRefused()
    {
        using var box = new SetupSandbox();
        var package = box.Package(box.Payload("v1", ManifestTests.SmallPayload));
        Directory.CreateDirectory(Path.Combine(box.Home, "work"));
        File.WriteAllText(Path.Combine(box.Home, "work", "notes.txt"), "not ours");

        var result = box.Run(package, box.Switches());
        Assert.Equal(new SetupResult(ExitCode.TargetNotEmpty, $"The folder {box.Home} is not empty and holds no FreexComp installation."), result);
        Assert.Equal(["work"], Directory.EnumerateFileSystemEntries(box.Home).Select(Path.GetFileName));
        Assert.Equal(["notes.txt"], Directory.EnumerateFileSystemEntries(Path.Combine(box.Home, "work")).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFileSystemEntries(box.StartMenu));
        Assert.False(box.RegistryRootExists());

        // an empty existing folder is fine
        Directory.Delete(box.Home, recursive: true);
        Directory.CreateDirectory(box.Home);
        Assert.Equal(ExitCode.Ok, box.Run(package, box.Switches()).Code);
    }
}
