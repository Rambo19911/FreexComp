using System.Buffers.Binary;
using System.Numerics;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Expressions;

/// <summary>The frame an expression is evaluated in.</summary>
/// <param name="AdapterFrameId">The adapter's frame id.</param>
/// <param name="Routine">The routine of the type information that contains the frame's line; null when the frame has none.</param>
/// <param name="Line">The frame's line (block-scoped variables are visible by line).</param>
internal sealed record FrameContext(int AdapterFrameId, PdiRoutine? Routine, int Line);

/// <summary>
/// Evaluates an expression tree in a frame (Pascal values §9.2 … §9.5). Uses only "address of a root variable" and
/// memory reads; it never changes the debuggee (no calls, no assignments) and never forwards text to the adapter.
/// </summary>
internal sealed class Evaluator
{
    /// <summary>Longest string read for an operator: 1 MiB of UTF-16 (§9.3).</summary>
    public const int MaxStringUnits = 1 << 19;

    private readonly PdiFile _pdi;
    private readonly IDebuggeeAccess _debuggee;
    private readonly FrameContext _frame;
    private readonly ValueDecoder _decoder;
    private readonly int _pointerSize;

    public Evaluator(PdiFile pdi, IDebuggeeAccess debuggee, FrameContext frame)
    {
        _pdi = pdi;
        _debuggee = debuggee;
        _frame = frame;
        _pointerSize = pdi.Abi.PointerSize;
        // Strings are read in full (up to the limit); no summary children are needed here.
        _decoder = new ValueDecoder(pdi, debuggee, new ValueFormatOptions { MaxStringChars = MaxStringUnits, MaxSummaryElements = 0 });
    }

    public static DebuggerException Error(string message) => new(DebugErrorCode.ExpressionEvaluation, message);

