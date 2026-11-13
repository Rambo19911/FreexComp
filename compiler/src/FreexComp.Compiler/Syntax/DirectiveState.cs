using System.Collections.Immutable;

namespace FreexComp.Compiler.Syntax;

/// <summary><c>{$WARN id ON|OFF|ERROR}</c> setting for one diagnostic code.</summary>
public enum WarnSetting { On, Off, Error }

/// <summary>
/// Switch state at a token (CMP-111, directives.md §5). Immutable; the preprocessor creates a new instance only when
/// a directive changes something, so consecutive tokens share one instance.
/// </summary>
public sealed record DirectiveState(bool RangeChecks, bool OverflowChecks, bool BoolEval, bool Assertions,
                                    bool IoChecks, bool TypeInfo, int Align, int MinEnumSize, bool PointerMath,
                                    bool ScopedEnums, bool WarningsOn, bool HintsOn)
{
    /// <summary>The defaults of the directives.md §5 table.</summary>
    public static DirectiveState Default { get; } = new(
        RangeChecks: false, OverflowChecks: false, BoolEval: false, Assertions: true, IoChecks: true, TypeInfo: false,
        Align: 8, MinEnumSize: 1, PointerMath: false, ScopedEnums: false, WarningsOn: true, HintsOn: true);

    /// <summary><c>{$WARN}</c> settings keyed by diagnostic code (case-insensitive), e.g. "W1041".</summary>
    public ImmutableDictionary<string, WarnSetting> WarnOverrides { get; init; } =
        ImmutableDictionary<string, WarnSetting>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>{$RTTI}</c> scope (LANG-882): "broad", "published", or null for the compilation's setting.</summary>
    public string? Rtti { get; init; }
}
