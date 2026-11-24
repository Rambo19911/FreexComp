using System.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>toolchain.md §5a (CMP-628, CMP-629 variant 1, C-167): effective manifest, <c>app.manifest</c>, link command line.</summary>
public sealed partial class ToolchainTests
{
    private static readonly string[] ManifestLines = ["/manifest:embed", @"/manifestinput:C:\p\obj\Debug\app.manifest"];

    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "CMP-628")]
    public void Manifest_DefaultText_IsTheCmp628Block()
    {
        // CMP-1102 (C-295): the default GUI text is the CMP-628 block plus the longPathAware line
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.RequireDocs(), "spec", "compiler", "toolchain.md"), Encoding.UTF8);
        var start = Array.FindIndex(lines, l => l.Contains("**CMP-628**", StringComparison.Ordinal));
        var open = Array.FindIndex(lines, start, l => l.Trim() == "```xml");
        var close = Array.FindIndex(lines, open + 1, l => l.Trim() == "```");

        var block = string.Concat(lines[(open + 1)..close].Select(l => l + "\n"));

        var generated = ApplicationManifest.Generate(AppType.Gui).Split('\n');
        var longPath = Assert.Single(generated, l => l.Contains("longPathAware", StringComparison.Ordinal));
        Assert.Equal("      <longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>", longPath);
        Assert.Equal(block, string.Join('\n', generated.Where(l => l != longPath)));
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    [Trait("Req", "CMP-629")]
    public void Manifest_GuiDefault_WritesAppManifestAndEmbedsIt()
    {
        var fs = new InMemoryFileSystem();

        var path = ApplicationManifest.Prepare(ManifestOptions("", AppType.Gui), Obj, fs);

        Assert.Equal(Obj + @"\app.manifest", path);
        Assert.Equal(new UTF8Encoding(false).GetBytes(ApplicationManifest.Generate(AppType.Gui)), fs.ReadAllBytes(path!));
        Assert.DoesNotContain('\r', ApplicationManifest.Generate(AppType.Gui));
        var args = LinkInvoker.BuildArguments(HelloRequest() with { AppType = AppType.Gui, ManifestPath = path });
        var at = args.IndexOf("/nxcompat");
        Assert.Equal(ManifestLines, args.Skip(at + 1).Take(2));
        Assert.Equal(Obj + @"\Hello.obj", args[at + 3]);
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    public void Manifest_ExplicitDefault_IsEmbeddedForAConsoleProgramToo()
    {
        var fs = new InMemoryFileSystem();

        Assert.Equal(Obj + @"\app.manifest", ApplicationManifest.Prepare(ManifestOptions("default", AppType.Console), Obj, fs));
        Assert.Equal(ApplicationManifest.Generate(AppType.Console), fs.ReadAllText(Obj + @"\app.manifest"));
    }

    /// <summary>CMP-1100 (C-295; replaces the console half of CMP-628): no manifest option → a generated manifest for a console program too.</summary>
    [Fact]
    [Trait("Req", "CMP-1100")]
    public void Manifest_ConsoleDefault_WritesTheGeneratedConsoleText()
    {
        var fs = new InMemoryFileSystem();

        Assert.Equal(Obj + @"\app.manifest", ApplicationManifest.Prepare(ManifestOptions("", AppType.Console), Obj, fs));
        Assert.Equal(ApplicationManifest.Generate(AppType.Console), fs.ReadAllText(Obj + @"\app.manifest"));
    }

    /// <summary>
    /// CMP-1104 (C-295): the linker's manifest merger adds its own <c>requestedExecutionLevel</c> (<c>asInvoker</c>) and rejects an
    /// input naming another level, so a generated manifest with another level tells the linker that level; a user file never does.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1104")]
    public void Manifest_ExecutionLevel_IsPassedToTheLinkerForTheGeneratedManifestOnly()
    {
        var fs = new InMemoryFileSystem();
        var options = ManifestOptions("", AppType.Console) with { ManifestExecutionLevel = ManifestExecutionLevel.RequireAdministrator };

        var path = ApplicationManifest.Prepare(options, Obj, fs, out var generated);

        Assert.True(generated);
        Assert.Contains("level=\"requireAdministrator\"", fs.ReadAllText(path!), StringComparison.Ordinal);
        var args = LinkInvoker.BuildArguments(HelloRequest() with { ManifestPath = path, ManifestUacLevel = "requireAdministrator" });
        var at = args.IndexOf("/manifest:embed");
        Assert.Equal(["/manifest:embed", @"/manifestinput:C:\p\obj\Debug\app.manifest", "/manifestuac:level='requireAdministrator'"], args.Skip(at).Take(3));
        Assert.DoesNotContain(LinkInvoker.BuildArguments(HelloRequest() with { ManifestPath = path }), a => a.StartsWith("/manifestuac", StringComparison.Ordinal));

        ApplicationManifest.Prepare(options with { Manifest = @"C:\m\x.manifest" }, Obj, fs, out generated);
        Assert.False(generated);
        ApplicationManifest.Prepare(options with { Manifest = "none" }, Obj, fs, out generated);
        Assert.False(generated);
        Assert.Equal("asInvoker", ApplicationManifest.LevelWord(ManifestExecutionLevel.AsInvoker));
        Assert.Equal("highestAvailable", ApplicationManifest.LevelWord(ManifestExecutionLevel.HighestAvailable));
    }

    private static CompilationOptions ManifestOptions(string manifest, AppType appType) =>
        new() { MainFile = @"C:\p\Hello.pas", Manifest = manifest, AppType = appType };

    [Theory]
    [InlineData("", AppType.Library)]
    [InlineData("default", AppType.Library)]
    [InlineData("none", AppType.Console)]
    [InlineData("none", AppType.Gui)]
    [Trait("Req", "CMP-628")]
    public void Manifest_None_WritesNothingAndAddsNoLinkerOption(string manifest, AppType appType)
    {
        var fs = new InMemoryFileSystem();

        var path = ApplicationManifest.Prepare(ManifestOptions(manifest, appType), Obj, fs);

        Assert.Null(path);
        Assert.False(fs.FileExists(Obj + @"\app.manifest"));
        var args = LinkInvoker.BuildArguments(HelloRequest() with { AppType = appType, ManifestPath = path });
        Assert.DoesNotContain(args, a => a.StartsWith("/manifest", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    [Trait("Req", "CMP-629")]
    public void Manifest_UserFile_GoesStraightToLldLink()
    {
        var fs = new InMemoryFileSystem();

        var path = ApplicationManifest.Prepare(ManifestOptions(@"C:\m\x.manifest", AppType.Gui), Obj, fs);

        Assert.Equal(@"C:\m\x.manifest", path);
        Assert.False(fs.FileExists(Obj + @"\app.manifest"));
        Assert.Contains(@"/manifestinput:C:\m\x.manifest", LinkInvoker.BuildArguments(HelloRequest() with { ManifestPath = path }));
    }
}