    /// <summary>The first type of the type information with this name (case-insensitive).</summary>
    public static PdiType? FindType(PdiFile pdi, string name) =>
        pdi.Types.FirstOrDefault(type => type.Name.Length > 0 && type.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The variable of the frame's routine that an identifier names: visible at the frame's line, never a <c>$</c> temporary.</summary>
    public static PdiVariable? FindVariable(FrameContext frame, string name) =>
        frame.Routine?.Variables.LastOrDefault(v => IsVisible(v, frame.Line) && DisplayName(v.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static PdiGlobal? FindGlobal(FrameContext frame, string name) =>
        frame.Routine?.Unit.Globals.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Not a compiler temporary or hidden companion, and (for a block-scoped variable) in scope at <paramref name="line"/>.</summary>
    public static bool IsVisible(PdiVariable variable, int line) =>
        !variable.Name.StartsWith('$') && !variable.Name.EndsWith("$high", StringComparison.OrdinalIgnoreCase)
        && (variable.FromLine is not { } from || line >= from) && (variable.ToLine is not { } to || line <= to);

    /// <summary>The declared name without the <c>$n</c> suffix of a shadowing redeclaration.</summary>
    public static string DisplayName(string declaredName)
    {
        var dollar = declaredName.LastIndexOf('$');
        return dollar > 0 && dollar < declaredName.Length - 1 && declaredName.AsSpan(dollar + 1).IndexOfAnyExceptInRange('0', '9') < 0
            ? declaredName[..dollar]
            : declaredName;
    }

    /// <summary>The value of the hidden upper-bound variable of an open-array parameter; null when it cannot be read.</summary>
    public static async Task<long?> OpenArrayHighAsync(IDebuggeeAccess debuggee, FrameContext frame, string declaredName, CancellationToken ct)
    {
        var hidden = declaredName + "$high";
        var size = frame.Routine?.Variables.FirstOrDefault(v => v.Name.Equals(hidden, StringComparison.OrdinalIgnoreCase))?.Type.Size ?? 4;
        if (await debuggee.AddressOfAsync(frame.AdapterFrameId, hidden, ct).ConfigureAwait(false) is not { } address)
            return null;
        return await debuggee.ReadMemoryAsync(address, size, ct).ConfigureAwait(false) is { } bytes ? ValueFormatter.Signed(bytes) : null;
    }

    /// <summary>The value of the expression: a location, or an immediate.</summary>
    public async Task<Value> EvaluateAsync(Ast node, CancellationToken ct)
    {
        switch (node)
        {
            case Ast.IntLit literal:
                return new IntValue(literal.Bits, literal.Unsigned);
            case Ast.FloatLit literal:
                return new FloatValue(literal.Value);
            case Ast.StrLit literal:
                return new StrValue(literal.Text);
            case Ast.CharLit literal:
                return new CharValue(literal.Unit);
            case Ast.NilLit:
                return new NilValue();
            case Ast.Name name:
                return await ResolveAsync(name.Identifier, ct).ConfigureAwait(false);
            case Ast.Field field:
                return await FieldAsync(await EvaluateAsync(field.Target, ct).ConfigureAwait(false), field.Member, ct).ConfigureAwait(false);
            case Ast.Index index:
            {
                var target = await EvaluateAsync(index.Target, ct).ConfigureAwait(false);
                var position = await EvaluateValueAsync(index.Position, ct).ConfigureAwait(false);
                return await IndexAsync(target, position, ct).ConfigureAwait(false);
            }

            case Ast.Deref deref:
                return await DereferenceAsync(await EvaluateAsync(deref.Target, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            case Ast.Unary { Operator: "@" } address:
            {
                // Only a location has an address.
                var operand = await EvaluateAsync(address.Operand, ct).ConfigureAwait(false);
                return operand is LocationValue location
                    ? new PtrValue(location.Address, location.Type)
                    : throw Error(Messages.OperatorNotApplicable("@", operand.TypeName));
            }

            case Ast.Unary unary:
                return Unary(unary.Operator, await EvaluateValueAsync(unary.Operand, ct).ConfigureAwait(false));
            case Ast.Binary binary:
            {
                // Full evaluation: both sides always (no side effects exist).
                var left = await EvaluateValueAsync(binary.Left, ct).ConfigureAwait(false);
                var right = await EvaluateValueAsync(binary.Right, ct).ConfigureAwait(false);
                return Binary(binary.Operator, left, right);
            }

            case Ast.Call call:
                return await BuiltinAsync(call, ct).ConfigureAwait(false);
            case Ast.Cast cast:
                return await CastAsync(cast, ct).ConfigureAwait(false);
            case Ast.SetCtor set:
                return await SetAsync(set, ct).ConfigureAwait(false);
            default:
                throw Error(Messages.SyntaxError);
        }
    }

    /// <summary>The value of the expression with a location read (rvalue conversion).</summary>
    public async Task<Value> EvaluateValueAsync(Ast node, CancellationToken ct) =>
        await ReadAsync(await EvaluateAsync(node, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    // ---- identifiers (§9.2) ----

    private async Task<Value> ResolveAsync(string name, CancellationToken ct)
    {
        // 1. Variables of the frame's routine.
        if (FindVariable(_frame, name) is { } variable)
        {
            var address = await RootAddressAsync(variable.Name, ct).ConfigureAwait(false);
            var high = variable.Type.Kind == PdiTypeKind.OpenArray
                ? await OpenArrayHighAsync(_debuggee, _frame, variable.Name, ct).ConfigureAwait(false)
                : null;
            return new LocationValue(address, variable.Type, high);
        }

        // 2. Fields of the static class of Self: own, then inherited.
        if (_frame.Routine?.Self is { } self)
        {
            for (var type = self; type is not null; type = type.Ancestor)
            {
                if (type.Fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } field)
                    continue;
                var selfName = _frame.Routine.Variables.FirstOrDefault(v => v.Kind == PdiVariableKind.Self)?.Name ?? "Self";
                var instance = await PointerAtAsync(await RootAddressAsync(selfName, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                return instance == 0 ? throw Error(Messages.NilReference) : new LocationValue(Offset(instance, field.Offset), field.Type);
            }
        }

        // 3. Globals of the unit that contains the frame's routine.
        if (FindGlobal(_frame, name) is { } global)
            return new LocationValue(await RootAddressAsync(global.Name, ct).ConfigureAwait(false), global.Type);

        // 4. Enum value names (first in file order), True, False.
        if (name.Equals("True", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (name.Equals("False", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        foreach (var type in _pdi.Types)
        {
            if (type.Kind == PdiTypeKind.Enum && FindEnumValue(type, name) is { } value)
                return value;
        }

        // 5. Type names.
        return FindType(_pdi, name) is { } named ? new TypeValue(named) : throw Error(Messages.UndeclaredIdentifier(name));
    }

    private static EnumValue? FindEnumValue(PdiType type, string name) =>
        type.Values.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } value
            ? new EnumValue(type, value.Value)
            : null;

    private async Task<ulong> RootAddressAsync(string declaredName, CancellationToken ct) =>
        await _debuggee.AddressOfAsync(_frame.AdapterFrameId, declaredName, ct).ConfigureAwait(false)
        ?? throw Error(Messages.NotAvailableVariable(declaredName));

    // ---- designators (§9.4) ----

    private async Task<Value> FieldAsync(Value target, string member, CancellationToken ct)
    {
        switch (target)
        {
            case TypeValue { Type.Kind: PdiTypeKind.Enum } type:
                return FindEnumValue(type.Type, member) ?? throw Error(Messages.UndeclaredIdentifier(member));
            case LocationValue { Type.Kind: PdiTypeKind.Record } record:
                return record.Type.Fields.FirstOrDefault(f => f.Name.Equals(member, StringComparison.OrdinalIgnoreCase)) is { } field
                    ? new LocationValue(Offset(record.Address, field.Offset), field.Type)
                    : throw Error(Messages.UndeclaredIdentifier(member));
            case LocationValue { Type.Kind: PdiTypeKind.Class }:
                return await FieldAsync(await ReadAsync(target, ct).ConfigureAwait(false), member, ct).ConfigureAwait(false);
            case InstanceValue instance:
                // The dynamic class, then its ancestors.
                for (var type = instance.Type; type is not null; type = type.Ancestor)
                {
                    if (type.Fields.FirstOrDefault(f => f.Name.Equals(member, StringComparison.OrdinalIgnoreCase)) is { } inherited)
                        return new LocationValue(Offset(instance.Address, inherited.Offset), inherited.Type);
                }

                throw Error(Messages.UndeclaredIdentifier(member));
            case NilValue:
                throw Error(Messages.NilReference);
            default:
                throw Error(Messages.OperatorNotApplicable(".", target.TypeName));
        }
    }

    private async Task<Value> IndexAsync(Value target, Value position, CancellationToken ct)
    {
        if (Ordinal(position) is not { } index)
            throw Error(Messages.OperatorNotApplicable("[]", target.TypeName, position.TypeName));

        switch (target)
        {
            case LocationValue { Type.Kind: PdiTypeKind.StaticArray } array:
            {
                var type = array.Type;
                CheckBounds(index, type.ArrayLow, type.ArrayLow + type.Count - 1);
                return new LocationValue(array.Address + (ulong)((index - type.ArrayLow) * type.Element!.Size), type.Element);
            }

            case LocationValue { Type.Kind: PdiTypeKind.DynArray or PdiTypeKind.OpenArray } array:
            {
                var (data, length) = await ArrayPayloadAsync(array, ct).ConfigureAwait(false);
                CheckBounds(index, 0, length - 1);
                return new LocationValue(data + (ulong)(index * array.Type.Element!.Size), array.Type.Element);
            }

            case LocationValue { Type: { Kind: PdiTypeKind.String, Flavor: PdiStringFlavor.PChar or PdiStringFlavor.PAnsiChar } } text:
            {
                // A pointer to characters: element i, unchecked.
                var pointer = await PointerAtAsync(text.Address, ct).ConfigureAwait(false);
                if (pointer == 0)
                    throw Error(Messages.NilReference);
                var unit = text.Type.Flavor == PdiStringFlavor.PChar ? 2 : 1;
                return new CharValue((char)ValueFormatter.Unsigned(await BytesAsync(pointer + (ulong)(index * unit), unit, ct).ConfigureAwait(false)));
            }

            case LocationValue { Type.Kind: PdiTypeKind.String } or StrValue or CharValue:
            {
                var text = Text(await ReadAsync(target, ct).ConfigureAwait(false))!;
                CheckBounds(index, 1, text.Length);
                return new CharValue(text[(int)index - 1]);
            }

            case LocationValue { Type: { Kind: PdiTypeKind.Pointer, To: not null } } or PtrValue { Target: not null }:
            {
                var pointer = (PtrValue)await ReadAsync(target, ct).ConfigureAwait(false);
                if (pointer.Address == 0)
                    throw Error(Messages.NilReference);
                return new LocationValue(unchecked(pointer.Address + (ulong)(index * pointer.Target!.Size)), pointer.Target);
            }

            default:
                throw Error(Messages.OperatorNotApplicable("[]", target.TypeName, position.TypeName));
        }
    }

    private static void CheckBounds(long index, long low, long high)
    {
        if (index < low || index > high)
            throw Error(Messages.IndexOutOfBounds(index, low, high));
    }

    private async Task<Value> DereferenceAsync(Value target, CancellationToken ct)
    {
        switch (target)
        {
            case LocationValue { Type.Kind: PdiTypeKind.Class } or InstanceValue:
                return target;   // a class instance is a reference already
            case LocationValue { Type.Kind: PdiTypeKind.Pointer } or PtrValue:
            {
                var pointer = (PtrValue)await ReadAsync(target, ct).ConfigureAwait(false);
                if (pointer.Target is null)
                    throw Error(Messages.UntypedDereference);
                return pointer.Address == 0 ? throw Error(Messages.NilReference) : new LocationValue(pointer.Address, pointer.Target);
            }

            case NilValue:
                throw Error(Messages.NilReference);
            default:
                throw Error(Messages.OperatorNotApplicable("^", target.TypeName));
        }
    }

    /// <summary>Payload address and element count of a dynamic array or open-array parameter.</summary>
    private async Task<(ulong Data, long Length)> ArrayPayloadAsync(LocationValue array, CancellationToken ct)
    {
        var data = await PointerAtAsync(array.Address, ct).ConfigureAwait(false);
        if (array.Type.Kind == PdiTypeKind.OpenArray)
            return (data, data == 0 ? 0 : (array.OpenArrayHigh ?? throw Error(Messages.NotAvailable)) + 1);
        if (data == 0)
            return (0, 0);
        var abi = _pdi.Abi.DynArray;
        var length = ValueFormatter.Signed(await BytesAsync(Offset(data, abi.LengthOffset), abi.LengthSize, ct).ConfigureAwait(false));
        return length < 0 ? throw Error(Messages.InvalidArray(data)) : (data, length);
    }

    // ---- rvalue conversion (§9.3) ----

    /// <summary>Reads a location whose type has an immediate form; other locations (records, arrays …) and immediates are returned as they are.</summary>
    public async Task<Value> ReadAsync(Value value, CancellationToken ct)
    {
        if (value is not LocationValue location)
            return value;
        var type = location.Type;
        switch (type.Kind)
        {
            case PdiTypeKind.Integer when type.Size is 1 or 2 or 4 or 8:
            {
                var bytes = await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false);
                return type.Signed
                    ? new IntValue(ValueFormatter.Signed(bytes))
                    : new IntValue(unchecked((long)ValueFormatter.Unsigned(bytes)), Unsigned: type.Size == 8);
            }

            case PdiTypeKind.Float:
            {
                var bytes = await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false);
                return type.FloatFormat switch
                {
                    PdiFloatFormat.Single when bytes.Length == 4 => new FloatValue(BinaryPrimitives.ReadSingleLittleEndian(bytes)),
                    PdiFloatFormat.Currency when bytes.Length == 8 => new FloatValue(BinaryPrimitives.ReadInt64LittleEndian(bytes) / 10000.0),
                    _ when bytes.Length == 8 => new FloatValue(BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
                    _ => value,
                };
            }

            case PdiTypeKind.Boolean:
                return new BoolValue(ValueFormatter.Unsigned(await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false)) != 0);
            case PdiTypeKind.Char:
                return new CharValue((char)ValueFormatter.Unsigned(await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false)));
            case PdiTypeKind.Enum:
                return new EnumValue(type, (long)ValueFormatter.Unsigned(await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false)));
            case PdiTypeKind.Set:
            {
                var bytes = await BytesAsync(location.Address, type.Size, ct).ConfigureAwait(false);
                return new SetValue(type.Base, new BigInteger(bytes, isUnsigned: true) << (int)Math.Clamp(type.BitBase, 0, 255));
            }

            case PdiTypeKind.String:
            {
                // Read in full for an operator, up to the limit: the declared length is checked before the payload is read.
                if (type.Flavor is PdiStringFlavor.Unicode or PdiStringFlavor.Ansi
                    && await PointerAtAsync(location.Address, ct).ConfigureAwait(false) is not 0 and var payload)
                {
                    var (offset, size, divisor) = type.Flavor == PdiStringFlavor.Unicode
                        ? (_pdi.Abi.UnicodeString.LengthOffset, _pdi.Abi.UnicodeString.LengthSize,
                            _pdi.Abi.UnicodeString.LengthInBytes ? Math.Max(_pdi.Abi.UnicodeString.CharSize, 1) : 1)
                        : (_pdi.Abi.AnsiString.LengthOffset, _pdi.Abi.AnsiString.LengthSize, 1);
                    if (ValueFormatter.Signed(await BytesAsync(Offset(payload, offset), size, ct).ConfigureAwait(false)) / divisor > MaxStringUnits)
                        throw Error(Messages.StringTooLong);
                }

                var node = await DecodeAsync(location, ct).ConfigureAwait(false);
                if (node.Capped)
                    throw Error(Messages.StringTooLong);
                return node.Partial ? throw Error(Messages.UnreadableMemory(node.Pointer)) : new StrValue(node.Text ?? "");
            }

            case PdiTypeKind.Pointer:
                return new PtrValue(await PointerAtAsync(location.Address, ct).ConfigureAwait(false), type.To);
            case PdiTypeKind.ClassRef or PdiTypeKind.Interface:
            case PdiTypeKind.Procedural when !type.Method:
                return new PtrValue(await PointerAtAsync(location.Address, ct).ConfigureAwait(false), null);
            case PdiTypeKind.Class:
            {
                // The dynamic class of the instance (DBG-168), by the one decoding path.
                var node = await DecodeAsync(location, ct).ConfigureAwait(false);
                return node.Pointer == 0 ? new NilValue() : new InstanceValue(node.Pointer, node.Type, node.Text ?? node.TypeName);
            }

            default:
                return value;
        }
    }

    private async Task<ValueNode> DecodeAsync(LocationValue location, CancellationToken ct)
    {
        var node = await _decoder.DecodeAsync("", location.Type, location.Address, null, ct, location.OpenArrayHigh).ConfigureAwait(false);
        return node.Kind == ValueKind.Error ? throw Error(node.Error ?? Messages.NotAvailable) : node;
    }

    private async Task<byte[]> BytesAsync(ulong address, int count, CancellationToken ct) =>
        await _debuggee.ReadMemoryAsync(address, count, ct).ConfigureAwait(false) ?? throw Error(Messages.UnreadableMemory(address));

    private async Task<ulong> PointerAtAsync(ulong address, CancellationToken ct) =>
        ValueFormatter.Unsigned(await BytesAsync(address, _pointerSize, ct).ConfigureAwait(false));

    private static ulong Offset(ulong address, long offset) => unchecked(address + (ulong)offset);

    // ---- operators (§9.4) ----

    private static Value Unary(string op, Value operand) => (op, operand) switch
    {
        ("-", IntValue i) => new IntValue(unchecked(-i.Bits)),
        ("-", FloatValue f) => new FloatValue(-f.Number),
        ("+", IntValue or FloatValue) => operand,
        ("not", BoolValue b) => new BoolValue(!b.Flag),
        ("not", IntValue i) => new IntValue(~i.Bits, i.Unsigned),
        _ => throw Error(Messages.OperatorNotApplicable(op, operand.TypeName)),
    };

    private static Value Binary(string op, Value left, Value right)
    {
        var result = op switch
        {
            "+" or "-" or "*" => Arithmetic(op, left, right),
            "/" => Number(left) is { } l && Number(right) is { } r
                ? new FloatValue(r == 0 ? throw Error(Messages.DivisionByZero) : l / r)
                : null,
            "div" or "mod" or "shl" or "shr" => left is IntValue l && right is IntValue r ? Integer(op, l, r) : null,
            "and" or "or" or "xor" => Logical(op, left, right),
            "=" => Equal(left, right) is { } equal ? new BoolValue(equal) : null,
            "<>" => Equal(left, right) is { } equal ? new BoolValue(!equal) : null,
            "<" or "<=" or ">" or ">=" => Relation(op, left, right),
            "in" => Membership(left, right),
            _ => null,
        };
        return result ?? throw Error(Messages.OperatorNotApplicable(op, left.TypeName, right.TypeName));
    }

    private static Value? Arithmetic(string op, Value left, Value right)
    {
        switch (left, right)
        {
            case (IntValue l, IntValue r):
                // 64-bit wrap-around, no overflow error.
                return new IntValue(unchecked(op switch { "+" => l.Bits + r.Bits, "-" => l.Bits - r.Bits, _ => l.Bits * r.Bits }), l.Unsigned || r.Unsigned);
            case (SetValue l, SetValue r) when SameElement(l.Element, r.Element):
                return new SetValue(l.Element ?? r.Element, op switch
                {
                    "+" => l.Members | r.Members,
                    "-" => l.Members & ~r.Members,
                    _ => l.Members & r.Members,
                });
        }

        if (op == "+" && Text(left) is { } a && Text(right) is { } b)
            return new StrValue(a + b);
        if (Number(left) is { } x && Number(right) is { } y)
            return new FloatValue(op switch { "+" => x + y, "-" => x - y, _ => x * y });
        return null;
    }

    private static IntValue Integer(string op, IntValue left, IntValue right)
    {
        var unsigned = left.Unsigned || right.Unsigned;
        switch (op)
        {
            case "shl":
                return new IntValue(left.Bits << (int)(right.Bits & 63), unsigned);
            case "shr":
                return new IntValue(unchecked((long)((ulong)left.Bits >> (int)(right.Bits & 63))), unsigned);
        }

        if (right.Number == 0)
            throw Error(Messages.DivisionByZero);
        // Truncating toward zero; the sign of "mod" is the sign of the dividend.
        var result = op == "div" ? left.Number / right.Number : left.Number % right.Number;
        return new IntValue(unchecked((long)(ulong)(UInt128)(result & ulong.MaxValue)), unsigned);
    }

    private static Value? Logical(string op, Value left, Value right) => (left, right) switch
    {
        (BoolValue l, BoolValue r) => new BoolValue(op switch { "and" => l.Flag & r.Flag, "or" => l.Flag | r.Flag, _ => l.Flag ^ r.Flag }),
        (IntValue l, IntValue r) => new IntValue(op switch { "and" => l.Bits & r.Bits, "or" => l.Bits | r.Bits, _ => l.Bits ^ r.Bits }, l.Unsigned || r.Unsigned),
        _ => null,
    };

    private static bool? Equal(Value left, Value right)
    {
        switch (left, right)
        {
            case (IntValue l, IntValue r):
                return l.Number == r.Number;
            case (BoolValue l, BoolValue r):
                return l.Flag == r.Flag;
            case (CharValue l, CharValue r):
                return l.Unit == r.Unit;
            case (EnumValue l, EnumValue r) when SameType(l.Type, r.Type):
                return l.Ordinal == r.Ordinal;
            case (SetValue l, SetValue r) when SameElement(l.Element, r.Element):
                return l.Members == r.Members;
        }

        if (Address(left) is { } a && Address(right) is { } b)
            return a == b;
        if (Text(left) is { } x && Text(right) is { } y)
            return string.Equals(x, y, StringComparison.Ordinal);
        if (Number(left) is { } m && Number(right) is { } n)
            return m == n;
        return null;
    }

    private static Value? Relation(string op, Value left, Value right)
    {
        int? order = (left, right) switch
        {
            (IntValue l, IntValue r) => l.Number.CompareTo(r.Number),
            (CharValue l, CharValue r) => l.Unit.CompareTo(r.Unit),
            (EnumValue l, EnumValue r) when SameType(l.Type, r.Type) => l.Ordinal.CompareTo(r.Ordinal),
            _ => null,
        };
        if (order is null && left is SetValue a && right is SetValue b && SameElement(a.Element, b.Element))
        {
            // On sets "<=" and ">=" mean subset and superset.
            return op switch
            {
                "<=" => new BoolValue((a.Members & ~b.Members).IsZero),
                ">=" => new BoolValue((b.Members & ~a.Members).IsZero),
                _ => null,
            };
        }

        // Strings compare by UTF-16 code unit, case-sensitive.
        if (order is null && Text(left) is { } x && Text(right) is { } y)
            order = string.CompareOrdinal(x, y);
        if (order is null && Number(left) is { } m && Number(right) is { } n)
        {
            if (double.IsNaN(m) || double.IsNaN(n))
                return new BoolValue(false);
            order = m.CompareTo(n);
        }

        return order is { } o
            ? new BoolValue(op switch { "<" => o < 0, "<=" => o <= 0, ">" => o > 0, _ => o >= 0 })
            : null;
    }

    private static Value? Membership(Value left, Value right)
    {
        if (right is not SetValue set || Ordinal(left) is not { } ordinal || !SameElement(set.Element, ElementType(left)))
            return null;
        return new BoolValue(ordinal is >= 0 and <= 255 && !(set.Members & (BigInteger.One << (int)ordinal)).IsZero);
    }

    // ---- value helpers ----

    /// <summary>Int or Float as a floating-point number.</summary>
    private static double? Number(Value value) => value switch
    {
        IntValue i => (double)i.Number,
        FloatValue f => f.Number,
        _ => null,
    };

    /// <summary>Str or Char as text.</summary>
    private static string? Text(Value value) => value switch
    {
        StrValue s => s.Text,
        CharValue c => c.Unit.ToString(),
        _ => null,
    };

    /// <summary>The address of a pointer-like value: pointer, nil or instance reference.</summary>
    private static ulong? Address(Value value) => value switch
    {
        PtrValue p => p.Address,
        InstanceValue i => i.Address,
        NilValue => 0,
        _ => null,
    };

    /// <summary>The ordinal of an Int, Char, Bool or Enum value.</summary>
    public static long? Ordinal(Value value) => value switch
    {
        IntValue i => i.Bits,
        CharValue c => c.Unit,
        BoolValue b => b.Flag ? 1 : 0,
        EnumValue e => e.Ordinal,
        _ => null,
    };

    /// <summary>The set element type an ordinal value stands for.</summary>
    private static PdiType? ElementType(Value ordinal) => ordinal switch
    {
        IntValue => SyntheticTypes.Integer,
        CharValue => SyntheticTypes.Char,
        BoolValue => SyntheticTypes.Boolean,
        EnumValue e => e.Type,
        _ => null,
    };

    private static PdiType Root(PdiType type)
    {
        for (var depth = 0; type.AliasOf is { } target && depth < 16; depth++)
            type = target;
        return type;
    }

    public static bool SameType(PdiType left, PdiType right) => ReferenceEquals(Root(left), Root(right));

    /// <summary>Same base for sets: the same enum, or the same ordinal kind; the empty constructor (no element type) fits every set.</summary>
    private static bool SameElement(PdiType? left, PdiType? right) =>
        left is null || right is null
        || (left.Kind == PdiTypeKind.Enum || right.Kind == PdiTypeKind.Enum ? SameType(left, right) : left.Kind == right.Kind);

    // ---- set constructor ----

    private async Task<Value> SetAsync(Ast.SetCtor set, CancellationToken ct)
    {
        PdiType? element = null;
        var members = BigInteger.Zero;
        foreach (var item in set.Items)
        {
            var low = await EvaluateValueAsync(item.Low, ct).ConfigureAwait(false);
            var high = item.High is null ? low : await EvaluateValueAsync(item.High, ct).ConfigureAwait(false);
            foreach (var bound in new[] { low, high })
            {
                if (ElementType(bound) is not { } type)
                    throw Error(Messages.OperatorNotApplicable("[]", bound.TypeName));
                if (element is not null && !SameElement(element, type))
                    throw Error(Messages.SetElementsDiffer);
                element ??= type;
            }

            var (first, last) = (Ordinal(low)!.Value, Ordinal(high)!.Value);
            if (first < 0 || first > 255 || last < 0 || last > 255)
                throw Error(Messages.SetElementOutOfRange);
            for (var ordinal = first; ordinal <= last; ordinal++)
                members |= BigInteger.One << (int)ordinal;
        }

        return new SetValue(element, members);
    }

    // ---- builtins (§9.5) ----

    private async Task<Value> BuiltinAsync(Ast.Call call, CancellationToken ct)
    {
        var name = call.Builtin;
        if (call.Arguments.Count != 1)
            throw Error(Messages.WrongArgumentCount(name));
        var argument = await EvaluateAsync(call.Arguments[0], ct).ConfigureAwait(false);
        DebuggerException NotApplicable() => Error(Messages.BuiltinNotApplicable(name, argument.TypeName));

        switch (name)
        {
            case "SizeOf":
                return argument switch
                {
                    TypeValue type => new IntValue(type.Type.Size),
                    LocationValue location => new IntValue(location.Type.Size),
                    _ => throw NotApplicable(),
                };

            case "Length":
                return new IntValue((await BoundsAsync(argument, lengthOnly: true, ct).ConfigureAwait(false) ?? throw NotApplicable()).Count);

            case "High" or "Low":
            {
                var bounds = await BoundsAsync(argument, lengthOnly: false, ct).ConfigureAwait(false) ?? throw NotApplicable();
                return name == "High" ? bounds.High : bounds.Low;
            }

            case "Assigned":
                switch (argument)
                {
                    case LocationValue { Type.Kind: PdiTypeKind.Pointer or PdiTypeKind.Class or PdiTypeKind.ClassRef or PdiTypeKind.DynArray
                        or PdiTypeKind.Procedural or PdiTypeKind.Interface } location:
                        return new BoolValue(await PointerAtAsync(location.Address, ct).ConfigureAwait(false) != 0);
                    case PtrValue or InstanceValue or NilValue:
                        return new BoolValue(Address(argument) != 0);
                    default:
                        throw NotApplicable();
                }
        }

        var value = await ReadAsync(argument, ct).ConfigureAwait(false);
        switch (name, value)
        {
            case ("Ord", _) when Ordinal(value) is { } ordinal:
                return new IntValue(ordinal, value is IntValue { Unsigned: true });
            case ("Chr", IntValue i):
                return i.Number >= 0 && i.Number <= char.MaxValue ? new CharValue((char)i.Bits) : throw Error(Messages.ValueOutOfRange("Char"));
            case ("Abs", IntValue i):
                return i.Unsigned || i.Bits >= 0 ? i : new IntValue(unchecked(-i.Bits));
            case ("Abs", FloatValue f):
                return new FloatValue(Math.Abs(f.Number));
            case ("Odd", IntValue i):
                return new BoolValue((i.Bits & 1) != 0);
            case ("Succ" or "Pred", IntValue i):
                return new IntValue(unchecked(i.Bits + Step(name)), i.Unsigned);
            case ("Succ" or "Pred", CharValue c):
                return new CharValue(unchecked((char)(c.Unit + Step(name))));
            case ("Succ" or "Pred", EnumValue e):
                return new EnumValue(e.Type, e.Ordinal + Step(name));
            default:
                throw NotApplicable();
        }

        static int Step(string builtin) => builtin == "Succ" ? 1 : -1;
    }

    private sealed record Bounds(Value Low, Value High, long Count);

    /// <summary>Low, High and Length of an array or string value; without <paramref name="lengthOnly"/> also of an ordinal type or value.</summary>
    private async Task<Bounds?> BoundsAsync(Value argument, bool lengthOnly, CancellationToken ct)
    {
        static Bounds Range(long low, long high) => new(new IntValue(low), new IntValue(high), high - low + 1);

        switch (argument)
        {
            case LocationValue { Type.Kind: PdiTypeKind.StaticArray } array:
            {
                var type = array.Type;
                var (low, high) = (type.ArrayLow, type.ArrayLow + type.Count - 1);
                return type.IndexType is { Kind: PdiTypeKind.Enum or PdiTypeKind.Char or PdiTypeKind.Boolean } index
                    ? new Bounds(OfOrdinal(index, low), OfOrdinal(index, high), type.Count)
                    : Range(low, high);
            }

            case LocationValue { Type.Kind: PdiTypeKind.DynArray or PdiTypeKind.OpenArray } array:
                return Range(0, (await ArrayPayloadAsync(array, ct).ConfigureAwait(false)).Length - 1);
            case LocationValue { Type.Kind: PdiTypeKind.String } or StrValue or CharValue:
                return Range(1, Text(await ReadAsync(argument, ct).ConfigureAwait(false))!.Length);
        }

        if (lengthOnly)
            return null;
        var ordinalType = argument switch
        {
            TypeValue type => type.Type,
            LocationValue location => location.Type,
            IntValue i => i.FitsInt32 ? SyntheticTypes.Integer : i.Unsigned ? SyntheticTypes.UInt64 : SyntheticTypes.Int64,
            CharValue => SyntheticTypes.Char,
            BoolValue => SyntheticTypes.Boolean,
            EnumValue e => e.Type,
            _ => null,
        };
        return ordinalType is null ? null : TypeBounds(ordinalType);
    }

    private static Bounds? TypeBounds(PdiType type)
    {
        switch (type.Kind)
        {
            case PdiTypeKind.Integer when type is { Low: { } low, High: { } high }:
                return new Bounds(new IntValue(low), new IntValue(high), 0);
            case PdiTypeKind.Integer when type.Size is 1 or 2 or 4 or 8:
            {
                var (low, high) = SizeBounds(type);
                return new Bounds(new IntValue((long)low), new IntValue(unchecked((long)(ulong)high), Unsigned: high > long.MaxValue), 0);
            }

            case PdiTypeKind.Enum when type.Values.Count > 0:
                return new Bounds(new EnumValue(type, type.Values[0].Value), new EnumValue(type, type.Values[^1].Value), 0);
            case PdiTypeKind.Char:
                return new Bounds(new CharValue('\0'), new CharValue(type.Size == 1 ? (char)255 : char.MaxValue), 0);
            case PdiTypeKind.Boolean:
                return new Bounds(new BoolValue(false), new BoolValue(true), 0);
            default:
                return null;
        }
    }

    /// <summary>The range of an integer type by its size and signedness.</summary>
    private static (Int128 Low, Int128 High) SizeBounds(PdiType type)
    {
        var bits = Math.Clamp(type.Size, 1, 8) * 8;
        return type.Signed
            ? (long.MinValue >> (64 - bits), long.MaxValue >> (64 - bits))
            : (0, ulong.MaxValue >> (64 - bits));
    }

    /// <summary>An ordinal as a value of an ordinal type.</summary>
    private static Value OfOrdinal(PdiType type, long ordinal) => type.Kind switch
    {
        PdiTypeKind.Enum => new EnumValue(type, ordinal),
        PdiTypeKind.Char => new CharValue(unchecked((char)ordinal)),
        PdiTypeKind.Boolean => new BoolValue(ordinal != 0),
        _ => new IntValue(ordinal),
    };

    // ---- typecasts (§9.4) ----

    private async Task<Value> CastAsync(Ast.Cast cast, CancellationToken ct)
    {
        var target = FindType(_pdi, cast.TypeName) ?? throw Error(Messages.UndeclaredIdentifier(cast.TypeName));
        var operand = await EvaluateAsync(cast.Operand, ct).ConfigureAwait(false);
        switch (target.Kind)
        {
            case PdiTypeKind.Integer or PdiTypeKind.Enum or PdiTypeKind.Char or PdiTypeKind.Boolean:
            {
                // A value cast: ordinal to ordinal.
                if (Ordinal(await ReadAsync(operand, ct).ConfigureAwait(false)) is not { } ordinal)
                    throw Error(Messages.InvalidTypecast);
                return target.Kind == PdiTypeKind.Integer ? new IntValue(Truncate(ordinal, target), !target.Signed && target.Size == 8) : OfOrdinal(target, ordinal);
            }

            case PdiTypeKind.Pointer or PdiTypeKind.Class:
            {
                // A pointer-like location keeps its storage; anything else is taken by value, unchecked.
                if (operand is LocationValue { Type.Kind: PdiTypeKind.Pointer or PdiTypeKind.Class } location)
                    return location with { Type = target };
                var value = await ReadAsync(operand, ct).ConfigureAwait(false);
                var address = Address(value) ?? (value is IntValue i ? unchecked((ulong)i.Bits) : throw Error(Messages.InvalidTypecast));
                return target.Kind == PdiTypeKind.Pointer ? new PtrValue(address, target.To)
                    : address == 0 ? new NilValue()
                    : new InstanceValue(address, target, target.Name);
            }

            default:
                // Reinterpreting storage: never across sizes.
                return operand is LocationValue storage && storage.Type.Size == target.Size
                    ? storage with { Type = target, OpenArrayHigh = null }
                    : throw Error(Messages.InvalidTypecast);
        }
    }

    /// <summary>The low bytes of an ordinal as a value of an integer type (two's complement).</summary>
    private static long Truncate(long ordinal, PdiType type)
    {
        var bits = Math.Clamp(type.Size, 1, 8) * 8;
        if (bits == 64)
            return ordinal;
        return type.Signed ? ordinal << (64 - bits) >> (64 - bits) : (long)((ulong)ordinal << (64 - bits) >> (64 - bits));
    }

    // ---- assignment conversion (§8.5) ----

    /// <summary>True for the kinds a value can be assigned to (DBG-176).</summary>
    public static bool IsModifiable(PdiType type) =>
        type.Kind is PdiTypeKind.Integer or PdiTypeKind.Float or PdiTypeKind.Boolean or PdiTypeKind.Char or PdiTypeKind.Enum or PdiTypeKind.Pointer;

    /// <summary>The little-endian storage bytes of <paramref name="value"/> converted to <paramref name="target"/> by assignment compatibility.</summary>
    public static byte[] ConvertForAssignment(Value value, PdiType target)
    {
        var bytes = new byte[target.Kind == PdiTypeKind.Pointer ? 8 : target.Size];
        var typeName = ValueFormatter.TypeName(target);
        switch (target.Kind, value)
        {
            case (PdiTypeKind.Integer, IntValue i) when target.Size is 1 or 2 or 4 or 8:
            {
                var (low, high) = SizeBounds(target);
                if (i.Number < low || i.Number > high)
                    throw Error(Messages.ValueOutOfRange(typeName));
                Write(bytes, unchecked((ulong)i.Bits));
                return bytes;
            }

            case (PdiTypeKind.Float, IntValue or FloatValue):
            {
                var number = Number(value)!.Value;
                switch (target.FloatFormat)
                {
                    case PdiFloatFormat.Single when bytes.Length == 4:
                        BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)number);
                        return bytes;
                    case PdiFloatFormat.Currency when bytes.Length == 8:
                        if (double.IsNaN(number) || Math.Abs(number) > long.MaxValue / 10000.0)
                            throw Error(Messages.ValueOutOfRange(typeName));
                        BinaryPrimitives.WriteInt64LittleEndian(bytes, (long)Math.Round(number * 10000.0, MidpointRounding.ToEven));
                        return bytes;
                    case PdiFloatFormat.Double or PdiFloatFormat.DateTime when bytes.Length == 8:
                        BinaryPrimitives.WriteDoubleLittleEndian(bytes, number);
                        return bytes;
                }

                break;
            }

            case (PdiTypeKind.Enum, EnumValue e) when SameType(e.Type, target):
                Write(bytes, unchecked((ulong)e.Ordinal));
                return bytes;
            case (PdiTypeKind.Boolean, BoolValue b):
                Write(bytes, b.Flag ? 1ul : 0);
                return bytes;
            case (PdiTypeKind.Char, CharValue or StrValue { Text.Length: 1 }):
            {
                var unit = Text(value)![0];
                if (target.Size == 1 && unit > 255)
                    throw Error(Messages.ValueOutOfRange(typeName));
                Write(bytes, unit);
                return bytes;
            }

            case (PdiTypeKind.Pointer, NilValue or PtrValue):
                Write(bytes, Address(value)!.Value);
                return bytes;
            case (PdiTypeKind.Pointer, IntValue i):
                Write(bytes, unchecked((ulong)i.Bits));
                return bytes;
        }

        throw Error(Messages.IncompatibleTypes(value.TypeName, typeName));

        static void Write(byte[] storage, ulong bits)
        {
            for (var i = 0; i < storage.Length; i++)
                storage[i] = i < 8 ? (byte)(bits >> (8 * i)) : (byte)0;
        }
    }
}
