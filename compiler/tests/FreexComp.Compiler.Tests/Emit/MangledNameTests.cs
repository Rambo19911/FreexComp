using System.Text;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-367 (CMP-361): a named pointer type is mangled with the unit (or type) that declares it, so overloads that differ only in two
/// units' pointers of one name get two link names and each call reaches its own routine — in the first build and when the units
/// are read back from their <c>.pcu</c>.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-361")]
public sealed class MangledNameTests
{
    private const string U1 = "unit U1;\ninterface\ntype\n  TFoo = record\n    A: Integer;\n  end;\n  PFoo = ^TFoo;\nimplementation\nend.\n";

    private const string U2 = "unit U2;\ninterface\ntype\n  TFoo = record\n    B: Int64;\n    C: Integer;\n  end;\n  PFoo = ^TFoo;\nimplementation\nend.\n";

    private const string U3 = """
        unit U3;
        interface
        uses U1, U2;
        procedure Show(X: U1.PFoo); overload;
        procedure Show(X: U2.PFoo); overload;
        implementation
        procedure Show(X: U1.PFoo);
        begin
          WriteLn('U1 ', X^.A);
        end;
        procedure Show(X: U2.PFoo);
        begin
          WriteLn('U2 ', X^.C);
        end;
        end.

        """;

    private const string Program = """
        program Q;
        uses U1, U2, U3;
        var
          A: U1.PFoo;
          B: U2.PFoo;
        begin
          New(A);
          A^.A := 7;
          New(B);
          B^.B := 0;
          B^.C := 9;
          Show(A);
          Show(B);
          Dispose(A);
          Dispose(B);
        end.

        """;

