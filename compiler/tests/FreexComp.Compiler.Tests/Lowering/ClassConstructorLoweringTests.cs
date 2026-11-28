using System.Text.Json.Nodes;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Types;
using FreexComp.Tests.Shared;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>C-374 (generics.md §14.3, §14.4): the class constructor items of a module (CMP-1396, LANG-790 c own items) and its unit table entry.</summary>
[Trait("Phase", "8")]
public sealed class ClassConstructorLoweringTests
{
    /// <summary>
    /// LANG-790 c: the module's own types with a class constructor or destructor in declaration order — interface part, then
    /// implementation part, an enclosing type before its nested types (depth first, in declaration order); a type with neither is
    /// no item. The constructor parts run in that order in <c>$init</c>, the destructor parts in reverse order in <c>$final</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1396")]
    public void Order_DeclarationOrder_EnclosingBeforeNested()
    {
        var (result, _) = Bind("""
            unit M;
            interface
            type
              TB = class class constructor Create; end;
              TA = class
                class destructor Destroy;
              public
                type
                  TN = class
                    class constructor Create;
                    class destructor Destroy;
                  public
                    type TDeep = record X: Integer; class constructor Create; end;
                  end;
                  TPlain = class end;
                  TM = class class constructor Create; end;
              end;
              TD = class(TB) class destructor Destroy; end;
            implementation
            type
              TC = record X: Integer; class constructor Create; class destructor Destroy; end;
            class constructor TB.Create; begin end;
            class destructor TA.Destroy; begin end;
            class constructor TA.TN.Create; begin end;
            class destructor TA.TN.Destroy; begin end;
            class constructor TA.TN.TDeep.Create; begin end;
            class constructor TA.TM.Create; begin end;
            class destructor TD.Destroy; begin end;
            class constructor TC.Create; begin end;
            class destructor TC.Destroy; begin end;
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));

        var lowered = Lowerer.Lower(result);
        Assert.Equal(["TB", "TA", "TA.TN", "TA.TN.TDeep", "TA.TM", "TD", "TC"], lowered.ClassConstructors.Select(i => TypeMembers.DisplayName(i.Type)));
        Assert.All(lowered.ClassConstructors, i => Assert.False(i.IsInstance));

        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(lowered, writer);
        var text = writer.GetText();
        static int At(string text, string what) => text.IndexOf(what, StringComparison.Ordinal) is var i and >= 0 ? i : throw new Xunit.Sdk.XunitException(what + " missing");
        var init = At(text, "define void @\"M.$init\"");
        var final = At(text, "define void @\"M.$final\"");
        string[] constructors = ["@\"M.TB.$cctor\"()", "@\"M.TA.TN.$cctor\"()", "@\"M.TA.TN.TDeep.$cctor\"()", "@\"M.TA.TM.$cctor\"()", "@\"M.TC.$cctor\"()"];
        string[] destructors = ["@\"M.TC.$cdtor\"()", "@\"M.TD.$cdtor\"()", "@\"M.TA.TN.$cdtor\"()", "@\"M.TA.$cdtor\"()"];
        var calls = constructors.Select(c => At(text[init..], "call void " + c)).ToArray();
        Assert.Equal(calls.Order(), calls);
        var dtorCalls = destructors.Select(d => At(text[final..], "call void " + d)).ToArray();
        Assert.Equal(dtorCalls.Order(), dtorCalls);
        Assert.Contains("@\"M.$ccdone\" = internal global [7 x i8] zeroinitializer, align 1\n", text);
    }

    /// <summary>
    /// CMP-1396: a unit whose only content is a class constructor (and destructor) gets a <c>$init</c> (and a <c>$final</c>) entry
    /// in the program's unit table and in its <c>.pcu</c> (<c>hasInit</c> / <c>hasFinal</c>); a program type with a class
    /// destructor gives the program its own last entry even without managed globals.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1396")]
    public void UnitWithOnlyAClassConstructor_HasInitAndFinal()
    {
        const string both = "unit UBoth;\ninterface\ntype TA = class class constructor Create; class destructor Destroy; end;\nimplementation\n"
            + "class constructor TA.Create; begin end;\nclass destructor TA.Destroy; begin end;\nend.\n";
        const string ctorOnly = "unit UCtor;\ninterface\nimplementation\ntype TR = record X: Integer; class constructor Create; end;\n"
            + "class constructor TR.Create; begin end;\nend.\n";
        const string program = "program P;\nuses UBoth, UCtor;\ntype TP = record X: Integer; class destructor Done; end;\n"
            + "class destructor TP.Done; begin end;\nbegin end.\n";
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\UBoth.pas", both).Add(@"C:\p\UCtor.pas", ctorOnly).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var pcus = Compilation.Create(options, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => (JsonObject)JsonNode.Parse(t.Text)!);
        Assert.Equal((true, true), ((bool)pcus["UBoth"]["hasInit"]!, (bool)pcus["UBoth"]["hasFinal"]!));
        Assert.Equal((true, false), ((bool)pcus["UCtor"]["hasInit"]!, (bool)pcus["UCtor"]["hasFinal"]!));

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var ir = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Contains("{ ptr @\"UBoth.$init\", ptr @\"UBoth.$final\" }", ir);
        Assert.Contains("{ ptr @\"UCtor.$init\", ptr null }", ir);
        Assert.Contains("{ ptr null, ptr @\"P.$final\" }]", ir);                            // the program's own entry, last
        Assert.Contains("define internal void @\"P.$final\"()", ir);
    }

    /// <summary>The display names of the module's class constructor items in run order, instance items marked.</summary>
    private static string[] RunOrderOf(string source)
    {
        var (result, _) = Bind(source);
        Assert.Empty(Format(result.Diagnostics));
        return [.. Lowerer.Lower(result).ClassConstructors.Select(i => TypeMembers.DisplayName(i.Type) + (i.IsInstance ? " (instance)" : ""))];
    }

    /// <summary>
    /// C-375 (LANG-790 c): an ancestor is a hard prerequisite — the instance <c>TBase&lt;Integer&gt;</c> runs before the own type
    /// <c>TD</c> derived from it (own items come first in S), and the instance <c>TBase&lt;Byte&gt;</c> before the instance
    /// <c>TAlpha&lt;Byte&gt;</c> derived from it, although the link-name order puts <c>TAlpha</c> first.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1396")]
    public void Order_AncestorInstanceBeforeDescendant()
    {
        Assert.Equal(["TBase<Byte> (instance)", "TAlpha<Byte> (instance)", "TBase<Integer> (instance)", "TD"], RunOrderOf("""
            unit M;
            interface
            type
              TBase<T> = class class constructor Create; class destructor Destroy; end;
              TAlpha<T> = class(TBase<T>) class constructor Create; end;
              TD = class(TBase<Integer>) class constructor Create; end;
            var
              A: TAlpha<Byte>;
              B: TBase<Byte>;
            implementation
            class constructor TBase<T>.Create; begin end;
            class destructor TBase<T>.Destroy; begin end;
            class constructor TAlpha<T>.Create; begin end;
            class constructor TD.Create; begin end;
            end.
            """));
    }

    /// <summary>
    /// C-375 (LANG-790 c, the spec's example with names that make the soft edge visible): an instance occurring in the type
    /// arguments of another runs first — <c>TBox&lt;Integer&gt;</c> before <c>TAwrap&lt;TBox&lt;Integer&gt;&gt;</c>, whose link name sorts
    /// before it; the own type <c>TA</c> comes first; an instance whose generic has neither (<c>TPlain&lt;Byte&gt;</c>) is no item.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1396")]
    public void Order_TypeArgumentInstanceFirst()
    {
        Assert.Equal(["TA", "TBox<Integer> (instance)", "TAwrap<TBox<Integer>> (instance)", "TBox<TA> (instance)"], RunOrderOf("""
            unit M;
            interface
            type
              TA = class class constructor Create; class destructor Destroy; end;
              TBox<T> = class class constructor Create; class destructor Destroy; end;
              TAwrap<T> = class class constructor Create; class destructor Destroy; end;
              TPlain<T> = class X: T; end;
            var
              A: TBox<TA>;
              B: TBox<Integer>;
              C: TAwrap<TBox<Integer>>;
              D: TPlain<Byte>;
            implementation
            class constructor TA.Create; begin end;
            class destructor TA.Destroy; begin end;
            class constructor TBox<T>.Create; begin end;
            class destructor TBox<T>.Destroy; begin end;
            class constructor TAwrap<T>.Create; begin end;
            class destructor TAwrap<T>.Destroy; begin end;
            end.
            """));
    }

    /// <summary>
    /// C-375 (LANG-790 c): <c>TC&lt;T&gt; = class(TZBox&lt;TC&lt;T&gt;&gt;)</c> — <c>TC&lt;Integer&gt;</c> has <c>TZBox&lt;TC&lt;Integer&gt;&gt;</c> as hard
    /// prerequisite, which has <c>TC&lt;Integer&gt;</c> as soft one; the cycle is broken by the hard edge, so the ancestor runs first
    /// although its link name sorts after its descendant's.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-790")]
    [Trait("Req", "CMP-1396")]
    public void Order_HardEdgeBreaksCycle()
    {
        Assert.Equal(["TZBox<TC<Integer>> (instance)", "TC<Integer> (instance)"], RunOrderOf("""
            unit M;
            interface
            type
              TZBox<T> = class class constructor Create; end;
              TC<T> = class(TZBox<TC<T>>) class constructor Create; class destructor Destroy; end;
            var
              C: TC<Integer>;
            implementation
            class constructor TZBox<T>.Create; begin end;
            class constructor TC<T>.Create; begin end;
            class destructor TC<T>.Destroy; begin end;
            end.
            """));
    }

    /// <summary>
    /// C-375 (CMP-1396, C-228 rule): the instance items of a module are exactly the instances whose functions it defines — the
    /// generic's declaring unit <c>G</c>, which reaches <c>TBox&lt;Integer&gt;</c> only through the signature of
    /// <c>TMaker&lt;T&gt;.Make</c>, has none; <c>U</c> (which uses <c>TMaker&lt;Integer&gt;</c>, so the <c>TBox&lt;Integer&gt;</c> of its signature,
    /// and <c>TBox&lt;TImpl&gt;</c> in its implementation part) and the program (<c>TBox&lt;Integer&gt;</c>, <c>TBox&lt;TLocal&gt;</c>) claim and
    /// run what they define, nothing of another module's private types.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1396")]
    [Trait("Req", "LANG-790")]
    public void InstanceItems_OnlyInstancesTheModuleDefines()
    {
        const string g = """
            unit G;
            interface
            type
              TBox<T> = class class constructor Create; class destructor Destroy; end;
              TMaker<T> = class function Make: TBox<T>; end;
            implementation
            class constructor TBox<T>.Create; begin end;
            class destructor TBox<T>.Destroy; begin end;
            function TMaker<T>.Make: TBox<T>; begin Result := nil end;
            end.
            """;
        const string u = """
            unit U;
            interface
            uses G;
            var M: TMaker<Integer>;
            implementation
            type TImpl = record X: Integer; end;
            var B: TBox<TImpl>;
            end.
            """;
        const string program = """
            program P;
            uses G, U;
            type TLocal = record X: Byte; end;
            var A: TBox<Integer>; L: TBox<TLocal>;
            begin end.
            """;
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\G.pas", g).Add(@"C:\p\U.pas", u).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        static string[] Matches(string text, string pattern) =>
            [.. System.Text.RegularExpressions.Regex.Matches(text, pattern).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)];
        foreach (var (module, expected) in new[] { ("G", Array.Empty<string>()), ("U", ["G.TBox<Integer>", "G.TBox<U.TImpl>"]), ("P", ["G.TBox<Integer>", "G.TBox<P.TLocal>"]) })
        {
            var ir = fs.ReadAllText(@"C:\p\obj\" + module + ".ll");
            Assert.Equal(expected, Matches(ir, "%cc\\d+\\.state = load i8, ptr @\"([^\"]+)\\.\\$ccstate\""));      // claimed and run here
            Assert.Equal(expected, Matches(ir, "define linkonce_odr void @\"([^\"]+)\\.\\$cctor\"\\(\\)"));      // defined here
            Assert.Equal(expected.Length != 0, ir.Contains("$ccdone", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// LANG-995, CMP-1491 (C-441): a helper with a class constructor or class destructor is an item of its unit in declaration order
    /// among the unit's classes and records, without prerequisites — neither its helped type (the instance <c>TBox&lt;Integer&gt;</c>
    /// runs after the helper for it, while the class <c>TD</c> derived from that instance waits for it) nor an ancestor helper; a
    /// helper with neither is no item. Its class constructor runs in <c>$init</c>, its class destructor in reverse order in
    /// <c>$final</c>, also when the helper is never used.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-995")]
    [Trait("Req", "CMP-1491")]
    public void Helper_IsAnItemOfItsUnit()
    {
        const string Source = """
            unit M;
            interface
            type
              TBox<T> = class class constructor Create; end;
              TA = class class constructor Create; end;
              TAH = class helper for TA class destructor Done; end;
              TPlainH = class helper for TA end;
              TIntH = record helper for Integer class var Calls: Integer; class constructor Init; class destructor Done; end;
              TR = record X: Integer; class constructor Create; end;
              TBoxH = class helper for TBox<Integer> class constructor Create; end;
              TD = class(TBox<Integer>) class constructor Create; end;
            implementation
            type
              TAH2 = class helper(TAH) for TA class constructor Create; end;
            class constructor TBox<T>.Create; begin end;
            class constructor TA.Create; begin end;
            class destructor TAH.Done; begin end;
            class constructor TIntH.Init; begin Calls := 10 end;
            class destructor TIntH.Done; begin end;
            class constructor TR.Create; begin end;
            class constructor TBoxH.Create; begin end;
            class constructor TD.Create; begin end;
            class constructor TAH2.Create; begin end;
            end.
            """;
        Assert.Equal(["TA", "TAH", "TIntH", "TR", "TBoxH", "TAH2", "TBox<Integer> (instance)", "TD"], RunOrderOf(Source));

        var (result, _) = Bind(Source);
        var writer = new LlvmTextWriter();
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer);
        var text = writer.GetText();
        static int At(string text, string what) => text.IndexOf(what, StringComparison.Ordinal) is var i and >= 0 ? i : throw new Xunit.Sdk.XunitException(what + " missing");
        var init = At(text, "define void @\"M.$init\"");
        var final = At(text, "define void @\"M.$final\"");
        string[] constructors = ["@\"M.TA.$cctor\"()", "@\"M.TIntH.$cctor\"()", "@\"M.TR.$cctor\"()", "@\"M.TBoxH.$cctor\"()", "@\"M.TAH2.$cctor\"()"];
        string[] destructors = ["@\"M.TIntH.$cdtor\"()", "@\"M.TAH.$cdtor\"()"];
        var calls = constructors.Select(c => At(text[init..], "call void " + c)).ToArray();
        Assert.Equal(calls.Order(), calls);
        var dtorCalls = destructors.Select(d => At(text[final..], "call void " + d)).ToArray();
        Assert.Equal(dtorCalls.Order(), dtorCalls);
        Assert.Matches(@"define [a-z_ ]*void @""M\.TIntH\.\$cctor""\(\)", text);
        Assert.Contains("@\"M.$ccdone\" = internal global [8 x i8] zeroinitializer, align 1\n", text);
        Assert.DoesNotContain("TPlainH", text, StringComparison.Ordinal);
    }
}
