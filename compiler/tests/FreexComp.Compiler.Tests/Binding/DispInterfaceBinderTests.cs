using System.Collections;
using BindingFlags = System.Reflection.BindingFlags;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-303 (com-interfaces.md §6): dispatch interfaces in the binder — the Automation type table (LANG-695), conversions to and
/// from <c>IDispatch</c> (LANG-696), member access with its <c>Invoke</c> flags (LANG-697), declaration rules (LANG-693, 694).
/// </summary>
[Trait("Phase", "8")]
public sealed class DispInterfaceBinderTests
{
    /// <summary>A <c>System</c> with the root interface, <c>IDispatch</c> (RTL-1001, shortened) and <c>TDateTime</c>.</summary>
    internal const string SystemSource = """
        unit System;
        interface
        type
          HResult = Integer;
          TDateTime = type Double;
          TGUID = packed record
            D1: Cardinal;
            D2: Word;
            D3: Word;
            D4: array[0..7] of Byte;
          end;
          TObject = class
          end;
          IInterface = interface
            ['{00000000-0000-0000-C000-000000000046}']
            function QueryInterface(const IID: TGUID; out Obj): HResult; stdcall;
            function _AddRef: Integer; stdcall;
            function _Release: Integer; stdcall;
          end;
          IUnknown = IInterface;
          IDispatch = interface(IUnknown)
            ['{00020400-0000-0000-C000-000000000046}']
            function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
          end;
        var Input, Output: Text;
        implementation
        end.
        """;

