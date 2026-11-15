using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Units;

/// <summary>A <c>.pcu</c> that does not describe what it should (unknown kinds, dangling ids or references).</summary>
internal sealed class PcuFormatException(string message) : Exception(message);

/// <summary>The interface of a unit read from its <c>.pcu</c>: the unit symbol and its interface scope.</summary>
internal sealed record PcuInterface(UnitSymbol Unit, Scope InterfaceScope);

/// <summary>
/// C-074: reads a <c>.pcu</c> (architecture.md §6) and rebuilds the unit's interface symbols — constants, typed constants,
/// variables, type names, routines, enumeration elements — and the types they use, so importers bind and link against the unit
/// without its source. Unknown keys are ignored (CMP-160); a different <c>formatVersion</c> makes the file unusable, never an error.
/// </summary>
internal static class PcuReader
{
    /// <summary>The parsed file, or null when it is not JSON or has another <c>formatVersion</c> (CMP-160).</summary>
    public static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject root && root["formatVersion"] is JsonValue version
                   && version.TryGetValue<int>(out var number) && number == PcuWriter.FormatVersion
                ? root
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>CMP-150 rules 2 and 3: the compiler fingerprint and (when given) the options fingerprint equal the current ones.</summary>
    public static bool HasFingerprints(JsonObject pcu, string compilerFingerprint, string? optionsFingerprint = null) =>
        Text(pcu, "compilerFingerprint") == compilerFingerprint
        && (optionsFingerprint is null || Text(pcu, "optionsFingerprint") == optionsFingerprint);

    /// <summary>The recorded <c>linkInputs</c> (C-077).</summary>
    public static UnitLinkInputs LinkInputs(JsonObject pcu)
    {
        var inputs = Node(pcu, "linkInputs");
        return new UnitLinkInputs(Strings(inputs, "dlls"), Strings(inputs, "objects"), Strings(inputs, "defs"));

        static ImmutableArray<string> Strings(JsonNode owner, string key) => [.. Array(owner, key).Select(v => v!.GetValue<string>())];
    }

    /// <summary>The unit's dependencies as recorded: canonical name and section (<c>interface</c> | <c>implementation</c>).</summary>
    public static ImmutableArray<(string Unit, string Section)> Dependencies(JsonObject pcu) =>
        [.. Array(pcu, "dependencies").Select(d => (Text(d, "unit"), Text(d, "section")))];

    /// <summary>
    /// Rebuilds the interface. <paramref name="parent"/> is the scope the interface scope hangs below (the built-ins for System);
    /// <paramref name="unitInterface"/> gives the interface scope of an already loaded unit by canonical name (for <c>ref</c> types).
    /// </summary>
    /// <exception cref="PcuFormatException">The file is inconsistent.</exception>
    public static PcuInterface Import(JsonObject pcu, BuiltInTypes types, Scope parent, Func<string, Scope?> unitInterface) =>
        new Importer(pcu, types, unitInterface).Run(parent);

    private sealed class Importer(JsonObject pcu, BuiltInTypes types, Func<string, Scope?> unitInterface)
    {
        private readonly UnitSymbol _unit = new(Text(pcu, "unit"), isProgram: false, []);
        private readonly JsonArray _types = Array(Node(pcu, "interface"), "types");
        private readonly ImmutableArray<string> _sources = [.. Array(pcu, "sources").Select(s => Text(s, "path"))];
        private readonly Dictionary<int, TypeSymbol> _byId = [];

        public PcuInterface Run(Scope parent)
        {
            var scope = new Scope(ScopeKind.Unit, parent, _unit);
            var members = new List<Symbol>();
            foreach (var entry in Array(Node(pcu, "interface"), "symbols"))
            {
                var name = Text(entry, "name");
                var symbol = Symbol(entry!, name);
                if (symbol is RoutineSymbol routine && scope.LookupLocal(name) is RoutineSymbol)
                {
                    scope.AddOverload(routine);
                }
                else if (!scope.TryDeclare(name, symbol, out _))
                {
                    throw new PcuFormatException($"'{name}' is declared twice");
                }

                if (!members.Contains(symbol))
                {
                    members.Add(symbol);
                }
            }

            _unit.InterfaceMembers = [.. members];
            return new PcuInterface(_unit, scope);
        }

