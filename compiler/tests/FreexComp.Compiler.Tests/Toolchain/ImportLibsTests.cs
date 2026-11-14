using System.Text;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-077, toolchain.md §4 across units (CMP-620…623): one import library for every DLL named by any linked unit (plus kernel32
/// and ntdll), each from the first <c>.def</c> found in the CMP-620 order, and <c>{$L}</c> inputs reaching the link.
/// </summary>
public sealed partial class ToolchainTests
{
    private const string RtlSrc = @"C:\repo\rtl\src";

    private static LinkUnit Unit(string directory, string[]? dlls = null, string[]? defs = null, string[]? objects = null) =>
        new(directory, new UnitLinkInputs([.. dlls ?? []], [.. objects ?? []], [.. defs ?? []]));

    private static string[] Planned(InMemoryFileSystem fs, params LinkUnit[] units)
    {
        var (libraries, errors) = ImportLibraryPlan.Create(fs, units, @"C:\rtl-sys", RtlSrc, MainLocation);
        Assert.Empty(errors);
        return [.. libraries.Select(l => l.Dll + " <- " + l.Definition)];
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void ImportLibs_EveryDllNamedByAnyUnit_PlusKernel32AndNtdll_SortedByName()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\repo\rtl\winapi\def\kernel32.def", "")
            .Add(@"C:\repo\rtl\winapi\def\ntdll.def", "")
            .Add(@"C:\a\user32.def", "")
            .Add(@"C:\b\gdi32.def", "");

        Assert.Equal(
            [
                @"gdi32.dll <- C:\b\gdi32.def",
                @"kernel32.dll <- C:\repo\rtl\winapi\def\kernel32.def",
                @"ntdll.dll <- C:\repo\rtl\winapi\def\ntdll.def",
                @"user32.dll <- C:\a\user32.def",
            ],
            Planned(fs, Unit(@"C:\p"), Unit(@"C:\a", dlls: ["user32.dll"]), Unit(@"C:\b", dlls: ["gdi32.dll", "user32.dll"])));
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void ImportLibs_DefinitionLookupOrder_LinkDirectiveDef_ThenDeclaringUnitDirectories_ThenTheRtl()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\repo\rtl\winapi\def\kernel32.def", "")
            .Add(@"C:\repo\rtl\winapi\def\ntdll.def", "")
            .Add(@"C:\repo\rtl\winapi\def\user32.def", "")
            .Add(@"C:\second\user32.def", "")
            .Add(@"C:\first\shell32.def", "")
            .Add(@"C:\second\shell32.def", "")
            .Add(@"C:\x\defs\Kernel32.def", "");

        Assert.Equal(
            [
                @"kernel32.dll <- C:\x\defs\Kernel32.def",                  // {$L} of any unit, even one that does not declare it
                @"ntdll.dll <- C:\repo\rtl\winapi\def\ntdll.def",
                @"shell32.dll <- C:\first\shell32.def",                     // first declaring unit in link order
                @"user32.dll <- C:\second\user32.def",                      // a declaring unit's directory before the RTL
            ],
            Planned(
                fs,
                Unit(@"C:\none", dlls: ["kernel32.dll"]),
                Unit(@"C:\first", dlls: ["shell32.dll", "user32.dll"]),
                Unit(@"C:\second", dlls: ["shell32.dll", "user32.dll"]),
                Unit(@"C:\x", defs: [@"C:\x\defs\Kernel32.def"])));
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void ImportLibs_LinkDirectiveDef_AddsItsDll_EvenWithoutAnExternalDeclaration()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\repo\rtl\winapi\def\kernel32.def", "")
            .Add(@"C:\repo\rtl\winapi\def\ntdll.def", "")
            .Add(@"C:\u\libs\Comdlg32.def", "");

        Assert.Contains(@"comdlg32.dll <- C:\u\libs\Comdlg32.def", Planned(fs, Unit(@"C:\u", defs: [@"C:\u\libs\Comdlg32.def"])));
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void ImportLibs_AlwaysLinkedDll_FallsBackToTheSystemDirectory()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\rtl-sys\kernel32.def", "")
            .Add(@"C:\rtl-sys\ntdll.def", "");

        Assert.Equal([@"kernel32.dll <- C:\rtl-sys\kernel32.def", @"ntdll.dll <- C:\rtl-sys\ntdll.def"], Planned(fs, Unit(@"C:\p")));
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void ImportLibs_MissingDefinition_IsE4006_PerDll_AndTheOthersArePlanned()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\repo\rtl\winapi\def\kernel32.def", "")
            .Add(@"C:\repo\rtl\winapi\def\ntdll.def", "");

        var (libraries, errors) = ImportLibraryPlan.Create(fs, [Unit(@"C:\p", dlls: ["winmm.dll", "advapi32.dll"])], @"C:\rtl-sys", RtlSrc, MainLocation);

        Assert.Equal(["kernel32.dll", "ntdll.dll"], libraries.Select(l => l.Dll));
        Assert.Equal(
            ["No import definition (.def) found for 'advapi32.dll'", "No import definition (.def) found for 'winmm.dll'"],
            errors.Select(e => e.Message));
        Assert.All(errors, e => Assert.Equal("E4006", e.Code));
    }

    /// <summary>
    /// End to end with the pinned toolchain: a unit's <c>{$L x.obj}</c> (an object built here by clang from hand-written IR) is
    /// passed to the link and its routine, declared <c>external;</c> with a link name, is called; a <c>{$L x.def}</c> import
    /// library is built from that file.
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-620")]
    [Trait("Req", "CMP-625")]
    [Trait("Req", "LANG-463")]
    public async Task ImportLibs_RealToolchain_LinkDirectiveObjectAndDefinition()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var ct = TestContext.Current.CancellationToken;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var fs = PhysicalFileSystem.Instance;
        try
        {
            fs.WriteAllBytes(Path.Combine(dir, "native", "triple.ll"), Encoding.UTF8.GetBytes(
                "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @native_triple(i32 %x) {\n  %r = mul i32 %x, 3\n  ret i32 %r\n}\n"));
            var compile = await new ProcessRunner().RunAsync(
                Clang.Path, ["-c", Path.Combine(dir, "native", "triple.ll"), "-o", Path.Combine(dir, "native", "triple.obj"), "--target=x86_64-pc-windows-msvc"],
                dir, _ => { }, _ => { }, ct);
            Assert.Equal(0, compile);
            fs.WriteAllBytes(Path.Combine(dir, "defs", "user32.def"), Encoding.UTF8.GetBytes("LIBRARY user32.dll\nEXPORTS\nIsCharAlphaW\n"));
            fs.WriteAllBytes(Path.Combine(dir, "Native.pas"), Encoding.UTF8.GetBytes("""
                unit Native;
                {$L native\triple.obj}
                {$L defs/user32.def}
                interface
                function Triple(X: Integer): Integer; external name 'native_triple';
                function IsAlpha(C: WideChar): LongBool; stdcall; external 'user32.dll' name 'IsCharAlphaW';
                implementation
                end.
                """));
            fs.WriteAllBytes(Path.Combine(dir, "P.pas"), Encoding.UTF8.GetBytes("""
                program P;
                uses Native;
                begin
                  Writeln(Triple(14));
                  Writeln(IsAlpha('A'), ' ', IsAlpha('1'));
                end.
                """));
            var exe = Path.Combine(dir, "out", "P.exe");
            var commands = new List<string>();
            var options = new CompilationOptions
            {
                MainFile = Path.Combine(dir, "P.pas"),
                RtlDirectory = MiniRtlCompilesTests.MiniRtlDirectory,
                ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
                ObjectDirectory = Path.Combine(dir, "obj"),
            };

            var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { OutputPath = exe, CommandLineSink = commands.Add }, ct);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            Assert.Contains(commands, c => c.Contains("-d " + Path.Combine(dir, "defs", "user32.def") + " -D user32.dll", StringComparison.Ordinal));
            var rsp = File.ReadAllLines(Path.Combine(dir, "obj", "link.rsp"));
            Assert.Contains(Path.Combine(dir, "native", "triple.obj"), rsp);
            Assert.Contains(Path.Combine(dir, "obj", "imports", "user32.lib"), rsp);
            var stdout = new List<string>();
            Assert.Equal(0, await new ProcessRunner().RunAsync(exe, [], dir, stdout.Add, _ => { }, ct));
            Assert.Equal(["42", "TRUE FALSE"], stdout);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
