using System.Text.Json;

namespace FreexComp.Debugger.Values.TypeInfo;

/// <summary>
/// Reads the type-information file written by the compiler next to the executable (interface document
/// "Pascal debug-info sidecar"). Unknown members are ignored; a missing file is not an error (DBG-403).
/// </summary>
internal static class PdiLoader
{
    public const int SupportedFormatVersion = 1;

    /// <summary>
    /// Null when the file does not exist. A file that cannot be read, is not valid JSON, has another format version
    /// or breaks the schema faults with <see cref="DebugErrorCode.TypeInfoInvalid"/>.
    /// </summary>
    public static PdiFile? Load(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
                return null;
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new DebuggerException(DebugErrorCode.TypeInfoInvalid, Messages.TypeInfoInvalid(path, e.Message), null, e);
        }

        return Parse(bytes, path);
    }

    /// <param name="utf8Json">File content.</param>
    /// <param name="path">Only for messages.</param>
    public static PdiFile Parse(ReadOnlyMemory<byte> utf8Json, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return Read(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new DebuggerException(DebugErrorCode.TypeInfoInvalid, Messages.TypeInfoInvalid(path, "malformed JSON: " + e.Message), null, e);
        }
        catch (FormatException e)
        {
            throw new DebuggerException(DebugErrorCode.TypeInfoInvalid, Messages.TypeInfoInvalid(path, e.Message), null, e);
        }
    }

    private static PdiFile Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("the root is not an object");
        var version = Int(root, "formatVersion", "the file");
        if (version != SupportedFormatVersion)
            throw new FormatException("unsupported formatVersion " + version);

        var abi = ReadAbi(Object(root, "abi", "the file"));

        // Types: read all entries, then link the references by id.
        var types = new List<PdiType>();
        var byId = new Dictionary<int, PdiType>();
        var elements = new List<JsonElement>();
        foreach (var element in Array(root, "types", "the file"))
        {
            var type = ReadType(element);
            if (type.Id <= 0 || !byId.TryAdd(type.Id, type))
                throw new FormatException("type id " + type.Id + " is not a unique positive integer");
            types.Add(type);
            elements.Add(element);
        }

        PdiType Resolve(int id, string owner) =>
            byId.TryGetValue(id, out var type) ? type : throw new FormatException(owner + " refers to unknown type id " + id);

        PdiType? Reference(JsonElement element, string member, string owner, bool required)
        {
            if (element.TryGetProperty(member, out var value) && value.ValueKind != JsonValueKind.Null)
                return Resolve(AsInt(value, member, owner), owner);
            return required ? throw new FormatException(owner + " lacks '" + member + "'") : null;
        }

        for (var i = 0; i < types.Count; i++)
        {
            var type = types[i];
            var element = elements[i];
            var owner = "type " + type.Id;
            type.AliasOf = Reference(element, "aliasOf", owner, required: false);
            switch (type.Kind)
            {
                case PdiTypeKind.Set:
                    type.Base = Reference(element, "base", owner, required: true);
                    break;
                case PdiTypeKind.StaticArray:
                    type.Element = Reference(element, "element", owner, required: true);
                    type.IndexType = Reference(element, "indexType", owner, required: false);
                    break;
                case PdiTypeKind.DynArray or PdiTypeKind.OpenArray:
                    type.Element = Reference(element, "element", owner, required: true);
                    break;
                case PdiTypeKind.Class:
                    type.Ancestor = Reference(element, "ancestor", owner, required: false);
                    break;
                case PdiTypeKind.ClassRef:
                    type.Of = Reference(element, "of", owner, required: true);
                    break;
                case PdiTypeKind.Pointer:
                    type.To = Reference(element, "to", owner, required: false);
                    break;
            }

            if (type.Fields.Count > 0)
            {
                var index = 0;
                foreach (var field in element.GetProperty("fields").EnumerateArray())
                    type.Fields[index++].Type = Resolve(Int(field, "type", owner + " field"), owner);
            }
        }

        var units = new List<PdiUnit>();
        foreach (var element in Array(root, "units", "the file"))
        {
            var unitName = String(element, "name", "a unit");
            var owner = "unit " + unitName;
            var globals = new List<PdiGlobal>();
            foreach (var global in OptionalArray(element, "globals"))
            {
                globals.Add(new PdiGlobal(
                    String(global, "name", owner + " global"),
                    Resolve(Int(global, "type", owner + " global"), owner),
                    OptionalString(global, "linkageName") ?? ""));
            }

            var routines = new List<PdiRoutine>();
            foreach (var routine in OptionalArray(element, "routines"))
                routines.Add(ReadRoutine(routine, owner, Resolve));

            var unit = new PdiUnit { Name = unitName, File = OptionalString(element, "file") ?? "", Globals = globals, Routines = routines };
            foreach (var routine in routines)
                routine.Unit = unit;
            units.Add(unit);
        }

        return new PdiFile(OptionalString(root, "program") ?? "", OptionalString(root, "container") ?? "", abi, types, units);
    }

    private static PdiAbi ReadAbi(JsonElement abi)
    {
        const string Owner = "abi";
        var unicode = Object(abi, "unicodeString", Owner);
        var ansi = Object(abi, "ansiString", Owner);
        var dynArray = Object(abi, "dynArray", Owner);
        var vmt = Object(abi, "vmt", Owner);
        var set = Object(abi, "set", Owner);

        var bitOrder = String(set, "bitOrder", "abi.set");
        if (bitOrder != "lsb0")
            throw new FormatException("unsupported abi.set.bitOrder '" + bitOrder + "'");
        var lengthUnit = String(unicode, "lengthUnit", "abi.unicodeString");
        if (lengthUnit is not ("chars" or "bytes"))
            throw new FormatException("unsupported abi.unicodeString.lengthUnit '" + lengthUnit + "'");
        var code = Long(abi, "pascalExceptionCode", Owner);
        if (code is < 0 or > uint.MaxValue)
            throw new FormatException("abi.pascalExceptionCode is not an unsigned 32-bit number");

        return new PdiAbi(
            Size(Int(abi, "pointerSize", Owner), "abi.pointerSize"),
            String(abi, "raiseHookSymbol", Owner),
            (uint)code,
            String(abi, "exceptionMessageField", Owner),
            new PdiUnicodeStringAbi(
                Long(unicode, "lengthOffset", "abi.unicodeString"),
                Size(Int(unicode, "lengthSize", "abi.unicodeString"), "abi.unicodeString.lengthSize"),
                lengthUnit == "bytes",
                Int(unicode, "charSize", "abi.unicodeString") is 2 ? 2 : throw new FormatException("unsupported abi.unicodeString.charSize")),
            new PdiAnsiStringAbi(
                Long(ansi, "lengthOffset", "abi.ansiString"),
                Size(Int(ansi, "lengthSize", "abi.ansiString"), "abi.ansiString.lengthSize"),
                Long(ansi, "codePageOffset", "abi.ansiString"),
                Size(Int(ansi, "codePageSize", "abi.ansiString"), "abi.ansiString.codePageSize")),
            new PdiDynArrayAbi(
                Long(dynArray, "lengthOffset", "abi.dynArray"),
                Size(Int(dynArray, "lengthSize", "abi.dynArray"), "abi.dynArray.lengthSize")),
            new PdiVmtAbi(
                Long(vmt, "classNameOffset", "abi.vmt"),
                String(vmt, "classNameKind", "abi.vmt") switch
                {
                    "pointerToShortString" => PdiClassNameKind.PointerToShortString,
                    "pointerToUnicodeString" => PdiClassNameKind.PointerToUnicodeString,
                    "inlineShortString" => PdiClassNameKind.InlineShortString,
                    var other => throw new FormatException("unsupported abi.vmt.classNameKind '" + other + "'"),
                },
                Long(vmt, "parentOffset", "abi.vmt"),
                String(vmt, "parentKind", "abi.vmt") switch
                {
                    "pointerToVmt" => PdiParentKind.PointerToVmt,
                    "pointerToPointerToVmt" => PdiParentKind.PointerToPointerToVmt,
                    var other => throw new FormatException("unsupported abi.vmt.parentKind '" + other + "'"),
                }));
    }

    /// <summary>Byte size of an integer field read from memory: 1, 2, 4 or 8.</summary>
    private static int Size(int value, string what) =>
        value is 1 or 2 or 4 or 8 ? value : throw new FormatException(what + " must be 1, 2, 4 or 8");

    private static PdiType ReadType(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("a type entry is not an object");
        var id = Int(element, "id", "a type");
        var owner = "type " + id;
        var kindText = String(element, "kind", owner);
        var size = Int(element, "size", owner);
        if (size < 0)
            throw new FormatException(owner + " has a negative size");

        // A kind this reader does not know is shown as raw bytes.
        var kind = kindText switch
        {
            "integer" => PdiTypeKind.Integer,
            "float" => PdiTypeKind.Float,
            "boolean" => PdiTypeKind.Boolean,
            "char" => PdiTypeKind.Char,
            "enum" => PdiTypeKind.Enum,
            "set" => PdiTypeKind.Set,
            "string" => PdiTypeKind.String,
            "staticArray" => PdiTypeKind.StaticArray,
            "dynArray" => PdiTypeKind.DynArray,
            "openArray" => PdiTypeKind.OpenArray,
            "record" => PdiTypeKind.Record,
            "class" => PdiTypeKind.Class,
            "classRef" => PdiTypeKind.ClassRef,
            "pointer" => PdiTypeKind.Pointer,
            "interface" => PdiTypeKind.Interface,
            "procedural" => PdiTypeKind.Procedural,
            _ => PdiTypeKind.Opaque,
        };

        var fields = new List<PdiField>();
        if (kind is PdiTypeKind.Record or PdiTypeKind.Class)
        {
            foreach (var field in OptionalArray(element, "fields"))
            {
                fields.Add(new PdiField
                {
                    Name = String(field, "name", owner + " field"),
                    Offset = Long(field, "offset", owner + " field"),
                    Visibility = OptionalString(field, "visibility"),
                });
            }
        }

        var values = new List<PdiEnumValue>();
        if (kind == PdiTypeKind.Enum)
        {
            foreach (var value in OptionalArray(element, "values"))
                values.Add(new PdiEnumValue(String(value, "name", owner + " value"), Long(value, "value", owner + " value")));
        }

        var count = kind == PdiTypeKind.StaticArray ? Long(element, "count", owner) : 0;
        if (count < 0)
            throw new FormatException(owner + " has a negative count");

        return new PdiType
        {
            Id = id,
            Name = OptionalString(element, "name") ?? "",
            Unit = OptionalString(element, "unit"),
            Kind = kind,
            Size = size,
            Signed = kind == PdiTypeKind.Integer && Bool(element, "signed", owner),
            Low = kind == PdiTypeKind.Integer ? OptionalLong(element, "low") : null,
            High = kind == PdiTypeKind.Integer ? OptionalLong(element, "high") : null,
            FloatFormat = kind != PdiTypeKind.Float ? default : String(element, "format", owner) switch
            {
                "single" => PdiFloatFormat.Single,
                "double" => PdiFloatFormat.Double,
                "currency" => PdiFloatFormat.Currency,
                "datetime" => PdiFloatFormat.DateTime,
                var other => throw new FormatException(owner + " has the unknown float format '" + other + "'"),
            },
            Flavor = kind != PdiTypeKind.String ? default : String(element, "flavor", owner) switch
            {
                "unicode" => PdiStringFlavor.Unicode,
                "ansi" => PdiStringFlavor.Ansi,
                "short" => PdiStringFlavor.Short,
                "pchar" => PdiStringFlavor.PChar,
                "pansichar" => PdiStringFlavor.PAnsiChar,
                var other => throw new FormatException(owner + " has the unknown string flavor '" + other + "'"),
            },
            MaxLength = (int)(OptionalLong(element, "maxLength") ?? Math.Max(size - 1, 0)),
            Values = values,
            BitBase = OptionalLong(element, "bitBase") ?? 0,
            ArrayLow = kind == PdiTypeKind.StaticArray ? Long(element, "low", owner) : 0,
            Count = count,
            Fields = fields,
            InstanceSize = (int)(OptionalLong(element, "instanceSize") ?? 0),
            Method = kind == PdiTypeKind.Procedural && element.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.True,
        };
    }

    private static PdiRoutine ReadRoutine(JsonElement routine, string unitOwner, Func<int, string, PdiType> resolve)
    {
        var displayName = String(routine, "displayName", unitOwner + " routine");
        var owner = "routine " + displayName;
        var variables = new List<PdiVariable>();
        foreach (var variable in OptionalArray(routine, "vars"))
        {
            var name = String(variable, "name", owner + " variable");
            variables.Add(new PdiVariable(
                name,
                String(variable, "kind", owner + " variable " + name) switch
                {
                    "param" => PdiVariableKind.Param,
                    "varParam" => PdiVariableKind.VarParam,
                    "self" => PdiVariableKind.Self,
                    "result" => PdiVariableKind.Result,
                    "local" => PdiVariableKind.Local,
                    var other => throw new FormatException(owner + " variable " + name + " has the unknown kind '" + other + "'"),
                },
                resolve(Int(variable, "type", owner + " variable " + name), owner),
                (int?)OptionalLong(variable, "fromLine"),
                (int?)OptionalLong(variable, "toLine")));
        }

        var self = routine.TryGetProperty("self", out var selfId) && selfId.ValueKind != JsonValueKind.Null
            ? resolve(AsInt(selfId, "self", owner), owner)
            : null;
        return new PdiRoutine
        {
            DisplayName = displayName,
            LinkageName = OptionalString(routine, "linkageName") ?? "",
            File = String(routine, "file", owner),
            Line = Int(routine, "line", owner),
            BodyLine = (int)(OptionalLong(routine, "bodyLine") ?? Int(routine, "line", owner)),
            EndLine = Int(routine, "endLine", owner),
            Self = self,
            Parent = OptionalString(routine, "parent"),
            Variables = variables,
        };
    }

    // ---- JSON access; a FormatException names what is wrong ----

    private static JsonElement Member(JsonElement element, string name, string owner)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException(owner + " is not an object");
        return element.TryGetProperty(name, out var value) ? value : throw new FormatException(owner + " lacks '" + name + "'");
    }

    private static JsonElement Object(JsonElement element, string name, string owner)
    {
        var value = Member(element, name, owner);
        return value.ValueKind == JsonValueKind.Object ? value : throw new FormatException("'" + name + "' of " + owner + " is not an object");
    }

    private static List<JsonElement> Array(JsonElement element, string name, string owner)
    {
        var value = Member(element, name, owner);
        return value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : throw new FormatException("'" + name + "' of " + owner + " is not an array");
    }

    private static List<JsonElement> OptionalArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("an entry that should be an object is not");
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return [];
        return value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : throw new FormatException("'" + name + "' is not an array");
    }

    private static string String(JsonElement element, string name, string owner)
    {
        var value = Member(element, name, owner);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException("'" + name + "' of " + owner + " is not a string");
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Long(JsonElement element, string name, string owner)
    {
        var value = Member(element, name, owner);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new FormatException("'" + name + "' of " + owner + " is not an integer");
    }

    private static long? OptionalLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static int Int(JsonElement element, string name, string owner) => AsInt(Member(element, name, owner), name, owner);

    private static int AsInt(JsonElement value, string name, string owner) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new FormatException("'" + name + "' of " + owner + " is not an integer");

    private static bool Bool(JsonElement element, string name, string owner)
    {
        var value = Member(element, name, owner);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException("'" + name + "' of " + owner + " is not a Boolean"),
        };
    }
}