        private Symbol Symbol(JsonNode entry, string name)
        {
            var locations = Declaration(entry);
            var kind = Text(entry, "kind");
            switch (kind)
            {
                case "const" when entry["typed"]?.GetValue<bool>() == true:
                {
                    var type = Type(entry, "type");
                    var storage = new VariableSymbol(name, type, isGlobal: true, _unit, locations) { IsReadOnly = true };
                    storage.ExplicitLinkName = Explicit(Text(entry, "linkName"), Emit.Mangler.GlobalVariable(storage).Text);
                    return new ConstantSymbol(name, type, value: null, isTyped: true, _unit, locations) { Storage = storage, Documentation = Doc(entry) };
                }

                case "const":
                {
                    var type = Type(entry, "type");
                    return new ConstantSymbol(name, type, Value(entry["value"], type), isTyped: false, _unit, locations) { Documentation = Doc(entry) };
                }

                case "var":
                {
                    var variable = new VariableSymbol(name, Type(entry, "type"), isGlobal: true, _unit, locations)
                    {
                        Documentation = Doc(entry),
                        ThreadVarOffset = entry["threadVarOffset"]?.GetValue<long>(),          // C-165
                    };
                    variable.ExplicitLinkName = Explicit(Text(entry, "linkName"), Emit.Mangler.GlobalVariable(variable).Text);
                    return variable;
                }

                case "typeAlias":
                {
                    var type = Type(entry, "type");
                    if (entry["distinct"]?.GetValue<bool>() == true && type.Locations.IsEmpty && !locations.IsEmpty)
                    {
                        type.AddLocation(locations[0]);
                    }

                    return type;
                }

                case "enumMember":
                {
                    var type = Type(entry, "type") as EnumTypeSymbol ?? throw new PcuFormatException($"'{name}' is not of an enumeration type");
                    var member = type.EnumMembers.FirstOrDefault(m => m.Name == name) ?? throw new PcuFormatException($"'{name}' is not an element of '{type.Name}'");
                    foreach (var location in locations)
                    {
                        member.AddLocation(location);
                    }

                    return member;
                }

                case "routine":
                    return Routine(entry, name, locations);
                default:
                    throw new PcuFormatException($"unknown symbol kind '{kind}'");
            }
        }

