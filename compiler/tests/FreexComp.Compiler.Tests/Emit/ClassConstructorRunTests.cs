using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-375 (generics.md §14.3, CMP-1397): class constructors of generic instances run once per image, also across separately compiled units.</summary>
[Trait("Phase", "8")]
public sealed class ClassConstructorRunTests
{
    private const string UReg = """
        unit UReg;
        interface
        type
          TRegistry<T> = class
            class var Count: Integer;
            class constructor Create;
            class destructor Destroy;
          end;
        var
          Runs: Integer;
        implementation
        class constructor TRegistry<T>.Create;
        begin
          Inc(Runs);
          Count := Count + 1;
          Writeln('cctor ', SizeOf(T));
        end;
        class destructor TRegistry<T>.Destroy;
        begin
          Writeln('cdtor ', SizeOf(T));
        end;
        end.
        """;

    private const string URegA = """
        unit URegA;
        interface
        uses UReg;
        function CountA: Integer;
        implementation
        function CountA: Integer;
        begin
          Result := TRegistry<Integer>.Count;
        end;
        initialization
          Writeln('A init');
        finalization
          Writeln('A final');
        end.
        """;

    private const string URegB = """
        unit URegB;
        interface
        uses UReg;
        function CountB: Integer;
        implementation
        function CountB: Integer;
        begin
          Result := TRegistry<Integer>.Count;
        end;
        initialization
          Writeln('B init');
        finalization
          Writeln('B final');
        end.
        """;

    private const string Program = """
        program P;
        uses UReg, URegA, URegB;
        begin
          Writeln(Runs, ' ', CountA, ' ', CountB, ' ', TRegistry<Integer>.Count);
        end.
        """;

    /// <summary>The class constructor runs before <c>URegA</c>'s initialization, once; the class destructor after its finalization.</summary>
    private static readonly string[] Expected = ["cctor 4", "A init", "B init", "1 1 1 1", "B final", "A final", "cdtor 4"];

    /// <summary>
    /// LANG-790 a, CMP-1397: two units and the program define <c>TRegistry&lt;Integer&gt;</c>; built from sources and then again with
    /// the units taken from their <c>.pcu</c> and objects (only the program compiled), the class constructor runs once, in the first
    /// unit in initialization order, and the class destructor once, in that unit's finalization.
    /// </summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1397")]
    public async Task InstanceRunsOnce_UnitsFromPcu()
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var ct = TestContext.Current.CancellationToken;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        var fs = PhysicalFileSystem.Instance;
        try
        {
            void Put(string relative, string text) => fs.WriteAllBytes(Path.Combine(dir, relative), Encoding.UTF8.GetBytes(text));
            Put(@"src\P.pas", Program);
            Put(@"src\UReg.pas", UReg);
            Put(@"src\URegA.pas", URegA);
            Put(@"src\URegB.pas", URegB);
            Put(@"app\P.pas", Program);
            CompilationOptions Options(string folder) => new()
            {
                MainFile = Path.Combine(dir, folder, "P.pas"),
                RtlDirectory = TestRtl.Directory,
                ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
                ObjectDirectory = Path.Combine(dir, folder, "obj"),
            };

            var fromSources = Compilation.Create(Options("src"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "src", "P.exe") }, ct);
            Assert.True(fromSources.Success, string.Join("\n", fromSources.Diagnostics.Select(d => d.ToText())));
            foreach (var unit in new[] { "UReg", "URegA", "URegB" })
            {
                File.Copy(Path.Combine(dir, "src", "obj", unit + ".pcu"), Path.Combine(dir, "app", unit + ".pcu"));
                File.Copy(Path.Combine(dir, "src", "obj", unit + ".obj"), Path.Combine(dir, "app", unit + ".obj"));
            }

            var fromPcus = Compilation.Create(Options("app"), [], fs).Emit(new EmitOptions { OutputPath = Path.Combine(dir, "app", "P.exe") }, ct);

            Assert.True(fromPcus.Success, string.Join("\n", fromPcus.Diagnostics.Select(d => d.ToText())));
            Assert.Equal(["P", "System"], fromPcus.CompiledUnits);
            foreach (var exe in new[] { @"src\P.exe", @"app\P.exe" })
            {
                var stdout = new List<string>();
                Assert.Equal(0, await new ProcessRunner().RunAsync(Path.Combine(dir, exe), [], dir, stdout.Add, _ => { }, ct));
                Assert.Equal(Expected, stdout);
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
