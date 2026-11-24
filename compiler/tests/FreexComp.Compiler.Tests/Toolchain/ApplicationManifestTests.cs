using System.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>app-manifest.md §2 (CMP-1101…1105, task C-295): the generated manifest text.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ApplicationManifestTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static string GoldenPath(string name) =>
        Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Compiler.Tests", "Toolchain", "Golden", name);

    [Fact]
    [Trait("Req", "CMP-1101")]
    [Trait("Req", "CMP-1102")]
    [Trait("Req", "CMP-1103")]
    public void DefaultGuiText_EqualsGolden() =>
        Assert.Equal(File.ReadAllBytes(GoldenPath("app.gui.manifest")), Utf8.GetBytes(ApplicationManifest.Generate(AppType.Gui)));

    [Fact]
    [Trait("Req", "CMP-1101")]
    [Trait("Req", "CMP-1102")]
    [Trait("Req", "CMP-1103")]
    public void DefaultConsoleText_EqualsGolden() =>
        Assert.Equal(File.ReadAllBytes(GoldenPath("app.console.manifest")), Utf8.GetBytes(ApplicationManifest.Generate(AppType.Console)));

    [Theory]
    [Trait("Req", "CMP-1104")]
    [InlineData(AppType.Gui, ManifestExecutionLevel.AsInvoker, "asInvoker")]
    [InlineData(AppType.Gui, ManifestExecutionLevel.HighestAvailable, "highestAvailable")]
    [InlineData(AppType.Gui, ManifestExecutionLevel.RequireAdministrator, "requireAdministrator")]
    [InlineData(AppType.Console, ManifestExecutionLevel.AsInvoker, "asInvoker")]
    [InlineData(AppType.Console, ManifestExecutionLevel.HighestAvailable, "highestAvailable")]
    [InlineData(AppType.Console, ManifestExecutionLevel.RequireAdministrator, "requireAdministrator")]
    public void ExecutionLevel_ChangesExactlyTheLevelAttribute(AppType appType, ManifestExecutionLevel level, string word)
    {
        var standard = ApplicationManifest.Generate(appType).Split('\n');
        var actual = ApplicationManifest.Generate(appType, level).Split('\n');

        Assert.Equal(standard.Length, actual.Length);
        var changed = Enumerable.Range(0, standard.Length).Where(i => standard[i] != actual[i]).ToList();
        Assert.Equal(level == ManifestExecutionLevel.AsInvoker ? 0 : 1, changed.Count);
        var line = Assert.Single(actual, l => l.Contains("requestedExecutionLevel", StringComparison.Ordinal));
        Assert.Equal($"        <requestedExecutionLevel level=\"{word}\" uiAccess=\"false\"/>", line);
        Assert.All(changed, i => Assert.Equal(line, actual[i]));
    }

    [Theory]
    [Trait("Req", "CMP-1105")]
    [InlineData(AppType.Gui, ManifestDpiAwareness.Default, "true/pm", "PerMonitorV2")]
    [InlineData(AppType.Gui, ManifestDpiAwareness.PerMonitorV2, "true/pm", "PerMonitorV2")]
    [InlineData(AppType.Gui, ManifestDpiAwareness.System, "true", "System")]
    [InlineData(AppType.Gui, ManifestDpiAwareness.Unaware, "false", "Unaware")]
    [InlineData(AppType.Console, ManifestDpiAwareness.Default, null, null)]
    [InlineData(AppType.Console, ManifestDpiAwareness.PerMonitorV2, "true/pm", "PerMonitorV2")]
    [InlineData(AppType.Console, ManifestDpiAwareness.System, "true", "System")]
    [InlineData(AppType.Console, ManifestDpiAwareness.Unaware, "false", "Unaware")]
    public void DpiAwareness_TableValues(AppType appType, ManifestDpiAwareness dpi, string? old, string? current)
    {
        static bool IsDpi(string line) => line.Contains("<dpiAware", StringComparison.Ordinal);
        var lines = ApplicationManifest.Generate(appType, ManifestExecutionLevel.AsInvoker, dpi).Split('\n');
        var settings = Array.IndexOf(lines, "    <windowsSettings>");
        var longPath = "      <longPathAware xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">true</longPathAware>";

        // everything but the two DPI lines is the default text of the application type
        var golden = File.ReadAllText(GoldenPath(appType == AppType.Gui ? "app.gui.manifest" : "app.console.manifest")).Split('\n');
        Assert.Equal(golden.Where(l => !IsDpi(l)), lines.Where(l => !IsDpi(l)));

        if (old is null)
        {
            Assert.Equal(longPath, lines[settings + 1]);
            Assert.DoesNotContain(lines, IsDpi);
            return;
        }

        Assert.Equal($"      <dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">{old}</dpiAware>", lines[settings + 1]);
        Assert.Equal($"      <dpiAwareness xmlns=\"http://schemas.microsoft.com/SMI/2016/WindowsSettings\">{current}</dpiAwareness>", lines[settings + 2]);
        Assert.Equal(longPath, lines[settings + 3]);
        Assert.Equal(2, lines.Count(IsDpi));
    }

    /// <summary>CMP-1101: a pure function of the three inputs — the written file does not depend on the project, its paths or the options around it.</summary>
    [Fact]
    [Trait("Req", "CMP-1101")]
    public void TextIsDeterministic_NoProjectData()
    {
        static byte[] Written(string main, string objDir, bool debug)
        {
            var fs = new InMemoryFileSystem();
            var options = new CompilationOptions
            {
                MainFile = main,
                ObjectDirectory = objDir,
                DebugInfo = debug,
                AppType = AppType.Gui,
                Defines = debug ? ["DEBUG"] : [],
                ManifestExecutionLevel = ManifestExecutionLevel.HighestAvailable,
                ManifestDpiAwareness = ManifestDpiAwareness.System,
            };
            return fs.ReadAllBytes(ApplicationManifest.Prepare(options, objDir, fs)!);
        }

        var first = Written(@"C:\p\Alpha.pas", @"C:\p\obj\Debug", debug: true);
        var second = Written(@"D:\other\Beta.pas", @"D:\other\tmp", debug: false);

        Assert.Equal(first, second);
        Assert.Equal(Utf8.GetBytes(ApplicationManifest.Generate(AppType.Gui, ManifestExecutionLevel.HighestAvailable, ManifestDpiAwareness.System)), first);
        var text = Utf8.GetString(first);
        Assert.DoesNotContain("Alpha", text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("</assembly>\n", text, StringComparison.Ordinal);
        Assert.Equal((byte)'<', first[0]);                                   // no BOM
    }
}
