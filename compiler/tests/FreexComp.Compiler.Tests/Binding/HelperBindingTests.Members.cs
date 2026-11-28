using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Tests.Units;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

// LANG-990 (c), (d): the remaining members of helpers (phase8/helpers-completion.md §15.4; card F10-027), C-441 — nested class and
// interface types, property accessors that name members of the helped type.
public sealed partial class HelperBindingTests
{
    // ------------------------------------------------------------------ LANG-990 (c): nested class and interface types

    /// <summary>
    /// LANG-990 (c), LANG-771: a class helper and a record helper declare nested class and interface types as a record does — the
    /// methods of a nested class are implemented as <c>THelper.TInner.Method</c>, the helper's methods create and use them, code
    /// elsewhere names them through the helper; the visibility of the section applies (E2610).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-990")]
    public void NestedClassAndInterfaceTypes()
    {
        var (result, _) = Bind("""
            program P;
            type
              TFoo = class
                F: Integer;
              end;
              TFooH = class helper for TFoo
              public
                type
                  IShape = interface
                    function Area: Integer;
                  end;
                  TInner = class
                    N: Integer;
                    function Value: Integer;
                  end;
              strict private
                type
                  TSecret = class
                  end;
              public
                function Make: TInner;
                function Measure(S: IShape): Integer;
                function Hidden: Boolean;
              end;
              TIntH = record helper for Integer
                type
                  TBox = class
                    V: Integer;
                    function Get: Integer;
                  end;
                function Boxed: TBox;
              end;
            function TFooH.TInner.Value: Integer; begin Result := N * 2 end;
            function TFooH.Make: TInner; begin Result := nil; if F > 0 then Result.N := F end;
            function TFooH.Measure(S: IShape): Integer; begin Result := S.Area end;
            function TFooH.Hidden: Boolean; var X: TSecret; begin X := nil; Result := X = nil end;
            function TIntH.TBox.Get: Integer; begin Result := V end;
            function TIntH.Boxed: TBox; begin Result := nil; if Self > 0 then Result.V := Self end;
            var
              O: TFoo;
              I: TFooH.TInner;
              S: TFooH.IShape;
              B: TIntH.TBox;
              N: Integer;
            begin
              I := O.Make;
              N := I.Value + O.Measure(S) + 5.Boxed.Get;
              B := TIntH.TBox(nil);
            end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var helper = Assert.IsType<HelperTypeSymbol>(result.Declarations.Scope.Lookup("TFooH"));
        var inner = Assert.IsType<ClassTypeSymbol>(TypeMembers.NestedType(helper, "TInner"));
        var shape = Assert.IsType<InterfaceTypeSymbol>(TypeMembers.NestedType(helper, "IShape"));
        Assert.Same(helper, inner.ContainingSymbol);
        Assert.Same(helper, shape.ContainingSymbol);
        Assert.Equal(Visibility.StrictPrivate, TypeMembers.NestedType(helper, "TSecret")!.NestedVisibility);
        Assert.NotNull(inner.Methods.Single().Definition);
        Assert.Equal("TFooH.TInner", TypeMembers.DisplayName(inner));
        var box = Assert.IsType<ClassTypeSymbol>(TypeMembers.NestedType(Assert.IsType<HelperTypeSymbol>(result.Declarations.Scope.Lookup("TIntH")), "TBox"));
        Assert.Same(box, Assert.IsType<VariableSymbol>(result.Declarations.Scope.Lookup("B")).Type);
        Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, inner.Methods.Single()));
        Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, box.Methods.Single()));

        // the visibility of the section
        Assert.Equal(["E2610(6,12): Cannot access strict private member 'TSecret' of 'TFooH'"], BindDiagnostics("""
            program P;
            type
              TFoo = class end;
              TFooH = class helper for TFoo strict private type TSecret = class end; end;
            var
              X: TFooH.TSecret;
            begin
            end.
            """));

        // through the .pcu: a program that names, creates and calls a nested class of a helper of another unit gets the same IR
        PcuImportTests.AssertSameIrAgainstPcu(NestedTypeUnit, """
            program P;
            uses U;
            var
              O: TFoo;
              I: TFooH.TInner;
              S: TFooH.IShape;
            begin
              O := TFoo.Create;
              I := O.Make;
              S := nil;
              Writeln(I.Value, TFooH.TInner.Create.Value, S = nil);
            end.
            """);
    }

    private const string NestedTypeUnit = """
        unit U;
        interface
        type
          TFoo = class
            F: Integer;
          end;
          TFooH = class helper for TFoo
          public
            type
              IShape = interface
                function Area: Integer;
              end;
              TInner = class
                N: Integer;
                function Value: Integer;
              end;
            function Make: TInner;
          end;
        implementation
        function TFooH.TInner.Value: Integer; begin Result := N * 2 end;
        function TFooH.Make: TInner; begin Result := TInner.Create; Result.N := F end;
        end.
        """;

    // ------------------------------------------------------------------ LANG-990 (d): property accessors

    private const string AccessorUnit = """
        unit U;
        interface
        type
          TBase = class
          protected
            FBase: Integer;
          end;
          TFoo = class(TBase)
          protected
            FValue: Integer;
            procedure SetValue(V: Integer);
            function GetDouble: Integer;
          public
            function GetShadow: Integer;
          end;
          TFooH = class helper for TFoo
          public
            class var Total: Integer;
            function GetShadow: Integer;
            property Value: Integer read FValue write SetValue;
            property Base: Integer read FBase write FBase;
            property Doubled: Integer read GetDouble;
            property Shadow: Integer read GetShadow;
            class property Sum: Integer read Total write Total;
          end;
          TPoint = record
            X, Y: Integer;
            function Len: Integer;
          end;
          TPointH = record helper for TPoint
            property Left: Integer read X write X;
            property Size: Integer read Len;
          end;
        implementation
        procedure TFoo.SetValue(V: Integer); begin FValue := V * 10 end;
        function TFoo.GetDouble: Integer; begin Result := FValue * 2 end;
        function TFoo.GetShadow: Integer; begin Result := 100 end;
        function TFooH.GetShadow: Integer; begin Result := 200 end;
        function TPoint.Len: Integer; begin Result := X + Y end;
        end.
        """;

    private const string AccessorProgram = """
        program P;
        uses U;
        var
          O: TFoo;
          R: TPoint;
          N: Integer;
        begin
          O := TFoo.Create;
          O.Value := 3;
          O.Base := 4;
          TFooH.Sum := 5;
          R.Left := 6;
          N := O.Value + O.Base + O.Doubled + O.Shadow + TFooH.Sum + R.Left + R.Size;
          Writeln(N);
        end.
        """;

    /// <summary>
    /// LANG-990 (d): a <c>read</c> / <c>write</c> name of a helper property is looked up in the helper chain first, then in the helped
    /// type by its accessor rules — a field or a method of the helped class or of an ancestor, a field or method of a helped record;
    /// a class property over a class var of the helper. An accessor the helper's code may not use is E2610 at the name. Read through
    /// the <c>.pcu</c> the program gets the same IR.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-990")]
    public void PropertyAccessors_OfTheHelpedType()
    {
        var compilation = MarkerCompilation([("U.pas", AccessorUnit), ("P.pas", AccessorProgram)]);
        Assert.Equal(["U.pas(19,14): H2012"], compilation.GetDiagnostics(ct: TestContext.Current.CancellationToken)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code}"));
        var unit = compilation.GetUnitSymbol("U")!;
        var foo = Assert.IsType<ClassTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TFoo"));
        var fooHelper = Assert.IsType<HelperTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TFooH"));
        var pointHelper = Assert.IsType<HelperTypeSymbol>(unit.InterfaceMembers.Single(m => m.Name == "TPointH"));
        PropertySymbol Property(HelperTypeSymbol helper, string name) => helper.Properties.Single(p => p.Name == name);
        static string PathOf(PropertySymbol p, bool write) => string.Join(".", (write ? p.WritePath : p.ReadPath).Select(s => s.ContainingSymbol!.Name + "." + s.Name));
        static string Method(MethodSymbol? m) => m is null ? "" : m.Owner.Name + "." + m.Name;

        Assert.Equal(("TFoo.FValue", "TFoo.SetValue"), (PathOf(Property(fooHelper, "Value"), false), Method(Property(fooHelper, "Value").WriteMethod)));
        Assert.Equal(("TBase.FBase", "TBase.FBase"), (PathOf(Property(fooHelper, "Base"), false), PathOf(Property(fooHelper, "Base"), true)));
        Assert.Equal("TFoo.GetDouble", Method(Property(fooHelper, "Doubled").ReadMethod));
        Assert.Equal("TFooH.GetShadow", Method(Property(fooHelper, "Shadow").ReadMethod));                 // the chain first
        Assert.Equal(("TFooH.Total", "TFooH.Total"), (PathOf(Property(fooHelper, "Sum"), false), PathOf(Property(fooHelper, "Sum"), true)));
        Assert.Equal(("TPoint.X", "TPoint.X"), (PathOf(Property(pointHelper, "Left"), false), PathOf(Property(pointHelper, "Left"), true)));
        Assert.Equal("TPoint.Len", Method(Property(pointHelper, "Size").ReadMethod));
        Assert.Same(foo, Property(fooHelper, "Doubled").ReadMethod!.Owner);

        // an accessor the helper's code may not use
        Assert.Equal(["E2610(5,35): Cannot access strict private member 'FHidden' of 'TFoo'",
                      "E2610(5,73): Cannot access strict private member 'GetHidden' of 'TFoo'"], BindDiagnostics("""
            program P;
            type
              TFoo = class strict private FHidden: Integer; function GetHidden: Integer; end;
              TFooH = class helper for TFoo
                property Hidden: Integer read FHidden; property Other: Integer read GetHidden;
              end;
            function TFoo.GetHidden: Integer; begin Result := 0 end;
            begin
            end.
            """));

        // the same IR against the .pcu
        PcuImportTests.AssertSameIrAgainstPcu(AccessorUnit, AccessorProgram);
    }
}
