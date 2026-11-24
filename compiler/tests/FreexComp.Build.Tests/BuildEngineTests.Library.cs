using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>dll-output.md §6 (task C-291; CMP-1080…1085): library projects in the build engine.</summary>
public sealed partial class BuildEngineTests
{
    private const string LibrarySource = """
        library Hello;
        function Add(A, B: Integer): Integer; stdcall;
        begin
          Result := A + B;
        end;
        exports Add;
        begin
        end.
        """;

    private const string Dll = @"C:\p\bin\Debug\Hello.dll";

    private static BuildWorkspace LibraryWorkspace(string extra = "") =>
        new(LibrarySource, BuildWorkspace.ProjectJson("Hello", extra: """ "appType": "library" """.Trim() + (extra.Length > 0 ? ", " + extra : "")));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1080")]
    [Trait("Req", "CMP-1081")]
    [Trait("Req", "CMP-1085")]
    public async Task Library_OutputsAndClean()
    {
        var w = LibraryWorkspace();

        var (result, log) = await w.RunAsync(w.Request());

        // CMP-1081: the DLL and its import library in the output directory; OutputPath = the DLL; the Info line names the DLL
        Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
        Assert.Equal(Dll, result.OutputPath);
        Assert.Equal(Dll, result.Projects[0].OutputPath);
        Assert.True(w.Fs.FileExists(Dll));
        Assert.True(w.Fs.FileExists(@"C:\p\bin\Debug\Hello.lib"));
        Assert.False(w.Fs.FileExists(@"C:\p\bin\Debug\Hello.exe"));
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.dll"], log.Info);
        Assert.Equal("EXPORTS\n    Add @1\n", w.Fs.ReadAllText(@"C:\p\obj\Debug\Hello.exports.def"));
        Assert.Contains("/dll", w.Fs.ReadAllText(@"C:\p\obj\Debug\link.rsp").Split('\n'));

        // up to date; CMP-1085: the import library or the DLL deleted → relink only
        (result, log) = await w.RunAsync(w.Request());
        Assert.True(result.UpToDate);
        Assert.Equal(["Hello: up to date"], log.Info);
        w.Fs.Delete(@"C:\p\bin\Debug\Hello.lib");
        (_, log) = await w.RunAsync(w.Request());
        Assert.Equal(["Linking Hello.dll"], log.Info);
        w.Fs.Delete(Dll);
        (_, log) = await w.RunAsync(w.Request());
        Assert.Equal(["Linking Hello.dll"], log.Info);

        // CMP-1085: an edited exports clause → the library file is recompiled, the definition file rewritten, the DLL relinked
        w.Fs.Add(BuildWorkspace.MainPath, LibrarySource.Replace("exports Add;", "exports Add name 'add_two';", StringComparison.Ordinal));
        (_, log) = await w.RunAsync(w.Request());
        Assert.Equal(["Compiling Hello", "Linking Hello.dll"], log.Info);
        Assert.Equal("EXPORTS\n    add_two @1\n", w.Fs.ReadAllText(@"C:\p\obj\Debug\Hello.exports.def"));

        // CMP-1081: Clean deletes the DLL, its import library, .pdb and .pdi - and nothing else of the output directory
        w.Fs.Add(@"C:\p\bin\Debug\Hello.pdb", "pdb");
        w.Fs.Add(@"C:\p\bin\Debug\Hello.pdi", "pdi");
        w.Fs.Add(@"C:\p\bin\Debug\Other.lib", "keep");
        (result, _) = await w.RunAsync(w.Request(BuildTarget.Clean));
        Assert.True(result.Success);
        Assert.Equal([@"C:\p\bin\Debug\Other.lib"], w.Fs.EnumerateFiles(@"C:\p\bin\Debug", "*"));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
    }

