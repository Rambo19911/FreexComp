using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// dll-output.md §10.4 (task C-288): a library linked with the pinned toolchain — its export table and header read back with
/// <c>llvm-readobj</c>, its import library with <c>llvm-nm</c>, the rebuild compared byte for byte, and a DLL another module
/// has loaded (CMP-1064).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed partial class DllLinkNativeTests : IDisposable
{
    private const string Source = """
        library mathlib;

        type
          TVec3 = record X, Y, Z: Integer end;

        function Add(A, B: Integer): Integer; stdcall;
        begin
          Result := A + B;
        end;

        function Scale(V, Factor: Double): Double; cdecl;
        begin
          Result := V * Factor;
        end;

        function MakeVec(X, Y, Z: Integer): TVec3; cdecl;
        begin
          Result.X := X; Result.Y := Y; Result.Z := Z;
        end;

        function Greeting: PWideChar;
        begin
          Result := 'hello';
        end;

        exports
          Add,
          Scale name 'scale_value',
          MakeVec name 'make_vec' index 7,
          Greeting;

        begin
        end.
        """;

    private readonly string _dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));

    public DllLinkNativeTests()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(_dir, "mathlib.pas"), Encoding.UTF8.GetBytes(Source));
    }

    private static string Tool(string name) => Path.Combine(Path.GetDirectoryName(Clang.Path)!, name);

    private EmitResult Build(string folder, bool force = false)
    {
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(_dir, "mathlib.pas"),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = Path.Combine(_dir, folder, "obj"),
            AppType = AppType.Library,
        };
        return Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = Path.Combine(_dir, folder, "mathlib.dll"), ForceRebuild = force }, TestContext.Current.CancellationToken);
    }

    private string BuildOk(string folder = "bin")
    {
        var result = Build(folder);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(Path.Combine(_dir, folder, "mathlib.dll"), result.OutputPath);
        return result.OutputPath!;
    }

    private async Task<string> RunToolAsync(string tool, params string[] arguments)
    {
        var lines = new ConcurrentQueue<string>();
        var exit = await NativeRun.RunAsync(Tool(tool), arguments, _dir, lines.Enqueue, lines.Enqueue);
        var output = string.Join("\n", lines);
        Assert.True(exit == 0, $"{tool} exit code {exit}\n{output}");
        return output;
    }

    [Fact]
    [Trait("Req", "LANG-549")]
    [Trait("Req", "CMP-1062")]
    public async Task ExportsListedByReadobj()
    {
        var dll = BuildOk();

        var exports = await RunToolAsync("llvm-readobj.exe", "--coff-exports", dll);
        var listed = ExportEntry().Matches(exports).Select(m => (Name: m.Groups["name"].Value, Ordinal: int.Parse(m.Groups["ordinal"].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(e => e.Ordinal).ToArray();
        // LANG-549: explicit index 7; the others get 1, 2, 3 in source order
        Assert.Equal([("Add", 1), ("scale_value", 2), ("Greeting", 3), ("make_vec", 7)], listed);

        var headers = await RunToolAsync("llvm-readobj.exe", "--file-headers", dll);
        Assert.Contains("IMAGE_FILE_DLL", headers, StringComparison.Ordinal);
        Assert.Contains($"(0x{TargetInfo.Win64.CoffMachine:X})", headers, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"Export \{\s*Ordinal: (?<ordinal>\d+)\s*Name: (?<name>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ExportEntry();

    [Fact]
    [Trait("Req", "CMP-1063")]
    public async Task ImportLibraryResolvesAllExports()
    {
        var lib = Path.ChangeExtension(BuildOk(), ".lib");

        Assert.True(File.Exists(lib));
        var symbols = (await RunToolAsync("llvm-nm.exe", lib)).Split('\n').Select(l => l.Trim().Split(' ')[^1]).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[] { "Add", "scale_value", "make_vec", "Greeting" })
        {
            Assert.Contains("__imp_" + name, symbols);
        }

        Assert.DoesNotContain("__imp_MakeVec", symbols);                         // the export name, not the routine name
    }

    [Fact]
    [Trait("Req", "CMP-001")]
    public void RebuildIsByteIdentical()
    {
        // two separate object directories and output folders with the same file names: /Brepro leaves no time stamp or path
        var first = BuildOk("a");
        var second = BuildOk("b");

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(first, ".lib")), File.ReadAllBytes(Path.ChangeExtension(second, ".lib")));

        // and a forced rebuild in place
        var before = File.ReadAllBytes(first);
        var beforeLib = File.ReadAllBytes(Path.ChangeExtension(first, ".lib"));
        Assert.True(Build("a", force: true).Success);
        Assert.Equal(before, File.ReadAllBytes(first));
        Assert.Equal(beforeLib, File.ReadAllBytes(Path.ChangeExtension(first, ".lib")));
    }

    [Fact]
    [Trait("Req", "CMP-1064")]
    public void LoadedDllCannotBeOverwritten_E4008()
    {
        var dll = BuildOk();

        // mapped as a module of this process without running its entry routine: the loader holds the image as for any loaded DLL
        var module = LoadLibraryExW(dll, 0, DontResolveDllReferences);
        Assert.True(module != 0, "LoadLibraryExW failed: " + Marshal.GetLastPInvokeError());
        try
        {
            var result = Build("bin", force: true);

            Assert.False(result.Success);
            var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal("E4008", error.Code);
            Assert.Equal($"Cannot write output file '{dll}': the file is in use by another process", error.Message);
            Assert.True(File.Exists(dll), "the loaded DLL stays");
        }
        finally
        {
            Assert.True(FreeLibrary(module));
        }

        // released: the same build goes through
        Assert.True(Build("bin", force: true).Success);
    }

    private const uint DontResolveDllReferences = 0x00000001;

#pragma warning disable SYSLIB1054 // the source-generated form needs unsafe code, which the test projects do not enable
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
#pragma warning restore SYSLIB1054

    public void Dispose()
    {
        try
        {
            TestDirectories.Delete(_dir);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
