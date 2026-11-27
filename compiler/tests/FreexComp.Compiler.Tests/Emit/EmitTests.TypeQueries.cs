using System.Globalization;
using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-416 (CMP-1474): <c>GetTypeKind</c> and the RTTI emitter share one type → <c>Kind</c> mapping.</summary>
public sealed partial class EmitTests
{
    private const string KindTypes = """
        program Kinds;
        type
          TEnum = (eA, eB, eC);
          TEnumSub = eA..eB;
          TIntSub = 1..10;
          TBigSub = 0..$FFFFFFFFFF;
          TSmall64 = Int64(1)..Int64(10);
          TWideSub = 'a'..'z';
          TSet = set of TEnum;
          TCls = class
          end;
          TClsRef = class of TCls;
          TMeth = procedure(Sender: TObject) of object;
          TProc = procedure;
          TFunc = function(X: Integer): Integer;
          TRef = reference to procedure;
          IIntf = interface
          end;
          TShort = string[10];
          TRec = record A: Integer; end;
          TRecS = record S: string; end;
          TArr = array[0..3] of Integer;
          TArrS = array[0..1] of string;
          TDyn = array of Integer;
          PInt = ^Integer;
          TBox<X> = record V: X; end;
        var
          P: Pointer;
          K: Integer;
        begin
        {BODY}end.

        """;

    /// <summary>One type for every row of the LANG-974 table that has an RTTI block (<c>Text</c>, <c>tkUnknown</c>, has none: E2002).</summary>
    private static readonly string[] KindOperands =
    [
        "Integer", "ShortInt", "Byte", "Word", "Cardinal", "TIntSub", "TSmall64",               // tkInteger
        "AnsiChar",                                                                             // tkChar
        "TEnum", "TEnumSub", "Boolean", "ByteBool", "WordBool", "LongBool",                    // tkEnumeration
        "Single", "Double", "Currency",                                                         // tkFloat
        "TSet",                                                                                 // tkSet
        "TCls",                                                                                 // tkClass
        "TMeth",                                                                                // tkMethod
        "Char", "TWideSub",                                                                     // tkWChar
        "string",                                                                               // tkUString
        "AnsiString", "UTF8String", "RawByteString",                                            // tkAString
        "ShortString", "TShort",                                                                // tkSString
        "TRec", "TRecS", "TBox<Integer>", "TBox<string>",                                       // tkRecord
        "TArr", "TArrS",                                                                        // tkArray
        "TDyn", "TArray<Byte>",                                                                 // tkDynArray
        "IIntf", "IInterface", "TRef",                                                          // tkInterface
        "Int64", "UInt64", "NativeInt", "NativeUInt", "TBigSub",                                // tkInt64
        "Pointer", "PInt", "PChar", "PAnsiChar",                                                // tkPointer
        "TClsRef", "TClass",                                                                    // tkClassRef
        "TProc", "TFunc",                                                                       // tkProcedure
    ];

    /// <summary>
    /// For every type: the ordinal <c>GetTypeKind(T)</c> folds to (the constant stored into <c>K</c>) is the first byte of the block
    /// <c>TypeInfo(T)</c> names (the address stored into <c>P</c>) — and the list reaches every kind from 1 to 19.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1474")]
    [Trait("Req", "LANG-974")]
    public void GetTypeKind_EqualsTheEmittedKindByte()
    {
        var body = string.Concat(KindOperands.Select(t => $"  P := TypeInfo({t});\n  K := Ord(GetTypeKind({t}));\n"));
        var ir = EmitProgramIr("Kinds", KindTypes.ReplaceLineEndings("\n").Replace("{BODY}", body));

        var stores = Regex.Matches(ir, @"store ptr @(""[^""\n]+""), ptr @""Kinds\.P""[^\n]*\n  store i32 (\d+), ptr @""Kinds\.K""");
        Assert.Equal(KindOperands.Length, stores.Count);
        var seen = new SortedSet<int>();
        for (var i = 0; i < stores.Count; i++)
        {
            var (block, folded) = (stores[i].Groups[1].Value, int.Parse(stores[i].Groups[2].Value, CultureInfo.InvariantCulture));
            Assert.Contains("$rtti", block, StringComparison.Ordinal);
            // "@<block> = [linkonce_odr] constant { i8, [7 x i8], ptr, … } { i8 <Kind>, [7 x i8] zeroinitializer, …"
            var definition = Regex.Match(ir, "^@" + Regex.Escape(block) + @" = [^\n]*? constant \{ i8, \[7 x i8\], [^\n]*?\} \{ i8 (\d+), \[7 x i8\] zeroinitializer", RegexOptions.Multiline);
            Assert.True(definition.Success, $"{KindOperands[i]}: no definition of {block}");
            Assert.True(folded == int.Parse(definition.Groups[1].Value, CultureInfo.InvariantCulture),
                $"{KindOperands[i]}: GetTypeKind folds to {folded}, the block {block} starts with {definition.Groups[1].Value}");
            seen.Add(folded);
        }

        Assert.Equal(Enumerable.Range(1, 19), seen);
    }
}
