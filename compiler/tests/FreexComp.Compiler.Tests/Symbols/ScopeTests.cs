using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Symbols;

/// <summary>The <see cref="Scope"/> chain (architecture.md §4, LANG-310, LANG-110 identifier comparison).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-310")]
public sealed class ScopeTests
{
    private static readonly BuiltInTypes Types = new();

    private static ConstantSymbol Constant(string name, int value) =>
        new(name, Types.Integer, new ConstantValue(value, Types.Integer), isTyped: false, containingSymbol: null, locations: []);

    [Fact]
    public void Declare_ThenLookup_FindsTheSymbol()
    {
        var scope = new Scope(ScopeKind.Block, parent: null);
        var a = Constant("Alpha", 1);

        Assert.True(scope.TryDeclare(a, out var existing));
        Assert.Null(existing);
        Assert.Same(a, scope.Lookup("Alpha"));
        Assert.Same(a, scope.LookupLocal("Alpha"));
    }

    [Theory]
    [Trait("Req", "LANG-110")]
    [InlineData("alpha")]
    [InlineData("ALPHA")]
    [InlineData("aLpHa")]
    public void Lookup_IgnoresAsciiCase(string spelling)
    {
        var scope = new Scope(ScopeKind.Block, parent: null);
        var a = Constant("Alpha", 1);
        scope.TryDeclare(a, out _);

        Assert.Same(a, scope.Lookup(spelling));
    }

    [Fact]
    [Trait("Req", "LANG-110")]
    public void Lookup_NonAsciiLettersMustMatchExactly()
    {
        var scope = new Scope(ScopeKind.Block, parent: null);
        var umlaut = Constant("Größe", 1);
        scope.TryDeclare(umlaut, out _);

        Assert.Same(umlaut, scope.Lookup("GRößE"));
        Assert.Null(scope.Lookup("GRÖßE"));
    }

    [Fact]
    public void Lookup_UnknownName_ReturnsNull()
    {
        var scope = new Scope(ScopeKind.Block, new Scope(ScopeKind.Unit, parent: null));

        Assert.Null(scope.Lookup("Missing"));
    }

    [Fact]
    public void TryDeclare_SameNameTwiceInOneScope_FailsAndReturnsTheFirst()
    {
        var scope = new Scope(ScopeKind.Block, parent: null);
        var first = Constant("X", 1);
        var second = Constant("x", 2);
        scope.TryDeclare(first, out _);

        Assert.False(scope.TryDeclare(second, out var existing));
        Assert.Same(first, existing);
        Assert.Same(first, scope.Lookup("X"));
    }

    [Fact]
    public void Lookup_WalksOutward_AndTheInnermostDeclarationWins()
    {
        var unit = new Scope(ScopeKind.Unit, parent: null);
        var routine = new Scope(ScopeKind.Routine, unit);
        var block = new Scope(ScopeKind.Block, routine);
        var outer = Constant("X", 1);
        var inner = Constant("X", 2);
        var onlyOuter = Constant("Y", 3);
        unit.TryDeclare(outer, out _);
        unit.TryDeclare(onlyOuter, out _);
        routine.TryDeclare(inner, out _);

        Assert.Same(inner, block.Lookup("x"));
        Assert.Same(onlyOuter, block.Lookup("Y"));
        Assert.Null(block.LookupLocal("Y"));
        Assert.Same(outer, unit.Lookup("X"));
    }

    [Fact]
    public void Shadowing_AnOuterName_IsNotARedeclaration()
    {
        var unit = new Scope(ScopeKind.Unit, parent: null);
        var block = new Scope(ScopeKind.Block, unit);
        unit.TryDeclare(Constant("X", 1), out _);

        Assert.True(block.TryDeclare(Constant("X", 2), out var existing));
        Assert.Null(existing);
    }

    [Fact]
    public void Symbols_AreListedInDeclarationOrder()
    {
        var scope = new Scope(ScopeKind.Block, parent: null);
        string[] names = ["Zeta", "Alpha", "Mid", "beta"];
        foreach (var name in names)
        {
            scope.TryDeclare(Constant(name, 0), out _);
        }

        Assert.Equal(names, scope.Symbols.Select(s => s.Name));
    }

    [Fact]
    public void Declare_UnderAnotherName_BindsTheNameToTheSameSymbol()
    {
        // aliases (type T2 = T1, LANG-205) name the same symbol
        var scope = new Scope(ScopeKind.Block, parent: null);
        var integer = Types.Integer;

        Assert.True(scope.TryDeclare("MyInt", integer, out _));

        Assert.Same(integer, scope.Lookup("myint"));
        Assert.Null(scope.Lookup("Integer"));
    }

    [Fact]
    [Trait("Req", "LANG-200")]
    public void BuiltInScope_IsTheRoot_AndAUnitMayShadowItsNames()
    {
        var builtIns = Types.Scope;
        var unit = new Scope(ScopeKind.Unit, builtIns);
        var block = new Scope(ScopeKind.Block, unit);
        var myInteger = Constant("Integer", 5);

        Assert.Equal(ScopeKind.BuiltIn, builtIns.Kind);
        Assert.Null(builtIns.Parent);
        Assert.Same(Types.Integer, block.Lookup("Integer"));

        Assert.True(unit.TryDeclare(myInteger, out _));
        Assert.Same(myInteger, block.Lookup("INTEGER"));
        Assert.Same(Types.Integer, builtIns.Lookup("Integer"));
    }

    [Fact]
    public void Owner_AndKind_AreKept()
    {
        var parent = new Scope(ScopeKind.Unit, parent: null);
        var scope = new Scope(ScopeKind.TypeMember, parent, owner: Types.Integer);

        Assert.Equal(ScopeKind.TypeMember, scope.Kind);
        Assert.Same(parent, scope.Parent);
        Assert.Same(Types.Integer, scope.Owner);
    }
}
