using System.Collections.Immutable;
using FreexComp.Compiler.Targets;

namespace FreexComp.Build.Tests;

/// <summary>
/// platform-model.md §3 (CMP-1360…1369, task C-339): the <c>.pproj</c> keys <c>platforms</c>, <c>activePlatform</c> and
/// <c>platformOptions</c>.
/// </summary>
public sealed partial class ProjectSerializerTests
{
    /// <summary>A project in canonical form; <paramref name="platformKeys"/> stands where the key <c>platforms</c> is written.</summary>
    private static string WithPlatformKeys(string platformKeys) => """
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
          "searchPaths": [],
          "defines": [],
          PLATFORMS
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
            "arguments": "",
            "workingDir": ""
          },
          "events": {
            "preBuild": [],
            "postBuild": []
          },
          "dependencies": []
        }

        """.Replace("PLATFORMS", platformKeys, StringComparison.Ordinal);

    private const string OnePlatform = "\"platforms\": [\n    \"win64\"\n  ],";

    private const string TwoPlatforms = "\"platforms\": [\n    \"win64\",\n    \"win32\"\n  ],";

    private const string TwoPlatformsWithOverrides = TwoPlatforms + "\n"
        + "  \"activePlatform\": \"win64\",\n"
        + "  \"platformOptions\": {\n"
        + "    \"win32\": {\n"
        + "      \"defines\": [\n        \"LEGACY\"\n      ],\n"
        + "      \"searchPaths\": [\n        \"lib32\"\n      ],\n"
        + "      \"libraryPaths\": [],\n"
        + "      \"outputDir\": \"out/$(Platform)/$(Config)\"\n"
        + "    }\n"
        + "  },";

    /// <summary>Card F03-003 acceptance 1: two platforms with per-platform overrides survive load and save unchanged.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1362")]
    [Trait("Req", "CMP-1363")]
    [Trait("Req", "CMP-1369")]
    public void Platforms_TwoPlatformsWithOverrides_RoundTripByteIdentical()
    {
        var text = Lf(WithPlatformKeys(TwoPlatformsWithOverrides));

        var p = Parse(text);

        Assert.Equal(["win64", "win32"], p.Platforms);
        Assert.Equal("win64", p.ActivePlatform);
        var options = Assert.Single(p.PlatformOptions);
        Assert.Equal("win32", options.Key);
        Assert.Equal(["LEGACY"], options.Value.Defines);
        Assert.Equal(["lib32"], options.Value.SearchPaths);
        Assert.Empty(options.Value.LibraryPaths);
        Assert.Equal("out/$(Platform)/$(Config)", options.Value.OutputDir);
        Assert.Equal(text, ProjectSerializer.Save(p));

        // a model built in code is written in the same canonical form: activePlatform directly after platforms, then platformOptions
        // with its entries in the order of platforms and the keys of an entry in the order defines, searchPaths, libraryPaths, outputDir
        var built = Parse(WithPlatformKeys(OnePlatform)) with
        {
            Platforms = ["win64", "win32"],
            ActivePlatform = "win32",
            PlatformOptions = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [
                KeyValuePair.Create("win32", new PlatformOptions(["LEGACY"], [@"lib32\x"], ["implib"], @"out\$(Platform)")),
                KeyValuePair.Create("win64", new PlatformOptions(["!", "ONLY"], [], [], null)),
            ]),
        };
        var saved = ProjectSerializer.Save(built);
        Assert.Contains(
            "  \"platforms\": [\n    \"win64\",\n    \"win32\"\n  ],\n  \"activePlatform\": \"win32\",\n  \"platformOptions\": {\n"
            + "    \"win64\": {\n      \"defines\": [\n        \"!\",\n        \"ONLY\"\n      ]\n    },\n"
            + "    \"win32\": {\n      \"defines\": [\n        \"LEGACY\"\n      ],\n      \"searchPaths\": [\n        \"lib32/x\"\n      ],\n"
            + "      \"libraryPaths\": [\n        \"implib\"\n      ],\n      \"outputDir\": \"out/$(Platform)\"\n    }\n  },\n  \"configurations\": {",
            saved, StringComparison.Ordinal);
        var again = Parse(saved);
        Assert.Equal("win32", again.ActivePlatform);
        Assert.Equal(["!", "ONLY"], again.PlatformOptions["win64"].Defines);
        Assert.Equal("out/$(Platform)", again.PlatformOptions["win32"].OutputDir);
        Assert.Equal(saved, ProjectSerializer.Save(again));

