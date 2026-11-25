using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>C-338: the cycle query for editors, <see cref="ProjectDependencies.FindCycle"/> (group-build.md CMP-1346…1348).</summary>
[Trait("Phase", "8")]
public sealed class ProjectDependenciesTests
{
    private static string PathOf(string name) => $@"C:\g\{name}\{name}.pproj";

    /// <summary>Adds <c>C:\g\&lt;name&gt;\&lt;name&gt;.pproj</c>; <paramref name="dependencies"/> are written as given.</summary>
    private static void AddProject(InMemoryFileSystem fs, string name, params string[] dependencies)
    {
        var deps = string.Join(", ", dependencies.Select(d => System.Text.Json.JsonSerializer.Serialize(d)));
        fs.Add(PathOf(name), BuildWorkspace.ProjectJson(name, name + ".pas", $""" "dependencies": [{deps}]"""));
    }

    /// <summary><c>B:C,D;C:</c> → project B depending on C and D, project C on nothing (member names → <c>../X/X.pproj</c>).</summary>
    private static InMemoryFileSystem Files(string projects)
    {
        var fs = new InMemoryFileSystem();
        foreach (var entry in projects.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            AddProject(fs, parts[0], [.. parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Relative)]);
        }

        return fs;
    }

    private static string Relative(string name) => $"../{name}/{name}.pproj";

    private static string? Names(IEnumerable<string>? cycle) => cycle is null ? null : string.Join(",", cycle.Select(p => Path.GetFileNameWithoutExtension(p)));

    [Theory]
    [Trait("Req", "CMP-1346")]
    [InlineData("A:;B:", "B", null)]                                   // no cycle
    [InlineData("A:;B:C;C:", "B,C", null)]                             // a diamond is not a cycle
    [InlineData("A:;B:A", "B", "A,B,A")]                               // direct
    [InlineData("A:;B:C;C:A", "B", "A,B,C,A")]                         // indirect
    [InlineData("A:;B:;C:D;D:A", "B,C", "A,C,D,A")]                    // through the second dependency
    [InlineData("A:", "A", "A,A")]                                     // the project depending on itself
    [InlineData("A:;B:", "B,Missing", null)]                           // a missing dependency file is a leaf
    [InlineData("A:;B:Missing", "B", null)]
    [InlineData("A:", "", null)]
    public void FindCycle_NoneDirectIndirectAndSelf(string projects, string dependencies, string? expected)
    {
        var fs = Files(projects);

        var cycle = ProjectDependencies.FindCycle(fs, PathOf("A"), dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Relative));

        Assert.Equal(expected, Names(cycle));
        if (cycle is { } path)
        {
            Assert.Equal(path[0], path[^1]);
            Assert.All(path, p => Assert.True(Path.IsPathFullyQualified(p) && p == PathUtil.Normalize(p), p));
        }
    }

    [Fact]
    [Trait("Req", "CMP-1346")]
    [Trait("Req", "CMP-1348")]
    public void FindCycle_InvalidFileIsALeaf_PathsAreComparedAsTheEngineDoes()
    {
        var fs = Files("A:;B:");
        fs.Add(PathOf("Bad"), "{ not json");
        Assert.Null(ProjectDependencies.FindCycle(fs, PathOf("A"), [Relative("Bad"), Relative("B")]));

        // absolute and relative spellings, '\' and '/', another case: one project
        AddProject(fs, "B", @"C:\g\X\..\a\A.PPROJ");
        var cycle = ProjectDependencies.FindCycle(fs, @"C:\g\A\sub\..\A.pproj", [@"..\B/B.pproj"]);

        Assert.Equal([PathOf("A"), PathOf("B"), PathOf("A")], cycle!.Value);
    }

    [Fact]
    [Trait("Req", "CMP-1346")]
    public void FindCycle_UsesTheArgumentForTheProjectItself()
    {
        // the file of A lists nothing; the argument closes the cycle
        var fs = Files("A:;B:A");
        Assert.Equal("A,B,A", Names(ProjectDependencies.FindCycle(fs, PathOf("A"), [Relative("B")])));
        Assert.Null(ProjectDependencies.FindCycle(fs, PathOf("A"), []));

        // the reverse: the file of A closes a cycle, the argument removes it
        var saved = Files("A:B;B:A;C:");
        Assert.Null(ProjectDependencies.FindCycle(saved, PathOf("A"), [Relative("C")]));
        Assert.Null(ProjectDependencies.FindCycle(saved, PathOf("A"), []));
        Assert.Equal("A,B,A", Names(ProjectDependencies.FindCycle(saved, PathOf("A"), [Relative("C"), Relative("B")])));

        // other projects are read from the file system: seen from B the saved file of A counts
        Assert.Equal("B,A,B", Names(ProjectDependencies.FindCycle(saved, PathOf("B"), [Relative("A")])));
        Assert.Null(ProjectDependencies.FindCycle(saved, PathOf("B"), []));

        // a project that is not saved yet: only the argument
        var unsaved = Files("B:New");
        Assert.Equal("New,B,New", Names(ProjectDependencies.FindCycle(unsaved, PathOf("New"), [Relative("B")])));
    }

    [Fact]
    [Trait("Req", "CMP-1347")]
    public void FindCycle_EqualsThePathOfE9006()
    {
        var w = new BuildWorkspace();
        foreach (var name in new[] { "A", "B", "C" })
        {
            w.Fs.Add($@"C:\g\{name}\{name}.pas", $"program {name}; begin end.");
        }

        AddProject(w.Fs, "A", Relative("B"));
        AddProject(w.Fs, "B", Relative("C"));
        AddProject(w.Fs, "C");
        Assert.Null(ProjectDependencies.FindCycle(w.Fs, PathOf("A"), [Relative("B")]));

        // the editor asks before C -> A is saved …
        Assert.Equal("C,A,B,C", Names(ProjectDependencies.FindCycle(w.Fs, PathOf("C"), [Relative("A")])));
        Assert.Empty(w.Tools.Calls);                                                     // no process

        // … and once it is saved, the build of A reports the same path
        AddProject(w.Fs, "C", Relative("A"));
        var cycle = ProjectDependencies.FindCycle(w.Fs, PathOf("A"), [Relative("B")]);
        var stdout = new StringWriter { NewLine = "\n" };
        var env = new PbuildEnvironment(@"C:\g\A", stdout, new StringWriter(), w.Fs)
        {
            ProcessRunner = w.Tools,
            CommandRunner = w.Commands,
            ToolchainDirectory = BuildWorkspace.Llvm,
            RtlDirectory = BuildWorkspace.Rtl,
        };

        var code = PbuildCommand.Run(["A.pproj"], env, TestContext.Current.CancellationToken);

        Assert.Equal(1, code);
        const string expected = @"C:\g\A\A.pproj -> C:\g\B\B.pproj -> C:\g\C\C.pproj -> C:\g\A\A.pproj";
        Assert.Equal(expected, string.Join(" -> ", cycle!.Value));
        Assert.Equal(@"C:\g\A\A.pproj(0,0): error E9006: Circular project dependency: " + string.Join(" -> ", cycle.Value), stdout.ToString().Split('\n')[0]);
        Assert.Empty(w.Tools.Calls);
    }
}
