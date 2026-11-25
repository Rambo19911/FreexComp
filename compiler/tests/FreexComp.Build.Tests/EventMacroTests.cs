using System.Text;

namespace FreexComp.Build.Tests;

/// <summary>C-328: the macros of build event commands (build-events.md CMP-1256…1258).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class EventMacroTests
{
    private const string AllMacros = "$(ProjectDir)|$(ProjectName)|$(Config)|$(Platform)|$(OutputDir)|$(OutputPath)";

    private static string GoldenPath => Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Build.Tests", "Golden", "EventMacros.txt");

    private static Project Parse(string path, string text)
    {
        var load = ProjectSerializer.ParseProject(path, text);
        Assert.Empty(load.Diagnostics);
        return load.Value!;
    }

    /// <summary>Card acceptance 3: absolute normalised paths without a trailing separator, for an exe and a library project.</summary>
    [Fact]
    [Trait("Req", "CMP-1257")]
    [Trait("Req", "CMP-1258")]
    public void Expand_AllMacros_Golden()
    {
        var app = Parse(@"C:\Work\My App\App.pproj", """
            {
              "formatVersion": 1, "name": "App", "mainSource": "src/App.pas",
              "configurations": {
                "Debug": { "optimization": 0, "debugInfo": true },
                "Release": { "optimization": 2, "outputDir": "out/final/" },
                "Release-Demo": { "inherits": "Release" },
                "Shared": { "inherits": "Debug", "outputDir": "../Shared Output/./bin" },
                "Far": { "inherits": "Debug", "outputDir": "D:/" }
              }
            }
            """);
        var library = Parse(@"C:\Work\My App\lib\Core.pproj", """{ "formatVersion": 1, "name": "Core", "mainSource": "Core.pas", "appType": "library" }""");
        var root = Parse(@"C:\Root.pproj", """{ "formatVersion": 1, "name": "Root", "mainSource": "Root.pas" }""");

        var actual = new StringBuilder();
        foreach (var (project, configurations) in new[] { (app, app.ConfigurationNames), (library, ["Debug", "Release"]), (root, ["Debug"]) })
        {
            foreach (var configuration in configurations)
            {
                var expansion = EventMacros.Expand(AllMacros, EventMacros.Values(project, configuration, "win64"));
                Assert.Empty(expansion.UnknownNames);
                actual.Append(project.Name).Append(' ').Append(configuration).Append(": ").Append(expansion.Text).Append('\n');
            }
        }

        Assert.Equal(File.ReadAllText(GoldenPath).ReplaceLineEndings("\n"), actual.ToString());
        Assert.Equal(["ProjectDir", "ProjectName", "Config", "Platform", "OutputDir", "OutputPath"], EventMacros.Names);
        Assert.Equal(EventMacros.Names.Order(StringComparer.Ordinal), EventMacros.Values(app, "Debug", "win64").Keys.Order(StringComparer.Ordinal));
        Assert.Equal("win32", EventMacros.Values(app, "Debug", "win32")["platform"]);       // the platform it is given; keys ignore case
    }

    [Theory]
    [Trait("Req", "CMP-1256")]
    [Trait("Req", "CMP-1258")]
    // names are compared ignoring case
    [InlineData("$(config) $(CONFIG) $(Config) $(cOnFiG)", "Debug Debug Debug Debug", "")]
    // $$( is a literal $( and never the start of a reference
    [InlineData("$$(Config) $$(Nope) a$$(b", "$(Config) $(Nope) a$(b", "")]
    [InlineData("$$$(Config)", "$$(Config)", "")]
    [InlineData("$(Config)$$($(Config)", "Debug$(Debug", "")]
    // any other use of $ is left unchanged
    [InlineData("$(Get-Date) $($x) $( Config ) $() $(1st) $(Config", "$(Get-Date) $($x) $( Config ) $() $(1st) $(Config", "")]
    [InlineData("$ $$ $Config ${Config} $[Config] cost: 5$", "$ $$ $Config ${Config} $[Config] cost: 5$", "")]
    // environment variables are not touched
    [InlineData("%PATH% %Config% \"%ProgramFiles%\\x\" $(Config)", "%PATH% %Config% \"%ProgramFiles%\\x\" Debug", "")]
    // nothing is quoted, escaped or trimmed
    [InlineData("  copy \"$(Dir)\" $(Dir)  ", "  copy \"C:\\My App & Co\" C:\\My App & Co  ", "")]
    // an inserted value is not scanned again
    [InlineData("$(Nested) $(Escaped)", "$(Config) $$(Config)", "")]
    // unknown names: as written, in order of first appearance, each once; the reference stays
    [InlineData("$(Nope) $(Config) $(_other1) $(NOPE) $(nope)", "$(Nope) Debug $(_other1) $(NOPE) $(nope)", "Nope,_other1")]
    [InlineData("", "", "")]
    public void ReferenceSyntaxAndEscape(string command, string expected, string unknown)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)        // an ordinary dictionary: the lookup ignores case by itself
        {
            ["Config"] = "Debug",
            ["Dir"] = @"C:\My App & Co",
            ["Nested"] = "$(Config)",
            ["Escaped"] = "$$(Config)",
        };

        var expansion = EventMacros.Expand(command, values);

        Assert.Equal(expected, expansion.Text);
        Assert.Equal(unknown.Split(',', StringSplitOptions.RemoveEmptyEntries), expansion.UnknownNames);
    }
}
