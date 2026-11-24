using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// The <c>abi</c> block of the Pascal debug-info sidecar (debug-info-sidecar.md §2, §3). Every value is normative in
/// rtl-abi.md: string header §3.1 (<c>Length</c> Int32 at −12, <c>CodePage</c> Word at −8), dynamic arrays §4 (<c>Length</c>
/// NativeInt at −8), VMT §7.2 (<c>Parent</c> at +0, <c>ClassName</c> at +16 as a UnicodeString payload pointer), ABI-125
/// (<c>System._Raise</c>, <c>_PascalExceptionCode = $E0504153</c>, <c>FMessage</c>).
/// </summary>
internal static class PdiAbi
{
    public const string RaiseHookSymbol = "System._Raise";
    public const uint PascalExceptionCode = 0xE0504153;
    public const string ExceptionMessageField = "FMessage";
    public const int StringLengthOffset = -12;
    public const int StringLengthSize = 4;
    public const int AnsiCodePageOffset = -8;
    public const int AnsiCodePageSize = 2;
    public const int UnicodeCharSize = 2;

    /// <param name="target">CMP-1009: the pointer size and the offsets of the layouts holding addresses come from the target.</param>
    public static JsonObject Block(TargetInfo target) => new()
    {
        ["pointerSize"] = target.PointerSize,
        ["raiseHookSymbol"] = RaiseHookSymbol,
        ["pascalExceptionCode"] = PascalExceptionCode,
        ["exceptionMessageField"] = ExceptionMessageField,
        ["unicodeString"] = new JsonObject
        {
            ["lengthOffset"] = StringLengthOffset, ["lengthSize"] = StringLengthSize, ["lengthUnit"] = "chars", ["charSize"] = UnicodeCharSize,
        },
        ["ansiString"] = new JsonObject
        {
            ["lengthOffset"] = StringLengthOffset, ["lengthSize"] = StringLengthSize, ["codePageOffset"] = AnsiCodePageOffset, ["codePageSize"] = AnsiCodePageSize,
        },
        ["dynArray"] = new JsonObject { ["lengthOffset"] = AbiLayout.DynArrayLengthOffset(target), ["lengthSize"] = target.PointerSize },
        ["vmt"] = new JsonObject
        {
            ["classNameOffset"] = AbiLayout.VmtFieldOffset(target, 2), ["classNameKind"] = "pointerToUnicodeString",
            ["parentOffset"] = AbiLayout.VmtFieldOffset(target, 0), ["parentKind"] = "pointerToVmt",
        },
        ["set"] = new JsonObject { ["bitOrder"] = "lsb0" },
    };
}

/// <summary>
/// CMP-425: the sidecar fragment of one unit (<c>&lt;obj-dir&gt;\&lt;Unit&gt;.pdi.json</c>, debug-info-sidecar.md §1): the same schema
/// with exactly one entry in <c>units</c> and only the types the unit references. Filled by <see cref="DebugInfoEmitter"/> at the
/// same points that emit <c>DISubprogram</c>, <c>DILocalVariable</c> and <c>DIGlobalVariable</c> nodes (DBG-145). Type ids are
/// given in first-use order (deterministic, DBG-106).
/// </summary>
internal sealed class PdiFragmentBuilder(TargetInfo target, string unitName, string unitPath, string container)
{
    private readonly Dictionary<TypeSymbol, int> _ids = [];
    private readonly Dictionary<TypeAlias, int> _aliasIds = new(ReferenceEqualityComparer.Instance);
    private readonly List<JsonObject> _types = [];
    private readonly List<(string Name, string LinkageName, int Type)> _globals = [];
    private readonly List<PdiRoutine> _routines = [];

    /// <summary>DBG-140/145: a unit-level variable for which a <c>DIGlobalVariable</c> was emitted.</summary>
    public void AddGlobal(string name, string linkageName, TypeSymbol type)
    {
        if (!name.StartsWith('$'))
        {
            _globals.Add((name, linkageName, TypeId(type)));
        }
    }

