using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Tests;

/// <summary>
/// api.md §1 (C-263): <c>Compilation.FindUnit</c> / <c>GetUnitSymbol</c> resolve a unit name with the rules of a uses entry
/// (LANG-510 search, LANG-533 alias table, LANG-535 unit scope names); a unit the main file does not reach is analysed as an
/// additional root (CMP-201); a name that does not resolve gives null.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Unit")]
public sealed class CompilationApiTests
{
    private const string Dir = @"C:\p";
    private const string Rtl = @"C:\rtl";

    private static Compilation Create()
    {
        var fs = new InMemoryFileSystem()
            .AddSystem(Rtl)
            .Add(Path.Combine(Dir, "P.pas"), "program P; uses Used; begin end.")
            .Add(Path.Combine(Dir, "Used.pas"), "unit Used; interface uses Helper; const A = 1; var V: Integer; implementation uses Other; end.")
            .Add(Path.Combine(Dir, "Helper.pas"), "unit Helper; interface implementation end.")
            .Add(Path.Combine(Dir, "Other.pas"), "unit Other; interface implementation end.")
            .Add(Path.Combine(Rtl, "Utils.pas"), "unit Utils; interface procedure Hello; implementation procedure Hello; begin end; end.")
            .Add(Path.Combine(Rtl, "unit-aliases.json"), """{ "aliases": { "Old.Utils": "Utils" } }""");
        var options = new CompilationOptions { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = Rtl, ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs);
    }

    [Fact]
    public void FindUnit_GetUnitSymbol()
    {
        var compilation = Create();

        // a unit of the program, found case-insensitively
        var used = compilation.FindUnit("USED");
        Assert.NotNull(used);
        Assert.Equal("Used", used.Name);
        Assert.Equal(@"C:\p\Used.pas", used.Path);
        Assert.False(used.IsProgram);
        Assert.False(used.FromPcu);
        Assert.Equal(["Helper"], used.InterfaceUses);
        Assert.Equal(["Other"], used.ImplementationUses);
        var symbol = compilation.GetUnitSymbol("Used");
        Assert.NotNull(symbol);
        Assert.Equal("Used", symbol.Name);
        Assert.Contains(symbol.InterfaceMembers, m => m.Name == "A");

        // System, and a unit in the RTL directory the program does not use (an additional root, CMP-201)
        Assert.Equal(@"C:\rtl\System.pas", compilation.FindUnit("System")?.Path);
        Assert.Equal(@"C:\rtl\Utils.pas", compilation.FindUnit("Utils")?.Path);
        Assert.Contains(compilation.GetUnitSymbol("Utils")!.InterfaceMembers, m => m.Name == "Hello");

        // through the alias table: the canonical unit
        var aliased = compilation.FindUnit("old.utils");
        Assert.NotNull(aliased);
        Assert.Equal("Utils", aliased.Name);
        Assert.Equal(@"C:\rtl\Utils.pas", aliased.Path);
        Assert.Same(compilation.GetUnitSymbol("Utils"), compilation.GetUnitSymbol("Old.Utils"));

        // not found
        Assert.Null(compilation.FindUnit("Missing"));
        Assert.Null(compilation.GetUnitSymbol("Missing"));
        Assert.Null(compilation.FindUnit("Old.Missing"));
    }

    [Fact]
    public void Units_InitOrder()
    {
        // LANG-520: System first, then depth-first by uses clause (interface before implementation), the main file last
        var units = Create().Units;
        Assert.Equal(["System", "Helper", "Other", "Used", "P"], units.Select(u => u.Name));
        Assert.True(units[^1].IsProgram);
        Assert.Equal(@"C:\p\P.pas", units[^1].Path);
        Assert.Equal(["Used"], units[^1].InterfaceUses);
        Assert.DoesNotContain(units, u => u.Name == "Utils");            // in the RTL directory, but not reached

        // no System: the front end does not start, so there are no units
        Assert.Empty(Bare().Units);
    }

    [Fact]
    public void BuiltIns_Present()
    {
        var compilation = Create();
        var builtIns = compilation.BuiltIns;
        Assert.Same(builtIns, compilation.BuiltIns);
        Assert.Equal("Integer", builtIns.Integer.Name);
        // the types the units are bound with
        var v = Assert.IsType<VariableSymbol>(compilation.GetUnitSymbol("Used")!.InterfaceMembers.Single(m => m.Name == "V"));
        Assert.Same(builtIns.Integer, v.Type);

        var bare = Bare();
        Assert.NotNull(bare.BuiltIns.String);
        Assert.Same(bare.BuiltIns, bare.BuiltIns);
    }

    /// <summary>A program without a <c>System</c> unit: the front end does not start.</summary>
    private static Compilation Bare() =>
        Compilation.Create(new CompilationOptions { MainFile = @"C:\q\Q.pas", RtlDirectory = @"C:\none", ObjectDirectory = @"C:\q\obj" },
            [], new InMemoryFileSystem().Add(@"C:\q\Q.pas", "program Q; begin end."));
}