        private RoutineSymbol Routine(JsonNode entry, string name, ImmutableArray<Location> locations)
        {
            var routine = new RoutineSymbol(name, _unit, locations) { Documentation = Doc(entry) };
            routine.Parameters = Parameters(entry, routine);
            routine.ReturnType = entry["result"] is null ? null : Type(entry, "result");
            var flags = Array(entry, "flags").Select(f => f!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            routine.IsOverload = flags.Contains("overload");
            routine.IsInline = flags.Contains("inline");
            routine.IsExternal = flags.Contains("external");
            routine.IsNoReturn = flags.Contains("noreturn");
            routine.LibraryName = entry["dll"] is JsonValue dll ? dll.GetValue<string>() : null;
            routine.ExplicitLinkName = Explicit(Text(entry, "linkName"), Emit.Mangler.RoutineText(routine));
            return routine;
        }

        private ImmutableArray<ParameterSymbol> Parameters(JsonNode owner, RoutineSymbol routine) =>
            [.. Array(owner, "params").Select((p, i) => new ParameterSymbol(Text(p, "name"), ParameterType(p!), Text(p, "mode") switch
            {
                "const" => ParameterMode.Const,
                "var" => ParameterMode.Var,
                "out" => ParameterMode.Out,
                "value" => ParameterMode.Value,
                var other => throw new PcuFormatException($"unknown parameter mode '{other}'"),
            }, i, routine, [], types.Int64)
            {
                DefaultValue = p is JsonObject o && o.TryGetPropertyValue("default", out var value) ? Value(value, Type(p, "type")) : null,
            })];

        /// <summary>A parameter's type: an open array (C-106) records its element type with <c>"openArray": true</c>.</summary>
        private TypeSymbol ParameterType(JsonNode parameter) =>
            parameter["openArray"] is JsonValue open && open.GetValue<bool>()
                ? types.OpenArrayOf(Type(parameter, "type"), parameter["arrayOfConst"] is JsonValue c && c.GetValue<bool>())
                : Type(parameter, "type");

        /// <summary>The recorded link name when it is not the one derived from the declaration (an import name, a <c>public name</c>).</summary>
        private static string? Explicit(string recorded, string derived) => recorded == derived ? null : recorded;

        private static string? Doc(JsonNode entry) => entry["doc"] is JsonValue doc ? doc.GetValue<string>() : null;

        private ImmutableArray<Location> Declaration(JsonNode entry)
        {
            if (entry["decl"] is not JsonObject decl)
            {
                return [];
            }

            var file = decl["file"]!.GetValue<int>();
            if (file < 0 || file >= _sources.Length)
            {
                throw new PcuFormatException($"declaration file index {file} is out of range");
            }

            var path = _sources[file];
            return [new Location(path, new TextSpan(decl["start"]!.GetValue<int>(), decl["length"]!.GetValue<int>()), Location.ForFile(path).LineSpan)];
        }

        private ConstantValue? Value(JsonNode? value, TypeSymbol type) => value switch
        {
            null => new ConstantValue(null, type),
            JsonArray elements when type is SetTypeSymbol =>
                new ConstantValue(elements.Aggregate(BigInteger.Zero, (mask, e) => mask | (BigInteger.One << e!.GetValue<int>())), type),
            JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => new ConstantValue(v.GetValue<bool>(), type),
            JsonValue v when v.GetValueKind() == JsonValueKind.String && type is RealTypeSymbol =>
                new ConstantValue(double.Parse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture), type),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => new ConstantValue(v.GetValue<string>(), type),
            JsonValue v when v.GetValueKind() == JsonValueKind.Number =>
                new ConstantValue(BigInteger.Parse(v.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), type),
            _ => throw new PcuFormatException("unsupported constant value " + value.ToJsonString()),
        };

        // -------------------------------------------------------------- types

        private TypeSymbol Type(JsonNode owner, string key) =>
            owner[key] is JsonValue id && id.TryGetValue<int>(out var number) ? TypeById(number) : throw new PcuFormatException($"'{key}' is not a type id");

