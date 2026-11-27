namespace FreexComp.Build.Tests;

/// <summary>resource-manager.md CMP-1427: the <c>.pproj</c> keys <c>icon</c>, <c>versionInfo</c> and <c>resources</c>.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ProjectFileTests
{
    private const string ProjectPath = @"C:\p\Hello.pproj";

    /// <summary>The keys in canonical form and order, with an unknown key inside <c>versionInfo</c> and inside one entry.</summary>
    private const string WithKeys = """
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
          "icon": "res/app.ico",
          "versionInfo": {
            "fileVersion": "1.2",
            "companyName": "Example \"Co\"",
            "x-note": "kept",
            "comments": ""
          },
          "resources": [
            {
              "path": "data/data.txt",
              "type": "RCDATA",
              "name": "MYDATA"
            },
            {
              "path": "res/second.ico",
              "type": "ICON",
              "name": "2",
              "x-origin": "kept too"
            }
          ],
          "dependencies": []
        }

        """;

    private static Project Parse(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, text.ReplaceLineEndings("\n"));
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Project>(result.Value);
    }

    [Fact]
    [Trait("Req", "CMP-1427")]
    public void ResourceKeysRoundTrip()
    {
        // read: every key, absent inner keys are null
        var project = Parse(WithKeys);
        Assert.Equal("res/app.ico", project.Icon);
        Assert.Equal(
            new ProjectVersionInfo { FileVersion = "1.2", CompanyName = "Example \"Co\"", Comments = "" },
            project.VersionInfo);
        Assert.Equal([new ProjectResource("data/data.txt", "RCDATA", "MYDATA"), new ProjectResource("res/second.ico", "ICON", "2")], project.Resources);

        // written back byte for byte: canonical order, unknown inner keys where they were
        Assert.Equal(WithKeys.ReplaceLineEndings("\n"), ProjectSerializer.Save(project));

        // an edit keeps the unknown key of the entry it belongs to (matched by path), also after reordering
        var reordered = project with
        {
            Resources = [project.Resources[1] with { Name = "3" }, project.Resources[0]],
            VersionInfo = project.VersionInfo! with { ProductVersion = "2.0" },
        };
        var saved = ProjectSerializer.Save(reordered);
        Assert.Contains("\"name\": \"3\",\n      \"x-origin\": \"kept too\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"fileVersion\": \"1.2\",\n    \"productVersion\": \"2.0\",\n    \"companyName\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"x-note\": \"kept\"", saved, StringComparison.Ordinal);
        Assert.Equal(reordered.Resources, Parse(saved).Resources);

        // absent keys are not written: an existing project stays byte-identical
        var plain = Parse(ProjectSerializerTests.CanonicalExample);
        Assert.Null(plain.Icon);
        Assert.Null(plain.VersionInfo);
        Assert.Empty(plain.Resources);
        Assert.Equal(ProjectSerializerTests.CanonicalExample.ReplaceLineEndings("\n"), ProjectSerializer.Save(plain));

        // removing the keys from the model removes them from the file; an empty list that was present stays
        var removed = ProjectSerializer.Save(project with { Icon = null, VersionInfo = null, Resources = [] });
        Assert.DoesNotContain("\"icon\"", removed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"versionInfo\"", removed, StringComparison.Ordinal);
        Assert.Contains("\"resources\": [],\n  \"dependencies\"", removed, StringComparison.Ordinal);

        // a new model writes the keys after "events", in the documented order
        var created = ProjectSerializer.Save(ProjectSerializer.CreateDefault("New", "New.pas", "console") with
        {
            Icon = @"res\app.ico",
            VersionInfo = new ProjectVersionInfo { FileVersion = "1.0.0.0" },
            Resources = [new ProjectResource(@"data\a.bin", "RCDATA", "A")],
        });
        Assert.Contains(
            "\"icon\": \"res/app.ico\",\n  \"versionInfo\": {\n    \"fileVersion\": \"1.0.0.0\"\n  },\n  \"resources\": [\n    {\n      \"path\": \"data/a.bin\",\n"
            + "      \"type\": \"RCDATA\",\n      \"name\": \"A\"\n    }\n  ],\n  \"dependencies\": []",
            created,
            StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "CMP-1427")]
    [InlineData("\"icon\": 1", "'icon' must be a string")]
    [InlineData("\"icon\": \"\"", "'icon' must not be empty")]
    [InlineData("\"versionInfo\": []", "'versionInfo' must be an object")]
    [InlineData("\"versionInfo\": { \"companyName\": 1 }", "'versionInfo.companyName' must be a string")]
    [InlineData("\"resources\": {}", "'resources' must be an array")]
    [InlineData("\"resources\": [ { \"type\": \"RCDATA\", \"name\": \"A\" } ]", "missing required key 'path'")]
    [InlineData("\"resources\": [ { \"path\": \"\", \"type\": \"RCDATA\", \"name\": \"A\" } ]", "'resources[0].path' must not be empty")]
    [InlineData("\"resources\": [ { \"path\": \"a\", \"type\": \"RCDATA\", \"name\": 2 } ]", "'resources[0].name' must be a string")]
    public void WrongJsonShape_E9002(string key, string detail)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, $$"""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", {{key}} }""");

        Assert.Null(result.Value);
        var d = Assert.Single(result.Diagnostics);
        Assert.Equal(("E9002", "Invalid project file: " + detail), (d.Code, d.Message));
    }
}
