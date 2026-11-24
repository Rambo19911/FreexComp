using System.Text;
using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>configurations.md CMP-1161 (task C-298): the options of a configuration come from its effective values.</summary>
public sealed partial class OptionsMappingTests
{
    /// <summary>Project files without <c>inherits</c>, per-configuration <c>searchPaths</c> and <c>"!"</c>: the fixtures of the snapshot.</summary>
    private static readonly (string Name, string Text)[] SnapshotFixtures =
    [
        ("example", ProjectSerializerTests.CanonicalExample),
        ("gui", """
            {
              "formatVersion": 1, "name": "App", "mainSource": "src/App.pas", "appType": "gui",
              "searchPaths": ["../shared", "C:/abs/lib", "units", "UNITS/"],
              "unitScopeNames": ["Winapi", "System"],
              "rtti": "published", "manifest": "res/app.manifest",
              "defines": ["APP", "debug"],
              "files": [
                { "path": "src/App.pas" },
                { "path": "units/A.pas" },
                { "path": "forms/Main.pas", "form": "forms/Main.form" },
                { "path": "doc/readme.md" }
              ],
              "configurations": {
                "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG", "APP"], "outputDir": "bin/Debug" },
                "Fast": { "optimization": 1, "outputDir": "C:/out/fast", "rtti": "broad" },
                "Bare": {}
              }
            }
            """),
        ("library", """
            {
              "formatVersion": 1, "name": "Lib", "mainSource": "Lib.pas", "appType": "library",
              "libraryPaths": ["imports", "D:/sdk/lib"],
              "manifest": "none",
              "configurations": {
                "Release": { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "outputDir": "bin/Release" }
              },
              "activeConfiguration": "Release"
            }
            """),
        ("minimal", """{ "formatVersion": 1, "name": "A", "mainSource": "A.pas" }"""),
    ];

    private static string SnapshotPath =>
        Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Build.Tests", "Golden", "options.snapshot.txt");

