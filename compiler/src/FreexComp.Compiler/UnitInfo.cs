using System.Collections.Immutable;

namespace FreexComp.Compiler;

/// <summary>
/// api.md §1 (C-263): one unit of a compilation, as <see cref="Compilation.FindUnit"/> returns it: its canonical name, file
/// (a <c>.pcu</c> for a precompiled unit), whether it is the program, whether it was imported from a <c>.pcu</c>, and the
/// canonical names of the units its interface (a program's) and implementation uses clauses name, <c>System</c> left out.
/// </summary>
public sealed record UnitInfo(string Name, string Path, bool IsProgram, bool FromPcu,
                              ImmutableArray<string> InterfaceUses, ImmutableArray<string> ImplementationUses);
