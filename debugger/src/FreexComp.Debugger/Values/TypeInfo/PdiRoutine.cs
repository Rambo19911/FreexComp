namespace FreexComp.Debugger.Values.TypeInfo;

internal enum PdiVariableKind { Param, VarParam, Self, Result, Local }

/// <summary>A parameter, local, <c>Self</c> or result of a routine.</summary>
internal sealed record PdiVariable(string Name, PdiVariableKind Kind, PdiType Type, int? FromLine, int? ToLine);

/// <summary>A unit-level variable.</summary>
internal sealed record PdiGlobal(string Name, PdiType Type, string LinkageName);

/// <summary>One routine of a unit (interface document §5).</summary>
internal sealed class PdiRoutine
{
    public required string DisplayName { get; init; }
    public required string LinkageName { get; init; }

    /// <summary>As written in the file.</summary>
    public required string File { get; init; }

    /// <summary>Line of the heading.</summary>
    public required int Line { get; init; }
    public required int BodyLine { get; init; }
    public required int EndLine { get; init; }

    /// <summary>The class of a method; null otherwise.</summary>
    public PdiType? Self { get; init; }

    /// <summary>Linkage name of the enclosing routine of a nested routine.</summary>
    public string? Parent { get; init; }
    public required IReadOnlyList<PdiVariable> Variables { get; init; }

    /// <summary>The unit that lists the routine.</summary>
    public PdiUnit Unit { get; internal set; } = null!;
}

/// <summary>One entry of <c>units[]</c>.</summary>
internal sealed class PdiUnit
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public required IReadOnlyList<PdiGlobal> Globals { get; init; }
    public required IReadOnlyList<PdiRoutine> Routines { get; init; }
}