    private static string Snapshot()
    {
        var text = new StringBuilder();
        foreach (var (name, json) in SnapshotFixtures)
        {
            var p = Load(json);
            foreach (var configuration in p.ConfigurationNames)
            {
                var o = ProjectOptions.ToCompilationOptions(p, configuration);
                text.Append("[").Append(name).Append(" / ").Append(configuration).Append("]\n");
                text.Append("MainFile = ").Append(o.MainFile).Append('\n');
                text.Append("SearchPaths = ").AppendJoin(" | ", o.SearchPaths).Append('\n');
                text.Append("LibraryPaths = ").AppendJoin(" | ", o.LibraryPaths).Append('\n');
                text.Append("Defines = ").AppendJoin(" | ", o.Defines).Append('\n');
                text.Append("ObjectDirectory = ").Append(o.ObjectDirectory).Append('\n');
                text.Append("RtlDirectory = ").Append(o.RtlDirectory).Append('\n');
                text.Append("ToolchainDirectory = ").Append(o.ToolchainDirectory).Append('\n');
                text.Append("Optimization = ").Append(o.Optimization).Append('\n');
                text.Append("DebugInfo = ").Append(o.DebugInfo).Append('\n');
                text.Append("DebugFormat = ").Append(o.DebugFormat).Append('\n');
                text.Append("AppType = ").Append(o.AppType).Append('\n');
                text.Append("Checks = ").Append(o.RangeChecks).Append(' ').Append(o.OverflowChecks).Append(' ').Append(o.Assertions).Append('\n');
                text.Append("MaxParallelism = ").Append(o.MaxParallelism).Append('\n');
                text.Append("UsePcuCache = ").Append(o.UsePcuCache).Append('\n');
                text.Append("Rtti = ").Append(o.Rtti).Append('\n');
                text.Append("Manifest = ").Append(o.Manifest).Append('\n');
                text.Append("ManifestSettings = ").Append(o.ManifestExecutionLevel).Append(' ').Append(o.ManifestDpiAwareness).Append('\n');
                text.Append("Target = ").Append(o.Target.Id).Append('\n');
                text.Append("UnitScopeNames = ").AppendJoin(" | ", o.UnitScopeNames).Append('\n');
                text.Append("Output = ").Append(ProjectOptions.GetOutputPath(p, configuration)).Append('\n');
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// CMP-1161: a project without inheritance yields the same <see cref="CompilationOptions"/> as before the card. The snapshot
    /// file was recorded with the code before the change (<c>UPDATE_OPTIONS_SNAPSHOT=1</c> rewrites it; review the diff).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1161")]
    public void ProjectWithoutInheritance_OptionsUnchanged()
    {
        var actual = Snapshot();
        if (Environment.GetEnvironmentVariable("UPDATE_OPTIONS_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.Equal(File.ReadAllText(SnapshotPath).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>CMP-1161: every per-configuration value of a derived configuration is the effective one.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1161")]
    public void DerivedConfiguration_MapsEffectiveValues()
    {
        var p = Load("""
            {
              "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "units/U.pas" }, { "path": "rel/R.pas" } ],
              "searchPaths": ["lib"],
              "defines": ["APP"],
              "rtti": "published",
              "configurations": {
                "Debug":        { "optimization": 0, "debugInfo": true,  "defines": ["DEBUG"],   "outputDir": "bin/Debug" },
                "Release":      { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "searchPaths": ["rel", "C:/abs/rel"], "outputDir": "out/release" },
                "Release-Demo": { "inherits": "Release", "defines": ["DEMO"], "searchPaths": ["demo", "LIB/"], "rtti": "broad" },
                "Profiling":    { "inherits": "Release-Demo", "debugInfo": true, "defines": ["!", "PROFILE"] },
                "Isolated":     { "inherits": "Release", "optimization": 1, "searchPaths": ["!", "only"], "outputDir": "D:/out/isolated" }
              }
            }
            """);

        var demo = ProjectOptions.ToCompilationOptions(p, "Release-Demo");
        Assert.Equal(OptimizationLevel.O2, demo.Optimization);
        Assert.False(demo.DebugInfo);
        Assert.Equal(["APP", "RELEASE", "DEMO"], demo.Defines);
        Assert.Equal("broad", demo.Rtti);
        // effective search paths made absolute (project level, then the chain), then the unit directories; duplicates dropped
        Assert.Equal(
            [@"C:\p\Hello\lib", @"C:\p\Hello\rel", @"C:\abs\rel", @"C:\p\Hello\demo", @"C:\p\Hello", @"C:\p\Hello\units"],
            demo.SearchPaths);
        // the intermediate and the output directory carry the configuration's own name: neither is inherited
        Assert.Equal(@"C:\p\Hello\obj\Release-Demo", demo.ObjectDirectory);
        Assert.Equal(@"C:\p\Hello\bin\Release-Demo\Hello.exe", ProjectOptions.GetOutputPath(p, "Release-Demo"));
        Assert.Equal(@"C:\p\Hello\out\release\Hello.exe", ProjectOptions.GetOutputPath(p, "Release"));

        var profiling = ProjectOptions.ToCompilationOptions(p, "Profiling");
        Assert.Equal(OptimizationLevel.O2, profiling.Optimization);
        Assert.True(profiling.DebugInfo);
        Assert.Equal(["PROFILE"], profiling.Defines);
        Assert.Equal("broad", profiling.Rtti);
        Assert.Equal(demo.SearchPaths, profiling.SearchPaths);
        Assert.Equal(@"C:\p\Hello\obj\Profiling", profiling.ObjectDirectory);

        var isolated = ProjectOptions.ToCompilationOptions(p, "Isolated");
        Assert.Equal(OptimizationLevel.O1, isolated.Optimization);
        Assert.Equal(["APP", "RELEASE"], isolated.Defines);
        Assert.Equal("published", isolated.Rtti);
        Assert.Equal([@"C:\p\Hello\only", @"C:\p\Hello", @"C:\p\Hello\units", @"C:\p\Hello\rel"], isolated.SearchPaths);
        Assert.Equal(@"D:\out\isolated\Hello.exe", ProjectOptions.GetOutputPath(p, "Isolated"));

        // the root configurations map as before
        var release = ProjectOptions.ToCompilationOptions(p, "Release");
        Assert.Equal(["APP", "RELEASE"], release.Defines);
        Assert.Equal([@"C:\p\Hello\lib", @"C:\p\Hello\rel", @"C:\abs\rel", @"C:\p\Hello", @"C:\p\Hello\units"], release.SearchPaths);
        Assert.Equal(["APP", "DEBUG"], ProjectOptions.ToCompilationOptions(p, "Debug").Defines);
        Assert.Equal([@"C:\p\Hello\lib", @"C:\p\Hello", @"C:\p\Hello\units", @"C:\p\Hello\rel"], ProjectOptions.ToCompilationOptions(p, "Debug").SearchPaths);

        // an exact name; a model with a broken chain throws like any value the loader would have rejected
        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p, "release-demo"));
        var broken = p with { Configurations = p.Configurations.Remove("Release") };
        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(broken, "Profiling"));
        Assert.Throws<ArgumentException>(() => ProjectOptions.GetOutputPath(broken, "Profiling"));
    }
}
