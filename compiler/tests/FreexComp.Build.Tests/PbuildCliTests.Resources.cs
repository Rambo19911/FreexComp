using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;
using FreexComp.PBuild;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>resource-compiler.md §5, card acceptance 1: resources of a project end up in the image (pinned tools).</summary>
public sealed partial class PbuildCliTests
{
    private const string VersionScript = """
        #include <winver.h>
        1 ICON "app.ico"
        VS_VERSION_INFO VERSIONINFO
        FILEVERSION 1,2,3,4
        PRODUCTVERSION 1,2,0,0
        FILEOS VOS_NT_WINDOWS32
        FILETYPE VFT_APP
        BEGIN
          BLOCK "StringFileInfo"
          BEGIN
            BLOCK "040904B0"
            BEGIN
              VALUE "ProductName", "Resources"
              VALUE "FileVersion", "1.2.3.4"
            END
          END
          BLOCK "VarFileInfo"
          BEGIN
            VALUE "Translation", 0x409, 1200
          END
        END

        """;

    /// <summary>Builds the project of <paramref name="dir"/> with the pinned tools.</summary>
    private static (int Code, string[] Lines) PbuildReal(string dir, params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var code = PbuildCommand.Run(
            args,
            new PbuildEnvironment(dir, stdout, new StringWriter(), PhysicalFileSystem.Instance)
            {
                ToolchainDirectory = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin"),
                RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
            },
            TestContext.Current.CancellationToken);
        return (code, stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The resource section of an image as the object reader of the toolchain prints it.</summary>
    private static async Task<string> ResourcesOfAsync(string image)
    {
        var reader = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin", "llvm-readobj.exe");
        Assert.True(File.Exists(reader), reader + " is missing");
        var lines = new List<string>();
        void Line(string line)
        {
            lock (lines)
            {
                lines.Add(line);
            }
        }

        var exit = await ProcessRunner.Instance.RunAsync(reader, ["--coff-resources", image], Path.GetDirectoryName(image)!, Line, Line, TestContext.Current.CancellationToken);
        Assert.True(exit == 0, string.Join("\n", lines));
        return string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.StartsWith("Type: ", StringComparison.Ordinal)));   // the type lines only
    }

    private static string ResourceProjectDirectory()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1319")]
    public async Task Resources_IconAndVersionAreInTheExe()
    {
        var dir = ResourceProjectDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Res.pproj"), """{ "formatVersion": 1, "name": "Res", "mainSource": "Res.pas", "files": [ { "path": "app.rc" } ] }""");
            File.WriteAllText(Path.Combine(dir, "Res.pas"), "program Res;\nbegin\n  Writeln('resources')\nend.\n");
            File.WriteAllText(Path.Combine(dir, "app.rc"), VersionScript.ReplaceLineEndings("\n"));
            File.WriteAllBytes(Path.Combine(dir, "app.ico"), PrcCliTests.IconFile(1));

            var (code, lines) = PbuildReal(dir, "Res.pproj");

            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Contains("Compiling resources app.rc", lines);
            var exe = Path.Combine(dir, "bin", "Debug", "Res.exe");
            var resources = await ResourcesOfAsync(exe);
            Assert.True(resources.Contains("Type: ICON (ID 3)", StringComparison.Ordinal), resources);
            Assert.True(resources.Contains("Type: GROUP_ICON (ID 14)", StringComparison.Ordinal), resources);
            Assert.True(resources.Contains("Type: VERSIONINFO (ID 16)", StringComparison.Ordinal), resources);
            Assert.True(resources.Contains("Type: MANIFEST (ID 24)", StringComparison.Ordinal), resources);       // the generated manifest is still embedded
            var output = new List<string>();
            Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
            Assert.Equal(["resources"], output);

            // a second build: nothing to do
            (code, lines) = PbuildReal(dir, "Res.pproj");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Contains(lines, l => l.Contains("up to date", StringComparison.Ordinal));
            Assert.DoesNotContain("Compiling resources app.rc", lines);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1322")]
    public async Task Resources_LibraryProjectGetsThem()
    {
        var dir = ResourceProjectDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "Lib.pproj"),
                """{ "formatVersion": 1, "name": "Lib", "appType": "library", "mainSource": "Lib.pas", "files": [ { "path": "app.rc" } ] }""");
            File.WriteAllText(Path.Combine(dir, "Lib.pas"), "library Lib;\nfunction Add(A, B: Integer): Integer; stdcall;\nbegin\n  Result := A + B;\nend;\nexports Add;\nbegin\nend.\n");
            File.WriteAllText(Path.Combine(dir, "app.rc"), VersionScript.ReplaceLineEndings("\n"));
            File.WriteAllBytes(Path.Combine(dir, "app.ico"), PrcCliTests.IconFile(1));

            var (code, lines) = PbuildReal(dir, "Lib.pproj");

            Assert.True(code == 0, string.Join("\n", lines));
            var resources = await ResourcesOfAsync(Path.Combine(dir, "bin", "Debug", "Lib.dll"));
            Assert.True(resources.Contains("Type: GROUP_ICON (ID 14)", StringComparison.Ordinal), resources);
            Assert.True(resources.Contains("Type: VERSIONINFO (ID 16)", StringComparison.Ordinal), resources);
            Assert.DoesNotContain("Type: MANIFEST", resources, StringComparison.Ordinal);         // a library embeds no manifest by default
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
