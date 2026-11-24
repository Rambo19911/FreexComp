using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler;

/// <summary>The module a compilation produces, decided by its main source (LANG-542, CMP-1056).</summary>
public enum ModuleKind
{
    /// <summary>A program file (or a file without heading): an executable.</summary>
    Program,

    /// <summary>A library file: a dynamic-link library.</summary>
    Library,
}

/// <summary>One export of a library file (LANG-545…549, CMP-1056).</summary>
/// <param name="Name">The export name, byte for byte (LANG-547).</param>
/// <param name="Ordinal">The explicit <c>index</c> or the assigned ordinal, 1…65535 (LANG-549).</param>
/// <param name="RoutineSymbol">The exported procedure or function.</param>
public sealed record ExportInfo(string Name, int Ordinal, RoutineSymbol RoutineSymbol);
