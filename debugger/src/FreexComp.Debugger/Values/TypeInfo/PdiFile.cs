using FreexComp.Debugger.Session;

namespace FreexComp.Debugger.Values.TypeInfo;

internal enum PdiClassNameKind { PointerToShortString, PointerToUnicodeString, InlineShortString }

internal enum PdiParentKind { PointerToVmt, PointerToPointerToVmt }

/// <summary>
/// Header of a string payload, relative to the payload pointer. The reference count field defaults to the run-time
/// library's layout when the type information does not name it (visualizers.md DBG-524).
/// </summary>
internal sealed record PdiUnicodeStringAbi(long LengthOffset, int LengthSize, bool LengthInBytes, int CharSize,
    long RefCountOffset = PdiAbi.DefaultRefCountOffset, int RefCountSize = PdiAbi.DefaultRefCountSize);

internal sealed record PdiAnsiStringAbi(long LengthOffset, int LengthSize, long CodePageOffset, int CodePageSize,
    long RefCountOffset = PdiAbi.DefaultRefCountOffset, int RefCountSize = PdiAbi.DefaultRefCountSize);

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
    PdiVmtAbi Vmt)
{
    /// <summary>Offset of a string block's reference count from its payload when the type information omits it (rtl-abi.md §3.1).</summary>
    public const long DefaultRefCountOffset = -16;

    /// <summary>Size of that field when omitted.</summary>
    public const int DefaultRefCountSize = 4;
}

/// <summary>The loaded type-information file of a program.</summary>
internal sealed class PdiFile
{
    private readonly Dictionary<string, List<PdiRoutine>> _routinesByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PdiRoutine> _routinesByLinkageName = new(StringComparer.Ordinal);

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
            _routinesByLinkageName.TryAdd(routine.LinkageName, routine);
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
    /// The routine a stack frame runs. The lines of a finally part belong to two subprograms: the outlined part, and
    /// the routine itself where it calls the part. The line alone names the part (DBG-134); a frame whose name at the
    /// adapter is not the part's (<c>Part</c> or <c>Scope::Part</c>) is a frame of the routine the part belongs to.
    /// A frame without a name is taken by its line.
    /// </summary>
    public PdiRoutine? FindRoutine(string path, int line, string? adapterFrameName)
    {
        var routine = FindRoutine(path, line);
        while (routine is not null && !string.IsNullOrEmpty(adapterFrameName) && OutlinedPartOf(routine) is { } owner
            && adapterFrameName != routine.DisplayName && !adapterFrameName.EndsWith("::" + routine.DisplayName, StringComparison.Ordinal))
            routine = owner;
        return routine;
    }

    /// <summary>
    /// The routine whose variables are in scope in <paramref name="routine"/>: for an outlined finally part (also one
    /// nested in another) the routine that contains the <c>try</c> statement and lists the variables declared in the
    /// part; every other routine itself.
    /// </summary>
    public PdiRoutine VariableOwner(PdiRoutine routine)
    {
        while (OutlinedPartOf(routine) is { } owner)
            routine = owner;
        return routine;
    }

    /// <summary>
    /// For an outlined finally part - a routine with <c>parent</c> whose linkage name is the parent's followed by
    /// <c>.$fin</c> and a number - its parent; null for any other routine (a nested routine too) and when the parent
    /// is not described.
    /// </summary>
    private PdiRoutine? OutlinedPartOf(PdiRoutine routine)
    {
        const string Marker = ".$fin";
        if (routine.Parent is not { } parent || routine.LinkageName.Length <= parent.Length + Marker.Length
            || !routine.LinkageName.StartsWith(parent, StringComparison.Ordinal)
            || string.CompareOrdinal(routine.LinkageName, parent.Length, Marker, 0, Marker.Length) != 0
            || routine.LinkageName.AsSpan(parent.Length + Marker.Length).IndexOfAnyExceptInRange('0', '9') >= 0)
            return null;
        return _routinesByLinkageName.GetValueOrDefault(parent);
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
