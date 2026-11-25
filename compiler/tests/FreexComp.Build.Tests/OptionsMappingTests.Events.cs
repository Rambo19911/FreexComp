namespace FreexComp.Build.Tests;

/// <summary>build-events.md CMP-1252 (task C-327): the events a configuration is built with.</summary>
public sealed partial class OptionsMappingTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1252")]
    public void EffectiveEvents_NearestConfigurationWins()
    {
        var p = Load("""
            {
              "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "configurations": {
                "Debug":   { "optimization": 0 },
                "Release": { "optimization": 2, "events": { "preBuild": ["rel pre"], "postBuild": ["rel post 1", "rel post 2"], "postBuildWhen": "always" } },
                "Demo":    { "inherits": "Release" },
                "Trial":   { "inherits": "Demo" },
                "Own":     { "inherits": "Release", "events": { "postBuild": ["own post"] } },
                "Quiet":   { "inherits": "Own", "events": {} },
                "Below":   { "inherits": "Quiet" }
              },
              "events": { "preBuild": ["project pre"], "postBuild": ["project post"] }
            }
            """);

        static string Of(ProjectEvents e) => $"pre[{string.Join("|", e.PreBuild)}] post[{string.Join("|", e.PostBuild)}] {e.PostBuildWhen}";

        // none in the chain → the project level
        Assert.Equal("pre[project pre] post[project post] Success", Of(ProjectOptions.GetEffectiveEvents(p, "Debug")));
        // own; the parent's; the grandparent's — the whole value, postBuildWhen travels with the lists
        const string Release = "pre[rel pre] post[rel post 1|rel post 2] Always";
        Assert.Equal(Release, Of(ProjectOptions.GetEffectiveEvents(p, "Release")));
        Assert.Equal(Release, Of(ProjectOptions.GetEffectiveEvents(p, "Demo")));
        Assert.Equal(Release, Of(ProjectOptions.GetEffectiveEvents(p, "Trial")));
        // the nearest one replaces: no merging with the parent's or the project's lists, no inherited postBuildWhen
        Assert.Equal("pre[] post[own post] Success", Of(ProjectOptions.GetEffectiveEvents(p, "Own")));
        // {} runs nothing, also for a configuration derived from it
        Assert.Equal("pre[] post[] Success", Of(ProjectOptions.GetEffectiveEvents(p, "Quiet")));
        Assert.Equal("pre[] post[] Success", Of(ProjectOptions.GetEffectiveEvents(p, "Below")));

        // an exact name, as for every other per-configuration value
        Assert.Throws<ArgumentException>(() => ProjectOptions.GetEffectiveEvents(p, "release"));
    }
}
