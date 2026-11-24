using FreexComp.Compiler;
using FreexComp.Opc;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>dll-output.md CMP-1060, CMP-1070 (tasks C-288, C-289): <c>opc</c> on a library main file, <c>--app-type library</c>, <c>-L</c>.</summary>
public sealed partial class OpcCliTests
{
    private const string LibrarySource = """
        library P;
        {$IFDEF CONSOLE} this is not Pascal: CONSOLE is never defined for a library (LANG-543) {$ENDIF}
        function Add(A, B: Integer): Integer; stdcall;
        begin
          Result := A + B;
        end;
        exports Add;
        begin
        end.
        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1060")]
    [Trait("Req", "LANG-543")]
    public void LibraryMain_DefaultOutputIsDll()
    {
        // the parsed defaults: only an explicit --app-type library is known before the main file is read
        Assert.Equal(@"C:\work\P.exe", Parse("P.pas").Emit!.OutputPath);
        Assert.Equal(@"C:\work\P.dll", Parse("P.pas", "--app-type", "library").Emit!.OutputPath);
        Assert.Equal(AppType.Library, Parse("P.pas", "--app-type", "library").Compilation!.AppType);
        Assert.Equal(@"C:\work\out\x.bin", Parse("P.pas", "--app-type", "library", "-o", @"out\x.bin").Emit!.OutputPath);

        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src"));
            File.WriteAllText(Path.Combine(dir, "src", "P.pas"), LibrarySource);
            (int Code, string Out) Opc(params string[] args)
            {
                var stdout = new StringWriter { NewLine = "\n" };
                var code = OpcCommand.Run(
                    [Path.Combine("src", "P.pas"), "--rtl", Path.Combine(RepoPaths.Root, "rtl", "src"), "--toolchain", bin, .. args],
                    new OpcEnvironment(dir, stdout, new StringWriter(), PhysicalFileSystem.Instance), TestContext.Current.CancellationToken);
                return (code, stdout.ToString());
            }

            // a library main file selects DLL output by itself: <current dir>\<main base name>.dll and its import library
            var (code, output) = Opc("--diagnostics", "json");
            Assert.True(code == 0, output);
            Assert.True(File.Exists(Path.Combine(dir, "P.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "P.lib")));
            Assert.False(File.Exists(Path.Combine(dir, "P.exe")));
            Assert.Contains("\"output\":\"" + Path.Combine(dir, "P.dll").Replace(@"\", @"\\", StringComparison.Ordinal) + "\"", output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(dir, "obj", "Debug", "P.exports.def")));

            // --app-type library is accepted with it; -o is used as given
            (code, output) = Opc("--app-type", "library", "-o", Path.Combine("out", "renamed.bin"));
            Assert.True(code == 0, output);
            Assert.Empty(output);
            Assert.True(File.Exists(Path.Combine(dir, "out", "renamed.bin")));
            Assert.True(File.Exists(Path.Combine(dir, "out", "renamed.lib")));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1070")]
    public void LibPathOption_ParsedInOrder()
    {
        Assert.Empty(Parse("P.pas").Compilation!.LibraryPaths);
        // repeatable, in command-line order, relative to the current directory; independent of -U
        var parsed = Parse("P.pas", "-L", @"libs\b", "-U", "units", "-L", @"D:\abs", "-L", "a").Compilation!;
        Assert.Equal([@"C:\work\libs\b", @"D:\abs", @"C:\work\a"], parsed.LibraryPaths);
        Assert.Equal([@"C:\work\units"], parsed.SearchPaths);

        var (code, output, _) = Run(Fs(), "P.pas", "-L");
        Assert.Equal(1, code);
        Assert.EndsWith("(0,0): error E0006: Missing argument for option '-L'", Assert.Single(output), StringComparison.Ordinal);

        var help = Run(new InMemoryFileSystem(), "--help").Out;
        var units = Array.FindIndex(help, l => l.StartsWith("  -U <dir>", StringComparison.Ordinal));
        Assert.StartsWith("  -L <dir>", help[units + 1], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1060")]
    [InlineData(LibrarySource, "console", "library")]
    [InlineData(LibrarySource, "gui", "library")]
    [InlineData(Source, "library", "program")]
    public void AppTypeMismatch_E0012(string source, string appType, string kind)
    {
        var fs = Fs(source);
        var files = fs.EnumerateFiles(Cwd, "*").ToList();

        var (code, output, err) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--app-type", appType);

        Assert.Equal(1, code);
        Assert.Equal([$@"C:\work\P.pas(0,0): error E0012: Application type '{appType}' does not match the main source: 'P.pas' is a {kind} file"], output);
        Assert.Empty(err);
        Assert.Equal(files, fs.EnumerateFiles(Cwd, "*"));                    // nothing is built
        Assert.Empty(fs.EnumerateDirectories(Cwd));

        // also for --check; and no mismatch without the option (the main source decides)
        Assert.Equal(1, Run(fs, "P.pas", "--rtl", @"C:\rtl", "--app-type", appType, "--check").ExitCode);
        var (ok, checkOutput, _) = Run(fs, "P.pas", "--rtl", @"C:\rtl", "--check");
        Assert.True(ok == 0, string.Join("\n", checkOutput));
    }
}
