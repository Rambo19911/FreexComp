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
            .Add(Path.Combine(Dir, "Used.pas"), "unit Used; interface uses Helper; const A = 1; implementation uses Other; end.")
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
}
