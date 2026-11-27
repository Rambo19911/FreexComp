using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build.Tests;

/// <summary>
/// resource-manager.md CMP-1428…1433: validation of the resource keys and the script the build generates from them (in-memory
/// workspace, fake tools; the pinned tools: <c>PbuildCliTests.ProjectResources_*</c>).
/// </summary>
[Trait("Phase", "8")]
public sealed class ProjectResourcesTests
{
    private const string Obj = @"C:\p\obj\Debug";
    private const string Script = Obj + @"\Hello.project.rc";

    private static BuildWorkspace Workspace(string keys)
    {
        var w = new BuildWorkspace(BuildWorkspace.HelloProgram, BuildWorkspace.ProjectJson("Hello", extra: keys));
        w.Fs.Add(BuildWorkspace.Llvm + @"\llvm-rc.exe", "");
        return w;
    }

    private static List<Diagnostic> Diagnostics(EventLog log) => [.. log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value)];

    private static string[] Rsp(BuildWorkspace w) => w.Fs.ReadAllText(Obj + @"\link.rsp").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static Project Load(string text)
    {
        var result = ProjectSerializer.ParseProject(@"C:\p\Hello.pproj", text);
        Assert.Empty(result.Diagnostics);
        return result.Value!;
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1428")]
    [Trait("Req", "CMP-1431")]
    public void Validation_E3120_E3123()
    {
        var longName = "N" + new string('x', 64);
        var text = """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "icon": "app.ico", """
            + """ "versionInfo": { "fileVersion": "1.2.3.4.5", "productVersion": "1.65536", "companyName": "C" }, "resources": [ """
            + """{ "path": "a.bin", "type": "DATA", "name": "A" }, """
            + """{ "path": "b.bin", "type": "RCDATA", "name": "1B" }, """
            + """{ "path": "c.bin", "type": "RCDATA", "name": "0" }, """
            + """{ "path": "d.bin", "type": "RCDATA", "name": "MyData" }, """
            + """{ "path": "e.bin", "type": "RCDATA", "name": "MYDATA" }, """
            + """{ "path": "f.ico", "type": "ICON", "name": "01" }, """
            + """{ "path": "g.bmp", "type": "BITMAP", "name": "MYDATA" }, """
            + """{ "path": "h.bin", "type": "RCDATA", "name": "65535" }, """
            + """{ "path": "i.ico", "type": "ICON", "name": "Named" }, """
            + """{ "path": "j.bin", "type": "rcdata", "name": "J" }, """
            + $$"""{ "path": "k.bin", "type": "RCDATA", "name": "{{longName}}" }, """
            + """{ "path": "l.bin", "type": "RCDATA", "name": "65536" } ] }""";
        int Col(string token) => text.IndexOf(token, StringComparison.Ordinal) + 1;

        var result = ProjectSerializer.ParseProject(@"C:\p\Hello.pproj", text);

        Assert.Null(result.Value);
        Assert.Equal(
            [
                $@"C:\p\Hello.pproj(1,{Col("\"1.2.3.4.5\"")}): error E3123: Version information: fileVersion '1.2.3.4.5' is not 1 to 4 numbers from 0 to 65535 separated by '.'",
                $@"C:\p\Hello.pproj(1,{Col("\"1.65536\"")}): error E3123: Version information: productVersion '1.65536' is not 1 to 4 numbers from 0 to 65535 separated by '.'",
                $@"C:\p\Hello.pproj(1,{Col("\"DATA\"")}): error E3120: Resource 'a.bin': type 'DATA' is not RCDATA, ICON, BITMAP or CURSOR",
                $@"C:\p\Hello.pproj(1,{Col("\"1B\"")}): error E3121: Resource 'b.bin': name '1B' is not an identifier of at most 64 characters or a number from 1 to 65535",
                $@"C:\p\Hello.pproj(1,{Col("\"0\"")}): error E3121: Resource 'c.bin': name '0' is not an identifier of at most 64 characters or a number from 1 to 65535",
                $@"C:\p\Hello.pproj(1,{Col("\"MYDATA\"")}): error E3122: Resource 'e.bin': RCDATA 'MYDATA' is already defined by 'd.bin'",
                $@"C:\p\Hello.pproj(1,{Col("\"01\"")}): error E3122: Resource 'f.ico': ICON '01' is already defined by 'app.ico'",
                $@"C:\p\Hello.pproj(1,{Col("\"rcdata\"")}): error E3120: Resource 'j.bin': type 'rcdata' is not RCDATA, ICON, BITMAP or CURSOR",
                $@"C:\p\Hello.pproj(1,{Col("\"" + longName)}): error E3121: Resource 'k.bin': name '{longName}' is not an identifier of at most 64 characters or a number from 1 to 65535",
                $@"C:\p\Hello.pproj(1,{Col("\"65536\"")}): error E3121: Resource 'l.bin': name '65536' is not an identifier of at most 64 characters or a number from 1 to 65535",
            ],
            result.Diagnostics.Select(d => d.ToText()));

        // without `icon` an ICON entry may take id 1
        Load("""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "resources": [ { "path": "f.ico", "type": "ICON", "name": "1" } ] }""");
    }

    [Theory]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1428")]
    [InlineData("1", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("65535.0.65535", true)]
    [InlineData("01.002", true)]
    [InlineData("", false)]
    [InlineData("1.", false)]
    [InlineData(".1", false)]
    [InlineData("1..2", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("65536", false)]
    [InlineData("000065535", false)]
    [InlineData("-1", false)]
    [InlineData("+1", false)]
    [InlineData(" 1", false)]
    [InlineData("1.a", false)]
    [InlineData("1,2", false)]
    public void VersionForm(string version, bool valid) => Assert.Equal(valid, ProjectResources.IsValidVersion(version));

    [Theory]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1428")]
    [InlineData("MYDATA", true)]
    [InlineData("_a1", true)]
    [InlineData("1", true)]
    [InlineData("65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("", false)]
    [InlineData("A-B", false)]
    [InlineData("Ä", false)]
    [InlineData("1A", false)]
    public void NameForm(string name, bool valid) => Assert.Equal(valid, ProjectResources.IsValidName(name));

    private const string GoldenKeys = """
        "appType": "gui",
        "icon": "res/app.ico",
        "versionInfo": { "fileVersion": "1.2", "companyName": "Example \"Co\" \\ Ltd", "legalCopyright": "© 2026",
                         "comments": "line 1\nline 2\ttab\u0001" },
        "resources": [ { "path": "data/my data ü.txt", "type": "RCDATA", "name": "MyData" },
                       { "path": "C:/shared/second.ico", "type": "ICON", "name": "002" } ],
        "configurations": {
          "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" },
          "Release": { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "outputDir": "bin/Release" }
        }
        """;

    private static string Golden(string fileFlags) => $$"""
        // Generated from Hello.pproj by the build; changes are overwritten.
        1 ICON "C:\\p\\res\\app.ico"
        #undef MYDATA
        MYDATA RCDATA "C:\\p\\data\\my data ü.txt"
        2 ICON "C:\\shared\\second.ico"
        1 VERSIONINFO
        FILEVERSION 1,2,0,0
        PRODUCTVERSION 1,2,0,0
        FILEFLAGSMASK 0x3F
        FILEFLAGS {{fileFlags}}
        FILEOS 0x40004
        FILETYPE 1
        BEGIN
          BLOCK "StringFileInfo"
          BEGIN
            BLOCK "040904B0"
            BEGIN
              VALUE "CompanyName", "Example ""Co"" \\ Ltd"
              VALUE "FileDescription", ""
              VALUE "FileVersion", "1.2.0.0"
              VALUE "InternalName", "Hello"
              VALUE "LegalCopyright", "© 2026"
              VALUE "OriginalFilename", "Hello.exe"
              VALUE "ProductName", ""
              VALUE "ProductVersion", "1.2.0.0"
              VALUE "Comments", "line 1\nline 2\ttab"
            END
          END
          BLOCK "VarFileInfo"
          BEGIN
            VALUE "Translation", 0x409, 1200
          END
        END

        """;

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1429")]
    [Trait("Req", "CMP-1430")]
    public void GeneratedScript_Golden()
    {
        var project = Load(BuildWorkspace.ProjectJson("Hello", extra: GoldenKeys));

        Assert.Equal(Golden("0x1").ReplaceLineEndings("\n"), ProjectResources.RenderScript(project, debugInfo: true, "Hello.exe", missing: []));
        Assert.Equal(Golden("0").ReplaceLineEndings("\n"), ProjectResources.RenderScript(project, debugInfo: false, "Hello.exe", missing: []));

        // a library: FILETYPE 2; product version of its own; a missing file is left out
        var library = project with
        {
            AppType = "library",
            VersionInfo = project.VersionInfo! with { ProductVersion = "3" },
        };
        var script = ProjectResources.RenderScript(library, debugInfo: false, "Hello.dll", missing: [@"C:\p\data\my data ü.txt"])!;
        Assert.Contains("FILETYPE 2\n", script, StringComparison.Ordinal);
        Assert.Contains("PRODUCTVERSION 3,0,0,0\n", script, StringComparison.Ordinal);
        Assert.Contains("VALUE \"ProductVersion\", \"3.0.0.0\"\n", script, StringComparison.Ordinal);
        Assert.Contains("VALUE \"OriginalFilename\", \"Hello.dll\"\n", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MYDATA", script, StringComparison.Ordinal);

        // only version information: no icon line; absent file version = 1.0.0.0
        var versionOnly = ProjectResources.RenderScript(project with { Icon = null, Resources = [], VersionInfo = new ProjectVersionInfo() }, false, "Hello.exe", [])!;
        Assert.StartsWith("// Generated from Hello.pproj by the build; changes are overwritten.\n1 VERSIONINFO\nFILEVERSION 1,0,0,0\nPRODUCTVERSION 1,0,0,0\n", versionOnly, StringComparison.Ordinal);

        // nothing to generate
        Assert.Null(ProjectResources.RenderScript(project with { Icon = null, Resources = [], VersionInfo = null }, false, "Hello.exe", []));
        Assert.Null(ProjectResources.RenderScript(project with { VersionInfo = null }, false, "Hello.exe", [@"C:\p\res\app.ico", @"C:\p\data\my data ü.txt", @"C:\shared\second.ico"]));
    }

    private const string BuildKeys = """
        "files": [ { "path": "Hello.pas" }, { "path": "user.rc" } ],
        "icon": "app.ico",
        "versionInfo": { "fileVersion": "1.0", "productName": "Hello" },
        "resources": [ { "path": "data.txt", "type": "RCDATA", "name": "MYDATA" } ]
        """;

    private static BuildWorkspace BuildWorkspaceWithKeys(string keys = BuildKeys)
    {
        var w = Workspace(keys);
        w.Fs.Add(@"C:\p\user.rc", "5 RCDATA BEGIN 1 END\n").Add(@"C:\p\app.ico", "icon").Add(@"C:\p\data.txt", "hello");
        return w;
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1429")]
    public async Task GeneratedScriptIsDeterministicAndNotRewritten()
    {
        var w = BuildWorkspaceWithKeys();

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));
        var bytes = w.Fs.ReadAllBytes(Script);
        Assert.Equal(
            ProjectResources.RenderScript(Load(w.Fs.ReadAllText(BuildWorkspace.ProjectPath)), debugInfo: true, "Hello.exe", missing: []),
            new System.Text.UTF8Encoding(false, true).GetString(bytes));
        Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
        Assert.DoesNotContain((byte)'\r', bytes);

        // the first resource input: before the project's own scripts; compiled like them, but not announced (verbose only)
        Assert.Equal(
            [("clang.exe", Script), ("llvm-rc.exe", Obj + @"\Hello.project.rc.i"), ("clang.exe", @"C:\p\user.rc"), ("llvm-rc.exe", Obj + @"\user.rc.i")],
            w.Tools.ResourceCalls.Select(c => (c.Tool, c.Arguments[^1])));
        var rsp = Rsp(w);
        Assert.Equal(Array.IndexOf(rsp, Obj + @"\Hello.project.res") + 1, Array.IndexOf(rsp, Obj + @"\user.res"));
        Assert.DoesNotContain(log.Info, l => l.Contains("Hello.project", StringComparison.Ordinal));
        Assert.Contains("Compiling resources user.rc", log.Info);
        Assert.Empty(Diagnostics(log));

        // same inputs: the script is not written again, nothing is compiled or linked
        var time = w.Fs.GetLastWriteTimeUtc(Script);
        var calls = w.Tools.Calls;
        var (again, againLog) = await w.RunAsync(w.Request());
        Assert.True(again.Success && again.UpToDate, string.Join("\n", againLog.Lines));
        Assert.Equal(time, w.Fs.GetLastWriteTimeUtc(Script));
        Assert.Equal(calls, w.Tools.Calls);

        // a changed data file recompiles the generated script and relinks (CMP-1317, 1318)
        w.Fs.Add(@"C:\p\data.txt", "changed");
        var (changed, changedLog) = await w.RunAsync(w.Request());
        Assert.True(changed.Success && !changed.UpToDate, string.Join("\n", changedLog.Lines));
        Assert.Equal(time, w.Fs.GetLastWriteTimeUtc(Script));
        Assert.Equal(["clang.exe", "llvm-rc.exe", "lld-link.exe"], w.Tools.Calls[calls.Length..]);

        // verbose output names it
        w.Fs.Add(@"C:\p\data.txt", "changed again");
        var (_, verboseLog) = await w.RunAsync(w.Request() with { Verbose = true });
        Assert.Contains("Compiling resources Hello.project.rc", verboseLog.Info);

        // Release: FILEFLAGS changes, its own object directory
        var (release, releaseLog) = await w.RunAsync(w.Request() with { Configuration = "Release" });
        Assert.True(release.Success, string.Join("\n", releaseLog.Lines));
        Assert.Contains("FILEFLAGS 0\n", w.Fs.ReadAllText(@"C:\p\obj\Release\Hello.project.rc"), StringComparison.Ordinal);
        Assert.Contains("FILEFLAGS 0x1\n", w.Fs.ReadAllText(Script), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1428")]
    public async Task MissingFile_W9002_EntryLeftOut()
    {
        var w = BuildWorkspaceWithKeys();
        w.Fs.Delete(@"C:\p\data.txt");
        w.Fs.Delete(@"C:\p\app.ico");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(
            [
                @"C:\p\Hello.pproj(0,0): warning W9002: File 'C:\p\app.ico' listed in the project does not exist",
                @"C:\p\Hello.pproj(0,0): warning W9002: File 'C:\p\data.txt' listed in the project does not exist",
            ],
            Diagnostics(log).Select(d => d.ToText()));
        var script = w.Fs.ReadAllText(Script);
        Assert.DoesNotContain("ICON", script, StringComparison.Ordinal);
        Assert.DoesNotContain("MYDATA", script, StringComparison.Ordinal);
        Assert.Contains("1 VERSIONINFO\n", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1433")]
    public async Task RemovingTheKeysDeletesTheGeneratedFiles()
    {
        var w = BuildWorkspaceWithKeys();
        var (first, firstLog) = await w.RunAsync(w.Request());
        Assert.True(first.Success, string.Join("\n", firstLog.Lines));
        Assert.True(w.Fs.FileExists(Obj + @"\Hello.project.res") && w.Fs.FileExists(Obj + @"\Hello.project.res.json"));

        // one entry less: the script changes, is compiled again, and the program is relinked
        var calls = w.Tools.Calls.Length;
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: BuildKeys.Replace(
            """{ "path": "data.txt", "type": "RCDATA", "name": "MYDATA" }""", "", StringComparison.Ordinal)));
        var (fewer, fewerLog) = await w.RunAsync(w.Request());
        Assert.True(fewer.Success, string.Join("\n", fewerLog.Lines));
        Assert.DoesNotContain("MYDATA", w.Fs.ReadAllText(Script), StringComparison.Ordinal);
        Assert.Equal(["clang.exe", "llvm-rc.exe", "lld-link.exe"], w.Tools.Calls[calls..]);

        // none left: the generated files are deleted and no longer linked
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """ "files": [ { "path": "Hello.pas" }, { "path": "user.rc" } ] """));
        var (none, noneLog) = await w.RunAsync(w.Request());
        Assert.True(none.Success && !none.UpToDate, string.Join("\n", noneLog.Lines));
        Assert.Empty(w.Fs.EnumerateFiles(Obj, "Hello.project.*"));
        Assert.DoesNotContain(Rsp(w), l => l.Contains("Hello.project", StringComparison.Ordinal));
        Assert.Contains(Obj + @"\user.res", Rsp(w));
        Assert.DoesNotContain("Hello.project", w.Fs.ReadAllText(Obj + @"\link.resources"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1429")]
    public async Task ErrorInTheGeneratedScriptIsLocatedAtTheProject()
    {
        // the fake resource tool fails on this text; a real error of the generated script "should not happen" (UI-DLG-335)
        var w = BuildWorkspaceWithKeys(BuildKeys.Replace("\"productName\": \"Hello\"", $"\"productName\": \"{FakeResourceTools.SyntaxError}\"", StringComparison.Ordinal));

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        var d = Assert.Single(Diagnostics(log));
        Assert.Equal(("E3112", Location.ForFile(BuildWorkspace.ProjectPath)), (d.Code, d.Location));
    }
}
