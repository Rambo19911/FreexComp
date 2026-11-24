using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Emit;

/// <summary>One routine imported from a DLL: its import name (link name) and the normalised DLL name (CMP-430).</summary>
internal readonly record struct ImportedRoutine(string ImportName, string DllName);

/// <summary>What the link step needs from the bound units (CMP-620): DLLs and the routines imported from them.</summary>
internal static class LinkInputs
{
    /// <summary>
    /// DLLs that are always linked (CMP-620): the start-up code needs the system DLL, exception handling the DLL of the target's
    /// personality routine (CMP-1012).
    /// </summary>
    public static ImmutableArray<string> AlwaysLinked(TargetInfo target) => ["kernel32.dll", target.PersonalityLibrary];

    /// <summary>The DLLs named by <c>external '&lt;dll&gt;'</c> in <paramref name="units"/> plus <see cref="AlwaysLinked"/>, distinct, sorted.</summary>
    /// <param name="target">null → <see cref="TargetRegistry.Default"/>.</param>
    public static ImmutableArray<string> Dlls(IEnumerable<UnitSymbol> units, TargetInfo? target = null) =>
        [.. Imports(units).Select(i => i.DllName).Concat(AlwaysLinked(target ?? TargetRegistry.Default)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Every routine with a library in <paramref name="units"/>, in declaration order.</summary>
    public static ImmutableArray<ImportedRoutine> Imports(IEnumerable<UnitSymbol> units) =>
        [.. units.SelectMany(u => u.InterfaceMembers.Concat(u.ImplementationMembers))
            .OfType<RoutineSymbol>()
            .Where(r => r.LibraryName is not null)
            .Select(r => new ImportedRoutine(r.LinkName, NormalizeDllName(r.LibraryName!)))];

    /// <summary>LANG-463: DLL names are case-insensitive, <c>.dll</c> appended without an extension; lower-cased (CMP-430).</summary>
    public static string NormalizeDllName(string name)
    {
        var lower = name.ToLowerInvariant();
        return Path.HasExtension(lower) ? lower : lower + ".dll";
    }
}
