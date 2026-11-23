using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-153 end to end (CMP-425, debug-info-sidecar.md §1, DBG-147): a program built with <c>-g</c> against the real RTL writes a
/// fragment per unit next to its <c>.ll</c> and the merged <c>&lt;output&gt;.pdi</c> at link; two builds give the same bytes;
/// without <c>-g</c> there is neither; the sidecar's sizes and offsets are the ones the running code sees (DBG-144).
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class SidecarBuildTests
{
    private const string DemoProgram = """
        program DbgDemo;

        type
          TPoint = record
            X, Y: Integer;
          end;
          TAnimal = class
            Name: string;
            Legs: Integer;
          end;

        var
          Pet: TAnimal;

        function Twice(X: Integer): Integer;
        begin
          Result := X * 2;
        end;

        procedure Show(const S: string; var P: TPoint);
        begin
          WriteLn(S, ' ', Twice(P.X), ' ', P.Y);
        end;

        var
          A: TPoint;
        begin
          A.X := 18;
          A.Y := 7;
          Pet := TAnimal.Create;
          Show('value', A);
          Pet.Free;
        end.
        """;

    /// <summary>Prints <c>SizeOf</c>, instance sizes and field offsets as the code computes them (<c>Name=Value</c> lines).</summary>
    private const string LayoutProgram = """
        program DbgLayout;

        type
          TInner = record
            B: Byte;
            D: Double;
          end;
          TRec = record
            A: Byte;
            Inner: TInner;
            W: Word;
            S: string;
            C: Char;
          end;
          TPacked = packed record
            A: Byte;
            I: Integer;
            W: Word;
          end;
          TBase = class
            F1: Byte;
          end;
          TDerived = class(TBase)
            F2: Int64;
            F3: Word;
          end;
          TGrid = array[1..3, 0..4] of Word;
          TShort = string[10];

        var
          R: TRec;
          P: TPacked;
          O: TDerived;
          G: TGrid;
          T: TShort;

        procedure Show(const Name: string; Value: NativeInt);
        begin
          WriteLn(Name, '=', Value);
        end;

        begin
          O := TDerived.Create;
          Show('TInner', SizeOf(TInner));
          Show('TInner.B', NativeInt(@R.Inner.B) - NativeInt(@R.Inner));
          Show('TInner.D', NativeInt(@R.Inner.D) - NativeInt(@R.Inner));
          Show('TRec', SizeOf(TRec));
          Show('TRec.A', NativeInt(@R.A) - NativeInt(@R));
          Show('TRec.Inner', NativeInt(@R.Inner) - NativeInt(@R));
          Show('TRec.W', NativeInt(@R.W) - NativeInt(@R));
          Show('TRec.S', NativeInt(@R.S) - NativeInt(@R));
          Show('TRec.C', NativeInt(@R.C) - NativeInt(@R));
          Show('TPacked', SizeOf(TPacked));
          Show('TPacked.A', NativeInt(@P.A) - NativeInt(@P));
          Show('TPacked.I', NativeInt(@P.I) - NativeInt(@P));
          Show('TPacked.W', NativeInt(@P.W) - NativeInt(@P));
          Show('TDerived', TDerived.InstanceSize);
          Show('TBase', TBase.InstanceSize);
          Show('TBase.F1', NativeInt(@O.F1) - NativeInt(Pointer(O)));
          Show('TDerived.F2', NativeInt(@O.F2) - NativeInt(Pointer(O)));
          Show('TDerived.F3', NativeInt(@O.F3) - NativeInt(Pointer(O)));
          Show('TGrid', SizeOf(TGrid));
          Show('TShort', SizeOf(TShort));
          O.Free;
        end.
        """;

    private sealed record Build(string Directory, EmitResult Result, string Exe)
    {
        public string Obj(string name) => Path.Combine(Directory, "obj", name);

        public string Pdi => Path.ChangeExtension(Exe, ".pdi");
    }

    private static Build Compile(string directory, string name, string source, bool debugInfo, bool force = false)
    {
        Assert.True(File.Exists(Clang.Path), $"Pinned toolchain missing: {Clang.Path} (run scripts/bootstrap.ps1).");
        var fs = PhysicalFileSystem.Instance;
        var main = Path.Combine(directory, name + ".pas");
        fs.WriteAllBytes(main, Encoding.UTF8.GetBytes(source));
        var exe = Path.Combine(directory, "out", name + ".exe");
        var options = new CompilationOptions
        {
            MainFile = PathUtil.Normalize(main),
            RtlDirectory = TestRtl.Directory,
            ToolchainDirectory = Path.GetDirectoryName(Clang.Path)!,
            ObjectDirectory = Path.Combine(directory, "obj"),
            DebugInfo = debugInfo,
        };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { OutputPath = exe, ForceRebuild = force }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return new Build(directory, result, exe);
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// CMP-425: fragments next to the <c>.ll</c>s, the merged sidecar next to the exe covering the program and the RTL units;
    /// a forced rebuild and an up-to-date rebuild give byte-identical sidecars (DBG-106); a later build without <c>-g</c>
    /// removes them (DBG-147).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-425")]
    [Trait("Req", "DBG-106")]
    [Trait("Req", "DBG-147")]
    public void Sidecar_Deterministic_WrittenAtLinkAndRemovedWithoutDebugInfo()
    {
        var dir = NewDirectory();
        try
        {
            var first = Compile(dir, "DbgDemo", DemoProgram, debugInfo: true);
            var bytes = File.ReadAllBytes(first.Pdi);
            var fragments = new[] { "DbgDemo", "System" }.Select(u => File.Exists(first.Obj(u + ".pdi.json"))).ToArray();
            var forced = File.ReadAllBytes(Compile(dir, "DbgDemo", DemoProgram, debugInfo: true, force: true).Pdi);
            var upToDate = Compile(dir, "DbgDemo", DemoProgram, debugInfo: true);
            var cached = File.ReadAllBytes(upToDate.Pdi);
            var plain = Compile(dir, "DbgDemo", DemoProgram, debugInfo: false);
            var leftOver = File.Exists(plain.Pdi) || Directory.EnumerateFiles(Path.Combine(dir, "obj"), "*.pdi.json").Any();

            Assert.Equal([true, true], fragments);
            Assert.NotEmpty(upToDate.Result.UpToDateUnits);
            Assert.Equal(bytes, forced);
            Assert.Equal(bytes, cached);
            Assert.False(leftOver, "a build without -g left a sidecar or a fragment");
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "BOM");
            Assert.DoesNotContain((byte)'\r', bytes);
            var pdi = JsonNode.Parse(bytes)!.AsObject();
            SidecarTests.ValidateSchema(pdi);
            Assert.Equal("DbgDemo", pdi["program"]!.GetValue<string>());
            var units = pdi["units"]!.AsArray().Select(u => u!["name"]!.GetValue<string>()).ToList();
            Assert.Equal("DbgDemo", units[0]);
            Assert.Contains("System", units);
            var demo = pdi["units"]![0]!;
            Assert.Equal(Path.Combine(dir, "DbgDemo.pas"), demo["file"]!.GetValue<string>(), ignoreCase: true);
            Assert.Equal(["A", "Pet"], demo["globals"]!.AsArray().Select(g => g!["name"]!.GetValue<string>()));
            Assert.Contains(pdi["units"]!.AsArray().Single(u => u!["name"]!.GetValue<string>() == "System")!["routines"]!.AsArray(),
                r => r!["linkageName"]!.GetValue<string>() == "System._Raise");
            var types = pdi["types"]!.AsArray();
            Assert.Single(types, t => t!["name"]!.GetValue<string>() == "Integer");
            Assert.Single(types, t => t!["name"]!.GetValue<string>() == "TObject" && t["kind"]!.GetValue<string>() == "class");
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }

    /// <summary>DBG-144: the sidecar's sizes, instance sizes and field offsets equal <c>SizeOf</c> and the field addresses of the running code.</summary>
    [Fact]
    [Trait("Req", "DBG-144")]
    public async Task DebugInfoLayoutMatches_SizeOfAndFieldOffsets()
    {
        var dir = NewDirectory();
        try
        {
            var build = Compile(dir, "DbgLayout", LayoutProgram, debugInfo: true);
            var stdout = new List<string>();
            var stderr = new List<string>();
            var exitCode = await FreexComp.Tests.Shared.NativeRun.RunAsync(build.Exe, dir, stdout.Add, stderr.Add);
            var pdi = JsonNode.Parse(File.ReadAllBytes(build.Pdi))!.AsObject();

            Assert.Equal(0, exitCode);
            Assert.Empty(stderr);
            var measured = stdout.Select(l => l.Split('=')).ToDictionary(p => p[0], p => long.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(20, measured.Count);
            // the program's types (a short string type carries no unit: the binder records none for it)
            var types = pdi["types"]!.AsArray().Select(t => t!.AsObject()).Where(t => t["unit"]?.GetValue<string>() is "DbgLayout" or null)
                .Where(t => t["name"]!.GetValue<string>().Length > 0).ToDictionary(t => t["name"]!.GetValue<string>());
            foreach (var (key, value) in measured)
            {
                var parts = key.Split('.');
                var type = types[parts[0]];
                if (parts.Length == 1)
                {
                    var expected = (type["kind"]!.GetValue<string>() == "class" ? type["instanceSize"] : type["size"])!.GetValue<long>();
                    Assert.True(value == expected, $"{key}: code {value}, sidecar {type.ToJsonString()}");
                }
                else
                {
                    var field = type["fields"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == parts[1])!;
                    Assert.True(value == field["offset"]!.GetValue<long>(), $"{key}: code {value}, sidecar {field.ToJsonString()}");
                }
            }
        }
        finally
        {
            TestDirectories.Delete(dir);
        }
    }
}