    /// <summary>A routine with a <c>DISubprogram</c> (debug-info-sidecar.md §5); its variables follow through <see cref="PdiRoutine.AddVariable"/>.</summary>
    public PdiRoutine AddRoutine(string displayName, string linkageName, string file, int line, int bodyLine, int endLine, TypeSymbol? self, string? parent)
    {
        var routine = new PdiRoutine(this, displayName, linkageName, file, line, bodyLine, endLine, self is null ? null : TypeId(self), parent);
        _routines.Add(routine);
        return routine;
    }

    /// <summary>The fragment's id of <paramref name="type"/> (debug-info-sidecar.md §4), describing it (and what it references) on first use.</summary>
    public int TypeId(TypeSymbol type)
    {
        if (_ids.TryGetValue(type, out var known))
        {
            return known;
        }

        if (type is EnumTypeSymbol { BaseType: EnumTypeSymbol host })
        {
            return _ids[type] = TypeId(host);                                      // enum subranges reuse the base enum (C-128 decision)
        }

        var id = _types.Count + 1;
        _ids[type] = id;
        var entry = new JsonObject { ["id"] = id, ["name"] = IsNamed(type) ? type.Name : "" };
        if (IsNamed(type) && UnitOf(type) is { } unit)
        {
            entry["unit"] = unit;
        }

        _types.Add(entry);
        switch (type)
        {
            case IntegerTypeSymbol integer:
                Kind(entry, "integer", integer.Size);
                entry["signed"] = integer.IsSigned;
                if (integer.TypeKind == TypeKind.Subrange)
                {
                    entry["low"] = Number(integer.Min);
                    entry["high"] = Number(integer.Max);
                }

                break;
            case BooleanTypeSymbol boolean:
                Kind(entry, "boolean", boolean.Size);
                break;
            case CharTypeSymbol c:
                Kind(entry, "char", c.Size);
                break;
            case EnumTypeSymbol enumeration:
                Kind(entry, "enum", enumeration.Size);
                entry["values"] = new JsonArray([.. enumeration.EnumMembers.OrderBy(m => m.Ordinal)
                    .Select(m => (JsonNode)new JsonObject { ["name"] = m.Name, ["value"] = Number(m.Ordinal) })]);
                break;
            case SetTypeSymbol set:
                Kind(entry, "set", set.Size);
                entry["base"] = set.ElementType is { } element ? TypeId(element) : (int?)null;
                entry["bitBase"] = 0;                                              // bit 0 is ordinal 0 (rtl-abi.md §5)
                break;
            case RealTypeSymbol real:
                Kind(entry, "float", real.Size);
                entry["format"] = real.RealKind switch
                {
                    RealKind.Single => "single",
                    RealKind.Currency => "currency",
                    _ => string.Equals(real.Name, "TDateTime", StringComparison.OrdinalIgnoreCase) ? "datetime" : "double",
                };
                break;
            case StringTypeSymbol s:
                Kind(entry, "string", s.Size);
                entry["flavor"] = s.Variant switch { StringVariant.Unicode => "unicode", StringVariant.Ansi => "ansi", _ => "short" };
                if (s.Variant == StringVariant.Short)
                {
                    entry["maxLength"] = s.MaxLength;
                }

                break;
            case PointerTypeSymbol p when Strings.IsCharPointer(p, wide: true):
                Kind(entry, "string", p.Size);
                entry["flavor"] = "pchar";
                break;
            case PointerTypeSymbol p when Strings.IsCharPointer(p, wide: false):
                Kind(entry, "string", p.Size);
                entry["flavor"] = "pansichar";
                break;
            case ArrayTypeSymbol array:
                Kind(entry, "staticArray", array.Size);
                entry["element"] = TypeId(array.ElementType);
                entry["low"] = Number(array.Low);
                entry["count"] = array.Length;
                if (array.IndexType is not IntegerTypeSymbol)
                {
                    entry["indexType"] = TypeId(array.IndexType);
                }

                break;
            case DynamicArrayTypeSymbol dynamic:
                Kind(entry, "dynArray", dynamic.Size);
                entry["element"] = TypeId(dynamic.ElementType);
                break;
            case OpenArrayTypeSymbol open:
                Kind(entry, "openArray", target.PointerSize);
                entry["element"] = TypeId(open.ElementType);
                break;
            case RecordTypeSymbol record:
                Kind(entry, "record", record.Size);
                entry["fields"] = new JsonArray([.. record.Fields.Select(f => (JsonNode)new JsonObject
                {
                    ["name"] = f.Name, ["type"] = TypeId(f.Type), ["offset"] = f.Offset,
                })]);
                break;
            case ClassTypeSymbol @class:
                Kind(entry, "class", @class.Size);
                entry["ancestor"] = @class.Parent is { } parent ? TypeId(parent) : (int?)null;
                entry["instanceSize"] = @class.InstanceSize;
                entry["fields"] = new JsonArray([.. @class.Fields.Select(f => (JsonNode)new JsonObject
                {
                    ["name"] = f.Name, ["type"] = TypeId(f.Type), ["offset"] = f.Offset, ["visibility"] = VisibilityName(f.Visibility),
                })]);
                break;
            case ClassRefTypeSymbol reference:
                Kind(entry, "classRef", reference.Size);
                entry["of"] = TypeId(reference.Class);
                break;
            case PointerTypeSymbol pointer:
                Kind(entry, "pointer", pointer.Size);
                entry["to"] = pointer.PointedType is { } pointed ? TypeId(pointed) : (int?)null;
                break;
            case UntypedTypeSymbol:
                Kind(entry, "pointer", target.PointerSize);                        // an untyped parameter is described as its address (C-128)
                entry["to"] = (JsonNode?)null;
                break;
            case InterfaceTypeSymbol:
                Kind(entry, "interface", type.Size);
                break;
            case ProceduralTypeSymbol:
                Kind(entry, "procedural", type.Size);
                entry["method"] = false;
                break;
            case MethodPointerTypeSymbol:
                Kind(entry, "procedural", type.Size);
                entry["method"] = true;
                break;
            default:
                Kind(entry, "opaque", Math.Max(0, type.Size));
                break;
        }

        return id;
    }

