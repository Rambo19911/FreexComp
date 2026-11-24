using System.Diagnostics;
using System.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-841 (winapi.md §5.3): a console program that uses <c>SysUtils</c>, including the case-mapping routines of the
/// <c>Ansi*</c> family (R-047), does not import <c>user32.dll</c>. The program is compiled against <c>rtl/src</c> and the
/// import directory of the resulting PE file is read here.
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class ImportTableTests
{
    private const string Program = """
        program ImportCheck;
        uses
          SysUtils;
        begin
          Writeln(AnsiUpperCase('hello'), ' ', AnsiLowerCase('WORLD'), ' ', AnsiCompareText('a', 'B'), ' ',
            AnsiSameStr('x', 'x'), ' ', UpperCase('ok'), ' ', IntToStr(42));
        end.
        """;

    /// <summary>A program that calls a user32 function directly, to show that the import reader sees user32.</summary>
    private const string User32Program = """
        program ImportCheckUser32;
        uses
          Windows;
        var
          B: array[0..1] of WideChar;
        begin
          B[0] := 'a';
          B[1] := #0;
          CharUpperBuffW(@B[0], 1);
          Writeln(B[0]);
        end.
        """;

    [Fact]
    [Trait("Req", "RTL-841")]
    public async Task ConsoleAppDoesNotImportUser32()
    {
        await WithProgramAsync("ImportCheck", Program, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Contains("kernel32.dll", dlls);
            Assert.DoesNotContain("user32.dll", dlls);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("HELLO world -1 TRUE OK 42", output.TrimEnd());
        });
    }

    /// <summary>The check above is meaningful: the same reader finds user32 in a program that uses it.</summary>
    [Fact]
    [Trait("Req", "RTL-841")]
    public async Task ImportReaderSeesUser32()
    {
        await WithProgramAsync("ImportCheckUser32", User32Program, async (exe, _) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Contains("kernel32.dll", dlls);
            Assert.Contains("user32.dll", dlls);
        });
    }

    /// <summary>Compiles <paramref name="text"/> against <c>rtl/src</c> in a fresh directory and hands the exe to <paramref name="check"/>.</summary>
    private static async Task WithProgramAsync(string name, string text, Func<string, string, Task> check)
    {
        var setup = RtlSetup.Default;
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = Path.Combine(work, name + ".pas");
            await File.WriteAllTextAsync(source, text, TestContext.Current.CancellationToken);
            var exe = Path.Combine(work, name + ".exe");
            string[] args =
            [
                source, "-U", setup.UnitDirectory, "-o", exe, "--obj-dir", Path.Combine(work, "obj"), .. setup.CompilerOptions,
            ];
            var (exit, output) = await RunAsync(setup.OpcPath, GoldenRunner.CommandLine(args, null), work);
            Assert.True(exit == 0, $"opc exit code {exit}\n{output}");
            await check(exe, work);
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
    }

    /// <summary>The lower-cased DLL names of the PE import directory (data directory 1).</summary>
    public static List<string> ImportedDlls(byte[] pe)
    {
        int I32(int at) => BitConverter.ToInt32(pe, at);
        int U16(int at) => BitConverter.ToUInt16(pe, at);

        var header = I32(0x3C);
        Assert.True(I32(header) == 0x4550, "no PE signature");
        var sections = U16(header + 6);
        var optionalSize = U16(header + 20);
        var optional = header + 24;
        var magic = U16(optional);
        var dataDirectories = optional + (magic == 0x20B ? 112 : 96);
        var importRva = I32(dataDirectories + 8);
        var sectionTable = optional + optionalSize;

        int FileOffset(int rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + i * 40;
                var virtualSize = I32(s + 8);
                var virtualAddress = I32(s + 12);
                var rawSize = I32(s + 16);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                {
                    return rva - virtualAddress + I32(s + 20);
                }
            }

            throw new InvalidOperationException($"RVA {rva:X} is in no section");
        }

        var dlls = new List<string>();
        if (importRva == 0)
        {
            return dlls;
        }

        for (var d = FileOffset(importRva); I32(d + 12) != 0; d += 20)
        {
            var name = FileOffset(I32(d + 12));
            var end = Array.IndexOf(pe, (byte)0, name);
            dlls.Add(Encoding.ASCII.GetString(pe, name, end - name).ToLowerInvariant());
        }

        return dlls;
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(info)!;
        var name = Path.GetFileName(file);
        var stdout = ChildPipe.ReadToEnd(process.StandardOutput.BaseStream, name + " stdout");   // a thread per pipe
        var stderr = ChildPipe.ReadToEnd(process.StandardError.BaseStream, name + " stderr");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(cts.Token);
        var output = process.StandardOutput.CurrentEncoding.GetString(await stdout.WaitAsync(TestContext.Current.CancellationToken));
        var errors = process.StandardError.CurrentEncoding.GetString(await stderr.WaitAsync(TestContext.Current.CancellationToken));
        return (process.ExitCode, output + errors);
    }
}
