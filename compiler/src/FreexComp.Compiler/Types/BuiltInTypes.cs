using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Types;

/// <summary>
/// The predeclared types and constants of LANG-200/201 and the intrinsics (LANG-900), built into the compiler, and the outermost
/// <see cref="ScopeKind.BuiltIn"/> scope that declares them (below unit <c>System</c>; units may shadow them).
/// </summary>
public sealed class BuiltInTypes
{
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

    /// <summary>The type of erroneous expressions (not declared in any scope).</summary>
    public TypeSymbol Error { get; } = new ErrorTypeSymbol();

    /// <summary>The type of the constructor <c>[]</c> without context: compatible with every set type (LANG-251; in no scope).</summary>
    public SetTypeSymbol EmptySet { get; } = new("[]", elementType: null, containingSymbol: null, locations: []);

    /// <summary>From unit <c>System</c>; null until System is bound or if it failed to load.</summary>
    public TypeSymbol? TObject { get; internal set; }

    public TypeSymbol? IInterface { get; internal set; }

    private T Declare<T>(T type) where T : TypeSymbol
    {
        Scope.TryDeclare(type, out _);
        return type;
    }

    private void Alias(string name, TypeSymbol type) => Scope.TryDeclare(name, type, out _);

    private void Constant(string name, TypeSymbol type, object value) =>
        Scope.TryDeclare(new ConstantSymbol(name, type, new ConstantValue(value, type), isTyped: false, containingSymbol: null, locations: []), out _);

    private void Intrinsic(string name, IntrinsicKind kind, string signature) =>
        Scope.TryDeclare(new IntrinsicSymbol(name, kind, signature), out _);
}
