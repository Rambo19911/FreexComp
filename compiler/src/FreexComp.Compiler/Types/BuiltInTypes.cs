using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Types;

/// <summary>
/// The predeclared types and constants of LANG-200/201 and the intrinsics (LANG-900), built into the compiler, and the outermost
/// <see cref="ScopeKind.BuiltIn"/> scope that declares them (below unit <c>System</c>; units may shadow them).
/// </summary>
public sealed class BuiltInTypes
{
    /// <summary>The compilation's RTTI scope (<c>--rtti</c>, LANG-648) for classes without a <c>{$RTTI}</c> directive (C-169).</summary>
    internal RttiScope DefaultRttiScope { get; init; } = RttiScope.Broad;

    internal BuiltInTypes()
    {
        Scope = new Scope(ScopeKind.BuiltIn, parent: null);

        ShortInt = Declare(new IntegerTypeSymbol("ShortInt", 8, isSigned: true));
        SmallInt = Declare(new IntegerTypeSymbol("SmallInt", 16, isSigned: true));
        Integer = Declare(new IntegerTypeSymbol("Integer", 32, isSigned: true));
        Alias("LongInt", Integer);
        Int64 = Declare(new IntegerTypeSymbol("Int64", 64, isSigned: true));
        NativeInt = Declare(new IntegerTypeSymbol("NativeInt", 64, isSigned: true));
        Byte = Declare(new IntegerTypeSymbol("Byte", 8, isSigned: false));
        Word = Declare(new IntegerTypeSymbol("Word", 16, isSigned: false));
        Cardinal = Declare(new IntegerTypeSymbol("Cardinal", 32, isSigned: false));
        Alias("LongWord", Cardinal);
        UInt64 = Declare(new IntegerTypeSymbol("UInt64", 64, isSigned: false));
        NativeUInt = Declare(new IntegerTypeSymbol("NativeUInt", 64, isSigned: false));

        Boolean = Declare(new BooleanTypeSymbol("Boolean", 1, isCStyle: false));
        ByteBool = Declare(new BooleanTypeSymbol("ByteBool", 1, isCStyle: true));
        WordBool = Declare(new BooleanTypeSymbol("WordBool", 2, isCStyle: true));
        LongBool = Declare(new BooleanTypeSymbol("LongBool", 4, isCStyle: true));

        Char = Declare(new CharTypeSymbol("Char", isWide: true));
        Alias("WideChar", Char);
        AnsiChar = Declare(new CharTypeSymbol("AnsiChar", isWide: false));

        Single = Declare(new RealTypeSymbol("Single", RealKind.Single));
        Double = Declare(new RealTypeSymbol("Double", RealKind.Double));
        Alias("Real", Double);
        Alias("Extended", Double);
        Currency = Declare(new RealTypeSymbol("Currency", RealKind.Currency));

        String = Declare(new StringTypeSymbol("string", StringVariant.Unicode, codePage: 1200));
        Alias("UnicodeString", String);
        AnsiString = Declare(new StringTypeSymbol("AnsiString", StringVariant.Ansi, codePage: 65001));
        Utf8String = Declare(new StringTypeSymbol("UTF8String", StringVariant.Ansi, codePage: 65001));
        RawByteString = Declare(new StringTypeSymbol("RawByteString", StringVariant.Ansi, codePage: 0xFFFF));
        ShortString = Declare(new StringTypeSymbol("ShortString", StringVariant.Short, codePage: 65001, maxLength: 255));

        Pointer = Declare(new PointerTypeSymbol("Pointer", pointedType: null, allowsPointerMath: false));
        PChar = Declare(new PointerTypeSymbol("PChar", Char, allowsPointerMath: true));
        Alias("PWideChar", PChar);
        PAnsiChar = Declare(new PointerTypeSymbol("PAnsiChar", AnsiChar, allowsPointerMath: true));

        Text = Declare(new TextTypeSymbol("Text"));
        Alias("TextFile", Text);

        Constant("True", Boolean, true);
        Constant("False", Boolean, false);
        Constant("MaxInt", Integer, 2147483647);
        Constant("MaxLongInt", Integer, 2147483647);
        Constant("Pi", Double, 3.14159265358979323846);

        Intrinsic("Break", IntrinsicKind.Break, "Break");
        Intrinsic("Continue", IntrinsicKind.Continue, "Continue");
        Intrinsic("Exit", IntrinsicKind.Exit, "Exit[(Value)]");
        Intrinsic("Halt", IntrinsicKind.Halt, "Halt[(ExitCode: Integer)]");
        Intrinsic("Inc", IntrinsicKind.Inc, "Inc(var X[; N: Integer])");
        Intrinsic("Dec", IntrinsicKind.Dec, "Dec(var X[; N: Integer])");
        Intrinsic("RunError", IntrinsicKind.RunError, "RunError[(ErrorCode: Integer)]");
        Intrinsic("Write", IntrinsicKind.Write, "Write([var F: Text;] Args)");
        Intrinsic("Writeln", IntrinsicKind.Writeln, "Writeln([var F: Text;] Args)");
        Intrinsic("Include", IntrinsicKind.Include, "Include(var S: Set; Element)");
        Intrinsic("Exclude", IntrinsicKind.Exclude, "Exclude(var S: Set; Element)");
        Intrinsic("Succ", IntrinsicKind.Succ, "Succ(X: Ordinal): Ordinal");
        Intrinsic("Pred", IntrinsicKind.Pred, "Pred(X: Ordinal): Ordinal");
        Intrinsic("Ord", IntrinsicKind.Ord, "Ord(X: Ordinal): Integer");
        Intrinsic("Chr", IntrinsicKind.Chr, "Chr(X: Integer): Char");
        Intrinsic("Odd", IntrinsicKind.Odd, "Odd(X: Integer): Boolean");
        Intrinsic("Abs", IntrinsicKind.Abs, "Abs(X: Number): Number");
        Intrinsic("Sqr", IntrinsicKind.Sqr, "Sqr(X: Number): Number");
        Intrinsic("Low", IntrinsicKind.Low, "Low(X: Type or Ordinal)");
        Intrinsic("High", IntrinsicKind.High, "High(X: Type or Ordinal)");
        Intrinsic("SizeOf", IntrinsicKind.SizeOf, "SizeOf(X: Type or Variable): Integer");
        Intrinsic("Lo", IntrinsicKind.Lo, "Lo(X: Integer): Byte");
        Intrinsic("Hi", IntrinsicKind.Hi, "Hi(X: Integer): Byte");
        Intrinsic("Swap", IntrinsicKind.Swap, "Swap(X: Integer): Word");
        Intrinsic("Length", IntrinsicKind.Length, "Length(X: String or Array): Integer");
        Intrinsic("New", IntrinsicKind.New, "New(var P: Pointer)");
        Intrinsic("Dispose", IntrinsicKind.Dispose, "Dispose(var P: Pointer)");
        Intrinsic("GetMem", IntrinsicKind.GetMem, "GetMem(var P: Pointer; Size: NativeInt)");
        Intrinsic("FreeMem", IntrinsicKind.FreeMem, "FreeMem(P: Pointer)");
        Intrinsic("ReallocMem", IntrinsicKind.ReallocMem, "ReallocMem(var P: Pointer; Size: NativeInt)");
        Intrinsic("FillChar", IntrinsicKind.FillChar, "FillChar(var X; Count: NativeInt; Value: Ordinal)");
        Intrinsic("Move", IntrinsicKind.Move, "Move(const Source; var Dest; Count: NativeInt)");
        Intrinsic("Assert", IntrinsicKind.Assert, "Assert(Condition: Boolean[; Message: string])");
        Intrinsic("SetLength", IntrinsicKind.SetLength, "SetLength(var S: string; NewLength: Integer)");
        Intrinsic("Delete", IntrinsicKind.Delete, "Delete(var S: string; Index, Count: Integer)");
        Intrinsic("Insert", IntrinsicKind.Insert, "Insert(const Source: string; var S: string; Index: Integer)");
        Intrinsic("Str", IntrinsicKind.Str, "Str(X[:Width[:Decimals]]; var S: string)");
        Intrinsic("Val", IntrinsicKind.Val, "Val(const S: string; var V; var Code: Integer)");
        Intrinsic("Assigned", IntrinsicKind.Assigned, "Assigned(P: Pointer): Boolean");
        Intrinsic("Addr", IntrinsicKind.Addr, "Addr(X): Pointer");
        Intrinsic("Ptr", IntrinsicKind.Ptr, "Ptr(Address: NativeUInt): Pointer");
        Intrinsic("Trunc", IntrinsicKind.Trunc, "Trunc(X: Double): Int64");
        Intrinsic("Round", IntrinsicKind.Round, "Round(X: Double): Int64");
        Intrinsic("Int", IntrinsicKind.Int, "Int(X: Double): Double");
        Intrinsic("Frac", IntrinsicKind.Frac, "Frac(X: Double): Double");
        Intrinsic("Sqrt", IntrinsicKind.Sqrt, "Sqrt(X: Double): Double");
        Intrinsic("AtomicIncrement", IntrinsicKind.AtomicIncrement, "AtomicIncrement(var Target[; Increment]): Target");
        Intrinsic("AtomicDecrement", IntrinsicKind.AtomicDecrement, "AtomicDecrement(var Target[; Decrement]): Target");
        Intrinsic("AtomicExchange", IntrinsicKind.AtomicExchange, "AtomicExchange(var Target; Value): Target");
        Intrinsic("AtomicCmpExchange", IntrinsicKind.AtomicCmpExchange, "AtomicCmpExchange(var Target; NewValue, Comparand): Target");
        Intrinsic("Copy", IntrinsicKind.Copy, "Copy(S: string; Index[, Count]: Integer): string");
        Intrinsic("Pos", IntrinsicKind.Pos, "Pos(const SubStr, S: string[; Offset: Integer]): Integer");
        Intrinsic("Concat", IntrinsicKind.Concat, "Concat(S1, S2, ...: string): string");
        Intrinsic("UpCase", IntrinsicKind.UpCase, "UpCase(C: Char): Char");
        Intrinsic("Read", IntrinsicKind.Read, "Read([var F: Text;] var V1, ...)");
        Intrinsic("Readln", IntrinsicKind.Readln, "Readln([var F: Text;] var V1, ...)");
        Intrinsic("Eof", IntrinsicKind.Eof, "Eof[(var F: Text)]: Boolean");
        Intrinsic("Eoln", IntrinsicKind.Eoln, "Eoln[(var F: Text)]: Boolean");
        Intrinsic("Slice", IntrinsicKind.Slice, "Slice(const A: array; Count: Integer): array");
        Intrinsic("TypeInfo", IntrinsicKind.TypeInfo, "TypeInfo(T: Type): Pointer");
    }

