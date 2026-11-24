using System.Collections.Immutable;
using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>configurations.md §2, §3 (CMP-1150…1157, task C-297): <c>inherits</c>, per-configuration <c>searchPaths</c>, the reset marker.</summary>
public sealed partial class ProjectSerializerTests
{
    private static string WithConfigurations(string configurations, string extra = "") =>
        $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas"{{extra}}, "configurations": { {{configurations}} } }""";

    private static ImmutableArray<Diagnostic> ParseErrors(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, text);
        Assert.Null(result.Value);
        return result.Diagnostics;
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1154")]
    public void Inherits_UnknownParent_E9011()
    {
        var text = WithConfigurations("""
            "Debug": { "optimization": 0 }, "Demo": { "inherits": "Release" }, "Other": { "inherits": "debug" }
            """.Trim());

        var errors = ParseErrors(text);

        // one per broken link, at the `inherits` token; names are matched exactly (case-sensitive)
        Assert.Equal(
            [
                "Invalid: E9011 Configuration 'Demo' inherits from unknown configuration 'Release'",
                "Invalid: E9011 Configuration 'Other' inherits from unknown configuration 'debug'",
            ],
            errors.Select(d => $"Invalid: {d.Code} {d.Message}"));
        Assert.All(errors, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(new(0, text.IndexOf("\"Release\" }", StringComparison.Ordinal)), errors[0].Location.LineSpan.Start);
        Assert.Equal(new(0, text.IndexOf("\"debug\" }", StringComparison.Ordinal)), errors[1].Location.LineSpan.Start);
        Assert.Equal(ProjectPath, errors[0].Location.Path);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1154")]
    // self reference
    [InlineData("""
        "Debug": {}, "Loop": { "inherits": "Loop" }
        """, "Loop -> Loop", "\"Loop\" }")]
    // two members: reported at the member that comes first in the file
    [InlineData("""
        "A": { "inherits": "B" }, "B": { "inherits": "A" }
        """, "A -> B -> A", "\"B\" }")]
    [InlineData("""
        "B": { "inherits": "A" }, "A": { "inherits": "B" }
        """, "B -> A -> B", "\"A\" }")]
    // three members, entered from a configuration outside the cycle (which is not reported itself)
    [InlineData("""
        "Outside": { "inherits": "Y" }, "Root": {}, "Z": { "inherits": "X" }, "X": { "inherits": "Y" }, "Y": { "inherits": "Z" }
        """, "Z -> X -> Y -> Z", "\"X\" }")]
    public void Inherits_Cycle_E9015_FileUntouched(string configurations, string chain, string at)
    {
        var text = WithConfigurations(configurations.Trim());
        var fs = new InMemoryFileSystem().Add(ProjectPath, text);
        var before = fs.ReadAllBytes(ProjectPath);

        var result = ProjectSerializer.LoadProject(ProjectPath, fs);

        Assert.Null(result.Value);
        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("E9015", d.Code);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("Circular configuration inheritance: " + chain, d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
        Assert.Equal(before, fs.ReadAllBytes(ProjectPath));                 // the file is never modified
        Assert.Equal([ProjectPath], fs.EnumerateFiles(@"C:\p\Hello", "*"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1154")]
    public void Inherits_TwoCycles_EachReportedOnce()
    {
        var text = WithConfigurations("""
            "A": { "inherits": "B" }, "B": { "inherits": "A" }, "C": { "inherits": "C" }, "D": { "inherits": "Missing" }
            """.Trim());

        Assert.Equal(
            [
                "E9015 Circular configuration inheritance: A -> B -> A",
                "E9015 Circular configuration inheritance: C -> C",
                "E9011 Configuration 'D' inherits from unknown configuration 'Missing'",
            ],
            ParseErrors(text).Select(d => d.Code + " " + d.Message));
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1154")]
    [InlineData("\"inherits\": 1", "1 }")]
    [InlineData("\"inherits\": \"\"", "\"\" }")]
    [InlineData("\"inherits\": null", "null }")]
    [InlineData("\"inherits\": [\"Debug\"]", "[\"Debug\"] }")]
    [InlineData("\"inherits\": { \"name\": \"Debug\" }", "{ \"name\"")]
    public void Inherits_WrongType_E9002(string key, string at)
    {
        var text = WithConfigurations($$"""
            "Debug": {}, "Demo": { {{key}} }
            """.Trim());

        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: 'inherits' must be the name of another configuration", d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1153")]
    [Trait("Req", "CMP-1154")]
    [InlineData("\"Debug\": { \"defines\": [\"A\", \"!\"] }", "", "\"!\"")]
    [InlineData("\"Debug\": { \"defines\": [\"!\", \"A\", \"!\"] }", "", "\"!\"] }")]
    [InlineData("\"Debug\": { \"searchPaths\": [\"lib\", \"!\"] }", "", "\"!\"")]
    [InlineData("\"Debug\": {}, \"Demo\": { \"inherits\": \"Debug\", \"searchPaths\": [\"a\", \"b\", \"!\"] }", "", "\"!\"")]
    [InlineData("\"Debug\": {}", ", \"defines\": [\"!\"]", "\"!\"")]
    [InlineData("\"Debug\": {}", ", \"defines\": [\"APP\", \"!\"]", "\"!\"")]
    [InlineData("\"Debug\": {}", ", \"searchPaths\": [\"!\", \"lib\"]", "\"!\"")]
    public void ResetMarker_NotFirstOrAtProjectLevel_E9002(string configurations, string projectLevel, string at)
    {
        var text = WithConfigurations(configurations, projectLevel);

        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: '!' is allowed only as the first entry of a configuration's list", d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);

        // as the first entry of a configuration's list it is accepted, in root and derived configurations
        var p = Parse(WithConfigurations("""
            "Debug": { "defines": ["!", "A"], "searchPaths": ["!"] }, "Demo": { "inherits": "Debug", "defines": ["!"], "searchPaths": ["!", "x"] }
            """.Trim()));
        Assert.Equal(["!", "A"], p.Configurations["Debug"].Defines);
        Assert.Equal(["!"], p.Configurations["Debug"].SearchPaths);
        Assert.Equal(["!"], p.Configurations["Demo"].Defines);
        Assert.Equal(["!", "x"], p.Configurations["Demo"].SearchPaths);
    }

    /// <summary>A canonical file with every key combination of CMP-1155 and an unknown key.</summary>
    private const string DerivedCanonical = """
        {
          "formatVersion": 1,
          "name": "Hello",
          "language": "pascal",
          "appType": "console",
          "mainSource": "Hello.pas",
          "files": [
            {
              "path": "Hello.pas"
            }
          ],
          "searchPaths": [
            "lib"
          ],
          "defines": [
            "APP"
          ],
          "platforms": [
            "win64"
          ],
          "configurations": {
            "Debug": {
              "optimization": 0,
              "debugInfo": true,
              "defines": [
                "DEBUG"
              ],
              "outputDir": "bin/Debug"
            },
            "Release": {
              "optimization": 2,
              "debugInfo": false,
              "defines": [
                "RELEASE"
              ],
              "searchPaths": [
                "rel"
              ],
              "outputDir": "bin/Release",
              "rtti": "published"
            },
            "Release-Demo": {
              "inherits": "Release",
              "defines": [
                "DEMO"
              ]
            },
            "Profiling": {
              "inherits": "Release-Demo",
              "debugInfo": true,
              "defines": [
                "!",
                "PROFILE"
              ]
            },
            "Bare": {
              "inherits": "Debug"
            },
            "Full": {
              "inherits": "Bare",
              "optimization": 1,
              "debugInfo": false,
              "x-note": "kept in place",
              "defines": [],
              "searchPaths": [
                "!",
                "full/units"
              ],
              "outputDir": "out/full",
              "rtti": "broad"
            },
            "EmptyLists": {
              "inherits": "Debug",
              "searchPaths": [],
              "outputDir": "bin/EmptyLists"
            },
            "RootWithPaths": {
              "optimization": 1,
              "debugInfo": false,
              "defines": [
                "!"
              ],
              "searchPaths": [],
              "outputDir": "bin/RootWithPaths"
            }
          },
          "activeConfiguration": "Profiling",
          "run": {
            "arguments": "",
            "workingDir": ""
          },
          "events": {
            "preBuild": [],
            "postBuild": []
          },
          "dependencies": []
        }

        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1151")]
    [Trait("Req", "CMP-1155")]
    [Trait("Req", "CMP-1156")]
    public void DerivedConfigurations_RoundTripByteIdentical()
    {
        var text = Lf(DerivedCanonical);

        var p = Parse(text);

        Assert.Equal(text, ProjectSerializer.Save(p));
        Assert.Equal(["Debug", "Release", "Release-Demo", "Profiling", "Bare", "Full", "EmptyLists", "RootWithPaths"], p.ConfigurationNames);

        // the model holds own values only (CMP-1156)
        var release = p.Configurations["Release"];
        Assert.Null(release.Inherits);
        Assert.False(release.InheritsOptimization);
        Assert.False(release.InheritsDebugInfo);
        Assert.Equal(["rel"], release.SearchPaths);

        var demo = p.Configurations["Release-Demo"];
        Assert.Equal("Release", demo.Inherits);
        Assert.True(demo.InheritsOptimization);
        Assert.True(demo.InheritsDebugInfo);
        Assert.Equal(0, demo.Optimization);
        Assert.False(demo.DebugInfo);
        Assert.Equal(["DEMO"], demo.Defines);
        Assert.Empty(demo.SearchPaths);
        Assert.Equal("bin/Release-Demo", demo.OutputDir);
        Assert.Null(demo.Rtti);

        var profiling = p.Configurations["Profiling"];
        Assert.Equal("Release-Demo", profiling.Inherits);
        Assert.True(profiling.InheritsOptimization);
        Assert.False(profiling.InheritsDebugInfo);
        Assert.True(profiling.DebugInfo);
        Assert.Equal(["!", "PROFILE"], profiling.Defines);

        var full = p.Configurations["Full"];
        Assert.Equal((1, false, "out/full", "broad"), (full.Optimization, full.DebugInfo, full.OutputDir, full.Rtti));
        Assert.False(full.InheritsOptimization || full.InheritsDebugInfo);
        Assert.Equal(["!", "full/units"], full.SearchPaths);

        // a model edit keeps the canonical form: a value of one's own appears, a removed one disappears
        var edited = p with
        {
            Configurations = p.Configurations
                .SetItem("Release-Demo", demo with { InheritsOptimization = false, Optimization = 1, Defines = [] })
                .SetItem("Profiling", profiling with { InheritsDebugInfo = true, SearchPaths = [@"prof\units"] }),
        };
        var saved = ProjectSerializer.Save(edited);
        Assert.Contains("""
                "Release-Demo": {
                  "inherits": "Release",
                  "optimization": 1,
                  "defines": []
                },
                "Profiling": {
                  "inherits": "Release-Demo",
                  "defines": [
                    "!",
                    "PROFILE"
                  ],
                  "searchPaths": [
                    "prof/units"
                  ]
                },
            """.ReplaceLineEndings("\n"), saved, StringComparison.Ordinal);
        Assert.Equal(saved, ProjectSerializer.Save(Parse(saved)));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1155")]
    public void NewDerivedConfiguration_WritesOnlyInherits()
    {
        var p = Parse(CanonicalExample);
        var derived = new ProjectConfiguration(0, false, [], "bin/Release-Demo")
        {
            Inherits = "Release",
            InheritsOptimization = true,
            InheritsDebugInfo = true,
        };

        var saved = ProjectSerializer.Save(p with { Configurations = p.Configurations.Add("Release-Demo", derived) });

        Assert.Contains("""
                "Release-Demo": {
                  "inherits": "Release"
                }
              },
            """.ReplaceLineEndings("\n"), saved, StringComparison.Ordinal);
        var reloaded = Parse(saved).Configurations["Release-Demo"];
        Assert.Equal(derived, reloaded, ConfigComparer.Instance);
        Assert.Equal(("Release", true, true), (reloaded.Inherits, reloaded.InheritsOptimization, reloaded.InheritsDebugInfo));
        Assert.Equal(saved, ProjectSerializer.Save(Parse(saved)));

        // a new project (no loaded file) writes the same object
        var fresh = ProjectSerializer.CreateDefault("N", "N.pas", "console");
        var text = ProjectSerializer.Save(fresh with { Configurations = fresh.Configurations.Add("Release-Demo", derived) });
        Assert.Contains("    \"Release-Demo\": {\n      \"inherits\": \"Release\"\n    }\n", text, StringComparison.Ordinal);
    }

    /// <summary>CMP-1151: root configurations keep their meaning and their bytes — every serializer fixture without <c>inherits</c> still round-trips.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1151")]
    [Trait("Req", "CMP-1155")]
    public void RootConfigurations_WriteAsBefore()
    {
        Assert.Equal(Lf(CanonicalExample), ProjectSerializer.Save(Parse(CanonicalExample)));
        Assert.Equal(Lf(WithManifest("{}")), ProjectSerializer.Save(Parse(WithManifest("{}"))));

        // the saved form of the minimal file, of a default project and of a file with every optional root key is a fixed point
        foreach (var text in new[]
                 {
                     ProjectSerializer.Save(Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""")),
                     ProjectSerializer.Save(ProjectSerializer.CreateDefault("N", "N.pas", "gui")),
                     ProjectSerializer.Save(Parse(WithConfigurations("""
                         "Fast": { "optimization": 1 }, "Typed": { "rtti": "published", "x-own": 1, "debugInfo": true }
                         """.Trim()))),
                 })
        {
            Assert.Equal(text, ProjectSerializer.Save(Parse(text)));
            Assert.DoesNotContain("inherits", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"searchPaths\": [],\n      \"outputDir\"", text, StringComparison.Ordinal);   // no per-configuration key appears
        }

        // a root configuration: the four keys always, in the order of before; absent keys take the fixed defaults
        var p = Parse(WithConfigurations("\"Fast\": { \"optimization\": 1 }"));
        var fast = p.Configurations["Fast"];
        Assert.Equal(new ProjectConfiguration(1, false, [], "bin/Fast"), fast, ConfigComparer.Instance);
        Assert.Null(fast.Inherits);
        Assert.False(fast.InheritsOptimization || fast.InheritsDebugInfo);
        Assert.Empty(fast.SearchPaths);
        Assert.Contains("""
                "Fast": {
                  "optimization": 1,
                  "debugInfo": false,
                  "defines": [],
                  "outputDir": "bin/Fast"
                }
            """.ReplaceLineEndings("\n"), ProjectSerializer.Save(p), StringComparison.Ordinal);
    }
}
