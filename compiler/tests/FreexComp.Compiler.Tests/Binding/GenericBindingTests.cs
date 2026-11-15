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

    /// <summary>LANG-711, CMP-508 (C-222): an error in an instance body is E2710 with the instantiation site as related location.</summary>
    [Fact]
    [Trait("Req", "LANG-711")]
    public void InstantiatedFromHere()
    {
        var (result, _) = Bind("""
            program P;
            type
              TRec = record X: Integer; end;
              TSorter<T> = class
                class function Less(const A, B: T): Boolean;
              end;
            class function TSorter<T>.Less(const A, B: T): Boolean;
            begin
              Result := A < B;
            end;
            var R: TRec; D: Boolean;
            begin
              D := TSorter<Integer>.Less(1, 2);
              D := TSorter<TRec>.Less(R, R);
            end.
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E2710(9,15): In instantiation 'TSorter<TRec>': Operator '<' not applicable to operand type(s) 'TRec' and 'TRec'",
            Format([diagnostic])[0]);
        var related = Assert.Single(diagnostic.Related);
        Assert.Equal("instantiated from here", related.Message);
        Assert.Equal((14, 8), (related.Location.LineSpan.Start.Line + 1, related.Location.LineSpan.Start.Column + 1));
    }

    /// <summary>
    /// C-222: warnings and hints of instance bodies (binder and flow) are reported unwrapped, once per position however many
    /// instances have them, with the first instantiation site as related location.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-711")]
    public void InstanceWarnings()
    {
        var (result, _) = Bind("""
            program P;
            type
              TBase = class
                destructor Destroy; virtual;
              end;
              TBox<T> = class(TBase)
                destructor Destroy; override;
                function Get: Integer;
              end;
            destructor TBase.Destroy;
            begin
            end;
            destructor TBox<T>.Destroy;
            begin
            end;
            function TBox<T>.Get: Integer;
            var
              Unused: T;
            begin
            end;
            var A: TBox<Integer>; B: TBox<string>;
            begin
              A := nil;
              B := nil;
            end.
            """);

        Assert.Equal(
            [
                "W1062(13,12): Destructor 'TBox<Integer>.Destroy' does not call inherited",
                "H2001(18,3): Variable 'Unused' is declared but never used",
                "W1042(20,1): Return value of function 'Get' might be undefined",
            ],
            Format(result.Diagnostics));
        Assert.All(result.Diagnostics, d => Assert.Equal((21, 8), (d.Related.Single().Location.LineSpan.Start.Line + 1, d.Related.Single().Location.LineSpan.Start.Column + 1)));
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