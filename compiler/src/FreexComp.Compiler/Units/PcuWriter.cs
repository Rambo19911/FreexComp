using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Units;

/// <summary>What one <c>.pcu</c> needs to know about its unit beyond the bound unit itself.</summary>
/// <param name="ReadSource">The bytes the unit (or an include) was compiled from, with size and last-write time (null when not on disk).</param>
internal sealed record PcuContext(
    string CompilerFingerprint,
    string OptionsFingerprint,
    Func<string, (byte[] Bytes, DateTime? Time)> ReadSource,
    IReadOnlyList<LoadedUnit> InitOrder,
    AppType AppType,
    Func<LoadedUnit, (bool HasInit, bool HasFinal)> Sections,
    BuiltInTypes Types);

/// <summary>
/// C-073: the <c>.pcu</c> of a bound unit (architecture.md §6) as canonical JSON — UTF-8, keys in the order of §6, no
/// insignificant whitespace, one final LF — and its <c>interfaceHash</c> (CMP-152). One writer per compilation: interface hashes
/// of used units are computed once and reused (interface uses form a DAG, LANG-513).
/// </summary>
internal sealed class PcuWriter(PcuContext context)
{
    public const int FormatVersion = 1;

    /// <summary>CMP-150 rule 2: compiler version, ABI version, pinned LLVM version.</summary>
    public static string CompilerFingerprint => $"opc {CompilerInfo.Version}; abi {AbiInfo.Version}; llvm {CompilerInfo.LlvmPin}";

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Dictionary<LoadedUnit, (JsonObject Interface, string Hash)> _interfaces = [];

    /// <summary>The complete <c>.pcu</c> text of <paramref name="unit"/> (bound without errors).</summary>
    public string Write(LoadedUnit unit) => Serialize(Build(unit));

    /// <summary>CMP-152: <c>sha256:</c> + hex SHA-256 of the canonical interface JSON without <c>decl</c> and <c>doc</c> (positions, comments).</summary>
    public string InterfaceHash(LoadedUnit unit) => Interface(unit).Hash;

    public static string Serialize(JsonNode node) => node.ToJsonString(Compact) + "\n";

    /// <summary>
    /// The <c>.pcu</c> object. For a precompiled unit (C-074) the header — sources, dependencies, sections, link inputs — is the
    /// recorded one and the interface is rebuilt from the imported symbols, so writing it again shows what the reader understood.
    /// </summary>
    public JsonObject Build(LoadedUnit unit)
    {
        if (unit.Pcu is { } recorded)
        {
            var paths = ((JsonArray)recorded["sources"]!).Select(s => (string)s!["path"]!).ToImmutableArray();
            var copy = (JsonObject)recorded.DeepClone();
            copy["interfaceHash"] = InterfaceHash(unit);
            copy["interface"] = WithDeclarations(unit, paths);
            return copy;
        }

        var sources = Sources(unit);
        var (hasInit, hasFinal) = context.Sections(unit);
        var root = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["unit"] = unit.Name,
            ["kind"] = unit.IsProgram ? "program" : "unit",
            ["compilerFingerprint"] = context.CompilerFingerprint,
            ["optionsFingerprint"] = context.OptionsFingerprint,
            ["sources"] = new JsonArray([.. sources.Select(s => (JsonNode?)s.Node)]),
            ["dependencies"] = Dependencies(unit),
            ["interfaceHash"] = InterfaceHash(unit),
            ["hasInit"] = hasInit,
            ["hasFinal"] = hasFinal,
            ["linkInputs"] = new JsonObject
            {
                ["dlls"] = Strings(unit.LinkInputs.Dlls),
                ["objects"] = Strings(unit.LinkInputs.Objects),
                ["defs"] = Strings(unit.LinkInputs.Defs),
            },
            ["resources"] = new JsonArray(),
            ["interface"] = WithDeclarations(unit, sources.Select(s => s.Path).ToImmutableArray()),
            ["genericBodies"] = new JsonArray(),
        };
        if (unit.IsProgram)
        {
            root["unitOrder"] = Strings(context.InitOrder.Select(u => u.Name));          // CMP-163
            root["appType"] = context.AppType == AppType.Gui ? "gui" : "console";
            root["unitTable"] = UnitTable();
        }

