using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-430: a generic method declared in a class or record and never implemented is E2605 at its declaration, like any method.</summary>
public sealed partial class GenericBindingTests
{
    /// <summary>
    /// LANG-620 / LANG-978: a generic method without implementation is E2605 at the method name of its declaration, with the text of
    /// a non-generic method (<c>TType.Name</c>) — once per declaration, whether nothing calls the method, one call does or several
    /// calls make several instances. Each overload is its own declaration; an implemented one is not reported. <paramref name="missing"/>:
    /// the declarations reported, as <c>Owner.Name</c> or <c>Owner.Name#n</c> for the n-th declaration of that name in the program.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-620")]
    [Trait("Req", "LANG-978")]
    [InlineData("CLASS", "procedure G<T>(const X: T);", "", "Foo.G(1)", "TFoo.G")]
    [InlineData("CLASS", "procedure G<T>(const X: T);", "", "Foo.G(1); Foo.G('x'); Foo.G<Boolean>(True)", "TFoo.G")]          // three instances, one diagnostic
    [InlineData("CLASS", "function F<K, V>(const X: K): V;", "", "I := Foo.F<string, Integer>('x')", "TFoo.F")]
    [InlineData("CLASS", "class function S<T>(X: T): T; static;", "", "I := TFoo.S(1)", "TFoo.S")]
    [InlineData("CLASS", "class procedure C<T>(const X: T);", "", "TFoo.C(1)", "TFoo.C")]
    [InlineData("CLASS", "strict private procedure G<T>(const X: T);", "", "I := 1", "TFoo.G")]
    [InlineData("RECORD", "procedure R<T>(const X: T);", "", "Rec.R(1)", "TRec.R")]
    [InlineData("RECORD", "class function S<T>(X: T): T; static;", "", "I := TRec.S<Integer>(1)", "TRec.S")]
    // each overload is a declaration of its own: the implemented one is not reported
    [InlineData("CLASS", "procedure H<T>(const X: T); overload; procedure H<T>(const X, Y: T); overload;", "procedure TFoo.H<T>(const X: T); begin end;",
        "Foo.H(1); Foo.H(1, 2)", "TFoo.H#2")]
    [InlineData("CLASS", "procedure H<T>(const X: T); overload; procedure H<T>(const X, Y: T); overload;", "procedure TFoo.H<T>(const X, Y: T); begin end;",
        "Foo.H(1, 2)", "TFoo.H#1")]
    [InlineData("CLASS", "procedure H<T>(const X: T); overload; procedure H<T>(const X, Y: T); overload;", "", "Foo.H(1)", "TFoo.H#1|TFoo.H#2")]
    [InlineData("CLASS", "procedure H<T>(const X: T); overload; procedure H<T, U>(const X: T; const Y: U); overload;", "procedure TFoo.H<T>(const X: T); begin end;",
        "Foo.H(1)", "TFoo.H#2")]
    // a generic and a non-generic method of one name: one overload set, two declarations, the same text
    [InlineData("CLASS", "procedure M(X: Integer); overload; procedure M<T>(const X: T); overload;", "", "Foo.M(1); Foo.M('x')", "TFoo.M#1|TFoo.M#2")]
    [InlineData("CLASS", "procedure M(X: Integer); overload; procedure M<T>(const X: T); overload;", "procedure TFoo.M(X: Integer); begin end;", "Foo.M('x')", "TFoo.M#2")]
    [InlineData("RECORD", "procedure M(X: Integer); overload; procedure M<T>(const X: T); overload;", "procedure TRec.M<T>(const X: T); begin end;", "Rec.M('x')", "TRec.M#1")]
    // a nested type: named like its non-generic methods
    [InlineData("CLASS", "type TInner = class procedure N<T>(const X: T); procedure Plain; end;", "", "I := 1", "TInner.N|TInner.Plain")]
    [InlineData("RECORD", "type TInner = record F: Integer; procedure N<T>(const X: T); end;", "", "I := 1", "TInner.N")]
    // implemented: nothing
    [InlineData("CLASS", "procedure G<T>(const X: T); class function S<T>(X: T): T; static;",
        "procedure TFoo.G<T>(const X: T); begin end; class function TFoo.S<T>(X: T): T; begin Result := X end;", "Foo.G(TFoo.S(1))", "")]
    [InlineData("RECORD", "procedure R<T>(const X: T);", "procedure TRec.R<T>(const X: T); begin end;", "Rec.R(1)", "")]
    public void GenericMethod_DeclaredNotImplemented_E2605(string where, string declaration, string implementation, string call, string missing)
    {
        foreach (var statement in new[] { "I := 0", call })                    // never called; called
        {
            var text = RestrictionProgram.Replace("{" + where + "}", declaration).Replace("{CLASS}", "").Replace("{RECORD}", "").Replace("{INTERFACE}", "")
                .Replace("{IMPL}", implementation).Replace("{CALL}", statement);
            var expected = missing.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
            {
                var parts = entry.Split('#');
                var name = parts[0][(parts[0].IndexOf('.') + 1)..];
                var (line, column) = NthDeclaration(text, name, parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1);
                return $"E2605({line},{column}): Method '{parts[0]}' is not implemented";
            });

            Assert.Equal(expected, Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
        }
    }

