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

    /// <summary>
    /// LANG-780 amendment (C-354): a class's type member scope includes its ancestors' nested types — <c>TDerived.TInner</c> in type
    /// and expression positions, unit-qualified, and the simple name in the derived class's member list and method bodies (LANG-772).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-780")]
    [Trait("Req", "LANG-772")]
    [Trait("Req", "LANG-774")]
    public void InheritedNestedType_ThroughDerivedClass()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBase = class
              public
                type
                  TInner = class
                    class function Id: Integer;
                  end;
                  TCell<T> = class
                    V: T;
                    class function Id: Integer;
                  end;
                const Limit = 7;
              protected
                type
                  THidden = class
                    class function Id: Integer;
                  end;
              end;
              TDerived = class(TBase)
              public
                F: TInner;
                G: THidden;
                function Make: TInner;
              end;
              TMore = class(TDerived)
              end;
            class function TBase.TInner.Id: Integer; begin Result := 1 end;
            class function TBase.TCell<T>.Id: Integer; begin Result := SizeOf(T) end;
            class function TBase.THidden.Id: Integer; begin Result := 2 end;
            function TDerived.Make: TInner;
            var H: THidden;
            begin
              H := G;
              Result := nil;
              if THidden.Id + TInner.Id = 3 then
                Result := F;
            end;
            var
              X: TDerived.TInner;
              Y: P.TDerived.TInner;
              Z: TMore.TInner;
              C: TDerived.TCell<Integer>;
              D: TBase.TCell<Integer>;
              I: Integer;
            begin
              I := TDerived.TInner.Id + P.TMore.TInner.Id + TDerived.TCell<Integer>.Id + TMore.Limit;
              X := nil;
              Z := X;
              Y := Z;
              D := C;
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var inner = Nested<ClassTypeSymbol>(Lookup<ClassTypeSymbol>(result, "TBase"), "TInner");
        var derived = Lookup<ClassTypeSymbol>(result, "TDerived");
        Assert.Same(inner, Lookup<VariableSymbol>(result, "X").Type);
        Assert.Same(inner, Lookup<VariableSymbol>(result, "Y").Type);
        Assert.Same(inner, Lookup<VariableSymbol>(result, "Z").Type);
        Assert.Same(inner, derived.Fields.Single(f => f.Name == "F").Type);
        Assert.Same(inner, derived.Methods.Single(m => m.Name == "Make").ReturnType);
        Assert.Same(Lookup<VariableSymbol>(result, "D").Type, Lookup<VariableSymbol>(result, "C").Type);
        Assert.Null(TypeMembers.NestedType(derived, "TInner"));                          // still declared only in TBase
    }

    /// <summary>LANG-780 amendment, LANG-774 (C-354): an inherited nested type keeps the visibility of the section that declares it.</summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    public void InheritedNestedType_Visibility_E2610() =>
        Assert.Equal(
        [
            "E2610(15,15): Cannot access strict protected member 'THidden' of 'TBase'",
            "E2610(17,17): Cannot access strict protected member 'THidden' of 'TBase'",
        ], BindDiagnostics("""
            program P;
            type
              TBase = class
              strict protected
                type
                  THidden = class
                    class function Id: Integer;
                  end;
              end;
              TDerived = class(TBase)
              end;
            class function TBase.THidden.Id: Integer; begin Result := 1 end;
            var
              I: Integer;
              X: TDerived.THidden;
            begin
              I := TDerived.THidden.Id;
            end.
            """));

    /// <summary>
    /// LANG-774, LANG-780 item 6 (C-356): a nested type named as the argument of a type-taking intrinsic (<c>SizeOf</c>, <c>TypeInfo</c>,
    /// <c>Default</c>, <c>High</c>, <c>Low</c>) obeys the visibility of its section like every other use — in a constant and in a statement,
    /// declared on the named type or inherited; E2610 names the declaring type. Code of the declaring type may name it.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-780")]
    public void IntrinsicArgument_NestedTypeVisibility_E2610()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", """
                program P;
                uses U;
                type
                  TLocal = class
                  strict private
                    type TLS = (lA, lB);
                  public
                    type TOk = record A, B: Integer; end;
                  end;
                  TLocalD = class(TLocal)
                  end;
                const
                  C1 = SizeOf(TOuter.TSP);
                  C2 = Ord(High(TDerived.TPriv));
                  C3 = High(TDerived.TSProt);
                  C4 = Ord(Low(TLocalD.TLS));
                  C5 = SizeOf(TLocal.TOk) + SizeOf(TDerived.TOpen);
                var
                  N: Integer;
                  Q: Pointer;
                begin
                  N := SizeOf(TDerived.TSP);
                  Q := TypeInfo(TOuter.TPriv);
                  Q := TypeInfo(TDerived.TSProt);
                  N := Default(TOuter.TSProt);
                  N := Ord(Low(TLocal.TLS));
                  N := Ord(High(TOuter.TPriv));
                  N := SizeOf(TLocalD.TOk) + Ord(High(TOuter.TOpen)) + Default(TDerived.TOpen);
                end.
                """)
            .Add(@"C:\p\U.pas", """
                unit U;
                interface
                type
                  TOuter = class
                  private
                    type TPriv = (pA, pB, pC);
                  strict private
                    type TSP = record A: Int64; end;
                  strict protected
                    type TSProt = 0..9;
                  public
                    type TOpen = 0..3;
                    procedure Use;
                  end;
                  TDerived = class(TOuter)
                  end;
                implementation
                var
                  N: Integer;
                  Q: Pointer;
                procedure TOuter.Use;
                const
                  K = SizeOf(TOuter.TSP) + Ord(High(TPriv)) + Low(TOuter.TSProt);
                begin
                  N := SizeOf(TOuter.TSP) + Default(TOuter.TSProt) + Ord(Low(TDerived.TPriv)) + K;
                  Q := TypeInfo(TOuter.TPriv);
                end;
                begin
                  N := SizeOf(TDerived.TPriv);
                  Q := TypeInfo(TOuter.TPriv);
                end.
                """)
            .AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "P.pas(13,22): E2610 Cannot access strict private member 'TSP' of 'TOuter'",
            "P.pas(14,26): E2610 Cannot access private member 'TPriv' of 'TOuter'",
            "P.pas(15,22): E2610 Cannot access strict protected member 'TSProt' of 'TOuter'",
            "P.pas(16,24): E2610 Cannot access strict private member 'TLS' of 'TLocal'",
            "P.pas(22,24): E2610 Cannot access strict private member 'TSP' of 'TOuter'",
            "P.pas(23,24): E2610 Cannot access private member 'TPriv' of 'TOuter'",
            "P.pas(24,26): E2610 Cannot access strict protected member 'TSProt' of 'TOuter'",
            "P.pas(25,23): E2610 Cannot access strict protected member 'TSProt' of 'TOuter'",
            "P.pas(26,23): E2610 Cannot access strict private member 'TLS' of 'TLocal'",
            "P.pas(27,24): E2610 Cannot access private member 'TPriv' of 'TOuter'",
        ], result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"));
    }

    /// <summary>
    /// LANG-774, LANG-785 (C-357): a typecast whose callee names a nested type obeys its visibility, in a constant and in a statement,
    /// own and inherited; code of the declaring type may cast to it.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-785")]
    public void Typecast_NestedTypeVisibility_E2610() =>
        Assert.Equal(
        [
            "E2610(14,14): Cannot access strict private member 'TEn' of 'TOuter'",
            "E2610(15,16): Cannot access strict protected member 'TSm' of 'TOuter'",
            "E2610(25,19): Cannot access strict private member 'TEn' of 'TOuter'",
            "E2610(26,17): Cannot access strict protected member 'TSm' of 'TOuter'",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
              strict private
                type TEn = (eA, eB);
              strict protected
                type TSm = 0..9;
              public
                procedure Use;
              end;
              TDerived = class(TOuter)
              end;
            const
              C = TOuter.TEn(1);
              D = TDerived.TSm(2);
            var
              N: Integer;
            procedure TOuter.Use;
            const
              K = TOuter.TEn(1);
            begin
              N := Ord(TOuter.TEn(N)) + Ord(K) + TDerived.TSm(3);
            end;
            begin
              N := Ord(TOuter.TEn(N));
              N := TDerived.TSm(N);
            end.
            """).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));

    /// <summary>
    /// LANG-774, C-311 (C-359): a nested type alias keeps the visibility of its section — as a type, as the argument of a
    /// type-taking intrinsic and as a typecast callee, in constants and statements, own and inherited; E2610 names the declaring
    /// type. Code of the declaring type may name it; public aliases stay usable.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-770")]
    public void NestedAlias_Visibility_E2610() =>
        Assert.Equal(
        [
            "E2610(15,22): Cannot access strict private member 'TSmall' of 'TOuter'",
            "E2610(16,17): Cannot access strict protected member 'TCount' of 'TOuter'",
            "E2610(17,20): Cannot access strict private member 'TSmall' of 'TOuter'",
            "E2610(20,13): Cannot access strict private member 'TSmall' of 'TOuter'",
            "E2610(21,15): Cannot access strict protected member 'TCount' of 'TOuter'",
            "E2610(33,22): Cannot access strict private member 'TSmall' of 'TOuter'",
            "E2610(34,15): Cannot access strict private member 'TSmall' of 'TOuter'",
            "E2610(35,25): Cannot access strict protected member 'TCount' of 'TOuter'",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
              strict private
                type TSmall = Byte;
              strict protected
                type TCount = Integer;
              public
                type TOpen = Word;
                procedure Use;
              end;
              TDerived = class(TOuter)
              end;
            const
              C1 = SizeOf(TOuter.TSmall);
              C2 = TDerived.TCount(7);
              C3 = High(TOuter.TSmall) - 1;
              C4 = SizeOf(TOuter.TOpen) + TDerived.TOpen(1);
            var
              A: TOuter.TSmall;
              B: TDerived.TCount;
              N: Integer;
            procedure TOuter.Use;
            const
              K = SizeOf(TOuter.TSmall) + TOuter.TCount(2) + High(TDerived.TSmall);
            var
              L: TOuter.TSmall;
            begin
              L := TOuter.TSmall(N) + K;
              N := SizeOf(TDerived.TCount) + Default(TOuter.TSmall);
            end;
            begin
              N := SizeOf(TOuter.TSmall);
              N := TOuter.TSmall(N);
              N := Default(TDerived.TCount);
              N := TOuter.TOpen(N) + SizeOf(TDerived.TOpen) + High(TOuter.TOpen);
            end.
            """).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));

    /// <summary>
    /// LANG-774, LANG-780 item 2 (C-360): every segment of a nested path obeys the visibility of its section, not only the last —
    /// <c>TOuter.TSecret.TInner</c> and <c>TOuter.TSecret.Limit</c> give one E2610 at <c>TSecret</c> as a type, a constant (also inside
    /// a constant expression), a typecast callee and an intrinsic argument, in constants and statements, own and inherited.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-780")]
    public void InnerSegment_Visibility_E2610() =>
        Assert.Equal(
        [
            "E2610(23,15): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(24,22): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(25,15): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(26,15): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(26,51): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(28,13): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(29,15): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(42,15): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(43,24): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(44,19): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(45,24): Cannot access strict private member 'TSecret' of 'TOuter'",
            "E2610(46,24): Cannot access strict private member 'TSecret' of 'TOuter'",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
              strict private
                type
                  TSecret = class
                  public
                    type
                      TInner = (iA, iB);
                      TDeep = class
                      public
                        type TX = Byte;
                      end;
                    const
                      Limit = 5;
                  end;
              public
                procedure Use;
              end;
              TDerived = class(TOuter)
              end;
            const
              C1 = TOuter.TSecret.Limit;
              C2 = SizeOf(TOuter.TSecret.TInner) + 1;
              C3 = TOuter.TSecret.TInner(1);
              C4 = TOuter.TSecret.Limit * 2 + SizeOf(TDerived.TSecret.TDeep.TX);
            var
              X: TOuter.TSecret.TInner;
              Y: TDerived.TSecret.TDeep.TX;
              N: Integer;
              Q: Pointer;
            procedure TOuter.Use;
            const
              K = TOuter.TSecret.Limit + Ord(High(TOuter.TSecret.TInner)) + SizeOf(TOuter.TSecret.TDeep.TX);
            var
              L: TOuter.TSecret.TInner;
            begin
              L := TOuter.TSecret.TInner(K);
              N := TOuter.TSecret.Limit + SizeOf(TDerived.TSecret.TDeep.TX);
            end;
            begin
              N := TOuter.TSecret.Limit;
              N := Ord(High(TOuter.TSecret.TInner));
              N := Ord(TOuter.TSecret.TInner(N));
              N := SizeOf(TDerived.TSecret.TDeep.TX);
              Q := TypeInfo(TOuter.TSecret.TInner);
            end.
            """).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));

    /// <summary>
    /// LANG-774, LANG-770 (C-361): a nested constant keeps the visibility of its section — <c>TOuter.Limit</c> from outside is E2610
    /// in constant expressions, type bounds and statements, own and inherited (naming the declaring type), also by simple name in a
    /// descendant; code of the declaring type may name it, a descendant its strict protected ones, public constants stay usable.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-770")]
    public void NestedConstant_Visibility_E2610() =>
        Assert.Equal(
        [
            "E2610(17,15): Cannot access strict private member 'Limit' of 'TOuter'",
            "E2610(18,15): Cannot access strict private member 'Limit' of 'TOuter'",
            "E2610(19,17): Cannot access strict protected member 'Step' of 'TOuter'",
            "E2610(22,22): Cannot access strict private member 'Limit' of 'TOuter'",
            "E2610(32,31): Cannot access strict private member 'Limit' of 'TOuter'",
            "E2610(36,15): Cannot access strict private member 'Limit' of 'TOuter'",
            "E2610(37,17): Cannot access strict protected member 'Step' of 'TOuter'",
            "E2610(38,19): Cannot access strict private member 'Limit' of 'TOuter'",
        ], BindDiagnostics("""
            program P;
            type
              TOuter = class
              strict private
                const Limit = 3;
              strict protected
                const Step = 2;
              public
                const Open = 10;
                procedure Use;
              end;
              TDerived = class(TOuter)
              public
                procedure Go;
              end;
            const
              C1 = TOuter.Limit;
              C2 = TOuter.Limit * 2;
              C3 = TDerived.Step + 1;
              C4 = TOuter.Open + TDerived.Open;
            var
              A: array[0..TOuter.Limit] of Byte;
              N: Integer;
            procedure TOuter.Use;
            const
              K = TOuter.Limit + TOuter.Step + TDerived.Limit + Step + Limit;
            begin
              N := TOuter.Limit + TDerived.Step + K + Step + Limit;
            end;
            procedure TDerived.Go;
            begin
              N := Step + TDerived.Step + Limit;
            end;
            begin
              N := C4;
              N := TOuter.Limit;
              N := TDerived.Step + TOuter.Open;
              N := Ord(TOuter.Limit = 3);
            end.
            """).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));

    /// <summary>
    /// LANG-774 (C-361): nested types, aliases and constants of a record or class helper obey the visibility of their section like
    /// those of classes and records — as a type, an intrinsic argument and a typecast callee in constants and statements, a constant
    /// through the helped type; code of the helper may name them, public ones stay usable.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-774")]
    [Trait("Req", "LANG-750")]
    public void HelperNestedType_Visibility_E2610() =>
        Assert.Equal(
        [
            "E2610(20,26): Cannot access strict private member 'TSmall' of 'TIntHelper'",
            "E2610(21,19): Cannot access strict protected member 'TWide' of 'TIntHelper'",
            "E2610(23,17): Cannot access strict private member 'TSmall' of 'TIntHelper'",
            "E2610(24,17): Cannot access strict private member 'TBox' of 'TObjHelper'",
            "E2610(35,19): Cannot access strict protected member 'TWide' of 'TIntHelper'",
            "E2610(36,26): Cannot access strict private member 'TSmall' of 'TIntHelper'",
            "E2610(37,16): Cannot access strict private member 'Limit' of 'TIntHelper'",
        ], BindDiagnostics("""
            program P;
            type
              TIntHelper = record helper for Integer
              strict private
                type TSmall = (sA, sB);
                const Limit = 3;
              strict protected
                type TWide = Int64;
              public
                type TOpen = Word;
                const Open = 4;
                function Twice: Integer;
              end;
              TOwn = class end;
              TObjHelper = class helper for TOwn
              strict private
                type TBox = record X: Integer; end;
              end;
            const
              C1 = SizeOf(TIntHelper.TSmall);
              C2 = TIntHelper.TWide(7);
            var
              A: TIntHelper.TSmall;
              B: TObjHelper.TBox;
              O: TIntHelper.TOpen;
              N: Integer;
            function TIntHelper.Twice: Integer;
            var
              L: TIntHelper.TSmall;
            begin
              L := TSmall(Self);
              Result := Ord(L) + SizeOf(TIntHelper.TWide) + Integer.Limit + Limit;
            end;
            begin
              N := TIntHelper.TWide(N);
              N := SizeOf(TIntHelper.TSmall);
              N := Integer.Limit;
              N := SizeOf(O) + SizeOf(TIntHelper.TOpen) + Integer.Open;
            end.
            """).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));

    /// <summary>LANG-780 amendment (C-354): through a generic instance the inherited name is the ancestor instance's nested type.</summary>
    [Fact]
    [Trait("Req", "LANG-780")]
    [Trait("Req", "LANG-775")]
    public void InheritedNestedType_ThroughGenericInstance()
    {
        var (result, _) = Bind("""
            program P;
            type
              TList<T> = class
              public
                type
                  TEnumerator = class
                    Current: T;
                    class function Id: Integer;
                  end;
                function GetEnumerator: TEnumerator;
              end;
              TObjectList<T: class> = class(TList<T>)
              public
                function First: TEnumerator;
              end;
              TFoo = class
              end;
              TIntList = class(TList<Integer>)
              end;
            class function TList<T>.TEnumerator.Id: Integer; begin Result := SizeOf(T) end;
            function TList<T>.GetEnumerator: TEnumerator; begin Result := nil end;
            function TObjectList<T>.First: TEnumerator; begin Result := GetEnumerator end;
            var
              A: TObjectList<TFoo>.TEnumerator;
              B: TList<TFoo>.TEnumerator;
              C: TIntList.TEnumerator;
              D: TList<Integer>.TEnumerator;
              E: TList<string>.TEnumerator;
              I: Integer;
            begin
              I := TObjectList<TFoo>.TEnumerator.Id + TIntList.TEnumerator.Id;
              A := nil;
              B := A;
              C := nil;
              D := C;
              A.Current := nil;
              D.Current := 1;
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Lookup<VariableSymbol>(result, "A").Type;
        var c = Lookup<VariableSymbol>(result, "C").Type;
        Assert.Same(a, Lookup<VariableSymbol>(result, "B").Type);
        Assert.Same(c, Lookup<VariableSymbol>(result, "D").Type);
        Assert.NotSame(a, c);
        Assert.NotSame(c, Lookup<VariableSymbol>(result, "E").Type);
        Assert.Equal("TList<TFoo>.TEnumerator", TypeMembers.DisplayName(a));
    }

    /// <summary>LANG-780 amendment (C-354): a nested type of the class itself hides a same-named inherited one.</summary>
    [Fact]
    [Trait("Req", "LANG-780")]
    [Trait("Req", "LANG-772")]
    public void InheritedNestedType_HiddenByOwn()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBase = class
              public
                type
                  TInner = class
                    A: Integer;
                  end;
              end;
              TDerived = class(TBase)
              public
                type
                  TInner = class
                    B: string;
                    class function Id: Integer;
                  end;
              public
                F: TInner;
                function Make: TInner;
              end;
              TMore = class(TDerived)
              end;
            class function TDerived.TInner.Id: Integer; begin Result := 2 end;
            function TDerived.Make: TInner; begin Result := F; Result.B := 'x' end;
            var
              X: TDerived.TInner;
              Y: TMore.TInner;
              Z: TBase.TInner;
              I: Integer;
            begin
              I := TMore.TInner.Id;
              X := Y;
              X.B := 'y';
              Z.A := 1;
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var baseInner = Nested<ClassTypeSymbol>(Lookup<ClassTypeSymbol>(result, "TBase"), "TInner");
        var derived = Lookup<ClassTypeSymbol>(result, "TDerived");
        var own = Nested<ClassTypeSymbol>(derived, "TInner");
        Assert.NotSame(baseInner, own);
        Assert.Same(own, Lookup<VariableSymbol>(result, "X").Type);
        Assert.Same(own, Lookup<VariableSymbol>(result, "Y").Type);
        Assert.Same(baseInner, Lookup<VariableSymbol>(result, "Z").Type);
        Assert.Same(own, derived.Fields.Single(f => f.Name == "F").Type);
        Assert.Same(own, derived.Methods.Single(m => m.Name == "Make").ReturnType);
    }
}
