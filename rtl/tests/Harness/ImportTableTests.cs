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
    [Trait("Req", "RTL-1082")]
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

    /// <summary>A console program that uses the rich edit binding unit: constants and record sizes only.</summary>
    private const string RichEditProgram = """
        program ImportCheckRichEdit;
        uses
          Windows, RichEdit;
        var
          CF: TCharFormat2W;
        begin
          CF.cbSize := SizeOf(CF);
          Writeln(MSFTEDIT_CLASS, ' ', CF.cbSize, ' ', EM_SETTEXTEX, ' ', SizeOf(TEditStream), ' ', CP_ACP);
        end.
        """;

    /// <summary>The same program without the rich edit unit: what <c>Windows</c> alone causes.</summary>
    private const string WindowsOnlyProgram = """
        program ImportCheckWindowsOnly;
        uses
          Windows;
        begin
          Writeln('RICHEDIT50W', ' ', 116, ' ', 1121, ' ', 20, ' ', CP_ACP);
        end.
        """;

    /// <summary>
    /// RTL-1025: unit <c>RichEdit</c> compiles with only <c>rtl/src</c> and <c>rtl/winapi/src</c> and declares no import:
    /// a console program that uses it imports exactly the libraries the same program imports with <c>Windows</c> alone,
    /// and neither the engine library nor <c>user32.dll</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1025")]
    [Trait("Phase", "8")]
    public async Task RichEditUnitAddsNoImport()
    {
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckWindowsOnly", WindowsOnlyProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("RICHEDIT50W 116 1121 20 0", output.TrimEnd());
        });
        Assert.Contains("kernel32.dll", baseline);

        await WithProgramAsync("ImportCheckRichEdit", RichEditProgram, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Equal(baseline.Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));
            Assert.DoesNotContain("msftedit.dll", dlls);
            Assert.DoesNotContain("user32.dll", dlls);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("RICHEDIT50W 116 1121 20 0", output.TrimEnd());
        });
    }

    /// <summary>A console program that uses the imaging binding unit: constants, GUIDs, a record size and an interface variable.</summary>
    private const string WincodecProgram = """
        program ImportCheckWincodec;
        uses
          Windows, ActiveX, Wincodec;
        var
          Factory: IWICImagingFactory;
          R: TWICRect;
        begin
          Factory := nil;
          R.Width := WICBitmapTransformFlipVertical;
          Writeln(SizeOf(R), ' ', R.Width, ' ', GUID_ContainerFormatPng.D1 = CLSID_WICImagingFactory.D1, ' ', Factory = nil, ' ', CP_ACP);
        end.
        """;

    /// <summary>The same program without the imaging unit: what <c>Windows</c> and <c>ActiveX</c> alone cause.</summary>
    private const string WincodecBaselineProgram = """
        program ImportCheckWincodecBase;
        uses
          Windows, ActiveX;
        begin
          Writeln(16, ' ', 16, ' ', False, ' ', True, ' ', CP_ACP);
        end.
        """;

    /// <summary>A program that calls two ole32 functions and none of oleaut32, shlwapi or gdi32.</summary>
    private const string WincodecCallingProgram = """
        program ImportCheckWincodecCalls;
        uses
          Windows, ActiveX, Wincodec;
        var
          Factory: IWICImagingFactory;
        begin
          if Succeeded(CoInitializeEx(nil, COINIT_APARTMENTTHREADED)) then
          begin
            Writeln(CoCreateInstance(CLSID_WICImagingFactory, nil, CLSCTX_INPROC_SERVER, IWICImagingFactory, Factory) = S_OK,
              ' ', Factory <> nil);
            Factory := nil;
            CoUninitialize;
          end;
        end.
        """;

    /// <summary>
    /// RTL-1036: unit <c>Wincodec</c> compiles on its own and declares no import: a console program that uses it without
    /// calling anything imports exactly the libraries the same program imports without it (no <c>ole32.dll</c>, no
    /// <c>windowscodecs.dll</c>), and a program that calls <c>ole32</c> functions imports from <c>ole32.dll</c> exactly the
    /// functions it calls.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1036")]
    public async Task WincodecUnitAddsNoImport()
    {
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckWincodecBase", WincodecBaselineProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("16 16 FALSE TRUE 0", output.TrimEnd());
        });
        Assert.Contains("kernel32.dll", baseline);

        await WithProgramAsync("ImportCheckWincodec", WincodecProgram, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Equal(baseline.Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));
            Assert.DoesNotContain("ole32.dll", dlls);
            Assert.DoesNotContain("oleaut32.dll", dlls);
            Assert.DoesNotContain("shlwapi.dll", dlls);
            Assert.DoesNotContain("gdi32.dll", dlls);
            Assert.DoesNotContain("windowscodecs.dll", dlls);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("16 16 FALSE TRUE 0", output.TrimEnd());
        });

        await WithProgramAsync("ImportCheckWincodecCalls", WincodecCallingProgram, async (exe, work) =>
        {
            var image = await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken);
            var dlls = ImportedDlls(image);
            Assert.Equal(baseline.Append("ole32.dll").Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));
            Assert.Equal(["CoCreateInstance", "CoInitializeEx", "CoUninitialize"],
                ImportedFunctions(image, "ole32.dll").Order(StringComparer.Ordinal));

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("TRUE TRUE", output.TrimEnd());
        });
    }

    /// <summary>A console program that uses <c>Classes</c> streams but not the resource stream.</summary>
    private const string NoResourceStreamProgram = """
        program ImportCheckNoResourceStream;
        uses
          SysUtils, Classes;
        var
          M: TMemoryStream;
        begin
          M := TMemoryStream.Create;
          M.WriteBuffer(M, SizeOf(M));
          Writeln(M.Size);
          M.Free;
        end.
        """;

    /// <summary>The same with the resource stream: reads the application manifest of the program (resource type 24, number 1).</summary>
    private const string ResourceStreamProgram = """
        program ImportCheckResourceStream;
        uses
          SysUtils, Classes, Windows;
        var
          R: TResourceStream;
        begin
          R := TResourceStream.CreateFromID(HInstance, 1, RT_MANIFEST);
          Writeln(R.Size > 0);
          R.Free;
        end.
        """;

    /// <summary>
    /// RTL-1055: <c>TResourceStream</c> adds no imported library: a console program that creates such a stream imports
    /// exactly the libraries the same kind of program imports without it. (The four resource functions are kernel32
    /// exports; a unit is linked whole, so every program that uses <c>Classes</c> lists their names under kernel32,
    /// with or without the class — the second program shows that they resolve and work.)
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1055")]
    public async Task ResourceStreamAddsNoImportedLibrary()
    {
        string[] resourceFunctions = ["FindResourceW", "LoadResource", "LockResource", "SizeofResource"];
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckNoResourceStream", NoResourceStreamProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Contains("kernel32.dll", baseline);
            Assert.DoesNotContain("user32.dll", baseline);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("8", output.TrimEnd());
        });

        await WithProgramAsync("ImportCheckResourceStream", ResourceStreamProgram, async (exe, work) =>
        {
            var image = await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken);
            Assert.Equal(baseline.Order(StringComparer.Ordinal), ImportedDlls(image).Order(StringComparer.Ordinal));
            Assert.Equal(resourceFunctions, ImportedFunctions(image, "kernel32.dll").Intersect(resourceFunctions).Order(StringComparer.Ordinal));

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("TRUE", output.TrimEnd());
        });
    }

    /// <summary>A console program that encodes three bytes with the shared Base64 instance of <c>NetEncoding</c>.</summary>
    private const string NetEncodingProgram = """
        program ImportCheckNetEncoding;
        uses
          SysUtils, Classes, NetEncoding;
        begin
          Writeln(TNetEncoding.Base64String.Encode('foo'));
        end.
        """;

    /// <summary>The same program with <c>SysUtils</c> and <c>Classes</c> only.</summary>
    private const string NetEncodingBaselineProgram = """
        program ImportCheckNetEncodingBase;
        uses
          SysUtils, Classes;
        begin
          Writeln('Zm9v');
        end.
        """;

    /// <summary>
    /// RTL-1200: unit <c>NetEncoding</c> is pure Object Pascal: a console program that uses it and encodes with
    /// <c>TNetEncoding.Base64String</c> imports exactly the libraries the same program imports with <c>SysUtils</c> and
    /// <c>Classes</c> only.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1200")]
    [Trait("Phase", "8")]
    public async Task NetEncodingUnitAddsNoImport()
    {
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckNetEncodingBase", NetEncodingBaselineProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("Zm9v", output.TrimEnd());
        });
        Assert.Contains("kernel32.dll", baseline);

        await WithProgramAsync("ImportCheckNetEncoding", NetEncodingProgram, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Equal(baseline.Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("Zm9v", output.TrimEnd());
        });
    }

    /// <summary>A console program that computes one SHA-256 digest with unit <c>Hash</c>.</summary>
    private const string HashProgram = """
        program ImportCheckHash;
        uses
          SysUtils, Classes, Hash;
        begin
          Writeln(THashSHA2.GetHashString('abc'));
        end.
        """;

    /// <summary>The same program with <c>SysUtils</c> and <c>Classes</c> only.</summary>
    private const string HashBaselineProgram = """
        program ImportCheckHashBase;
        uses
          SysUtils, Classes;
        begin
          Writeln('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
        end.
        """;

    /// <summary>
    /// RTL-1230: unit <c>Hash</c> computes every digest in Object Pascal: a console program that uses it and calls
    /// <c>THashSHA2.GetHashString</c> imports exactly the libraries the same program imports with <c>SysUtils</c> and
    /// <c>Classes</c> only - in particular neither of the two system libraries that offer hashing services.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1230")]
    [Trait("Phase", "8")]
    public async Task HashUnitAddsNoImport()
    {
        const string digest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckHashBase", HashBaselineProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal(digest, output.TrimEnd());
        });
        Assert.Contains("kernel32.dll", baseline);

        await WithProgramAsync("ImportCheckHash", HashProgram, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Equal(baseline.Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));
            Assert.DoesNotContain("bcrypt.dll", dlls, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("advapi32.dll", dlls, StringComparer.OrdinalIgnoreCase);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal(digest, output.TrimEnd());
        });
    }

    /// <summary>A console program that asks for a new GUID through <c>TGUID.NewGuid</c> and prints the length of its text.</summary>
    private const string GuidProgram = """
        program ImportCheckGuid;
        uses
          SysUtils;
        begin
          Writeln(Length(TGUID.NewGuid.ToString));
        end.
        """;

    /// <summary>The same program without the GUID.</summary>
    private const string GuidBaselineProgram = """
        program ImportCheckGuidBase;
        uses
          SysUtils;
        begin
          Writeln(38);
        end.
        """;

    /// <summary>
    /// RTL-1275: <c>CreateGUID</c> (and with it <c>TGUID.NewGuid</c>) finds <c>CoCreateGuid</c> at run time, so a console
    /// program that creates a GUID imports exactly the libraries of the same program without it — no
    /// <c>ole32.dll</c> — and still prints the 38 characters of a GUID's text.
    /// </summary>
    [Fact]
    [Trait("Req", "RTL-1275")]
    [Trait("Phase", "8")]
    public async Task GuidHelperAddsNoImport()
    {
        List<string> baseline = [];
        await WithProgramAsync("ImportCheckGuidBase", GuidBaselineProgram, async (exe, work) =>
        {
            baseline = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("38", output.TrimEnd());
        });
        Assert.Contains("kernel32.dll", baseline);

        await WithProgramAsync("ImportCheckGuid", GuidProgram, async (exe, work) =>
        {
            var dlls = ImportedDlls(await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
            Assert.Equal(baseline.Order(StringComparer.Ordinal), dlls.Order(StringComparer.Ordinal));
            Assert.DoesNotContain("ole32.dll", dlls, StringComparer.OrdinalIgnoreCase);

            var (exit, output) = await RunAsync(exe, "", work);
            Assert.Equal(0, exit);
            Assert.Equal("38", output.TrimEnd());
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

    /// <summary>The function names a 64-bit PE image imports by name from <paramref name="dll"/> (lower-case file name), in table order.</summary>
    public static List<string> ImportedFunctions(byte[] pe, string dll)
    {
        int I32(int at) => BitConverter.ToInt32(pe, at);
        int U16(int at) => BitConverter.ToUInt16(pe, at);

        var header = I32(0x3C);
        var sections = U16(header + 6);
        var optional = header + 24;
        Assert.True(U16(optional) == 0x20B, "not a 64-bit image");
        var importRva = I32(optional + 112 + 8);
        var sectionTable = optional + U16(header + 20);

        int FileOffset(int rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + i * 40;
                var virtualAddress = I32(s + 12);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(I32(s + 8), I32(s + 16)))
                {
                    return rva - virtualAddress + I32(s + 20);
                }
            }

            throw new InvalidOperationException($"RVA {rva:X} is in no section");
        }

        string Text(int at) => Encoding.ASCII.GetString(pe, at, Array.IndexOf(pe, (byte)0, at) - at);

        var names = new List<string>();
        for (var d = FileOffset(importRva); importRva != 0 && I32(d + 12) != 0; d += 20)
        {
            if (!string.Equals(Text(FileOffset(I32(d + 12))), dll, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lookup = I32(d) != 0 ? I32(d) : I32(d + 16);       // the import lookup table, else the address table
            for (var t = FileOffset(lookup); BitConverter.ToInt64(pe, t) != 0; t += 8)
            {
                var entry = BitConverter.ToInt64(pe, t);
                Assert.True(entry >= 0, dll + ": an import by ordinal");
                names.Add(Text(FileOffset((int)entry) + 2));          // skip the hint
            }
        }

        return names;
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
