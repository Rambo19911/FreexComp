using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-420: where a method may have type parameters (LANG-978) — the restrictions are reported at the declaration, once, whether or
/// not the method is called (E2713, E2714) — and a generic method as a procedural value (LANG-981).
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string RestrictionProgram = """
        program P;
        type
          TBase = class
            procedure O(const X: Integer); virtual;
          end;
          TFoo = class(TBase)
            {CLASS}
          end;
          TRec = record
            F: Integer;
            {RECORD}
          end;
          IFoo = interface
            {INTERFACE}
          end;
        procedure TBase.O(const X: Integer); begin end;
        {IMPL}
        var
          Foo: TFoo;
          Rec: TRec;
          I: Integer;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-978 table: a generic constructor, destructor or operator, a generic method under <c>published</c>, and a generic method
    /// with <c>virtual</c>, <c>dynamic</c>, <c>override</c>, <c>abstract</c> or <c>message</c> is E2713 at the method name of the
    /// declaration — one diagnostic per declaration (the first row of the table that applies, directives in source order), when
    /// nothing calls the method and when something does: the declaration is kept without the offending property, so the
    /// implementation and a call give no second diagnostic. A generic method of an interface is E2714 (no phrase).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    // rows 1–8 of the golden generics/ErrGenericMethod
    [InlineData("CLASS", "procedure V<T>(const X: T); virtual;", "procedure TFoo.V<T>(const X: T); begin end;", "Foo.V(1)", "V", "virtual")]
    [InlineData("CLASS", "procedure D<T>(const X: T); dynamic;", "procedure TFoo.D<T>(const X: T); begin end;", "Foo.D<Integer>(1)", "D", "dynamic")]
    [InlineData("CLASS", "procedure A<T>(const X: T); virtual; abstract;", "", "Foo.A(1)", "A", "virtual")]
    [InlineData("CLASS", "procedure O<T>(const X: T); override;", "procedure TFoo.O<T>(const X: T); begin end;", "Foo.O<string>('x')", "O", "an override")]
    [InlineData("CLASS", "procedure M<T>(var Msg: T); message 1;", "procedure TFoo.M<T>(var Msg: T); begin end;", "Foo.M(I)", "M", "a message handler")]
    [InlineData("CLASS", "published procedure P<T>(const X: T);", "procedure TFoo.P<T>(const X: T); begin end;", "Foo.P(1)", "P", "published")]
    [InlineData("CLASS", "constructor Create<T>(const X: T);", "constructor TFoo.Create<T>(const X: T); begin end;", "Foo := TFoo.Create<Integer>(1)", "Create", "a constructor")]
    [InlineData("INTERFACE", "procedure I<T>(const X: T);", "", "I := 0", "I", null)]
    // the other kinds and the other places
    [InlineData("CLASS", "destructor Done<T>(const X: T);", "destructor TFoo.Done<T>(const X: T); begin end;", "Foo.Done<Integer>(1)", "Done", "a destructor")]
    [InlineData("CLASS", "class operator Add<T>(const A, B: TFoo): TFoo;", "", "Foo := TFoo.Add<Integer>(Foo, Foo)", "Add", "an operator")]
    [InlineData("CLASS", "class procedure C<T>(const X: T); virtual;", "class procedure TFoo.C<T>(const X: T); begin end;", "TFoo.C(1)", "C", "virtual")]
    [InlineData("CLASS", "procedure A<T>(const X: T); abstract;", "", "Foo.A(1)", "A", "abstract")]
    [InlineData("RECORD", "constructor Make<T>(const X: T);", "constructor TRec.Make<T>(const X: T); begin end;", "Rec := TRec.Make<Integer>(1)", "Make", "a constructor")]
    [InlineData("RECORD", "destructor Done<T>(const X: T);", "", "Rec.Done<Integer>(1)", "Done", "a destructor")]
    [InlineData("RECORD", "class operator Add<T>(const A, B: TRec): TRec;", "class operator TRec.Add<T>(const A, B: TRec): TRec; begin end;", "Rec := TRec.Add<Integer>(Rec, Rec)", "Add", "an operator")]
    [InlineData("RECORD", "procedure V<T>(const X: T); virtual;", "procedure TRec.V<T>(const X: T); begin end;", "Rec.V(1)", "V", "virtual")]
    // a kind the type cannot declare at all (an operator of a class, a destructor of a record): an inferred call adds no E2711 either
    [InlineData("CLASS", "class operator Add<T>(const A: TFoo; const B: T): TFoo;", "", "Foo := TFoo.Add(Foo, 1)", "Add", "an operator")]
    [InlineData("RECORD", "destructor Done<T>(const X: T);", "", "Rec.Done(1)", "Done", "a destructor")]
    // one diagnostic: the first row of the table that applies, directives in source order
    [InlineData("CLASS", "constructor Make<T>(const X: T); virtual;", "constructor TFoo.Make<T>(const X: T); begin end;", "Foo := TFoo.Make(1)", "Make", "a constructor")]
    [InlineData("CLASS", "published procedure P<T>(const X: T); virtual;", "procedure TFoo.P<T>(const X: T); begin end;", "Foo.P(1)", "P", "published")]
    [InlineData("CLASS", "procedure A<T>(const X: T); abstract; virtual;", "", "Foo.A(1)", "A", "abstract")]
    [InlineData("CLASS", "procedure O<T>(const X: T); override; final;", "procedure TFoo.O<T>(const X: T); begin end;", "Foo.O<Byte>(1)", "O", "an override")]
    public void GenericMethod_Restrictions(string where, string declaration, string implementation, string call, string name, string? phrase)
    {
        foreach (var statement in new[] { "I := 0", call })                    // never called; called
        {
            var text = RestrictionProgram.Replace("{" + where + "}", declaration).Replace("{CLASS}", "").Replace("{RECORD}", "").Replace("{INTERFACE}", "")
                .Replace("{IMPL}", implementation).Replace("{CALL}", statement);
            var (line, column) = PositionOf(text, " " + name + "<");

            Assert.Equal(
                [phrase is null
                    ? $"E2714({line},{column + 1}): Generic method '{name}' is not allowed in an interface"
                    : $"E2713({line},{column + 1}): Generic method '{name}' cannot be {phrase}"],
                Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
        }
    }

    /// <summary>
    /// LANG-978 table: a generic method in an interface or a dispatch interface is E2714 at the method name (not kept); a helper keeps
    /// its E2900, a generic global routine its E2700.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    [InlineData("type IFoo = interface function G<K, V>(const X: K): V; end;", "E2714", "G", "Generic method 'G' is not allowed in an interface")]
    [InlineData("type IFoo = dispinterface ['{00000000-0000-0000-C000-000000000046}'] procedure I<T>(X: Integer); dispid 1; end;", "E2714", "I",
        "Generic method 'I' is not allowed in an interface")]
    [InlineData("type TH = record helper for Integer procedure H<T>(const X: T); end;", "E2900", "procedure H", "Feature not supported: generic method")]
    [InlineData("procedure G<T>(const X: T); begin end;", "E2700", "G", "Generic global procedures and functions are not supported; use a generic method")]
    public void GenericMethod_Restrictions_OtherPlaces(string declaration, string code, string at, string message)
    {
        var text = $"program P;\n{declaration}\nbegin\nend.\n";
        var column = declaration.IndexOf(at.Contains(' ') ? at : " " + at + "<", StringComparison.Ordinal) + (at.Contains(' ') ? 1 : 2);

        Assert.Equal(
            [$"{code}(2,{column}): {message}"],
            Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>
    /// LANG-730 / LANG-978 (C-431): <c>final</c> and <c>reintroduce</c> on a generic method of a record are reported where the method is
    /// declared, like on a non-generic record method — E2730 at the directive, the first of them, with the same text — whether or not
    /// the method is called; the declaration is kept without the directive, so its implementation and its calls give no second
    /// diagnostic. One diagnostic per declaration: next to a directive of the LANG-978 table the E2713 stands alone.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-730")]
    [Trait("Req", "LANG-978")]
    [InlineData("procedure F<T>(const X: T); final;", "procedure TRec.F<T>(const X: T); begin end;", "Rec.F(1)", "final", null)]
    [InlineData("procedure N<T>(const X: T); reintroduce;", "procedure TRec.N<T>(const X: T); begin end;", "Rec.N<string>('x')", "reintroduce", null)]
    [InlineData("class function S<T>(X: T): T; reintroduce; static;", "class function TRec.S<T>(X: T): T; begin Result := X end;", "I := TRec.S(1)", "reintroduce", null)]
    [InlineData("procedure B<T>(const X: T); reintroduce; final;", "procedure TRec.B<T>(const X: T); begin end;", "Rec.B(1)", "reintroduce", null)]
    [InlineData("procedure B<T>(const X: T); final; reintroduce;", "procedure TRec.B<T>(const X: T); begin end;", "Rec.B(1)", "final", null)]
    [InlineData("procedure O<T>(const X: T); overload; final;", "procedure TRec.O<T>(const X: T); begin end;", "Rec.O(1)", "final", null)]
    [InlineData("procedure V<T>(const X: T); final; virtual;", "procedure TRec.V<T>(const X: T); begin end;", "Rec.V(1)", null, "virtual")]
    [InlineData("procedure V<T>(const X: T); override; final;", "procedure TRec.V<T>(const X: T); begin end;", "Rec.V(1)", null, "an override")]
    public void GenericMethod_RecordDirectives(string declaration, string implementation, string call, string? directive, string? phrase)
    {
        var name = declaration[(declaration.IndexOf('<') - 1)..declaration.IndexOf('<')];
        foreach (var statement in new[] { "I := 0", call })                    // never called; called
        {
            var text = RestrictionProgram.Replace("{RECORD}", declaration).Replace("{CLASS}", "").Replace("{INTERFACE}", "")
                .Replace("{IMPL}", implementation).Replace("{CALL}", statement);
            var (line, column) = PositionOf(text, directive is null ? " " + name + "<" : " " + directive + ";");

            Assert.Equal(
                [directive is null
                    ? $"E2713({line},{column + 1}): Generic method '{name}' cannot be {phrase}"
                    : $"E2730({line},{column + 1}): '{directive}' is not allowed in a record"],
                Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
        }

        if (directive is not null)
        {
            // the control: the non-generic record method with these directives — the same code and text, at its directive
            var plain = RestrictionProgram.Replace("{RECORD}", "procedure Plain(const X: Integer);" + declaration[(declaration.IndexOf(';') + 1)..].Replace(" static;", ""))
                .Replace("{CLASS}", "").Replace("{INTERFACE}", "").Replace("{IMPL}", "").Replace("{CALL}", "I := 0");
            var (line, column) = PositionOf(plain, " " + directive + ";");
            Assert.Equal([$"E2730({line},{column + 1}): '{directive}' is not allowed in a record"], Format(BindRestricted(plain).Diagnostics).Where(d => !d.StartsWith('H')));
        }
    }

    /// <summary>
    /// LANG-978: in a class compiled with type information the members before the first visibility keyword are published (LANG-611);
    /// a generic method there is <c>public</c>, without a diagnostic — only a section the source marks <c>published</c> is E2713.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    public void GenericMethod_DefaultSectionOfATypeInfoClass_IsPublic()
    {
        var result = BindRestricted("""
            program P;
            type
              {$M+}
              TFoo = class
                procedure Plain;
                procedure G<T>(const X: T);
              end;
              {$M-}
            procedure TFoo.Plain; begin end;
            procedure TFoo.G<T>(const X: T); begin end;
            var
              Foo: TFoo;
            begin
              Foo.G(1);
            end.
            """);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        var type = Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TFoo"));
        Assert.True(type.IsTypeInfo);
        Assert.Equal(Visibility.Published, Assert.Single(type.Methods).Visibility);          // the control: the default section is published
        var generic = Assert.Single(type.GenericMethods);
        Assert.Equal(Visibility.Public, generic.Visibility);
        Assert.Equal(Visibility.Public, Assert.Single(generic.Instances).Value.Visibility);
    }

    /// <summary>
    /// LANG-978: a generic method and its instances have no entry in the method table of the class RTTI — the table lists the
    /// non-generic methods only, although the instance <c>G&lt;Integer&gt;</c> is defined in the module.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    public void GenericMethod_NoRttiMethodEntry()
    {
        var module = IrSnapshotTests.Emit("""
            program M;
            type
              {$M+}
              TFoo = class
                procedure Plain;
                procedure G<T>(const X: T);
              public
                procedure Pub<T>(const X: T);
              end;
              {$M-}
            procedure TFoo.Plain; begin end;
            procedure TFoo.G<T>(const X: T); begin end;
            procedure TFoo.Pub<T>(const X: T); begin end;
            var
              Foo: TFoo;
            begin
              Foo.G(1);
              Foo.Pub('x');
            end.
            """);

        Assert.Contains("@\"M.TFoo.G<Integer>\"(", module, StringComparison.Ordinal);       // the instances exist
        Assert.Contains("@\"M.TFoo.Pub<", module, StringComparison.Ordinal);
        var table = Assert.Single(module.Split('\n'), l => l.StartsWith("@\"M.TFoo.$rtti.methods\" = ", StringComparison.Ordinal));
        Assert.Contains("[1 x {", table, StringComparison.Ordinal);                            // one entry: Plain
        Assert.Contains("@\"M.TFoo.Plain\"", table, StringComparison.Ordinal);
        Assert.DoesNotContain("TFoo.G", table, StringComparison.Ordinal);
        Assert.DoesNotContain("TFoo.Pub", table, StringComparison.Ordinal);
    }

    private const string ValueProgram = """
        program P;
        type
          TIntFunc = function(X: Integer): Integer;
          TIntMethod = function(X: Integer): Integer of object;
          TIntRef = reference to function(X: Integer): Integer;
          TUtil = class
            class function Twice<T>(X: T): T; static;
            class function Cls<T>(X: T): T;
            class function Plain(X: Integer): Integer; static;
            class function Mix(X: Integer): Integer; overload; static;
            class function Mix<T>(X: T): T; overload; static;
            class function MixS(const S: string): Integer; overload; static;
            class function MixS<T>(X: T): T; overload; static;
            class function Two<T>(X, Y: T): T; overload; static;
            class function Two<T>(X: T): T; overload; static;
            class function Keep<T: class>(X: Integer): Integer; static;
            class function Make<T>: T; static;
            class procedure Run;
          strict private
            class function Secret<T>(X: T): T; static;
          end;
          TUtilClass = class of TUtil;
          TObj = class
            function Scale<T>(X: T): T;
            function Mix(X: Integer): Integer; overload;
            function Mix<T>(X: T): T; overload;
            procedure Run;
          end;
        class function TUtil.Twice<T>(X: T): T; begin Result := X end;
        class function TUtil.Cls<T>(X: T): T; begin Result := X end;
        class function TUtil.Plain(X: Integer): Integer; begin Result := X end;
        class function TUtil.Mix(X: Integer): Integer; begin Result := X end;
        class function TUtil.Mix<T>(X: T): T; begin Result := X end;
        class function TUtil.MixS(const S: string): Integer; begin Result := 0 end;
        class function TUtil.MixS<T>(X: T): T; begin Result := X end;
        class function TUtil.Two<T>(X, Y: T): T; begin Result := X end;
        class function TUtil.Two<T>(X: T): T; begin Result := X end;
        class function TUtil.Keep<T>(X: Integer): Integer; begin Result := X end;
        class function TUtil.Make<T>: T; begin Result := Default(T) end;
        class function TUtil.Secret<T>(X: T): T; begin Result := X end;
        function TObj.Scale<T>(X: T): T; begin Result := X end;
        function TObj.Mix(X: Integer): Integer; begin Result := X end;
        function TObj.Mix<T>(X: T): T; begin Result := X end;
        function Apply(const Ref: TIntRef; X: Integer): Integer; begin Result := Ref(X) end;
        var
          F: TIntFunc;
          M: TIntMethod;
          R: TIntRef;
          P: Pointer;
          B: Boolean;
          N: Integer;
          Obj: TObj;
          Meta: TUtilClass;
        class procedure TUtil.Run;
        begin
          {STATIC};
        end;
        procedure TObj.Run;
        begin
          {INSTANCE};
        end;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-981: where a method name is a procedural value — assigned or passed to a plain procedural, method-pointer or
    /// method-reference type, or the operand of <c>@</c> — <c>N&lt;X&gt;</c> denotes the instance (the four forms of the golden
    /// generics/GenericMethodAddress first); a name without type arguments never denotes a generic method: the non-generic methods of
    /// the name are taken as before, and a name with generic methods only is E2711.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-981")]
    // the four forms of the golden
    [InlineData("CALL", "F := TUtil.Twice<Integer>", "@Twice<Integer>")]
    [InlineData("CALL", "M := Obj.Scale<Integer>", "method Scale<Integer>")]
    [InlineData("CALL", "R := TUtil.Twice<Integer>", "@Twice<Integer>")]
    [InlineData("CALL", "N := Apply(TUtil.Twice<Integer>, 8)", "@Twice<Integer>")]
    [InlineData("CALL", "B := @TUtil.Twice<Integer> = Pointer(F)", "@Twice<Integer>")]
    // the other forms: with @, a method of an instance as a reference, a class method, overloads of one number of type parameters, unqualified
    [InlineData("CALL", "F := @TUtil.Twice<Integer>", "@Twice<Integer>")]
    [InlineData("CALL", "P := @TUtil.Twice<string>", "@Twice<string>")]
    [InlineData("CALL", "M := @Obj.Scale<Integer>", "method Scale<Integer>")]
    [InlineData("CALL", "R := Obj.Scale<Integer>", "method Scale<Integer>")]
    [InlineData("CALL", "M := TUtil.Cls<Integer>", "method Cls<Integer>")]
    [InlineData("CALL", "M := Meta.Cls<Integer>", "method Cls<Integer>")]                         // through a class-reference value
    [InlineData("CALL", "F := TUtil.Two<Integer>", "@Two<Integer>")]
    [InlineData("STATIC", "F := Twice<Integer>", "@Twice<Integer>")]
    [InlineData("STATIC", "F := Secret<Integer>", "@Secret<Integer>")]                          // strict private, inside the class
    [InlineData("STATIC", "P := @Secret<Integer>", "@Secret<Integer>")]
    [InlineData("INSTANCE", "M := Scale<Integer>", "method Scale<Integer>")]
    [InlineData("INSTANCE", "R := Scale<Integer>", "method Scale<Integer>")]
    [InlineData("CALL", "N := TUtil.Make<Integer>", null)]                                       // no procedural context: a call, as before
    // a name without type arguments next to a non-generic method: that method, as before
    [InlineData("CALL", "F := TUtil.Mix", "@Mix")]
    [InlineData("CALL", "F := @TUtil.Mix", "@Mix")]
    [InlineData("CALL", "M := Obj.Mix", "method Mix")]
    [InlineData("CALL", "R := TUtil.Mix", "@Mix")]
    [InlineData("CALL", "R := Obj.Mix", "method Mix")]
    [InlineData("CALL", "N := Apply(TUtil.Mix, 8)", "@Mix")]
    [InlineData("CALL", "P := @TUtil.Mix", "@Mix")]
    [InlineData("CALL", "P := @TUtil.Plain", "@Plain")]
    public void GenericMethod_AsProceduralValue(string where, string statement, string? expected)
    {
        var result = BindValue(statement, where);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        var body = where == "CALL" ? result.Bodies[0] : result.Routines.Single(r => r.Routine.Name == "Run" && r.Routine.ContainingSymbol!.Name == (where == "STATIC" ? "TUtil" : "TObj")).Body;
        var value = Assert.IsType<BoundAssignmentStatement>(body.Statements.Single()).Value;
        Assert.Equal(expected, ProceduralValue(value));
        if (expected?.Contains('<') == true)
        {
            // it counts as used: the instance is recorded for the unit, as for a call
            Assert.Contains(result.Unit.UsedInstances, u => u is MethodSymbol { GenericMethodDefinition: not null } m && expected.EndsWith(InstanceName(m), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// LANG-981: the diagnostics of a generic method used as a value — E2711 for a name without type arguments when the type has
    /// only generic methods of that name; with type arguments the instance is checked like any routine of that signature (E2010),
    /// its constraints as for a call (E2704), the number of type arguments (E2703), its visibility (E2610). Next to a non-generic method that does not fit
    /// the type, the generic one is not tried (E2010). Outside a procedural context a mixed name without arguments is a call (E2123).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-981")]
    [InlineData("CALL", "F := TUtil.Twice", "E2711", 14, "Cannot infer type arguments for 'Twice'; specify them explicitly")]          // row 12 of generics/ErrGenericMethod
    [InlineData("CALL", "F := @TUtil.Twice", "E2711", 15, "Cannot infer type arguments for 'Twice'; specify them explicitly")]
    [InlineData("CALL", "M := Obj.Scale", "E2711", 12, "Cannot infer type arguments for 'Scale'; specify them explicitly")]
    [InlineData("CALL", "R := TUtil.Twice", "E2711", 14, "Cannot infer type arguments for 'Twice'; specify them explicitly")]
    [InlineData("CALL", "P := @TUtil.Twice", "E2711", 15, "Cannot infer type arguments for 'Twice'; specify them explicitly")]
    [InlineData("CALL", "N := Apply(TUtil.Twice, 8)", "E2711", 20, "Cannot infer type arguments for 'Twice'; specify them explicitly")]
    [InlineData("STATIC", "F := Twice", "E2711", 8, "Cannot infer type arguments for 'Twice'; specify them explicitly")]
    [InlineData("INSTANCE", "M := Scale", "E2711", 8, "Cannot infer type arguments for 'Scale'; specify them explicitly")]
    [InlineData("CALL", "F := TUtil.MixS", "E2010", 8, "Incompatible types: 'function(const string): Integer' and 'TIntFunc'")]
    [InlineData("CALL", "N := TUtil.Mix", "E2123", 8, "There is no overloaded version of 'Mix' that can be called with these arguments")]
    [InlineData("CALL", "F := TUtil.Twice<string>", "E2010", 8, "Incompatible types: 'function(string): string' and 'TIntFunc'")]
    [InlineData("CALL", "F := Obj.Scale<Integer>", "E2010", 8, "Incompatible types: 'function(Integer): Integer of object' and 'TIntFunc'")]
    [InlineData("CALL", "M := TUtil.Twice<Integer>", "E2010", 8, "Incompatible types: 'function(Integer): Integer' and 'TIntMethod'")]
    [InlineData("CALL", "F := TUtil.Keep<Integer>", "E2704", 19, "Type argument 'Integer' does not satisfy constraint 'class' of 'T'")]
    [InlineData("CALL", "F := TUtil.Twice<Integer, string>", "E2703", 14, "'Twice' requires 1 type argument(s)")]
    [InlineData("CALL", "F := TUtil.Secret<Integer>", "E2610", 14, "Cannot access strict private member 'Secret' of 'TUtil'")]         // visibility as for a call
    [InlineData("CALL", "P := @TUtil.Secret<Integer>", "E2610", 15, "Cannot access strict private member 'Secret' of 'TUtil'")]
    public void GenericMethod_AsProceduralValue_Diagnostics(string where, string statement, string code, int column, string message) =>
        Assert.Equal(
            [$"{code}({PositionOf(ValueProgram, "{" + where + "}").Line},{column}): {message}"],
            Format(BindValue(statement, where).Diagnostics).Where(d => !d.StartsWith('H')));

    /// <summary>
    /// The routine a procedural value names: <c>@Name</c> for a code address, <c>method Name</c> for a method pointer — a generic
    /// method instance with its type arguments; looked for through conversions, a comparison and the arguments of a call (a method
    /// reference wraps the value in a call of its forwarder). Null when the expression holds none.
    /// </summary>
    private static string? ProceduralValue(BoundExpression expression) => expression switch
    {
        BoundAddressOfExpression { Routine: { } routine } => "@" + InstanceName(routine),
        BoundMethodPointerExpression pointer => "method " + InstanceName(pointer.Method),
        BoundConversionExpression conversion => ProceduralValue(conversion.Operand),
        BoundBinaryExpression binary => ProceduralValue(binary.Left) ?? ProceduralValue(binary.Right),
        BoundCallExpression call => call.Arguments.Select(ProceduralValue).FirstOrDefault(v => v is not null),
        _ => null,
    };

    private static string InstanceName(RoutineSymbol routine) => routine is MethodSymbol { GenericMethodDefinition: not null } method
        ? method.Name + "<" + string.Join(", ", method.GenericMethodArguments.Select(a => a.Name)) + ">"
        : routine.Name;

    /// <summary><see cref="ValueProgram"/> with <paramref name="statement"/> at <paramref name="where"/> (the other places empty).</summary>
    private static BindingResult BindValue(string statement, string where)
    {
        var text = ValueProgram;
        foreach (var marker in new[] { "CALL", "STATIC", "INSTANCE" })
        {
            text = text.Replace("{" + marker + "}", marker == where ? statement : "", StringComparison.Ordinal);
        }

        return BindRestricted(text);
    }

    /// <summary>Binds <paramref name="text"/> against a <c>System</c> with <c>TObject</c> and <c>TInterfacedObject</c>.</summary>
    private static BindingResult BindRestricted(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return BindWithStubSystem(tree, AnonymousMethodBindingTests.SystemSource);
    }

    /// <summary>The 1-based line and column of the first <paramref name="marker"/> in <paramref name="text"/>.</summary>
    private static (int Line, int Column) PositionOf(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, marker);
        return (text[..index].Count(c => c == '\n') + 1, index - text.LastIndexOf('\n', index));
    }
}
