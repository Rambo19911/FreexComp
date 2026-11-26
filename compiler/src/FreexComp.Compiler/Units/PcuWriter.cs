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

    /// <summary>
    /// CMP-150 rule 2: compiler version, ABI version, pinned LLVM version and the build of the compiler assembly (C-195: its
    /// MVID, which the deterministic build changes whenever the binary changes, so a rebuilt compiler never reuses stale output).
    /// </summary>
    public static string CompilerFingerprint { get; } = CompilerFingerprintFor(typeof(PcuWriter).Assembly.ManifestModule.ModuleVersionId);

    /// <summary>The fingerprint of the compiler build <paramref name="build"/> (an assembly MVID) of this version.</summary>
    internal static string CompilerFingerprintFor(Guid build) =>
        $"opc {CompilerInfo.Version}; abi {AbiInfo.Version}; llvm {CompilerInfo.LlvmPin}; build {build:N}";

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Dictionary<LoadedUnit, (JsonObject Interface, string Hash, JsonArray Bodies)> _interfaces = [];

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
            copy["genericBodies"] = Interface(unit).Bodies.DeepClone();             // C-119: as reconstructed
            return copy;
        }

        var sources = Sources(unit);
        var (hasInit, hasFinal) = context.Sections(unit);
        var root = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["unit"] = unit.Name,
            ["kind"] = unit.IsLibrary ? "library" : unit.IsProgram ? "program" : "unit",
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
            ["genericBodies"] = Interface(unit).Bodies.DeepClone(),                   // CMP-162 (C-119)
        };
        if (!unit.LinkInputs.Resources.IsEmpty)
        {
            // CMP-1325: written only when not empty, so the file of a unit without the directive is unchanged
            root["linkInputs"]!["resources"] = Strings(unit.LinkInputs.Resources);
        }

        if (!unit.FormClasses.IsEmpty)
        {
            // CMP-1381: written only when not empty, so formatVersion stays 1 and the file of a unit without forms is unchanged
            root["forms"] = Strings(unit.FormClasses);
        }

        if (unit.IsProgram)
        {
            root["unitOrder"] = Strings(context.InitOrder.Select(u => u.Name));          // CMP-163
            root["appType"] = unit.IsLibrary ? "library" : context.AppType == AppType.Gui ? "gui" : "console";
            root["unitTable"] = UnitTable();
        }

        if (unit.IsLibrary)
        {
            // CMP-1055: the exports in ordinal order (information for tools; nothing imports a library's .pcu)
            root["exports"] = new JsonArray([.. unit.Symbol!.Exports.Select(e => (JsonNode?)new JsonObject
            {
                ["name"] = e.Name,
                ["index"] = e.Ordinal,
                ["symbol"] = Emit.Mangler.RoutineText(e.RoutineSymbol),
            })]);
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
        foreach (var include in unit.Tree!.IncludedTrees.Select(t => t.Text.Path).Concat(unit.Forms.Select(f => f.Path)))     // C-114: form files too
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

    private (JsonObject Interface, string Hash, JsonArray Bodies) Interface(LoadedUnit unit)
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
        builder.AddGenericSupport(node);
        // C-119: dependents compile the generic bodies into their instances, so the hash covers them too
        var hashed = builder.Bodies.Count == 0 ? node.ToJsonString(Compact) : node.ToJsonString(Compact) + builder.Bodies.ToJsonString(Compact);
        var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashed)));
        _interfaces[unit] = (node, hash, builder.Bodies);
        return (node, hash, builder.Bodies);
    }

    /// <summary>The written interface: the hashed one with <c>decl</c> spans (indexes into <paramref name="sources"/>).</summary>
    private JsonObject WithDeclarations(LoadedUnit unit, ImmutableArray<string> sources)
    {
        var builder = new InterfaceBuilder(unit, Closure(unit), sources, context.Types);
        var hashed = Interface(unit).Interface;
        var node = new JsonObject
        {
            ["uses"] = hashed["uses"]!.DeepClone(),
            ["symbols"] = builder.Symbols(),
            ["types"] = builder.Types,
        };
        builder.AddGenericSupport(node);
        return node;
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

        /// <summary>CMP-162 (C-119): one entry per generic described, in type id order (<c>generic.body</c> indexes it).</summary>
        public JsonArray Bodies { get; } = [];

        /// <summary>
        /// C-119: <c>genericSupport</c> — the implementation-only symbols the generic bodies name (<see cref="Binding.GenericSupport"/>),
        /// as symbol entries; only when there are any (older files and units without generics keep their exact form).
        /// </summary>
        public void AddGenericSupport(JsonObject node)
        {
            if (unit.Symbol is not { } symbol)
            {
                return;
            }

            var support = new JsonArray();
            foreach (var member in FreexComp.Compiler.Binding.GenericSupport.Of(symbol))
            {
                if (Symbol(support.Count, member.Name, member) is { } entry)
                {
                    support.Add(entry);
                }
            }

            if (support.Count > 0)
            {
                node["genericSupport"] = support;
            }
        }

        /// <summary>A <c>genericBodies</c> entry for the type <paramref name="symbol"/> (its index).</summary>
        private int AddBody(int symbol, ImmutableArray<GenericBodyText.Piece> pieces)
        {
            var text = string.Concat(pieces.Select(p => p.Text));
            Bodies.Add(new JsonObject
            {
                ["symbol"] = symbol,
                ["text"] = text,
                ["sha256"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                ["pieces"] = new JsonArray([.. pieces.Select(p => (JsonNode?)new JsonObject { ["line"] = p.Line, ["col"] = p.Col, ["length"] = p.Text.Length })]),
            });
            return Bodies.Count - 1;
        }

        /// <summary>
        /// C-227, C-232: the generic methods of a plain class or record — <c>genericMethods</c> (name, visibility, and <c>member</c>:
        /// the index of the method declaration among those of the stored type declaration) and <c>genericBody</c>, a
        /// <c>genericBodies</c> entry with the type declaration and the methods' implementations. Only when there are any (other
        /// files keep their exact form).
        /// </summary>
        private void GenericMethods(JsonObject entry, ImmutableArray<GenericMethodSymbol> methods)
        {
            var declaration = methods.IsEmpty ? null : Ancestor<TypeDeclarationSyntax>(methods[0].Declaration);
            if (declaration is null)
            {
                return;
            }

            var pieces = GenericBodyText.Pieces(declaration, methods.Select(m => m.Implementation?.Declaration).OfType<RoutineDeclarationSyntax>());
            if (pieces.IsEmpty)
            {
                return;
            }

            var members = declaration.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            entry["genericMethods"] = new JsonArray([.. methods.Select(m => (JsonNode?)new JsonObject
            {
                ["name"] = m.Name,
                ["visibility"] = VisibilityName(m.Visibility),
                ["member"] = members.IndexOf(m.Declaration),
            })]);
            entry["genericBody"] = AddBody((int)entry["id"]!, pieces);
        }

        private static T? Ancestor<T>(SyntaxNode node) where T : SyntaxNode
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is T found)
                {
                    return found;
                }
            }

            return null;
        }

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
                    if (symbol is TypeSymbol)
                    {
                        Hints(entry, scope.HintsOf(name));                      // LANG-472 (C-232): a type's hints belong to its name
                    }

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
                    if (variable.ThreadVarOffset is { } threadVarOffset)
                    {
                        entry["threadVarOffset"] = threadVarOffset;                     // ABI-181: reached through U.$tls (C-165)
                    }

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

            Hints(entry, symbol.Hints);
        }

        /// <summary>
        /// LANG-472 (C-127, C-232): <c>deprecated</c>, <c>deprecatedText</c>, <c>platform</c>, <c>experimental</c> of a symbol or member
        /// entry — only those present (other files keep their exact form).
        /// </summary>
        private static JsonObject Hints(JsonObject entry, SymbolHints? hints)
        {
            if (hints is null)
            {
                return entry;
            }

            if (hints.IsDeprecated)
            {
                entry["deprecated"] = true;
            }

            if (hints.DeprecationMessage is { } deprecation)
            {
                entry["deprecatedText"] = deprecation;
            }

            if (hints.IsPlatform)
            {
                entry["platform"] = true;
            }

            if (hints.IsExperimental)
            {
                entry["experimental"] = true;
            }

            return entry;
        }

        /// <summary>Prefix of the flag that records a calling-convention directive (CMP-1011).</summary>
        internal const string CallConvFlag = "cc:";

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

            if (routine.CallingConvention != CallConvDirective.Default)
            {
                yield return CallConvFlag + routine.CallingConvention;                  // CMP-1011: only when a directive is written
            }
        }

        /// <summary>CMP-1011: <c>"callConv"</c> of a procedural type, only when a directive is written.</summary>
        private static void CallConv(JsonObject entry, RoutineSymbol signature)
        {
            if (signature.CallingConvention != CallConvDirective.Default)
            {
                entry["callConv"] = signature.CallingConvention.ToString();
            }
        }

        private JsonArray Parameters(RoutineSymbol routine) =>
            new([.. routine.Parameters.Select(p =>
            {
                var parameter = new JsonObject
                {
                    ["name"] = p.Name,
                    ["type"] = TypeId(p.Type is OpenArrayTypeSymbol open ? open.ElementType : p.Type),     // an open array: its element type (C-106)
                    ["mode"] = p.Mode switch { ParameterMode.Const => "const", ParameterMode.Var => "var", ParameterMode.Out => "out", _ => "value" },
                    ["openArray"] = p.IsOpenArray,
                };
                if (p.Type is OpenArrayTypeSymbol { IsArrayOfConst: true })
                {
                    parameter["arrayOfConst"] = true;                           // C-107: element TVarRec
                }

                if (p.IsRef)
                {
                    parameter["ref"] = true;                                    // C-231: [Ref] const, passed by address
                }
                if (p.DefaultValue is { } defaultValue)
                {
                    parameter["default"] = Value(defaultValue, p.Type);         // present only with a default (nil = null)
                }

                return (JsonNode?)parameter;
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
            : ReferenceEquals(type, _types.Untyped) ? "untyped"                         // C-202: an untyped parameter
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

            if (_owners.TryGetValue(type, out var owner))
            {
                return owner;
            }

            // CMP-1392 (C-344): a nested type of another unit's interface type is named by its path, TOuter.TInner
            if (TypeMembers.EnclosingTypes(type).FirstOrDefault() is { } outer && !outer.IsGenericInstance
                && ReferenceEquals(TypeMembers.NestedType(outer, type.Name), type) && Owner(outer) is { } outerOwner)
            {
                return (outerOwner.Unit, outerOwner.Name + "." + type.Name);
            }

            return null;
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
            if (TypeMembers.EnclosingTypes(type).FirstOrDefault() is { } outer && TypeMembers.EnclosingInstance(type) is null)
            {
                entry["outer"] = TypeId(outer);                                    // CMP-1392 (C-344): created inside its enclosing type
            }
            else if (type is PointerTypeSymbol { DeclaredIn: ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol } pointer
                     && TypeMembers.EnclosingInstance((TypeSymbol)pointer.DeclaredIn) is null)
            {
                // C-367: a named pointer has no containing symbol; read back in its declaring type, it keeps its link name (CMP-361)
                entry["outer"] = TypeId((TypeSymbol)pointer.DeclaredIn);
            }
            else if (type.ContainingSymbol is HelperTypeSymbol helper && TypeMembers.NestedMembers(helper).Contains(type))
            {
                entry["outer"] = TypeId(helper);                                   // C-363: a helper's nested type keeps its section's visibility
            }

            var constants = TypeMembers.NestedMembers(type).OfType<ConstantSymbol>().Where(c => c is { IsTyped: false, Value: not null }).ToList();
            if (!type.IsGenericInstance && (TypeMembers.NestedMembers(type).OfType<TypeSymbol>().Any() || !TypeMembers.NestedAliases(type).IsEmpty || constants.Count > 0))
            {
                // CMP-1392 (C-344): the nested types (and type aliases) of a class or record, with their section's visibility;
                // C-361: its nested (true) constants with their value and section's visibility too
                entry["nested"] = new JsonArray([.. TypeMembers.NestedMembers(type).OfType<TypeSymbol>().Select(t => (JsonNode?)new JsonObject
                {
                    ["name"] = t.Name,
                    ["type"] = TypeId(t),
                    ["visibility"] = VisibilityName(t.NestedVisibility),
                }), .. constants.Select(c => (JsonNode?)new JsonObject
                {
                    ["name"] = c.Name,
                    ["type"] = TypeId(c.Type),
                    ["const"] = true,
                    ["value"] = Value(c.Value, c.Type),
                    ["visibility"] = VisibilityName(c.Visibility),
                }), .. TypeMembers.NestedAliases(type).Select(a => (JsonNode?)new JsonObject
                {
                    ["name"] = a.Name,
                    ["type"] = TypeId(a.Target),
                    ["alias"] = true,
                    ["visibility"] = VisibilityName(a.Visibility),            // C-359
                })]);
            }

            return id;
        }

        private void Describe(JsonObject entry, TypeSymbol type)
        {
            if (type is { GenericDefinition: GenericTypeSymbol definition })
            {
                // C-119: an instance is its generic and arguments; the reader instantiates it (LANG-710: the same type everywhere)
                entry["kind"] = "instance";
                entry["generic"] = TypeId(definition);
                entry["arguments"] = new JsonArray([.. type.TypeArguments.Select(a => (JsonNode?)TypeId(a))]);
                return;
            }

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
                    // C-122 (LANG-730), imported since C-219
                    entry["methods"] = StaticMethods(record.Methods);
                    GenericMethods(entry, record.GenericMethods);                      // C-232: as for a plain class (C-227)
                    entry["properties"] = MemberProperties(record.Properties);
                    if (!record.ClassVariables.IsEmpty)
                    {
                        entry["classVars"] = ClassVars(record.ClassVariables);
                    }
                    entry["managed"] = record.IsManaged;
                    if (record.HasVariantPart)
                    {
                        entry["variant"] = true;                                // C-108: overlapping fields, byte storage
                    }

                    return;
                case HelperTypeSymbol helper:
                    // C-124 (LANG-750), imported since C-219
                    entry["kind"] = "helper";
                    entry["name"] = helper.Name;
                    entry["classHelper"] = helper.IsClassHelper;
                    entry["extends"] = TypeId(helper.ExtendedType);
                    entry["parent"] = helper.Parent is { } parentHelper ? TypeId(parentHelper) : null;
                    entry["methods"] = StaticMethods(helper.Methods);
                    entry["properties"] = MemberProperties(helper.Properties);
                    if (!helper.ClassVariables.IsEmpty)
                    {
                        entry["classVars"] = ClassVars(helper.ClassVariables);
                    }

                    return;
                case ProceduralTypeSymbol procedural:
                    entry["kind"] = "procedural";
                    entry["name"] = procedural.Name;
                    entry["params"] = Parameters(procedural.Signature);
                    entry["result"] = procedural.Signature.ReturnType is { } result ? TypeId(result) : null;
                    entry["ofObject"] = false;
                    entry["reference"] = false;
                    CallConv(entry, procedural.Signature);
                    return;
                case MethodPointerTypeSymbol methodPointer:
                    entry["kind"] = "procedural";                               // C-103: architecture.md §6 ofObject
                    entry["name"] = methodPointer.Name;
                    entry["params"] = Parameters(methodPointer.Signature);
                    entry["result"] = methodPointer.Signature.ReturnType is { } methodResult ? TypeId(methodResult) : null;
                    entry["ofObject"] = true;
                    entry["reference"] = false;
                    CallConv(entry, methodPointer.Signature);
                    return;
                case ClassTypeSymbol @class:
                    // architecture.md §6 `class` (C-080); interfaces, properties and RTTI arrive with M2.5
                    entry["kind"] = "class";
                    entry["name"] = @class.Name;
                    entry["parent"] = @class.Parent is { } parent ? TypeId(parent) : null;
                    // C-098: listed interfaces with their hidden field offsets (architecture.md §6 names only the key)
                    entry["interfaces"] = new JsonArray([.. @class.Interfaces.Select(i => (JsonNode?)new JsonObject
                    {
                        ["type"] = TypeId(i.Interface),
                        ["offset"] = i.Offset,
                    })]);
                    entry["instanceSize"] = @class.InstanceSize;
                    entry["flags"] = Strings([.. @class.IsAbstract ? ["abstract"] : Array.Empty<string>(), .. @class.IsSealed ? ["sealed"] : Array.Empty<string>(), .. @class.IsTypeInfo ? ["typeinfo"] : Array.Empty<string>()]);
                    entry["fields"] = new JsonArray([.. @class.Fields.Select(f => (JsonNode?)new JsonObject
                    {
                        ["name"] = f.Name,
                        ["type"] = TypeId(f.Type),
                        ["offset"] = f.Offset,
                        ["visibility"] = VisibilityName(f.Visibility),
                        ["classVar"] = false,
                    }), .. @class.ClassVariables.Select(v => (JsonNode?)new JsonObject
                    {
                        ["name"] = v.Name,
                        ["type"] = TypeId(v.Type),
                        ["offset"] = 0,
                        ["visibility"] = VisibilityName(v.Visibility),
                        ["classVar"] = true,
                        ["linkName"] = Emit.Mangler.GlobalVariable(v).Text,          // CMP-370 (C-086)
                    })]);
                    entry["methods"] = new JsonArray([.. @class.Methods.Select(m => (JsonNode?)Hints(new JsonObject
                    {
                        ["name"] = m.Name,
                        ["linkName"] = m.LinkName,
                        ["params"] = Parameters(m),
                        ["result"] = m.ReturnType is { } result && m.MethodKind != MethodKind.Constructor ? TypeId(result) : null,
                        // C-189: "class" marks a class method whose binding names its dispatch (virtual, override…)
                        ["flags"] = Strings([.. Flags(m), .. m.IsClassMethod ? ["class"] : Array.Empty<string>()]),
                        ["visibility"] = VisibilityName(m.Visibility),
                        ["binding"] = Binding(m),
                        ["slot"] = m.VmtSlot,
                        ["msgId"] = m.MessageId,                                       // C-113
                    }, m.Hints))]);
                    GenericMethods(entry, @class.GenericMethods);                      // C-227
                    entry["properties"] = new JsonArray([.. @class.Properties.Select(p => (JsonNode?)new JsonObject
                    {
                        ["name"] = p.Name,
                        ["type"] = TypeId(p.Type),
                        ["visibility"] = VisibilityName(p.Visibility),
                        ["read"] = Accessor(p.ReadPath, p.ReadMethod),
                        ["write"] = Accessor(p.WritePath, p.WriteMethod),
                        ["stored"] = null,
                        ["default"] = null,
                        ["index"] = p.Index,
                        ["params"] = new JsonArray([.. p.Parameters.Select(q => (JsonNode?)new JsonObject { ["name"] = q.Name, ["type"] = TypeId(q.Type) })]),
                        ["isDefault"] = p.IsDefault,
                    })]);                                                                   // C-085
                    entry["vmtLinkName"] = Emit.Mangler.Vmt(@class).Text;
                    entry["vmtSlotCount"] = @class.VmtSlotCount;
                    return;
                case InterfaceTypeSymbol intf:
                    // architecture.md §6 `interface` (C-098); methods carry their IMT slot, no link name (they have no body)
                    entry["kind"] = "interface";
                    entry["name"] = intf.Name;
                    entry["parent"] = intf.Parent is { } parentInterface ? TypeId(parentInterface) : null;
                    entry["guid"] = intf.Guid is { } guid ? new System.Guid([.. guid]).ToString("B").ToUpperInvariant() : null;
                    if (intf.IsMethodReference)
                    {
                        entry["methodReference"] = true;                        // LANG-720 (C-120)
                    }

                    if (intf.IsDispatchInterface)
                    {
                        entry["dispatch"] = true;                               // com-interfaces.md CMP-1211 (C-303)
                    }

                    // CMP-1211: `dispid` of a member, `readonly` / `writeonly` of a dispatch property — only where given (other
                    // files keep their exact form)
                    entry["methods"] = new JsonArray([.. intf.Methods.Select(m =>
                    {
                        var method = new JsonObject
                        {
                            ["name"] = m.Name,
                            ["params"] = Parameters(m),
                            ["result"] = m.ReturnType is { } result ? TypeId(result) : null,
                            ["flags"] = Strings([.. m.IsOverload ? new[] { "overload" } : Array.Empty<string>(),
                                .. m.CallingConvention != CallConvDirective.Default ? new[] { CallConvFlag + m.CallingConvention } : Array.Empty<string>()]),
                            ["slot"] = m.VmtSlot,
                        };
                        if (m.DispId is { } dispId)
                        {
                            method["dispid"] = dispId;
                        }

                        return (JsonNode?)Hints(method, m.Hints);
                    })]);
                    // C-205: accessors are interface methods, named by their IMT slot's method name (they have no link name); a
                    // dispatch property has none (the reader makes its hidden get and put members again)
                    entry["properties"] = new JsonArray([.. intf.Properties.Select(p =>
                    {
                        var property = new JsonObject
                        {
                            ["name"] = p.Name,
                            ["type"] = TypeId(p.Type),
                            ["read"] = intf.IsDispatchInterface ? null : p.ReadMethod?.VmtSlot,
                            ["write"] = intf.IsDispatchInterface ? null : p.WriteMethod?.VmtSlot,
                            ["index"] = p.Index,
                            ["params"] = new JsonArray([.. p.Parameters.Select(q => (JsonNode?)new JsonObject { ["name"] = q.Name, ["type"] = TypeId(q.Type) })]),
                            ["isDefault"] = p.IsDefault,
                        };
                        if (p.DispId is { } dispId)
                        {
                            property["dispid"] = dispId;
                        }

                        if (p.IsReadOnly || p.IsWriteOnly)
                        {
                            property["access"] = p.IsReadOnly ? "readonly" : "writeonly";
                        }

                        return (JsonNode?)property;
                    })]);
                    return;
                case GenericTypeSymbol generic:
                {
                    // architecture.md §6 `generic` (C-116); CMP-162 genericBodies (C-119): the pieces, re-parsed by the reader
                    entry["kind"] = "generic";
                    entry["name"] = generic.Name;
                    entry["typeParams"] = new JsonArray([.. generic.ParameterNames.Select(n => (JsonNode?)new JsonObject { ["name"] = n, ["constraints"] = new JsonArray() })]);
                    var pieces = GenericBodyText.Pieces(generic);
                    if (pieces.IsEmpty)
                    {
                        entry["body"] = null;
                        return;
                    }

                    entry["body"] = AddBody((int)entry["id"]!, pieces);
                    return;
                }
                case ClassRefTypeSymbol reference:
                    entry["kind"] = "classRef";                                     // C-086
                    entry["name"] = reference.Name;
                    entry["target"] = TypeId(reference.Class);
                    return;
                default:
                    throw new NotSupportedException($".pcu description of type '{type.Name}' ({type.TypeKind})");
            }
        }

        /// <summary>
        /// C-122/C-124/C-219: record and helper methods (never virtual): flags add <c>class</c> (static), <c>constructor</c> (a record
        /// constructor) and <c>operator</c> (a LANG-731 <c>class operator</c>, named by its operator name).
        /// </summary>
        private JsonArray StaticMethods(ImmutableArray<MethodSymbol> methods) =>
            new([.. methods.Select(m => (JsonNode?)Hints(new JsonObject
            {
                ["name"] = m.Name,
                ["linkName"] = m.LinkName,
                ["params"] = Parameters(m),
                ["result"] = m.ReturnType is { } result ? TypeId(result) : null,
                ["flags"] = Strings([.. Flags(m), .. m.IsClassMethod ? ["class"] : Array.Empty<string>(),
                    .. m.IsRecordConstructor ? ["constructor"] : Array.Empty<string>(), .. m.OperatorName is not null ? ["operator"] : Array.Empty<string>()]),
                ["visibility"] = VisibilityName(m.Visibility),
            }, m.Hints))]);

        private JsonArray MemberProperties(ImmutableArray<PropertySymbol> properties) =>
            new([.. properties.Select(p => (JsonNode?)new JsonObject
            {
                ["name"] = p.Name,
                ["type"] = TypeId(p.Type),
                ["visibility"] = VisibilityName(p.Visibility),
                ["read"] = Accessor(p.ReadPath, p.ReadMethod),
                ["write"] = Accessor(p.WritePath, p.WriteMethod),
                ["index"] = p.Index,
                ["params"] = new JsonArray([.. p.Parameters.Select(q => (JsonNode?)new JsonObject { ["name"] = q.Name, ["type"] = TypeId(q.Type) })]),
                ["isDefault"] = p.IsDefault,
            })]);

        private JsonArray ClassVars(ImmutableArray<VariableSymbol> variables) =>
            new([.. variables.Select(v => (JsonNode?)new JsonObject
            {
                ["name"] = v.Name,
                ["type"] = TypeId(v.Type),
                ["visibility"] = VisibilityName(v.Visibility),
                ["linkName"] = Emit.Mangler.GlobalVariable(v).Text,
            })]);

        /// <summary>A property accessor: the field path (<c>FRect.Left</c>) or the method's link name; null without one.</summary>
        private static string? Accessor(ImmutableArray<Symbol> path, MethodSymbol? method) =>
            method is not null ? method.LinkName : path.IsEmpty ? null : string.Join(".", path.Select(s => s.Name));

        private static string VisibilityName(Visibility visibility) => visibility switch
        {
            Visibility.StrictPrivate => "strict private",
            Visibility.Private => "private",
            Visibility.StrictProtected => "strict protected",
            Visibility.Protected => "protected",
            Visibility.Published => "published",
            _ => "public",
        };

        /// <summary>The §6 <c>binding</c> of a method: its kind, else how it is dispatched.</summary>
        private static string Binding(MethodSymbol method) => method switch
        {
            { MethodKind: MethodKind.Constructor } => "constructor",
            { MethodKind: MethodKind.Destructor } => "destructor",
            { MethodKind: MethodKind.ClassStatic } => "classStatic",
            { IsAbstract: true } => "abstract",
            { IsOverride: true, IsFinal: true } or { IsVirtual: true, IsFinal: true } => "final",
            { IsOverride: true } => "override",
            { IsVirtual: true } => "virtual",
            { MethodKind: MethodKind.ClassMethod } => "class",
            { MessageId: not null } => "message",
            _ => "static",
        };

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