    /// <summary>CMP-1080, CMP-1060: the project's appType and the kind of its main source must agree — E0012, like any compile error.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1080")]
    [InlineData("library", BuildWorkspace.HelloProgram, "program")]
    [InlineData("console", LibrarySource, "library")]
    [InlineData("gui", LibrarySource, "library")]
    public async Task Library_AppTypeMismatch_E0012(string appType, string source, string kind)
    {
        var w = new BuildWorkspace(source, BuildWorkspace.ProjectJson("Hello", extra: $"\"appType\": \"{appType}\""));

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(
            [$@"C:\p\Hello.pas(0,0): error E0012: Application type '{appType}' does not match the main source: 'Hello.pas' is a {kind} file"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.Empty(w.Tools.Calls);
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
    }

    /// <summary>CMP-810 as changed by CMP-1080: E9008 remains for <c>package</c>.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1080")]
    public async Task Library_PackageAppTypeIsStillE9008()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "appType": "package" """.Trim()));

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(["E9008"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1083")]
    public async Task Library_RunWithoutHost_E9014()
    {
        var w = LibraryWorkspace();

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Run));

        // the library is built; the run is not attempted
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E9014: Cannot run library project 'Hello': no host application is set"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.True(w.Fs.FileExists(Dll));
        Assert.Contains("Linking Hello.dll", log.Info);
        Assert.Empty(w.Tools.Programs);
        Assert.Null(result.RunExitCode);
        Assert.False(result.Success);
        Assert.DoesNotContain(log.Info, l => l.StartsWith("Program exited", StringComparison.Ordinal));

        // an empty member is "not set"
        var empty = LibraryWorkspace(""" "run": { "arguments": "", "workingDir": "", "hostApplication": "" } """.Trim());
        (_, log) = await empty.RunAsync(empty.Request(BuildTarget.Run));
        Assert.Equal(["E9014"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1083")]
    public async Task Library_RunStartsHostApplication()
    {
        var w = LibraryWorkspace(""" "run": { "arguments": "one \"two words\"", "workingDir": "", "hostApplication": "host/Host.exe" } """.Trim());
        w.Fs.Add(@"C:\p\host\Host.exe", "host image");
        w.Tools.Program = (_, _, _) => (["host says hi"], [], 5);

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Run));

        // the host application (relative to the project directory), not the DLL; run.arguments; empty workingDir → the host's directory
        Assert.True(result.Success, string.Join("\n", log.Lines));
        var program = Assert.Single(w.Tools.Programs);
        Assert.Equal(@"C:\p\host\Host.exe", program.FileName);
        Assert.Equal(["one", "two words"], program.Arguments);
        Assert.Equal(@"C:\p\host", program.WorkingDirectory);
        Assert.Equal(5, result.RunExitCode);
        Assert.Equal(Dll, result.OutputPath);
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.dll", "Program exited with code 5"], log.Info);
        Assert.Contains("programstdout host says hi", log.Lines);

        // run.workingDir (relative to the project) and RunArguments win as for an executable; an absolute host path is used as is
        var other = LibraryWorkspace(""" "run": { "arguments": "x", "workingDir": "data", "hostApplication": "D:/tools/Host.exe" } """.Trim());
        other.Fs.Add(@"D:\tools\Host.exe", "host image");
        await other.RunAsync(other.Request(BuildTarget.Run) with { RunArguments = "override" });
        program = Assert.Single(other.Tools.Programs);
        Assert.Equal((@"D:\tools\Host.exe", @"C:\p\data"), (program.FileName, program.WorkingDirectory));
        Assert.Equal(["override"], program.Arguments);

        // host file missing → E9009 with the host path
        var missing = LibraryWorkspace(""" "run": { "hostApplication": "host/Gone.exe" } """.Trim());
        (result, log) = await missing.RunAsync(missing.Request(BuildTarget.Run));
        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E9009: Cannot run: project output 'C:\p\host\Gone.exe' does not exist"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.Empty(missing.Tools.Programs);

        // for a project that is not a library the member is ignored: its own exe runs
        var app = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "run": { "hostApplication": "host/Host.exe" } """.Trim()));
        await app.RunAsync(app.Request(BuildTarget.Run));
        Assert.Equal(BuildWorkspace.Exe, Assert.Single(app.Tools.Programs).FileName);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1082")]
    [Trait("Req", "CMP-1084")]
    public async Task LibraryDependency_AddsLibraryPathAndDeploysDll()
    {
        const string app = "program App;\nfunction Add(A, B: Integer): Integer; stdcall; external 'MathLib.dll';\nbegin\n  Writeln(Add(1, 2));\nend.\n";
        var w = new BuildWorkspace();
        w.Fs.Add(@"C:\g\MathLib\MathLib.pproj", BuildWorkspace.ProjectJson("MathLib", "MathLib.pas", """ "appType": "library" """.Trim()));
        w.Fs.Add(@"C:\g\MathLib\MathLib.pas", LibrarySource.Replace("library Hello", "library MathLib", StringComparison.Ordinal));
        w.Fs.Add(@"C:\g\App\App.pproj", BuildWorkspace.ProjectJson("App", "App.pas",
            """ "libraryPaths": ["libs", "D:/sdk/lib"], "dependencies": ["../MathLib/MathLib.pproj"] """.Trim()));
        w.Fs.Add(@"C:\g\App\App.pas", app);
        var request = w.Request(path: @"C:\g\App\App.pproj");

        var (result, log) = await w.RunAsync(request);

        // the dependency is built first; (a) its DLL directory is a library path of the dependant, after the project's own:
        // the import library goes to the linker from there, and no definition file was needed
        Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
        Assert.Equal([@"C:\g\MathLib\MathLib.pproj", @"C:\g\App\App.pproj"], result.Projects.Select(p => p.ProjectPath));
        Assert.Equal(@"C:\g\MathLib\bin\Debug\MathLib.dll", result.Projects[0].OutputPath);
        // (the DLL name of an external declaration is case-insensitive: the file name is spelled as the lookup spelled it)
        Assert.Contains(@"C:\g\MathLib\bin\Debug\MathLib.lib", w.Fs.ReadAllText(@"C:\g\App\obj\Debug\link.rsp").Split('\n'), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            [@"C:\g\App\libs", @"D:\sdk\lib"],
            ProjectOptions.ToCompilationOptions(ProjectSerializer.LoadProject(@"C:\g\App\App.pproj", w.Fs).Value!, "Debug").LibraryPaths);

        // (b) after the link the DLL is copied next to the exe - with its .pdi (the Debug configuration has debug info) - and recorded
        Assert.Equal(w.Fs.ReadAllBytes(@"C:\g\MathLib\bin\Debug\MathLib.dll"), w.Fs.ReadAllBytes(@"C:\g\App\bin\Debug\MathLib.dll"));
        Assert.Equal(w.Fs.ReadAllBytes(@"C:\g\MathLib\bin\Debug\MathLib.pdi"), w.Fs.ReadAllBytes(@"C:\g\App\bin\Debug\MathLib.pdi"));
        Assert.Equal("MathLib.dll\nMathLib.pdi\n", w.Fs.ReadAllText(@"C:\g\App\obj\Debug\deployed.txt"));
        Assert.False(w.Fs.FileExists(@"C:\g\App\bin\Debug\MathLib.lib"), "the import library is not deployed");
        Assert.Equal(["Linking App.exe", "Deploying MathLib.dll", "Deploying MathLib.pdi"], log.Info[^3..]);

        // nothing changed: both up to date, nothing is copied again
        var copied = w.Fs.GetLastWriteTimeUtc(@"C:\g\App\bin\Debug\MathLib.dll");
        (result, log) = await w.RunAsync(request);
        Assert.True(result.UpToDate);
        Assert.Equal(["MathLib: up to date", "App: up to date"], log.Info);
        Assert.Equal(copied, w.Fs.GetLastWriteTimeUtc(@"C:\g\App\bin\Debug\MathLib.dll"));

        // the dependency's DLL changed (and a .pdb appeared next to it): deployed again although the dependant itself is up to date;
        // the unchanged .pdi is not copied again
        w.Fs.Add(@"C:\g\MathLib\bin\Debug\MathLib.dll", "rebuilt dll");
        w.Fs.Add(@"C:\g\MathLib\bin\Debug\MathLib.pdb", "pdb");
        (result, log) = await w.RunAsync(request);
        Assert.True(result.Success);
        Assert.False(result.UpToDate);
        Assert.Equal(["MathLib: up to date", "Deploying MathLib.dll", "Deploying MathLib.pdb"], log.Info);
        Assert.Equal("rebuilt dll", w.Fs.ReadAllText(@"C:\g\App\bin\Debug\MathLib.dll"));
        Assert.Equal("MathLib.dll\nMathLib.pdb\nMathLib.pdi\n", w.Fs.ReadAllText(@"C:\g\App\obj\Debug\deployed.txt"));

        // CMP-1085 / CMP-1071: the import library of the dependency changed → the dependant relinks, nothing is recompiled
        w.Fs.Add(@"C:\g\MathLib\bin\Debug\MathLib.lib", "another import library");
        (_, log) = await w.RunAsync(request);
        Assert.Equal(["MathLib: up to date", "Linking App.exe"], log.Info);

        // a deployed file that is in use → E4008 with its path
        w.Fs.Add(@"C:\g\MathLib\bin\Debug\MathLib.dll", "rebuilt again");
        (result, log) = await w.RunAsync(request, fs: new InUseFileSystem(w.Fs, @"C:\g\App\bin\Debug\MathLib.dll"));
        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\g\App\App.pproj(0,0): error E4008: Cannot write output file 'C:\g\App\bin\Debug\MathLib.dll': the file is in use by another process"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));

        // Clean of the dependant removes exactly the deployed files besides its own outputs
        w.Fs.Add(@"C:\g\App\bin\Debug\Other.dll", "keep");
        (result, _) = await w.RunAsync(w.Request(BuildTarget.Clean, @"C:\g\App\App.pproj"));
        Assert.True(result.Success);
        Assert.Equal([@"C:\g\App\bin\Debug\Other.dll"], w.Fs.EnumerateFiles(@"C:\g\App\bin\Debug", "*"));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\g\MathLib\bin\Debug", "*"));          // the dependency was cleaned as well (its own outputs)
    }

    /// <summary>Replacing <paramref name="path"/> fails as it does for a DLL a running process has loaded.</summary>
    private sealed class InUseFileSystem(IFileSystem inner, string path) : IFileSystem
    {
        public bool FileExists(string file) => inner.FileExists(file);

        public string ReadAllText(string file) => inner.ReadAllText(file);

        public byte[] ReadAllBytes(string file) => inner.ReadAllBytes(file);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string file) => inner.GetLastWriteTimeUtc(file);

        public long GetLength(string file) => inner.GetLength(file);

        public void WriteAllBytes(string file, byte[] data)
        {
            if (string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"The process cannot access the file '{file}' because it is being used by another process.");
            }

            inner.WriteAllBytes(file, data);
        }

        public void Delete(string file) => inner.Delete(file);

        public void CreateDirectory(string directory) => inner.CreateDirectory(directory);
    }
}