    /// <summary>
    /// C-268 (debug-info-sidecar.md §4): a plain alias of <paramref name="unit"/> as its own entry — the alias name and unit, the
    /// target's kind and members, and <c>aliasOf</c> = the target's id (the inner alias's for an alias of an alias). Variables of
    /// the alias keep referencing the target's entry (the alias is the same type, LANG-205).
    /// </summary>
    public int AliasId(TypeAlias alias, string unit)
    {
        if (_aliasIds.TryGetValue(alias, out var known))
        {
            return known;
        }

        var target = alias.Of is { } inner ? AliasId(inner, unit) : TypeId(alias.Target);
        var entry = new JsonObject { ["id"] = _types.Count + 1, ["name"] = alias.Name, ["unit"] = unit };
        foreach (var (name, value) in _types[target - 1])
        {
            if (name is not ("id" or "name" or "unit" or "aliasOf"))
            {
                entry[name] = value?.DeepClone();
            }
        }

        entry["aliasOf"] = target;
        _types.Add(entry);
        return _aliasIds[alias] = entry["id"]!.GetValue<int>();
    }

    /// <summary>The fragment as JSON text (formatVersion 1; <c>globals</c> sorted by name, <c>routines</c> by file and line).</summary>
    public string ToJson() => PdiSidecar.Write(ToNode());

