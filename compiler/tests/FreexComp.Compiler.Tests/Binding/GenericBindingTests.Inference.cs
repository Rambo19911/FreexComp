using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-418: type-argument inference for calls of generic methods by unification (LANG-979) — <c>var</c> / <c>out</c> positions, open
/// arrays, dynamic arrays and instances of generic types; conflicts; an argument's own error is reported once (CMP-1477).
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string InferenceProgram = """
        program P;
        type
          TArray<T> = array of T;
          TDoubles = array of Double;
          TNames = array[0..2] of string;
          IComparer<T> = interface
            function Compare(const A, B: T): Integer;
          end;
          IIntOrder = interface(IComparer<Integer>)
          end;
          TComparison<T> = reference to function(const A, B: T): Integer;
          TPlainComparison<T> = function(const A, B: T): Integer;
          TBox<T> = class
            Value: T;
          end;
          TStringBox = class(TBox<string>)
          end;
          TPair<K, V> = record
            Key: K;
            Value: V;
          end;
          TIntComparer = class(TObject, IComparer<Integer>)
            function Compare(const A, B: Integer): Integer;
          end;
          TUtil = class
            class procedure Swap<T>(var A, B: T); static;
            class function Max<T>(const A, B: T; const Comparer: IComparer<T>): T; static;
            class function MaxBy<T>(const A, B: T; const Compare: TComparison<T>): T; static;
            class function MaxPlain<T>(const A, B: T; Compare: TPlainComparison<T>): T; static;
            class function Only<T>(const Compare: TComparison<T>): Integer; static;
            class function IndexOf<T>(const Values: array of T; const Item: T): Integer; static;
            class function Sum<T>(const Values: array of T): T; static;
            class procedure Reset<T>(var Values: array of T); static;
            class function First<T>(const Values: TArray<T>): T; static;
            class function FirstOr<T>(const Values: TArray<T>; const Fallback: T): T; static;
            class function Count<T>(const Box: TBox<T>): Integer; static;
            class function KeyOf<K, V>(const Pair: TPair<K, V>): K; static;
            class procedure Fill<K, V>(const Key: K; out Value: V); static;
            class function Pick<T>(const A, B: T): T; static;
            class function Opt<T>(const A: T; N: Integer = 3): T; static;
            class function OrNil<T>(const A: T; P: Pointer): T; static;
            class function Make<T>: T; static;
            class procedure Two<T>(var A, B: T); overload; static;
            class procedure Two<T>(var A, B: T; N: Integer); overload; static;
          end;
        function TIntComparer.Compare(const A, B: Integer): Integer; begin Result := A - B end;
        class procedure TUtil.Swap<T>(var A, B: T); begin end;
        class function TUtil.Max<T>(const A, B: T; const Comparer: IComparer<T>): T; begin Result := A end;
        class function TUtil.MaxBy<T>(const A, B: T; const Compare: TComparison<T>): T; begin Result := A end;
        class function TUtil.MaxPlain<T>(const A, B: T; Compare: TPlainComparison<T>): T; begin Result := A end;
        class function TUtil.Only<T>(const Compare: TComparison<T>): Integer; begin Result := 0 end;
        class function TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer; begin Result := 0 end;
        class function TUtil.Sum<T>(const Values: array of T): T; begin Result := Values[0] end;
        class procedure TUtil.Reset<T>(var Values: array of T); begin end;
        class function TUtil.First<T>(const Values: TArray<T>): T; begin Result := Values[0] end;
        class function TUtil.FirstOr<T>(const Values: TArray<T>; const Fallback: T): T; begin Result := Fallback end;
        class function TUtil.Count<T>(const Box: TBox<T>): Integer; begin Result := 0 end;
        class function TUtil.KeyOf<K, V>(const Pair: TPair<K, V>): K; begin Result := Pair.Key end;
        class procedure TUtil.Fill<K, V>(const Key: K; out Value: V); begin end;
        class function TUtil.Pick<T>(const A, B: T): T; begin Result := A end;
        class function TUtil.Opt<T>(const A: T; N: Integer = 3): T; begin Result := A end;
        class function TUtil.OrNil<T>(const A: T; P: Pointer): T; begin Result := A end;
        class function TUtil.Make<T>: T; begin end;
        class procedure TUtil.Two<T>(var A, B: T); begin end;
        class procedure TUtil.Two<T>(var A, B: T; N: Integer); begin end;
        function ByValue(const A, B: Integer): Integer; begin Result := A - B end;
        var
          I, J, N: Integer;
          L: Int64;
          B: Byte;
          D: Double;
          S: string;
          Names: TNames;
          Ints: array of Integer;
          Big: array[0..9] of Integer;
          List: TArray<Double>;
          Doubles: TDoubles;
          Cmp: IComparer<Integer>;
          StrCmp: IComparer<string>;
          Order: IIntOrder;
          CmpObject: TIntComparer;
          CmpRef: TComparison<Integer>;
          StrBox: TStringBox;
          IntBox: TBox<Integer>;
          Pair: TPair<Byte, string>;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-979 (1), (2), (4): each parameter form infers the type arguments — the call binds, without a diagnostic, to the named instance.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    [Trait("Req", "LANG-714")]
    // a bare type parameter by value or const (LANG-714, as before)
    [InlineData("N := TUtil.Pick(10, 4)", "Pick<Integer>")]
    [InlineData("L := TUtil.Pick(5000000000, 1 + L)", "Pick<Int64>")]
    [InlineData("D := TUtil.Pick(1.5, D)", "Pick<Double>")]
    [InlineData("S := TUtil.Pick(S, S + S)", "Pick<string>")]
    [InlineData("B := TUtil.Pick(B, B)", "Pick<Byte>")]
    // var and out positions: the declared type of the variable
    [InlineData("TUtil.Swap(I, J)", "Swap<Integer>")]
    [InlineData("TUtil.Swap(B, B)", "Swap<Byte>")]
    [InlineData("TUtil.Swap(Names[0], S)", "Swap<string>")]
    [InlineData("TUtil.Fill(1, S)", "Fill<Integer, string>")]
    [InlineData("TUtil.Fill(S, D)", "Fill<string, Double>")]
    // open arrays: static, dynamic and open arrays, Slice, bracket constructors of one element type; also a var open array
    [InlineData("N := TUtil.IndexOf(Names, S)", "IndexOf<string>")]
    [InlineData("N := TUtil.IndexOf(Ints, 3)", "IndexOf<Integer>")]
    [InlineData("N := TUtil.Sum(Big)", "Sum<Integer>")]
    [InlineData("N := TUtil.Sum(Slice(Big, 2))", "Sum<Integer>")]
    [InlineData("N := TUtil.Sum([1, 2, 3])", "Sum<Integer>")]
    [InlineData("D := TUtil.Sum([1.5, D])", "Sum<Double>")]
    [InlineData("N := TUtil.IndexOf([I, J], N)", "IndexOf<Integer>")]
    [InlineData("TUtil.Reset(Ints)", "Reset<Integer>")]
    [InlineData("TUtil.Reset(Names)", "Reset<string>")]
    // dynamic arrays
    [InlineData("D := TUtil.First(List)", "First<Double>")]
    [InlineData("N := TUtil.First(TArray<Integer>.Create(1, 2))", "First<Integer>")]
    [InlineData("D := TUtil.First(List + List)", "First<Double>")]
    [InlineData("D := TUtil.First(List + [D])", "First<Double>")]
    [InlineData("D := TUtil.FirstOr([1.5] + [D], D)", "FirstOr<Double>")]              // + and Concat of constructors take the parameter's type
    [InlineData("D := TUtil.FirstOr(Concat([1.5], [D]), D)", "FirstOr<Double>")]
    [InlineData("D := TUtil.FirstOr([1.5, 2.5], D)", "FirstOr<Double>")]
    [InlineData("D := TUtil.FirstOr(nil, D)", "FirstOr<Double>")]
    // instances of generic types: interface, class (also through a descendant and an implementing class), record, method reference
    [InlineData("N := TUtil.Max(3, 7, Cmp)", "Max<Integer>")]
    [InlineData("N := TUtil.Max(3, 7, Order)", "Max<Integer>")]
    [InlineData("N := TUtil.Max(3, 7, CmpObject)", "Max<Integer>")]
    [InlineData("N := TUtil.Count(IntBox)", "Count<Integer>")]
    [InlineData("N := TUtil.Count(StrBox)", "Count<string>")]
    [InlineData("B := TUtil.KeyOf(Pair)", "KeyOf<Byte, string>")]
    [InlineData("N := TUtil.MaxBy(3, 7, CmpRef)", "MaxBy<Integer>")]
    [InlineData("N := TUtil.Only(CmpRef)", "Only<Integer>")]
    // arguments without an argument type next to an inferable one: a default value, nil, an anonymous method, a routine name
    [InlineData("N := TUtil.Opt(5)", "Opt<Integer>")]
    [InlineData("S := TUtil.Opt(S, 4)", "Opt<string>")]
    [InlineData("N := TUtil.OrNil(5, nil)", "OrNil<Integer>")]
    [InlineData("N := TUtil.Max(3, 7, nil)", "Max<Integer>")]
    [InlineData("N := TUtil.MaxBy(3, 7, function(const A, B: Integer): Integer begin Result := A - B end)", "MaxBy<Integer>")]
    [InlineData("N := TUtil.MaxPlain(3, 7, ByValue)", "MaxPlain<Integer>")]
    public void Inference_ParameterForms(string call, string expected)
    {
        var result = BindInference(call);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        Assert.Equal(expected, CalledInstance(result));
    }

    /// <summary>LANG-979 (3), (4): a type parameter bound to two types, or reached by no argument, is E2711 at the method name — nothing else.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    [InlineData("TUtil.Swap(I, L)", 9)]                                          // Integer and Int64: no widening between arguments
    [InlineData("TUtil.Pick(1, 'a')", 9)]
    [InlineData("N := TUtil.Max(3, 7, StrCmp)", 14)]                             // T: Integer from the values, string from the comparer
    [InlineData("N := TUtil.IndexOf(Names, 3)", 14)]
    [InlineData("D := TUtil.First(List) + TUtil.Sum([1, 'a'])", 34)]             // constructor elements of two natural types fit nothing
    [InlineData("N := TUtil.Sum([])", 14)]
    [InlineData("N := TUtil.Sum([1, Undeclared])", 14)]                          // an element that does not bind has no natural type
    [InlineData("N := TUtil.Make", 14)]                                          // no parameter mentions T
    [InlineData("N := TUtil.Only(nil)", 14)]                                     // nil has no argument type
    [InlineData("N := TUtil.Count(CmpObject)", 14)]                              // no instance of TBox among the ancestors
    public void Inference_Conflicts_AreE2711(string call, int column)
    {
        var name = call[(call.LastIndexOf("TUtil.", StringComparison.Ordinal) + 6)..].Split('(', ';', ' ')[0];

        Assert.Equal(
            [$"E2711({Line("{CALL}")},{column}): Cannot infer type arguments for '{name}'; specify them explicitly"],
            Format(BindInference(call).Diagnostics));
    }

    /// <summary>
    /// LANG-979 (5), CMP-1477 (2): an error inside an argument is reported once — by the one binding of that argument, whatever the
    /// number of generic candidates — and the call adds no E2711.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    [Trait("Req", "CMP-1477")]
    [InlineData("TUtil.Swap(I, Undeclared)", "E2003", 17, "Undeclared identifier 'Undeclared'")]
    [InlineData("TUtil.Two(I, Undeclared)", "E2003", 16, "Undeclared identifier 'Undeclared'")]                 // two generic candidates
    [InlineData("TUtil.Two(Undeclared, I, 1)", "E2003", 13, "Undeclared identifier 'Undeclared'")]
    [InlineData("N := TUtil.Pick(Undeclared, 1)", "E2003", 19, "Undeclared identifier 'Undeclared'")]
    [InlineData("N := TUtil.IndexOf(Ints, Undeclared)", "E2003", 28, "Undeclared identifier 'Undeclared'")]
    [InlineData("N := TUtil.Max(3, 7, Undeclared)", "E2003", 24, "Undeclared identifier 'Undeclared'")]
    [InlineData("N := TUtil.OrNil(Nope + 1, nil)", "E2003", 20, "Undeclared identifier 'Nope'")]
    [InlineData("N := TUtil.Opt(5, Nope)", "E2003", 21, "Undeclared identifier 'Nope'")]                        // bound with the candidate
    [InlineData("N := TUtil.Pick(ByValue, 1)", "E2110", 19, "Not enough actual parameters for 'ByValue'")]
    public void Inference_ArgumentErrorIsReportedOnce(string call, string code, int column, string message) =>
        Assert.Equal(
            [$"{code}({Line("{CALL}")},{column}): {message}"],
            Format(BindInference(call).Diagnostics));

    /// <summary><see cref="InferenceProgram"/> with <paramref name="call"/> as its statement, bound against a <c>System</c> with <c>TObject</c>.</summary>
    private static BindingResult BindInference(string call)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, InferenceProgram.Replace("{CALL}", call)));
        Assert.Empty(tree.Diagnostics);
        return BindWithStubSystem(tree, AnonymousMethodBindingTests.SystemSource);
    }

    /// <summary>
    /// LANG-979 rule 1 (spec risk 10): a bracket constructor is examined element by element for the inference and then bound once,
    /// against the open-array parameter of the instance — an array constructor of that type, never the set it is without a context;
    /// an argument the inference bound is the argument of the call (a <c>var</c> argument: the variable itself).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-979")]
    [Trait("Req", "CMP-1477")]
    public void Inference_ArgumentsAreBoundOnce_ConstructorAgainstTheInstance()
    {
        static ImmutableArray<BoundExpression> Arguments(BindingResult result) => result.Bodies[0].Statements.Single() switch
        {
            BoundCallStatement call => call.Arguments,
            BoundAssignmentStatement { Value: BoundCallExpression call } => call.Arguments,
            _ => [],
        };

        var sum = BindInference("N := TUtil.Sum([I, 2, J])");
        Assert.Equal([], Format(sum.Diagnostics).Where(d => !d.StartsWith('H')));
        var constructor = Assert.IsType<BoundArrayConstructor>(Assert.Single(Arguments(sum)));
        Assert.Equal("array of Integer", constructor.Type.Name);
        Assert.Equal(3, constructor.Elements.Length);

        var swap = BindInference("TUtil.Swap(I, J)");
        Assert.Equal(["I:Integer", "J:Integer"], Arguments(swap).Select(BoundDump.Of));
    }

    /// <summary>The 1-based line of <paramref name="marker"/> in <see cref="InferenceProgram"/>.</summary>
    private static int Line(string marker) =>
        InferenceProgram[..InferenceProgram.IndexOf(marker, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    /// <summary>
    /// The generic method instance the statement of the program block calls, as <c>Name&lt;Arguments&gt;</c>: the callee of the call
    /// statement, or of the outermost call in the value of the assignment.
    /// </summary>
    private static string? CalledInstance(BindingResult result)
    {
        var statement = result.Bodies[0].Statements.Single();
        var routine = statement switch
        {
            BoundCallStatement call => call.Routine,
            BoundAssignmentStatement { Value: var value } => Unconverted(value) is BoundCallExpression call ? call.Routine : null,
            _ => null,
        };
        return routine is MethodSymbol { GenericMethodDefinition: not null } method
            ? method.Name + "<" + string.Join(", ", method.GenericMethodArguments.Select(a => a.Name)) + ">"
            : null;

        static BoundExpression Unconverted(BoundExpression e) => e is BoundConversionExpression conversion ? Unconverted(conversion.Operand) : e;
    }
}
