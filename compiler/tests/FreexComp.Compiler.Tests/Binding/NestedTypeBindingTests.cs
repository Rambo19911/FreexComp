using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-344: nested class and interface types of classes and records (generics.md LANG-771…777) — declaration and forward completion,
/// lookup order, enclosing members, visibility, ancestors and method implementations by type path.
/// </summary>
[Trait("Phase", "8")]
public sealed class NestedTypeBindingTests
{
    private static T Lookup<T>(BindingResult result, string name) => Assert.IsType<T>(result.Declarations.Scope.Lookup(name));

    private static T Nested<T>(TypeSymbol outer, string name) => Assert.IsType<T>(TypeMembers.NestedType(outer, name));

    [Fact]
    [Trait("Req", "LANG-771")]
    [Trait("Req", "LANG-775")]
    public void NestedClassAndInterface_AreTypesOfTheEnclosingType()
    {
        var (result, _) = Bind("""
            program P;
            type
              TOuter = class
              public
                type
                  IInner = interface
                    function Get: Integer;
                  end;
                  TInner = class
                    function Get: Integer;
                    function Self2: IInner;
                  end;
              end;
              TRec = record
                type
                  TBox = class
                    V: Integer;
                  end;
              end;
            function TOuter.TInner.Get: Integer; begin Result := 1 end;
            function TOuter.TInner.Self2: IInner; begin Result := nil end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var outer = Lookup<ClassTypeSymbol>(result, "TOuter");
        var inner = Nested<ClassTypeSymbol>(outer, "TInner");
        var intf = Nested<InterfaceTypeSymbol>(outer, "IInner");
        var box = Nested<ClassTypeSymbol>(Lookup<RecordTypeSymbol>(result, "TRec"), "TBox");
        Assert.Same(outer, inner.ContainingSymbol);
        Assert.Same(outer, intf.ContainingSymbol);
        Assert.Equal("TInner", inner.Name);                                          // ClassName stays the simple name
        Assert.Equal("P.TOuter.TInner", inner.QualifiedName);                         // CMP-1390
        Assert.Equal("TOuter.TInner", TypeMembers.DisplayName(inner));               // LANG-775
        Assert.Equal("TRec.TBox", TypeMembers.DisplayName(box));
        Assert.Same(intf, inner.Methods.Single(m => m.Name == "Self2").ReturnType);
        Assert.NotNull(inner.Methods.Single(m => m.Name == "Get").Definition);
        Assert.Null(result.Declarations.Scope.Lookup("TInner"));                     // not a unit-level name
        Assert.Contains(inner, DeclarationBinder.ClassesOf(result.Declarations.Unit));
        Assert.Contains(box, DeclarationBinder.ClassesOf(result.Declarations.Unit));
    }

    [Fact]
    [Trait("Req", "LANG-771")]
    public void NestedForwardClass_CompletedInTheSameSection()
    {
        var (result, _) = Bind("""
            program P;
            type
              TOuter = class
              public
                type
                  TB = class;
                  TA = class F: TB; end;
                  TB = class G: TA; end;
              end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var outer = Lookup<ClassTypeSymbol>(result, "TOuter");
        var a = Nested<ClassTypeSymbol>(outer, "TA");
        var b = Nested<ClassTypeSymbol>(outer, "TB");
        Assert.Same(b, a.Fields[0].Type);
        Assert.Same(a, b.Fields[0].Type);
        Assert.False(b.IsForward);
        Assert.Equal(2, b.Locations.Length);
    }

    [Fact]
    [Trait("Req", "LANG-771")]
    public void NestedForwardClass_Unsatisfied_E2015() =>
        Assert.Equal(["E2015(1,44): Unsatisfied forward declaration of type 'TB'"],
            BindDiagnostics("program P; type TOuter = class public type TB = class; end; begin end."));

    /// <summary>LANG-772: the nested type's members, then the enclosing type's (with its ancestors), then the unit's names.</summary>
    [Fact]
    [Trait("Req", "LANG-772")]
    public void LookupOrder_NestedThenEnclosingThenUnit()
    {
        var (result, _) = Bind("""
            program P;
            const A = 1; B = 1; C = 1; D = 1;
            type
              TBase = class
              public
                const D = 4;
              end;
              TOuter = class(TBase)
              public
                const B = 2; C = 2;
                type
                  TInner = class
                  public
                    const C = 3;
                    const S = A * 1000 + B * 100 + C * 10 + D;
                    function Sum: Integer;
                  end;
              end;
            function TOuter.TInner.Sum: Integer; begin Result := A * 1000 + B * 100 + C * 10 + D end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var inner = Nested<ClassTypeSymbol>(Lookup<ClassTypeSymbol>(result, "TOuter"), "TInner");
        var s = Assert.IsType<ConstantSymbol>(inner.NestedMembers.Single(m => m.Name == "S"));
        Assert.Equal("1234", Convert.ToString(s.Value!.Value, System.Globalization.CultureInfo.InvariantCulture));   // member list
        var sum = BoundDump.Of(result.Routines.Single(r => r.Routine.Name == "Sum").Body);
        Assert.Contains("1234", sum);                                                    // method body
    }

    [Fact]
    [Trait("Req", "LANG-773")]
    public void EnclosingInstanceMember_E2707() =>
        Assert.Equal(
        [
            "E2707(9,52): Instance member 'FValue' of 'TOuter' cannot be used without an object",
            "E2707(10,52): Instance member 'Twice' of 'TOuter' cannot be used without an object",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
                FValue: Integer;
                function Twice: Integer;
                type TInner = class function A: Integer; function B: Integer; end;
              end;
            function TOuter.Twice: Integer; begin Result := 2 * FValue end;
            function TOuter.TInner.A: Integer; begin Result := FValue end;
            function TOuter.TInner.B: Integer; begin Result := Twice end;
            begin end.
            """));

    /// <summary>LANG-773: through an object reference every member is usable; class members of the enclosing type by simple name.</summary>
    [Fact]
    [Trait("Req", "LANG-773")]
    public void EnclosingInstanceMemberThroughReference_Allowed()
    {
        var (result, _) = Bind("""
            program P;
            type
              TOuter = class
                FValue: Integer;
                class var Count: Integer;
                class function Kind: Integer;
                function Twice: Integer;
                type TInner = class Owner: TOuter; function A: Integer; end;
              end;
            class function TOuter.Kind: Integer; begin Result := 7 end;
            function TOuter.Twice: Integer; begin Result := 2 * FValue end;
            function TOuter.TInner.A: Integer; begin Result := Owner.FValue + Owner.Twice + Count + Kind end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = BoundDump.Of(result.Routines.Single(r => r.Routine.Name == "A").Body);
        Assert.Contains("TOuter", a);                                                   // Kind is called through TOuter
    }

    [Fact]
    [Trait("Req", "LANG-774")]
    public void StrictPrivateOfEnclosing_VisibleInNested() =>
        Assert.Empty(BindDiagnostics("""
            program P;
            type
              TOuter = class
              strict private
                FSecret: Integer;
                class var FCount: Integer;
                procedure Hidden;
              strict protected
                FShared: Integer;
              public
                type TInner = class function A(O: TOuter): Integer; end;
              end;
            procedure TOuter.Hidden; begin end;
            function TOuter.TInner.A(O: TOuter): Integer; begin O.Hidden; Result := O.FSecret + O.FShared + FCount end;
            begin end.
            """));

    [Fact]
    [Trait("Req", "LANG-774")]
    public void StrictPrivateOfNested_NotVisibleInEnclosing_E2610() =>
        Assert.Equal(["E2610(10,61): Cannot access strict private member 'FSecret' of 'TOuter.TInner'"], BindDiagnostics("""
            program P;
            type
              TOuter = class
                type TInner = class
                strict private
                  FSecret: Integer;
                end;
                function Peek(I: TInner): Integer;
              end;
            function TOuter.Peek(I: TInner): Integer; begin Result := I.FSecret end;
            begin end.
            """));

    [Fact]
    [Trait("Req", "LANG-774")]
    public void PrivateNestedType_FromAnotherUnit_E2610()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", "program P;\nuses U;\nvar\n  A: TOuter.TOpen;\n  B: TOuter.TSecret;\nbegin\n  TOuter.TSecret.Create.Free;\nend.\n")
            .Add(@"C:\p\U.pas", """
                unit U;
                interface
                type
                  TOuter = class
                  private
                    type TSecret = class end;
                  public
                    type TOpen = class end;
                    procedure Use;
                  end;
                implementation
                procedure TOuter.Use; var S: TOuter.TSecret; begin S := TSecret.Create; S.Free end;
                var X: TOuter.TSecret;
                end.
                """)
            .AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "P.pas(5,13): E2610 Cannot access private member 'TSecret' of 'TOuter'",
            "P.pas(7,10): E2610 Cannot access private member 'TSecret' of 'TOuter'",
        ], result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"));
    }

    [Fact]
    [Trait("Req", "LANG-777")]
    public void NestedClassAncestorIsEnclosing_E2007() =>
        Assert.Equal(
        [
            "E2007(5,24): Type 'TOuter' is not yet completely defined",
            "E2007(7,22): Type 'TLater' is not yet completely defined",
            "E2007(10,28): Type 'TMid' is not yet completely defined",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
                type
                  TSelfish = class(TOuter) end;
                  TLater = class;
                  TEarly = class(TLater) end;
                  TLater = class end;
                  TMid = class
                    type TDeep = class(TMid) end;
                  end;
                  TFine = class(TEarly) Owner: TOuter; end;
              end;
            begin end.
            """));

    [Fact]
    [Trait("Req", "LANG-777")]
    public void NestedRecordFieldOfEnclosingRecord_E2007() =>
        Assert.Equal(["E2007(1,49): Type 'TR' is not yet completely defined"],
            BindDiagnostics("program P; type TR = record type TN = record F: TR; end; end; begin end."));

    [Fact]
    [Trait("Req", "LANG-776")]
    public void MethodImplementationPath_Unknown_E2606() =>
        Assert.Equal(
        [
            "E2606(4,18): 'TMissing' is not a member of class 'TOuter'",
            "E2606(5,25): 'Nope' is not a member of class 'TOuter.TInner'",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class type TInner = class procedure Run; end; end;
            procedure TOuter.TMissing.Run; begin end;
            procedure TOuter.TInner.Nope; begin end;
            procedure TOuter.TInner.Run; begin end;
            begin end.
            """));
}
