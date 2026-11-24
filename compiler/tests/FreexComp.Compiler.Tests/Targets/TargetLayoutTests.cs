using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Targets;
using static FreexComp.Compiler.Tests.Targets.TargetTestSupport;

namespace FreexComp.Compiler.Tests.Targets;

/// <summary>targets.md CMP-1007…1009, CMP-1032 (task C-279): sizes, alignments and the run-time layouts follow the compilation's target.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetLayoutTests
{
    private const string Probe = """
        program Probe;
        var
          p: Pointer;
          n: NativeInt;
          r: record a: Byte; p: Pointer end;
        begin
          {$IFDEF CPU32BITS}
          n := 32;
          {$ELSE}
          n := 64;
          {$ENDIF}
          Writeln(SizeOf(Pointer), SizeOf(r));
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-1007")]
    [Trait("Req", "CMP-1032")]
    public void FakeTarget32_SizeOfPointerIs4InIr()
    {
        var ll = EmitLl(Fake32, "Probe", Probe)["Probe"];

        Assert.Contains("target triple = \"i686-pc-windows-msvc\"\n", ll, StringComparison.Ordinal);
        Assert.DoesNotContain("target datalayout", ll, StringComparison.Ordinal);                  // CMP-1006
        // n is i32 and its storage is 4 bytes; p is an address aligned to 4
        Assert.Contains("@\"Probe.n\" = internal global i32 zeroinitializer, align 4\n", ll, StringComparison.Ordinal);
        Assert.Contains("@\"Probe.p\" = internal global ptr zeroinitializer, align 4\n", ll, StringComparison.Ordinal);
        // the record is the plain struct { i8, ptr }: the back end puts p at offset 4, size 8, under 4-byte pointers
        Assert.Contains("@\"Probe.r\" = internal global { i8, ptr } zeroinitializer, align 4\n", ll, StringComparison.Ordinal);
        // {$IFDEF CPU32BITS} was active
        Assert.Contains("store i32 32, ptr @\"Probe.n\", align 4\n", ll, StringComparison.Ordinal);
        Assert.DoesNotContain("store i32 64", ll, StringComparison.Ordinal);
        // the folded constants SizeOf(Pointer) = 4 and SizeOf(r) = 8 are the arguments of the two integer writes, in order
        Assert.Equal(["i64 4", "i64 8"], WrittenIntegers(ll));

        // the same source for the baseline target: 8-byte pointers, 64-bit NativeInt, SizeOf(r) = 16
        var baseline = EmitLl(TargetInfo.Win64, "Probe", Probe)["Probe"];
        Assert.Contains("target triple = \"x86_64-pc-windows-msvc\"\n", baseline, StringComparison.Ordinal);
        Assert.Contains("@\"Probe.n\" = internal global i64 zeroinitializer, align 8\n", baseline, StringComparison.Ordinal);
        Assert.Contains("store i64 64, ptr @\"Probe.n\", align 8\n", baseline, StringComparison.Ordinal);
        Assert.Equal(["i64 8", "i64 16"], WrittenIntegers(baseline));
    }

    private const string Abi = """
        program Abi;
        type
          TBase = class
            procedure First; virtual;
            procedure Second; virtual;
          end;
          TInts = array of Integer;
        procedure TBase.First; begin end;
        procedure TBase.Second; begin end;
        procedure Show(const Args: array of const); begin end;
        var
          o: TBase;
          a: TInts;
          m: procedure of object;
          len: Integer;
        begin
          o.Second;
          len := Length(a);
          Show([1, o]);
          Writeln(SizeOf(TVarRec), SizeOf(m), SizeOf(o), SizeOf(a));
        end.
        """;

    [Fact]
    [Trait("Req", "CMP-1007")]
    public void FakeTarget32_AbiLayoutsScaleWithPointerSize()
    {
        var fake = EmitLl(Fake32, "Abi", Abi)["Abi"];
        var win64 = EmitLl(TargetInfo.Win64, "Abi", Abi)["Abi"];

        // VMT (rtl-abi.md §7.2): the header is { ptr, NativeInt, 6 x ptr } — 32 bytes under 4-byte pointers, 64 under 8 — then one
        // address per virtual slot; InstanceSize is a NativeInt
        const string VmtHead = "@\"Abi.TBase.$vmt\" = internal constant ";
        Assert.Contains(VmtHead + "{ ptr, i32, ptr, ptr, ptr, ptr, ptr, ptr, [2 x ptr] } { ptr @\"System.TObject.$vmt\", i32 8, ptr ", fake, StringComparison.Ordinal);
        Assert.Contains(VmtHead + "{ ptr, i64, ptr, ptr, ptr, ptr, ptr, ptr, [2 x ptr] } { ptr @\"System.TObject.$vmt\", i64 8, ptr ", win64, StringComparison.Ordinal);
        Assert.EndsWith(", align 4", LineStarting(fake, VmtHead), StringComparison.Ordinal);
        Assert.EndsWith(", align 8", LineStarting(win64, VmtHead), StringComparison.Ordinal);
        // the call of virtual slot 1: header size + one address
        Assert.Contains("getelementptr inbounds i8, ptr %t2, i64 36\n", fake, StringComparison.Ordinal);
        Assert.Contains("getelementptr inbounds i8, ptr %t2, i64 72\n", win64, StringComparison.Ordinal);
        Assert.DoesNotContain("i64 72", fake, StringComparison.Ordinal);

        // unit table entry (CMP-355): { ptr $init, ptr $final }, aligned like an address; the count passed to _Startup is a NativeInt
        const string UnitsHead = "@\"$units\" = internal constant [2 x { ptr, ptr }] ";
        Assert.EndsWith(", align 4", LineStarting(fake, UnitsHead), StringComparison.Ordinal);
        Assert.EndsWith(", align 8", LineStarting(win64, UnitsHead), StringComparison.Ordinal);
        Assert.Contains("declare void @\"System._Startup\"(ptr, i32)\n", fake, StringComparison.Ordinal);
        Assert.Contains("declare void @\"System._Startup\"(ptr, i64)\n", win64, StringComparison.Ordinal);

        // dynamic-array header (rtl-abi.md §4): Length is the NativeInt in front of element 0
        Assert.Contains("  %t8 = getelementptr inbounds i8, ptr %t6, i64 -4\n  %t9 = load i32, ptr %t8, align 4\n", fake, StringComparison.Ordinal);
        Assert.Contains("  %t8 = getelementptr inbounds i8, ptr %t6, i64 -8\n  %t9 = load i64, ptr %t8, align 8\n", win64, StringComparison.Ordinal);
        // the dynamic array's type information stores the element size as a NativeInt
        Assert.Contains("@\"Abi.TInts.$rtti\" = linkonce_odr constant { i8, [7 x i8], ptr, i32, ptr, ptr }", fake, StringComparison.Ordinal);
        Assert.Contains("@\"Abi.TInts.$rtti\" = linkonce_odr constant { i8, [7 x i8], ptr, i64, ptr, ptr }", win64, StringComparison.Ordinal);

        // array of const: a TVarRec is 16 bytes on both targets (tag, then an 8-byte payload at offset 8); the address stored in
        // the payload is aligned like an address
        Assert.Contains("alloca [2 x [16 x i8]], align 8\n", fake, StringComparison.Ordinal);
        Assert.Matches(new Regex("store ptr %t[0-9]+, ptr %t[0-9]+, align 4\n  call void @\"Abi[.]Show\""), fake);
        Assert.Matches(new Regex("store ptr %t[0-9]+, ptr %t[0-9]+, align 8\n  call void @\"Abi[.]Show\""), win64);

        // TVarRec, a method pointer, an object reference and a dynamic-array reference: 16, 8, 4, 4 against 16, 16, 8, 8
        Assert.Equal(["i64 16", "i64 8", "i64 4", "i64 4"], WrittenIntegers(fake));
        Assert.Equal(["i64 16", "i64 16", "i64 8", "i64 8"], WrittenIntegers(win64));
    }

    private static string LineStarting(string ll, string head) => Lines(ll).Single(l => l.StartsWith(head, StringComparison.Ordinal));

    /// <summary>The <c>i64</c> arguments of the integer <c>Write</c> helper calls, in order.</summary>
    private static string[] WrittenIntegers(string ll) =>
        [.. Lines(ll).Where(l => l.Contains("call void @\"System._WriteInt\"", StringComparison.Ordinal)).Select(l => Regex.Match(l, "i64 -?[0-9]+").Value)];

    [Fact]
    [Trait("Req", "CMP-1009")]
    public void Sidecar_PointerSizeFromTarget()
    {
        static (JsonObject Abi, string Ll) Debug(TargetInfo target)
        {
            var fs = new InMemoryFileSystem().Add(Path.Combine(Rtl, "System.pas"), StubSystem).Add(@"C:\p\Abi.pas", Abi);
            var options = new CompilationOptions { MainFile = @"C:\p\Abi.pas", RtlDirectory = Rtl, ObjectDirectory = Obj, Target = target, DebugInfo = true };
            var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
            return (JsonNode.Parse(fs.ReadAllText(Path.Combine(Obj, "Abi.pdi.json")))!["abi"]!.AsObject(), fs.ReadAllText(Path.Combine(Obj, "Abi.ll")));
        }

        var (fake, fakeLl) = Debug(Fake32);
        Assert.Equal(4, fake["pointerSize"]!.GetValue<int>());
        Assert.Equal(-4, fake["dynArray"]!["lengthOffset"]!.GetValue<long>());
        Assert.Equal(4, fake["dynArray"]!["lengthSize"]!.GetValue<int>());
        Assert.Equal(8, fake["vmt"]!["classNameOffset"]!.GetValue<long>());
        Assert.Equal(0, fake["vmt"]!["parentOffset"]!.GetValue<long>());

        var (win64, win64Ll) = Debug(TargetInfo.Win64);
        Assert.Equal(8, win64["pointerSize"]!.GetValue<int>());
        Assert.Equal(-8, win64["dynArray"]!["lengthOffset"]!.GetValue<long>());
        Assert.Equal(8, win64["dynArray"]!["lengthSize"]!.GetValue<int>());
        Assert.Equal(16, win64["vmt"]!["classNameOffset"]!.GetValue<long>());
        Assert.Equal(0, win64["vmt"]!["parentOffset"]!.GetValue<long>());

        // pointer-sized debug metadata (CMP-1009)
        static string[] PointerTypes(string ll) => [.. Lines(ll).Where(l => l.Contains("DW_TAG_pointer_type", StringComparison.Ordinal))];
        Assert.NotEmpty(PointerTypes(fakeLl));
        Assert.All(PointerTypes(fakeLl), l => Assert.EndsWith("size: 32)", l, StringComparison.Ordinal));
        Assert.All(PointerTypes(win64Ll), l => Assert.EndsWith("size: 64)", l, StringComparison.Ordinal));
    }
}