        // the optional key jobs keeps its place after the platform keys
        Assert.Contains("    }\n  },\n  \"jobs\": 4,\n  \"configurations\": {", ProjectSerializer.Save(built with { Jobs = 4 }), StringComparison.Ordinal);

        // a model the loader would refuse is not written (as for the links of configurations)
        Assert.Equal("project", Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(built with { ActivePlatform = "amiga" })).ParamName);
        Assert.Equal("project", Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(built with { Platforms = ["win64"], ActivePlatform = null })).ParamName);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1369")]
    public void Platforms_FileWithoutNewKeys_WrittenAsBefore()
    {
        // the canonical example of the suite: no key appears by itself
        var example = Parse(CanonicalExample);
        Assert.Null(example.ActivePlatform);
        Assert.Empty(example.PlatformOptions);
        var saved = ProjectSerializer.Save(example);
        Assert.Equal(Lf(CanonicalExample), saved);
        Assert.DoesNotContain("activePlatform", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("platformOptions", saved, StringComparison.Ordinal);

        // a minimal file and a new project
        var minimal = ProjectSerializer.Save(Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }"""));
        Assert.Contains("  \"platforms\": [\n    \"win64\"\n  ],\n  \"configurations\": {", minimal, StringComparison.Ordinal);
        var created = ProjectSerializer.CreateDefault("A", "A.pas", "console");
        Assert.Null(created.ActivePlatform);
        Assert.Empty(created.PlatformOptions);
        Assert.Contains("  \"platforms\": [\n    \"win64\"\n  ],\n  \"configurations\": {", ProjectSerializer.Save(created), StringComparison.Ordinal);

        // clearing the new members of a loaded model removes their keys again
        var cleared = Parse(WithPlatformKeys(TwoPlatformsWithOverrides)) with
        {
            ActivePlatform = null,
            PlatformOptions = ImmutableDictionary<string, PlatformOptions>.Empty,
        };
        Assert.Equal(Lf(WithPlatformKeys(TwoPlatforms)), ProjectSerializer.Save(cleared));
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1360")]
    [Trait("Req", "CMP-1362")]
    [Trait("Req", "CMP-1363")]
    [Trait("Req", "CMP-1366")]
    // CMP-1360: the form of an id (ids are case-sensitive: `Win64` is not `win64`), no id twice
    [InlineData("\"platforms\": [\"Win64\"]", "invalid platform id 'Win64'", "\"Win64\"")]
    [InlineData("\"platforms\": [\"win64\", \"w\"]", "invalid platform id 'w'", "\"w\"")]
    [InlineData("\"platforms\": [\"win64\", \"9x\"]", "invalid platform id '9x'", "\"9x\"")]
    [InlineData("\"platforms\": [\"win-64\"]", "invalid platform id 'win-64'", "\"win-64\"")]
    [InlineData("\"platforms\": [\"\"]", "invalid platform id ''", "\"\"]")]
    [InlineData("\"platforms\": [\"a2345678901234567\"]", "invalid platform id 'a2345678901234567'", "\"a2345678901234567\"")]
    [InlineData("\"platforms\": [\"win64\", 32]", "'platforms[1]' must be a string", "32]")]
    [InlineData("\"platforms\": \"win64\"", "'platforms' must be an array", "\"win64\"")]
    [InlineData("\"platforms\": [\"win64\", \"win32\", \"win64\"]", "platform 'win64' is listed twice", "\"win64\"]")]
    // CMP-1362: the active platform is a listed one
    [InlineData("\"platforms\": [\"win64\"], \"activePlatform\": \"win32\"", "active platform 'win32' is not listed in platforms", "\"win32\"")]
    [InlineData("\"activePlatform\": \"amiga\"", "active platform 'amiga' is not listed in platforms", "\"amiga\"")]
    [InlineData("\"platforms\": [], \"activePlatform\": \"win64\"", "active platform 'win64' is not listed in platforms", "\"win64\"")]
    [InlineData("\"activePlatform\": 64", "'activePlatform' must be a string", "64")]
    // CMP-1363: options only for listed platforms (at the key), every value of the right type
    [InlineData("\"platformOptions\": { \"win32\": {} }", "platform options for 'win32', which is not listed in platforms", "\"win32\"")]
    [InlineData("\"platforms\": [\"win64\", \"win32\"], \"platformOptions\": { \"win32\": {}, \"amiga\": { \"defines\": [] } }",
        "platform options for 'amiga', which is not listed in platforms", "\"amiga\"")]
    [InlineData("\"platformOptions\": []", "'platformOptions' must be an object", "[]")]
    [InlineData("\"platformOptions\": { \"win64\": [] }", "'platformOptions.win64' must be an object", "[] }")]
    [InlineData("\"platformOptions\": { \"win64\": \"fast\" }", "'platformOptions.win64' must be an object", "\"fast\"")]
    [InlineData("\"platformOptions\": { \"win64\": { \"defines\": \"X\" } }", "'platformOptions.win64.defines' must be an array", "\"X\"")]
    [InlineData("\"platformOptions\": { \"win64\": { \"defines\": [\"X\", 1] } }", "'platformOptions.win64.defines[1]' must be a string", "1]")]
    [InlineData("\"platformOptions\": { \"win64\": { \"searchPaths\": {} } }", "'platformOptions.win64.searchPaths' must be an array", "{} }")]
    [InlineData("\"platformOptions\": { \"win64\": { \"libraryPaths\": true } }", "'platformOptions.win64.libraryPaths' must be an array", "true")]
    [InlineData("\"platformOptions\": { \"win64\": { \"outputDir\": [\"out\"] } }", "'platformOptions.win64.outputDir' must be a string", "[\"out\"]")]
    [InlineData("\"platformOptions\": { \"win64\": { \"defines\": [\"A\", \"!\"] } }", "'!' is allowed only as the first entry of a platform's list", "\"!\"")]
    // CMP-1366: an output directory knows the macros Config and Platform only
    [InlineData("\"platformOptions\": { \"win64\": { \"outputDir\": \"out/$(Arch)\" } }",
        "'platformOptions.win64.outputDir' may use only the macros $(Config) and $(Platform)", "\"out/$(Arch)\"")]
    [InlineData("\"platformOptions\": { \"win64\": { \"outputDir\": \"$(Platform)/$(OutputDir)\" } }",
        "'platformOptions.win64.outputDir' may use only the macros $(Config) and $(Platform)", "\"$(Platform)/$(OutputDir)\"")]
    [InlineData("\"platformOptions\": { \"win64\": { \"outputDir\": \"out/$(Config\" } }",
        "'platformOptions.win64.outputDir' may use only the macros $(Config) and $(Platform)", "\"out/$(Config\"")]
    public void Platforms_SyntaxRules_E9002(string keys, string detail, string at)
    {
        var text = $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", {{keys}} }""";

        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: " + detail, d.Message);
        Assert.Equal(new(0, text.LastIndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1361")]
    public void Platforms_ListedButNotInstalled_Loads()
    {
        Assert.False(TargetRegistry.TryGet("amiga", out _));

        // a platform of another installation or of a later version: the file opens and is written back unchanged
        var text = Lf(WithPlatformKeys("\"platforms\": [\n    \"win64\",\n    \"amiga\"\n  ],\n  \"activePlatform\": \"amiga\","));
        var p = Parse(text);
        Assert.Equal(["win64", "amiga"], p.Platforms);
        Assert.Equal("amiga", p.ActivePlatform);
        Assert.Equal(text, ProjectSerializer.Save(p));

        // also as the only platform, and with options
        var only = Parse("""
            { "formatVersion": 1, "name": "A", "mainSource": "A.pas", "platforms": ["amiga"], "platformOptions": { "amiga": { "defines": ["M68K"] } } }
            """);
        Assert.Equal(["amiga"], only.Platforms);
        Assert.Null(only.ActivePlatform);
        Assert.Equal(["M68K"], only.PlatformOptions["amiga"].Defines);

        // as before the card: a registered id is accepted, and without the key the project has the baseline platform
        Assert.Equal(["win64"], Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "platforms": ["win64"] }""").Platforms);
        Assert.Equal([TargetRegistry.Default.Id], Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""").Platforms);

        // an empty list is not a syntax error (unchanged): it loads as an empty list and is written back as one
        var empty = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "platforms": [] }""");
        Assert.Empty(empty.Platforms);
        Assert.Contains("  \"platforms\": [],\n", ProjectSerializer.Save(empty), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1363")]
    public void PlatformOptions_UnknownKeysKept_EmptyEntryNotWritten()
    {
        // keys of later versions (linker, deployment) stay where they were: an entry of unknown keys only, and one between known keys
        const string Keys = TwoPlatforms + "\n"
            + "  \"platformOptions\": {\n"
            + "    \"win64\": {\n      \"x-linker\": {\n        \"stack\": 4096\n      }\n    },\n"
            + "    \"win32\": {\n      \"defines\": [\n        \"LEGACY\"\n      ],\n      \"x-deploy\": [\n        \"a.dll\"\n      ],\n"
            + "      \"outputDir\": \"out32\"\n    }\n"
            + "  },";
        var text = Lf(WithPlatformKeys(Keys));
        var p = Parse(text);
        Assert.Equal(["win32", "win64"], p.PlatformOptions.Keys.Order(StringComparer.Ordinal));
        var win64 = p.PlatformOptions["win64"];
        Assert.Empty(win64.Defines);
        Assert.Empty(win64.SearchPaths);
        Assert.Empty(win64.LibraryPaths);
        Assert.Null(win64.OutputDir);
        Assert.Equal("out32", p.PlatformOptions["win32"].OutputDir);
        Assert.Equal(text, ProjectSerializer.Save(p));

        // unknown keys follow their entry when a known key changes
        var changed = p with { PlatformOptions = p.PlatformOptions.SetItem("win32", p.PlatformOptions["win32"] with { OutputDir = null, LibraryPaths = ["implib"] }) };
        Assert.Contains(
            "    \"win32\": {\n      \"defines\": [\n        \"LEGACY\"\n      ],\n      \"x-deploy\": [\n        \"a.dll\"\n      ],\n"
            + "      \"libraryPaths\": [\n        \"implib\"\n      ]\n    }\n  },",
            ProjectSerializer.Save(changed), StringComparison.Ordinal);

        // an entry without any key is not written; without entries the key platformOptions is not written
        var oneEmpty = Parse(WithPlatformKeys(
            TwoPlatforms + "\n  \"platformOptions\": {\n    \"win64\": {},\n    \"win32\": {\n      \"outputDir\": \"out32\"\n    }\n  },"));
        Assert.Equal(2, oneEmpty.PlatformOptions.Count);
        Assert.Equal(
            Lf(WithPlatformKeys(TwoPlatforms + "\n  \"platformOptions\": {\n    \"win32\": {\n      \"outputDir\": \"out32\"\n    }\n  },")),
            ProjectSerializer.Save(oneEmpty));
        var allEmpty = Parse(WithPlatformKeys(OnePlatform + "\n  \"platformOptions\": {\n    \"win64\": {}\n  },"));
        Assert.Equal(Lf(WithPlatformKeys(OnePlatform)), ProjectSerializer.Save(allEmpty));
        var none = Parse(WithPlatformKeys(OnePlatform + "\n  \"platformOptions\": {},"));
        Assert.Empty(none.PlatformOptions);
        Assert.Equal(Lf(WithPlatformKeys(OnePlatform)), ProjectSerializer.Save(none));

        // a list that is present and empty in the file stays (the entry has a key); an entry made in code with nothing in it is not written
        var emptyList = Lf(WithPlatformKeys(OnePlatform + "\n  \"platformOptions\": {\n    \"win64\": {\n      \"defines\": []\n    }\n  },"));
        Assert.Equal(emptyList, ProjectSerializer.Save(Parse(emptyList)));
        var made = Parse(WithPlatformKeys(OnePlatform)) with
        {
            PlatformOptions = ImmutableDictionary<string, PlatformOptions>.Empty.Add("win64", new PlatformOptions([], [], [], null)),
        };
        Assert.Equal(Lf(WithPlatformKeys(OnePlatform)), ProjectSerializer.Save(made));

        // the options of a platform that left the model do not survive as unknown keys
        var removed = ProjectSerializer.Save(p with { Platforms = ["win64"], PlatformOptions = p.PlatformOptions.Remove("win32") });
        Assert.DoesNotContain("win32", removed, StringComparison.Ordinal);
        Assert.Contains("\"x-linker\"", removed, StringComparison.Ordinal);
    }
}