    internal static BindingResult BindWithDispatch(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    private static string[] Diagnostics(string text) => Format(BindWithDispatch(text).Diagnostics);

    /// <summary>The whole list in the failure message (a sequence assertion shows only the first difference).</summary>
    private static void AssertDiagnostics(string[] expected, string text)
    {
        var actual = Diagnostics(text);
        Assert.True(expected.SequenceEqual(actual), "actual diagnostics:\n" + string.Join("\n", actual));
    }

    private const string Declarations = """
        type
          TColor = (Red, Green, Blue);
          TSmall = 1..10;
          TMyDate = TDateTime;
          IPlain = interface procedure P; end;
          IChild = interface(IDispatch) end;
          IOtherDisp = dispinterface end;
          TProc = procedure;

        """;

    // ------------------------------------------------------------------ LANG-695

    /// <summary>Every row of the LANG-695 table: the type is accepted and has the descriptor code of ABI-205.</summary>
    [Theory]
    [Trait("Req", "LANG-695")]
    [InlineData("SmallInt", 2)]
    [InlineData("Integer", 3)]
    [InlineData("LongInt", 3)]
    [InlineData("TColor", 3)]
    [InlineData("Single", 4)]
    [InlineData("Double", 5)]
    [InlineData("Currency", 6)]
    [InlineData("TDateTime", 7)]
    [InlineData("TMyDate", 7)]
    [InlineData("Boolean", 11 + 0x200)]
    [InlineData("WordBool", 11)]
    [InlineData("IDispatch", 9)]
    [InlineData("IChild", 9)]
    [InlineData("IOtherDisp", 9)]
    [InlineData("IInterface", 13)]
    [InlineData("IUnknown", 13)]
    [InlineData("IPlain", 13)]
    [InlineData("ShortInt", 16)]
    [InlineData("Byte", 17)]
    [InlineData("TSmall", 17)]
    [InlineData("Word", 18)]
    [InlineData("Cardinal", 19)]
    [InlineData("Int64", 20)]
    [InlineData("UInt64", 21)]
    [InlineData("string", 0x100)]
    [InlineData("UnicodeString", 0x100)]
    public void AutomationTypeTable(string type, int code)
    {
        var result = BindWithDispatch(Declarations + $$"""
              IDisp = dispinterface
                procedure ByValue(A: {{type}}); dispid 1;
                procedure ByConst(const A: {{type}}); dispid 2;
                procedure ByVar(var A: {{type}}); dispid 3;
                procedure ByOut(out A: {{type}}); dispid 4;
                function Result_: {{type}}; dispid 5;
                property Prop: {{type}} dispid 6;
                property Indexed[I: {{type}}]: Integer dispid 7;
              end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var disp = result.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == "IDisp");
        Assert.True(disp.IsDispatchInterface);
        Assert.Equal(code, AutomationTypes.CodeOf(disp.Methods[0].Parameters[0].Type));
        Assert.Equal(code, AutomationTypes.CodeOf(disp.Methods[4].ReturnType!));
    }

    /// <summary>Types outside the table are E2658 at the type, wherever a member names them; a default value is E2660.</summary>
    [Theory]
    [Trait("Req", "LANG-695")]
    [InlineData("Char")]
    [InlineData("AnsiChar")]
    [InlineData("AnsiString")]
    [InlineData("ShortString")]
    [InlineData("ByteBool")]
    [InlineData("LongBool")]
    [InlineData("Pointer")]
    [InlineData("TObject")]
    [InlineData("TGUID")]
    [InlineData("TProc")]
    [InlineData("set of Byte")]
    [InlineData("array of Integer")]
    public void AutomationTypeTable_Rejected(string type)
    {
        var name = type switch
        {
            "set of Byte" => "set of Byte",
            "array of Integer" => "array of Integer",
            _ => type,
        };
        var diagnostics = Diagnostics(Declarations + $$"""
              IDisp = dispinterface
                procedure ByValue(A: {{type}}); dispid 1;
                property Prop: {{type}} dispid 6;
              end;
            begin
            end.
            """);
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.StartsWith("E2658(", d, StringComparison.Ordinal));
        Assert.Contains($"E2658(10,26): Type '{name}' is not an Automation type", diagnostics);
    }

    [Fact]
    [Trait("Req", "LANG-693")]
    [Trait("Req", "LANG-694")]
    [Trait("Req", "LANG-695")]
    public void DeclarationRules()
    {
        AssertDiagnostics(
            [
                "E2660(2,23): 'an ancestor' is not allowed in a dispatch interface",
                "E2659(5,32): Duplicate dispid 1 in 'IBad'",
                "E2660(6,31): 'overload' is not allowed in a dispatch interface",
                "E2660(7,31): 'stdcall' is not allowed in a dispatch interface",
                "E2660(8,25): 'read' is not allowed in a dispatch interface",
                "E2658(10,23): Type 'untyped parameter' is not an Automation type",
                "E2660(11,32): 'a default value' is not allowed in a dispatch interface",
                "E2014(12,27): Type of expression must be Integer",
                "E2660(13,5): 'a field' is not allowed in a dispatch interface",
                "E2660(14,5): 'class' is not allowed in a dispatch interface",
                "E2660(15,5): 'constructor' is not allowed in a dispatch interface",
                "E2004(17,15): Identifier redeclared: 'Twice'",
                "E2660(18,37): 'writeonly' is not allowed in a dispatch interface",
                "E2660(19,3): 'private' is not allowed in a dispatch interface",
                "E2661(23,17): A class cannot implement dispatch interface 'IGood'",
                "E2652(24,22): Interface type required",
            ],
            """
                type
                  IBad = dispinterface(IDispatch)
                    procedure NoId;
                    procedure One; dispid 1;
                    procedure OneAgain; dispid 1;
                    procedure Over; dispid 2; overload;
                    procedure Conv; dispid 3; stdcall;
                    property P: Integer read One dispid 4;
                    property NoPropId: Integer;
                    procedure Untyped(var X); dispid 5;
                    procedure Def(A: Integer = 1); dispid 6;
                    procedure Big; dispid 3000000000;
                    F: Integer;
                    class procedure Shared; dispid 9;
                    constructor Create; dispid 10;
                    procedure Twice; dispid 11;
                    procedure Twice; dispid 12;
                    property Both: Integer readonly writeonly dispid 13;
                  private
                    procedure Hidden; dispid 14;
                  end;
                  IGood = dispinterface procedure P; dispid 1; end;
                  TImpl = class(IGood) end;
                  IChild = interface(IGood) end;
                begin
                end.
                """);
    }

    [Fact]
    [Trait("Req", "LANG-694")]
    public void DispIdOnOrdinaryInterfaceMembers_IsKept()
    {
        var result = BindWithDispatch("""
            type
              IFoo = interface
                procedure P; dispid 7;
                function GetX: Integer; dispid -4;
                property X: Integer read GetX dispid 8;
              end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var foo = result.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == "IFoo");
        Assert.False(foo.IsDispatchInterface);
        Assert.Equal([7, -4], foo.Methods.Select(m => m.DispId));
        Assert.Equal(8, foo.Properties[0].DispId);
        Assert.Equal([3, 4], foo.Methods.Select(m => m.VmtSlot));              // still ordinary method-table members

        AssertDiagnostics(
            ["E2014(1,43): Type of expression must be Integer", "E2900(1,96): Feature not supported: readonly outside a dispatch interface"],
            ("type IFoo = interface procedure P; dispid 'x'; function G: Integer; property X: Integer read G readonly; end; begin end."));
    }

    /// <summary>
    /// LANG-694 amended (C-352): a member of a dispatch interface without <c>dispid</c> is numbered automatically, in declaration
    /// order, after the highest <c>dispid</c> written in that interface (1 when none is written; negative numbers such as -4 do not
    /// lower the start); explicit numbers are kept; a property's accessors carry its number; E2659 still reports duplicates among the
    /// explicit numbers, and an automatic number never collides with one. E2657 is no longer reported.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-694")]
    public void MemberWithoutDispid_GetsNumberAfterTheHighest()
    {
        var result = BindWithDispatch("""
            type
              IAuto = dispinterface
                procedure First;
                procedure Fixed; dispid 7;
                property Value: Integer;
                function Neg: Integer; dispid -4;
                property Item[I: Integer]: Integer dispid 0; default;
                procedure Last;
              end;
              INone = dispinterface
                procedure A;
                property B: Integer readonly;
              end;
              INeg = dispinterface
                function Items: IDispatch; dispid -4;
                procedure A;
              end;
            begin
            end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        InterfaceTypeSymbol Named(string name) => result.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == name);
        var auto = Named("IAuto");
        Assert.Equal([("First", 8), ("Fixed", 7), ("Neg", -4), ("Last", 10)], auto.Methods.Select(m => (m.Name, m.DispId ?? int.MinValue)));
        Assert.Equal([("Value", 9), ("Item", 0)], auto.Properties.Select(p => (p.Name, p.DispId ?? int.MinValue)));
        Assert.Equal(9, auto.Properties[0].ReadMethod!.DispId);
        Assert.Equal(9, auto.Properties[0].WriteMethod!.DispId);
        var none = Named("INone");
        Assert.Equal([1], none.Methods.Select(m => m.DispId));
        Assert.Equal(2, none.Properties[0].DispId);
        Assert.Null(none.Properties[0].WriteMethod);
        Assert.Equal(2, none.Properties[0].ReadMethod!.DispId);
        Assert.Equal([-4, 1], Named("INeg").Methods.Select(m => m.DispId));

        AssertDiagnostics(
            ["E2659(1,83): Duplicate dispid 1 in 'IDup'"],
            "type IDup = dispinterface procedure X; procedure Y; dispid 1; procedure Z; dispid 1; end; begin end.");
        var dup = BindWithDispatch("type IDup = dispinterface procedure X; procedure Y; dispid 1; procedure Z; end; begin end.");
        Assert.Empty(Format(dup.Diagnostics));
        Assert.Equal([2, 1, 3], dup.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single().Methods.Select(m => m.DispId));
    }

    /// <summary>
    /// LANG-694 (C-373): an automatic number above <c>High(Integer)</c> is E2012 at the first member that would need one (the
    /// later ones are not reported again and stay unnumbered); the members before it keep theirs, up to <c>High(Integer)</c> itself.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-694")]
    public void AutomaticDispidOverflow_IsAnError()
    {
        AssertDiagnostics(
            ["E2012(1,71): Constant value 2147483648 is out of range for type 'Integer'"],
            "type IMax = dispinterface procedure Top; dispid 2147483647; procedure A; procedure B; end; begin end.");
        AssertDiagnostics(
            ["E2012(1,84): Constant value 2147483648 is out of range for type 'Integer'"],
            "type IEdge = dispinterface procedure Top; dispid 2147483646; procedure A; property B: Integer; procedure C; end; begin end.");

        var result = BindWithDispatch("type IEdge = dispinterface procedure Top; dispid 2147483646; procedure A; property B: Integer; end; begin end.");
        var edge = result.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single();
        Assert.Equal([int.MaxValue - 1, int.MaxValue], edge.Methods.Select(m => m.DispId ?? 0));
        Assert.Null(edge.Properties[0].DispId);
        Assert.Null(edge.Properties[0].ReadMethod!.DispId);
    }

    // ------------------------------------------------------------------ LANG-696

    [Fact]
    [Trait("Req", "LANG-696")]
    public void ConversionsToAndFromIDispatch()
    {
        const string Types = Declarations + """
              IFooDisp = dispinterface ['{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D01}'] procedure P; dispid 1; end;
              INoGuid = dispinterface procedure P; dispid 1; end;
            var
              D: IFooDisp; O: IOtherDisp; N: INoGuid; Disp: IDispatch; Child: IChild; Unk: IInterface; Plain: IPlain; B: Boolean; Ptr: Pointer;

            """;

        // a dispatch-interface value is an IDispatch and an IInterface; IDispatch, its descendants and other dispatch interfaces
        // convert to any dispatch interface; nil, =, <>, Assigned and the pointer casts work as for interfaces
        Assert.Empty(Diagnostics(Types + """
            begin
              Disp := D;
              Unk := D;
              D := Disp;
              D := Child;
              D := O;
              O := D;
              D := nil;
              B := (D = nil) or (D <> O) or Assigned(D) or (Disp = D);
              D := Unk as IFooDisp;
              D := Plain as IFooDisp;
              Ptr := Pointer(D);
              D := IFooDisp(Ptr);
            end.
            """));

        var result = BindWithDispatch(Types + "begin end.");
        var disp = result.Unit.ImplementationMembers.OfType<InterfaceTypeSymbol>().Single(i => i.Name == "IFooDisp");
        Assert.Equal("IDispatch", disp.Parent!.Name);
        Assert.NotNull(disp.Guid);
        Assert.Equal(-1, disp.Methods[0].VmtSlot);                              // no method table of its own

        AssertDiagnostics(
            [
                "E2010(14,8): Incompatible types: 'IInterface' and 'IFooDisp'",
                "E2010(15,8): Incompatible types: 'IPlain' and 'IFooDisp'",
                "E2010(16,12): Incompatible types: 'IFooDisp' and 'IPlain'",
                "E2010(17,12): Incompatible types: 'IFooDisp' and 'IChild'",
                "E2640(18,15): Interface 'INoGuid' has no GUID; 'is'/'as' requires one",
            ],
            Types + """
                begin
                  D := Unk;
                  D := Plain;
                  Plain := D;
                  Child := D;
                  N := Unk as INoGuid;
                end.
                """);
    }

    // ------------------------------------------------------------------ LANG-697

    private const string Members = Declarations + """
          IInv = dispinterface
            procedure Proc(A: Integer; const S: string); dispid 1;
            function Func(var N: Integer; out S: string): string; dispid 2;
            property Count: Integer readonly dispid 3;
            property Sink: Integer writeonly dispid 4;
            property Value: Double dispid 5;
            property Item[const Key: string]: Integer dispid 0; default;
            property Cell[Row, Col: Integer]: string dispid 6;
            property Link: IDispatch dispid 7;
            property Other: IOtherDisp dispid 8;
          end;
          TEvent = procedure(A: Integer; const S: string) of object;
        var
          D: IInv; I: Integer; S: string; X: Double; Disp: IDispatch; E: TEvent; P: Pointer;

        """;

    /// <summary>The rows of LANG-697: the source form, the member's dispid, the <c>Invoke</c> flags and the number of arguments.</summary>
    [Theory]
    [Trait("Req", "LANG-697")]
    [InlineData("D.Proc(1, 'a')", 1, 1, 2)]
    [InlineData("S := D.Func(I, S)", 2, 1, 2)]
    [InlineData("D.Func(I, S)", 2, 1, 2)]
    [InlineData("I := D.Count", 3, 2, 0)]
    [InlineData("X := D.Value", 5, 2, 0)]
    [InlineData("I := D.Item['k']", 0, 2, 1)]
    [InlineData("I := D['k']", 0, 2, 1)]
    [InlineData("S := D.Cell[1, 2]", 6, 2, 2)]
    [InlineData("Disp := D.Link", 7, 2, 0)]
    [InlineData("D.Sink := 5", 4, 4, 1)]
    [InlineData("D.Value := 1.5", 5, 4, 1)]
    [InlineData("D.Item['k'] := 7", 0, 4, 2)]
    [InlineData("D['k'] := 7", 0, 4, 2)]
    [InlineData("D.Cell[1, 2] := 'x'", 6, 4, 3)]
    [InlineData("D.Link := Disp", 7, 8, 1)]
    [InlineData("D.Other := nil", 8, 8, 1)]
    public void MemberAccessFlags(string statement, int dispId, int flags, int arguments)
    {
        var result = BindWithDispatch(Members + "begin\n  " + statement + ";\nend.");
        Assert.Empty(Format(result.Diagnostics));
        var (method, count) = Assert.Single(DispatchCalls(result.Bodies[0]));
        Assert.Equal((dispId, flags, arguments), (method.DispId, method.DispatchFlags, count));
        Assert.True(method.ContainingInterface!.IsDispatchInterface);
    }

    [Fact]
    [Trait("Req", "LANG-697")]
    public void MemberAccessChecks()
    {
        AssertDiagnostics(
            [
                "E2110(24,3): Not enough actual parameters for 'Proc'",
                "E2111(25,18): Too many actual parameters for 'Proc'",
                "E2010(26,10): Incompatible types: 'Char' and 'Integer'",
                "E2112(27,10): Constant object cannot be passed as var parameter",
                "E2632(28,8): Cannot read a write-only property",
                "E2633(29,3): Cannot assign to a read-only property",
                "E2660(30,8): '@' is not allowed in a dispatch interface",
                "E2660(31,8): '@' is not allowed in a dispatch interface",
                "E2005(32,5): 'IInv' does not contain a member named 'Missing'",
            ],
            Members + """
                begin
                  D.Proc(1);
                  D.Proc(1, 'a', 2);
                  D.Proc('x', 'a');
                  D.Func(1, S);
                  I := D.Sink;
                  D.Count := 1;
                  E := D.Proc;
                  P := @D.Proc;
                  D.Missing;
                end.
                """);
    }

    /// <summary>Every call of a dispatch member in <paramref name="node"/>, with its argument count (the bound tree is walked by reflection).</summary>
    private static List<(MethodSymbol Method, int Arguments)> DispatchCalls(object? node)
    {
        var found = new List<(MethodSymbol, int)>();
        Walk(node, found, []);
        return found;

        static void Walk(object? value, List<(MethodSymbol, int)> found, HashSet<object> seen)
        {
            switch (value)
            {
                case null or string or Symbol or SyntaxNode or TypeSymbol:
                    return;
                case BoundCallExpression { Routine: MethodSymbol { DispatchFlags: not 0 } method } call when seen.Add(call):
                    found.Add((method, call.Arguments.Length));
                    break;
                case BoundCallStatement { Routine: MethodSymbol { DispatchFlags: not 0 } method } call when seen.Add(call):
                    found.Add((method, call.Arguments.Length));
                    break;
            }

            if (value is IEnumerable sequence)
            {
                foreach (var item in sequence)
                {
                    Walk(item, found, seen);
                }

                return;
            }

            if (value is not (BoundNode or BoundReceiver))
            {
                return;
            }

            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0 && property.Name is not ("Syntax" or "Type" or "ConstantValue"))
                {
                    Walk(property.GetValue(value), found, seen);
                }
            }
        }
    }
}
