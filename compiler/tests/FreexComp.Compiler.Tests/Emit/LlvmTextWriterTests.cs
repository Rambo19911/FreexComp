using System.Collections.Immutable;
using System.Diagnostics;
using FreexComp.Compiler.Emit;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>Text produced by <see cref="LlvmTextWriter"/> for the IR shapes of codegen.md §1–§4 (CMP-125, 300…323).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Snapshot")]
public sealed class LlvmTextWriterTests
{
    private const string Header = "; ModuleID = 'M'\nsource_filename = \"M.pas\"\ntarget triple = \"x86_64-pc-windows-msvc\"\n";

    private static readonly ImmutableArray<IrAttribute> PascalRoutine = [IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")];
    private static readonly ImmutableArray<IrAttribute> NoReturn = [IrAttribute.Enum("noreturn"), IrAttribute.Enum("uwtable")];

    private static LinkName Pascal(string text) => new(text, AlwaysQuote: true);

    private static string Write(Action<LlvmTextWriter> body)
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule("M", "M.pas");
        body(writer);
        writer.EndModule();
        return writer.GetText();
    }

    private static string Lines(string text) => text.ReplaceLineEndings("\n");

    // ------------------------------------------------------------------ module skeleton (CMP-300, 301)

    [Fact]
    [Trait("Req", "CMP-300")]
    public void Module_EmptyModule_IsTheHeaderOnly() => Assert.Equal(Header, Write(_ => { }));

