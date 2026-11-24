using FreexComp.Debugger.Session;

namespace FreexComp.Debugger.Values.TypeInfo;

internal enum PdiClassNameKind { PointerToShortString, PointerToUnicodeString, InlineShortString }

internal enum PdiParentKind { PointerToVmt, PointerToPointerToVmt }

/// <summary>Header of a string payload, relative to the payload pointer.</summary>
internal sealed record PdiUnicodeStringAbi(long LengthOffset, int LengthSize, bool LengthInBytes, int CharSize);

internal sealed record PdiAnsiStringAbi(long LengthOffset, int LengthSize, long CodePageOffset, int CodePageSize);

internal sealed record PdiDynArrayAbi(long LengthOffset, int LengthSize);

internal sealed record PdiVmtAbi(long ClassNameOffset, PdiClassNameKind ClassNameKind, long ParentOffset, PdiParentKind ParentKind);

/// <summary>The <c>abi</c> block: every layout constant the debugger uses comes from here (interface document §3).</summary>
internal sealed record PdiAbi(
    int PointerSize,
    string RaiseHookSymbol,
    uint PascalExceptionCode,
    string ExceptionMessageField,
    PdiUnicodeStringAbi UnicodeString,
    PdiAnsiStringAbi AnsiString,
    PdiDynArrayAbi DynArray,
    PdiVmtAbi Vmt);

/// <summary>The loaded type-information file of a program.</summary>
internal sealed class PdiFile
{
    private readonly Dictionary<string, List<PdiRoutine>> _routinesByFile = new(StringComparer.OrdinalIgnoreCase);

    public PdiFile(string program, string container, PdiAbi abi, IReadOnlyList<PdiType> types, IReadOnlyList<PdiUnit> units)
    {
        Program = program;
        Container = container;
        Abi = abi;
        Types = types;
        Units = units;
        foreach (var routine in units.SelectMany(unit => unit.Routines))
        {
            var file = PathText.Normalize(routine.File);
            if (!_routinesByFile.TryGetValue(file, out var list))
                _routinesByFile.Add(file, list = []);
            list.Add(routine);
        }
    }

    /// <summary>
    /// One view over the type information of several modules of one process (a library and its host): the types and
    /// units of all of them, in the order given; program, container and layout constants are those of the first. Null
    /// for none, the file itself for one. Modules built with the same run-time library describe its routines and
    /// types alike, so the first description of a name or a source line stands for all of them.
    /// </summary>
    public static PdiFile? Merge(IReadOnlyList<PdiFile> files) => files.Count switch
    {
        0 => null,
        1 => files[0],
        _ => new PdiFile(files[0].Program, files[0].Container, files[0].Abi,
            [.. files.SelectMany(file => file.Types)], [.. files.SelectMany(file => file.Units)]),
    };

    public string Program { get; }
    public string Container { get; }
    public PdiAbi Abi { get; }

    /// <summary>In file order.</summary>
    public IReadOnlyList<PdiType> Types { get; }
    public IReadOnlyList<PdiUnit> Units { get; }

    /// <summary>
    /// DBG-134: the routine whose file equals <paramref name="path"/> (case-insensitive) and whose lines
    /// <c>line … endLine</c> contain <paramref name="line"/>; of several (nested routines) the one with the greatest
    /// heading line. Null when the frame has no type information.
    /// </summary>
    public PdiRoutine? FindRoutine(string path, int line)
    {
        if (!_routinesByFile.TryGetValue(PathText.Normalize(path), out var routines))
            return null;
        PdiRoutine? best = null;
        foreach (var routine in routines)
        {
            if (routine.Line <= line && line <= routine.EndLine && (best is null || routine.Line > best.Line))
                best = routine;
        }

        return best;
    }

    /// <summary>
    /// DBG-168: the class named <paramref name="name"/> (case-insensitive; alias entries are skipped). Of several,
    /// one that is <paramref name="staticType"/> or descends from it is preferred, then the first in file order.
    /// </summary>
    public PdiType? FindClass(string name, PdiType? staticType)
    {
        PdiType? first = null;
        foreach (var type in Types)
        {
            if (type.Kind != PdiTypeKind.Class || type.AliasOf is not null || !string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (staticType is not null && type.IsOrDescendsFrom(staticType))
                return type;
            first ??= type;
        }

        return first;
    }
}
