// The semantic model's lookups are synchronous index reads over an already bound compilation; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-345: nested types of instances, generics nested in types and routines, qualified generic names and generic names without
/// arguments (generics.md LANG-775, 778…781).
/// </summary>
public sealed partial class GenericBindingTests
{
    private static TypeSymbol VariableType(BindingResult result, string variable) => ((VariableSymbol)result.Scope.Lookup(variable)!).Type;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-775")]
    public void NestedTypeOfInstance_DistinctPerInstance_SharedForEqualArguments()
    {
        var (result, _) = Bind("""
            program P;
            type
              TList<T> = class
              public
                type
                  TEnumerator = class
                    Current: T;
                  end;
                  TNode = record
                    Value: T;
                  end;
              end;
            var
              L: TList<Integer>;
              A: TList<Integer>.TEnumerator;
              B: TList<Integer>.TEnumerator;
              C: TList<string>.TEnumerator;
              N: TList<Integer>.TNode;
              M: TList<string>.TNode;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Assert.IsType<ClassTypeSymbol>(VariableType(result, "A"));
        Assert.Same(a, VariableType(result, "B"));                                   // LANG-710: one instance, one nested type
        Assert.NotSame(a, VariableType(result, "C"));
        Assert.Same(VariableType(result, "L"), a.ContainingSymbol);
        Assert.Same(result.Types.Integer, a.Fields.Single().Type);
        Assert.Equal("TEnumerator", a.Name);
        Assert.Equal("TList<Integer>.TEnumerator", TypeMembers.DisplayName(a));
        Assert.Equal("P.TList<Integer>.TEnumerator", a.QualifiedName);               // CMP-1390
        Assert.Same(VariableType(result, "L"), TypeMembers.EnclosingInstance(a));
        Assert.Equal(4, VariableType(result, "N").Size);
        Assert.NotSame(VariableType(result, "N"), VariableType(result, "M"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-778")]
    public void GenericNestedInClass_IdentityAndArity()
    {
        var (result, _) = Bind("""
            program P;
            type
              TOuter = class
              public
                type
                  TBox = class end;
                  TBox<T> = class
                    Value: T;
                    function Same: TBox<T>;
                  end;
                  TBox<A, B> = class
                    First: A;
                    Second: B;
                  end;
                function Wrap: TBox<Integer>;
              end;
              TR = record
                type
                  TCell<T> = record
                    Item: T;
                  end;
              end;
            function TOuter.TBox<T>.Same: TBox<T>; begin Result := Self end;
            function TOuter.Wrap: TBox<Integer>; begin Result := nil end;
            var
              X: TOuter.TBox;
              Y: TOuter.TBox<Integer>;
              Z: TOuter.TBox<Integer>;
              W: TOuter.TBox<Byte, Word>;
              C: TR.TCell<Int64>;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var outer = Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TOuter"));
        Assert.False(VariableType(result, "X").IsGenericInstance);
        var y = Assert.IsType<ClassTypeSymbol>(VariableType(result, "Y"));
        Assert.Same(y, VariableType(result, "Z"));
        Assert.Same(y, outer.Methods.Single(m => m.Name == "Wrap").ReturnType);       // by simple name inside the enclosing type
        Assert.Same(y, y.Methods.Single(m => m.Name == "Same").ReturnType);
        Assert.NotNull(y.Methods.Single(m => m.Name == "Same").Definition);             // TOuter.TBox<T>.Same (LANG-778)
        Assert.Same(outer, y.ContainingSymbol);
        Assert.Same(outer, y.GenericDefinition!.ContainingSymbol);
        Assert.Equal("TOuter.TBox<Integer>", TypeMembers.DisplayName(y));
        Assert.Equal("P.TOuter.TBox<Integer>", y.QualifiedName);
        Assert.Equal([result.Types.Byte, result.Types.Word], VariableType(result, "W").TypeArguments);
        Assert.Equal(8, VariableType(result, "C").Size);
        Assert.Null(result.Scope.Lookup("TBox`1"));                                    // not a unit-level name
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-778")]
    public void GenericNestedInGeneric_IdentityIncludesOuterArguments()
    {
        var (result, _) = Bind("""
            program P;
            type
              TA<T> = class
              public
                type
                  TB<U> = class
                    Left: T;
                    Right: U;
                  end;
              end;
            var
              P1: TA<Integer>.TB<string>;
              P2: TA<Integer>.TB<string>;
              P3: TA<Byte>.TB<string>;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var p1 = Assert.IsType<ClassTypeSymbol>(VariableType(result, "P1"));
        Assert.Same(p1, VariableType(result, "P2"));
        var p3 = Assert.IsType<ClassTypeSymbol>(VariableType(result, "P3"));
        Assert.NotSame(p1, p3);
        Assert.Equal([result.Types.Integer, result.Types.String], p1.Fields.Select(f => f.Type));
        Assert.Equal([result.Types.Byte, result.Types.String], p3.Fields.Select(f => f.Type));
        Assert.Equal("TA<Integer>.TB<string>", TypeMembers.DisplayName(p1));
        Assert.Equal([result.Types.String], p1.TypeArguments);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-778")]
    public void NestedTypeParameterNameClash_E2004() =>
        Assert.Equal(
        [
            "E2004(4,16): Identifier redeclared: 'T'",
            "E2004(7,38): Identifier redeclared: 'T'",
        ],
        BindDiagnostics("""
            program P;
            type
              TA<T> = class
                type TB<U, T> = class end;
              end;
              TOuter = class
                type TBox<T> = class type TInner<T> = record end; end;
              end;
            var X: TA<Integer>;
            begin end.
            """));

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-779")]
    [InlineData("procedure Q; type TBox<T> = class end; begin end;", 23)]
    [InlineData("procedure Q; type TBox<T> = record X: T; end; begin end;", 23)]
    [InlineData("procedure Q; type TFun<T> = function(X: T): T; begin end;", 23)]
    [InlineData("procedure Q; procedure R; type TBox<T> = record X: T; end; begin end; begin R end;", 36)]
    public void LocalGeneric_E2705(string routine, int column) =>
        Assert.Equal([$"E2705(2,{column}): Generic types cannot be declared inside a routine"],
            BindDiagnostics("program P;\n" + routine + "\nbegin Q end.").Where(d => !d.StartsWith('H')));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-779")]
    public void LocalGeneric_UsesAreUndeclaredOnce() =>
        Assert.Equal(
        [
            "E2705(2,23): Generic types cannot be declared inside a routine",
            "E2003(2,54): Undeclared identifier 'TBox'",
        ],
        BindDiagnostics("program P;\nprocedure Q; type TBox<T> = record X: T; end; var A: TBox<Integer>; B: TBox<Byte>; begin end;\nbegin Q end.")
            .Where(d => !d.StartsWith('H')));

    private const string QualifiedUnitU = """
        unit U;
        interface
        type
          TBox<T> = record
            V: T;
          end;
          TOuter = class
          public
            type
              TCell<T> = record
                Item: T;
              end;
          end;
          TList<T> = class
          public
            type
              TNode = record
                Value: T;
              end;
          end;
        implementation
        end.
        """;

    private const string QualifiedUnitAB = """
        unit A.B;
        interface
        type
          TGen<T> = record
            W: T;
          end;
        implementation
        end.
        """;

    /// <summary>LANG-780: a qualified instance name is the type of the unqualified one, in type and in expression positions.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-780")]
    [InlineData("U.TBox<Integer>", "TBox<Integer>")]
    [InlineData("A.B.TGen<Integer>", "TGen<Integer>")]
    [InlineData("TOuter.TCell<Integer>", "U.TOuter.TCell<Integer>")]
    [InlineData("TList<Integer>.TNode", "U.TList<Integer>.TNode")]
    [InlineData("P.TLocal<Integer>", "TLocal<Integer>")]
    [InlineData("P.TLocal<Integer>.TInner", "TLocal<Integer>.TInner")]
    public void QualifiedInstanceName_IsTheSameType(string qualified, string plain)
    {
        var program = $"""
            program P;
            uses U, A.B;
            type
              TLocal<T> = record
                L: T;
                type
                  TInner = record
                    Z: T;
                  end;
              end;
            var
              X: {qualified};
              Y: {plain};
            begin
              Writeln(TypeInfo({qualified}) = TypeInfo({plain}), SizeOf({qualified}));
              X := Y;
              Y := X;
            end.
            """;
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\U.pas", QualifiedUnitU).Add(@"C:\p\A.B.pas", QualifiedUnitAB).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var model = Compilation.Create(options, [], fs).GetSemanticModel(@"C:\p\P.pas");

        Assert.Empty(model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToText()));
        var variables = model.Unit.InterfaceMembers.Concat(model.Unit.ImplementationMembers).OfType<VariableSymbol>().ToList();
        var x = variables.Single(v => v.Name == "X").Type;
        Assert.True(x.TypeKind != TypeKind.Error && (x.IsGenericInstance || TypeMembers.EnclosingInstance(x) is not null), x.Name);
        Assert.Same(x, variables.Single(v => v.Name == "Y").Type);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-780")]
    [InlineData("Nope.TBox<Integer>", "E2003(1,92): Undeclared identifier 'Nope'")]
    [InlineData("TOuter.TMissing<Integer>", "E2005(1,99): 'TOuter' does not contain a member named 'TMissing'")]
    [InlineData("TOuter.TBox<Integer, Byte>", "E2703(1,99): 'TBox' requires 1 type argument(s)")]
    [InlineData("TOuter.TBox", "E2703(1,99): 'TBox' requires 1 type argument(s)")]
    [InlineData("P.TList<Integer, Byte>", "E2703(1,94): 'TList' requires 1 type argument(s)")]
    [InlineData("TList<Integer>.TMissing", "E2005(1,107): 'TList<Integer>' does not contain a member named 'TMissing'")]
    [InlineData("TList<Integer>.TNode<Byte>", "E2005(1,107): 'TList<Integer>' does not contain a member named 'TNode'")]
    public void QualifiedSegment_Errors(string name, string expected) =>
        Assert.Equal([expected], BindDiagnostics(
            "program P; type TOuter = class type TBox<T> = class end; end; TList<T> = class end; var X: " + name + "; begin end."));

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-781")]
    [InlineData("var X: TBox; begin end.", "E2703(1,100): 'TBox' requires 1 type argument(s)")]
    [InlineData("var X: TPair; begin end.", "E2703(1,100): 'TPair' requires 2 type argument(s)")]
    [InlineData("begin TBox.Create; end.", "E2703(1,99): 'TBox' requires 1 type argument(s)")]
    [InlineData("begin Writeln(SizeOf(TPair)); end.", "E2703(1,114): 'TPair' requires 2 type argument(s)")]
    public void GenericNameWithoutArguments_E2703(string rest, string expected) =>
        Assert.Equal([expected], BindDiagnostics(
            "program P; type TBox<T> = class end; TPair<A, B> = record end; TPair<A, B, C> = record end; " + rest));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-781")]
    public void GenericNameWithoutArguments_InsideItsOwnDeclaration_E2703() =>
        Assert.Equal(["E2710(1,49): In instantiation 'TBox<Byte>': 'TBox' requires 1 type argument(s)"],
            BindDiagnostics("program P; type TBox<T> = class function Clone: TBox; end; var B: TBox<Byte>; begin end."));
}
