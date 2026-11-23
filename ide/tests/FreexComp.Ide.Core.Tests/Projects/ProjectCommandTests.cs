using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>Project-menu commands of Phase 3 that act on the project's files on disk (phase3-build-run.md §1).</summary>
[Trait("Phase", "3")]
public sealed class ProjectCommandTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeShellService _shell = new();
    private readonly IdeServices _services;

    public ProjectCommandTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Shell = _shell;
        });
    }

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    [Fact(Timeout = 30000)]
    public Task OpenOutputFolder() => TestUiThread.RunAsync(() => OpenOutputFolderOnUiThread()); // I-209

    private async Task OpenOutputFolderOnUiThread()
    {
        var commands = _services.Commands;
        Assert.False(commands.CanExecute("project.openOutputFolder")); // no project

        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "activeConfiguration": "Release",
              "configurations": {
                "Debug":   { "optimization": 0, "debugInfo": true,  "defines": [], "outputDir": "bin/Debug" },
                "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "out/rel" } } }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));

        // menus.md: enabled only while the active configuration's output folder exists.
        Assert.False(commands.CanExecute("project.openOutputFolder"));
        _fs.CreateDirectory(Dir + @"\out\rel");
        Assert.True(commands.CanExecute("project.openOutputFolder"));

        await commands.ExecuteAsync("project.openOutputFolder");
        Assert.Equal([@"OpenFolder C:\p\Hello\out\rel"], _shell.Calls);

        // The active configuration decides the folder.
        _fs.CreateDirectory(Dir + @"\bin\Debug");
        await commands.ExecuteAsync("project.setConfiguration", "Debug");
        await commands.ExecuteAsync("project.openOutputFolder");
        Assert.Equal(@"OpenFolder C:\p\Hello\bin\Debug", _shell.Calls[^1]);
    }
}
