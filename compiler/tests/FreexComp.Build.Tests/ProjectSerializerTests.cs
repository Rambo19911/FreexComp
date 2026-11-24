using System.Collections.Immutable;
using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>docs/interfaces/project-format.md §1–§2; build-engine.md CMP-809…811.</summary>
[Trait("Phase", "1")]
public sealed class ProjectSerializerTests
{
    private const string ProjectPath = @"C:\p\Hello\Hello.pproj";

    /// <summary>The example of project-format.md §1 without its documentation comments, in canonical form.</summary>
    internal const string CanonicalExample = """
        {
          "formatVersion": 1,
          "name": "Hello",
          "language": "pascal",
          "appType": "console",
          "mainSource": "Hello.pas",
          "files": [
            {
              "path": "Unit1.pas"
            },
            {
              "path": "MainForm.pas",
              "form": "MainForm.form"
            },
            {
              "path": "notes.md",
              "kind": "other"
            }
          ],
          "searchPaths": [
            "lib"
          ],
          "unitScopeNames": [],
          "rtti": "broad",
          "manifest": "default",
          "defines": [],
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
              "outputDir": "bin/Release"
            }
          },
          "activeConfiguration": "Debug",
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

    private static string Lf(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static Project Parse(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, Lf(text));
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Project>(result.Value);
    }

    private static Diagnostic ParseError(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, Lf(text));
        Assert.Null(result.Value);
        return Assert.Single(result.Diagnostics);
    }

    // --- loading

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_InterfaceExample_ReadsEveryKey()
    {
        var p = Parse(CanonicalExample);

        Assert.Equal(1, p.FormatVersion);
        Assert.Equal("Hello", p.Name);
        Assert.Equal("pascal", p.Language);
        Assert.Equal("console", p.AppType);
        Assert.Equal("Hello.pas", p.MainSource);
        Assert.Equal(
            [new ProjectFile("Unit1.pas", null, null), new ProjectFile("MainForm.pas", "MainForm.form", null), new ProjectFile("notes.md", null, "other")],
            p.Files);
        Assert.Equal(["lib"], p.SearchPaths);
        Assert.Empty(p.UnitScopeNames);
        Assert.Equal("broad", p.Rtti);
        Assert.Equal("default", p.Manifest);
        Assert.Empty(p.Defines);
        Assert.Equal(["win64"], p.Platforms);
        Assert.Equal(["Debug", "Release"], p.ConfigurationNames);
        var debug = p.Configurations["Debug"];
        Assert.Equal((0, true, "bin/Debug"), (debug.Optimization, debug.DebugInfo, debug.OutputDir));
        Assert.Equal(["DEBUG"], debug.Defines);
        var release = p.Configurations["Release"];
        Assert.Equal((2, false, "bin/Release"), (release.Optimization, release.DebugInfo, release.OutputDir));
        Assert.Equal("Debug", p.ActiveConfiguration);
        Assert.Equal(new RunSettings("", ""), p.Run);
        Assert.Empty(p.Events.PreBuild);
        Assert.Empty(p.Events.PostBuild);
        Assert.Empty(p.Dependencies);
        Assert.Equal(ProjectPath, p.FilePath);
        Assert.Equal(@"C:\p\Hello", p.Directory);
    }

    [Fact]
    public void ProjectFile_EffectiveKind_DefaultsFromExtension()
    {
        Assert.Equal("unit", new ProjectFile("A.pas", null, null).EffectiveKind);
        Assert.Equal("unit", new ProjectFile("sub/B.PAS", null, null).EffectiveKind);
        Assert.Equal("form", new ProjectFile("A.form", null, null).EffectiveKind);
        Assert.Equal("other", new ProjectFile("readme.md", null, null).EffectiveKind);
        Assert.Equal("other", new ProjectFile("A.pas", null, "other").EffectiveKind);
    }

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_MinimalFile_TakesDefaults()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "Mini", "mainSource": "Mini.pas" }""");

        Assert.Equal("pascal", p.Language);
        Assert.Equal("console", p.AppType);
        Assert.Equal([new ProjectFile("Mini.pas", null, null)], p.Files);
        Assert.Empty(p.SearchPaths);
        Assert.Empty(p.UnitScopeNames);
        Assert.Null(p.Rtti);
        Assert.Null(p.Manifest);
        Assert.Empty(p.Defines);
        Assert.Equal(["win64"], p.Platforms);
        Assert.Equal(["Debug", "Release"], p.ConfigurationNames);
        Assert.Equal(new ProjectConfiguration(0, true, ["DEBUG"], "bin/Debug"), p.Configurations["Debug"], ConfigComparer.Instance);
        Assert.Equal(new ProjectConfiguration(2, false, ["RELEASE"], "bin/Release"), p.Configurations["Release"], ConfigComparer.Instance);
        Assert.Equal("Debug", p.ActiveConfiguration);
        Assert.Equal(new RunSettings("", ""), p.Run);
        Assert.Empty(p.Events.PreBuild);
        Assert.Empty(p.Dependencies);
    }

    [Fact]
    public void ParseProject_BackslashPaths_AreReadWithForwardSlashes()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "src\\A.pas", "searchPaths": ["..\\lib"] }""");

        Assert.Equal("src/A.pas", p.MainSource);
        Assert.Equal(["../lib"], p.SearchPaths);
    }

    [Fact]
    public void LoadProject_ReadsThroughFileSystem()
    {
        var fs = new InMemoryFileSystem().Add(ProjectPath, CanonicalExample);

        var result = ProjectSerializer.LoadProject(ProjectPath, fs);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Hello", result.Value!.Name);
    }

    [Fact]
    public void LoadProject_MissingFile_E9003()
    {
        var result = ProjectSerializer.LoadProject(ProjectPath, new InMemoryFileSystem());

        Assert.Null(result.Value);
        var d = Assert.Single(result.Diagnostics);
        Assert.Equal(@"C:\p\Hello\Hello.pproj(0,0): error E9003: Project file not found: 'C:\p\Hello\Hello.pproj'", d.ToText());
    }

    // --- errors (CMP-810): E9002 at the offending token, E9001, E9007, E9008

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_Comment_IsE9002AtTheComment()
    {
        var d = ParseError("""
            {
              "formatVersion": 1, // version
              "name": "A", "mainSource": "A.pas"
            }
            """);

        Assert.Equal(@"C:\p\Hello\Hello.pproj(2,23): error E9002: Invalid project file: comments are not allowed", d.ToText());
    }

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_TrailingComma_IsE9002AtTheComma()
    {
        var d = ParseError("""
            {
              "formatVersion": 1, "name": "A", "mainSource": "A.pas",
            }
            """);

        Assert.Equal(@"C:\p\Hello\Hello.pproj(2,57): error E9002: Invalid project file: trailing comma is not allowed", d.ToText());
    }

    [Fact]
    public void ParseProject_TrailingCommaInArray_IsE9002()
    {
        var d = ParseError("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "defines": ["X",] }""");

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: trailing comma is not allowed", d.Message);
        Assert.Equal(new(0, 73), d.Location.LineSpan.Start); // the comma after "X"
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{ "formatVersion": 1 } x""")]
    [InlineData("""{ "name": 'A' }""")]
    public void ParseProject_MalformedJson_IsE9002(string text)
    {
        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: malformed JSON", d.Message);
    }

    [Fact]
    public void ParseProject_MalformedJson_ColumnsCountUtf16()
    {
        // "é" is two UTF-8 bytes but one UTF-16 code unit; the error is at the stray 'x' (0-based column 14).
        var d = ParseError("{ \"name\": \"é\" x }");

        Assert.Equal(new(0, 14), d.Location.LineSpan.Start);
    }

    [Fact]
    public void ParseProject_RootNotObject_IsE9002()
    {
        var d = ParseError("[1]");

        Assert.Equal("Invalid project file: the root value must be an object", d.Message);
    }

    [Theory]
    [InlineData("""{ "name": "A", "mainSource": "A.pas" }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 1, "mainSource": "A.pas" }""", "name")]
    [InlineData("""{ "formatVersion": 1, "name": "A" }""", "mainSource")]
    public void ParseProject_MissingRequiredKey_IsE9002AtTheObject(string text, string key)
    {
        var d = ParseError(text);

        Assert.Equal($"Invalid project file: missing required key '{key}'", d.Message);
        Assert.Equal(new(0, 0), d.Location.LineSpan.Start);
    }

    [Fact]
    public void ParseProject_MissingKeyInConfiguration_TakesDefaults()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "configurations": { "Fast": { "optimization": 1 } }, "activeConfiguration": "Fast" }""");

        Assert.Equal(new ProjectConfiguration(1, false, [], "bin/Fast"), p.Configurations["Fast"], ConfigComparer.Instance);
        Assert.Equal(["Fast"], p.ConfigurationNames);
    }

    [Theory]
    [InlineData("""{ "formatVersion": "1", "name": "A", "mainSource": "A.pas" }""", "'formatVersion' must be an integer", "\"1\"")]
    [InlineData("""{ "formatVersion": 1, "name": 5, "mainSource": "A.pas" }""", "'name' must be a string", "5")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "files": {} }""", "'files' must be an array", "{}")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "files": [ "A.pas" ] }""", "'files[0]' must be an object", "\"A.pas\" ]")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "files": [ {} ] }""", "missing required key 'path'", "{} ]")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "defines": [ 1 ] }""", "'defines[0]' must be a string", "1 ]")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "configurations": { "D": { "debugInfo": 1 } } }""", "'configurations.D.debugInfo' must be a Boolean", "1 }")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "configurations": { "D": { "optimization": 3 } } }""", "'configurations.D.optimization' must be 0, 1 or 2", "3 }")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "rtti": "all" }""", "'rtti' must be \"broad\" or \"published\"", "\"all\"")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "run": [] }""", "'run' must be an object", "[]")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "unitScopeNames": null }""", "'unitScopeNames' must be an array", "null")]
    [InlineData("""{ "formatVersion": 0, "name": "A", "mainSource": "A.pas" }""", "'formatVersion' must be at least 1", "0")]
    [InlineData("""{ "formatVersion": 1, "name": "A", "name": "B", "mainSource": "A.pas" }""", "duplicate key 'name'", "\"name\": \"B\"")]
    public void ParseProject_WrongValue_IsE9002AtTheToken(string text, string detail, string at)
    {
        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: " + detail, d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
    }

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_NewerFormatVersion_IsE9001()
    {
        var d = ParseError("""{ "formatVersion": 2, "name": "A", "mainSource": "A.pas" }""");

        Assert.Equal(@"C:\p\Hello\Hello.pproj(1,20): error E9001: Project format version 2 is newer than supported version 1", d.ToText());
    }

    [Fact]
    [Trait("Req", "CMP-810")]
    public void ParseProject_UnsupportedLanguage_IsE9007()
    {
        var d = ParseError("""{ "formatVersion": 1, "name": "A", "language": "cpp", "mainSource": "A.pas" }""");

        Assert.Equal(@"C:\p\Hello\Hello.pproj(1,48): error E9007: Unsupported language 'cpp'", d.ToText());
    }

    [Theory]
    [Trait("Req", "CMP-810")]
    [InlineData("package")]
    [InlineData("service")]
    [InlineData("Library")]
    public void ParseProject_UnsupportedAppType_IsE9008(string appType)
    {
        var d = ParseError($$"""{ "formatVersion": 1, "name": "A", "appType": "{{appType}}", "mainSource": "A.pas" }""");

        Assert.Equal($"Unsupported application type '{appType}'", d.Message);
        Assert.Equal("E9008", d.Code);
        Assert.Equal(new(0, 46), d.Location.LineSpan.Start);
    }

    [Fact]
    public void ParseProject_GuiAppType_IsAccepted()
    {
        Assert.Equal("gui", Parse("""{ "formatVersion": 1, "name": "A", "appType": "gui", "mainSource": "A.pas" }""").AppType);
    }

    [Fact]
    public void ParseProject_RelativePath_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProjectSerializer.ParseProject("Hello.pproj", "{}"));
    }

    // --- saving (CMP-811)

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_CanonicalInput_IsByteIdentical()
    {
        var text = Lf(CanonicalExample);

        Assert.Equal(text, ProjectSerializer.Save(Parse(text)));
    }

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_IsLfTwoSpaceNoBomWithFinalNewline()
    {
        var saved = ProjectSerializer.Save(ProjectSerializer.CreateDefault("App", "App.pas", "console"));

        Assert.DoesNotContain("\r", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", saved, StringComparison.Ordinal);
        Assert.NotEqual('\uFEFF', saved[0]);
        Assert.StartsWith("{\n  \"formatVersion\": 1,\n  \"name\": \"App\",\n", saved, StringComparison.Ordinal);
        Assert.EndsWith("}\n", saved, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_NonCanonicalInput_IsCanonicalised()
    {
        var p = Parse("""
            {"mainSource":"Hello.pas","name":"Hello","formatVersion":1,
             "files":[{"path":"Unit1.pas"},{"form":"MainForm.form","path":"MainForm.pas"},{"path":"notes.md","kind":"other"}],
             "searchPaths":["lib"],"unitScopeNames":[],"rtti":"broad","manifest":"default",
             "language":"pascal","appType":"console"}
            """);

        Assert.Equal(Lf(CanonicalExample), ProjectSerializer.Save(p));
    }

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_PreservesUnknownKeysInPlace()
    {
        var p = Parse("""
            {
              "x-first": 0,
              "formatVersion": 1,
              "name": "Hello",
              "x-afterName": { "a": [1, 2] },
              "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas", "x-file": true } ],
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug", "x-cfg": "k" } },
              "run": { "arguments": "-v", "workingDir": "", "x-run": null },
              "x-last": "z"
            }
            """) with { Name = "Renamed" };

        var saved = ProjectSerializer.Save(p);

        Assert.Equal(Lf("""
            {
              "x-first": 0,
              "formatVersion": 1,
              "name": "Renamed",
              "x-afterName": {
                "a": [
                  1,
                  2
                ]
              },
              "language": "pascal",
              "appType": "console",
              "mainSource": "Hello.pas",
              "files": [
                {
                  "path": "Hello.pas",
                  "x-file": true
                }
              ],
              "searchPaths": [],
              "defines": [],
              "platforms": [
                "win64"
              ],
              "configurations": {
                "Debug": {
                  "optimization": 0,
                  "debugInfo": true,
                  "defines": [],
                  "outputDir": "bin/Debug",
                  "x-cfg": "k"
                }
              },
              "activeConfiguration": "Debug",
              "run": {
                "arguments": "-v",
                "workingDir": "",
                "x-run": null
              },
              "events": {
                "preBuild": [],
                "postBuild": []
              },
              "dependencies": [],
              "x-last": "z"
            }

            """), saved);
    }

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_FileEntriesKeepUnknownKeysByPathAfterReorder()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "files": [ { "path": "A.pas", "x": 1 }, { "path": "B.pas", "y": 2 } ] }""");
        p = p with { Files = [p.Files[1], new ProjectFile("C.pas", null, null), p.Files[0]] };

        var saved = ProjectSerializer.Save(p);

        Assert.Contains(
            "\"files\": [\n    {\n      \"path\": \"B.pas\",\n      \"y\": 2\n    },\n    {\n      \"path\": \"C.pas\"\n    },\n    {\n      \"path\": \"A.pas\",\n      \"x\": 1\n    }\n  ],",
            saved,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-811")]
    public void Save_WritesPathsWithForwardSlashes()
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "console") with
        {
            SearchPaths = [@"..\lib", @"C:\abs\dir"],
            Files = [new ProjectFile(@"src\U.pas", @"src\U.form", null)],
        };

        var saved = ProjectSerializer.Save(p);

        Assert.Contains("\"../lib\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"C:/abs/dir\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"src/U.pas\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"form\": \"src/U.form\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_NonAsciiIsWrittenVerbatim()
    {
        var p = ProjectSerializer.CreateDefault("A", "Ünïcode.pas", "console") with { Run = new RunSettings("\"quoted\" ä", "") };

        var saved = ProjectSerializer.Save(p);

        Assert.Contains("\"mainSource\": \"Ünïcode.pas\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"arguments\": \"\\\"quoted\\\" ä\"", saved, StringComparison.Ordinal);
        Assert.Equal(p.Run, Parse(saved).Run);
    }

    [Fact]
    public void Save_ConfigurationOrder_SourceOrderThenNewOnesOrdinal()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "configurations": { "Release": { }, "Debug": { } } }""");
        p = p with
        {
            Configurations = p.Configurations
                .Add("Zeta", new ProjectConfiguration(0, false, [], "z"))
                .Add("Alpha", new ProjectConfiguration(0, false, [], "a")),
        };

        Assert.Equal(["Release", "Debug", "Alpha", "Zeta"], p.ConfigurationNames);
        Assert.Equal(["Release", "Debug", "Alpha", "Zeta"], Parse(ProjectSerializer.Save(p)).ConfigurationNames);
    }

    [Fact]
    public void Save_RemovedConfiguration_IsNotWritten()
    {
        var p = Parse(CanonicalExample);
        p = p with { Configurations = p.Configurations.Remove("Release") };

        var saved = ProjectSerializer.Save(p);

        Assert.DoesNotContain("Release", saved, StringComparison.Ordinal);
        Assert.Equal(["Debug"], Parse(saved).ConfigurationNames);
    }

    [Fact]
    public void Save_OptionalKeysOmittedWhenAbsentAndDefault()
    {
        var saved = ProjectSerializer.Save(Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }"""));

        Assert.DoesNotContain("unitScopeNames", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("rtti", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest", saved, StringComparison.Ordinal);
        Assert.Equal(saved, ProjectSerializer.Save(Parse(saved)));
    }

    [Fact]
    public void Save_OptionalKeysWrittenWhenSet()
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "gui") with
        {
            UnitScopeNames = ["System", "Winapi"],
            Rtti = "published",
            Manifest = "none",
        };

        var reloaded = Parse(ProjectSerializer.Save(p));

        Assert.Equal(["System", "Winapi"], reloaded.UnitScopeNames);
        Assert.Equal("published", reloaded.Rtti);
        Assert.Equal("none", reloaded.Manifest);
    }

    /// <summary>C-272 (CMP-1133): optional top-level key <c>jobs</c> after <c>platforms</c>; absent by default; 0…256.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1133")]
    public void JobsKey_RoundTripsAndValidates()
    {
        const string Minimal = """{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""";

        // absent by default: not read, not written, not created
        Assert.Equal(0, Parse(Minimal).Jobs);
        Assert.DoesNotContain("\"jobs\"", ProjectSerializer.Save(Parse(Minimal)), StringComparison.Ordinal);
        Assert.Equal(0, ProjectSerializer.CreateDefault("A", "A.pas", "console").Jobs);
        Assert.DoesNotContain("\"jobs\"", ProjectSerializer.Save(ProjectSerializer.CreateDefault("A", "A.pas", "console")), StringComparison.Ordinal);

        // read, written after "platforms", stable
        var four = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "jobs": 4 }""");
        Assert.Equal(4, four.Jobs);
        var saved = ProjectSerializer.Save(four);
        Assert.Contains("  \"platforms\": [\n    \"win64\"\n  ],\n  \"jobs\": 4,\n  \"configurations\": {", saved, StringComparison.Ordinal);
        Assert.Equal(saved, ProjectSerializer.Save(Parse(saved)));
        Assert.Equal(256, Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "jobs": 256 }""").Jobs);

        // set in memory → written; an explicit 0 in the file is preserved
        Assert.Contains("  \"jobs\": 8,\n", ProjectSerializer.Save(Parse(Minimal) with { Jobs = 8 }), StringComparison.Ordinal);
        var zero = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "jobs": 0 }""");
        Assert.Equal(0, zero.Jobs);
        Assert.Contains("  \"jobs\": 0,\n", ProjectSerializer.Save(zero), StringComparison.Ordinal);

        // E9002 at the JSON token
        foreach (var (value, detail) in new[]
                 {
                     ("-1", "'jobs' must be an integer from 0 to 256"),
                     ("257", "'jobs' must be an integer from 0 to 256"),
                     ("\"4\"", "'jobs' must be an integer"),
                     ("1.5", "'jobs' must be an integer"),
                     ("null", "'jobs' must be an integer"),
                     ("true", "'jobs' must be an integer"),
                 })
        {
            var text = """{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "jobs": """ + value + " }";
            var d = ParseError(text);
            Assert.Equal("E9002", d.Code);
            Assert.Equal("Invalid project file: " + detail, d.Message);
            Assert.Equal(new(0, text.IndexOf(value + " }", StringComparison.Ordinal)), d.Location.LineSpan.Start);
        }
    }

    [Fact]
    public void ConfigurationRtti_RoundTrips()
    {
        var p = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "configurations": { "D": { "rtti": "published" } } }""");

        Assert.Equal("published", p.Configurations["D"].Rtti);
        Assert.Contains("\"rtti\": \"published\"", ProjectSerializer.Save(p), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDefault_MatchesInterfaceExampleDefaults()
    {
        var p = ProjectSerializer.CreateDefault("Hello", "Hello.pas", "console");

        Assert.Equal(1, p.FormatVersion);
        Assert.Equal("Hello", p.Name);
        Assert.Equal("pascal", p.Language);
        Assert.Equal("console", p.AppType);
        Assert.Equal([new ProjectFile("Hello.pas", null, null)], p.Files);
        Assert.Equal(["Debug", "Release"], p.ConfigurationNames);
        Assert.Equal("Debug", p.ActiveConfiguration);
        Assert.Equal("", p.FilePath);
        Assert.Equal(ProjectSerializer.Save(p), ProjectSerializer.Save(Parse(ProjectSerializer.Save(p))));
    }

    // --- groups (project-format.md §2)

    private const string GroupPath = @"C:\p\All.pgroup";

    private const string CanonicalGroup = """
        {
          "formatVersion": 1,
          "projects": [
            "App/App.pproj",
            "Lib/Lib.pproj"
          ],
          "activeProject": "App/App.pproj"
        }

        """;

    [Fact]
    public void LoadGroup_ReadsAndRoundTrips()
    {
        var fs = new InMemoryFileSystem().Add(GroupPath, Lf(CanonicalGroup));

        var result = ProjectSerializer.LoadGroup(GroupPath, fs);

        Assert.Empty(result.Diagnostics);
        var g = result.Value!;
        Assert.Equal(1, g.FormatVersion);
        Assert.Equal(["App/App.pproj", "Lib/Lib.pproj"], g.Projects);
        Assert.Equal("App/App.pproj", g.ActiveProject);
        Assert.Equal(GroupPath, g.FilePath);
        Assert.Equal(Lf(CanonicalGroup), ProjectSerializer.Save(g));
    }

    [Fact]
    public void LoadGroup_PreservesUnknownKeys()
    {
        var fs = new InMemoryFileSystem().Add(GroupPath, """{ "formatVersion": 1, "x": 1, "projects": [] }""");

        var g = ProjectSerializer.LoadGroup(GroupPath, fs).Value!;

        Assert.Equal("", g.ActiveProject);
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"x\": 1,\n  \"projects\": [],\n  \"activeProject\": \"\"\n}\n", ProjectSerializer.Save(g));
    }

    [Theory]
    [InlineData("""{ "formatVersion": 3, "projects": [] }""", "E9001")]
    [InlineData("""{ "formatVersion": 1 }""", "E9002")]
    [InlineData("""{ "formatVersion": 1, "projects": [], }""", "E9002")]
    public void LoadGroup_Errors(string text, string code)
    {
        var fs = new InMemoryFileSystem().Add(GroupPath, text);

        var result = ProjectSerializer.LoadGroup(GroupPath, fs);

        Assert.Null(result.Value);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void LoadGroup_MissingFile_E9003()
    {
        var result = ProjectSerializer.LoadGroup(GroupPath, new InMemoryFileSystem());

        Assert.Equal("E9003", Assert.Single(result.Diagnostics).Code);
    }

    // --- library projects (dll-output.md CMP-1080, 1082, 1083; task C-291)

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1080")]
    [Trait("Req", "CMP-1082")]
    [Trait("Req", "CMP-1083")]
    public void LibraryKeys_RoundTrip()
    {
        const string text = """
            {
              "formatVersion": 1,
              "name": "MathLib",
              "language": "pascal",
              "appType": "library",
              "mainSource": "MathLib.pas",
              "files": [
                {
                  "path": "MathLib.pas"
                }
              ],
              "searchPaths": [],
              "libraryPaths": [
                "libs",
                "D:/sdk/lib"
              ],
              "defines": [],
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
                }
              },
              "activeConfiguration": "Debug",
              "run": {
                "arguments": "--demo",
                "workingDir": "",
                "hostApplication": "../Host/bin/Debug/Host.exe"
              },
              "events": {
                "preBuild": [],
                "postBuild": []
              },
              "dependencies": []
            }

            """;

        var p = Parse(text);

        Assert.Equal("library", p.AppType);
        Assert.Equal(["libs", "D:/sdk/lib"], p.LibraryPaths);
        Assert.Equal(new RunSettings("--demo", "") { HostApplication = "../Host/bin/Debug/Host.exe" }, p.Run);
        Assert.Equal(Lf(text), ProjectSerializer.Save(p));                          // byte for byte, keys in the order of the interface

        // the mapping: library app type, resolved library paths in order, the DLL as output, the resolved host application
        var options = ProjectOptions.ToCompilationOptions(p, "Debug");
        Assert.Equal(AppType.Library, options.AppType);
        Assert.Equal([@"C:\p\Hello\libs", @"D:\sdk\lib"], options.LibraryPaths);
        Assert.Equal(@"C:\p\Hello\bin\Debug\MathLib.dll", ProjectOptions.GetOutputPath(p, "Debug"));
        Assert.Equal(@"C:\p\Host\bin\Debug\Host.exe", ProjectOptions.GetHostApplicationPath(p));

        // both keys are optional: a file without them loads with the defaults and is saved without them …
        var plain = Parse(CanonicalExample);
        Assert.Empty(plain.LibraryPaths);
        Assert.Equal("", plain.Run.HostApplication);
        Assert.Null(ProjectOptions.GetHostApplicationPath(plain));
        Assert.Equal(@"C:\p\Hello\bin\Debug\Hello.exe", ProjectOptions.GetOutputPath(plain, "Debug"));
        var saved = ProjectSerializer.Save(plain);
        Assert.DoesNotContain("libraryPaths", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("hostApplication", saved, StringComparison.Ordinal);

        // … and they appear, at their place, once set; back slashes are written as '/'
        var edited = ProjectSerializer.Save(plain with { LibraryPaths = [@"out\libs"], Run = plain.Run with { HostApplication = @"tools\Host.exe" } });
        Assert.Contains("  \"searchPaths\": [\n    \"lib\"\n  ],\n  \"libraryPaths\": [\n    \"out/libs\"\n  ],\n", edited, StringComparison.Ordinal);
        Assert.Contains("    \"workingDir\": \"\",\n    \"hostApplication\": \"tools/Host.exe\"\n  },\n", edited, StringComparison.Ordinal);

        // present but empty in the loaded file: kept
        var empty = Parse(text.Replace("\"libs\",\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal));
        var kept = ProjectSerializer.Save(empty with { LibraryPaths = [], Run = empty.Run with { HostApplication = "" } });
        Assert.Contains("  \"libraryPaths\": [],\n", kept, StringComparison.Ordinal);
        Assert.Contains("    \"hostApplication\": \"\"\n", kept, StringComparison.Ordinal);

        // wrong value types are E9002 at the token
        Assert.Equal("E9002", ParseError("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "libraryPaths": "libs" }""").Code);
        Assert.Equal("E9002", ParseError("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "run": { "hostApplication": 1 } }""").Code);
    }

    private sealed class ConfigComparer : IEqualityComparer<ProjectConfiguration>
    {
        public static readonly ConfigComparer Instance = new();

        public bool Equals(ProjectConfiguration? x, ProjectConfiguration? y) =>
            x is not null && y is not null
            && x.Optimization == y.Optimization && x.DebugInfo == y.DebugInfo && x.OutputDir == y.OutputDir
            && x.Rtti == y.Rtti && x.Defines.SequenceEqual(y.Defines);

        public int GetHashCode(ProjectConfiguration obj) => obj.Optimization;
    }
}
