using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>build-events.md §3 (CMP-1250, 1251, task C-327): <c>events.postBuildWhen</c> and the <c>events</c> object of a configuration.</summary>
public sealed partial class ProjectSerializerTests
{
    private static string WithEvents(string events) =>
        $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "events": {{events}} }""";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1250")]
    public void Events_PostBuildWhen_RoundTripsAndDefaults()
    {
        // absent → Success; a project that never used the key keeps its bytes on save
        var example = Parse(CanonicalExample);
        Assert.Equal(PostBuildWhen.Success, example.Events.PostBuildWhen);
        Assert.Equal(Lf(CanonicalExample), ProjectSerializer.Save(example));
        Assert.Equal(PostBuildWhen.Success, Parse("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""").Events.PostBuildWhen);
        Assert.DoesNotContain("postBuildWhen", ProjectSerializer.Save(Parse(WithEvents("""{ "preBuild": ["a"], "postBuild": ["b"] }"""))), StringComparison.Ordinal);

        // "always" is read, and written after postBuild also when it was set in the model
        var always = Parse(WithEvents("""{ "postBuildWhen": "always", "postBuild": ["b"], "preBuild": ["a"] }"""));
        Assert.Equal(PostBuildWhen.Always, always.Events.PostBuildWhen);
        const string Expected = """
              "events": {
                "preBuild": [
                  "a"
                ],
                "postBuild": [
                  "b"
                ],
                "postBuildWhen": "always"
              },
            """;
        Assert.Contains(Lf(Expected), ProjectSerializer.Save(always), StringComparison.Ordinal);
        var set = Parse(WithEvents("""{ "preBuild": ["a"], "postBuild": ["b"] }"""));
        set = set with { Events = set.Events with { PostBuildWhen = PostBuildWhen.Always } };
        Assert.Contains(Lf(Expected), ProjectSerializer.Save(set), StringComparison.Ordinal);
        Assert.Equal(PostBuildWhen.Always, Parse(ProjectSerializer.Save(set)).Events.PostBuildWhen);

        // "success" is kept when the file has it
        var success = Parse(WithEvents("""{ "preBuild": [], "postBuild": [], "postBuildWhen": "success" }"""));
        Assert.Equal(PostBuildWhen.Success, success.Events.PostBuildWhen);
        Assert.Contains("\"postBuild\": [],\n    \"postBuildWhen\": \"success\"\n", ProjectSerializer.Save(success), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1251")]
    public void Events_PerConfiguration_RoundTripsInCanonicalForm()
    {
        var text = WithConfigurations("""
            "Debug": { "events": { "postBuildWhen": "always", "x-note": 1, "postBuild": ["echo d2"], "preBuild": ["echo \"d 1\""] }, "x-after": true, "optimization": 0, "debugInfo": true },
            "Empty": { "inherits": "Debug", "events": {} },
            "Lists": { "inherits": "Debug", "rtti": "published", "events": { "preBuild": [] } },
            "Plain": { "inherits": "Debug" }
            """.Trim());

        var p = Parse(text);

        Assert.Equal(["echo \"d 1\""], p.Configurations["Debug"].Events!.PreBuild);
        Assert.Equal(["echo d2"], p.Configurations["Debug"].Events!.PostBuild);
        Assert.Equal(PostBuildWhen.Always, p.Configurations["Debug"].Events!.PostBuildWhen);
        var empty = p.Configurations["Empty"].Events;
        Assert.NotNull(empty);                                                   // {} is an events value of its own, with the defaults
        Assert.Empty(empty.PreBuild);
        Assert.Empty(empty.PostBuild);
        Assert.Equal(PostBuildWhen.Success, empty.PostBuildWhen);
        Assert.Null(p.Configurations["Plain"].Events);
        Assert.Empty(p.Events.PreBuild);                                         // the project level is not touched

        // the last known key of the configuration; {} kept; the unknown member kept; keys in the order preBuild, postBuild, postBuildWhen
        const string Expected = """
              "configurations": {
                "Debug": {
                  "optimization": 0,
                  "debugInfo": true,
                  "defines": [],
                  "outputDir": "bin/Debug",
                  "events": {
                    "preBuild": [
                      "echo \"d 1\""
                    ],
                    "postBuild": [
                      "echo d2"
                    ],
                    "postBuildWhen": "always",
                    "x-note": 1
                  },
                  "x-after": true
                },
                "Empty": {
                  "inherits": "Debug",
                  "events": {}
                },
                "Lists": {
                  "inherits": "Debug",
                  "rtti": "published",
                  "events": {
                    "preBuild": []
                  }
                },
                "Plain": {
                  "inherits": "Debug"
                }
              },
            """;
        var saved = ProjectSerializer.Save(p);
        Assert.Contains(Lf(Expected), saved, StringComparison.Ordinal);
        Assert.Equal(saved, ProjectSerializer.Save(Parse(saved)));               // canonical form is stable

        // set and removed in the model
        var changed = p with
        {
            Configurations = p.Configurations
                .SetItem("Plain", p.Configurations["Plain"] with { Events = new ProjectEvents(["echo p"], []) })
                .SetItem("Empty", p.Configurations["Empty"] with { Events = null }),
        };
        var changedText = ProjectSerializer.Save(changed);
        Assert.Contains("\"Empty\": {\n      \"inherits\": \"Debug\"\n    },", changedText, StringComparison.Ordinal);
        Assert.Contains(
            "\"Plain\": {\n      \"inherits\": \"Debug\",\n      \"events\": {\n        \"preBuild\": [\n          \"echo p\"\n        ]\n      }\n    }",
            changedText, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1250")]
    [Trait("Req", "CMP-1251")]
    [InlineData("\"events\": { \"postBuildWhen\": \"never\" }", "", "'events.postBuildWhen' must be \"success\" or \"always\"", "\"never\"")]
    [InlineData("\"events\": { \"postBuildWhen\": \"Always\" }", "", "'events.postBuildWhen' must be \"success\" or \"always\"", "\"Always\"")]
    [InlineData("\"events\": { \"postBuildWhen\": 1 }", "", "'events.postBuildWhen' must be a string", "1 }")]
    [InlineData("", "\"events\": [\"echo\"]", "'configurations.Debug.events' must be an object", "[\"echo\"]")]
    [InlineData("", "\"events\": \"echo\"", "'configurations.Debug.events' must be an object", "\"echo\"")]
    [InlineData("", "\"events\": { \"preBuild\": \"echo\" }", "'configurations.Debug.events.preBuild' must be an array", "\"echo\"")]
    [InlineData("", "\"events\": { \"postBuild\": [1] }", "'configurations.Debug.events.postBuild[0]' must be a string", "1]")]
    [InlineData("", "\"events\": { \"postBuildWhen\": true }", "'configurations.Debug.events.postBuildWhen' must be a string", "true }")]
    [InlineData("", "\"events\": { \"postBuildWhen\": \"\" }", "'configurations.Debug.events.postBuildWhen' must be \"success\" or \"always\"", "\"\" }")]
    public void Events_WrongValues_E9002(string projectLevel, string inDebug, string detail, string at)
    {
        var text = WithConfigurations($$"""
            "Debug": { {{inDebug}} }
            """.Trim(), projectLevel.Length > 0 ? ", " + projectLevel : "");

        var d = ParseError(text);

        Assert.Equal("E9002", d.Code);
        Assert.Equal("Invalid project file: " + detail, d.Message);
        Assert.Equal(new(0, text.IndexOf(at, StringComparison.Ordinal)), d.Location.LineSpan.Start);
    }
}
