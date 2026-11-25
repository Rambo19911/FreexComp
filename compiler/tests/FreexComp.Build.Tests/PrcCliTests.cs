using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;
using FreexComp.Prc;

namespace FreexComp.Build.Tests;

/// <summary>resource-compiler.md §4, §9 (CMP-1308, 1310…1312): the tool <c>prc</c>, run in-process.</summary>
[Trait("Phase", "8")]
public sealed class PrcCliTests
{
    private const string Cwd = @"C:\work";

    private static readonly string Bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");

    private static (int ExitCode, string[] Out, string Err) Run(IFileSystem fs, string cwd, IProcessRunner? runner, params string[] args)
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var env = new PrcEnvironment(cwd, stdout, stderr, fs);
        var code = PrcCommand.Run(args, runner is null ? env : env with { ProcessRunner = runner }, TestContext.Current.CancellationToken);
        var lines = stdout.ToString().Split('\n');
        return (code, lines[^1].Length == 0 ? lines[..^1] : lines, stderr.ToString());
    }

    /// <summary>A real directory with the pinned tools at hand; removed by the caller.</summary>
    private static string RealDirectory()
    {
        Assert.True(File.Exists(Path.Combine(Bin, "clang.exe")), $"Pinned toolchain missing in {Bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text.ReplaceLineEndings("\n")));
    }

    // ------------------------------------------------------------------ own 16 x 16 images, written from byte arrays

    /// <summary>The image part shared by the icon and the cursor: a 32-bit bitmap header, 16 x 16 pixels, the 1-bit mask.</summary>
    private static byte[] IconImage()
    {
        var image = new byte[40 + (16 * 16 * 4) + (16 * 4)];
        image[0] = 40;                                                      // header size
        image[4] = 16;                                                      // width
        image[8] = 32;                                                      // height: colour rows + mask rows
        image[12] = 1;                                                      // planes
        image[14] = 32;                                                     // bits per pixel
        for (var i = 0; i < 16 * 16; i++)
        {
            image[40 + (i * 4) + 1] = (byte)(i % 16 * 16);                  // a green ramp
            image[40 + (i * 4) + 3] = 0xFF;
        }

        return image;
    }

    /// <param name="kind">1 = icon, 2 = cursor (planes and bit count are then the hot spot).</param>
    internal static byte[] IconFile(byte kind)
    {
        var image = IconImage();
        byte[] header =
        [
            0, 0, kind, 0, 1, 0,                                            // reserved, kind, one image
            16, 16, 0, 0, (byte)(kind == 1 ? 1 : 3), 0, (byte)(kind == 1 ? 32 : 5), 0,
            (byte)image.Length, (byte)(image.Length >> 8), 0, 0, 22, 0, 0, 0,
        ];
        return [.. header, .. image];
    }

    private static byte[] BitmapFile()
    {
        var pixels = new byte[16 * 16 * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)(i / 3);                                      // a blue ramp
        }

        var size = 14 + 40 + pixels.Length;
        var file = new byte[54];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        file[2] = (byte)size;
        file[3] = (byte)(size >> 8);
        file[10] = 54;                                                      // offset of the pixels
        file[14] = 40;
        file[18] = 16;
        file[22] = 16;
        file[26] = 1;
        file[28] = 24;
        return [.. file, .. pixels];
    }

    private const string Statements = """
        #include <windows.h>
        #include "ids.h"
        // a line comment
        /* a block
           comment */
        #define IDI_APP 1
        #ifdef WITH_EXTRA
        EXTRA RCDATA BEGIN 1, 2, 3 END
        #endif
        #if 0
        this text is skipped
        #endif

        IDI_APP ICON "app.ico"
        IDB_LOGO BITMAP "logo.bmp"
        IDC_POINTER CURSOR "pointer.cur"
        10 RCDATA "payload.bin"
        11 RCDATA
        BEGIN
          "inline", 1, 2
        END

        STRINGTABLE
        BEGIN
          1 "one"
          2 "two"
          17 "seventeen"
        END

        VS_VERSION_INFO VERSIONINFO
        FILEVERSION 1,2,3,4
        PRODUCTVERSION 1,2,0,0
        FILEFLAGSMASK VS_FFI_FILEFLAGSMASK
        FILEFLAGS VS_FF_PRERELEASE
        FILEOS VOS_NT_WINDOWS32
        FILETYPE VFT_APP
        FILESUBTYPE VFT2_UNKNOWN
        BEGIN
          BLOCK "StringFileInfo"
          BEGIN
            BLOCK "040904B0"
            BEGIN
              VALUE "ProductName", "Statements"
              VALUE "FileVersion", "1.2.3.4"
            END
          END
          BLOCK "VarFileInfo"
          BEGIN
            VALUE "Translation", 0x409, 1200
          END
        END

        CREATEPROCESS_MANIFEST_RESOURCE_ID 24 "app.manifest"
        LANGUAGE 21, 1
        ISOLATIONAWARE_MANIFEST_RESOURCE_ID RT_MANIFEST "app.manifest"
        SETTINGS CONFIG "payload.bin"
        5 300 "payload.bin"

        """;

    /// <summary>The script with every statement of the card and the files it names.</summary>
    private static string WriteStatements(string dir)
    {
        Write(Path.Combine(dir, "src", "all.rc"), Statements);
        Write(Path.Combine(dir, "src", "ids.h"), "#define IDB_LOGO 2\n#define IDC_POINTER 3\n");
        Write(Path.Combine(dir, "src", "app.manifest"), "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"/>\n");
        File.WriteAllBytes(Path.Combine(dir, "src", "app.ico"), IconFile(1));
        File.WriteAllBytes(Path.Combine(dir, "src", "pointer.cur"), IconFile(2));
        File.WriteAllBytes(Path.Combine(dir, "src", "logo.bmp"), BitmapFile());
        File.WriteAllBytes(Path.Combine(dir, "src", "payload.bin"), [1, 2, 3, 4, 5, 6, 7]);
        return Path.Combine(dir, "src", "all.rc");
    }

    // ------------------------------------------------------------------ real tools

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1310")]
    public void Prc_AllSupportedStatements()
    {
        var dir = RealDirectory();
        try
        {
            var script = WriteStatements(dir);

            var (code, output, err) = Run(PhysicalFileSystem.Instance, dir, null, script, "--toolchain", Bin, "-D", "WITH_EXTRA");

            Assert.True(code == 0, string.Join("\n", output) + err);
            Assert.Empty(output);
            var entries = ResourceFile.Read(File.ReadAllBytes(Path.Combine(dir, "src", "all.res")));     // default: beside the script
            var golden = File.ReadAllText(Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Build.Tests", "Golden", "PrcStatements.txt"));
            Assert.Equal(
                golden.ReplaceLineEndings("\n"),
                string.Concat(entries.Select(e => $"{e.Type} {e.Name} {e.Language} {e.DataLength}\n")));
            Assert.True(Directory.Exists(Path.Combine(dir, "obj", "Resources", "rc-include")));           // default object directory
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1311")]
    public void Prc_SyntaxError_PrintsDiagnosticAndExitsOne()
    {
        var dir = RealDirectory();
        try
        {
            Write(Path.Combine(dir, "bad.rc"), "1 RCDATA\nBEGIN\n  1, 2\n");

            var (code, output, _) = Run(PhysicalFileSystem.Instance, dir, null, "bad.rc", "--toolchain", Bin);

            Assert.Equal(1, code);
            Assert.Equal(
                [Path.Combine(dir, "bad.rc") + "(0,0): error E3112: Resource script error: Error parsing file: expected int or string, got <EOF>"],
                output);
            Assert.False(File.Exists(Path.Combine(dir, "bad.res")));
            Assert.False(File.Exists(Path.Combine(dir, "bad.res.tmp")));

            // an error of the preprocessor carries the position in the user's file
            Write(Path.Combine(dir, "bad2.rc"), "1 RCDATA BEGIN 1 END\n#include \"nothere.h\"\n");

            (code, output, _) = Run(PhysicalFileSystem.Instance, dir, null, "bad2.rc", "--toolchain", Bin);

            Assert.Equal(1, code);
            Assert.Equal([Path.Combine(dir, "bad2.rc") + "(2,10): error E3111: Resource script: 'nothere.h' file not found"], output);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1310")]
    public void Prc_IncludePathAndDefine()
    {
        var dir = RealDirectory();
        try
        {
            Write(Path.Combine(dir, "res", "app.rc"), "#include \"ids.h\"\n#if LEVEL == 3\nID_DATA RCDATA \"data.bin\"\n#endif\n#ifdef NOT_DEFINED\n9 RCDATA \"data.bin\"\n#endif\n");
            Write(Path.Combine(dir, "shared", "ids.h"), "#define ID_DATA 42\n");
            Write(Path.Combine(dir, "shared", "data.bin"), "12345");

            var (code, output, err) = Run(
                PhysicalFileSystem.Instance, dir, null,
                @"res\app.rc", "-I", "shared", "-D", "LEVEL=3", "-o", @"out\app.res", "--obj-dir", "tmp", "--toolchain", Bin, "--verbose");

            Assert.True(code == 0, string.Join("\n", output) + err);
            Assert.Equal([new ResourceEntry(10, 42, 0x0409, 5)], ResourceFile.Read(File.ReadAllBytes(Path.Combine(dir, "out", "app.res"))));
            Assert.Equal(2, output.Length);                                 // both command lines, with absolute paths
            Assert.StartsWith("> ", output[0], StringComparison.Ordinal);
            Assert.Contains(" -DLEVEL=3 -I " + Path.Combine(dir, "res") + " -I " + Path.Combine(dir, "shared") + " -isystem " + Path.Combine(dir, "tmp", "rc-include"), output[0], StringComparison.Ordinal);
            Assert.EndsWith(" /fo " + Path.Combine(dir, "out", "app.res.tmp") + " " + Path.Combine(dir, "tmp", "app.rc.i"), output[1], StringComparison.Ordinal);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1308")]
    public void Prc_SameInputsGiveIdenticalBytes()
    {
        var dir = RealDirectory();
        try
        {
            var script = WriteStatements(dir);

            var first = Run(PhysicalFileSystem.Instance, dir, null, script, "--toolchain", Bin, "-o", "one.res", "--obj-dir", "obj1");
            var second = Run(PhysicalFileSystem.Instance, dir, null, script, "--toolchain", Bin, "-o", "two.res", "--obj-dir", "obj2");

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            var bytes = File.ReadAllBytes(Path.Combine(dir, "one.res"));
            Assert.True(bytes.Length > 2000);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(dir, "two.res")));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    // ------------------------------------------------------------------ no process

    private static InMemoryFileSystem Fs() => new InMemoryFileSystem()
        .Add(@"C:\tc\clang.exe", "")
        .Add(@"C:\tc\llvm-rc.exe", "")
        .Add(@"C:\work\app.rc", "1 RCDATA BEGIN 1 END\n");

    /// <summary>Writes what the two tools would write; starts nothing.</summary>
    private sealed class WritingRunner(InMemoryFileSystem fs) : IProcessRunner
    {
        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct)
        {
            var args = arguments.ToList();
            if (fileName.EndsWith("clang.exe", StringComparison.Ordinal))
            {
                fs.Add(args[args.IndexOf("-o") + 1], "# 1 \"C:\\\\work\\\\app.rc\"\n1 RCDATA BEGIN 1 END\n");
            }
            else
            {
                fs.WriteAllBytes(args[args.IndexOf("/fo") + 1], [1, 2, 3]);
            }

            return Task.FromResult(0);
        }
    }

    [Fact]
    [Trait("Req", "CMP-1311")]
    public void Prc_MissingTool_ExitsTwo()
    {
        var fs = Fs();
        fs.Delete(@"C:\tc\llvm-rc.exe");                                    // a directory with the preprocessor only

        var (code, output, _) = Run(fs, Cwd, new WritingRunner(fs), "app.rc", "--toolchain", @"C:\tc");

        Assert.Equal(2, code);
        Assert.Equal([@"C:\work\app.rc(0,0): error E4001: Toolchain not found: llvm-rc.exe (searched: C:\tc)"], output);
        Assert.False(fs.FileExists(@"C:\work\app.res"));

        (code, output, _) = Run(fs, Cwd, new WritingRunner(fs), "app.rc", "--toolchain", @"C:\nothing");
        Assert.Equal(2, code);
        Assert.Equal([@"C:\work\app.rc(0,0): error E4001: Toolchain not found: clang.exe (searched: C:\nothing)"], output);
    }

    [Fact]
    [Trait("Req", "CMP-1310")]
    public void Prc_JsonDiagnosticsAndSummary()
    {
        var fs = Fs();

        var (code, output, _) = Run(fs, Cwd, new WritingRunner(fs), "app.rc", "--toolchain", @"C:\tc", "--diagnostics", "json", "--verbose");

        Assert.Equal(0, code);
        Assert.Equal(3, output.Length);
        Assert.StartsWith("{\"event\":\"commandLine\",\"text\":\"C:\\\\tc\\\\clang.exe -E ", output[0], StringComparison.Ordinal);
        Assert.StartsWith("{\"event\":\"commandLine\",\"text\":\"C:\\\\tc\\\\llvm-rc.exe /no-preprocess ", output[1], StringComparison.Ordinal);
        Assert.StartsWith("{\"summary\":{\"errors\":0,\"warnings\":0,\"hints\":0,\"output\":\"C:\\\\work\\\\app.res\",\"ms\":", output[2], StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], fs.ReadAllBytes(@"C:\work\app.res"));
        Assert.True(fs.FileExists(@"C:\work\obj\Resources\app.rc.i"));

        (code, output, _) = Run(fs, Cwd, new WritingRunner(fs), "missing.rc", "--toolchain", @"C:\tc", "--diagnostics", "json");

        Assert.Equal(1, code);
        Assert.Equal(
            "{\"file\":\"C:\\\\work\\\\missing.rc\",\"line\":0,\"col\":0,\"endLine\":0,\"endCol\":0,\"severity\":\"error\",\"code\":\"E3110\","
                + "\"message\":\"Resource script 'C:\\\\work\\\\missing.rc' not found or unreadable\",\"related\":[]}",
            output[0]);
        Assert.StartsWith("{\"summary\":{\"errors\":1,\"warnings\":0,\"hints\":0,\"output\":null,\"ms\":", output[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-1311")]
    [Trait("Req", "CMP-1312")]
    public void Prc_HelpVersionAndUsageErrors()
    {
        var fs = Fs();

        var help = Run(fs, Cwd, null, "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Equal(
            [
                "prc <script.rc> [options]",
                "  -o <file>             output file (default: <script>.res beside the script)",
                "  -I <dir>              include directory (repeatable; searched in order after the script's directory)",
                "  -D <NAME[=value]>     define a preprocessor symbol (repeatable)",
                "  --obj-dir <dir>       intermediates (default: ./obj/Resources)",
                "  --toolchain <dir>     LLVM bin dir (default as opc)",
                "  --diagnostics text|json   (default text)",
                "  --verbose             echo external commands",
                "  --version | --help",
            ],
            help.Out);

        var version = Run(fs, Cwd, null, "--version");
        Assert.Equal(0, version.ExitCode);
        Assert.Equal([$"prc {CompilerInfo.Version} (abi {AbiInfo.Version}, llvm {CompilerInfo.LlvmPin} pin)"], version.Out);

        // a wrong command line: the driver's diagnostics on stdout, exit code 1 (as opc)
        Assert.Equal("1: " + @"C:\work(0,0): error E0007: No input file", Usage());
        Assert.Equal("1: " + @"C:\work\app.rc(0,0): error E0005: Invalid command-line option '--frob'", Usage("app.rc", "--frob"));
        Assert.Equal("1: " + @"C:\work\app.rc(0,0): error E0006: Missing argument for option '-I'", Usage("app.rc", "-I"));
        Assert.Equal("1: " + @"C:\work\app.rc(0,0): error E0005: Invalid command-line option '--diagnostics xml'", Usage("app.rc", "--diagnostics", "xml"));
        Assert.Equal("1: " + @"C:\work\app.rc(0,0): error E0008: More than one input file ('C:\work\app.rc' and 'C:\work\b.rc')", Usage("app.rc", "b.rc"));

        string Usage(params string[] args)
        {
            var (code, output, _) = Run(fs, Cwd, null, args);
            return code + ": " + string.Join('|', output);
        }
    }
}
