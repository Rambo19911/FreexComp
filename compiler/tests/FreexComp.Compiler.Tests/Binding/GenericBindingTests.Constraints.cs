using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-421: generic constraints, completion (F10-038) — which constraint lists are allowed (LANG-982), when a type argument satisfies
/// a constraint (LANG-983; <c>constructor</c> follows the lookup of the call <c>X.Create</c>), and how a failed constraint is
/// reported at the instantiation (LANG-984).
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string ConstraintProgram = """
        program P;
        type
          TBase = class end;
          TChild = class(TBase) end;
          TGrand = class(TChild) end;
          TOther = class end;
          IGreeter = interface procedure Hi; end;
          ILoud = interface(IGreeter) procedure Loud; end;
          IOther = interface procedure Ho; end;
          TGreeter = class(TInterfacedObject, IGreeter) procedure Hi; end;
          TGreeterKid = class(TGreeter) end;
          TLoud = class(TInterfacedObject, ILoud) procedure Hi; procedure Loud; end;
          TOwn = class constructor Create; end;
          TNoDefault = class constructor Create(A: Integer); end;
          TRec = record X: Integer; end;
          TManaged = record S: string; end;
          PRec = ^TRec;
          TColor = (Red, Green);
          TSmall = 1..10;
          TArr = array[0..3] of Integer;
          TDyn = array of Integer;
          TSet = set of TColor;
          TMeta = class of TBase;
          TProc = procedure;
          TMeth = procedure of object;
          TRef = reference to procedure;
          TShort = string[10];
          TBox<T> = class end;
          TPair<T> = record A, B: T; end;
          {DECLARATION}
        procedure TGreeter.Hi; begin end;
        procedure TLoud.Hi; begin end;
        procedure TLoud.Loud; begin end;
        constructor TOwn.Create; begin end;
        constructor TNoDefault.Create(A: Integer); begin end;
        {USE}
        begin
        end.
        """;

    /// <summary>
    /// LANG-982 table, for the type parameter list of a generic type and of a generic method: <c>class</c> with <c>record</c>,
    /// <c>record</c> with <c>constructor</c>, with a class type or with an interface type, and two class types are E2701 — one per
    /// item that cannot be combined with an earlier one (however many earlier ones; C-422: once per conflicting item, not once per
    /// pair), at that item, naming the type parameter (the first name of a shared list); every other combination
    /// is allowed in any order; a constraint type that is no class or interface type, or a type parameter, is E2603; a keyword or
    /// type written twice has no further effect. Expected: <c>CODE@column in the list</c>, with <c>:name</c> for E2701.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-982")]
    [InlineData("T: class, record", "E2701@11:T")]
    [InlineData("T: record, class", "E2701@12:T")]
    [InlineData("T: record, constructor", "E2701@12:T")]                        // new: nothing can satisfy it
    [InlineData("T: constructor, record", "E2701@17:T")]
    [InlineData("T: record, TBase", "E2701@12:T")]
    [InlineData("T: TBase, record", "E2701@11:T")]
    [InlineData("T: record, IGreeter", "E2701@12:T")]                           // new: no value type implements an interface
    [InlineData("T: IGreeter, record", "E2701@14:T")]
    [InlineData("T: TBase, TOther", "E2701@11:T")]
    // allowed
    [InlineData("T: class", "")]
    [InlineData("T: record", "")]
    [InlineData("T: constructor", "")]
    [InlineData("T: TBase", "")]
    [InlineData("T: IGreeter", "")]
    [InlineData("T: class, TBase", "")]
    [InlineData("T: TBase, class", "")]
    [InlineData("T: class, constructor", "")]
    [InlineData("T: constructor, class", "")]
    [InlineData("T: TBase, constructor", "")]
    [InlineData("T: constructor, TBase", "")]
    [InlineData("T: class, IGreeter", "")]
    [InlineData("T: IGreeter, TBase", "")]
    [InlineData("T: constructor, IGreeter", "")]
    [InlineData("T: IGreeter, IOther", "")]
    [InlineData("T: class, TBase, constructor, IGreeter, IOther", "")]
    [InlineData("T: IOther, constructor, IGreeter, TBase, class", "")]
    // no class or interface type
    [InlineData("T: Integer", "E2603@4")]
    [InlineData("T: TRec", "E2603@4")]
    [InlineData("T: TColor", "E2603@4")]
    [InlineData("T: PRec", "E2603@4")]
    [InlineData("T: TMeta", "E2603@4")]
    [InlineData("T: class, TDyn", "E2603@11")]
    [InlineData("T; U: T", "E2603@7")]                                           // a type parameter of the list
    [InlineData("T: class; U: T", "E2603@14")]
    [InlineData("T: T", "E2603@4")]
    // written twice
    [InlineData("T: class, class", "")]
    [InlineData("T: record, record", "")]
    [InlineData("T: constructor, constructor", "")]
    [InlineData("T: TBase, TBase", "")]
    [InlineData("T: IGreeter, IGreeter", "")]
    [InlineData("T: TBase, class, TBase, TOther", "E2701@25:T")]
    [InlineData("T: record, record, class", "E2701@20:T")]
    [InlineData("T: class, record, record", "E2701@11:T")]
    // shared and grouped lists
    [InlineData("A, B: class", "")]
    [InlineData("A, B: class, record", "E2701@14:A")]
    [InlineData("K: record; V: class", "")]
    [InlineData("K: record; V: class, record", "E2701@22:V")]
    [InlineData("K: class, record; V: record, constructor", "E2701@11:K E2701@30:V")]
    [InlineData("K, L: TBase; V, W: record, IGreeter", "E2701@28:V")]
    // several pairs: one diagnostic per conflicting item (not per pair), at that item
    [InlineData("T: class, constructor, record", "E2701@24:T")]
    [InlineData("T: class, TBase, record", "E2701@18:T")]
    [InlineData("T: record, class, TBase", "E2701@12:T E2701@19:T")]
    [InlineData("T: record, constructor, IGreeter", "E2701@12:T E2701@25:T")]
    [InlineData("T: TBase, IGreeter, IOther, record", "E2701@29:T")]
    [InlineData("T: record, TBase, TOther", "E2701@12:T E2701@19:T")]
    [InlineData("T: TBase, TOther, TChild", "E2701@11:T E2701@19:T")]
    [InlineData("T: class, constructor, TBase, IGreeter, record, TOther", "E2701@41:T E2701@49:T")]
    [InlineData("A, B: class, constructor, record", "E2701@27:A")]
    public void Constraints_Combinations(string list, string expected)
    {
        var names = string.Join(", ", list.Split(';').Select(g => g.Split(':')[0].Trim()));
        var asType = ConstraintProgram.Replace("{DECLARATION}", $"TG<{list}> = class end;", StringComparison.Ordinal).Replace("{USE}", "", StringComparison.Ordinal);
        var asMethod = ConstraintProgram.Replace("{DECLARATION}", $"TU = class class procedure M<{list}>(X: Integer); static; end;", StringComparison.Ordinal)
            .Replace("{USE}", $"class procedure TU.M<{names}>(X: Integer); begin end;", StringComparison.Ordinal);

        Assert.Equal(expected, InList(asType, $"TG<{list}>", "TG<".Length));
        Assert.Equal(expected, InList(asMethod, $"M<{list}>", "M<".Length));

        static string InList(string text, string piece, int skip)
        {
            var (line, column) = PositionOf(text, piece);
            return string.Join(" ", BindRestricted(text).Diagnostics.Where(d => d.Code[0] != 'H').Select(d =>
            {
                Assert.Equal(line, d.Location.LineSpan.Start.Line + 1);
                var at = d.Location.LineSpan.Start.Column + 1 - column - skip + 1;
                if (d.Code != "E2701")
                {
                    return $"{d.Code}@{at}";
                }

                var name = d.Message.Split((char)39)[1];
                Assert.Equal($"Conflicting constraints on type parameter '{name}'", d.Message);
                return $"E2701@{at}:{name}";
            }));
        }
    }

    /// <summary>
    /// LANG-982: a type parameter of an enclosing generic is no constraint type either — E2603 in every instance (inside E2710),
    /// also in one whose argument is a class.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-982")]
    public void Constraints_EnclosingTypeParameterIsNoConstraintType()
    {
        var result = BindRestricted("""
            program P;
            type
              TBase = class end;
              TOuter<T> = class
                procedure M<U: T>(const X: U);
              end;
            procedure TOuter<T>.M<U>(const X: U); begin end;
            var O: TOuter<TBase>;
            begin
            end.
            """);

        Assert.Equal(["E2710(5,20): In instantiation 'TOuter<TBase>': Class type required"], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>
    /// LANG-983 table, one row per type family: <c>class</c> — class types only; <c>record</c> — ordinal, real (<c>Currency</c>
    /// too), record (also with managed fields), static array and set types, and nothing that is a string, a dynamic array or a
    /// reference; <c>constructor</c> — a class with a callable public parameterless <c>Create</c>
    /// (<see cref="Constraints_ConstructorFollowsCallLookup"/>); a class type — itself and its descendants; an interface type —
    /// itself, a descendant interface and a class that implements it or a descendant of it, itself or through an ancestor. A
    /// failed constraint is one E2704 at the type argument, with the argument's display name.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-983")]
    // class
    [InlineData("class", "TBase", true)]
    [InlineData("class", "TObject", true)]
    [InlineData("class", "TGreeter", true)]
    [InlineData("class", "TBox<Integer>", true)]
    [InlineData("class", "IGreeter", false)]
    [InlineData("class", "TMeta", false)]
    [InlineData("class", "PRec", false)]
    [InlineData("class", "Pointer", false)]
    [InlineData("class", "Integer", false)]
    [InlineData("class", "TRec", false)]
    [InlineData("class", "string", false)]
    [InlineData("class", "TDyn", false)]
    [InlineData("class", "TProc", false)]
    [InlineData("class", "TMeth", false)]
    [InlineData("class", "TRef", false)]
    // record
    [InlineData("record", "Integer", true)]
    [InlineData("record", "Int64", true)]
    [InlineData("record", "Byte", true)]
    [InlineData("record", "Char", true)]
    [InlineData("record", "AnsiChar", true)]
    [InlineData("record", "Boolean", true)]
    [InlineData("record", "TColor", true)]
    [InlineData("record", "TSmall", true)]
    [InlineData("record", "Double", true)]
    [InlineData("record", "Single", true)]
    [InlineData("record", "Currency", true)]
    [InlineData("record", "TRec", true)]
    [InlineData("record", "TManaged", true)]
    [InlineData("record", "TPair<string>", true)]
    [InlineData("record", "TArr", true)]
    [InlineData("record", "TSet", true)]
    [InlineData("record", "string", false)]
    [InlineData("record", "UnicodeString", false, "string")]
    [InlineData("record", "AnsiString", false)]
    [InlineData("record", "RawByteString", false)]
    [InlineData("record", "ShortString", false)]
    [InlineData("record", "TShort", false)]
    [InlineData("record", "TDyn", false)]
    [InlineData("record", "TBase", false)]
    [InlineData("record", "TBox<Integer>", false)]
    [InlineData("record", "IGreeter", false)]
    [InlineData("record", "PRec", false)]
    [InlineData("record", "Pointer", false)]
    [InlineData("record", "PChar", false)]
    [InlineData("record", "TMeta", false)]
    [InlineData("record", "TProc", false)]
    [InlineData("record", "TMeth", false)]
    [InlineData("record", "TRef", false)]
    // constructor
    [InlineData("constructor", "TBase", true)]
    [InlineData("constructor", "TObject", true)]
    [InlineData("constructor", "TOwn", true)]
    [InlineData("constructor", "TBox<Integer>", true)]
    [InlineData("constructor", "TNoDefault", false)]
    [InlineData("constructor", "TRec", false)]
    [InlineData("constructor", "IGreeter", false)]
    [InlineData("constructor", "Integer", false)]
    [InlineData("constructor", "string", false)]
    [InlineData("constructor", "TMeta", false)]
    [InlineData("constructor", "PRec", false)]
    [InlineData("constructor", "TDyn", false)]
    [InlineData("constructor", "TRef", false)]
    // a class type
    [InlineData("TBase", "TBase", true)]
    [InlineData("TBase", "TChild", true)]
    [InlineData("TBase", "TGrand", true)]
    [InlineData("TChild", "TBase", false)]                                       // an ancestor is not enough
    [InlineData("TBase", "TOther", false)]
    [InlineData("TBase", "TObject", false)]
    [InlineData("TBase", "IGreeter", false)]
    [InlineData("TBase", "Integer", false)]
    [InlineData("TBase", "TRec", false)]
    [InlineData("TBase", "TMeta", false)]
    [InlineData("TBase", "PRec", false)]
    [InlineData("TBase", "string", false)]
    // an interface type
    [InlineData("IGreeter", "IGreeter", true)]
    [InlineData("IGreeter", "ILoud", true)]                                      // a descendant interface
    [InlineData("IGreeter", "TGreeter", true)]
    [InlineData("IGreeter", "TGreeterKid", true)]                                // implemented by the ancestor
    [InlineData("IGreeter", "TLoud", true)]                                      // implements a descendant of it
    [InlineData("ILoud", "IGreeter", false)]
    [InlineData("ILoud", "TGreeter", false)]
    [InlineData("IGreeter", "IOther", false)]
    [InlineData("IGreeter", "IInterface", false)]
    [InlineData("IGreeter", "TBase", false)]
    [InlineData("IGreeter", "Integer", false)]
    [InlineData("IGreeter", "TRec", false)]
    [InlineData("IGreeter", "TRef", false)]
    [InlineData("IGreeter", "string", false)]
    public void Constraints_Satisfaction(string constraint, string argument, bool satisfied, string? display = null)
    {
        var text = ConstraintProgram.Replace("{DECLARATION}", $"TG<T: {constraint}> = class end;", StringComparison.Ordinal)
            .Replace("{USE}", $"var V: TG<{argument}>;", StringComparison.Ordinal);
        var (line, column) = PositionOf(text, $"TG<{argument}>;");

        var result = BindRestricted(text);

        string[] expected = satisfied ? [] : [$"E2704({line},{column + 3}): Type argument '{display ?? argument}' does not satisfy constraint '{constraint}' of 'T'"];
        Assert.Equal(expected, Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        // LANG-984: no instance after an E2704 — the variable has the error type
        var variable = result.Unit.ImplementationMembers.Concat(result.Unit.InterfaceMembers).OfType<VariableSymbol>().Single(v => v.Name == "V");
        Assert.Equal(satisfied, variable.Type.TypeKind != TypeKind.Error);
    }

    private const string ConstructorProgram = """
        program P;
        type
          TPlain = class end;
          TOwn = class constructor Create; end;
          TNoDefault = class constructor Create(A: Integer); end;
          TNoDefaultKid = class(TNoDefault) end;
          THidden = class private constructor Create; end;
          TStrictHidden = class strict private constructor Create; end;
          TProt = class protected constructor Create; end;
          TStrictProt = class strict protected constructor Create; end;
          TProtKid = class(TProt) end;
          TProtOver = class protected constructor Create; overload; end;
          TOverload = class constructor Create(A: Integer); overload; end;
          TDefaulted = class constructor Create(A: Integer = 0); end;
          TBoth = class constructor Create(A: Integer = 0); overload; end;
          TTwo = class constructor Create(A: Integer = 0); overload; constructor Create(const S: string = ''); overload; end;
          TAbstract = class procedure M; virtual; abstract; end;
          TField = class Create: Integer; end;
          TFunc = class class function Create: TFunc; end;
          TReopened = class(TNoDefault) constructor Create; end;
          TVirt = class constructor Create; virtual; end;
          TVirtKid = class(TVirt) constructor Create; override; end;
          TPublished = class published constructor Create; end;
          TF<T: constructor> = class function Make: T; end;
        constructor TOwn.Create; begin end;
        constructor TNoDefault.Create(A: Integer); begin end;
        constructor THidden.Create; begin end;
        constructor TStrictHidden.Create; begin end;
        constructor TProt.Create; begin end;
        constructor TStrictProt.Create; begin end;
        constructor TProtOver.Create; begin end;
        constructor TOverload.Create(A: Integer); begin end;
        constructor TDefaulted.Create(A: Integer); begin end;
        constructor TBoth.Create(A: Integer); begin end;
        constructor TTwo.Create(A: Integer); begin end;
        constructor TTwo.Create(const S: string); begin end;
        class function TFunc.Create: TFunc; begin Result := nil; end;
        constructor TReopened.Create; begin end;
        constructor TVirt.Create; begin end;
        constructor TVirtKid.Create; begin end;
        constructor TPublished.Create; begin end;
        function TF<T>.Make: T; begin Result := T.Create; end;
        var
          V: TF<{ARGUMENT}>;
        begin
        end.
        """;

    /// <summary>
    /// LANG-983 <c>constructor</c>: satisfied by what the call <c>X.Create</c> without arguments, written outside the class
    /// hierarchy, resolves to (LANG-441, 442) when that is a <c>public</c> or <c>published</c> constructor — an inherited one, one
    /// next to a <c>Create(…)</c> with <c>overload</c>, one whose parameters all have defaults, the one of an abstract class (W1061
    /// where the body constructs it, as for the call). Not satisfied: a parameterless <c>Create</c> of an ancestor that a
    /// <c>Create(…)</c> without <c>overload</c> hides (also for a descendant), a <c>private</c> or <c>protected</c> one (strict or
    /// not, also when the unit could call it), an ambiguous call, a <c>Create</c> that is a field or no constructor. In the body
    /// <c>T.Create</c> calls the constructor found (<paramref name="constructs"/>: its class).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-983")]
    [InlineData("TPlain", "TObject")]
    [InlineData("TOwn", "TOwn")]
    [InlineData("TOverload", "TObject")]                                         // Create(A) has overload: the ancestor's one is visible
    [InlineData("TDefaulted", "TDefaulted")]                                     // X.Create resolves to Create(A: Integer = 0)
    [InlineData("TBoth", "TBoth")]                                               // the method of the more derived class (LANG-442)
    [InlineData("TAbstract", "TObject")]
    [InlineData("TReopened", "TReopened")]
    [InlineData("TVirt", "TVirt")]
    [InlineData("TVirtKid", "TVirtKid")]
    [InlineData("TPublished", "TPublished")]
    [InlineData("TNoDefault", null)]                                             // hidden: row 1 of the golden generics/ErrGenericConstraintKinds
    [InlineData("TNoDefaultKid", null)]
    [InlineData("THidden", null)]                                                // row 2 of the golden
    [InlineData("TStrictHidden", null)]
    [InlineData("TProt", null)]
    [InlineData("TStrictProt", null)]
    [InlineData("TProtKid", null)]
    [InlineData("TProtOver", null)]                                              // it hides the ancestor's one of the same parameters
    [InlineData("TTwo", null)]                                                   // X.Create is ambiguous (E2122)
    [InlineData("TField", null)]
    [InlineData("TFunc", null)]                                                  // a class function is no constructor
    public void Constraints_ConstructorFollowsCallLookup(string argument, string? constructs)
    {
        var text = ConstructorProgram.Replace("{ARGUMENT}", argument, StringComparison.Ordinal);
        var (line, column) = PositionOf(text, $"TF<{argument}>;");

        var result = BindRestricted(text);

        var diagnostics = Format(result.Diagnostics).Where(d => !d.StartsWith('H')).ToArray();
        if (constructs is null)
        {
            Assert.Equal([$"E2704({line},{column + 3}): Type argument '{argument}' does not satisfy constraint 'constructor' of 'T'"], diagnostics);
            Assert.DoesNotContain(result.Routines, r => r.Routine.Name == "Make");   // no instance, so no diagnostics from its body
            return;
        }

        var create = PositionOf(text, "T.Create;");
        Assert.Equal(argument == "TAbstract" ? [$"W1061({create.Line},{create.Column}): Constructing instance of 'TAbstract' containing abstract method 'TAbstract.M'"] : [],
            diagnostics);
        var body = result.Routines.Single(r => r.Routine.Name == "Make" && r.Routine.ContainingSymbol!.Name == $"TF<{argument}>").Body;
        var call = Assert.IsType<BoundCallExpression>(Unconverted(Assert.IsType<BoundAssignmentStatement>(body.Statements.Single()).Value));
        var constructor = Assert.IsType<MethodSymbol>(call.Routine);
        Assert.Equal(MethodKind.Constructor, constructor.MethodKind);
        Assert.Equal(constructs, constructor.Owner.Name);

        static BoundExpression Unconverted(BoundExpression e) => e is BoundConversionExpression conversion ? Unconverted(conversion.Operand) : e;
    }

    private const string OrderProgram = """
        program P;
        type
          TBase = class end;
          TNoDefault = class(TBase) constructor Create(A: Integer); end;
          IGreeter = interface procedure Hi; end;
          IOther = interface procedure Ho; end;
          TRec = record X: Integer; end;
          TPairOf<A, B: class> = class end;
          TMap<K: record; V: class> = class end;
          TAll<T: constructor, IOther, class, TBase, IGreeter> = class end;
          TTwice<T: TBase, class, TBase, class> = class end;
          TObjList<T: class> = class end;
          TUtil = class
            class procedure Keep<T: class>(const X: T); static;
            class procedure Two<A: class; B: record>(const X: A; const Y: B); static;
            class function Make<T: class, constructor>: T; static;
            class procedure Local; static;
            procedure Inst<T: IGreeter>(const X: T);
          end;
          TOuter<T> = class
            function Inner: TObjList<T>;
            procedure Run;
          end;
        constructor TNoDefault.Create(A: Integer); begin end;
        class procedure TUtil.Keep<T>(const X: T); begin end;
        class procedure TUtil.Two<A, B>(const X: A; const Y: B); begin end;
        class function TUtil.Make<T>: T; begin Result := nil; end;
        class procedure TUtil.Local; begin Keep<Integer>(5); Keep(6); end;
        procedure TUtil.Inst<T>(const X: T); begin end;
        function TOuter<T>.Inner: TObjList<T>; begin Result := nil; end;
        procedure TOuter<T>.Run;
        var L: TObjList<T>;
        begin
          TUtil.Keep<T>(Default(T));
          TUtil.Keep(Default(T));
        end;
        var
          p1: TPairOf<TBase, Integer>;
          p2: TPairOf<Integer, string>;
          m1: TMap<TBase, Integer>;
          a1: TAll<TRec>;
          t1: TTwice<Integer>;
          o1: TOuter<Integer>;
          U: TUtil;
          B: TBase;
          S: string;
        begin
          TUtil.Keep<Integer>(5);
          TUtil.Keep(5);
          TUtil.Two<Integer, string>(1, S);
          TUtil.Two(1, S);
          B := TUtil.Make<TNoDefault>;
          U.Inst<TBase>(B);
          U.Inst(B);
        end.
        """;

    /// <summary>
    /// LANG-984: one E2704 per failed constraint — the type arguments in order, for each its constraints in the order
    /// <c>class</c>, <c>record</c>, <c>constructor</c>, then the constraint types as written (one written twice once); at the
    /// type argument where the arguments are written (a type instance, a generic method call with a list, qualified or not), at
    /// the method name where they are inferred; an instantiation written in a generic body is reported per instance, inside E2710.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-984")]
    public void Constraints_ReportedPerArgumentInOrder()
    {
        var text = OrderProgram;
        string At(string piece, string inner, int occurrence = 0)
        {
            var start = text.IndexOf(piece, StringComparison.Ordinal);
            Assert.True(start >= 0, piece);
            var index = start - 1;
            for (var i = 0; i <= occurrence; i++)
            {
                index = text.IndexOf(inner, index + 1, StringComparison.Ordinal);
            }

            Assert.True(index >= start && index < start + piece.Length, inner);
            return $"({text[..index].Count(c => c == '\n') + 1},{index - text.LastIndexOf('\n', index)})";
        }

        static string Failed(string argument, string constraint, string parameter) =>
            $"Type argument '{argument}' does not satisfy constraint '{constraint}' of '{parameter}'";
        string[] expected =
        [
            // (the binder's diagnostics are in source order)
            // a generic's declaration: per instance, inside E2710
            $"E2710{At("function Inner: TObjList<T>", "T", 1)}: In instantiation 'TOuter<Integer>': {Failed("Integer", "class", "T")}",
            // an unqualified generic method call: at the written argument; at the name when inferred
            $"E2704{At("Keep<Integer>(5); Keep(6)", "Integer")}: {Failed("Integer", "class", "T")}",
            $"E2704{At("Keep<Integer>(5); Keep(6)", "Keep", 1)}: {Failed("Integer", "class", "T")}",
            // a generic's method body: per instance, inside E2710
            $"E2710{At("var L: TObjList<T>;", "T", 1)}: In instantiation 'TOuter<Integer>': {Failed("Integer", "class", "T")}",
            $"E2710{At("TUtil.Keep<T>(Default(T));", "T", 1)}: In instantiation 'TOuter<Integer>': {Failed("Integer", "class", "T")}",
            $"E2710{At("  TUtil.Keep(Default(T));", "Keep")}: In instantiation 'TOuter<Integer>': {Failed("Integer", "class", "T")}",
            // two type arguments: only the failing one; both, in order
            $"E2704{At("p1: TPairOf<TBase, Integer>", "Integer")}: {Failed("Integer", "class", "B")}",
            $"E2704{At("p2: TPairOf<Integer, string>", "Integer")}: {Failed("Integer", "class", "A")}",
            $"E2704{At("p2: TPairOf<Integer, string>", "string")}: {Failed("string", "class", "B")}",
            $"E2704{At("m1: TMap<TBase, Integer>", "TBase")}: {Failed("TBase", "record", "K")}",
            $"E2704{At("m1: TMap<TBase, Integer>", "Integer")}: {Failed("Integer", "class", "V")}",
            // one argument failing every constraint: class, constructor, then the types as written
            $"E2704{At("a1: TAll<TRec>", "TRec")}: {Failed("TRec", "class", "T")}",
            $"E2704{At("a1: TAll<TRec>", "TRec")}: {Failed("TRec", "constructor", "T")}",
            $"E2704{At("a1: TAll<TRec>", "TRec")}: {Failed("TRec", "IOther", "T")}",
            $"E2704{At("a1: TAll<TRec>", "TRec")}: {Failed("TRec", "TBase", "T")}",
            $"E2704{At("a1: TAll<TRec>", "TRec")}: {Failed("TRec", "IGreeter", "T")}",
            // a constraint written twice counts once
            $"E2704{At("t1: TTwice<Integer>", "Integer")}: {Failed("Integer", "class", "T")}",
            $"E2704{At("t1: TTwice<Integer>", "Integer")}: {Failed("Integer", "TBase", "T")}",
            // generic methods: written (at the argument) and inferred (at the method name)
            $"E2704{At("TUtil.Keep<Integer>(5);", "Integer")}: {Failed("Integer", "class", "T")}",
            $"E2704{At("  TUtil.Keep(5);", "Keep")}: {Failed("Integer", "class", "T")}",
            $"E2704{At("TUtil.Two<Integer, string>(1, S);", "Integer")}: {Failed("Integer", "class", "A")}",
            $"E2704{At("TUtil.Two<Integer, string>(1, S);", "string")}: {Failed("string", "record", "B")}",
            $"E2704{At("  TUtil.Two(1, S);", "Two")}: {Failed("Integer", "class", "A")}",
            $"E2704{At("  TUtil.Two(1, S);", "Two")}: {Failed("string", "record", "B")}",
            $"E2704{At("TUtil.Make<TNoDefault>;", "TNoDefault")}: {Failed("TNoDefault", "constructor", "T")}",
            $"E2704{At("U.Inst<TBase>(B);", "TBase")}: {Failed("TBase", "IGreeter", "T")}",
            $"E2704{At("  U.Inst(B);", "Inst")}: {Failed("TBase", "IGreeter", "T")}",
        ];

        Assert.Equal(expected, Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
    }
}
