using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-116: generic type declarations and instances (LANG-700, 703, 704, 710…713).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-710")]
public sealed class GenericBindingTests
{
    private static TypeSymbol TypeOf(BindingResult result, string variable) => ((VariableSymbol)result.Scope.Lookup(variable)!).Type;

    [Fact]
    public void Instances_WithIdenticalArguments_AreOneType()
    {
        var (result, _) = Bind("program P; type TBox<T> = record V: T; end; var A: TBox<Integer>; B: TBox<Integer>; C: TBox<string>; begin end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Same(TypeOf(result, "A"), TypeOf(result, "B"));
        Assert.NotSame(TypeOf(result, "A"), TypeOf(result, "C"));
        var instance = TypeOf(result, "A");
        Assert.Equal("TBox<Integer>", instance.Name);
        Assert.Equal("P.TBox<Integer>", instance.QualifiedName);                  // CMP-360 link-name stem
        Assert.True(instance.IsGenericInstance);
        Assert.Equal([result.Types.Integer], instance.TypeArguments);
        Assert.IsType<GenericTypeSymbol>(instance.GenericDefinition);
        Assert.Equal(4, instance.Size);
        Assert.True(TypeOf(result, "C").IsManaged);
    }

    [Fact]
    [Trait("Req", "LANG-703")]
    public void SameName_DifferentArity_Coexist()
    {
        var (result, _) = Bind("""
            program P;
            type
              TFoo = Int64;
              TFoo<T> = record X: T; end;
              TFoo<A, B> = record X: A; Y: B; end;
            var Q: TFoo; R: TFoo<Byte>; S: TFoo<Byte, Word>;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal([8L, 1L, 4L], new[] { "Q", "R", "S" }.Select(v => TypeOf(result, v).Size));
    }

    [Theory]
    [Trait("Req", "LANG-703")]
    [InlineData("var Q: TFoo<Byte, Word>;", "E2703(1,52): 'TFoo' requires 1 type argument(s)")]
    [InlineData("var Q: TBar<Byte>;", "E2003(1,52): Undeclared identifier 'TBar'")]
    [InlineData("var Q: TFoo<Nothing>;", "E2003(1,57): Undeclared identifier 'Nothing'")]
    public void WrongUses_AreReported(string declaration, string expected) =>
        Assert.Equal([expected], BindDiagnostics($"program P; type TFoo<T> = record X: T; end; {declaration} begin end."));

    /// <summary>LANG-711: a name that does not depend on a type parameter binds in the declaring scope.</summary>
    [Fact]
    [Trait("Req", "LANG-711")]
    public void NonDependentNames_BindInTheDeclaringScope()
    {
        var (result, _) = Bind("""
            program P;
            type
              TSize = Word;
              THolder<T> = record Item: T; Count: TSize; end;
            procedure Use;
            type TSize = Int64;
            var H: THolder<Byte>;
            begin
            end;
            var G: THolder<Byte>;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(4, TypeOf(result, "G").Size);                                 // Byte + Word
    }

    /// <summary>LANG-713: Default(T) is a constant zero of T; an operand that is not a type is E2002.</summary>
    [Theory]
    [Trait("Req", "LANG-713")]
    [InlineData("I := Default(Integer);", "")]
    [InlineData("S := Default(string);", "")]
    [InlineData("I := Default(I);", "E2002(1,58): 'I' is not a type identifier")]
    public void Default_OfATypeOnly(string statement, string expected) =>
        Assert.Equal(expected.Length == 0 ? [] : [expected],
            BindDiagnostics($"program P; var I: Integer; S: string; begin {statement} Writeln(I, S) end.").Where(d => !d.StartsWith("H", StringComparison.Ordinal)));
}