        private TypeSymbol TypeById(int id)
        {
            if (_byId.TryGetValue(id, out var known))
            {
                return known;
            }

            if (id < 0 || id >= _types.Count || _types[id] is not JsonObject entry)
            {
                throw new PcuFormatException($"type id {id} is out of range");
            }

            var name = entry["name"] is JsonValue n ? n.GetValue<string>() : "";
            var kind = Text(entry, "kind");
            switch (kind)
            {
                case "builtin":
                    return _byId[id] = BuiltIn(name);
                case "ref":
                {
                    var unit = Text(entry, "unit");
                    return _byId[id] = unitInterface(unit)?.LookupLocal(name) as TypeSymbol
                        ?? throw new PcuFormatException($"type '{unit}.{name}' not found");
                }

                case "enum":
                {
                    var type = new EnumTypeSymbol(name, entry["size"]!.GetValue<int>(), entry["scoped"]!.GetValue<bool>(), _unit, []);
                    type.SetMembers([.. Array(entry, "members").Select(m => new EnumMemberSymbol(Text(m, "name"), type, m!["ordinal"]!.GetValue<long>(), []))]);
                    return _byId[id] = type;
                }

                case "subrange":
                {
                    var host = Type(entry, "base");
                    var low = BigInteger.Parse(entry["lo"]!.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    var high = BigInteger.Parse(entry["hi"]!.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    return _byId[id] = host switch
                    {
                        IntegerTypeSymbol integer => new IntegerTypeSymbol(name, integer, low, high, _unit, []),
                        CharTypeSymbol c => new CharTypeSymbol(name, c, (int)low, (int)high, _unit, []),
                        EnumTypeSymbol e => new EnumTypeSymbol(name, e, low, high, _unit, []),
                        _ => throw new PcuFormatException($"subrange '{name}' of '{host.Name}'"),
                    };
                }

                case "boolean":
                    return _byId[id] = new BooleanTypeSymbol(name, entry["size"]!.GetValue<int>(), entry["cStyle"]!.GetValue<bool>());
                case "real":
                    return _byId[id] = new RealTypeSymbol(name, Text(entry, "realKind") switch
                    {
                        "single" => RealKind.Single,
                        "double" => RealKind.Double,
                        "currency" => RealKind.Currency,
                        var other => throw new PcuFormatException($"unknown real kind '{other}'"),
                    });
                case "string":
                    return _byId[id] = new StringTypeSymbol(name, Text(entry, "variant") switch
                    {
                        "unicode" => StringVariant.Unicode,
                        "ansi" => StringVariant.Ansi,
                        "short" => StringVariant.Short,
                        var other => throw new PcuFormatException($"unknown string variant '{other}'"),
                    }, entry["codePage"]!.GetValue<int>(), entry["maxLen"]!.GetValue<int>());
                case "set":
                    return _byId[id] = new SetTypeSymbol(name, Type(entry, "base"), _unit, []);
                case "array":
                {
                    var index = Array(entry, "index").Single() is JsonValue i ? TypeById(i.GetValue<int>()) : throw new PcuFormatException("array index");
                    return _byId[id] = new ArrayTypeSymbol(name, index, Type(entry, "element"), _unit, []);
                }

                case "dynArray":
                    return _byId[id] = new DynamicArrayTypeSymbol(name, Type(entry, "element"), _unit, []);
                case "pointer":
                {
                    var pointer = new PointerTypeSymbol(name, pointedType: null, entry["pointerMath"]!.GetValue<bool>());
                    _byId[id] = pointer;                                    // before the target: ^T may point back (LANG-208)
                    pointer.PointedType = entry["target"] is null ? null : Type(entry, "target");
                    return pointer;
                }

                case "record":
                {
                    var record = new RecordTypeSymbol(name, entry["size"]!.GetValue<long>(), entry["align"]!.GetValue<int>(), entry["packed"]!.GetValue<bool>(),
                        entry["packing"]!.GetValue<int>(), entry["anonymous"]!.GetValue<bool>(), _unit, [])
                    {
                        HasVariantPart = entry["variant"] is JsonValue variant && variant.GetValue<bool>(),
                    };
                    _byId[id] = record;
                    record.Fields = [.. Array(entry, "fields").Select(f => new FieldSymbol(Text(f, "name"), Type(f!, "type"), f!["offset"]!.GetValue<long>(), record, []))];
                    record.ClassVariables = ClassVars(record, entry);                                       // C-219
                    record.Methods = [.. (entry["methods"] as JsonArray ?? []).Select(m => StaticMethod(record, m!))];
                    record.Properties = [.. (entry["properties"] as JsonArray ?? []).Select(p => Property(record, p!))];
                    return record;
                }

                case "helper":
                {
                    var helper = new HelperTypeSymbol(name, entry["classHelper"]!.GetValue<bool>(), Type(entry, "extends"), _unit, []);   // C-219
                    _byId[id] = helper;
                    helper.Parent = entry["parent"] is null ? null
                        : Type(entry, "parent") as HelperTypeSymbol ?? throw new PcuFormatException($"the ancestor of '{name}' is not a helper");
                    helper.ClassVariables = ClassVars(helper, entry);
                    helper.Methods = [.. Array(entry, "methods").Select(m => StaticMethod(helper, m!))];
                    helper.Properties = [.. (entry["properties"] as JsonArray ?? []).Select(p => Property(helper, p!))];
                    types.HasHelpers = true;
                    return helper;
                }

                case "procedural" when entry["ofObject"] is JsonValue ofObject && ofObject.GetValue<bool>():
                {
                    var methodPointer = new MethodPointerTypeSymbol(name, _unit, []);                    // C-103
                    _byId[id] = methodPointer;
                    methodPointer.Signature.Parameters = Parameters(entry, methodPointer.Signature);
                    methodPointer.Signature.ReturnType = entry["result"] is null ? null : Type(entry, "result");
                    return methodPointer;
                }

                case "procedural":
                {
                    var procedural = new ProceduralTypeSymbol(name, _unit, []);
                    _byId[id] = procedural;
                    procedural.Signature.Parameters = Parameters(entry, procedural.Signature);
                    procedural.Signature.ReturnType = entry["result"] is null ? null : Type(entry, "result");
                    return procedural;
                }

                case "class":
                    return Class(id, entry, name);
                case "interface":
                    return Interface(id, entry, name);
                case "generic":
                    // C-116: without its body text (C-119) an imported generic cannot be instantiated (E2900 at the use)
                    return _byId[id] = new GenericTypeSymbol(name, [.. Array(entry, "typeParams").Select(p => Text(p, "name"))], null, null, 0, _unit, []);
                case "classRef":
                {
                    var target = Type(entry, "target") as ClassTypeSymbol ?? throw new PcuFormatException($"'{name}' does not refer to a class");
                    return _byId[id] = name == target.Reference.Name ? target.Reference : new ClassRefTypeSymbol(name, target, _unit, []);
                }

                default:
                    throw new PcuFormatException($"unknown type kind '{kind}'");
            }
        }

        /// <summary>
        /// C-189: a class of architecture.md §6 — parent, layout, fields, class vars, methods (with <c>Self</c> and the
        /// constructor/destructor flag as the binder creates them), VMT slots and properties.
        /// </summary>
        private ClassTypeSymbol Class(int id, JsonObject entry, string name)
        {
            var type = new ClassTypeSymbol(name, _unit, []) { IsForward = false };
            _byId[id] = type;                                                   // before the members: they may name the class
            if (string.Equals(_unit.Name, "System", StringComparison.OrdinalIgnoreCase) && string.Equals(name, "TObject", StringComparison.OrdinalIgnoreCase))
            {
                types.TObject = type;                                           // the root class (LANG-600)
            }

            type.Parent = entry["parent"] is null ? null
                : Type(entry, "parent") as ClassTypeSymbol ?? throw new PcuFormatException($"the parent of '{name}' is not a class");
            var flags = Array(entry, "flags").Select(f => f!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            type.IsAbstract = flags.Contains("abstract");
            type.IsSealed = flags.Contains("sealed");
        type.IsTypeInfo = flags.Contains("typeinfo");                                    // ABI-142 (C-112)
            type.InstanceSize = entry["instanceSize"]!.GetValue<long>();
            var fields = new List<FieldSymbol>();
            var classVariables = new List<VariableSymbol>();
            foreach (var field in Array(entry, "fields"))
            {
                var fieldName = Text(field, "name");
                var visibility = VisibilityOf(Text(field, "visibility"));
                if (field!["classVar"]?.GetValue<bool>() == true)
                {
                    var variable = new VariableSymbol(fieldName, Type(field, "type"), isGlobal: true, type, [], visibility: visibility);
                    variable.ExplicitLinkName = Explicit(Text(field, "linkName"), Emit.Mangler.GlobalVariable(variable).Text);
                    classVariables.Add(variable);
                }
                else
                {
                    fields.Add(new FieldSymbol(fieldName, Type(field, "type"), field["offset"]!.GetValue<long>(), type, [], visibility));
                }
            }

            type.Fields = [.. fields];
            type.ClassVariables = [.. classVariables];
            var parentSlots = type.Parent?.VirtualSlots ?? [];
            var methods = Array(entry, "methods").Select(m => Method(type, m!, parentSlots)).ToImmutableArray();
            type.Methods = methods;
            var slots = new MethodSymbol?[entry["vmtSlotCount"]!.GetValue<int>()];
            parentSlots.CopyTo(slots!);
            foreach (var method in methods.Where(m => m.VmtSlot >= 0))
            {
                slots[method.VmtSlot] = method;
            }

            type.VirtualSlots = [.. slots.Select(s => s ?? throw new PcuFormatException($"VMT slot of '{name}' without a method"))];
            if (Text(entry, "vmtLinkName") != Emit.Mangler.Vmt(type).Text)
            {
                throw new PcuFormatException($"VMT link name of '{name}'");
            }

            type.Properties = [.. Array(entry, "properties").Select(p => Property(type, p!))];
            // C-205: the listed interfaces with their hidden field offsets; the implementing methods matter only to the declaring
            // unit (its IMTs and thunks), so an imported class carries none
            type.Interfaces = [.. Array(entry, "interfaces").Select(i => new ImplementedInterface(
                Type(i!, "type") as InterfaceTypeSymbol ?? throw new PcuFormatException($"an interface of '{name}' is not an interface"),
                i!["offset"]!.GetValue<long>(), []))];
            return type;
        }

        /// <summary>C-205: an interface of architecture.md §6 — parent, GUID, methods with their IMT slots, properties.</summary>
        private InterfaceTypeSymbol Interface(int id, JsonObject entry, string name)
        {
            var type = new InterfaceTypeSymbol(name, _unit, []) { IsForward = false };
            _byId[id] = type;                                                   // before the members: they may name the interface
            if (string.Equals(_unit.Name, "System", StringComparison.OrdinalIgnoreCase) && string.Equals(name, "IInterface", StringComparison.OrdinalIgnoreCase))
            {
                types.IInterface = type;                                        // the root interface (ABI-094)
            }

            type.Parent = entry["parent"] is null ? null
                : Type(entry, "parent") as InterfaceTypeSymbol ?? throw new PcuFormatException($"the parent of '{name}' is not an interface");
            if (entry["guid"] is JsonValue guid)
            {
                type.Guid = System.Guid.TryParseExact(guid.GetValue<string>(), "B", out var parsed)
                    ? [.. parsed.ToByteArray()] : throw new PcuFormatException($"GUID of '{name}'");
            }

            type.Methods = [.. Array(entry, "methods").Select(m =>
            {
                var method = new MethodSymbol(Text(m, "name"), type, [], declaration: null!, MethodKind.Ordinary, Visibility.Public);
                method.Parameters = Parameters(m!, method);
                method.ReturnType = m!["result"] is null ? null : Type(m, "result");
                method.IsOverload = Array(m, "flags").Any(f => f!.GetValue<string>() == "overload");
                method.VmtSlot = m["slot"]!.GetValue<int>();
                method.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, method, []);
                return method;
            })];
            MethodSymbol? Slot(JsonNode? slot) => slot is JsonValue value
                ? type.AllMethods.ElementAtOrDefault(value.GetValue<int>()) ?? throw new PcuFormatException($"accessor slot of '{name}'")
                : null;
            type.Properties = [.. Array(entry, "properties").Select(p =>
            {
                var property = new PropertySymbol(Text(p, "name"), type, [], Visibility.Public)
                {
                    Type = Type(p!, "type"),
                    Index = p!["index"] is JsonValue index ? index.GetValue<int>() : null,
                    IsDefault = p["isDefault"]!.GetValue<bool>(),
                    ReadMethod = Slot(p["read"]),
                    WriteMethod = Slot(p["write"]),
                };
                property.Parameters = [.. Array(p, "params").Select((q, i) => new ParameterSymbol(Text(q, "name"), Type(q!, "type"), ParameterMode.Value, i, property, []))];
                return property;
            })];
            return type;
        }

        private MethodSymbol Method(ClassTypeSymbol type, JsonNode entry, ImmutableArray<MethodSymbol> parentSlots)
        {
            var flags = Array(entry, "flags").Select(f => f!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var binding = Text(entry, "binding");
            var isClass = flags.Contains("class") || binding is "class" or "classStatic";
            var kind = binding switch
            {
                "constructor" => MethodKind.Constructor,
                "destructor" => MethodKind.Destructor,
                "classStatic" => MethodKind.ClassStatic,
                _ => isClass ? MethodKind.ClassMethod : MethodKind.Ordinary,
            };
            var method = new MethodSymbol(Text(entry, "name"), type, [], declaration: null!, kind, VisibilityOf(Text(entry, "visibility")));
            method.Parameters = Parameters(entry, method);
            method.ReturnType = kind == MethodKind.Constructor ? type : entry["result"] is null ? null : Type(entry, "result");
            method.IsOverload = flags.Contains("overload");
            method.IsInline = flags.Contains("inline");
            method.IsNoReturn = flags.Contains("noreturn");
            method.VmtSlot = entry["slot"]!.GetValue<int>();
            method.MessageId = entry["msgId"]?.GetValue<int>();                       // C-113
            var inherited = method.VmtSlot >= 0 && method.VmtSlot < parentSlots.Length;   // the slot of an ancestor's method
            method.IsAbstract = binding == "abstract";
            method.IsFinal = binding == "final";
            method.IsOverride = inherited && binding is "override" or "final" or "abstract" or "constructor" or "destructor";
            method.IsVirtual = method.VmtSlot >= 0 && !method.IsOverride;
            method.OverriddenMethod = method.IsOverride ? parentSlots[method.VmtSlot] : null;
            if (kind != MethodKind.ClassStatic)
            {
                method.SelfParameter = new ParameterSymbol("Self", isClass ? type.Reference : type, ParameterMode.Value, -1, method, []);
            }

            if (kind is MethodKind.Constructor or MethodKind.Destructor)
            {
                method.FlagParameter = new ParameterSymbol(kind == MethodKind.Constructor ? "Alloc" : "Dealloc", BuiltIn("Byte"), ParameterMode.Value, -1, method, []);
            }

            if (Text(entry, "linkName") != method.LinkName)
            {
                throw new PcuFormatException($"link name of method '{type.Name}.{method.Name}'");
            }

            return method;
        }

        /// <summary>The class vars of a record or helper (C-219); a file written before C-219 has no visibility (public).</summary>
        private ImmutableArray<VariableSymbol> ClassVars(TypeSymbol owner, JsonNode entry) =>
            [.. (entry["classVars"] as JsonArray ?? []).Select(v =>
            {
                var visibility = v!["visibility"] is JsonValue text ? VisibilityOf(text.GetValue<string>()) : Visibility.Public;
                var variable = new VariableSymbol(Text(v, "name"), Type(v, "type"), isGlobal: true, owner, [], visibility: visibility);
                variable.ExplicitLinkName = Explicit(Text(v, "linkName"), Emit.Mangler.GlobalVariable(variable).Text);
                return variable;
            })];

        /// <summary>
        /// C-219: a record or helper method as the binder creates it (C-122…C-124): class methods, record constructors and operators are
        /// static; a record constructor returns the record through its <c>Result</c>; an instance method's <c>Self</c> is a <c>var</c>
        /// parameter of the record (or the helped value), the object itself for a class helper.
        /// </summary>
        private MethodSymbol StaticMethod(TypeSymbol owner, JsonNode entry)
        {
            var flags = Array(entry, "flags").Select(f => f!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var name = Text(entry, "name");
            var isConstructor = flags.Contains("constructor");
            var isStatic = flags.Contains("class") || isConstructor;
            var method = new MethodSymbol(name, owner, [], declaration: null!, isStatic ? MethodKind.ClassStatic : MethodKind.Ordinary,
                VisibilityOf(Text(entry, "visibility")))
            {
                IsRecordConstructor = isConstructor,
                OperatorName = flags.Contains("operator")
                    ? Binding.Operators.Arity.Keys.FirstOrDefault(k => Syntax.IdentifierComparer.Instance.Equals(k, name)) ?? throw new PcuFormatException($"unknown operator '{name}'")
                    : null,
            };
            method.Parameters = Parameters(entry, method);
            method.ReturnType = entry["result"] is null ? null : Type(entry, "result");
            method.IsOverload = flags.Contains("overload");
            method.IsInline = flags.Contains("inline");
            method.IsNoReturn = flags.Contains("noreturn");
            if (isConstructor)
            {
                method.ResultVariable = new VariableSymbol("Result", owner, isGlobal: false, method, []);
            }
            else if (!isStatic)
            {
                var helper = owner as HelperTypeSymbol;
                method.SelfParameter = new ParameterSymbol("Self", helper?.ExtendedType ?? owner,
                    helper is { IsClassHelper: true } ? ParameterMode.Value : ParameterMode.Var, -1, method, []);
            }

            if (Text(entry, "linkName") != method.LinkName)
            {
                throw new PcuFormatException($"link name of method '{owner.Name}.{name}'");
            }

            return method;
        }

        /// <summary>A property; its accessors are a method's link name or a field path from the class, record or helper (LANG-641).</summary>
        private PropertySymbol Property(TypeSymbol type, JsonNode entry)
        {
            var property = new PropertySymbol(Text(entry, "name"), type, [], VisibilityOf(Text(entry, "visibility")))
            {
                Type = Type(entry, "type"),
                Index = entry["index"] is JsonValue index ? index.GetValue<int>() : null,
                IsDefault = entry["isDefault"]!.GetValue<bool>(),
            };
            property.Parameters = [.. Array(entry, "params").Select((p, i) => new ParameterSymbol(Text(p, "name"), Type(p!, "type"), ParameterMode.Value, i, property, []))];
            (property.ReadPath, property.ReadMethod) = Accessor(type, entry["read"]);
            (property.WritePath, property.WriteMethod) = Accessor(type, entry["write"]);
            property.IsClassProperty = property.ReadMethod?.IsClassMethod ?? property.WriteMethod?.IsClassMethod
                ?? (property.ReadPath.IsEmpty ? property.WritePath : property.ReadPath).FirstOrDefault() is VariableSymbol;
            return property;
        }

        private static (ImmutableArray<Symbol> Path, MethodSymbol? Method) Accessor(TypeSymbol type, JsonNode? accessor)
        {
            if (accessor is not JsonValue value)
            {
                return ([], null);
            }

            static IEnumerable<MethodSymbol> MethodsOf(TypeSymbol? t) => t switch
            {
                ClassTypeSymbol c => c.SelfAndAncestors().SelectMany(a => a.Methods),
                RecordTypeSymbol r => r.Methods,
                HelperTypeSymbol h => h.SelfAndAncestors().SelectMany(a => a.Methods).Concat(MethodsOf(h.ExtendedType)),
                _ => [],
            };

            static Symbol? Member(TypeSymbol? t, string part) => t switch
            {
                ClassTypeSymbol c => c.SelfAndAncestors().SelectMany(a => a.Fields.Cast<Symbol>().Concat(a.ClassVariables))
                    .FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase)),
                RecordTypeSymbol r => r.Fields.Cast<Symbol>().Concat(r.ClassVariables).FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase)),
                HelperTypeSymbol h => h.SelfAndAncestors().SelectMany(a => a.ClassVariables).FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase))
                    ?? Member(h.ExtendedType, part),
                _ => null,
            };

