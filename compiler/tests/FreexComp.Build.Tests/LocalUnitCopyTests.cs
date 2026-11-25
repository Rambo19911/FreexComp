using System.Collections.Immutable;
using System.Text.Json;
using FreexComp.Compiler;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-330 (card F05-099): the search order of a project's own unit against a library unit of the same name
/// (library-local-copy.md CMP-1280…1282; LANG-510 steps 2…5, CMP-805). Tests only: the resolver already behaves so.
/// </summary>
[Trait("Phase", "8")]
public sealed class LocalUnitCopyTests
{
    private static string Unit(string name, string origin) => $"unit {name}; interface const Origin = '{origin}'; implementation end.";

    private static ImmutableArray<string> Diagnostics(EventLog log) => [.. log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())];

    /// <summary>The source files recorded in the unit's <c>.pcu</c>: what the unit was compiled from.</summary>
    private static ImmutableArray<string> CompiledSources(BuildWorkspace w, string unit)
    {
        using var pcu = JsonDocument.Parse(w.Fs.ReadAllText($@"C:\p\obj\Debug\{unit}.pcu"));
        return [.. pcu.RootElement.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("path").GetString()!)];
    }

    /// <summary>
    /// The library unit is reached through an extra search path — as the units of the UI framework and of the data-access
    /// library are — or lies under the RTL root (directly, or in an immediate sub-directory); the project's copy is (a) listed in
    /// <c>files</c>, (b) only lying beside the main source, (c) in a project <c>searchPaths</c> directory.
    /// </summary>
    [Theory]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1280")]
    [Trait("Req", "CMP-1282")]
    [InlineData("ui-framework", "files")]
    [InlineData("ui-framework", "beside the main source")]
    [InlineData("ui-framework", "searchPaths")]
    [InlineData("data", "files")]
    [InlineData("data", "beside the main source")]
    [InlineData("data", "searchPaths")]
    [InlineData("rtl", "files")]
    [InlineData("rtl", "beside the main source")]
    [InlineData("rtl", "searchPaths")]
    [InlineData("rtl sub-directory", "files")]
    [InlineData("rtl sub-directory", "beside the main source")]
    [InlineData("rtl sub-directory", "searchPaths")]
    public async Task ProjectCopyWinsOverExtraSearchPathAndRtl(string origin, string place)
    {
        var (unit, libraryDirectory, extra) = origin switch
        {
            "ui-framework" => ("Forms", @"C:\lib\ui-framework\src", true),
            "data" => ("DB", @"C:\lib\data\src", true),
            "rtl" => ("SysUtils", BuildWorkspace.Rtl, false),
            "rtl sub-directory" => ("Registry", BuildWorkspace.Rtl + @"\win", false),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        var (projectExtra, copy) = place switch
        {
            "files" => ($$""" "files": [{ "path": "Hello.pas" }, { "path": "local/{{unit}}.pas" }]""", $@"C:\p\local\{unit}.pas"),
            "beside the main source" => ("", $@"C:\p\{unit}.pas"),
            "searchPaths" => (""" "searchPaths": ["units"]""", $@"C:\p\units\{unit}.pas"),
            _ => throw new ArgumentOutOfRangeException(nameof(place)),
        };
        var library = $@"{libraryDirectory}\{unit}.pas";

        BuildWorkspace Bed(bool withCopy)
        {
            var w = new BuildWorkspace($"program Hello; uses {unit}; begin Writeln(Origin) end.", BuildWorkspace.ProjectJson("Hello", extra: withCopy ? projectExtra : ""));
            w.Fs.Add(library, Unit(unit, "library"));
            if (withCopy)
            {
                w.Fs.Add(copy, Unit(unit, "local"));
            }

            return w;
        }

        BuildRequest Request(BuildWorkspace w) => w.Request() with { ExtraSearchPaths = extra ? [libraryDirectory] : [] };

        // without the copy the library unit is the one compiled (so the case below is not vacuous)
        var plain = Bed(withCopy: false);
        var (plainResult, plainLog) = await plain.RunAsync(Request(plain));
        Assert.True(plainResult.Success, string.Join("\n", Diagnostics(plainLog)));
        Assert.Equal([library], CompiledSources(plain, unit));

        // with it the project's file is compiled instead; the shadowing is no diagnostic
        var w = Bed(withCopy: true);
        var (result, log) = await w.RunAsync(Request(w));

        Assert.True(result.Success, string.Join("\n", Diagnostics(log)));
        Assert.Empty(Diagnostics(log));
        Assert.Equal((0, 0, 0), (result.Errors, result.Warnings, result.Hints));
        Assert.Equal([copy], CompiledSources(w, unit));
        Assert.Contains("Compiling " + unit, log.Info);
        Assert.Single(log.Info, l => l == "Compiling " + unit);                  // compiled once: the library file is not compiled beside it
    }

    [Fact]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1281")]
    public async Task AddingAndRemovingTheCopyRecompilesUsers()
    {
        const string Library = @"C:\lib\src\Marker.pas", Copy = @"C:\p\Marker.pas";
        var w = new BuildWorkspace("program Hello; uses Mid, Other; begin Writeln(Where, Unrelated) end.");
        w.Fs.Add(@"C:\p\Mid.pas", "unit Mid; interface uses Marker; const Where = Origin; implementation end.");
        w.Fs.Add(@"C:\p\Other.pas", "unit Other; interface const Unrelated = 1; implementation end.");
        w.Fs.Add(Library, Unit("Marker", "library"));
        var request = w.Request() with { ExtraSearchPaths = [@"C:\lib\src"] };

        async Task<ImmutableArray<string>> Build()
        {
            var (result, log) = await w.RunAsync(request);
            Assert.True(result.Success, string.Join("\n", Diagnostics(log)));
            Assert.Empty(Diagnostics(log));
            return log.Info;
        }

        var first = await Build();
        Assert.Equal(["Compiling Hello", "Compiling Marker", "Compiling Mid", "Compiling Other", "Compiling System", "Linking Hello.exe"], first.Order(StringComparer.Ordinal));
        Assert.Equal([Library], CompiledSources(w, "Marker"));
        Assert.Equal(["Hello: up to date"], await Build());

        // the copy is added: the unit and the units that use it are recompiled and the program is linked again
        w.Fs.Add(Copy, Unit("Marker", "local"));
        var added = await Build();
        Assert.Equal(["Compiling Hello", "Compiling Marker", "Compiling Mid", "Linking Hello.exe"], added.Order(StringComparer.Ordinal));
        Assert.Equal("Linking Hello.exe", added[^1]);
        Assert.Equal([Copy], CompiledSources(w, "Marker"));                       // nothing is left pointing at the library file
        Assert.Equal(["Hello: up to date"], await Build());

        // the copy is removed: the same, back on the library file
        w.Fs.Delete(Copy);
        var removed = await Build();
        Assert.Equal(["Compiling Hello", "Compiling Marker", "Compiling Mid", "Linking Hello.exe"], removed.Order(StringComparer.Ordinal));
        Assert.Equal([Library], CompiledSources(w, "Marker"));
        Assert.Equal(["Hello: up to date"], await Build());
    }

    /// <summary>Card F05-099 acceptance 4, end to end with the pinned toolchain.</summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1280")]
    [Trait("Req", "CMP-1281")]
    public async Task ProgramRunsTheLocalCopy()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(dir, "project");
        var library = Path.Combine(dir, "library");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(library);
        try
        {
            static string Marker(string origin) => $"unit Marker;\ninterface\nfunction Origin: string;\nimplementation\nfunction Origin: string;\nbegin\n  Result := '{origin}';\nend;\nend.\n";
            File.WriteAllText(Path.Combine(project, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello"));
            File.WriteAllText(Path.Combine(project, "Hello.pas"), "program Hello;\nuses Marker;\nbegin\n  Writeln(Origin);\nend.\n");
            File.WriteAllText(Path.Combine(library, "Marker.pas"), Marker("library"));
            var copy = Path.Combine(project, "Marker.pas");

            async Task<(List<string> Output, ImmutableArray<string> Info)> BuildAndRunAsync()
            {
                var log = new EventLog();
                var result = await new BuildEngine(PhysicalFileSystem.Instance).RunAsync(
                    new BuildRequest
                    {
                        ProjectPath = Path.Combine(project, "Hello.pproj"),
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                        ExtraSearchPaths = [library],
                    },
                    log,
                    TestContext.Current.CancellationToken);
                Assert.True(result.Success, string.Join("\n", Diagnostics(log)));
                Assert.Empty(Diagnostics(log));
                var output = new List<string>();
                Assert.Equal(0, await NativeRun.RunAsync(result.OutputPath!, project, output.Add, _ => { }));
                return (output, log.Info);
            }

            var first = await BuildAndRunAsync();
            Assert.Equal(["library"], first.Output);

            File.WriteAllText(copy, Marker("local"));                            // the project's own copy of the library unit
            var withCopy = await BuildAndRunAsync();
            Assert.Equal(["local"], withCopy.Output);
            Assert.Contains("Compiling Marker", withCopy.Info);

            File.Delete(copy);
            var without = await BuildAndRunAsync();
            Assert.Equal(["library"], without.Output);
            Assert.Contains("Compiling Marker", without.Info);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
