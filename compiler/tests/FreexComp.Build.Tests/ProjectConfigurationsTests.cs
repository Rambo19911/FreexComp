namespace FreexComp.Build.Tests;

/// <summary>configurations.md CMP-1157, CMP-1159 (task C-298): the model operations on configurations.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ProjectConfigurationsTests
{
    private static Project Load(string text) => ConfigurationResolverTests.Load(text);

    private static Project WithConfigurations(string configurations, string projectLevel = "") => Load(
        $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas"{{projectLevel}}, "configurations": { {{configurations}} } }""");

    [Fact]
    [Trait("Req", "CMP-1159")]
    public void Rename_KeepsPositionUnknownKeysChildrenAndActive()
    {
        var p = WithConfigurations("""
            "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" },
            "Release": { "optimization": 2, "x-note": { "by": "me" }, "debugInfo": false, "defines": ["RELEASE"], "outputDir": "out/rel" },
            "Release-Demo": { "inherits": "Release", "defines": ["DEMO"] },
            "Profiling": { "inherits": "Release-Demo", "debugInfo": true },
            "Other": { "inherits": "Release" }
            """, ", \"activeConfiguration\": \"Release\"");

        var renamed = ProjectConfigurations.Rename(p, "Release", "Shipping");

        // position in the file order, own values, unknown keys
        Assert.Equal(["Debug", "Shipping", "Release-Demo", "Profiling", "Other"], renamed.ConfigurationNames);
        Assert.False(renamed.Configurations.ContainsKey("Release"));
        var shipping = renamed.Configurations["Shipping"];
        Assert.Equal((2, false, "out/rel"), (shipping.Optimization, shipping.DebugInfo, shipping.OutputDir));
        Assert.Equal(["RELEASE"], shipping.Defines);
        // every `inherits` that named it, and the active configuration
        Assert.Equal("Shipping", renamed.Configurations["Release-Demo"].Inherits);
        Assert.Equal("Shipping", renamed.Configurations["Other"].Inherits);
        Assert.Equal("Release-Demo", renamed.Configurations["Profiling"].Inherits);
        Assert.Equal("Shipping", renamed.ActiveConfiguration);
        Assert.Equal(["Release-Demo", "Other"], ProjectConfigurations.ChildrenOf(renamed, "Shipping"));

        var saved = ProjectSerializer.Save(renamed);
        Assert.Contains("""
                "Shipping": {
                  "optimization": 2,
                  "x-note": {
                    "by": "me"
                  },
                  "debugInfo": false,
                  "defines": [
                    "RELEASE"
                  ],
                  "outputDir": "out/rel"
                },
                "Release-Demo": {
                  "inherits": "Shipping",
                  "defines": [
                    "DEMO"
                  ]
                },
            """.ReplaceLineEndings("\n"), saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Release\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"activeConfiguration\": \"Shipping\"", saved, StringComparison.Ordinal);
        var reloaded = Load(saved);
        Assert.Equal(renamed.ConfigurationNames, reloaded.ConfigurationNames);
        Assert.Equal(saved, ProjectSerializer.Save(reloaded));

        // effective values of the descendants are what they were
        Assert.Equal(
            ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(p, "Profiling")).Replace("\"Release\"", "\"Shipping\"", StringComparison.Ordinal),
            ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(renamed, "Profiling")));

        // the original is untouched; a rename that leaves the active configuration alone
        Assert.Equal("Release", p.ActiveConfiguration);
        Assert.True(p.Configurations.ContainsKey("Release"));
        Assert.Equal("Release", ProjectConfigurations.Rename(p, "Debug", "Dev").ActiveConfiguration);

        // names that do not exist or already exist (compared exactly)
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Rename(p, "Missing", "X"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Rename(p, "release", "X"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Rename(p, "Release", "Debug"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Rename(p, "Release", "Release"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Rename(p, "Release", ""));
        Assert.Equal(["Debug", "debug", "Release-Demo", "Profiling", "Other"], ProjectConfigurations.Rename(p, "Release", "debug").ConfigurationNames);
    }

    [Fact]
    [Trait("Req", "CMP-1159")]
    public void Rename_DefaultOutputDirFollows_WrittenOneStays()
    {
        var p = WithConfigurations("""
            "Root": { "optimization": 1 },
            "Demo": { "inherits": "Root" },
            "Written": { "inherits": "Root", "outputDir": "bin/Written" },
            "Custom": { "inherits": "Root", "outputDir": "out/custom" }
            """);

        // not written in the file and the default: follows the new name (root and derived)
        Assert.Equal("bin/Trial", ProjectConfigurations.Rename(p, "Demo", "Trial").Configurations["Trial"].OutputDir);
        Assert.Equal("bin/Base", ProjectConfigurations.Rename(p, "Root", "Base").Configurations["Base"].OutputDir);
        // written in the file — even with the default text — or another folder: left alone
        Assert.Equal("bin/Written", ProjectConfigurations.Rename(p, "Written", "Kept").Configurations["Kept"].OutputDir);
        Assert.Equal("out/custom", ProjectConfigurations.Rename(p, "Custom", "Kept").Configurations["Kept"].OutputDir);

        var saved = ProjectSerializer.Save(ProjectConfigurations.Rename(ProjectConfigurations.Rename(p, "Demo", "Trial"), "Written", "Kept"));
        Assert.Contains("    \"Trial\": {\n      \"inherits\": \"Root\"\n    },\n", saved, StringComparison.Ordinal);
        Assert.Contains("    \"Kept\": {\n      \"inherits\": \"Root\",\n      \"outputDir\": \"bin/Written\"\n    },\n", saved, StringComparison.Ordinal);
        Assert.Equal(@"C:\p\Hello\bin\Trial\A.exe", ProjectOptions.GetOutputPath(ProjectConfigurations.Rename(p, "Demo", "Trial"), "Trial"));

        // a project that was never loaded: nothing is written in a file yet
        var fresh = ProjectSerializer.CreateDefault("N", "N.pas", "console");
        var renamed = ProjectConfigurations.Rename(fresh, "Debug", "Dev");
        Assert.Equal("bin/Dev", renamed.Configurations["Dev"].OutputDir);
        Assert.Equal("Dev", renamed.ActiveConfiguration);
        Assert.Equal("out", ProjectConfigurations.Rename(
            fresh with { Configurations = fresh.Configurations.SetItem("Debug", fresh.Configurations["Debug"] with { OutputDir = "out" }) }, "Debug", "Dev")
            .Configurations["Dev"].OutputDir);
    }

    [Theory]
    [Trait("Req", "CMP-1159")]
    // without a reset marker: the chain's entries, without the project level
    [InlineData("""
        "Root": { "optimization": 2, "debugInfo": true, "defines": ["R"], "searchPaths": ["r"], "rtti": "published" },
        "Mid": { "inherits": "Root", "defines": ["M"], "searchPaths": ["m"] },
        "Leaf": { "inherits": "Mid", "debugInfo": false, "defines": ["L"] }
        """, new[] { "R", "M", "L" }, new[] { "r", "m" }, "published")]
    // with a reset marker in effect: preceded by "!" (for that list only)
    [InlineData("""
        "Root": { "optimization": 1, "defines": ["R"], "searchPaths": ["r"] },
        "Mid": { "inherits": "Root", "defines": ["!", "M"], "searchPaths": ["m"] },
        "Leaf": { "inherits": "Mid", "defines": ["L"], "outputDir": "out/leaf" }
        """, new[] { "!", "M", "L" }, new[] { "r", "m" }, null)]
    [InlineData("""
        "Root": { "defines": ["!", "R"], "searchPaths": ["r"] },
        "Mid": { "inherits": "Root", "searchPaths": ["!"] },
        "Leaf": { "inherits": "Mid" }
        """, new[] { "!", "R" }, new[] { "!" }, null)]
    public void Detach_KeepsEffectiveValues(string configurations, string[] defines, string[] searchPaths, string? rtti)
    {
        var p = WithConfigurations(configurations, ", \"defines\": [\"P\"], \"searchPaths\": [\"p\"], \"rtti\": \"broad\"");
        var before = ConfigurationResolver.Resolve(p, "Leaf");

        var detached = ProjectConfigurations.Detach(p, "Leaf");

        var leaf = detached.Configurations["Leaf"];
        Assert.Null(leaf.Inherits);
        Assert.False(leaf.InheritsOptimization || leaf.InheritsDebugInfo);
        Assert.Equal(defines, leaf.Defines);
        Assert.Equal(searchPaths, leaf.SearchPaths);
        Assert.Equal(rtti, leaf.Rtti);
        Assert.Equal(p.Configurations["Leaf"].OutputDir, leaf.OutputDir);

        // resolve before = resolve after (but for the chain, which is now the configuration alone)
        var after = ConfigurationResolver.Resolve(detached, "Leaf");
        Assert.Equal(["Leaf"], after.Chain);
        Assert.Equal(ConfigurationResolver.ToJson(before with { Chain = ["Leaf"] }), ConfigurationResolver.ToJson(after));

        // … also after a save and a reload; the other configurations are untouched
        var saved = ProjectSerializer.Save(detached);
        var reloaded = Load(saved);
        Assert.Equal(ConfigurationResolver.ToJson(after), ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(reloaded, "Leaf")));
        Assert.Equal(saved, ProjectSerializer.Save(reloaded));
        Assert.Equal(["Root", "Mid", "Leaf"], reloaded.ConfigurationNames);
        Assert.Equal("Root", reloaded.Configurations["Mid"].Inherits);
        Assert.Equal(
            ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(p, "Mid")),
            ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(detached, "Mid")));

        // a root configuration stays as it is; an unknown name throws
        Assert.Same(p, ProjectConfigurations.Detach(p, "Root"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.Detach(p, "leaf"));
    }

    [Fact]
    [Trait("Req", "CMP-1159")]
    public void ChildrenAndDescendants_FileOrder()
    {
        var p = WithConfigurations("""
            "Zeta": { "inherits": "Root" },
            "Root": {},
            "Leaf": { "inherits": "Alpha" },
            "Alpha": { "inherits": "Root" },
            "Twig": { "inherits": "Leaf" },
            "Alone": {},
            "Beta": { "inherits": "Zeta" }
            """);

        Assert.Equal(["Zeta", "Alpha"], ProjectConfigurations.ChildrenOf(p, "Root"));
        Assert.Equal(["Zeta", "Leaf", "Alpha", "Twig", "Beta"], ProjectConfigurations.DescendantsOf(p, "Root"));
        Assert.Equal(["Leaf"], ProjectConfigurations.ChildrenOf(p, "Alpha"));
        Assert.Equal(["Leaf", "Twig"], ProjectConfigurations.DescendantsOf(p, "Alpha"));
        Assert.Equal(["Beta"], ProjectConfigurations.DescendantsOf(p, "Zeta"));
        Assert.Empty(ProjectConfigurations.ChildrenOf(p, "Alone"));
        Assert.Empty(ProjectConfigurations.DescendantsOf(p, "Twig"));

        // a configuration added to the model comes after those of the file
        var added = p with { Configurations = p.Configurations.Add("Added", new ProjectConfiguration(0, false, [], "bin/Added") { Inherits = "Root" }) };
        Assert.Equal(["Zeta", "Alpha", "Added"], ProjectConfigurations.ChildrenOf(added, "Root"));

        Assert.Throws<ArgumentException>(() => ProjectConfigurations.ChildrenOf(p, "root"));
        Assert.Throws<ArgumentException>(() => ProjectConfigurations.DescendantsOf(p, "Missing"));
    }

    [Fact]
    [Trait("Req", "CMP-1157")]
    public void Save_InvalidLinks_Throws()
    {
        var p = Load(ConfigurationResolverTests.Example);
        var release = p.Configurations["Release"];

        // a link to a configuration that is not there (removed parent, or a name that differs in case)
        var orphan = Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(p with { Configurations = p.Configurations.Remove("Release") }));
        Assert.Contains("'Release-Demo' inherits from unknown configuration 'Release'", orphan.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(
            p with { Configurations = p.Configurations.SetItem("Debug", p.Configurations["Debug"] with { Inherits = "release" }) }));

        // a cycle, and a self reference
        var cycle = Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(
            p with { Configurations = p.Configurations.SetItem("Release", release with { Inherits = "Profiling" }) }));
        Assert.Contains("Circular configuration inheritance", cycle.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ProjectSerializer.Save(
            p with { Configurations = p.Configurations.SetItem("Release", release with { Inherits = "Release" }) }));

        // the model operations never produce one
        var text = ProjectSerializer.Save(ProjectConfigurations.Detach(ProjectConfigurations.Rename(p, "Release", "Shipping"), "Release-Demo"));
        Assert.Empty(ProjectSerializer.ParseProject(ConfigurationResolverTests.ProjectPath, text).Diagnostics);
    }
}
