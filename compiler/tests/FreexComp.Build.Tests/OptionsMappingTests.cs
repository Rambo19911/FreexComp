using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>CMP-812: <see cref="ProjectOptions.ToCompilationOptions"/>.</summary>
[Trait("Phase", "1")]
[Trait("Req", "CMP-812")]
public sealed class OptionsMappingTests
{
    private const string ProjectPath = @"C:\p\Hello\Hello.pproj";

    private static Project Load(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, text);
        Assert.Empty(result.Diagnostics);
        return result.Value!;
    }

    [Fact]
    public void Example_Debug_MapsEveryOption()
    {
        var p = Load(ProjectSerializerTests.CanonicalExample);

        var o = ProjectOptions.ToCompilationOptions(p, "Debug");

        Assert.Equal(@"C:\p\Hello\Hello.pas", o.MainFile);
        Assert.Equal([@"C:\p\Hello\lib", @"C:\p\Hello"], o.SearchPaths);
        Assert.Equal(["DEBUG"], o.Defines);
        Assert.Equal(OptimizationLevel.O0, o.Optimization);
        Assert.True(o.DebugInfo);
        Assert.Equal(AppType.Console, o.AppType);
        Assert.Equal(@"C:\p\Hello\obj\Debug", o.ObjectDirectory);
        Assert.Equal(@"C:\p\Hello\bin\Debug\Hello.exe", ProjectOptions.GetOutputPath(p, "Debug"));
    }

    [Fact]
    public void Example_Release_MapsOptimizationAndDefines()
    {
        var p = Load(ProjectSerializerTests.CanonicalExample);

        var o = ProjectOptions.ToCompilationOptions(p, "Release");

        Assert.Equal(OptimizationLevel.O2, o.Optimization);
        Assert.False(o.DebugInfo);
        Assert.Equal(["RELEASE"], o.Defines);
        Assert.Equal(@"C:\p\Hello\obj\Release", o.ObjectDirectory);
        Assert.Equal(@"C:\p\Hello\bin\Release\Hello.exe", ProjectOptions.GetOutputPath(p, "Release"));
    }

    [Fact]
    public void SearchPaths_RelativeAbsoluteAndUnitDirectories_DeduplicatedInProjectOrder()
    {
        var p = Load("""
            {
              "formatVersion": 1, "name": "App", "mainSource": "src/App.pas", "appType": "gui",
              "searchPaths": ["../shared", "C:/abs/lib", "units", "UNITS/"],
              "files": [
                { "path": "src/App.pas" },
                { "path": "units/A.pas" },
                { "path": "forms/Main.pas", "form": "forms/Main.form" },
                { "path": "forms/Main.form" },
                { "path": "docs/readme.md" },
                { "path": "gen/B.inc", "kind": "unit" },
                { "path": "other/C.pas", "kind": "other" },
                { "path": "src/D.pas" }
              ],
              "defines": ["APP", "TRACE"],
              "configurations": { "Fast": { "optimization": 1, "defines": ["FAST"], "outputDir": "out" } },
              "activeConfiguration": "Fast"
            }
            """);

        var o = ProjectOptions.ToCompilationOptions(p, "Fast");

        Assert.Equal(@"C:\p\Hello\src\App.pas", o.MainFile);
        Assert.Equal(
            [@"C:\p\shared", @"C:\abs\lib", @"C:\p\Hello\units", @"C:\p\Hello\src", @"C:\p\Hello\forms", @"C:\p\Hello\gen"],
            o.SearchPaths);
        Assert.Equal(["APP", "TRACE", "FAST"], o.Defines);
        Assert.Equal(OptimizationLevel.O1, o.Optimization);
        Assert.False(o.DebugInfo);
        Assert.Equal(AppType.Gui, o.AppType);
        Assert.Equal(@"C:\p\Hello\obj\Fast", o.ObjectDirectory);
        Assert.Equal(@"C:\p\Hello\out\App.exe", ProjectOptions.GetOutputPath(p, "Fast"));
    }

    /// <summary>C-164: project <c>unitScopeNames</c> → <see cref="CompilationOptions.UnitScopeNames"/>, in order (LANG-535).</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void UnitScopeNames_AreMappedInOrder()
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "console") with { FilePath = ProjectPath, UnitScopeNames = ["Winapi", "System", "Lib.Sub"] };

        Assert.Equal(["Winapi", "System", "Lib.Sub"], ProjectOptions.ToCompilationOptions(p, "Debug").UnitScopeNames);
    }

    /// <summary>
    /// C-240: <c>rtti</c> (per configuration, else project, else <c>broad</c>) and <c>manifest</c> (<c>default</c> / <c>none</c> as is,
    /// a path resolved against the project directory, absent → "" = the compiler's default per app type).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-648")]
    [Trait("Req", "CMP-628")]
    public void RttiAndManifest_AreMapped()
    {
        var p = Load("""
            {
              "formatVersion": 1, "name": "App", "mainSource": "App.pas", "appType": "gui",
              "rtti": "published", "manifest": "res/app.manifest",
              "configurations": {
                "Debug": { "optimization": 0, "outputDir": "bin/Debug" },
                "Broad": { "optimization": 0, "outputDir": "bin/Broad", "rtti": "broad" }
              }
            }
            """);

        var debug = ProjectOptions.ToCompilationOptions(p, "Debug");
        var broad = ProjectOptions.ToCompilationOptions(p, "Broad");

        Assert.Equal("published", debug.Rtti);
        Assert.Equal("broad", broad.Rtti);
        Assert.Equal(@"C:\p\Hello\res\app.manifest", debug.Manifest);
        Assert.Equal("none", ProjectOptions.ToCompilationOptions(p with { Manifest = "none" }, "Debug").Manifest);
        Assert.Equal("default", ProjectOptions.ToCompilationOptions(p with { Manifest = "default" }, "Debug").Manifest);
        Assert.Equal(@"C:\m\x.manifest", ProjectOptions.ToCompilationOptions(p with { Manifest = "C:/m/x.manifest" }, "Debug").Manifest);

        var plain = ProjectSerializer.CreateDefault("A", "A.pas", "gui") with { FilePath = ProjectPath };
        var plainOptions = ProjectOptions.ToCompilationOptions(plain, "Debug");
        Assert.Equal("broad", plainOptions.Rtti);
        Assert.Equal("", plainOptions.Manifest);
    }

    [Fact]
    public void AbsoluteOutputDir_IsUsedAsIs()
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "console") with { FilePath = ProjectPath };
        p = p with { Configurations = p.Configurations.SetItem("Debug", p.Configurations["Debug"] with { OutputDir = "D:/build/x" }) };

        Assert.Equal(@"D:\build\x\A.exe", ProjectOptions.GetOutputPath(p, "Debug"));
    }

    [Fact]
    public void UnknownConfiguration_Throws()
    {
        var p = Load(ProjectSerializerTests.CanonicalExample);

        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p, "Profile"));
        Assert.Throws<ArgumentException>(() => ProjectOptions.GetOutputPath(p, "Profile"));
    }

    [Fact]
    public void ProjectWithoutFilePath_Throws()
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "console");

        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p, "Debug"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void InvalidOptimizationInModel_Throws(int level)
    {
        var p = ProjectSerializer.CreateDefault("A", "A.pas", "console") with { FilePath = ProjectPath };
        p = p with { Configurations = p.Configurations.SetItem("Debug", p.Configurations["Debug"] with { Optimization = level }) };

        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p, "Debug"));
    }
}