    /// <summary>
    /// The outermost scope: every LANG-200 type name (aliases included), the LANG-201 constants and the intrinsics
    /// bound so far (LANG-900).
    /// </summary>
    internal Scope Scope { get; }

    public TypeSymbol Integer { get; }
    public TypeSymbol Cardinal { get; }
    public TypeSymbol Int64 { get; }
    public TypeSymbol UInt64 { get; }
    public TypeSymbol ShortInt { get; }
    public TypeSymbol SmallInt { get; }
    public TypeSymbol Byte { get; }
    public TypeSymbol Word { get; }
    public TypeSymbol NativeInt { get; }
    public TypeSymbol NativeUInt { get; }
    public TypeSymbol Boolean { get; }
    public TypeSymbol ByteBool { get; }
    public TypeSymbol WordBool { get; }
    public TypeSymbol LongBool { get; }
    public TypeSymbol Char { get; }
    public TypeSymbol AnsiChar { get; }
    public TypeSymbol Single { get; }
    public TypeSymbol Double { get; }
    public TypeSymbol Currency { get; }
    public TypeSymbol String { get; }
    public TypeSymbol AnsiString { get; }
    public TypeSymbol Utf8String { get; }
    public TypeSymbol RawByteString { get; }
    public TypeSymbol ShortString { get; }
    public TypeSymbol Pointer { get; }
    public TypeSymbol PChar { get; }
    public TypeSymbol PAnsiChar { get; }
    public TypeSymbol Text { get; }