        return root;
    }

    /// <summary>
    /// C-075: a program's CMP-355 unit table as compiled — per unit in init order its name and whether its <c>$init</c>/<c>$final</c>
    /// are referenced. The program's code depends on it, but the units' interface hashes do not cover it (an implementation-only
    /// edit can add an initialization section or a unit in an implementation uses clause), so it is part of the program's
    /// up-to-date check.
    /// </summary>
    public JsonArray UnitTable() =>
        new([.. context.InitOrder.Select(u =>
        {
            var (hasInit, hasFinal) = context.Sections(u);
            return (JsonNode?)new JsonObject { ["unit"] = u.Name, ["hasInit"] = hasInit, ["hasFinal"] = hasFinal };
        })]);

    // ------------------------------------------------------------------ header parts

    private List<(string Path, JsonObject Node)> Sources(LoadedUnit unit)
    {
        var paths = new List<string> { unit.Path };
        foreach (var include in unit.Tree!.IncludedTrees.Select(t => t.Text.Path))
        {
            if (!paths.Contains(include, PathUtil.Comparer))
            {
                paths.Add(include);
            }
        }

        return [.. paths.Select((path, i) =>
        {
            var (bytes, time) = context.ReadSource(path);
            return (path, new JsonObject
            {
                ["path"] = path,
                ["role"] = i == 0 ? "unit" : "include",
                ["size"] = bytes.LongLength,
                ["mtime"] = (time ?? DateTime.MinValue).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            });
        })];
    }

    /// <summary><c>System</c> (section interface) for every unit but System, then the interface uses, then the implementation uses.</summary>
    private JsonArray Dependencies(LoadedUnit unit)
    {
        var dependencies = new JsonArray();
        foreach (var (used, section) in Used(unit, includeImplementation: true))
        {
            dependencies.Add(new JsonObject
            {
                ["unit"] = used.Name,
                ["path"] = used.Path,
                ["section"] = section,
                ["interfaceHash"] = InterfaceHash(used),
            });
        }

        return dependencies;
    }

    private IEnumerable<(LoadedUnit Unit, string Section)> Used(LoadedUnit unit, bool includeImplementation)
    {
        var system = context.InitOrder.Count > 0 && IdentifierComparer.Instance.Equals(context.InitOrder[0].Name, "System") ? context.InitOrder[0] : null;
        if (system is not null && !ReferenceEquals(system, unit))
        {
            yield return (system, "interface");
        }

        foreach (var used in unit.InterfaceUses)
        {
            yield return (used, "interface");
        }

        if (includeImplementation)
        {
            foreach (var used in unit.ImplementationUses)
            {
                yield return (used, "implementation");
            }
        }
    }

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);

    // ------------------------------------------------------------------ interface (CMP-152)

    private (JsonObject Interface, string Hash) Interface(LoadedUnit unit)
    {
        if (_interfaces.TryGetValue(unit, out var known))
        {
            return known;
        }

        var builder = new InterfaceBuilder(unit, Closure(unit), sources: null, context.Types);
        var node = new JsonObject
        {
            ["uses"] = new JsonArray([.. Used(unit, includeImplementation: false).Select(u => (JsonNode?)new JsonObject
            {
                ["unit"] = u.Unit.Name,
                ["interfaceHash"] = InterfaceHash(u.Unit),
            })]),
            ["symbols"] = builder.Symbols(),
            ["types"] = builder.Types,
        };
        var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString(Compact))));
        _interfaces[unit] = (node, hash);
        return (node, hash);
    }

    /// <summary>The written interface: the hashed one with <c>decl</c> spans (indexes into <paramref name="sources"/>).</summary>
    private JsonObject WithDeclarations(LoadedUnit unit, ImmutableArray<string> sources)
    {
        var builder = new InterfaceBuilder(unit, Closure(unit), sources, context.Types);
        var hashed = Interface(unit).Interface;
        return new JsonObject
        {
            ["uses"] = hashed["uses"]!.DeepClone(),
            ["symbols"] = builder.Symbols(),
            ["types"] = builder.Types,
        };
    }

    /// <summary>
    /// The units whose interface types <paramref name="unit"/>'s interface may name: System and its interface-uses, transitively, in
    /// init order (a dependency before its users, so a type re-exported by an alias is owned by the unit that created it).
    /// </summary>
    private List<LoadedUnit> Closure(LoadedUnit unit)
    {
        var reached = new HashSet<LoadedUnit>();
        var stack = new Stack<LoadedUnit>(Used(unit, includeImplementation: false).Select(u => u.Unit));
        while (stack.TryPop(out var next))
        {
            if (reached.Add(next))
            {
                foreach (var used in next.InterfaceUses)
                {
                    stack.Push(used);
                }
            }
        }

        return [.. context.InitOrder.Where(reached.Contains)];
    }

    /// <summary>Builds the <c>symbols</c> and <c>types</c> arrays of one unit's interface.</summary>
    private sealed class InterfaceBuilder(LoadedUnit unit, List<LoadedUnit> closure, ImmutableArray<string>? sources, BuiltInTypes types)
    {
        private readonly BuiltInTypes _types = types;

        private readonly Dictionary<TypeSymbol, int> _ids = new(ReferenceEqualityComparer.Instance);
        private Dictionary<TypeSymbol, (string Unit, string Name)>? _owners;

        public JsonArray Types { get; } = [];

        public JsonArray Symbols()
        {
            var symbols = new JsonArray();
            if (unit.Interface?.InterfaceScope is not { } scope)
            {
                return symbols;
            }

            foreach (var (name, symbol) in scope.Names.Zip(scope.Symbols))
            {
                if (Symbol(symbols.Count, name, symbol) is { } entry)
                {
                    symbols.Add(entry);
                }
            }

            return symbols;
        }

        private JsonObject? Symbol(int id, string name, Symbol symbol)
        {
            var entry = new JsonObject { ["id"] = id };
            switch (symbol)
            {
                case ConstantSymbol constant:
                    Head(entry, "const", name, constant);
                    entry["type"] = TypeId(constant.Type);
                    if (constant.IsTyped)
                    {
                        entry["typed"] = true;
                        entry["linkName"] = Emit.Mangler.GlobalVariable(constant.Storage!).Text;
                    }
                    else
                    {
                        entry["value"] = Value(constant.Value, constant.Type);
                    }

                    return entry;
                case VariableSymbol variable:
                    Head(entry, "var", name, variable);
                    entry["type"] = TypeId(variable.Type);
                    entry["linkName"] = Emit.Mangler.GlobalVariable(variable).Text;
                    return entry;
                case TypeSymbol type:
                    Head(entry, "typeAlias", name, type);
                    entry["type"] = TypeId(type);
                    entry["distinct"] = IsOwn(type) && IdentifierComparer.Instance.Equals(type.Name, name);
                    return entry;
                case RoutineSymbol routine:
                    Head(entry, "routine", name, routine);
                    entry["linkName"] = routine.LinkName;
                    entry["params"] = Parameters(routine);
                    entry["result"] = routine.ReturnType is { } result ? TypeId(result) : null;
                    entry["flags"] = Strings(Flags(routine));
                    entry["dll"] = routine.LibraryName is { } library ? Emit.LinkInputs.NormalizeDllName(library) : null;
                    entry["importName"] = routine.IsExternal ? routine.LinkName : null;
                    return entry;
                case EnumMemberSymbol member:
                    Head(entry, "enumMember", name, member);
                    entry["type"] = TypeId(member.Type);
                    entry["ordinal"] = member.Ordinal;
                    return entry;
                default:
                    return null;                                        // nothing else is declared at unit level yet
            }
        }

        private void Head(JsonObject entry, string kind, string name, Symbol symbol)
        {
            entry["kind"] = kind;
            entry["name"] = name;
            if (sources is { } files && symbol.Locations.FirstOrDefault() is { } location
                && files.IndexOf(location.Path, PathUtil.Comparer) is var file and >= 0)
            {
                entry["decl"] = new JsonObject { ["file"] = file, ["start"] = location.Span.Start, ["length"] = location.Span.Length };
            }

            if (sources is not null && symbol.Documentation is { } doc)
            {
                entry["doc"] = doc;
            }

            if (symbol.IsDeprecated)
            {
                entry["deprecated"] = true;
            }
        }

        private static IEnumerable<string> Flags(RoutineSymbol routine)
        {
            if (routine.IsOverload)
            {
                yield return "overload";
            }

            if (routine.IsInline)
            {
                yield return "inline";
            }

            if (routine.IsExternal)
            {
                yield return "external";
            }

            if (routine.IsNoReturn)
            {
                yield return "noreturn";
            }
        }

        private JsonArray Parameters(RoutineSymbol routine) =>
            new([.. routine.Parameters.Select(p => (JsonNode?)new JsonObject
            {
                ["name"] = p.Name,
                ["type"] = TypeId(p.Type),
                ["mode"] = p.Mode switch { ParameterMode.Const => "const", ParameterMode.Var => "var", ParameterMode.Out => "out", _ => "value" },
                ["openArray"] = p.IsOpenArray,
            })]);

        /// <summary>Ordinals and chars as numbers, sets as ascending element ordinals, reals as round-trip strings, nil as null.</summary>
        private static JsonNode? Value(ConstantValue? value, TypeSymbol type) => value?.Value switch
        {
            null => null,
            bool b => b,
            string s => s,
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            BigInteger mask when type is SetTypeSymbol => new JsonArray([.. Elements(mask).Select(e => (JsonNode?)e)]),
            BigInteger i => Number(i),
            _ => throw new NotSupportedException("constant value " + value.Value.GetType().Name),
        };

        private static IEnumerable<int> Elements(BigInteger mask)
        {
            for (var bit = 0; !mask.IsZero; bit++, mask >>= 1)
            {
                if (!mask.IsEven)
                {
                    yield return bit;
                }
            }
        }

        private static JsonNode Number(BigInteger value) =>
            value >= long.MinValue && value <= long.MaxValue ? JsonValue.Create((long)value) : JsonValue.Create((ulong)value);

        // -------------------------------------------------------------- types

        private bool IsOwn(TypeSymbol type) => !IsBuiltIn(type) && Owner(type) is null;

        private bool IsBuiltIn(TypeSymbol type) => BuiltInName(type) is not null;

        /// <summary>A predeclared type (LANG-200) by its canonical name, or <c>nil</c> / <c>[]</c> (BuiltInTypes.Nil, .EmptySet).</summary>
        private string? BuiltInName(TypeSymbol type) =>
            ReferenceEquals(type, _types.Nil) ? "nil"
            : ReferenceEquals(type, _types.EmptySet) ? "[]"
            : ReferenceEquals(_types.Scope.LookupLocal(type.Name), type) ? type.Name
            : null;

        /// <summary>The unit of <see cref="closure"/> whose interface first declares <paramref name="type"/>, with that name.</summary>
        private (string Unit, string Name)? Owner(TypeSymbol type)
        {
            if (_owners is null)
            {
                _owners = new(ReferenceEqualityComparer.Instance);
                foreach (var other in closure.Where(u => !ReferenceEquals(u, unit)))
                {
                    if (other.Interface?.InterfaceScope is not { } scope)
                    {
                        continue;
                    }

                    foreach (var (name, symbol) in scope.Names.Zip(scope.Symbols))
                    {
                        if (symbol is TypeSymbol t && !IsBuiltIn(t))
                        {
                            _owners.TryAdd(t, (other.Name, name));
                        }
                    }
                }
            }

            return _owners.TryGetValue(type, out var owner) ? owner : null;
        }

        private int TypeId(TypeSymbol type)
        {
            if (_ids.TryGetValue(type, out var known))
            {
                return known;
            }

            var id = Types.Count;
            _ids.Add(type, id);
            var entry = new JsonObject { ["id"] = id };
            Types.Add(entry);
            if (BuiltInName(type) is { } builtIn)
            {
                entry["kind"] = "builtin";
                entry["name"] = builtIn;
                return id;
            }

            if (Owner(type) is { } owner)
            {
                entry["kind"] = "ref";
                entry["unit"] = owner.Unit;
                entry["name"] = owner.Name;
                return id;
            }

            Describe(entry, type);
            return id;
        }

        private void Describe(JsonObject entry, TypeSymbol type)
        {
            switch (type)
            {
                case EnumTypeSymbol { TypeKind: TypeKind.Enum } e:
                    entry["kind"] = "enum";
                    entry["name"] = e.Name;
                    entry["members"] = new JsonArray([.. e.EnumMembers.Select(m => (JsonNode?)new JsonObject { ["name"] = m.Name, ["ordinal"] = m.Ordinal })]);
                    entry["size"] = e.Size;
                    entry["scoped"] = e.IsScoped;
                    return;
                case EnumTypeSymbol e:
                    Subrange(entry, e.Name, e.BaseType!, e.Low, e.High);
                    return;
                case IntegerTypeSymbol i:
                    Subrange(entry, i.Name, i.BaseType!, i.Min, i.Max);
                    return;
                case CharTypeSymbol c:
                    Subrange(entry, c.Name, c.BaseType!, c.MinValue, c.MaxValue);
                    return;
                case BooleanTypeSymbol b:
                    entry["kind"] = "boolean";
                    entry["name"] = b.Name;
                    entry["size"] = b.Size;
                    entry["cStyle"] = b.IsCStyle;
                    return;
                case RealTypeSymbol r:
                    entry["kind"] = "real";
                    entry["name"] = r.Name;
                    entry["realKind"] = r.RealKind == RealKind.Single ? "single" : r.RealKind == RealKind.Double ? "double" : "currency";
                    return;
                case StringTypeSymbol s:
                    entry["kind"] = "string";
                    entry["name"] = s.Name;
                    entry["variant"] = s.Variant switch { StringVariant.Unicode => "unicode", StringVariant.Ansi => "ansi", _ => "short" };
                    entry["maxLen"] = s.MaxLength;
                    entry["codePage"] = s.CodePage;
                    return;
                case SetTypeSymbol set:
                    entry["kind"] = "set";
                    entry["name"] = set.Name;
                    entry["base"] = TypeId(set.ElementType!);
                    return;
                case ArrayTypeSymbol array:
                    entry["kind"] = "array";
                    entry["name"] = array.Name;
                    entry["index"] = new JsonArray(TypeId(array.IndexType));
                    entry["element"] = TypeId(array.ElementType);
                    return;
                case DynamicArrayTypeSymbol dynamic:
                    entry["kind"] = "dynArray";
                    entry["name"] = dynamic.Name;
                    entry["element"] = TypeId(dynamic.ElementType);
                    return;
                case PointerTypeSymbol pointer:
                    entry["kind"] = "pointer";
                    entry["name"] = pointer.Name;
                    entry["target"] = pointer.PointedType is { } target ? TypeId(target) : null;
                    entry["pointerMath"] = pointer.AllowsPointerMath;
                    return;
                case RecordTypeSymbol record:
                    entry["kind"] = "record";
                    entry["name"] = record.Name;
                    entry["size"] = record.Size;
                    entry["align"] = record.Alignment;
                    entry["packed"] = record.IsPacked;
                    entry["packing"] = record.Packing;
                    entry["anonymous"] = record.IsAnonymous;
                    entry["fields"] = new JsonArray([.. record.Fields.Select(f => (JsonNode?)new JsonObject
                    {
                        ["name"] = f.Name,
                        ["type"] = TypeId(f.Type),
                        ["offset"] = f.Offset,
                        ["visibility"] = "public",
                    })]);
                    entry["methods"] = new JsonArray();
                    entry["properties"] = new JsonArray();
                    entry["managed"] = record.IsManaged;
                    return;
                case ProceduralTypeSymbol procedural:
                    entry["kind"] = "procedural";
                    entry["name"] = procedural.Name;
                    entry["params"] = Parameters(procedural.Signature);
                    entry["result"] = procedural.Signature.ReturnType is { } result ? TypeId(result) : null;
                    entry["ofObject"] = false;
                    entry["reference"] = false;
                    return;
                default:
                    throw new NotSupportedException($".pcu description of type '{type.Name}' ({type.TypeKind})");
            }
        }

        private void Subrange(JsonObject entry, string name, TypeSymbol host, BigInteger low, BigInteger high)
        {
            entry["kind"] = "subrange";
            entry["name"] = name;
            entry["base"] = TypeId(host);
            entry["lo"] = Number(low);
            entry["hi"] = Number(high);
        }
    }
}
