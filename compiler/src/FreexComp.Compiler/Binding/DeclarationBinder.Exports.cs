using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// dll-output.md §2.2 (C-286): the exports clauses of a library file — resolution and exportability (LANG-545), export names
// (LANG-547, 548), ordinals (LANG-549) and the boundary-safety warning (LANG-551).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// LANG-547: link names the export thunks must not take — the memory routines and <c>_fltused</c> (CMP-610), and the target's
    /// entry, personality and stack-probe symbols.
    /// </summary>
    private static readonly string[] ReservedExportNames = ["memcpy", "memmove", "memset", "memcmp", "strlen", "wcslen", "_fltused"];

    private const int MaxOrdinal = 65535;

    /// <summary>One export item that named an exportable routine: its name and explicit index when they were valid.</summary>
    private sealed record PendingExport(ExportItemSyntax Item, RoutineSymbol Routine, string? Name, int? Index);

    private readonly List<PendingExport> _exports = [];

    /// <summary>
    /// LANG-544…547, 549, 551: the items of one clause, resolved at this point of the declaration part (later declarations are
    /// not visible). Outside the unit level of a library file the parser has reported E2211 and nothing is bound.
    /// </summary>
    private void BindExportsClause(ExportsClauseSyntax clause)
    {
        if (_container is not UnitSymbol { IsLibrary: true } || !IsGlobalLevel)
        {
            return;
        }

        foreach (var item in clause.Items)
        {
            if (item.Name.IsMissing || item.Name.ContainsDiagnostics || ResolveExport(item) is not { } routine)
            {
                continue;
            }

            string? name = routine.Name;                                // LANG-547: the declared casing
            SyntaxNode nameAt = item.Name;
            if (item.NameSpecifier is { Value: var nameValue })
            {
                name = nameValue.IsMissing ? null : FoldString(nameValue);
                nameAt = nameValue;
            }

            if (name is not null && !IsValidExportName(name))
            {
                _diagnostics.Report(nameAt, "E2213", name);
                name = null;
            }
            else if (name is not null && routine.ExplicitLinkName == name && Emit.IrEmitter.HasHiddenResult(routine))
            {
                // CMP-1054: exported under its own public name there is no thunk to return the hidden result pointer
                _diagnostics.Report(item.Name, "E2212", NameOf(item.Name), "the result type needs an export thunk; export it under another name");
                continue;
            }

            int? index = null;
            if (item.IndexSpecifier is { Value: var indexValue } && !indexValue.IsMissing)
            {
                // the folder's own E2050 would repeat what E2215 says
                if (new ConstantFolder(_types, _scope, new DiagnosticBag()).Fold(indexValue) is { Value: BigInteger value, Type: IntegerTypeSymbol }
                    && value >= 1 && value <= MaxOrdinal)
                {
                    index = (int)value;
                }
                else
                {
                    _diagnostics.Report(indexValue, "E2215");
                    continue;
                }
            }

            if (FirstUnsafeType(routine) is { } unsafeType)
            {
                _diagnostics.Report(item, "W1082", routine.Name, unsafeType.Name);     // LANG-551: once per item
            }

            _exports.Add(new PendingExport(item, routine, name, index));
        }
    }

    /// <summary>LANG-545: the routine an item designates; null after E2003 or E2212.</summary>
    private RoutineSymbol? ResolveExport(ExportItemSyntax item)
    {
        var written = NameOf(item.Name);
        RoutineSymbol? Refuse(string reason)
        {
            _diagnostics.Report(item.Name, "E2212", written, reason);
            return null;
        }

        const string notARoutine = "not a procedure or function";
        Scope scope;
        string name;
        switch (item.Name)
        {
            case IdentifierNameSyntax identifier:
                (scope, name) = (_scope, NameOf(identifier.Identifier));
                break;
            case QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified when UnitQualifiers.Qualifier(_scope, qualified.Left) is { } unit:
                (scope, name) = (unit.Qualified, NameOf(right.Identifier));                 // UnitName.Proc (LANG-538)
                break;
            case QualifiedNameSyntax qualified:
            {
                // no unit of that name: Type.Method, Routine.Nested, or nothing at all
                var first = qualified.DescendantTokens().First(t => t.Kind == SyntaxKind.IdentifierToken);
                switch (_scope.Lookup(NameOf(first)))
                {
                    case null:
                        _diagnostics.ReportUndeclared(item.Name, NameOf(first));
                        return null;
                    case TypeSymbol:
                        return Refuse("it is a method");
                    case RoutineSymbol:
                        return Refuse("it is a nested routine");
                    default:
                        return Refuse(notARoutine);
                }
            }

            default:
                return null;
        }

        var symbol = scope.Lookup(name);
        switch (symbol)
        {
            case null when item.Name is IdentifierNameSyntax && UnitQualifiers.Qualifier(_scope, item.Name) is not null:
                return Refuse(notARoutine);                                 // a unit
            case null:
                _diagnostics.ReportUndeclared(item.Name, written);
                return null;
            case IntrinsicSymbol:
                return Refuse("it is a compiler intrinsic");
            case MethodSymbol:
                return Refuse("it is a method");
            case RoutineSymbol routine:
                return routine.IsNested ? Refuse("it is a nested routine")
                    : routine.IsOverload || scope.LookupCandidates(name).Length > 1 ? Refuse("it is overloaded")
                    : routine.Declaration?.Heading.TypeParameterList is not null ? Refuse("it is generic")
                    : routine.IsExternal ? Refuse("it is imported (declared external)")
                    : routine;
            default:
                return Refuse(notARoutine);
        }
    }

    /// <summary>LANG-547: <c>[A-Za-z_][A-Za-z0-9_$]{0,254}</c> and none of the reserved link names.</summary>
    private bool IsValidExportName(string name)
    {
        var target = _types.Target;
        return name.Length is >= 1 and <= 255
            && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
            && !ReservedExportNames.Contains(name, StringComparer.Ordinal)
            && name != target.EntrySymbol && name != target.LibraryEntrySymbol && name != target.PersonalitySymbol && name != target.StackProbeSymbol;
    }

    /// <summary>
    /// LANG-548, 549: duplicate names and indexes, then the ordinals — explicit indexes as given, the other items in source order
    /// the smallest positive ordinal that no explicit index uses and that is not assigned yet. The result is in ordinal order.
    /// </summary>
    private void FinishExports(UnitSymbol library)
    {
        var names = new Dictionary<string, PendingExport>(StringComparer.Ordinal);
        var indexes = new Dictionary<int, PendingExport>();
        var valid = new List<PendingExport>();
        foreach (var export in _exports)
        {
            var ok = true;
            if (export.Name is { } name && !names.TryAdd(name, export))
            {
                _diagnostics.Report(export.Item, "E2214", [new RelatedLocation(DiagnosticBag.LocationOf(names[name].Item), "first export of this name")], name);
                ok = false;
            }

            if (export.Index is { } index && !indexes.TryAdd(index, export))
            {
                _diagnostics.Report(export.Item, "E2216",
                    [new RelatedLocation(DiagnosticBag.LocationOf(indexes[index].Item), "first export with this index")], index);
                ok = false;
            }

            if (ok && export.Name is not null)
            {
                valid.Add(export);
            }
        }

        var result = ImmutableArray.CreateBuilder<ExportInfo>();
        var next = 1;
        foreach (var export in valid)
        {
            if (export.Index is not { } ordinal)
            {
                while (next <= MaxOrdinal && indexes.ContainsKey(next))
                {
                    next++;
                }

                if (next > MaxOrdinal)
                {
                    _diagnostics.Report(export.Item, "E2215");            // LANG-549: more than 65535 exports
                    break;
                }

                ordinal = next++;
            }

            result.Add(new ExportInfo(export.Name!, ordinal, export.Routine));
        }

        library.Exports = [.. result.OrderBy(e => e.Ordinal)];
    }

    /// <summary>LANG-551: the first parameter type (in order), else the result type, that is not boundary-safe; null when all are.</summary>
    private static TypeSymbol? FirstUnsafeType(RoutineSymbol routine) =>
        routine.Parameters.Select(p => p.Type).Concat(routine.ReturnType is { } result ? [result] : []).FirstOrDefault(t => !IsBoundarySafe(t));

    /// <summary>
    /// LANG-551: integer, Boolean, character, enumeration and real types, pointers, plain procedural types, short strings,
    /// interfaces, untyped parameters, and records, static arrays and sets made only of such types.
    /// </summary>
    internal static bool IsBoundarySafe(TypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Error or TypeKind.Integer or TypeKind.Boolean or TypeKind.Char or TypeKind.Real or TypeKind.Enum or TypeKind.Subrange
            or TypeKind.Set or TypeKind.ShortString or TypeKind.Pointer or TypeKind.Procedural or TypeKind.Untyped => true,
        TypeKind.Interface => type is not InterfaceTypeSymbol { IsMethodReference: true },   // 'reference to': the caller's closure object
        TypeKind.Array => type is ArrayTypeSymbol { ElementType: var element } && IsBoundarySafe(element),
        TypeKind.Record => type is RecordTypeSymbol record && record.Fields.All(f => IsBoundarySafe(f.Type)),
        _ => false,       // long strings, dynamic and open arrays, classes, class references, method pointers, method references, Text
    };
}