            var text = value.GetValue<string>();
            if (MethodsOf(type).FirstOrDefault(m => m.LinkName == text) is { } method)
            {
                return ([], method);
            }

            var path = new List<Symbol>();
            TypeSymbol? current = type;
            foreach (var part in text.Split('.'))
            {
                var member = Member(current, part);
                path.Add(member ?? throw new PcuFormatException($"property accessor '{text}' of '{type.Name}'"));
                current = member switch { FieldSymbol f => f.Type, VariableSymbol v => v.Type, _ => null };
            }

            return ([.. path], null);
        }

        private static Visibility VisibilityOf(string text) => text switch
        {
            "strict private" => Visibility.StrictPrivate,
            "private" => Visibility.Private,
            "strict protected" => Visibility.StrictProtected,
            "protected" => Visibility.Protected,
            "published" => Visibility.Published,
            "public" => Visibility.Public,
            _ => throw new PcuFormatException($"unknown visibility '{text}'"),
        };

        private TypeSymbol BuiltIn(string name) => name switch
        {
            "nil" => types.Nil,
            "[]" => types.EmptySet,
            "untyped" => types.Untyped,
            _ => types.Scope.LookupLocal(name) as TypeSymbol ?? throw new PcuFormatException($"'{name}' is not a predeclared type"),
        };
    }

    private static JsonNode Node(JsonNode? owner, string key) => owner?[key] ?? throw new PcuFormatException($"missing '{key}'");

    private static JsonArray Array(JsonNode? owner, string key) => Node(owner, key) as JsonArray ?? throw new PcuFormatException($"'{key}' is not an array");

    private static string Text(JsonNode? owner, string key) =>
        Node(owner, key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new PcuFormatException($"'{key}' is not a string");
}