    [Fact]
    [Trait("Req", "CMP-300")]
    public void Module_GetTextBeforeEndModule_Throws()
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule("M", "M.pas");
        Assert.Throws<InvalidOperationException>(writer.GetText);
    }

    [Fact]
    [Trait("Req", "CMP-301")]
    public void Module_TopLevelEntities_AreWrittenInCmp301Order()
    {
        var text = Write(w =>
        {
            w.DefineAttributeGroup(1, NoReturn);
            w.DefineAttributeGroup(0, PascalRoutine);
            w.DeclareFunction(new(Pascal("System._RangeError"), IrType.Void, [], 1));
            DefineEmpty(w, "M.$init", IrLinkage.External);
            w.DefineGlobal(new(Pascal("M.B"), IrLinkage.Internal, false, new IrZeroConstant(IrType.I8), 1));
            w.DeclareFunction(new(Pascal("System._DivZeroError"), IrType.Void, [], 1));
            w.DeclareStruct(Pascal("M.Outer.$frame"), new StructIrType([IrType.Ptr, IrType.I32]));
            w.DefineGlobal(new(Pascal("M.A"), IrLinkage.External, false, IrIntConstant.I32(5), 4));
            DefineEmpty(w, "M.$final", IrLinkage.External);
        });

        Assert.Equal(Header + Lines("""

            %"M.Outer.$frame" = type { ptr, i32 }

            @"M.B" = internal global i8 zeroinitializer, align 1
            @"M.A" = global i32 5, align 4

            declare void @"System._DivZeroError"() #1
            declare void @"System._RangeError"() #1

            define void @"M.$init"() #0 {
            entry:
              ret void
            }

            define void @"M.$final"() #0 {
            entry:
              ret void
            }

            attributes #0 = { uwtable "frame-pointer"="all" }
            attributes #1 = { noreturn uwtable }

            """), text);
    }

    /// <summary>C-199 (codegen.md lead decision): a <c>linkonce_odr</c> global carries its own COFF comdat, so lld-link folds copies.</summary>
    [Fact]
    [Trait("Req", "CMP-365")]
    public void LinkOnceOdrGlobal_HasAComdat()
    {
        var text = Write(w =>
        {
            w.DefineGlobal(new(Pascal("System.string.$rtti"), IrLinkage.LinkOnceOdr, true, IrIntConstant.I32(9), 8));
            w.DefineGlobal(new(Pascal("M.A"), IrLinkage.Internal, false, IrIntConstant.I32(5), 4));
        });

        Assert.Equal(Header + Lines("""

            $"System.string.$rtti" = comdat any
            @"System.string.$rtti" = linkonce_odr constant i32 9, comdat, align 8
            @"M.A" = internal global i32 5, align 4

            """), text);
    }

    private static void DefineEmpty(IIrWriter w, string name, IrLinkage linkage)
    {
        w.BeginFunction(new(Pascal(name), linkage, IrType.Void, [], 0));
        w.AppendBlock(new IrBlock("entry"));
        w.Ret(null);
        w.EndFunction();
    }

    // ------------------------------------------------------------------ routines (CMP-310)

    /// <summary>The CMP-310 example, character for character.</summary>
    [Fact]
    [Trait("Req", "CMP-310")]
    public void Function_Add_MatchesTheCmp310Example()
    {
        var text = Write(w =>
        {
            w.DefineAttributeGroup(0, PascalRoutine);
            WriteAdd(w);
        });

        Assert.Equal(Header + Lines("""

            define i32 @"M.Add"(i32 %a, i32 %b) #0 {
            entry:
              %a.addr = alloca i32, align 4
              %b.addr = alloca i32, align 4
              %Result.addr = alloca i32, align 4
              store i32 %a, ptr %a.addr, align 4
              store i32 %b, ptr %b.addr, align 4
              %t1 = load i32, ptr %a.addr, align 4
              %t2 = load i32, ptr %b.addr, align 4
              %t3 = add i32 %t1, %t2
              store i32 %t3, ptr %Result.addr, align 4
              br label %exit
            exit:
              %t4 = load i32, ptr %Result.addr, align 4
              ret i32 %t4
            }

            attributes #0 = { uwtable "frame-pointer"="all" }

            """), text);
    }

    internal static void WriteAdd(IIrWriter w)
    {
        var p = w.BeginFunction(new(Pascal("M.Add"), IrLinkage.External, IrType.I32, [new(IrType.I32, "a"), new(IrType.I32, "b")], 0));
        var exit = new IrBlock("exit");
        w.AppendBlock(new IrBlock("entry"));
        var a = w.Alloca("a.addr", IrType.I32, 4);
        var b = w.Alloca("b.addr", IrType.I32, 4);
        var result = w.Alloca("Result.addr", IrType.I32, 4);
        w.Store(p[0], a, 4);
        w.Store(p[1], b, 4);
        var t1 = w.Load("t1", IrType.I32, a, 4);
        var t2 = w.Load("t2", IrType.I32, b, 4);
        w.Store(w.Binary("t3", IrBinaryOperator.Add, t1, t2), result, 4);
        w.Br(exit);
        w.AppendBlock(exit);
        w.Ret(w.Load("t4", IrType.I32, result, 4));
        w.EndFunction();
    }

    [Fact]
    [Trait("Req", "CMP-311")]
    public void Function_Linkages()
    {
        var text = Write(w =>
        {
            w.DefineAttributeGroup(0, PascalRoutine);
            DefineEmpty(w, "M.Hidden", IrLinkage.Internal);
            DefineEmpty(w, "M.Inst", IrLinkage.LinkOnceOdr);
            DefineEmpty(w, "M.Priv", IrLinkage.Private);
        });

        Assert.Contains("define internal void @\"M.Hidden\"() #0 {", text);
        Assert.Contains("define linkonce_odr void @\"M.Inst\"() #0 {", text);
        Assert.Contains("define private void @\"M.Priv\"() #0 {", text);
    }

    // ------------------------------------------------------------------ integer arithmetic, division (CMP-312)

    /// <summary>The CMP-312 division pattern for <c>a div b</c> (signed).</summary>
    [Fact]
    [Trait("Req", "CMP-312")]
    public void Division_MatchesTheCmp312Pattern()
    {
        var text = Write(w =>
        {
            w.DefineAttributeGroup(0, PascalRoutine);
            w.DefineAttributeGroup(1, NoReturn);
            WriteDivision(w);
        });

        Assert.Equal(Header + Lines("""

            declare void @"System._DivZeroError"() #1

            define i32 @"M.Div"(i32 %a, i32 %b) #0 {
            entry:
              %t1 = icmp eq i32 %b, 0
              br i1 %t1, label %div.zero.1, label %div.ok.1
            div.zero.1:
              call void @"System._DivZeroError"() #1
              unreachable
            div.ok.1:
              %t2 = icmp eq i32 %b, -1
              br i1 %t2, label %div.neg.1, label %div.do.1
            div.neg.1:
              %t3 = sub i32 0, %a
              br label %div.end.1
            div.do.1:
              %t4 = sdiv i32 %a, %b
              br label %div.end.1
            div.end.1:
              %t5 = phi i32 [ %t3, %div.neg.1 ], [ %t4, %div.do.1 ]
              ret i32 %t5
            }

            attributes #0 = { uwtable "frame-pointer"="all" }
            attributes #1 = { noreturn uwtable }

            """), text);
    }

    internal static void WriteDivision(IIrWriter w)
    {
        var divZero = w.DeclareFunction(new(Pascal("System._DivZeroError"), IrType.Void, [], 1));
        var p = w.BeginFunction(new(Pascal("M.Div"), IrLinkage.External, IrType.I32, [new(IrType.I32, "a"), new(IrType.I32, "b")], 0));
        IrBlock zero = new("div.zero.1"), ok = new("div.ok.1"), neg = new("div.neg.1"), @do = new("div.do.1"), end = new("div.end.1");
        w.AppendBlock(new IrBlock("entry"));
        w.CondBr(w.ICmp("t1", IrIntPredicate.Eq, p[1], IrIntConstant.I32(0)), zero, ok);
        w.AppendBlock(zero);
        Assert.Null(w.Call(null, IrType.Void, divZero, [], 1));
        w.Unreachable();
        w.AppendBlock(ok);
        w.CondBr(w.ICmp("t2", IrIntPredicate.Eq, p[1], IrIntConstant.I32(-1)), neg, @do);
        w.AppendBlock(neg);
        var t3 = w.Binary("t3", IrBinaryOperator.Sub, IrIntConstant.I32(0), p[0]);
        w.Br(end);
        w.AppendBlock(@do);
        var t4 = w.Binary("t4", IrBinaryOperator.SDiv, p[0], p[1]);
        w.Br(end);
        w.AppendBlock(end);
        w.Ret(w.Phi("t5", IrType.I32, [(t3, neg), (t4, @do)]));
        w.EndFunction();
    }

    [Theory]
    [InlineData(nameof(IrBinaryOperator.Add), "add")]
    [InlineData(nameof(IrBinaryOperator.Sub), "sub")]
    [InlineData(nameof(IrBinaryOperator.Mul), "mul")]
    [InlineData(nameof(IrBinaryOperator.SDiv), "sdiv")]
    [InlineData(nameof(IrBinaryOperator.UDiv), "udiv")]
    [InlineData(nameof(IrBinaryOperator.SRem), "srem")]
    [InlineData(nameof(IrBinaryOperator.URem), "urem")]
    [InlineData(nameof(IrBinaryOperator.And), "and")]
    [InlineData(nameof(IrBinaryOperator.Or), "or")]
    [InlineData(nameof(IrBinaryOperator.Xor), "xor")]
    [InlineData(nameof(IrBinaryOperator.Shl), "shl")]
    [InlineData(nameof(IrBinaryOperator.LShr), "lshr")]
    [InlineData(nameof(IrBinaryOperator.AShr), "ashr")]
    [Trait("Req", "CMP-312")]
    public void Binary_IntegerOperators(string opName, string mnemonic) =>
        Assert.Equal($"  %t1 = {mnemonic} i64 %x, 3", BodyLine(IrType.I64, (w, x) => w.Binary("t1", Enum.Parse<IrBinaryOperator>(opName), x, IrIntConstant.I64(3))));

    [Theory]
    [InlineData(nameof(IrBinaryOperator.FAdd), "fadd")]
    [InlineData(nameof(IrBinaryOperator.FSub), "fsub")]
    [InlineData(nameof(IrBinaryOperator.FMul), "fmul")]
    [InlineData(nameof(IrBinaryOperator.FDiv), "fdiv")]
    [Trait("Req", "CMP-316")]
    public void Binary_RealOperators(string opName, string mnemonic) =>
        Assert.Equal($"  %t1 = {mnemonic} double %x, %x", BodyLine(IrType.Double, (w, x) => w.Binary("t1", Enum.Parse<IrBinaryOperator>(opName), x, x)));

    [Fact]
    [Trait("Req", "CMP-316")]
    public void FNeg() => Assert.Equal("  %t1 = fneg float %x", BodyLine(IrType.Float, (w, x) => w.FNeg("t1", x)));

    [Fact]
    [Trait("Req", "CMP-312")]
    public void Binary_OperandTypesMustAgree() =>
        Assert.Throws<InvalidOperationException>(() => BodyLine(IrType.I32, (w, x) => w.Binary("t1", IrBinaryOperator.Add, x, IrIntConstant.I64(1))));

    /// <summary><c>{$Q+}</c> shape: overflow intrinsic returning <c>{ i32, i1 }</c> + <c>extractvalue</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-312")]
    public void OverflowIntrinsic_StructResultAndExtractValue()
    {
        var pair = new StructIrType([IrType.I32, IrType.I1]);
        var text = Write(w =>
        {
            var sadd = w.DeclareFunction(new(new LinkName("llvm.sadd.with.overflow.i32", false), pair, [IrType.I32, IrType.I32]));
            var p = w.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.I1, [new(IrType.I32, "a")]));
            w.AppendBlock(new IrBlock("entry"));
            var r = w.Call("t1", pair, sadd, [p[0], p[0]])!;
            Assert.Equal(IrType.I32, w.ExtractValue("t2", r, 0).Type);
            w.Ret(w.ExtractValue("t3", r, 1));
            w.EndFunction();
        });

        Assert.Contains("declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)\n", text);
        Assert.Contains(Lines("""
              %t1 = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %a, i32 %a)
              %t2 = extractvalue { i32, i1 } %t1, 0
              %t3 = extractvalue { i32, i1 } %t1, 1
              ret i1 %t3

            """), text);
    }

    // ------------------------------------------------------------------ conversions, comparisons, select (CMP-305, 313, 315)

    [Theory]
    [InlineData(nameof(IrCastOperator.SExt), "i32", "i64", "sext i32 %x to i64")]
    [InlineData(nameof(IrCastOperator.ZExt), "i1", "i8", "zext i1 %x to i8")]
    [InlineData(nameof(IrCastOperator.Trunc), "i64", "i16", "trunc i64 %x to i16")]
    [InlineData(nameof(IrCastOperator.SIToFP), "i32", "double", "sitofp i32 %x to double")]
    [InlineData(nameof(IrCastOperator.UIToFP), "i64", "float", "uitofp i64 %x to float")]
    [InlineData(nameof(IrCastOperator.FPExt), "float", "double", "fpext float %x to double")]
    [InlineData(nameof(IrCastOperator.FPTrunc), "double", "float", "fptrunc double %x to float")]
    [InlineData(nameof(IrCastOperator.FPToSI), "double", "i32", "fptosi double %x to i32")]
    [InlineData(nameof(IrCastOperator.FPToUI), "double", "i32", "fptoui double %x to i32")]
    [InlineData(nameof(IrCastOperator.PtrToInt), "ptr", "i64", "ptrtoint ptr %x to i64")]
    [InlineData(nameof(IrCastOperator.IntToPtr), "i64", "ptr", "inttoptr i64 %x to ptr")]
    [InlineData(nameof(IrCastOperator.BitCast), "i64", "double", "bitcast i64 %x to double")]
    [Trait("Req", "CMP-313")]
    public void Cast_Operators(string opName, string from, string to, string expected) =>
        Assert.Equal("  %t1 = " + expected, BodyLine(TypeOf(from), (w, x) => w.Cast("t1", Enum.Parse<IrCastOperator>(opName), x, TypeOf(to))));

    private static IrType TypeOf(string text) => text switch
    {
        "i1" => IrType.I1,
        "i8" => IrType.I8,
        "i16" => IrType.I16,
        "i32" => IrType.I32,
        "i64" => IrType.I64,
        "float" => IrType.Float,
        "double" => IrType.Double,
        "ptr" => IrType.Ptr,
        _ => throw new ArgumentException(text),
    };

    [Theory]
    [InlineData(nameof(IrIntPredicate.Eq), "eq")]
    [InlineData(nameof(IrIntPredicate.Ne), "ne")]
    [InlineData(nameof(IrIntPredicate.Slt), "slt")]
    [InlineData(nameof(IrIntPredicate.Sle), "sle")]
    [InlineData(nameof(IrIntPredicate.Sgt), "sgt")]
    [InlineData(nameof(IrIntPredicate.Sge), "sge")]
    [InlineData(nameof(IrIntPredicate.Ult), "ult")]
    [InlineData(nameof(IrIntPredicate.Ule), "ule")]
    [InlineData(nameof(IrIntPredicate.Ugt), "ugt")]
    [InlineData(nameof(IrIntPredicate.Uge), "uge")]
    [Trait("Req", "CMP-315")]
    public void ICmp_Predicates(string predicateName, string text)
    {
        IrLocal? result = null;
        Assert.Equal($"  %t1 = icmp {text} i16 %x, 7", BodyLine(IrType.I16, (w, x) => result = w.ICmp("t1", Enum.Parse<IrIntPredicate>(predicateName), x, new IrIntConstant(IrType.I16, 7))));
        Assert.Equal(IrType.I1, result!.Type);
    }

    [Theory]
    [InlineData(nameof(IrFloatPredicate.Oeq), "oeq")]
    [InlineData(nameof(IrFloatPredicate.Une), "une")]
    [InlineData(nameof(IrFloatPredicate.Olt), "olt")]
    [InlineData(nameof(IrFloatPredicate.Ole), "ole")]
    [InlineData(nameof(IrFloatPredicate.Ogt), "ogt")]
    [InlineData(nameof(IrFloatPredicate.Oge), "oge")]
    [InlineData(nameof(IrFloatPredicate.Uno), "uno")]
    [Trait("Req", "CMP-315")]
    public void FCmp_Predicates(string predicateName, string text) =>
        Assert.Equal($"  %t1 = fcmp {text} double %x, 0x3FF8000000000000",
            BodyLine(IrType.Double, (w, x) => w.FCmp("t1", Enum.Parse<IrFloatPredicate>(predicateName), x, new IrFloatConstant(IrType.Double, 1.5))));

    [Fact]
    [Trait("Req", "CMP-324")]
    public void Select() =>
        Assert.Equal("  %t2 = select i1 %t1, i32 0, i32 %x",
            BodyLine(IrType.I32, (w, x) => w.Select("t2", w.ICmp("t1", IrIntPredicate.Eq, x, IrIntConstant.I32(0)), IrIntConstant.I32(0), x), line: 1));

    /// <summary>CMP-305: Boolean loaded as <c>i8</c>, compared <c>ne 0</c>; stored via <c>zext i1</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-305")]
    public void BooleanMemoryRoundTrip()
    {
        var text = Write(w =>
        {
            var p = w.BeginFunction(new(Pascal("M.Not"), IrLinkage.Internal, IrType.Void, [new(IrType.Ptr, "b")]));
            w.AppendBlock(new IrBlock("entry"));
            var v = w.Load("t1", IrType.I8, p[0], 1);
            var c = w.ICmp("t2", IrIntPredicate.Ne, v, new IrIntConstant(IrType.I8, 0));
            var n = w.Binary("t3", IrBinaryOperator.Xor, c, IrIntConstant.I1(true));
            w.Store(w.Cast("t4", IrCastOperator.ZExt, n, IrType.I8), p[0], 1);
            w.Ret(null);
            w.EndFunction();
        });

        Assert.Contains(Lines("""
            define internal void @"M.Not"(ptr %b) {
            entry:
              %t1 = load i8, ptr %b, align 1
              %t2 = icmp ne i8 %t1, 0
              %t3 = xor i1 %t2, true
              %t4 = zext i1 %t3 to i8
              store i8 %t4, ptr %b, align 1
              ret void
            }

            """), text);
    }

    // ------------------------------------------------------------------ statements (CMP-317…323)

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Switch_ListsEveryCase()
    {
        var text = Write(w =>
        {
            var p = w.BeginFunction(new(Pascal("M.Case"), IrLinkage.External, IrType.Void, [new(IrType.I32, "sel")]));
            IrBlock @else = new("case.else.1"), arm = new("case.arm.2"), arm2 = new("case.arm.3");
            w.AppendBlock(new IrBlock("entry"));
            w.Switch(p[0], @else, [(IrIntConstant.I32(1), arm), (IrIntConstant.I32(2), arm), (IrIntConstant.I32(-5), arm2)]);
            foreach (var block in new[] { arm, arm2, @else })
            {
                w.AppendBlock(block);
                w.Ret(null);
            }

            w.EndFunction();
        });

        Assert.Contains(Lines("""
            entry:
              switch i32 %sel, label %case.else.1 [
                i32 1, label %case.arm.2
                i32 2, label %case.arm.2
                i32 -5, label %case.arm.3
              ]
            case.arm.2:
              ret void

            """), text);
    }

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Switch_WithoutCases() =>
        Assert.Contains("  switch i8 %x, label %case.else.1 [\n  ]\n", Write(w =>
        {
            var p = w.BeginFunction(new(Pascal("M.S"), IrLinkage.External, IrType.Void, [new(IrType.I8, "x")]));
            var @else = new IrBlock("case.else.1");
            w.AppendBlock(new IrBlock("entry"));
            w.Switch(p[0], @else, []);
            w.AppendBlock(@else);
            w.Ret(null);
            w.EndFunction();
        }));

    /// <summary>CMP-345 example: frame record struct, <c>%$link</c>, GEP into the frame.</summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void NestedRoutine_FrameAccess_MatchesTheCmp345Example()
    {
        var text = Write(w =>
        {
            w.DefineAttributeGroup(0, PascalRoutine);
            WriteFrameAccess(w);
        });

        Assert.Equal(Header + Lines("""

            %"R.Outer.$frame" = type { ptr, i32 }

            define internal void @"R.Outer.Add"(ptr %$link, i32 %x) #0 {
            entry:
              %t1 = getelementptr inbounds %"R.Outer.$frame", ptr %$link, i32 0, i32 1
              %t2 = load i32, ptr %t1, align 4
              %t3 = add i32 %t2, %x
              store i32 %t3, ptr %t1, align 4
              br label %exit
            exit:
              ret void
            }

            attributes #0 = { uwtable "frame-pointer"="all" }

            """), text);
    }

    internal static void WriteFrameAccess(IIrWriter w)
    {
        var frame = w.DeclareStruct(Pascal("R.Outer.$frame"), new StructIrType([IrType.Ptr, IrType.I32]));
        var p = w.BeginFunction(new(Pascal("R.Outer.Add"), IrLinkage.Internal, IrType.Void, [new(IrType.Ptr, "$link"), new(IrType.I32, "x")], 0));
        var exit = new IrBlock("exit");
        w.AppendBlock(new IrBlock("entry"));
        var field = w.GetElementPtr("t1", frame, p[0], [IrIntConstant.I32(0), IrIntConstant.I32(1)]);
        var t2 = w.Load("t2", IrType.I32, field, 4);
        w.Store(w.Binary("t3", IrBinaryOperator.Add, t2, p[1]), field, 4);
        w.Br(exit);
        w.AppendBlock(exit);
        w.Ret(null);
        w.EndFunction();
    }

    [Fact]
    [Trait("Req", "CMP-308")]
    public void GetElementPtr_ArrayAndByteForms()
    {
        var text = Write(w =>
        {
            var p = w.BeginFunction(new(Pascal("M.G"), IrLinkage.External, IrType.Ptr, [new(IrType.Ptr, "a"), new(IrType.I64, "i")]));
            w.AppendBlock(new IrBlock("entry"));
            var e = w.GetElementPtr("t1", new ArrayIrType(10, IrType.I16), p[0], [IrIntConstant.I64(0), p[1]]);
            w.Ret(w.GetElementPtr("t2", IrType.I8, e, [IrIntConstant.I64(-12)], inBounds: false));
            w.EndFunction();
        });

        Assert.Contains("  %t1 = getelementptr inbounds [10 x i16], ptr %a, i64 0, i64 %i\n  %t2 = getelementptr i8, ptr %t1, i64 -12\n", text);
    }

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Call_WithResultArgumentsAndIndirectCallee()
    {
        var text = Write(w =>
        {
            var f = w.DeclareFunction(new(Pascal("M.F"), IrType.I64, [IrType.I32, IrType.Ptr]));
            var p = w.BeginFunction(new(Pascal("M.G"), IrLinkage.External, IrType.Void, [new(IrType.Ptr, "fp")]));
            w.AppendBlock(new IrBlock("entry"));
            w.Call("t1", IrType.I64, f, [IrIntConstant.I32(-1), new IrNullConstant()]);
            Assert.Null(w.Call(null, IrType.I64, f, [IrIntConstant.I32(2), p[0]]));
            w.Call(null, IrType.Void, p[0], []);
            w.Ret(null);
            w.EndFunction();
        });

        Assert.Contains("declare i64 @\"M.F\"(i32, ptr)\n", text);
        Assert.Contains("  %t1 = call i64 @\"M.F\"(i32 -1, ptr null)\n  call i64 @\"M.F\"(i32 2, ptr %fp)\n  call void %fp()\n", text);
    }

    /// <summary>CMP-326: <c>llvm.memcpy</c> operands carry <c>align N</c>.</summary>
    [Fact]
    [Trait("Req", "CMP-326")]
    public void Call_AlignedPointerArguments()
    {
        var text = Write(w =>
        {
            var memcpy = w.DeclareFunction(new(new LinkName("llvm.memcpy.p0.p0.i64", false), IrType.Void, [IrType.Ptr, IrType.Ptr, IrType.I64, IrType.I1]));
            var p = w.BeginFunction(new(Pascal("M.G"), IrLinkage.External, IrType.Void, [new(IrType.Ptr, "d"), new(IrType.Ptr, "s")]));
            w.AppendBlock(new IrBlock("entry"));
            w.Call(null, IrType.Void, memcpy, [new IrAlignedPointer(p[0], 4), new IrAlignedPointer(p[1], 4), IrIntConstant.I64(12), IrIntConstant.I1(false)]);
            w.Ret(null);
            w.EndFunction();
        });

        Assert.Contains("  call void @llvm.memcpy.p0.p0.i64(ptr align 4 %d, ptr align 4 %s, i64 12, i1 false)\n", text);
    }

    [Fact]
    public void Call_VoidWithName_Throws() =>
        Assert.Throws<InvalidOperationException>(() => BodyLine(IrType.Ptr, (w, x) => w.Call("t1", IrType.Void, x, [])));

    // ------------------------------------------------------------------ globals and constants (CMP-309, 341, 355)

    [Fact]
    [Trait("Req", "CMP-341")]
    public void Globals_ScalarArrayStructAndUnnamedAddrConstants()
    {
        var pair = new StructIrType([IrType.Ptr, IrType.Ptr]);
        var text = Write(w =>
        {
            w.DefineGlobal(new(Pascal("U.Table"), IrLinkage.External, true,
                new IrArrayConstant(new ArrayIrType(3, IrType.I32), [IrIntConstant.I32(1), IrIntConstant.I32(2), IrIntConstant.I32(3)]), 4));
            w.DefineGlobal(new(new LinkName("$units", true), IrLinkage.Internal, true,
                new IrArrayConstant(new ArrayIrType(1, pair), [new IrStructConstant(pair, [new IrGlobal(Pascal("System.$init")), new IrNullConstant()])]), 8));
            w.DefineGlobal(new(new LinkName(".str.empty", false), IrLinkage.Private, true, new IrZeroConstant(new ArrayIrType(1, IrType.I16)), 2, UnnamedAddr: true));
            w.DefineGlobal(new(Pascal("U.Big"), IrLinkage.External, false, new IrIntConstant(IrType.I64, ulong.MaxValue), 8));
            w.DefineGlobal(new(Pascal("U.Min"), IrLinkage.External, false, new IrIntConstant(IrType.I64, long.MinValue), 8));
            w.DefineGlobal(new(Pascal("U.Flag"), IrLinkage.Internal, false, IrIntConstant.I1(false), 1));
            w.DefineGlobal(new(Pascal("U.S"), IrLinkage.External, false, new IrFloatConstant(IrType.Float, 0.1), 4));
            w.DefineGlobal(new(Pascal("U.D"), IrLinkage.External, false, new IrFloatConstant(IrType.Double, -0.0), 8));
            w.DefineGlobal(new(Pascal("U.Rec"), IrLinkage.External, false, new IrZeroConstant(new StructIrType([IrType.I8, IrType.I64], IsPacked: true)), 1));
            w.DefineGlobal(new(Pascal("U.E"), IrLinkage.External, false, new IrZeroConstant(new StructIrType([])), 1));
        });

        Assert.Equal(Header + Lines("""

            @"U.Table" = constant [3 x i32] [i32 1, i32 2, i32 3], align 4
            @"$units" = internal constant [1 x { ptr, ptr }] [{ ptr, ptr } { ptr @"System.$init", ptr null }], align 8
            @.str.empty = private unnamed_addr constant [1 x i16] zeroinitializer, align 2
            @"U.Big" = global i64 -1, align 8
            @"U.Min" = global i64 -9223372036854775808, align 8
            @"U.Flag" = internal global i1 false, align 1
            @"U.S" = global float 0x3FB99999A0000000, align 4
            @"U.D" = global double 0x8000000000000000, align 8
            @"U.Rec" = global <{ i8, i64 }> zeroinitializer, align 1
            @"U.E" = global {} zeroinitializer, align 1

            """), text);
    }

    /// <summary>C-040: external global declarations and constant GEP expressions (CMP-320 string references).</summary>
    [Fact]
    [Trait("Req", "CMP-320")]
    public void Globals_ExternalDeclarationAndConstantGep()
    {
        var block = new StructIrType([IrType.I32, new ArrayIrType(2, IrType.I16)]);
        var text = Write(w =>
        {
            w.DeclareGlobal(Pascal("System.Output"), new ArrayIrType(624, IrType.I8), 8);
            w.DefineGlobal(new(Pascal("U.P"), IrLinkage.Internal, false,
                new IrGepConstant(block, new IrGlobal(new LinkName(".str.0", false)), [IrIntConstant.I32(0), IrIntConstant.I32(1)]), 8));
        });

        Assert.Equal(Header + Lines("""

            @"System.Output" = external global [624 x i8], align 8
            @"U.P" = internal global ptr getelementptr inbounds ({ i32, [2 x i16] }, ptr @.str.0, i32 0, i32 1), align 8

            """), text);
    }

    [Fact]
    public void Global_ReturnsItsAddress() =>
        Assert.Equal(new IrGlobal(Pascal("U.X")), Write2(w => w.DefineGlobal(new(Pascal("U.X"), IrLinkage.External, false, IrIntConstant.I32(0), 4))));

    private static T Write2<T>(Func<LlvmTextWriter, T> body)
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule("M", "M.pas");
        var result = body(writer);
        writer.EndModule();
        return result;
    }

    // ------------------------------------------------------------------ names (CMP-303, 360 quoting rule)

    [Theory]
    [InlineData("U.F", true, "@\"U.F\"")]
    [InlineData("memset", false, "@memset")]
    [InlineData("_start", false, "@_start")]
    [InlineData(".str.3", false, "@.str.3")]
    [InlineData("my func", false, "@\"my func\"")]
    [InlineData("9lives", false, "@\"9lives\"")]
    [InlineData("a\"b\\c", true, "@\"a\\22b\\5Cc\"")]
    [InlineData("Größe", true, "@\"Gr\\C3\\B6\\C3\\9Fe\"")]
    [Trait("Req", "CMP-360")]
    public void GlobalNames_QuotingRule(string name, bool alwaysQuote, string expected) =>
        Assert.Contains($"declare void {expected}()\n", Write(w => w.DeclareFunction(new(new LinkName(name, alwaysQuote), IrType.Void, []))));

    [Theory]
    [InlineData("a.addr", "%a.addr")]
    [InlineData("$link", "%$link")]
    [InlineData("t12", "%t12")]
    [InlineData("Größe", "%\"Gr\\C3\\B6\\C3\\9Fe\"")]
    [InlineData("x y", "%\"x y\"")]
    [Trait("Req", "CMP-303")]
    public void LocalNames_AreQuotedOnlyWhenNeeded(string name, string expected) =>
        Assert.Equal($"  {expected} = alloca i32, align 4", BodyLine(IrType.I32, (w, _) => w.Alloca(name, IrType.I32, 4)));

    [Fact]
    [Trait("Req", "CMP-300")]
    public void SourceFileName_IsEscaped() =>
        Assert.StartsWith("; ModuleID = 'Q'\nsource_filename = \"a\\22b.pas\"\n", Module("Q", "a\"b.pas"));

    private static string Module(string name, string file)
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule(name, file);
        writer.EndModule();
        return writer.GetText();
    }

    // ------------------------------------------------------------------ structural checks

    [Fact]
    public void InstructionOutsideABlock_Throws()
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule("M", "M.pas");
        writer.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.Void, []));
        Assert.Throws<InvalidOperationException>(() => writer.Ret(null));
    }

    [Fact]
    public void InstructionAfterTerminator_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Write(w =>
        {
            w.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.Void, []));
            w.AppendBlock(new IrBlock("entry"));
            w.Ret(null);
            w.Ret(null);
        }));

    [Fact]
    public void BlockWithoutTerminator_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Write(w =>
        {
            w.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.Void, []));
            w.AppendBlock(new IrBlock("entry"));
            w.EndFunction();
        }));

    [Fact]
    public void EndModuleInsideAFunction_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Write(w =>
        {
            w.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.Void, []));
        }));

    [Fact]
    public void DuplicateAttributeGroup_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Write(w =>
        {
            w.DefineAttributeGroup(0, PascalRoutine);
            w.DefineAttributeGroup(0, NoReturn);
        }));

    // ------------------------------------------------------------------ helpers

    /// <summary>One function <c>M.F(x: T)</c> with a body produced by <paramref name="body"/>; returns its first instruction line.</summary>
    private static string BodyLine(IrType parameterType, Action<IIrWriter, IrLocal> body, int line = 0)
    {
        var text = Write(w =>
        {
            var p = w.BeginFunction(new(Pascal("M.F"), IrLinkage.External, IrType.Void, [new(parameterType, "x")]));
            w.AppendBlock(new IrBlock("entry"));
            body(w, p[0]);
            w.Ret(null);
            w.EndFunction();
        });
        var lines = text.Split('\n');
        return lines[Array.IndexOf(lines, "entry:") + 1 + line];
    }
}

