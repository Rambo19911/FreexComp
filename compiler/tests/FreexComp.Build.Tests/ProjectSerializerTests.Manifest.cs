namespace FreexComp.Build.Tests;

/// <summary>app-manifest.md §4 (CMP-1110…1112, task C-296): the <c>.pproj</c> <c>manifest</c> object.</summary>
public sealed partial class ProjectSerializerTests
{
    private static string WithManifest(string manifest) => """
        {
          "formatVersion": 1,
          "name": "Hello",
          "language": "pascal",
          "appType": "gui",
          "mainSource": "Hello.pas",
          "files": [
            {
              "path": "Hello.pas"
            }
          ],
          "searchPaths": [],
          "manifest": MANIFEST,
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
            "arguments": "",
            "workingDir": ""
          },
          "events": {
            "preBuild": [],
            "postBuild": []
          },
          "dependencies": []
        }

        """.Replace("MANIFEST", manifest, StringComparison.Ordinal);

    public static TheoryData<string, string?, string?, string?> ManifestObjects => new()
    {
        { "{}", null, null, null },
        { "{\n    \"executionLevel\": \"requireAdministrator\"\n  }", "requireAdministrator", null, null },
        { "{\n    \"dpiAwareness\": \"system\"\n  }", null, "system", null },
        { "{\n    \"executionLevel\": \"highestAvailable\",\n    \"dpiAwareness\": \"unaware\"\n  }", "highestAvailable", "unaware", null },
        { "{\n    \"executionLevel\": \"asInvoker\",\n    \"dpiAwareness\": \"perMonitorV2\"\n  }", "asInvoker", "perMonitorV2", null },
        { "{\n    \"customFile\": \"res/app.manifest\"\n  }", null, null, "res/app.manifest" },
        // an unknown key stays where it was (CMP-811)
        { "{\n    \"executionLevel\": \"asInvoker\",\n    \"x-note\": [\n      1\n    ],\n    \"dpiAwareness\": \"system\"\n  }", "asInvoker", "system", null },
    };

    [Theory]
    [MemberData(nameof(ManifestObjects))]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1110")]
    [Trait("Req", "CMP-1112")]
    public void ManifestObject_RoundTripsByteIdentical(string manifest, string? level, string? dpi, string? customFile)
    {
        var text = Lf(WithManifest(manifest));

        var p = Parse(text);

        Assert.Null(p.Manifest);
        Assert.Equal(new ManifestSettings(level, dpi, customFile), p.ManifestSettings);
        Assert.Equal(text, ProjectSerializer.Save(p));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1110")]
    [Trait("Req", "CMP-1112")]
    public void ManifestObject_WritesKeysInCanonicalOrder_AndRejectsBothForms()
    {
        var p = Parse(WithManifest("\"default\"")) with { Manifest = null, ManifestSettings = new ManifestSettings("highestAvailable", "system", null) };

        var saved = ProjectSerializer.Save(p);

        Assert.Equal(Lf(WithManifest("{\n    \"executionLevel\": \"highestAvailable\",\n    \"dpiAwareness\": \"system\"\n  }")), saved);
        Assert.Equal(p.ManifestSettings, Parse(saved).ManifestSettings);

        // a custom file written with '\' is saved with '/'; non-canonical key order is canonicalised
        var custom = Parse(WithManifest("\"none\"")) with { Manifest = null, ManifestSettings = new ManifestSettings(null, null, @"res\app.manifest") };
        Assert.Contains("\"manifest\": {\n    \"customFile\": \"res/app.manifest\"\n  },", ProjectSerializer.Save(custom), StringComparison.Ordinal);
        var reordered = Parse(WithManifest("{ \"dpiAwareness\": \"system\", \"executionLevel\": \"highestAvailable\" }"));
        Assert.Equal(saved, ProjectSerializer.Save(reordered));

        // CMP-1112: both forms on one model
        var both = p with { Manifest = "default" };
        var e = Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(both));
        Assert.Equal("project", e.ParamName);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1111")]
    [InlineData("\"manifest\": 5", "'manifest' must be a string or an object", "5")]
    [InlineData("\"manifest\": [\"default\"]", "'manifest' must be a string or an object", "[\"default\"]")]
    [InlineData("\"manifest\": true", "'manifest' must be a string or an object", "true")]
    [InlineData("\"manifest\": { \"executionLevel\": \"admin\" }",
        "'manifest.executionLevel' must be \"asInvoker\", \"highestAvailable\" or \"requireAdministrator\"", "\"admin\"")]
    [InlineData("\"manifest\": { \"executionLevel\": \"AsInvoker\" }",
        "'manifest.executionLevel' must be \"asInvoker\", \"highestAvailable\" or \"requireAdministrator\"", "\"AsInvoker\"")]
    [InlineData("\"manifest\": { \"dpiAwareness\": \"perMonitor\" }",
        "'manifest.dpiAwareness' must be \"perMonitorV2\", \"system\" or \"unaware\"", "\"perMonitor\"")]
    [InlineData("\"manifest\": { \"executionLevel\": 1 }", "'manifest.executionLevel' must be a string", "1 }")]
    [InlineData("\"manifest\": { \"dpiAwareness\": true }", "'manifest.dpiAwareness' must be a string", "true }")]
    [InlineData("\"manifest\": { \"customFile\": [] }", "'manifest.customFile' must be a string", "[] }")]
    [InlineData("\"manifest\": { \"customFile\": \"\" }", "'manifest.customFile' must not be empty", "\"\" }")]
    [InlineData("\"manifest\": { \"executionLevel\": \"asInvoker\", \"customFile\": \"a.manifest\" }",
        "'manifest.customFile' cannot be combined with 'executionLevel' or 'dpiAwareness'", "\"a.manifest\"")]
    [InlineData("\"manifest\": { \"customFile\": \"a.manifest\", \"dpiAwareness\": \"system\" }",
        "'manifest.customFile' cannot be combined with 'executionLevel' or 'dpiAwareness'", "\"a.manifest\"")]
    public void ManifestObject_InvalidValues_E9002(string key, string detail, string at)
    {
        var text = $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", {{key}} }""";

        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: " + detail, d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1112")]
    [InlineData("\"default\"", "default")]
    [InlineData("\"none\"", "none")]
    [InlineData("\"res/app.manifest\"", "res/app.manifest")]
    public void ManifestStringForms_Unchanged(string manifest, string expected)
    {
        var text = Lf(WithManifest(manifest));

        var p = Parse(text);

        Assert.Equal(expected, p.Manifest);
        Assert.Null(p.ManifestSettings);
        Assert.Equal(text, ProjectSerializer.Save(p));

        // absent stays absent; the interface example (string form) still round-trips
        var absent = Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""");
        Assert.Null(absent.Manifest);
        Assert.Null(absent.ManifestSettings);
        Assert.DoesNotContain("manifest", ProjectSerializer.Save(absent), StringComparison.Ordinal);
        Assert.Equal(Lf(CanonicalExample), ProjectSerializer.Save(Parse(CanonicalExample)));
    }
}
