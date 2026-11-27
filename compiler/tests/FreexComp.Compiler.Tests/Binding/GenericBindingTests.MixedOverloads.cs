using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-419: the generic and the non-generic methods of one name are one overload set (LANG-980) — candidates, the tie-break "the single
/// non-generic method among the equally cheapest", the diagnostics when nothing applies, every receiver form; a candidate that is not
/// chosen leaves no trace (CMP-1477 (3)); LANG-440 across generic and non-generic methods (LANG-978 "Overloads").
/// </summary>
public sealed partial class GenericBindingTests
{
    private const string MixedProgram = """
        program P;
        type
          TProc = procedure;
          TBase = class
            class procedure Up<T>(const V: T); overload; static;
            class procedure Down(I: Integer); overload; static;
            class procedure Hide(I: Integer); overload; static;
            class procedure Cover<T>(const V: T); overload; static;
          end;
          TOut = class(TBase)
            class procedure Show(I: Integer); overload; static;
            class procedure Show<T>(const V: T); overload; static;
            procedure Put(I: Integer); overload;
            procedure Put<T>(const V: T); overload;
            class procedure Cls(I: Integer); overload;
            class procedure Cls<T>(const V: T); overload;
            class procedure Up(I: Integer); overload; static;
            class procedure Down<T>(const V: T); overload; static;
            class procedure Hide<T>(const V: T); static;
            class procedure Cover(I: Integer); static;
            class procedure Opt(I: Integer; N: Integer = 0); overload; static;
            class procedure Opt<T>(const V: T); overload; static;
            class procedure Amb(I: Integer; J: Integer = 0); overload; static;
            class procedure Amb(I: Integer; const S: string = ''); overload; static;
            class procedure Amb<T>(const V: T; N: Integer = 0); overload; static;
            class procedure Gen<T>(const V: T; N: Integer); overload; static;
            class procedure Gen<K, V>(const A: K; const B: V); overload; static;
            class procedure Keep<T: class>(const X: T); static;
            class procedure Both<T: class>(const X: T); overload; static;
            class procedure Both<T: class>(const X: T; N: Integer); overload; static;
            class procedure KeepI(I: Integer); overload; static;
            class procedure KeepI<T: class>(const X: T); overload; static;
            class procedure Mk(I: Integer); overload; static;
            class function Mk<T>: T; overload; static;
            class procedure Ref(var I: Integer); overload; static;
            class procedure Ref<T>(var V: T); overload; static;
            class procedure Plain(I: Integer); static;
            class procedure Call(P: TProc); overload; static;
            class procedure Call<T>(const V: T); overload; static;
            procedure Run;
          end;
          TOutClass = class of TOut;
          TRec = record
            class procedure Show(I: Integer); overload; static;
            class procedure Show<T>(const V: T); overload; static;
            procedure Put(I: Integer); overload;
            procedure Put<T>(const V: T); overload;
            procedure Run;
          end;
        class procedure TBase.Up<T>(const V: T); begin end;
        class procedure TBase.Down(I: Integer); begin end;
        class procedure TBase.Hide(I: Integer); begin end;
        class procedure TBase.Cover<T>(const V: T); begin end;
        class procedure TOut.Show(I: Integer); begin end;
        class procedure TOut.Show<T>(const V: T); begin end;
        procedure TOut.Put(I: Integer); begin end;
        procedure TOut.Put<T>(const V: T); begin end;
        class procedure TOut.Cls(I: Integer); begin end;
        class procedure TOut.Cls<T>(const V: T); begin end;
        class procedure TOut.Up(I: Integer); begin end;
        class procedure TOut.Down<T>(const V: T); begin end;
        class procedure TOut.Hide<T>(const V: T); begin end;
        class procedure TOut.Cover(I: Integer); begin end;
        class procedure TOut.Opt(I: Integer; N: Integer = 0); begin end;
        class procedure TOut.Opt<T>(const V: T); begin end;
        class procedure TOut.Amb(I: Integer; J: Integer = 0); begin end;
        class procedure TOut.Amb(I: Integer; const S: string = ''); begin end;
        class procedure TOut.Amb<T>(const V: T; N: Integer = 0); begin end;
        class procedure TOut.Gen<T>(const V: T; N: Integer); begin end;
        class procedure TOut.Gen<K, V>(const A: K; const B: V); begin end;
        class procedure TOut.Keep<T>(const X: T); begin end;
        class procedure TOut.Both<T>(const X: T); begin end;
        class procedure TOut.Both<T>(const X: T; N: Integer); begin end;
        class procedure TOut.KeepI(I: Integer); begin end;
        class procedure TOut.KeepI<T>(const X: T); begin end;
        class procedure TOut.Mk(I: Integer); begin end;
        class function TOut.Mk<T>: T; begin end;
        class procedure TOut.Ref(var I: Integer); begin end;
        class procedure TOut.Ref<T>(var V: T); begin end;
        class procedure TOut.Plain(I: Integer); begin end;
        class procedure TOut.Call(P: TProc); begin end;
        class procedure TOut.Call<T>(const V: T); begin end;
        procedure Idle; begin end;
        function Five: Integer; begin Result := 5 end;
        procedure TOut.Run;
        begin
          {INNER};
        end;
        class procedure TRec.Show(I: Integer); begin end;
        class procedure TRec.Show<T>(const V: T); begin end;
        procedure TRec.Put(I: Integer); begin end;
        procedure TRec.Put<T>(const V: T); begin end;
        procedure TRec.Run;
        begin
          {RECORD};
        end;
        var
          I: Integer;
          B: Byte;
          L: Int64;
          D: Double;
          S: string;
          O: TOut;
          C: TOutClass;
          R: TRec;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-980 (2)…(4): without a type-argument list the non-generic methods and the instance of every inferable generic method are the
    /// candidates; a non-generic method wins only as an equally good match (the cost sums tie); a type-argument list names generic
    /// methods only.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [Trait("Req", "LANG-442")]
    [InlineData("TOut.Show(5)", "Show")]                                 // both cost the same: the non-generic one
    [InlineData("TOut.Show(I)", "Show")]
    [InlineData("TOut.Show(I + 1)", "Show")]
    [InlineData("TOut.Show('xy')", "Show<string>")]                      // the non-generic one is not applicable
    [InlineData("TOut.Show('x')", "Show<Char>")]                         // a character literal: its natural type
    [InlineData("TOut.Show(S)", "Show<string>")]
    [InlineData("TOut.Show(2.5)", "Show<Double>")]
    [InlineData("TOut.Show(D)", "Show<Double>")]
    [InlineData("TOut.Show(B)", "Show<Byte>")]                           // cost 0 against the widening cost 1 of Integer
    [InlineData("TOut.Show(L)", "Show<Int64>")]
    [InlineData("TOut.Show(5000000000)", "Show<Int64>")]
    [InlineData("TOut.Show(O)", "Show<TOut>")]
    [InlineData("TOut.Show<Integer>(5)", "Show<Integer>")]               // a type-argument list: generic methods only
    [InlineData("TOut.Show<Integer>(I)", "Show<Integer>")]
    [InlineData("TOut.Show<Int64>(I)", "Show<Int64>")]
    [InlineData("TOut.Opt(5)", "Opt")]                                   // the tie-break comes before the defaulted parameters
    [InlineData("TOut.Opt(5, 1)", "Opt")]                                // the instance has one parameter: not applicable
    [InlineData("TOut.Opt(S)", "Opt<string>")]
    [InlineData("TOut.Ref(I)", "Ref")]                                   // var positions: both exact
    [InlineData("TOut.Ref(S)", "Ref<string>")]
    [InlineData("TOut.Ref(B)", "Ref<Byte>")]                             // a var parameter takes no other type: only the instance fits
    [InlineData("TOut.Mk(5)", "Mk")]                                     // the generic method is not inferable: no candidate, no diagnostic
    [InlineData("TOut.Amb('xy')", "Amb<string>")]
    [InlineData("TOut.Call(Idle)", "Call")]                              // a routine name is its address for the procedural parameter
    [InlineData("TOut.Call(Five)", "Call<Integer>")]                     // no value of TProc: the function is called
    [InlineData("TOut.Call(5)", "Call<Integer>")]
    public void MixedOverloads_Resolution(string call, string expected)
    {
        var result = BindMixed(call);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        Assert.Equal(expected, CalledMethod(result.Bodies[0]));
    }

    /// <summary>
    /// LANG-980 (1), (7): the same set and the same choice for every receiver form — a class name, an instance, a class-reference
    /// value, a record type, a record value, an unqualified call inside a method — and over an ancestor and a descendant (LANG-441:
    /// while every method of the name carries <c>overload</c>).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [Trait("Req", "LANG-441")]
    [InlineData("CALL", "TOut.Show(5)", "Show")]                         // a class name
    [InlineData("CALL", "TOut.Show('xy')", "Show<string>")]
    [InlineData("CALL", "O.Put(5)", "Put")]                              // an instance
    [InlineData("CALL", "O.Put('xy')", "Put<string>")]
    [InlineData("CALL", "O.Put<Integer>(5)", "Put<Integer>")]
    [InlineData("CALL", "O.Cls(I)", "Cls")]                              // a class method through an instance
    [InlineData("CALL", "O.Cls(B)", "Cls<Byte>")]
    [InlineData("CALL", "C.Cls(5)", "Cls")]                              // a class-reference value
    [InlineData("CALL", "C.Cls('xy')", "Cls<string>")]
    [InlineData("CALL", "C.Cls<Integer>(5)", "Cls<Integer>")]
    [InlineData("CALL", "C.Show(D)", "Show<Double>")]
    [InlineData("CALL", "TRec.Show(5)", "Show")]                         // a record type
    [InlineData("CALL", "TRec.Show('xy')", "Show<string>")]
    [InlineData("CALL", "TRec.Show<Integer>(5)", "Show<Integer>")]
    [InlineData("CALL", "R.Put(I)", "Put")]                              // a record value
    [InlineData("CALL", "R.Put(S)", "Put<string>")]
    [InlineData("INNER", "Put(5)", "Put")]                               // unqualified, inside a method of the class
    [InlineData("INNER", "Put('xy')", "Put<string>")]
    [InlineData("INNER", "Put<Integer>(5)", "Put<Integer>")]
    [InlineData("INNER", "Show(5)", "Show")]
    [InlineData("INNER", "Show(2.5)", "Show<Double>")]
    [InlineData("INNER", "Up(5)", "Up")]
    [InlineData("INNER", "Up('xy')", "Up<string>")]
    [InlineData("INNER", "Down(5)", "Down")]
    [InlineData("INNER", "Amb('xy')", "Amb<string>")]                    // two non-generic methods of the name and a generic one
    [InlineData("INNER", "Self.Put('xy')", "Put<string>")]
    [InlineData("RECORD", "Put(5)", "Put")]                              // unqualified, inside a method of the record
    [InlineData("RECORD", "Put('xy')", "Put<string>")]
    [InlineData("RECORD", "Show(2.5)", "Show<Double>")]
    [InlineData("CALL", "TOut.Up(5)", "Up")]                             // the descendant's non-generic, the ancestor's generic method
    [InlineData("CALL", "TOut.Up('xy')", "Up<string>")]
    [InlineData("CALL", "TOut.Down(5)", "Down")]                         // the ancestor's non-generic method is still the equally good match
    [InlineData("CALL", "TOut.Down('xy')", "Down<string>")]
    [InlineData("CALL", "TBase.Up(5)", "Up<Integer>")]                   // the ancestor alone has the generic method only
    [InlineData("CALL", "TOut.Hide(5)", "Hide<Integer>")]                // no overload on the descendant's: the ancestor's is hidden
    public void MixedOverloads_EveryReceiverForm(string where, string call, string expected)
    {
        var result = BindMixed(call, where);

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
        var body = where switch
        {
            "INNER" => result.Routines.Single(r => r.Routine is MethodSymbol { Name: "Run", Owner: ClassTypeSymbol }).Body,
            "RECORD" => result.Routines.Single(r => r.Routine is MethodSymbol { Name: "Run", Owner: RecordTypeSymbol }).Body,
            _ => result.Bodies[0],
        };
        Assert.Equal(expected, CalledMethod(body));
    }

    /// <summary>
    /// LANG-980 (5): no applicable candidate — only generic methods, none inferable: E2711; the only candidate an inferable generic
    /// method that misses its constraint: its E2704 at the method name; otherwise E2123. Equally good non-generic methods stay E2122;
    /// a type-argument list no generic method of the name takes is E2703.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [InlineData("TOut.Gen(nil, nil)", "E2711", 8, "Cannot infer type arguments for 'Gen'; specify them explicitly")]
    [InlineData("TOut.Keep(5)", "E2704", 8, "Type argument 'Integer' does not satisfy constraint 'class' of 'T'")]
    [InlineData("TOut.Both(5)", "E2123", 3, "There is no overloaded version of 'Both' that can be called with these arguments")]     // two inferable, none satisfies its constraint
    [InlineData("TOut.Both(5, 1)", "E2123", 3, "There is no overloaded version of 'Both' that can be called with these arguments")]
    [InlineData("TOut.Show(O, 1)", "E2123", 3, "There is no overloaded version of 'Show' that can be called with these arguments")]
    [InlineData("TOut.Mk('x')", "E2123", 3, "There is no overloaded version of 'Mk' that can be called with these arguments")]
    [InlineData("TOut.KeepI('x')", "E2123", 3, "There is no overloaded version of 'KeepI' that can be called with these arguments")]
    [InlineData("TOut.Opt(S, 1)", "E2123", 3, "There is no overloaded version of 'Opt' that can be called with these arguments")]
    [InlineData("O.Put(O, 1)", "E2123", 3, "There is no overloaded version of 'Put' that can be called with these arguments")]
    [InlineData("TRec.Show(R, 1)", "E2123", 3, "There is no overloaded version of 'Show' that can be called with these arguments")]
    [InlineData("TOut.Amb(5)", "E2122", 3, "Ambiguous overloaded call to 'Amb'")]
    [InlineData("TOut.Show(nil)", "E2123", 3, "There is no overloaded version of 'Show' that can be called with these arguments")]   // nil infers nothing
    [InlineData("TOut.Ref(5)", "E2123", 3, "There is no overloaded version of 'Ref' that can be called with these arguments")]       // no variable
    [InlineData("TOut.Show<Integer, string>(5)", "E2703", 8, "'Show' requires 1 type argument(s)")]
    [InlineData("TOut.Show<Integer>('x')", "E2010", 22, "Incompatible types: 'Char' and 'Integer'")]         // no fall-back to the other methods
    [InlineData("TOut.Cover('x')", "E2010", 14, "Incompatible types: 'Char' and 'Integer'")]                 // no overload: the ancestor's generic method is hidden
    public void MixedOverloads_NoCandidate_Diagnostics(string call, string code, int column, string message) =>
        Assert.Equal(
            [$"{code}({MixedLine("{CALL}")},{column}): {message}"],
            Format(BindMixed(call).Diagnostics).Where(d => !d.StartsWith('H')));

    /// <summary>A <c>System</c> whose <c>TObject</c> has <c>Free</c>.</summary>
    internal const string ProbeSystemSource = """
        unit System;
        interface
        type
          TObject = class
            constructor Create;
            procedure Free;
          end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        procedure TObject.Free; begin end;
        end.
        """;

    /// <summary>The pair of spec §5.5: a non-generic method and a generic one whose body does not compile for <c>Integer</c>.</summary>
    internal const string ProbeProgram = """
        program P;
        type
          TProbe = class
            class procedure Take(I: Integer); overload; static;
            class procedure Take<T>(const V: T); overload; static;
          end;
        class procedure TProbe.Take(I: Integer);
        begin
        end;
        class procedure TProbe.Take<T>(const V: T);
        begin
          V.Free;
        end;
        var
          O: TObject;
        begin
          {CALL};
        end.
        """;

    /// <summary>
    /// LANG-980 (6), CMP-1477 (3): the instance formed for a candidate that is not chosen leaves no trace — the call that resolves to
    /// the non-generic method gives no diagnostic although the generic body does not compile for <c>Integer</c>, the generic method has
    /// no instance for <c>Integer</c> and the unit records no generic use. The chosen instance is recorded as before (the control).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-980")]
    [Trait("Req", "CMP-1477")]
    public void MixedOverloads_UnchosenInstanceLeavesNoTrace()
    {
        static BindingResult Bind(string call)
        {
            var tree = SyntaxTree.Parse(SourceText.From(TestPath, ProbeProgram.Replace("{CALL}", call)));
            Assert.Empty(tree.Diagnostics);
            return BindWithStubSystem(tree, ProbeSystemSource);
        }

        static GenericMethodSymbol Take(BindingResult result) =>
            Assert.Single(Assert.IsType<ClassTypeSymbol>(result.Unit.InterfaceMembers.Concat(result.Unit.ImplementationMembers).Single(m => m.Name == "TProbe")).GenericMethods);

        var unchosen = Bind("TProbe.Take(5)");
        Assert.Equal([], Format(unchosen.Diagnostics));
        Assert.Equal("Take", CalledMethod(unchosen.Bodies[0]));
        Assert.Empty(Take(unchosen).Instances);
        Assert.DoesNotContain(unchosen.Unit.UsedInstances, u => u is MethodSymbol);
        Assert.DoesNotContain(unchosen.Routines, r => r.Routine is MethodSymbol { GenericMethodDefinition: not null });

        // the control: the instance the call binds to is formed, recorded and its body bound
        var chosen = Bind("TProbe.Take(O)");
        Assert.Equal([], Format(chosen.Diagnostics));
        Assert.Equal("Take<TObject>", CalledMethod(chosen.Bodies[0]));
        var instance = Assert.Single(Take(chosen).Instances).Value;
        Assert.Contains(instance, chosen.Unit.UsedInstances);
        Assert.Contains(chosen.Routines, r => ReferenceEquals(r.Routine, instance));

        // and what the unchosen instance would have reported, had its body been bound
        var written = Bind("TProbe.Take<Integer>(5)");
        Assert.Contains(Format(written.Diagnostics), d => d.StartsWith("E2710", StringComparison.Ordinal) && d.Contains("TProbe.Take<Integer>", StringComparison.Ordinal));
    }

    /// <summary>
    /// LANG-978 "Overloads" (LANG-440): the methods of one name in one type, generic and non-generic together, must each carry
    /// <c>overload</c> (E2120, at the later declaration); two generic methods with the same number of type parameters and the same
    /// parameter list after the type parameters are replaced by their positions are one declaration (E2121); a generic and a
    /// non-generic method never clash.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    [Trait("Req", "LANG-440")]
    // a non-generic method without overload next to a generic one, in either order, in a class and in a record
    [InlineData("class", "procedure Show(I: Integer); procedure Show<T>(const V: T); overload;", "E2120")]
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show(I: Integer);", "E2120")]
    [InlineData("class", "procedure Show(I: Integer); overload; procedure Show<T>(const V: T);", "E2120")]
    [InlineData("class", "procedure Show<T>(const V: T); procedure Show(I: Integer); overload;", "E2120")]
    [InlineData("record", "procedure Show(I: Integer); procedure Show<T>(const V: T); overload;", "E2120")]
    [InlineData("record", "procedure Show<T>(const V: T); procedure Show(I: Integer); overload;", "E2120")]
    // two generic methods of one name
    [InlineData("class", "procedure Show<T>(const V: T); procedure Show<K, V>(const A: K; const B: V); overload;", "E2120")]
    [InlineData("record", "procedure Show<T>(const V: T); overload; procedure Show<K, V>(const A: K; const B: V);", "E2120")]
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show<U>(const X: U); overload;", "E2121")]
    [InlineData("record", "procedure Show<T>(const V: T); overload; procedure Show<U>(const X: U); overload;", "E2121")]
    [InlineData("class", "function Show<K, V>(const A: K; const B: V): K; overload; function Show<V, K>(const A: V; const B: K): V; overload;", "E2121")]
    // accepted: every method carries overload and the declarations differ
    [InlineData("class", "procedure Show(I: Integer); overload; procedure Show<T>(const V: T); overload;", null)]
    [InlineData("class", "procedure Show(I: Integer); overload; procedure Show<T>(I: Integer); overload;", null)]              // generic and non-generic never clash
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show<K, V>(const A: K; const B: V); overload;", null)]
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show<K, V>(const V: K); overload;", null)]         // another number of type parameters
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show<T>(var V: T); overload;", null)]
    [InlineData("class", "procedure Show<T>(const V: T); overload; procedure Show<T>(const V: T; N: Integer); overload;", null)]
    [InlineData("record", "procedure Show(I: Integer); overload; procedure Show<T>(const V: T); overload;", null)]
    [InlineData("class", "procedure Show<T>(const V: T); procedure Other(I: Integer);", null)]                                  // alone: no directive needed
    public void MixedOverloads_OverloadDirective(string kind, string members, string? code)
    {
        const string indent = "    ";
        var text = $"program P;\ntype\n  TOut = {kind}\n{indent}{members}\n  end;\nbegin\nend.\n";
        var later = members.LastIndexOf(" Show", StringComparison.Ordinal) + 2 + indent.Length;        // the 1-based column of the later declaration's name
        var message = code == "E2120" ? "Previous declaration of 'Show' was not marked with the 'overload' directive" : "Ambiguous overload declaration of 'Show'";

        var diagnostics = Format(Bind(text).Result.Diagnostics).Where(d => !d.StartsWith(Unimplemented, StringComparison.Ordinal) && !d.StartsWith('H'));

        Assert.Equal(code is null ? [] : [$"{code}(4,{later}): {message}"], diagnostics);
    }

    /// <summary>
    /// LANG-978 "Overloads": after E2120 the declarations are kept, so a call of the name still resolves over both and gives no second
    /// diagnostic.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-978")]
    public void MixedOverloads_OverloadDirective_DeclarationsAreKept()
    {
        var result = Bind("""
            program P;
            type
              TOut = class
                class procedure Show(I: Integer); static;
                class procedure Show<T>(const V: T); overload; static;
              end;
            class procedure TOut.Show(I: Integer); begin end;
            class procedure TOut.Show<T>(const V: T); begin end;
            begin
              TOut.Show(5);
              TOut.Show('x');
            end.
            """).Result;

        Assert.Equal(
            ["E2120(5,21): Previous declaration of 'Show' was not marked with the 'overload' directive"],
            Format(result.Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>E2605: the methods of <see cref="MixedOverloads_OverloadDirective"/> are declared only.</summary>
    private const string Unimplemented = "E2605";

    /// <summary><see cref="MixedProgram"/> with <paramref name="call"/> as the statement at <paramref name="where"/> (the others empty).</summary>
    private static BindingResult BindMixed(string call, string where = "CALL")
    {
        var text = MixedProgram;
        foreach (var marker in new[] { "CALL", "INNER", "RECORD" })
        {
            text = text.Replace("{" + marker + "}", marker == where ? call : "", StringComparison.Ordinal);
        }

        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return BindWithStubSystem(tree, AnonymousMethodBindingTests.SystemSource);
    }

    /// <summary>The 1-based line of <paramref name="marker"/> in <see cref="MixedProgram"/>.</summary>
    private static int MixedLine(string marker) =>
        MixedProgram[..MixedProgram.IndexOf(marker, StringComparison.Ordinal)].Count(c => c == '\n') + 1;

    /// <summary>
    /// The method the one call statement of <paramref name="body"/> binds to: its name, for a generic method instance followed by the
    /// type arguments (<c>Show</c>, <c>Show&lt;Integer&gt;</c>).
    /// </summary>
    private static string? CalledMethod(BoundBlock body) =>
        body.Statements.OfType<BoundCallStatement>().Single().Routine switch
        {
            MethodSymbol { GenericMethodDefinition: not null } method => method.Name + "<" + string.Join(", ", method.GenericMethodArguments.Select(a => a.Name)) + ">",
            { } routine => routine.Name,
            null => null,
        };
}
