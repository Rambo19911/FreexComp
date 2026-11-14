using System.Collections.Immutable;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Toolchain;

/// <summary>One linked unit as the import-library step sees it: the directory of its source and its link inputs.</summary>
internal sealed record LinkUnit(string Directory, UnitLinkInputs Inputs);

/// <summary>One import library to build: the DLL and the <c>.def</c> it is generated from.</summary>
internal sealed record PlannedImportLibrary(string Dll, string Definition);

/// <summary>CMP-620 across units (C-077): which import libraries a link needs and where their <c>.def</c> files come from.</summary>
internal static class ImportLibraryPlan
{
    /// <summary>
    /// The DLLs are the union of every unit's <see cref="UnitLinkInputs.Dlls"/>, the DLL of every <c>{$L x.def}</c> (base name +
    /// <c>.dll</c>) and <see cref="LinkInputs.AlwaysLinked"/>, sorted. Each DLL's definition is looked up in the CMP-620 order: a
    /// <c>{$L}</c> <c>.def</c> of any unit (units in link order, then directive order); the directory of each unit declaring the DLL,
    /// in link order (for kernel32/ntdll then <paramref name="systemDirectory"/>); <c>&lt;rtl&gt;\..\winapi\def</c>. Not found → E4006.
    /// </summary>
    /// <param name="units">The linked units in link order (program, System, init order; CMP-625).</param>
    public static (ImmutableArray<PlannedImportLibrary> Libraries, ImmutableArray<Diagnostic> Errors) Create(
        IFileSystem fs, IReadOnlyList<LinkUnit> units, string systemDirectory, string rtlDirectory, Location location)
    {
        var defs = units.SelectMany(u => u.Inputs.Defs).Distinct(PathUtil.Comparer).ToImmutableArray();
        var dlls = units.SelectMany(u => u.Inputs.Dlls)
            .Concat(defs.Select(d => Path.GetFileNameWithoutExtension(d).ToLowerInvariant() + ".dll"))
            .Concat(LinkInputs.AlwaysLinked)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var libraries = ImmutableArray.CreateBuilder<PlannedImportLibrary>();
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var dll in dlls)
        {
            var declaring = units.Where(u => u.Inputs.Dlls.Contains(dll, StringComparer.Ordinal)).Select(u => u.Directory);
            if (LinkInputs.AlwaysLinked.Contains(dll, StringComparer.Ordinal))
            {
                declaring = declaring.Append(systemDirectory);
            }

            var (definition, notFound) = DllToolInvoker.FindDefinition(fs, dll, defs, declaring.Distinct(PathUtil.Comparer), rtlDirectory, location);
            if (definition is null)
            {
                errors.Add(notFound!);
                continue;
            }

            libraries.Add(new PlannedImportLibrary(dll, definition));
        }

        return (libraries.ToImmutable(), errors.ToImmutable());
    }
}