    public JsonObject ToNode()
    {
        var globals = _globals.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Name, StringComparer.Ordinal)
            .ThenBy(g => g.LinkageName, StringComparer.Ordinal)
            .Select(g => (JsonNode)new JsonObject { ["name"] = g.Name, ["type"] = g.Type, ["linkageName"] = g.LinkageName });
        var routines = _routines.OrderBy(r => r.File, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Line)
            .ThenBy(r => r.LinkageName, StringComparer.Ordinal).Select(r => (JsonNode)r.ToNode());
        return new JsonObject
        {
            ["formatVersion"] = 1,
            ["program"] = unitName,
            ["container"] = container,
            ["abi"] = PdiAbi.Block(target),
            ["types"] = new JsonArray([.. _types.Select(t => (JsonNode)t.DeepClone())]),
            ["units"] = new JsonArray(new JsonObject
            {
                ["name"] = unitName,
                ["file"] = unitPath,
                ["globals"] = new JsonArray([.. globals]),
                ["routines"] = new JsonArray([.. routines]),
            }),
        };
    }

    private static void Kind(JsonObject entry, string kind, long size)
    {
        entry["kind"] = kind;
        entry["size"] = size;
    }

    /// <summary>Declared types carry their name; constructed ones (<c>array of T</c>, <c>^T</c>, anonymous records) are <c>""</c>.</summary>
    private static bool IsNamed(TypeSymbol type) => type switch
    {
        RecordTypeSymbol record => !record.IsAnonymous && record.Name.Length > 0,
        PointerTypeSymbol pointer => pointer.Name.Length > 0 && !pointer.Name.StartsWith('^'),
        StringTypeSymbol { Variant: StringVariant.Short } s => !s.Name.StartsWith("string[", StringComparison.OrdinalIgnoreCase),
        OpenArrayTypeSymbol or UntypedTypeSymbol => false,
        _ => type.IsPredefined || !type.Locations.IsEmpty,
    };

    /// <summary>The unit declaring <paramref name="type"/> (omitted for built-ins).</summary>
    private static string? UnitOf(TypeSymbol type)
    {
        if (type.IsPredefined)
        {
            return null;
        }

        for (var symbol = type.ContainingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is UnitSymbol unit)
            {
                return unit.Name;
            }
        }

        return null;
    }

    private static string VisibilityName(Visibility visibility) => visibility switch
    {
        Visibility.StrictPrivate => "strict private",
        Visibility.Private => "private",
        Visibility.StrictProtected => "strict protected",
        Visibility.Protected => "protected",
        Visibility.Published => "published",
        _ => "public",
    };

    private static JsonNode Number(BigInteger value) =>
        value >= long.MinValue && value <= long.MaxValue ? JsonValue.Create((long)value) : JsonValue.Create((ulong)value);
}

/// <summary>A routine entry of a fragment (debug-info-sidecar.md §5).</summary>
internal sealed class PdiRoutine(PdiFragmentBuilder owner, string displayName, string linkageName, string file, int line, int bodyLine, int endLine, int? self, string? parent)
{
    private readonly List<JsonObject> _vars = [];

    public string LinkageName { get; } = linkageName;

    public string File { get; } = file;

    public int Line { get; } = line;

    /// <summary>DBG-145: a variable for which a <c>DILocalVariable</c> was emitted; <c>$</c>-prefixed temporaries are left out.</summary>
    public void AddVariable(string name, string kind, TypeSymbol type)
    {
        if (!name.StartsWith('$'))
        {
            _vars.Add(new JsonObject { ["name"] = name, ["kind"] = kind, ["type"] = owner.TypeId(type) });
        }
    }

    public JsonObject ToNode()
    {
        var node = new JsonObject
        {
            ["displayName"] = displayName,
            ["linkageName"] = LinkageName,
            ["file"] = File,
            ["line"] = Line,
            ["bodyLine"] = bodyLine,
            ["endLine"] = endLine,
            ["self"] = self,
        };
        if (parent is not null)
        {
            node["parent"] = parent;
        }

        node["vars"] = new JsonArray([.. _vars.Select(v => (JsonNode)v.DeepClone())]);
        return node;
    }
}