    /// <summary>
    /// LANG-620 / LANG-978: a restriction of the declaration (E2713) does not excuse the missing implementation — the declaration is
    /// kept without the offending property, so it needs one like any method (E2605 next to E2713). Not so a method declared
    /// <c>abstract</c> (an abstract method has no implementation, LANG-622) and a kind the type cannot declare at all (an operator of a
    /// class, a destructor of a record: the non-generic ones are not kept either) — those stay at their E2713.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-620")]
    [Trait("Req", "LANG-978")]
    [InlineData("CLASS", "procedure V<T>(const X: T); virtual;", "V", "virtual", true)]
    [InlineData("CLASS", "procedure O<T>(const X: T); override;", "O", "an override", true)]
    [InlineData("CLASS", "published procedure P<T>(const X: T);", "P", "published", true)]
    [InlineData("CLASS", "constructor Make<T>(const X: T);", "Make", "a constructor", true)]
    [InlineData("CLASS", "destructor Done<T>(const X: T);", "Done", "a destructor", true)]
    [InlineData("RECORD", "constructor Make<T>(const X: T);", "Make", "a constructor", true)]
    [InlineData("RECORD", "class operator Add<T>(const A, B: TRec): TRec;", "Add", "an operator", true)]
    [InlineData("RECORD", "procedure V<T>(const X: T); virtual;", "V", "virtual", true)]
    [InlineData("CLASS", "procedure A<T>(const X: T); abstract;", "A", "abstract", false)]
    [InlineData("CLASS", "procedure A<T>(const X: T); virtual; abstract;", "A", "virtual", false)]
    [InlineData("CLASS", "class operator Add<T>(const A, B: TFoo): TFoo;", "Add", "an operator", false)]
    [InlineData("RECORD", "destructor Done<T>(const X: T);", "Done", "a destructor", false)]
    public void GenericMethod_DeclaredNotImplemented_NextToARestriction(string where, string declaration, string name, string phrase, bool reported)
    {
        var text = RestrictionProgram.Replace("{" + where + "}", declaration).Replace("{CLASS}", "").Replace("{RECORD}", "").Replace("{INTERFACE}", "")
            .Replace("{IMPL}", "").Replace("{CALL}", "I := 0");
        var (line, column) = PositionOf(text, " " + name + "<");
        column++;
        var owner = where == "CLASS" ? "TFoo" : "TRec";
        string[] expected = reported
            ? [$"E2713({line},{column}): Generic method '{name}' cannot be {phrase}", $"E2605({line},{column}): Method '{owner}.{name}' is not implemented"]
            : [$"E2713({line},{column}): Generic method '{name}' cannot be {phrase}"];

        Assert.Equal(expected, Format(BindRestricted(text).Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>
    /// LANG-620: a unit — a generic method declared in the interface part and implemented nowhere in the implementation part is
    /// E2605 at its declaration; one implemented there is not.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-620")]
    [Trait("Req", "LANG-978")]
    public void GenericMethod_DeclaredNotImplemented_InAUnit()
    {
        const string Unit = """
            unit U;
            interface
            type
              TUtil = class
                class function Lost<T>(X: T): T; static;
                class function Kept<T>(X: T): T; static;
              end;
            implementation
            type
              TLocal = record
                procedure Gone<T>(const X: T);
              end;
            class function TUtil.Kept<T>(X: T): T; begin Result := X end;
            end.
            """;
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\U.pas", Unit));
        Assert.Empty(tree.Diagnostics);

        Assert.Equal(
            ["E2605(5,20): Method 'TUtil.Lost' is not implemented", "E2605(11,15): Method 'TLocal.Gone' is not implemented"],
            Format(BindWithStubSystem(tree, AnonymousMethodBindingTests.SystemSource).Diagnostics).Where(d => !d.StartsWith('H')));
    }

    /// <summary>
    /// LANG-620: inside a generic type a generic method follows the rule of the type's non-generic methods — both are reported for
    /// the instances the unit declares a name for (<c>TIntBox = TBox&lt;Integer&gt;</c>: E2605 at the declaration, naming that
    /// instance), with the same text and the same number of diagnostics; an implemented generic method (<c>Ok</c>) is not reported.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-620")]
    [Trait("Req", "LANG-978")]
    [InlineData("", 0)]
    [InlineData("TIntBox = TBox<Integer>;", 1)]
    [InlineData("TIntBox = TBox<Integer>; TStrBox = TBox<string>;", 2)]
    public void GenericMethod_DeclaredNotImplemented_InAGenericType(string aliases, int named)
    {
        var result = BindRestricted($$"""
            program P;
            type
              TBox<T> = class
                procedure Plain;
                procedure G<U>(const X: U);
                procedure Ok<U>(const X: U);
              end;
              {{aliases}}
            procedure TBox<T>.Ok<U>(const X: U); begin end;
            var
              B: TBox<Byte>;
            begin
              B.Ok(1);
            end.
            """);

        var diagnostics = Format(result.Diagnostics).Where(d => !d.StartsWith('H')).ToList();
        var plain = diagnostics.Where(d => d.StartsWith("E2605(4,15): ", StringComparison.Ordinal) && d.EndsWith(".Plain' is not implemented", StringComparison.Ordinal)).ToList();
        var generic = diagnostics.Where(d => d.StartsWith("E2605(5,15): ", StringComparison.Ordinal) && d.EndsWith(".G' is not implemented", StringComparison.Ordinal)).ToList();
        Assert.Equal(diagnostics.Count, plain.Count + generic.Count);                  // nothing else, nothing for Ok
        Assert.Equal(plain.Select(d => d.Replace("(4,15)", "(5,15)").Replace(".Plain'", ".G'")), generic);
        if (named > 0)
        {
            Assert.Equal(named, generic.Count);
            Assert.Contains("E2605(5,15): Method 'TBox<Integer>.G' is not implemented", generic);
        }
    }

    /// <summary>The 1-based line and column of the name of the <paramref name="n"/>-th method called <paramref name="name"/> declared in <paramref name="text"/>.</summary>
    private static (int Line, int Column) NthDeclaration(string text, string name, int n)
    {
        var index = -1;
        for (var k = 0; k < n; k++)
        {
            var generic = text.IndexOf(" " + name + "<", index + 1, StringComparison.Ordinal);
            var plain = text.IndexOf(" " + name + "(", index + 1, StringComparison.Ordinal);
            var semicolon = text.IndexOf(" " + name + ";", index + 1, StringComparison.Ordinal);
            index = new[] { generic, plain, semicolon }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            Assert.True(index >= 0, name);
        }

        return (text[..index].Count(c => c == '\n') + 1, index - text.LastIndexOf('\n', index) + 1);
    }
}
