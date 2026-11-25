using System.Diagnostics;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Services;

/// <summary>I-257: the user help pages of the repository's <c>help</c> folder are shipped next to the IDE.</summary>
[Trait("Phase", "8")]
public sealed class HelpServiceTests
{
    /// <summary>The output directories that get the pages: the IDE's and this project's (the directory the IDE of the UI tests runs from).</summary>
    private static (string Source, string[] Outputs) Deployment()
    {
        var root = SpecTables.RepoRootPath();

        // <this project>/bin/<configuration>/<framework>/ -> the same place of the IDE project.
        var own = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var ideOutput = Path.Combine(root, "ide", "src", "FreexComp.Ide", "bin",
            Path.GetFileName(Path.GetDirectoryName(own)!), Path.GetFileName(own));
        Assert.True(File.Exists(Path.Combine(ideOutput, "FreexComp.Ide.exe")), ideOutput + ": not the output directory of the IDE");
        return (Path.Combine(root, "help"), [ideOutput, own]);
    }

    /// <summary>The files below a directory, relative, sorted; none when it does not exist.</summary>
    private static List<string> FilesBelow(string directory) => !Directory.Exists(directory) ? [] :
    [
        .. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f)).Order(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// Building the IDE copies every <c>help/**/*.md</c> to <c>help</c> below its output directory, and to the output of
    /// a project that references it (this one: the directory the IDE of the UI tests runs from), byte for byte.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-031")]
    public void HelpPagesAreDeployed()
    {
        var (source, outputs) = Deployment();
        var pages = Directory.EnumerateFiles(source, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(source, f)).ToList();
        Assert.True(pages.Count >= 3, "the repository has help pages");

        foreach (var output in outputs)
        {
            foreach (var page in pages)
            {
                var deployed = Path.Combine(output, "help", page);
                Assert.True(File.Exists(deployed), deployed + " is missing");
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, page)), File.ReadAllBytes(deployed));
            }
        }
    }

    /// <summary>
    /// UI-MENU-033: the deployed <c>help</c> folder holds exactly the source pages. (1) The outputs of this build: the
    /// same relative paths and bytes, nothing else. (2) The rule that keeps it so — the targets file the IDE project and
    /// this project import — run on a deployed folder that has a removed page, a renamed page's old file, a page of a
    /// removed subfolder and a file that never was a page: after the copy step of a build, and of a publish, only the
    /// source pages are left (their content untouched) and the emptied subfolder is gone.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-033")]
    public async Task DeployedPagesMatchSource()
    {
        var (source, outputs) = Deployment();
        var pages = FilesBelow(source);
        Assert.True(pages.Count >= 3, "the repository has help pages");
        Assert.All(pages, p => Assert.EndsWith(".md", p, StringComparison.Ordinal));
        foreach (var output in outputs)
        {
            Assert.Equal(pages, FilesBelow(Path.Combine(output, "help")));
            foreach (var page in pages)
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, page)), File.ReadAllBytes(Path.Combine(output, "help", page)));
        }

        var targets = Path.Combine(SpecTables.RepoRootPath(), "ide", "src", "FreexComp.Ide", "HelpPages.targets");
        Assert.True(File.Exists(targets), targets + " is missing");
        foreach (var project in (string[])["src/FreexComp.Ide/FreexComp.Ide.csproj", "tests/FreexComp.Ide.Tests/FreexComp.Ide.Tests.csproj"])
        {
            var text = File.ReadAllText(Path.Combine(SpecTables.RepoRootPath(), "ide", project));
            Assert.Contains("HelpPages.targets\" />", text, StringComparison.Ordinal);
        }

        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var pagesDir = Path.Combine(work, "pages");
            Write(Path.Combine(pagesDir, "kept.md"), "# Kept\n");
            Write(Path.Combine(pagesDir, "renamed-to.md"), "# Renamed\n");
            Write(Path.Combine(pagesDir, "sub", "inner.md"), "# Inner\n");
            foreach (var (step, property) in ((string, string)[])[("CopyFilesToOutputDirectory", "OutDir"), ("CopyFilesToPublishDirectory", "PublishDir")])
            {
                // What an earlier build left: the pages of then (one with older content — the copy step, not this rule,
                // refreshes it), and what has no source any more.
                var deployed = Path.Combine(work, property, "help");
                Write(Path.Combine(deployed, "kept.md"), "# Kept, as deployed\n");
                Write(Path.Combine(deployed, "sub", "inner.md"), "# Inner\n");
                Write(Path.Combine(deployed, "removed.md"), "# Removed\n");
                Write(Path.Combine(deployed, "renamed-from.md"), "# Renamed\n");
                Write(Path.Combine(deployed, "gone", "deep", "page.md"), "# Gone\n");
                Write(Path.Combine(deployed, "notes.txt"), "not a page\n");
                Write(Path.Combine(work, property, "help.md"), "next to the folder, not in it\n");
                Write(Path.Combine(work, property, "other", "removed.md"), "another folder\n");

                var projectFile = Path.Combine(work, property + ".proj");
                File.WriteAllText(projectFile, $"""
                    <Project>
                      <PropertyGroup>
                        <{property}>{Path.Combine(work, property)}\</{property}>
                        <HelpPagesSourceDir>{pagesDir}\</HelpPagesSourceDir>
                      </PropertyGroup>
                      <Target Name="{step}" />
                      <Import Project="{targets}" />
                    </Project>
                    """);
                var (exitCode, output) = await RunMSBuildAsync(projectFile, step);
                Assert.True(exitCode == 0, output);

                Assert.Equal(["kept.md", @"sub\inner.md"], FilesBelow(deployed));
                Assert.Equal("# Kept, as deployed\n", File.ReadAllText(Path.Combine(deployed, "kept.md")));
                Assert.False(Directory.Exists(Path.Combine(deployed, "gone")), "the emptied subfolder is removed");
                Assert.Equal(["help.md", @"help\kept.md", @"help\sub\inner.md", @"other\removed.md"], FilesBelow(Path.Combine(work, property)));
                Assert.Equal(["kept.md", "renamed-to.md", @"sub\inner.md"], FilesBelow(pagesDir)); // the source is only read

                // Nothing deployed (a first build, a project without the folder): nothing to do, no error.
                Directory.Delete(deployed, recursive: true);
                (exitCode, output) = await RunMSBuildAsync(projectFile, step);
                Assert.True(exitCode == 0, output);
                Assert.False(Directory.Exists(deployed));
            }
        }
        finally
        {
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Runs one target of a project with the build engine of the .NET SDK that built this test (no restore, no package,
    /// no network; no build node is left behind).
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunMSBuildAsync(string project, string target)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!,
        };
        foreach (var argument in (string[])["msbuild", project, "-t:" + target, "-nologo", "-nodeReuse:false", "-v:minimal"])
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(start)!;
        var output = ChildProcessOutput.Read(process); // I-262: dedicated threads, not the pool
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // the test's deadline: no build engine is left behind
            throw;
        }

        return (process.ExitCode, await output.StandardOutput + await output.StandardError);
    }
}