/// <summary>The writer's output is accepted by the pinned clang (scripts/toolchain.json); fails when the toolchain is missing.</summary>
[Trait("Phase", "2")]
[Trait("Category", "Toolchain")]
public sealed class LlvmTextWriterToolchainTests
{
    private static LinkName Pascal(string text) => new(text, AlwaysQuote: true);

    [Theory]
    [InlineData("-O0")]
    [InlineData("-O2")]
    [Trait("Req", "CMP-125")]
    public void LlvmTextWriter_EveryConstruct_CompilesWithPinnedClang(string optimisation)
    {
        var writer = new LlvmTextWriter();
        writer.BeginModule("All", "All.pas");
        WriteEveryConstruct(writer);
        writer.EndModule();

        var (exitCode, output) = Clang.CompileToObject(writer.GetText(), optimisation);

        Assert.True(exitCode == 0, "clang failed:\n" + output);
    }

    private static void WriteEveryConstruct(IIrWriter w)
    {
        w.DefineAttributeGroup(0, [IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        w.DefineAttributeGroup(1, [IrAttribute.Enum("noreturn"), IrAttribute.Enum("uwtable")]);
        w.DefineAttributeGroup(2, [IrAttribute.Enum("uwtable"), IrAttribute.String("no-builtins")]);
        w.DefineAttributeGroup(3, [IrAttribute.Enum("noinline"), IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        LlvmTextWriterTests.WriteAdd(w);
        LlvmTextWriterTests.WriteDivision(w);
        LlvmTextWriterTests.WriteFrameAccess(w);

        var pair = new StructIrType([IrType.Ptr, IrType.Ptr]);
        var init = w.DeclareFunction(new(Pascal("System.$init"), IrType.Void, []));
        w.DefineGlobal(new(new LinkName("$units", true), IrLinkage.Internal, true,
            new IrArrayConstant(new ArrayIrType(1, pair), [new IrStructConstant(pair, [init, new IrNullConstant()])]), 8));
        w.DefineGlobal(new(new LinkName(".str.empty", false), IrLinkage.Private, true, new IrZeroConstant(new ArrayIrType(1, IrType.I16)), 2, UnnamedAddr: true));
        var total = w.DefineGlobal(new(Pascal("All.Total"), IrLinkage.Internal, false, new IrIntConstant(IrType.I64, ulong.MaxValue), 8));
        w.DefineGlobal(new(Pascal("All.Ratio"), IrLinkage.External, false, new IrFloatConstant(IrType.Float, 0.1), 4));
        w.DefineGlobal(new(Pascal("All.Rec"), IrLinkage.External, false, new IrZeroConstant(new StructIrType([IrType.I8, IrType.I64], IsPacked: true)), 1));
        w.DefineGlobal(new(Pascal("Größe \"q\""), IrLinkage.External, false, IrIntConstant.I1(true), 1));
        var tri = new StructIrType([IrType.I64, IrType.I1]);
        var smul = w.DeclareFunction(new(new LinkName("llvm.smul.with.overflow.i64", false), tri, [IrType.I64, IrType.I64]));
        var range = w.DeclareFunction(new(Pascal("System._RangeError"), IrType.Void, [], 1));

        var p = w.BeginFunction(new(Pascal("All.Every"), IrLinkage.External, IrType.Double,
            [new(IrType.I32, "n"), new(IrType.Double, "d"), new(IrType.Ptr, "p"), new(IrType.I8, "Größe")], 3));
        IrBlock entry = new("entry"), loop = new("while.cond.1"), body = new("while.body.2"), end = new("while.end.3"),
            arm = new("case.arm.4"), @else = new("case.else.5"), bad = new("range.err.6"), exit = new("exit");
        w.AppendBlock(entry);
        var i = w.Alloca("i.addr", IrType.I32, 4);
        var buf = w.Alloca("buf.addr", new ArrayIrType(4, IrType.I16), 2);
        w.Store(IrIntConstant.I32(0), i, 4);
        w.Switch(p[0], @else, [(IrIntConstant.I32(1), arm), (IrIntConstant.I32(-3), arm)]);
        w.AppendBlock(arm);
        w.Br(loop);
        w.AppendBlock(@else);
        w.Br(loop);
        w.AppendBlock(loop);
        var iv = w.Load("t1", IrType.I32, i, 4);
        w.CondBr(w.ICmp("t2", IrIntPredicate.Slt, iv, p[0]), body, end);
        w.AppendBlock(body);
        var wide = w.Cast("t3", IrCastOperator.SExt, iv, IrType.I64);
        var elem = w.GetElementPtr("t4", new ArrayIrType(4, IrType.I16), buf, [IrIntConstant.I64(0), w.Binary("t5", IrBinaryOperator.And, wide, IrIntConstant.I64(3))]);
        w.Store(w.Cast("t6", IrCastOperator.Trunc, iv, IrType.I16), elem, 2);
        var mul = w.Call("t7", tri, smul, [wide, wide])!;
        w.Store(w.ExtractValue("t8", mul, 0), total, 8);
        var overflow = w.ExtractValue("t9", mul, 1);
        var next = EveryIntegerOperator(w, "t10", iv);
        w.Store(next, i, 4);
        w.CondBr(overflow, bad, loop);
        w.AppendBlock(bad);
        w.Call(null, IrType.Void, range, [], 1);
        w.Unreachable();
        w.AppendBlock(end);
        var cond = w.FCmp("t11", IrFloatPredicate.Une, p[1], new IrFloatConstant(IrType.Double, 0));
        var asReal = w.Cast("t12", IrCastOperator.SIToFP, iv, IrType.Double);
        var sum = w.Binary("t13", IrBinaryOperator.FMul, w.FNeg("t14", p[1]), asReal);
        var addr = w.Cast("t15", IrCastOperator.PtrToInt, p[2], IrType.I64);
        w.Cast("t16", IrCastOperator.IntToPtr, addr, IrType.Ptr);
        w.Cast("t17", IrCastOperator.ZExt, p[3], IrType.I32);
        var chosen = w.Select("t18", cond, sum, w.Cast("t19", IrCastOperator.FPExt, new IrFloatConstant(IrType.Float, 2.5), IrType.Double));
        w.Br(exit);
        w.AppendBlock(exit);
        w.Ret(w.Phi("t20", IrType.Double, [(chosen, end)]));
        w.EndFunction();

        w.BeginFunction(new(new LinkName("memset", false), IrLinkage.External, IrType.Ptr, [new(IrType.Ptr, "d"), new(IrType.I32, "c"), new(IrType.I64, "n")], 2));
        w.AppendBlock(new IrBlock("entry"));
        w.Ret(new IrNullConstant());
        w.EndFunction();
    }

    /// <summary>Every integer operator once, chained on <paramref name="iv"/>.</summary>
    private static IrLocal EveryIntegerOperator(IIrWriter w, string name, IrValue iv)
    {
        IrValue v = w.Binary(name + "a", IrBinaryOperator.Add, iv, IrIntConstant.I32(1));
        var ops = new[] { IrBinaryOperator.Sub, IrBinaryOperator.Mul, IrBinaryOperator.SDiv, IrBinaryOperator.UDiv, IrBinaryOperator.SRem,
            IrBinaryOperator.URem, IrBinaryOperator.Or, IrBinaryOperator.Xor, IrBinaryOperator.Shl, IrBinaryOperator.LShr, IrBinaryOperator.AShr };
        for (var k = 0; k < ops.Length; k++)
        {
            v = w.Binary(name + "b" + k, ops[k], v, IrIntConstant.I32(1));
        }

        return w.Binary(name, IrBinaryOperator.Add, v, IrIntConstant.I32(0));
    }
}

/// <summary>Runs the pinned <c>clang -Wno-override-module -c</c> on IR text in a per-test temp folder (TESTING.md rules 3, 4).</summary>
internal static class Clang
{
    public static string Path => System.IO.Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin", "clang.exe");

    public static (int ExitCode, string Output) CompileToObject(string irText, string optimisation)
    {
        Assert.True(File.Exists(Path), $"Pinned toolchain missing: {Path} (run scripts/bootstrap.ps1).");
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = System.IO.Path.Combine(dir, "m.ll");
        File.WriteAllText(source, irText);
        var start = new ProcessStartInfo(Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-Wno-override-module", optimisation, "-c", source, "-o", System.IO.Path.Combine(dir, "m.obj") })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(20_000), "clang timed out");
            var output = stdout.Result + stderr.Result;
            if (process.ExitCode == 0)
            {
                Assert.True(File.Exists(System.IO.Path.Combine(dir, "m.obj")));
                TestDirectories.Delete(dir);
            }
            else
            {
                output += "\n(IR kept in " + source + ")";
            }

            return (process.ExitCode, output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