/// <summary>
/// CMP-425: writing and merging sidecars (debug-info-sidecar.md §1, DBG-146/147). The program's <c>&lt;output&gt;.pdi</c> is the
/// merge of the fragments of every linked unit compiled with <c>-g</c>, in link order: type ids are re-numbered in first-use
/// order, identical named types (same <c>unit</c> + <c>name</c>) and structurally identical types without a unit are unified.
/// </summary>
internal static class PdiSidecar
{
    /// <summary>The members of a type entry that hold type ids.</summary>
    private static readonly string[] TypeReferences = ["base", "element", "indexType", "ancestor", "of", "to", "aliasOf"];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTF-8 JSON text, two-space indentation, LF line ends and a final LF (debug-info-sidecar.md §1).</summary>
    public static string Write(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Merges fragments (JSON text, link order) into the program's sidecar.</summary>
    public static string Merge(TargetInfo target, string program, string container, IEnumerable<string> fragments)
    {
        var types = new List<JsonObject>();
        var byKey = new Dictionary<string, (int Id, int Fragment, int Local)>(StringComparer.Ordinal);
        var units = new JsonArray();
        var index = 0;
        foreach (var text in fragments)
        {
            var fragment = JsonNode.Parse(text)!.AsObject();
            var local = fragment["types"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<int>());
            var mapped = new Dictionary<int, int>();
            var fragmentIndex = index++;

            int Map(int id)
            {
                if (mapped.TryGetValue(id, out var known))
                {
                    return known;
                }

                var source = local[id];
                var entry = (JsonObject)source.DeepClone();
                if (source["unit"] is { } unit)
                {
                    // a named type: unified by unit + name (its id is fixed before its references, which may lead back to it)
                    var key = "U|" + unit.GetValue<string>() + "|" + source["name"]!.GetValue<string>();
                    if (byKey.TryGetValue(key, out var found) && (found.Fragment != fragmentIndex || found.Local == id))
                    {
                        return mapped[id] = found.Id;
                    }

                    var newId = types.Count + 1;
                    mapped[id] = newId;
                    byKey.TryAdd(key, (newId, fragmentIndex, id));
                    entry["id"] = newId;
                    types.Add(entry);
                    Remap(entry, Map);
                    return newId;
                }

                // a built-in or constructed type: unified by its structure (references mapped first)
                mapped[id] = -1;                                                    // a cycle through types without a unit is not expected
                Remap(entry, Map);
                entry.Remove("id");
                var structure = "S|" + entry.ToJsonString();
                if (byKey.TryGetValue(structure, out var same))
                {
                    return mapped[id] = same.Id;
                }

                var structuralId = types.Count + 1;
                byKey[structure] = (structuralId, fragmentIndex, id);
                var withId = new JsonObject { ["id"] = structuralId };
                foreach (var (name, value) in entry.ToList())
                {
                    entry.Remove(name);
                    withId[name] = value;
                }

                types.Add(withId);
                return mapped[id] = structuralId;
            }

            foreach (var unit in fragment["units"]!.AsArray())
            {
                var copy = (JsonObject)unit!.DeepClone();
                foreach (var global in copy["globals"]!.AsArray())
                {
                    MapMember(global!.AsObject(), "type", Map);
                }

                foreach (var routine in copy["routines"]!.AsArray())
                {
                    MapMember(routine!.AsObject(), "self", Map);
                    foreach (var variable in routine!["vars"]!.AsArray())
                    {
                        MapMember(variable!.AsObject(), "type", Map);
                    }
                }

                units.Add(copy);
            }

            // types the fragment lists but no variable reaches stay described (ascending local id: deterministic)
            foreach (var id in local.Keys.Order())
            {
                Map(id);
            }
        }

        return Write(new JsonObject
        {
            ["formatVersion"] = 1,
            ["program"] = program,
            ["container"] = container,
            ["abi"] = PdiAbi.Block(target),
            ["types"] = new JsonArray([.. types.Select(t => (JsonNode)t)]),
            ["units"] = units,
        });
    }

    private static void Remap(JsonObject entry, Func<int, int> map)
    {
        foreach (var name in TypeReferences)
        {
            MapMember(entry, name, map);
        }

        if (entry["fields"] is JsonArray fields)
        {
            foreach (var field in fields)
            {
                MapMember(field!.AsObject(), "type", map);
            }
        }
    }

    private static void MapMember(JsonObject node, string name, Func<int, int> map)
    {
        if (node[name] is JsonValue value && value.TryGetValue<int>(out var id))
        {
            node[name] = map(id);
        }
    }

    /// <summary>The container name of the sidecar (debug-info-sidecar.md §2).</summary>
    public static string ContainerName(DebugFormat format) => format == DebugFormat.Dwarf ? "dwarf" : "codeview";
}
