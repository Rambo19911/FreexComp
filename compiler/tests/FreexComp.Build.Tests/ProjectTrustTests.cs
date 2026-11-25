using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-323: command hash, trust evaluation and the list of untrusted projects of a build (project-trust.md CMP-1233, 1235, 1237, 1238).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ProjectTrustTests
{
    private const string HashOther = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    private static Project Parse(string events, string path = @"C:\Work\App\App.pproj", string extra = "")
    {
        var text = $$"""{ "formatVersion": 1, "name": "App", "mainSource": "App.pas"{{(events.Length > 0 ? ", \"events\": " + events : "")}}{{(extra.Length > 0 ? ", " + extra : "")}} }""";
        var load = ProjectSerializer.ParseProject(path, text);
        Assert.Empty(load.Diagnostics);
        return load.Value!;
    }

    private static string Hash(string pre, string post) => ProjectTrust.CommandsHash(Parse($$"""{ "preBuild": {{pre}}, "postBuild": {{post}} }"""));

    private static TrustSnapshot Snapshot(params TrustEntry[] entries) => new([.. entries], null);

    private static TrustEntry Folder(string path) => new(TrustEntryKind.Folder, path, null);

    private static TrustEntry Entry(string path, string hash) => new(TrustEntryKind.Project, path, hash);

    [Fact]
    [Trait("Req", "CMP-1233")]
    public void CommandsHash_IsStableAndSensitive()
    {
        // sha256 of "freexcomp-commands-v1\npreBuild\n2\n7\necho hi\n14\ntool.exe \"a b\"\npostBuild\n1\n7\necho ž\n" (ž = 2 bytes), computed outside the product
        const string Expected = "sha256:f6fdbb941e621c207970df281c8a0cf84092378e2d98c5bbdefda8339cd60d2c";
        const string Pre = """["echo hi", "tool.exe \"a b\""]""";
        const string Post = """["echo \u017e"]""";

        Assert.Equal(Expected, Hash(Pre, Post));
        Assert.Equal("sha256:7610512f6041ca8b2bb42e55d4fdd4e39026ffaadd9cc6a2b37fd3a1ed56b002", ProjectTrust.CommandsHash(Parse("")));   // no commands

        Assert.NotEqual(Expected, Hash("""["echo ho", "tool.exe \"a b\""]""", Post));            // a changed command
        Assert.NotEqual(Expected, Hash("""["tool.exe \"a b\"", "echo hi"]""", Post));            // swapped order
        Assert.NotEqual(Expected, Hash("""["echo hi"]""", """["tool.exe \"a b\"", "echo \u017e"]"""));   // a command moved between the lists
        Assert.NotEqual(Expected, Hash("""["echo hi ", "tool.exe \"a b\""]""", Post));           // a trailing blank
        Assert.NotEqual(Expected, Hash("""["Echo hi", "tool.exe \"a b\""]""", Post));            // case
        Assert.NotEqual(Hash("""["ab", "c"]""", "[]"), Hash("""["a", "bc"]""", "[]"));           // the lengths separate the commands

        var other = Parse($$"""{ "preBuild": {{Pre}}, "postBuild": {{Post}} }""", @"D:\Elsewhere\Other.pproj",
            """ "defines": ["X"], "searchPaths": ["lib"], "run": { "arguments": "-v", "workingDirectory": "" } """);
        Assert.Equal(Expected, ProjectTrust.CommandsHash(other));                               // nothing else in the project file counts
    }

    [Fact]
    [Trait("Req", "CMP-1235")]
    [Trait("Req", "CMP-1237")]
    public void Evaluate_AllStates()
    {
        const string Path = @"C:\Work\App\App.pproj";
        var project = Parse("""{ "preBuild": ["echo hi"], "postBuild": [] }""");
        var hash = ProjectTrust.CommandsHash(project);
        var plain = Parse("");

        Assert.False(ProjectTrust.HasCommands(plain));
        Assert.False(ProjectTrust.HasCommands(Parse("""{ "preBuild": [], "postBuild": [] }""")));
        Assert.True(ProjectTrust.HasCommands(project));
        Assert.True(ProjectTrust.HasCommands(Parse("""{ "preBuild": [], "postBuild": ["x"] }""")));

        Assert.Equal(TrustState.NoCommands, ProjectTrust.Evaluate(plain, Path, Snapshot()));
        Assert.Equal(TrustState.NoCommands, ProjectTrust.Evaluate(plain, Path, Snapshot(Folder(@"C:\Work"))));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(project, Path, Snapshot()));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(project, Path, Snapshot(Entry(@"C:\Work\App\Other.pproj", hash))));

        // folders: the project's own directory, a parent, a drive root; component boundary; case
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\Work\App"))));
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\Work"))));
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\"))));
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, @"C:\App.pproj", Snapshot(Folder(@"C:\"))));
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, @"c:\WORK\app\APP.PPROJ", Snapshot(Folder(@"C:\work"))));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(project, @"C:\Workshop\App.pproj", Snapshot(Folder(@"C:\Work"))));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\Work\App\Sub"))));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"D:\"))));

        // project entries: hash equal, hash different, path case
        Assert.Equal(TrustState.TrustedProject, ProjectTrust.Evaluate(project, Path, Snapshot(Entry(Path, hash))));
        Assert.Equal(TrustState.TrustedProject, ProjectTrust.Evaluate(project, Path, Snapshot(Entry(@"c:\work\APP\app.pproj", hash))));
        Assert.Equal(TrustState.Changed, ProjectTrust.Evaluate(project, Path, Snapshot(Entry(Path, HashOther))));
        Assert.Equal(TrustState.Changed, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\Workshop"), Entry(Path, HashOther))));

        // a folder wins over a stale project hash
        Assert.Equal(TrustState.TrustedFolder, ProjectTrust.Evaluate(project, Path, Snapshot(Folder(@"C:\Work"), Entry(Path, HashOther))));
    }

    [Fact]
    [Trait("Req", "CMP-1238")]
    public void FindUntrusted_ListsProjectAndDependenciesInBuildOrder()
    {
        const string App = @"C:\g\App\App.pproj", Lib = @"C:\g\Lib\Lib.pproj", Tool = @"C:\g\Tool\Tool.pproj", Plain = @"C:\g\Plain\Plain.pproj";
        const string Group = @"C:\g\All.pgroup", TrustFile = @"C:\user\roaming\trust.json";
        var fs = new InMemoryFileSystem();
        static string Json(string name, string extra) => $$"""{ "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas", {{extra}} }""";
        fs.Add(App, Json("App", """ "dependencies": ["../Lib/Lib.pproj", "../Plain/Plain.pproj"], "events": { "preBuild": ["a1"], "postBuild": ["a2", "a3"] } """));
        fs.Add(Lib, Json("Lib", """ "events": { "preBuild": [], "postBuild": ["l1"] } """));
        fs.Add(Plain, Json("Plain", """ "dependencies": [] """));
        fs.Add(Tool, Json("Tool", """ "events": { "preBuild": ["t1"], "postBuild": [] } """));
        fs.Add(Group, """{ "formatVersion": 1, "projects": ["Tool/Tool.pproj", "App/App.pproj", "Lib/Lib.pproj", "Plain/Plain.pproj"], "activeProject": "App/App.pproj" }""");
        var store = new FileTrustStore(fs, TrustFile);

        // nothing trusted: the dependency first, the project last; a project without commands is never listed
        var all = ProjectTrust.FindUntrusted(fs, App, null, store);
        Assert.Equal([Lib, App], all.Select(u => u.ProjectPath));
        Assert.All(all, u => Assert.Equal(TrustState.Untrusted, u.State));
        Assert.Equal(["a1"], all[1].PreBuild);
        Assert.Equal(["a2", "a3"], all[1].PostBuild);
        Assert.Empty(all[0].PreBuild);
        Assert.Equal(["l1"], all[0].PostBuild);
        Assert.Equal([Lib, App], ProjectTrust.FindUntrusted(fs, App, null, null).Select(u => u.ProjectPath));     // no store = empty list
        Assert.False(fs.FileExists(TrustFile));

        // the whole group in its build order (the active project last); one member of it with its dependencies
        Assert.Equal([Tool, Lib, App], ProjectTrust.FindUntrusted(fs, Group, null, store).Select(u => u.ProjectPath));
        Assert.Equal([Lib, App], ProjectTrust.FindUntrusted(fs, Group, "App/App.pproj", store).Select(u => u.ProjectPath));
        Assert.Equal([Tool], ProjectTrust.FindUntrusted(fs, Group, "Tool/Tool.pproj", store).Select(u => u.ProjectPath));

        // trusted ones are omitted; a project trusted with other commands is listed as Changed
        var appProject = ProjectSerializer.LoadProject(App, fs).Value!;
        store.TrustProject(Lib, ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(Lib, fs).Value!));
        store.TrustProject(App, ProjectTrust.CommandsHash(appProject with { Events = new ProjectEvents(["other"], []) }));
        var changed = Assert.Single(ProjectTrust.FindUntrusted(fs, App, null, store));
        Assert.Equal(App, changed.ProjectPath);
        Assert.Equal(TrustState.Changed, changed.State);
        store.TrustFolder(@"C:\g");
        Assert.Empty(ProjectTrust.FindUntrusted(fs, Group, null, store));
        Assert.True(store.Remove(new TrustEntry(TrustEntryKind.Folder, @"C:\g", null)));

        // a project file that cannot be loaded is skipped (the build reports it); so is a missing group
        fs.Add(Lib, "{ not a project");
        Assert.Equal([Tool, App], ProjectTrust.FindUntrusted(fs, Group, null, store).Select(u => u.ProjectPath));
        Assert.Empty(ProjectTrust.FindUntrusted(fs, Lib, null, store));
        Assert.Empty(ProjectTrust.FindUntrusted(fs, @"C:\g\Missing.pgroup", null, store));
    }
}