    [Fact]
    [Trait("Category", "Toolchain")]
    public async Task SameNamedPointersOfTwoUnits_MangleApart()
    {
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Put(dir, "U1", U1);
            Put(dir, "U2", U2);
            Put(dir, "U3", U3);
            Put(dir, "Q", Program);

            var first = Build(dir, "Q");
            var ir = File.ReadAllText(Path.Combine(dir, "obj", "U3.ll"));
            Assert.Contains("define void @\"U3.Show$U1.PFoo\"(", ir, StringComparison.Ordinal);
            Assert.Contains("define void @\"U3.Show$U2.PFoo\"(", ir, StringComparison.Ordinal);
            Assert.Equal(["U1 7", "U2 9"], await Run(first));

            // the units read back from their .pcu alone: the reader's recomputed link names agree with the recorded ones
            var lib = PrecompiledOnly(dir, "U1", "U2", "U3");
            Put(dir, "Q", Program.ReplaceLineEndings("\n").Replace("Show(A);\n  Show(B);", "Show(B);\n  Show(A);", StringComparison.Ordinal));
            var second = Build(dir, "Q", lib);
            Assert.Equal(["Q"], second.CompiledUnits);
            Assert.Equal(["U2 9", "U1 7"], await Run(second));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-367: a pointer declared in a class is mangled with its class (<c>U4.TBox.PItem</c>), and keeps that name when the class is
    /// read back from the <c>.pcu</c> (the reader checks each method's recorded link name).
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    public async Task PointerNestedInAClass_KeepsItsLinkNameThroughThePcu()
    {
        const string U4 = """
            unit U4;
            interface
            type
              TBox = class
              public
                type
                  PItem = ^TItem;
                  TItem = record
                    V: Integer;
                  end;
                procedure Put(P: PItem); overload;
                procedure Put(N: Integer); overload;
              end;
            implementation
            procedure TBox.Put(P: PItem);
            begin
              WriteLn('item ', P^.V);
            end;
            procedure TBox.Put(N: Integer);
            begin
              WriteLn('int ', N);
            end;
            end.

            """;
        const string Main = """
            program Q;
            uses U4;
            var
              Box: TBox;
              Item: TBox.TItem;
            begin
              Box := TBox.Create;
              Item.V := 5;
              Box.Put(@Item);
              Box.Put(3);
              Box.Free;
            end.

            """;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Put(dir, "U4", U4);
            Put(dir, "Q", Main);

            var first = Build(dir, "Q");
            Assert.Contains("define void @\"U4.TBox.Put$U4.TBox.PItem\"(", File.ReadAllText(Path.Combine(dir, "obj", "U4.ll")), StringComparison.Ordinal);
            Assert.Equal(["item 5", "int 3"], await Run(first));

            var lib = PrecompiledOnly(dir, "U4");
            Put(dir, "Q", Main.Replace("Item.V := 5;", "Item.V := 6;", StringComparison.Ordinal));
            var second = Build(dir, "Q", lib);
            Assert.Equal(["Q"], second.CompiledUnits);
            Assert.Equal(["item 6", "int 3"], await Run(second));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>
    /// C-368 (CMP-361, CMP-362): a type local to an overloaded routine is qualified by the routine's link name, so the types
    /// <c>TLocal</c> of <c>P(Integer)</c> and <c>P(string)</c> give two generic instances and two type infos (and two
    /// <c>.pdi</c> routine entries for the instances' methods) instead of one shared <c>Q.P.TLocal</c>; each overload sees its own.
    /// </summary>
    [Fact]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-362")]
    public async Task LocalTypesOfTwoOverloads_MangleApart()
    {
        const string Main = """
            program Q;
            uses TypInfo;
            type
              TBox<T> = class
                class function Size: Integer;
              end;
            class function TBox<T>.Size: Integer;
            begin
              Result := SizeOf(T);
            end;
            procedure P(A: Integer); overload;
            type
              TLocal = record
                X: Integer;
              end;
            begin
              WriteLn('int ', A, ' ', TBox<TLocal>.Size, ' ', PTypeInfo(TypeInfo(TLocal))^.Name, ' ', PInteger(NativeInt(TypeInfo(TLocal)) + 16)^);
            end;
            procedure P(A: string); overload;
            type
              TLocal = record
                X, Y, Z: Int64;
              end;
            begin
              WriteLn('str ', A, ' ', TBox<TLocal>.Size, ' ', PTypeInfo(TypeInfo(TLocal))^.Name, ' ', PInteger(NativeInt(TypeInfo(TLocal)) + 16)^);
            end;
            begin
              P(1);
              P('s');
            end.

            """;
        var dir = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Put(dir, "Q", Main);
            var result = Build(dir, "Q", debugInfo: true);
            var ir = File.ReadAllText(Path.Combine(dir, "obj", "Q.ll"));
            Assert.Contains("@\"Q.TBox<Q.P$Integer.TLocal>.Size\"(", ir, StringComparison.Ordinal);
            Assert.Contains("@\"Q.TBox<Q.P$string.TLocal>.Size\"(", ir, StringComparison.Ordinal);
            Assert.Contains("@\"Q.P$Integer.TLocal.$rtti\" =", ir, StringComparison.Ordinal);
            Assert.Contains("@\"Q.P$string.TLocal.$rtti\" =", ir, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Q.P.TLocal", ir, StringComparison.Ordinal);
            var pdi = File.ReadAllText(Path.ChangeExtension(result.OutputPath!, ".pdi"));
            Assert.Contains("\"Q.TBox<Q.P$Integer.TLocal>.Size\"", pdi, StringComparison.Ordinal);
            Assert.Contains("\"Q.TBox<Q.P$string.TLocal>.Size\"", pdi, StringComparison.Ordinal);
            Assert.Equal(["int 1 4 TLocal 4", "str s 24 TLocal 24"], await Run(result));
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    private static void Put(string dir, string name, string text) =>
        PhysicalFileSystem.Instance.WriteAllBytes(Path.Combine(dir, name + ".pas"), Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")));

    /// <summary>Moves the units' <c>.pcu</c> and <c>.obj</c> into <c>lib</c> and deletes their sources, so a build reads them from the <c>.pcu</c>.</summary>
    private static string PrecompiledOnly(string dir, params string[] units)
    {
        var lib = Path.Combine(dir, "lib");
        Directory.CreateDirectory(lib);
        foreach (var unit in units)
        {
            foreach (var extension in new[] { ".pcu", ".obj" })
            {
                File.Move(Path.Combine(dir, "obj", unit + extension), Path.Combine(lib, unit + extension));
            }

            File.Delete(Path.Combine(dir, unit + ".pas"));
        }

        return lib;
    }

    private static EmitResult Build(string dir, string program, string? lib = null, bool debugInfo = false)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(dir, program + ".pas"),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = Path.Combine(dir, "obj"),
            SearchPaths = lib is null ? [] : [lib],
            DebugInfo = debugInfo,
        };
        var result = Compilation.Create(options, [], PhysicalFileSystem.Instance)
            .Emit(new EmitOptions { OutputPath = Path.Combine(dir, program + ".exe") }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return result;
    }

    private static async Task<List<string>> Run(EmitResult result)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        var exitCode = await NativeRun.RunAsync(result.OutputPath!, Path.GetDirectoryName(result.OutputPath!)!, stdout.Add, stderr.Add);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {string.Join("\n", stderr)}");
        return stdout;
    }
}