    /// <summary>The type of <c>nil</c> (LANG-266): a pointer compatible with every pointer type (not declared in any scope).</summary>
    public PointerTypeSymbol Nil { get; } = new("nil", pointedType: null, allowsPointerMath: false);

    /// <summary>The type of erroneous expressions (not declared in any scope).</summary>
    public TypeSymbol Error { get; } = new ErrorTypeSymbol();

    /// <summary>The type of untyped <c>var</c>/<c>const</c>/<c>out</c> parameters (C-202; not declared in any scope).</summary>
    public TypeSymbol Untyped { get; } = new UntypedTypeSymbol();

    /// <summary>The type of the constructor <c>[]</c> without context: compatible with every set type (LANG-251; in no scope).</summary>
    public SetTypeSymbol EmptySet { get; } = new("[]", elementType: null, containingSymbol: null, locations: []);

    /// <summary>From unit <c>System</c>; null until System is bound or if it failed to load.</summary>
    public TypeSymbol? TObject { get; internal set; }

    public TypeSymbol? IInterface { get; internal set; }

    private readonly Dictionary<(TypeSymbol, bool), OpenArrayTypeSymbol> _openArrays = new();

    /// <summary>The open array type over <paramref name="element"/> (one per element type in this compilation, LANG-430; C-106).</summary>
    internal OpenArrayTypeSymbol OpenArrayOf(TypeSymbol element, bool isArrayOfConst = false)
    {
        lock (_openArrays)
        {
            if (!_openArrays.TryGetValue((element, isArrayOfConst), out var type))
            {
                _openArrays[(element, isArrayOfConst)] = type = new OpenArrayTypeSymbol(element, isArrayOfConst);
            }

            return type;
        }
    }

    private T Declare<T>(T type) where T : TypeSymbol
    {
        Scope.TryDeclare(type, out _);
        type.IsPredefined = true;
        return type;
    }

    private void Alias(string name, TypeSymbol type) => Scope.TryDeclare(name, type, out _);

    private void Constant(string name, TypeSymbol type, object value) =>
        Scope.TryDeclare(new ConstantSymbol(name, type, new ConstantValue(value, type), isTyped: false, containingSymbol: null, locations: []), out _);

    private void Intrinsic(string name, IntrinsicKind kind, string signature) =>
        Scope.TryDeclare(new IntrinsicSymbol(name, kind, signature), out _);
}
