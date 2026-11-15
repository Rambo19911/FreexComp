namespace FreexComp.Build.Tests;

/// <summary>C-144: the <c>Clean</c> target (build-engine.md CMP-832).</summary>
[Trait("Phase", "3")]
public sealed class CleanTests
{
    [Fact]
    [Trait("Req", "CMP-832")]
    public async Task Clean_DeletesObjConfigRecursively_AndTheExeAndPdb_Only()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());
        w.Fs.Add(@"C:\p\obj\Debug\sub\deep.txt", "x");
        w.Fs.Add(@"C:\p\bin\Debug\Hello.pdb", "pdb");
        w.Fs.Add(@"C:\p\bin\Debug\data.txt", "keep");
        w.Fs.Add(@"C:\p\obj\Release\Hello.obj", "keep");
        w.Fs.Add(@"C:\p\obj\keep.txt", "keep");
        Assert.True(w.Fs.FileExists(@"C:\p\obj\Debug\Hello.obj"));
        var calls = w.Tools.Calls.Length;

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Clean));

        Assert.True(result.Success);
        Assert.Equal(["started Hello.pproj Debug win64 Clean", "finished Hello.pproj success=True upToDate=False 0/0/0"], log.Lines);
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\p\obj\Debug"));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(@"C:\p\bin\Debug\Hello.pdb"));
        Assert.True(w.Fs.FileExists(@"C:\p\bin\Debug\data.txt"));
        Assert.True(w.Fs.FileExists(@"C:\p\obj\Release\Hello.obj"));
        Assert.True(w.Fs.FileExists(@"C:\p\obj\keep.txt"));
        Assert.True(w.Fs.FileExists(BuildWorkspace.MainPath));
        Assert.Null(result.Projects[0].OutputPath);
        Assert.Equal(calls, w.Tools.Calls.Length);                                  // Clean starts no process
    }

    [Fact]
    [Trait("Req", "CMP-832")]
    public async Task Clean_NothingToDelete_IsNotAnError()
    {
        var w = new BuildWorkspace();

        var (result, _) = await w.RunAsync(w.Request(BuildTarget.Clean));

        Assert.True(result.Success);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    [Trait("Req", "CMP-832")]
    public async Task Clean_ObjectDirectoryOutsideTheProject_IsRefusedWithE9002()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra:
            """ "configurations": { "..\\..": { "optimization": 0, "debugInfo": false, "defines": [], "outputDir": "bin" } }, "activeConfiguration": "..\\.." """));
        w.Fs.Add(@"C:\other\file.txt", "keep");
        w.Fs.Add(@"C:\p\bin\Hello.exe", "keep");

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Clean));

        Assert.False(result.Success);
        Assert.Contains("diagnostic E9002", log.Lines);
        Assert.True(w.Fs.FileExists(@"C:\other\file.txt"));
        Assert.True(w.Fs.FileExists(@"C:\p\bin\Hello.exe"));
        Assert.True(w.Fs.FileExists(BuildWorkspace.MainPath));
        Assert.True(w.Fs.FileExists(BuildWorkspace.Rtl + @"\System.pas"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "CMP-832")]
    public async Task Clean_OnDisk_RemovesTheObjectDirectoryItself()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "obj", "Debug", "sub"));
        Directory.CreateDirectory(Path.Combine(dir, "bin", "Debug"));
        File.WriteAllText(Path.Combine(dir, "obj", "Debug", "sub", "x.obj"), "x");
        File.WriteAllText(Path.Combine(dir, "bin", "Debug", "Hello.exe"), "x");
        File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello"));
        File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);

        var result = await new BuildEngine(Compiler.PhysicalFileSystem.Instance)
            .RunAsync(new BuildRequest { ProjectPath = Path.Combine(dir, "Hello.pproj"), Target = BuildTarget.Clean }, new EventLog(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(Path.Combine(dir, "obj", "Debug")));
        Assert.True(Directory.Exists(Path.Combine(dir, "bin", "Debug")));
        Assert.False(File.Exists(Path.Combine(dir, "bin", "Debug", "Hello.exe")));
        Directory.Delete(dir, recursive: true);
    }
}
