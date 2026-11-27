using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-378 (generics.md LANG-790 b, CMP-1397): the managed class vars of a generic instance are released once per image.</summary>
[Trait("Phase", "8")]
public sealed class ClassVarReleaseTests
{
    private const string UProbe = """
        unit UProbe;
        interface
        implementation
        var
          Before: NativeInt;
        initialization
          Before := _LiveAllocCount;
        finalization
          Writeln('leaked ', _LiveAllocCount - Before);
        end.
        """;

    private const string UGen = """
        unit UGen;
        interface
        type
          TBox<T> = class
            class var Name: string;
            class var Items: array of T;
          end;
        implementation
        end.
        """;

    private const string UUse = """
        unit UUse;
        interface
        uses UGen;
        procedure Fill;
        implementation
        procedure Fill;
        begin
          TBox<string>.Name := Copy('strings!', 1, 7);
          SetLength(TBox<string>.Items, 2);
          TBox<string>.Items[0] := Copy('one!', 1, 3);
        end;
        initialization
        finalization
          Writeln('use final ', TBox<string>.Name, ' ', Length(TBox<string>.Items));
        end.
        """;

    private const string FirstProgram = """
        program P;
        uses UProbe, UGen;
        begin
          Writeln('first');
        end.
        """;

    private const string Program = """
        program P;
        uses UProbe, UGen, UUse;
        begin
          Fill;
          TBox<Integer>.Name := Copy('integers!', 1, 8);
          SetLength(TBox<Integer>.Items, 3);
          Writeln(TBox<string>.Name, ' ', TBox<Integer>.Name);
        end.
        """;

    private static readonly string[] Expected = ["strings integers", "use final strings 2", "leaked 0"];

    /// <summary>
    /// C-378: <c>UGen</c> is compiled (and its <c>.pcu</c> and object written) by a build that uses no instance of <c>TBox</c>; a later
    /// build takes <c>UGen</c> and <c>UProbe</c> from those files and adds <c>UUse</c>, which uses <c>TBox&lt;string&gt;</c>, and the
    /// program, which uses <c>TBox&lt;Integer&gt;</c>. The managed class vars of both instances are released (no leak at the
    /// probe's finalization, the last one), and <c>UUse</c>'s finalization still sees them — as in a build from sources.
    /// </summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1397")]
    public async Task InstanceAddedByALaterUnit_ReleasedWhenTheGenericUnitComesFromPcu()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var ct = TestContext.Current.CancellationToken;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var fs = PhysicalFileSystem.Instance;
        try
        {
            void Put(string relative, string text) => fs.WriteAllBytes(Path.Combine(dir, relative), Encoding.UTF8.GetBytes(text));
            Put(@"first\P.pas", FirstProgram);
            Put(@"first\UProbe.pas", UProbe);
            Put(@"first\UGen.pas", UGen);
            Put(@"src\P.pas", Program);
            Put(@"src\UProbe.pas", UProbe);
            Put(@"src\UGen.pas", UGen);
            Put(@"src\UUse.pas", UUse);
            Put(@"app\P.pas", Program);
            Put(@"app\UUse.pas", UUse);
            CompilationOptions Options(string folder) => new()
            {
                MainFile = Path.Combine(dir, folder, "P.pas"),
                RtlDirectory = TestRtl.Directory,
                ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
                ObjectDirectory = Path.Combine(dir, folder, "obj"),
            };

            var first = Compilation.Create(Options("first"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "first", "P.exe") }, ct);
            Assert.True(first.Success, string.Join("\n", first.Diagnostics.Select(d => d.ToText())));
            foreach (var unit in new[] { "UProbe", "UGen" })
            {
                File.Copy(Path.Combine(dir, "first", "obj", unit + ".pcu"), Path.Combine(dir, "app", unit + ".pcu"));
                File.Copy(Path.Combine(dir, "first", "obj", unit + ".obj"), Path.Combine(dir, "app", unit + ".obj"));
            }

            var fromSources = Compilation.Create(Options("src"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "src", "P.exe") }, ct);
            Assert.True(fromSources.Success, string.Join("\n", fromSources.Diagnostics.Select(d => d.ToText())));
            var incremental = Compilation.Create(Options("app"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "app", "P.exe") }, ct);
            Assert.True(incremental.Success, string.Join("\n", incremental.Diagnostics.Select(d => d.ToText())));
            Assert.Equal(["P", "System", "UUse"], incremental.CompiledUnits.Order(StringComparer.Ordinal));
            foreach (var exe in new[] { @"src\P.exe", @"app\P.exe" })
            {
                var stdout = new List<string>();
                Assert.Equal(0, await new ProcessRunner().RunAsync(Path.Combine(dir, exe), [], dir, stdout.Add, _ => { }, ct));
                Assert.Equal($"{string.Join(" | ", Expected)} ({exe})", $"{string.Join(" | ", stdout)} ({exe})");
            }
        }
        finally
        {
            try
            {
                TestDirectories.Delete(dir);
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
