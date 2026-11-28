using System.Collections.Frozen;

namespace FreexComp.Designer.Model;

/// <summary>
/// D-163: the identifiers unit <c>System</c> makes visible in every unit — the compiler's predeclared types, constants and
/// intrinsics and the interface of <c>rtl/src/System.pas</c>. A field of a form class with one of these names hides it in
/// every method of the form (<c>Exit: TMenuItem</c> turns <c>Exit;</c> into E2060), so a component name derived from a
/// text counts such a name as taken (UIF-727). Generated list: <c>SystemIdentifiersTests.ListIsTheCompilersSystemScope</c>
/// compares it with the symbols the compiler binds and prints the list to paste when they differ.
/// </summary>
public static class SystemIdentifiers
{
    private static readonly FrozenSet<string> Names = new[]
    {
        "_AbstractError", "_AddRef", "_AddRefArray", "_AllocZero", "_AsClass", "_Assert", "_AStrAddRef", "_AStrAsg",
        "_AStrCat", "_AStrClr", "_AStrCmp", "_AStrCopy", "_AStrFromPCharLen", "_AStrFromUStr", "_AStrSetLen", "_AStrUnique",
        "_BeginCatch", "_ClassInherits", "_CopyArray", "_CopyRecord", "_CurrentExcept", "_Dispatch", "_DispCall",
        "_DivZeroError", "_DllEntry", "_DllInitDone", "_DllInitFailed", "_DynArrayAddRef", "_DynArrayAsg", "_DynArrayCat",
        "_DynArrayClr", "_DynArrayCopy", "_DynArrayDelete", "_DynArrayInsert", "_DynArraySetLen", "_EndCatch", "_Eof",
        "_Eoln", "_Finalize", "_FinalizeArray", "_FloatDigitsPrecision", "_FloatDigitsShortest", "_Frac", "_FreeMem",
        "_GetMem", "_GetMXCSR", "_Halt", "_Halt0", "_InitUnits", "_Int", "_IntfAddRef", "_IntfAsg", "_IntfCast", "_IntfClr",
        "_InvalidCast", "_IOCheck", "_IsClass", "_LiveAllocCount", "_ObjSupports", "_ObjToIntf", "_OverflowError",
        "_PascalExceptionCode", "_Raise", "_RangeError", "_ReadEnd", "_ReadFloat", "_ReadInt", "_ReadLn", "_ReadUInt",
        "_ReadUStr", "_ReadWChar", "_ReallocMem", "_RegisterResources", "_ReRaise", "_ReturnAddress", "_Round", "_RunError",
        "_SafecallError", "_SafecallException", "_SehFilter", "_SetDiff", "_SetEq", "_SetInter", "_SetLe", "_SetMXCSR",
        "_SetRange", "_SetSymDiff", "_SetUnion", "_SStrAsg", "_SStrCmp", "_SStrFromUStr", "_Startup", "_StrFloat",
        "_StrInt", "_StrUInt", "_StrUStr", "_TlsBase", "_TlsRegister", "_Trunc", "_UnhandledException", "_UStrAddRef",
        "_UStrAsg", "_UStrCat", "_UStrCatN", "_UStrClr", "_UStrCmp", "_UStrCopy", "_UStrDelete", "_UStrEq", "_UStrFromAStr",
        "_UStrFromPWChar", "_UStrFromPWCharLen", "_UStrFromSStr", "_UStrFromWChar", "_UStrInsert", "_UStrNew", "_UStrPos",
        "_UStrSetLen", "_UStrUnique", "_ValFloat", "_ValInt", "_ValUInt", "_WriteAChar", "_WriteAStr", "_WriteBool",
        "_WriteEnd", "_WriteFloat", "_WriteInt", "_WriteLn", "_WritePWChar", "_WriteSStr", "_WriteUInt", "_WriteUStr",
        "_WriteWChar", "Abs", "AbstractErrorProc", "AcquireExceptionObject", "Addr", "AllocMem", "AnsiChar", "AnsiString",
        "Append", "ArcTan", "Assert", "AssertErrorProc", "Assign", "Assigned", "AssignFile", "AtomicCmpExchange",
        "AtomicDecrement", "AtomicExchange", "AtomicIncrement", "BeginThread", "Boolean", "Break", "Byte", "ByteBool",
        "Cardinal", "Char", "CheckMemoryManagerSwitch", "Chr", "Close", "CloseFile", "CmdLine", "Concat", "Continue",
        "Copy", "Cos", "CP_ACP", "CP_UTF16", "CP_UTF8", "Currency", "Dec", "Default", "DefaultSystemCodePage",
        "DefaultTextLineBreakStyle", "Delete", "DispCallProc", "Dispose", "Double", "E_NOINTERFACE", "E_NOTIMPL",
        "E_UNEXPECTED", "EndThread", "Eof", "Eoln", "Erase", "ErrorAddr", "ErrorProc", "ErrOutput", "ExceptAddr",
        "ExceptObject", "ExceptObjProc", "ExceptProc", "Exclude", "Exit", "ExitCode", "ExitProc", "Exp", "Extended",
        "False", "FillChar", "FindResourceData", "FloatDigitsMax", "Flush", "Frac", "FreeMem", "GetHeapUsage", "GetMem",
        "GetMemoryManager", "GetTypeKind", "Halt", "HasWeakRef", "Hi", "High", "HInstance", "HResult", "IDispatch",
        "IInterface", "Inc", "Include", "Input", "Insert", "Int", "Int64", "Integer", "IOResult", "IsConsole",
        "IsConstValue", "IsLibrary", "IsManagedType", "IsMemoryManagerSet", "IsMultiThread", "IUnknown", "Length", "Ln",
        "Lo", "LongBool", "LongInt", "LongWord", "Low", "MainThreadID", "MaxInt", "MaxLongInt", "memcmp", "memcpy",
        "memmove", "memset", "Move", "NativeInt", "NativeUInt", "New", "Odd", "Ord", "Output", "PAnsiChar", "PAnsiString",
        "ParamCount", "ParamStr", "PBoolean", "PByte", "PCardinal", "PChar", "PDateTime", "PDouble", "PGUID", "Pi",
        "PInt64", "PInteger", "PMethod", "PNativeInt", "PNativeUInt", "Pointer", "Pos", "PPAnsiChar", "PPChar", "PPointer",
        "Pred", "PShortString", "PSingle", "PString", "Ptr", "PUInt64", "PUnicodeString", "PVarRec", "PWideChar", "PWord",
        "RaiseList", "Random", "Randomize", "RandSeed", "RawByteString", "Read", "Readln", "Real", "ReallocMem",
        "RegisterExpectedMemoryLeak", "ReleaseExceptionObject", "Rename", "ReportMemoryLeaksOnShutdown", "Reset", "Rewrite",
        "Round", "RunError", "S_FALSE", "S_OK", "SafeCallErrorProc", "SeekEof", "SeekEoln", "SetInOutRes", "SetLength",
        "SetMemoryManager", "SetString", "SetTextCodePage", "ShortInt", "ShortString", "ShowErrorProc", "Sin", "Single",
        "SizeOf", "Slice", "sLineBreak", "SmallInt", "Sqr", "Sqrt", "Str", "string", "StringCodePage", "StringElementSize",
        "StringOfChar", "StringRefCount", "strlen", "Succ", "Swap", "TArray", "TBytes", "TClass", "TCustomAttribute",
        "TDate", "TDateTime", "Text", "TextFile", "TFloatDigits", "TGUID", "THandle", "THeapUsage", "TInterfacedObject",
        "tkArray", "tkAString", "tkChar", "tkClass", "tkClassRef", "tkDynArray", "tkEnumeration", "tkFloat", "tkInt64",
        "tkInteger", "tkInterface", "tkLString", "tkMethod", "tkPointer", "tkProcedure", "tkRecord", "tkSet", "tkSString",
        "tkString", "tkUnknown", "tkUString", "tkWChar", "tlbsCRLF", "tlbsLF", "TMemoryManager", "TMethod", "TObject",
        "TProcedure", "True", "Trunc", "TTextLineBreakStyle", "TThreadFunc", "TTime", "TTypeKind", "TVarRec", "TypeInfo",
        "UInt64", "UnicodeString", "UniqueString", "UnregisterExpectedMemoryLeak", "UpCase", "UTF8Decode", "UTF8Encode",
        "UTF8String", "UTF8ToString", "Val", "vtAnsiString", "vtBoolean", "vtChar", "vtClass", "vtCurrency", "vtExtended",
        "vtInt64", "vtInteger", "vtInterface", "vtObject", "vtPChar", "vtPointer", "vtPWideChar", "vtString",
        "vtUnicodeString", "vtVariant", "vtWideChar", "vtWideString", "wcslen", "WideChar", "Word", "WordBool", "Write",
        "Writeln",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name of the list.</summary>
    public static IReadOnlyCollection<string> All => Names;

    /// <summary>True when <paramref name="name"/> (case-insensitive) is an identifier of <c>System</c>.</summary>
    public static bool Contains(string name) => Names.Contains(name);
}
