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
}
