using FreexComp.Compiler.Targets;

namespace FreexComp.Build.Tests;

/// <summary>
/// platform-model.md §3 (CMP-1364…1368, task C-339): the effective platform, the per-platform lists and the output directory of a
/// platform. The second platform is a descriptor outside the registry.
/// </summary>
public sealed partial class OptionsMappingTests
{
    private static TargetInfo Test32 => TestPlatforms.Test32;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1364")]
    public void EffectivePlatform_RequestThenActiveThenFirstThenBaseline()
    {
        var p = Load("""
            { "formatVersion": 1, "name": "A", "mainSource": "A.pas", "platforms": ["test32", "win64", "other"], "activePlatform": "win64" }
            """);

        // the request wins, also over the active platform and also when the project does not list it (the engine checks that)
        Assert.Equal("other", ProjectOptions.GetEffectivePlatform(p, "other"));
        Assert.Equal("amiga", ProjectOptions.GetEffectivePlatform(p, "amiga"));

        // else the active platform
        Assert.Equal("win64", ProjectOptions.GetEffectivePlatform(p));
        Assert.Equal("win64", ProjectOptions.GetEffectivePlatform(p, null));
        Assert.Equal("other", ProjectOptions.GetEffectivePlatform(p with { ActivePlatform = "other" }));

        // else the first listed platform
        Assert.Equal("test32", ProjectOptions.GetEffectivePlatform(p with { ActivePlatform = null }));

        // else the baseline platform: a file without the key, a new project, and a model without platforms
        Assert.Equal("win64", ProjectOptions.GetEffectivePlatform(Load("""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }""")));
        Assert.Equal(TargetRegistry.Default.Id, ProjectOptions.GetEffectivePlatform(ProjectSerializer.CreateDefault("A", "A.pas", "console")));
        Assert.Equal(TargetRegistry.Default.Id, ProjectOptions.GetEffectivePlatform(p with { ActivePlatform = null, Platforms = [] }));
        Assert.Equal(TargetRegistry.Default.Id, ProjectOptions.GetEffectivePlatform(p with { ActivePlatform = null, Platforms = default }));
        Assert.Equal("test32", ProjectOptions.GetEffectivePlatform(p with { ActivePlatform = null, Platforms = [] }, "test32"));

        Assert.Throws<ArgumentNullException>(() => ProjectOptions.GetEffectivePlatform(null!));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1365")]
    [Trait("Req", "CMP-1368")]
    public void PlatformLists_FollowProjectAndConfigurationChain_BangDiscards()
    {
        var p = Load("""
            {
              "formatVersion": 1, "name": "App", "mainSource": "App.pas",
              "defines": ["APP"], "searchPaths": ["lib"], "libraryPaths": ["implib"],
              "platforms": ["win64", "test32", "other"],
              "platformOptions": {
                "test32": { "defines": ["LEGACY", "APP"], "searchPaths": ["lib32", "LIB"], "libraryPaths": ["implib32"] },
                "other": { "defines": ["!", "ONLY"], "searchPaths": ["!", "only"], "libraryPaths": ["!"] }
              },
              "configurations": {
                "Base": { "optimization": 0, "defines": ["BASE"], "searchPaths": ["base"] },
                "Debug": { "inherits": "Base", "defines": ["DEBUG"], "searchPaths": ["dbg"] },
                "Clean": { "inherits": "Debug", "defines": ["!", "CLEAN"], "searchPaths": ["!"] }
              },
              "activeConfiguration": "Debug"
            }
            """);

        // project, then the configuration chain, then the platform (CMP-1365); a platform without an entry adds nothing
        var baseline = ProjectOptions.ToCompilationOptions(p, "Debug", TargetInfo.Win64);
        Assert.Equal(["APP", "BASE", "DEBUG"], baseline.Defines);
        Assert.Equal([@"C:\p\Hello\lib", @"C:\p\Hello\base", @"C:\p\Hello\dbg", @"C:\p\Hello"], baseline.SearchPaths);
        Assert.Equal([@"C:\p\Hello\implib"], baseline.LibraryPaths);

        var test32 = ProjectOptions.ToCompilationOptions(p, "Debug", Test32);
        Assert.Equal(["APP", "BASE", "DEBUG", "LEGACY", "APP"], test32.Defines);
        Assert.Equal([@"C:\p\Hello\lib", @"C:\p\Hello\base", @"C:\p\Hello\dbg", @"C:\p\Hello\lib32", @"C:\p\Hello"], test32.SearchPaths);   // "LIB" is "lib" again
        Assert.Equal([@"C:\p\Hello\implib", @"C:\p\Hello\implib32"], test32.LibraryPaths);
        Assert.Same(Test32, test32.Target);                                                    // CMP-1010: the predefined symbols stay the target's
        Assert.Equal(@"C:\p\Hello\obj\test32\Debug", test32.ObjectDirectory);

        // the platform's lists come after a configuration's reset
        var clean = ProjectOptions.ToCompilationOptions(p, "Clean", Test32);
        Assert.Equal(["CLEAN", "LEGACY", "APP"], clean.Defines);
        Assert.Equal([@"C:\p\Hello\lib32", @"C:\p\Hello\LIB", @"C:\p\Hello"], clean.SearchPaths);

        // "!" as the first entry of a platform list discards the project's and the configurations' entries
        var other = ProjectOptions.ToCompilationOptions(p, "Debug", Test32 with { Id = "other" });
        Assert.Equal(["ONLY"], other.Defines);
        Assert.Equal([@"C:\p\Hello\only", @"C:\p\Hello"], other.SearchPaths);                  // the directories of the project's units stay
        Assert.Empty(other.LibraryPaths);

        // CMP-1368: without a descriptor, the registered one of the effective platform — or the baseline's when it is not installed
        var byDefault = ProjectOptions.ToCompilationOptions(p, "Debug");
        Assert.Same(TargetInfo.Win64, byDefault.Target);
        Assert.Equal(baseline.Defines, byDefault.Defines);
        var notInstalled = ProjectOptions.ToCompilationOptions(p with { ActivePlatform = "test32" }, "Debug");
        Assert.Same(TargetInfo.Win64, notInstalled.Target);
        Assert.Equal(baseline.Defines, notInstalled.Defines);
        Assert.Equal(@"C:\p\Hello\obj\Debug", notInstalled.ObjectDirectory);
        var withBaselineEntry = p with { PlatformOptions = p.PlatformOptions.Add("win64", new PlatformOptions(["W64"], [], [], null)) };
        Assert.Equal(["APP", "BASE", "DEBUG", "W64"], ProjectOptions.ToCompilationOptions(withBaselineEntry, "Debug").Defines);

        // the effective values of the configuration itself are not changed by a platform
        Assert.Equal(["APP", "BASE", "DEBUG"], ConfigurationResolver.Resolve(p, "Debug").Defines);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1366")]
    [Trait("Req", "CMP-1367")]
    [Trait("Req", "CMP-1368")]
    // no override: the id goes before a last segment that is the configuration name (any case), else it is appended
    [InlineData("bin/Debug", "Debug", null, @"C:\p\Hello\bin\Debug", @"C:\p\Hello\bin\test32\Debug")]
    [InlineData("BIN/debug", "Debug", null, @"C:\p\Hello\BIN\debug", @"C:\p\Hello\BIN\test32\debug")]
    [InlineData("bin/Debug/", "Debug", null, @"C:\p\Hello\bin\Debug", @"C:\p\Hello\bin\test32\Debug")]
    [InlineData("build", "Debug", null, @"C:\p\Hello\build", @"C:\p\Hello\build\test32")]
    [InlineData("out/x/Release", "Release", null, @"C:\p\Hello\out\x\Release", @"C:\p\Hello\out\x\test32\Release")]
    [InlineData("out/x/Release", "Debug", null, @"C:\p\Hello\out\x\Release", @"C:\p\Hello\out\x\Release\test32")]
    [InlineData("Debug", "Debug", null, @"C:\p\Hello\Debug", @"C:\p\Hello\test32\Debug")]
    [InlineData(".", "Debug", null, @"C:\p\Hello", @"C:\p\Hello\test32")]
    [InlineData("../shared/Debug", "Debug", null, @"C:\p\shared\Debug", @"C:\p\shared\test32\Debug")]
    // an absolute path follows the same rule
    [InlineData("D:/build/Debug", "Debug", null, @"D:\build\Debug", @"D:\build\test32\Debug")]
    [InlineData("D:/build", "Debug", null, @"D:\build", @"D:\build\test32")]
    [InlineData("D:/", "Debug", null, @"D:\", @"D:\test32")]
    // an override replaces the rule: both macros, without $(Config), any case of the names, the escape, an absolute path
    [InlineData("bin/Debug", "Debug", "out/$(Platform)/$(Config)", @"C:\p\Hello\bin\Debug", @"C:\p\Hello\out\test32\Debug")]
    [InlineData("bin/Debug", "Debug", "dist/$(Platform)", @"C:\p\Hello\bin\Debug", @"C:\p\Hello\dist\test32")]
    [InlineData("bin/Debug", "Debug", "fixed", @"C:\p\Hello\bin\Debug", @"C:\p\Hello\fixed")]
    [InlineData("bin/Debug", "Debug", "$(CONFIG)-$(platform)", @"C:\p\Hello\bin\Debug", @"C:\p\Hello\Debug-test32")]
    [InlineData("bin/Debug", "Debug", "out/$$(Config)", @"C:\p\Hello\bin\Debug", @"C:\p\Hello\out\$(Config)")]
    [InlineData("bin/Debug", "Debug", "E:/drop/$(Config)", @"C:\p\Hello\bin\Debug", @"E:\drop\Debug")]
    public void OutputDirectory_OverridePattern_BaselineUnchanged_OtherInsertedOrAppended(
        string outputDir, string configuration, string? overrideDir, string baseline, string other)
    {
        var p = ProjectSerializer.CreateDefault("App", "App.pas", "console") with { FilePath = ProjectPath, Platforms = ["win64", "test32"] };
        p = p with { Configurations = p.Configurations.SetItem(configuration, p.Configurations[configuration] with { OutputDir = outputDir }) };
        if (overrideDir is not null)
        {
            p = p with { PlatformOptions = p.PlatformOptions.Add("test32", new PlatformOptions([], [], [], overrideDir)) };
        }

        // CMP-1367: the baseline platform keeps the configuration's directory, also with a second platform listed
        Assert.Equal(baseline, ProjectOptions.GetOutputDirectory(p, configuration, "win64"));
        Assert.Equal(baseline, ProjectOptions.GetOutputDirectory(p with { Platforms = ["win64"] }, configuration, "win64"));
        Assert.Equal(Path.Combine(baseline, "App.exe"), ProjectOptions.GetOutputPath(p, configuration, "win64"));
        Assert.Equal(Path.Combine(baseline, "App.exe"), ProjectOptions.GetOutputPath(p, configuration));          // effective = first listed

        // CMP-1366: another platform
        Assert.Equal(other, ProjectOptions.GetOutputDirectory(p, configuration, "test32"));
        Assert.Equal(Path.Combine(other, "App.exe"), ProjectOptions.GetOutputPath(p, configuration, "test32"));
        Assert.Equal(Path.Combine(other, "App.dll"), ProjectOptions.GetOutputPath(p with { AppType = "library" }, configuration, "test32"));

        // the two-argument overload answers for the effective platform (CMP-1368)
        Assert.Equal(Path.Combine(other, "App.exe"), ProjectOptions.GetOutputPath(p with { ActivePlatform = "test32" }, configuration));
        Assert.Equal(Path.Combine(other, "App.exe"), ProjectOptions.GetOutputPath(p with { Platforms = ["test32", "win64"] }, configuration));

        // an override for the baseline platform is rule 1 as well
        if (overrideDir is not null)
        {
            var onBaseline = p with { PlatformOptions = p.PlatformOptions.Add("win64", new PlatformOptions([], [], [], overrideDir)) };
            Assert.Equal(other.Replace("test32", "win64", StringComparison.Ordinal), ProjectOptions.GetOutputDirectory(onBaseline, configuration, "win64"));
        }

        Assert.Throws<ArgumentException>(() => ProjectOptions.GetOutputDirectory(p, "Profile", "test32"));
        Assert.Throws<ArgumentException>(() => ProjectOptions.GetOutputPath(p, "Profile", "test32"));
    }
}
