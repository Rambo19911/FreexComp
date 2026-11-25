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

    // ---- build-events.md CMP-1253…1255 (task C-327): the command lists of configurations

    private const string WithConfigurationEvents = """
        "configurations": {
          "Debug": { "optimization": 0 },
          "Release": { "optimization": 2, "events": { "preBuild": [], "postBuild": ["echo \u017e"], "postBuildWhen": "always" } },
          "Quiet": { "inherits": "Release", "events": {} }
        }
        """;

    private static string Describe(CommandList list) =>
        $"{list.Name} | {list.Configuration ?? "-"} | {(list.PostBuild ? "post" : "pre")} | {string.Join(" ; ", list.Commands)}";

    [Fact]
    [Trait("Req", "CMP-1253")]
    public void CommandLists_FixedOrderAndNames()
    {
        var project = Parse("""{ "preBuild": ["echo hi"], "postBuild": [] }""", extra: WithConfigurationEvents);

        // project level first (both lists, also the empty one), then per configuration that has an events key, in file order
        Assert.Equal(
            ["preBuild | - | pre | echo hi",
             "postBuild | - | post | ",
             "configurations/Release/preBuild | Release | pre | ",
             "configurations/Release/postBuild | Release | post | echo \u017e",
             "configurations/Quiet/preBuild | Quiet | pre | ",
             "configurations/Quiet/postBuild | Quiet | post | "],
            ProjectTrust.CommandLists(project).Select(Describe));

        // a project without any events key: the two project-level lists, empty
        Assert.Equal(["preBuild | - | pre | ", "postBuild | - | post | "], ProjectTrust.CommandLists(Parse("")).Select(Describe));
        Assert.Equal(
            ["preBuild | - | pre | ", "postBuild | - | post | "],
            ProjectTrust.CommandLists(Parse("", extra: """ "configurations": { "Debug": {}, "Demo": { "inherits": "Debug" } } """)).Select(Describe));
    }

    [Fact]
    [Trait("Req", "CMP-1253")]
    [Trait("Req", "CMP-1254")]
    public void CommandsHash_CoversConfigurationLists()
    {
        static string Configurations(string release, string name = "Release", string more = "") =>
            $$""" "configurations": { "Debug": { "optimization": 0 }, "{{name}}": { "optimization": 2{{release}} }{{more}} } """;
        const string Events = """{ "preBuild": ["echo hi"], "postBuild": [] }""";
        const string ReleaseEvents = """, "events": { "preBuild": [], "postBuild": ["echo \u017e"] }""";

        // sha256 of "freexcomp-commands-v1\npreBuild\n1\n7\necho hi\npostBuild\n0\nconfigurations/Release/preBuild\n0\n" +
        // "configurations/Release/postBuild\n1\n7\necho ž\n" (ž = 2 bytes), computed outside the product
        const string Expected = "sha256:ebed2f79b93b9e36cb148fe619dff9d560ae0380450e44b887622231348c8703";
        var hash = ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(ReleaseEvents)));
        Assert.Equal(Expected, hash);

        // configurations without an events key do not count: the hash of before this card
        var plain = ProjectTrust.CommandsHash(Parse(Events));
        Assert.Equal(plain, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(""))));
        Assert.NotEqual(plain, hash);

        // a changed configuration command; a command moved to the other list; the configuration's name; "events": {} added
        Assert.NotEqual(hash, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(""", "events": { "preBuild": [], "postBuild": ["echo z"] }"""))));
        Assert.NotEqual(hash, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(""", "events": { "preBuild": ["echo \u017e"], "postBuild": [] }"""))));
        Assert.NotEqual(hash, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(ReleaseEvents, name: "Final"))));
        Assert.NotEqual(hash, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(ReleaseEvents, more: """, "Demo": { "inherits": "Release", "events": {} }"""))));
        Assert.NotEqual(plain, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(""", "events": {}"""))));
        // a command of a configuration is not the same as that command at project level
        Assert.NotEqual(
            ProjectTrust.CommandsHash(Parse("""{ "preBuild": ["x"], "postBuild": [] }""", extra: Configurations(""", "events": {}"""))),
            ProjectTrust.CommandsHash(Parse("", extra: Configurations(""", "events": { "preBuild": ["x"] }"""))));

        // postBuildWhen adds no command: not hashed, at either level
        Assert.Equal(hash, ProjectTrust.CommandsHash(Parse(Events, extra: Configurations(""", "events": { "preBuild": [], "postBuild": ["echo \u017e"], "postBuildWhen": "always" }"""))));
        Assert.Equal(plain, ProjectTrust.CommandsHash(Parse("""{ "preBuild": ["echo hi"], "postBuild": [], "postBuildWhen": "always" }""")));

        // HasCommands looks at every list
        Assert.True(ProjectTrust.HasCommands(Parse("", extra: Configurations(ReleaseEvents))));
        Assert.False(ProjectTrust.HasCommands(Parse("", extra: Configurations(""", "events": {}"""))));
        Assert.Equal(TrustState.Untrusted, ProjectTrust.Evaluate(Parse("", extra: Configurations(ReleaseEvents)), @"C:\Work\App\App.pproj", Snapshot()));
    }

    [Fact]
    [Trait("Req", "CMP-1255")]
    public void FindUntrusted_ListsConfigurationCommands()
    {
        const string App = @"C:\g\App\App.pproj", Lib = @"C:\g\Lib\Lib.pproj", TrustFile = @"C:\user\roaming\trust.json";
        var fs = new InMemoryFileSystem();
        fs.Add(App, $$"""{ "formatVersion": 1, "name": "App", "mainSource": "App.pas", "dependencies": ["../Lib/Lib.pproj"], "events": { "preBuild": ["a1"], "postBuild": [] }, {{WithConfigurationEvents}} }""");
        // the only commands of this one are in a configuration
        fs.Add(Lib, """{ "formatVersion": 1, "name": "Lib", "mainSource": "Lib.pas", "configurations": { "Debug": {}, "Release": { "events": { "preBuild": ["l1", "l2"] } } } }""");
        var store = new FileTrustStore(fs, TrustFile);

        var all = ProjectTrust.FindUntrusted(fs, App, null, store);

        Assert.Equal([Lib, App], all.Select(u => u.ProjectPath));
        // Lists: the non-empty lists, in the order of CommandLists; PreBuild / PostBuild stay the project-level lists
        Assert.Equal(["configurations/Release/preBuild | Release | pre | l1 ; l2"], all[0].Lists.Select(Describe));
        Assert.Empty(all[0].PreBuild);
        Assert.Empty(all[0].PostBuild);
        Assert.Equal(["preBuild | - | pre | a1", "configurations/Release/postBuild | Release | post | echo \u017e"], all[1].Lists.Select(Describe));
        Assert.Equal(["a1"], all[1].PreBuild);
        Assert.Empty(all[1].PostBuild);

        // trusted with the hash over all lists → not listed; a changed configuration command → Changed
        store.TrustProject(Lib, ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(Lib, fs).Value!));
        Assert.Equal([App], ProjectTrust.FindUntrusted(fs, App, null, store).Select(u => u.ProjectPath));
        fs.Add(Lib, fs.ReadAllText(Lib).Replace("\"l2\"", "\"l3\"", StringComparison.Ordinal));
        var changed = ProjectTrust.FindUntrusted(fs, App, null, store)[0];
        Assert.Equal((Lib, TrustState.Changed), (changed.ProjectPath, changed.State));
        Assert.Equal(["configurations/Release/preBuild | Release | pre | l1 ; l3"], changed.Lists.Select(Describe));
    }
}
