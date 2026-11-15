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
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.RequireDocs(), "spec", "compiler", "toolchain.md"), Encoding.UTF8);
        var start = Array.FindIndex(lines, l => l.Contains("**CMP-628**", StringComparison.Ordinal));
        var open = Array.FindIndex(lines, start, l => l.Trim() == "```xml");
        var close = Array.FindIndex(lines, open + 1, l => l.Trim() == "```");

        var block = string.Concat(lines[(open + 1)..close].Select(l => l + "\n"));

        Assert.Equal(ApplicationManifest.DefaultText, block);
    }

    [Fact]
    [Trait("Req", "CMP-628")]
    [Trait("Req", "CMP-629")]
    public void Manifest_GuiDefault_WritesAppManifestAndEmbedsIt()
    {
        var fs = new InMemoryFileSystem();

        var path = ApplicationManifest.Prepare("", AppType.Gui, Obj, fs);

        Assert.Equal(Obj + @"\app.manifest", path);
        Assert.Equal(new UTF8Encoding(false).GetBytes(ApplicationManifest.DefaultText), fs.ReadAllBytes(path!));
        Assert.DoesNotContain('\r', ApplicationManifest.DefaultText);
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

        Assert.Equal(Obj + @"\app.manifest", ApplicationManifest.Prepare("default", AppType.Console, Obj, fs));
        Assert.Equal(ApplicationManifest.DefaultText, fs.ReadAllText(Obj + @"\app.manifest"));
    }

    [Theory]
    [InlineData("", AppType.Console)]
    [InlineData("none", AppType.Console)]
    [InlineData("none", AppType.Gui)]
    [Trait("Req", "CMP-628")]
    public void Manifest_None_WritesNothingAndAddsNoLinkerOption(string manifest, AppType appType)
    {
        var fs = new InMemoryFileSystem();

        var path = ApplicationManifest.Prepare(manifest, appType, Obj, fs);

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

        var path = ApplicationManifest.Prepare(@"C:\m\x.manifest", AppType.Gui, Obj, fs);

        Assert.Equal(@"C:\m\x.manifest", path);
        Assert.False(fs.FileExists(Obj + @"\app.manifest"));
        Assert.Contains(@"/manifestinput:C:\m\x.manifest", LinkInvoker.BuildArguments(HelloRequest() with { ManifestPath = path }));
    }
}
