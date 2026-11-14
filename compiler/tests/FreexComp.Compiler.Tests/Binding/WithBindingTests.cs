using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-109: the <c>with</c> statement (expressions-statements.md LANG-375, LANG-310).</summary>
[Trait("Phase", "2")]
public sealed class WithBindingTests
{
    private const string Prelude = """
        program T;
        type
          TR = record a, b: Integer end;
          TC = class x: Integer; procedure M(n: Integer); function F: Integer; property P: Integer read x write x; end;
        procedure TC.M(n: Integer); begin end;
        function TC.F: Integer; begin Result := 0 end;
        var r: TR; c: TC; a, i: Integer;

        """;

    private static string Body(string statements)
    {
        var (result, _) = Bind(Prelude + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(result.Bodies[^1]);
    }

    private static string[] Diagnostics(string statements) => BindDiagnostics(Prelude + "begin " + statements + " end.");

    /// <summary>LANG-375: a record designator is referenced once (its address in a hidden variable); members win over outer names.</summary>
    [Fact]
    [Trait("Req", "LANG-375")]
    public void Record_MembersThroughTheHiddenReference() =>
        Assert.Equal(
            "(block (with $with1:^TR (@ r:TR) (:= (. (^ $with1:^TR):TR a):Integer (. (^ $with1:^TR):TR b):Integer)) (:= a:Integer 1:Integer))",
            Body("with r do a := b; a := 1"));

    /// <summary>LANG-375: an object designator is evaluated once; fields, methods, properties are reached through it.</summary>
    [Fact]
    [Trait("Req", "LANG-375")]
    public void Object_FieldsMethodsProperties() =>
        Assert.Equal(
            "(block (with $with1:TC c:TC (block (:= (. $with1:TC x):Integer 1:Integer) (call M 2:Integer) (:= i:Integer (call F:Integer)) (:= i:Integer (. $with1:TC x):Integer))))",
            Body("with c do begin x := 1; M(2); i := F; i := P end"));

    /// <summary><c>with a, b do</c> = <c>with a do with b do</c>; the innermost designator's members win.</summary>
    [Fact]
    [Trait("Req", "LANG-375")]
    public void List_IsNested_InnermostWins() =>
        Assert.Equal(
            "(block (with $with1:^TR (@ r:TR) (with $with2:TC c:TC (:= (. $with2:TC x):Integer (. (^ $with1:^TR):TR a):Integer))))",
            Body("with r, c do x := a"));

    /// <summary>E2068 for a designator of another type; an unknown name is still E2003.</summary>
    [Theory]
    [Trait("Req", "LANG-375")]
    [InlineData("with i do a := 1", "E2068(8,12): 'with' requires a record, class or interface expression")]
    [InlineData("with r do z := 1", "E2003(8,17): Undeclared identifier 'z'")]
    public void Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));
}
