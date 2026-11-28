#:property PublishAot=false
// GenCharTables - dev-time generator of the character tables (other-units.md section 5, RTL-640; RTL-1277): one run
// writes rtl/src/CharTables.inc (general categories, included by unit Character) and rtl/src/CaseTables.inc (simple
// case mappings, included by unit SysUtils).
// Run from the repository root:  dotnet run rtl/tools/GenCharTables.cs
// (optional arguments: the data file, then the output directory).
// Source data: UnicodeData.txt of the Unicode Character Database, the pinned build input third_party/unicode
// (field 2 = general category, fields 12 and 13 = simple uppercase and lowercase mapping). The file carries no version,
// so the version and the SHA-256 of the pinned file are constants here and the generator refuses any other file: when
// the pin moves, change both, run the generator and put the printed checksums into rtl/tests/other/Char_Classify.pas
// and rtl/tests/other/Char_Supplementary.pas.
// Nothing else is read (no globalization data of the runtime, no network), so the output depends on the data file only.
// All code points U+0000 .. U+10FFFF are tabulated. The BMP (UTF-16 code units) is two-level: a block index per
// 2^Shift code units and the distinct blocks; case mappings are stored as indexes into a small table of deltas. The
// supplementary planes are runs, never one entry per code point: the general categories as one sorted array of
// "first code point shl 5 or category" (a run lasts up to the next entry; code points without a line are
// unassigned), the case mappings as runs of consecutive code points with the same delta. A simple case mapping never
// crosses the border of the BMP (RTL-1277 (d)); the generator refuses data where one does. The generator also prints
// checksums that the Char_Classify and Char_Supplementary tests compare with a scan of the generated lookup functions.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

const string UnicodeVersion = "18.0.0";
const string UnicodeDataSha256 = "0736451de439ae7baf1425136617da495e09ee5afbe6e394374db7009ea08950";
const int Shift = 6;
const int BlockSize = 1 << Shift;
const int Unassigned = 29;
const int CategoryBits = 5;
const int Bmp = 0x10000;
const int CodeSpace = 0x110000;

// the two-letter general categories in TUnicodeCategory order (unit Character)
List<string> categoryCodes =
[
    "Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co",
    "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "Sm", "Sc", "Sk", "So", "Cn",
];

var input = args.Length > 0 ? args[0] : "third_party/unicode/UnicodeData.txt";
var outputDirectory = args.Length > 1 ? args[1] : "rtl/src";

var data = File.ReadAllBytes(input);
var hash = Convert.ToHexStringLower(SHA256.HashData(data));
if (hash != UnicodeDataSha256)
{
    Console.Error.WriteLine($"{input}: SHA-256 {hash} is not the pinned UnicodeData.txt {UnicodeVersion} ({UnicodeDataSha256})");
    return 1;
}

// code points without a line are unassigned (Cn) and map to themselves
var category = Enumerable.Repeat(Unassigned, CodeSpace).ToArray();
var upper = new int[CodeSpace];
var lower = new int[CodeSpace];
var rangeFirst = -1;
var lineNumber = 0;
foreach (var line in Encoding.ASCII.GetString(data).Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
    lineNumber++;
    var fields = line.TrimEnd('\r').Split(';');
    if (fields.Length != 15)
    {
        throw new InvalidDataException($"{input}({lineNumber}): {fields.Length} fields");
    }

    var code = int.Parse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    if (code >= CodeSpace)
    {
        throw new InvalidDataException($"{input}({lineNumber}): code point beyond U+10FFFF");
    }

    var kind = categoryCodes.IndexOf(fields[2]);
    if (kind < 0 || kind == Unassigned)
    {
        throw new InvalidDataException($"{input}({lineNumber}): general category '{fields[2]}'");
    }

    // a range is two lines: "<Name, First>" and "<Name, Last>", every code point between them has the same category
    if (fields[1].EndsWith(", First>", StringComparison.Ordinal))
    {
        rangeFirst = code;
        continue;
    }

    var first = code;
    if (fields[1].EndsWith(", Last>", StringComparison.Ordinal))
    {
        first = rangeFirst;
    }

    if (first < 0 || first > code)
    {
        throw new InvalidDataException($"{input}({lineNumber}): range without its first line");
    }

    rangeFirst = -1;
    for (var c = first; c <= code; c++)
    {
        category[c] = kind;
    }

    upper[code] = Mapping(fields[12]) - code;
    lower[code] = Mapping(fields[13]) - code;

    int Mapping(string field)
    {
        if (field.Length == 0)
        {
            return code;
        }

        var target = int.Parse(field, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return target < Bmp == code < Bmp
            ? target
            : throw new InvalidDataException($"{input}({lineNumber}): case mapping crosses the border of the BMP");
    }
}

var text = new StringBuilder();
Header(
    "Unicode general categories (TUnicodeCategory order), for unit Character. The BMP: two-level tables with blocks of "
    + BlockSize + " code units. U+10000 .. U+10FFFF: CatRuns, sorted, each entry = first code point shl "
    + CategoryBits + " or category; a run lasts up to the next entry, the last one up to U+10FFFF.",
    "CharBlockShift");
Emit("Cat", category, null);
var catRuns = new List<int>();
for (var c = Bmp; c < CodeSpace; c++)
{
    if (c == Bmp || category[c] != category[c - 1])
    {
        catRuns.Add(c << CategoryBits | category[c]);
    }
}

Constant("CatRunCount", catRuns.Count);
Array("CatRuns", "Cardinal", catRuns, hex: true);
var catBytes = Write("CharTables.inc");

Header(
    "Simple upper- and lower-case mappings, for unit SysUtils. The BMP: two-level tables with blocks of " + BlockSize
    + " code units, values are indexes into the deltas. U+10000 .. U+10FFFF: runs of consecutive code points with the"
    + " same delta (First .. Last, sorted); a code point outside every run maps to itself.",
    "CaseBlockShift");
Emit("Upper", upper, "UpperDelta");
Emit("Lower", lower, "LowerDelta");
var upperRuns = Runs("Upper", upper);
var lowerRuns = Runs("Lower", lower);
var caseBytes = Write("CaseTables.inc");

Console.WriteLine($"category checksum {Checksum(category, 0, Bmp)}");
Console.WriteLine($"upper checksum {Checksum(upper.Select((d, c) => c + d).ToArray(), 0, Bmp)}");
Console.WriteLine($"lower checksum {Checksum(lower.Select((d, c) => c + d).ToArray(), 0, Bmp)}");
Console.WriteLine($"supplementary category checksum {Checksum(category, Bmp, CodeSpace)}");
Console.WriteLine($"supplementary runs: {catRuns.Count} category ({catRuns.Count * 4} bytes of table data), {upperRuns} upper and {lowerRuns} lower case ({(upperRuns + lowerRuns) * 12} bytes)");
Console.WriteLine($"file sizes: CharTables.inc {catBytes} bytes, CaseTables.inc {caseBytes} bytes");
return 0;

// the licence line, the origin and licence of the numbers, the header comment and the block-size constant of one file
void Header(string content, string shiftName)
{
    text.Append("// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception\n");
    // the file and its layout are ours (line above); the numbers come from the data file, whose origin and licence are named here
    text.Append("// The table values are derived from the Unicode Character Database ").Append(UnicodeVersion)
        .Append(" (UnicodeData.txt), Copyright (c) Unicode, Inc., used under the Unicode License V3 (Unicode-3.0); see THIRD-PARTY-NOTICES.txt.\n");
    var comment = "{ Generated by rtl/tools/GenCharTables.cs - do not edit. " + content;
    var lineText = new StringBuilder();
    foreach (var word in comment.Split(' '))
    {
        if (lineText.Length > 0 && lineText.Length + 1 + word.Length > 118)
        {
            text.Append(lineText).Append('\n');
            lineText.Clear().Append(' ');
        }

        lineText.Append(lineText.Length > 0 ? " " : "").Append(word);
    }

    text.Append(lineText).Append('\n');
    text.Append("  Source: UnicodeData.txt of the Unicode Character Database ").Append(UnicodeVersion).Append(",\n");
    text.Append("  SHA-256 ").Append(UnicodeDataSha256).Append(". }\n\n");
    text.Append("const\n");
    Constant(shiftName, Shift);
}

void Constant(string name, int value)
{
    text.Append("  ").Append(name).Append(" = ").Append(value.ToString(CultureInfo.InvariantCulture)).Append(";\n\n");
}

int Write(string name)
{
    var path = Path.Combine(outputDirectory, name);
    var content = text.ToString().Replace("\r\n", "\n");
    File.WriteAllText(path, content, new UTF8Encoding(false));
    text.Clear();
    Console.WriteLine($"wrote {path}");
    return Encoding.UTF8.GetByteCount(content);
}

// sum over the code points first .. limit - 1 of value * (c mod 251 + 1), modulo 2^32 (the tests compute the same)
static uint Checksum(int[] values, int first, int limit)
{
    uint sum = 0;
    for (var c = first; c < limit; c++)
    {
        unchecked { sum += (uint)values[c] * (uint)(c % 251 + 1); }
    }

    return sum;
}

// the BMP part of one table: two levels, through a table of deltas when deltaName is given
void Emit(string name, int[] all, string? deltaName)
{
    var values = all[..Bmp];
    int[] codes = values;
    if (deltaName is not null)
    {
        var deltas = values.Distinct().OrderBy(d => d == 0 ? int.MinValue : d).ToList();
        if (deltas.Count > 256)
        {
            throw new InvalidOperationException($"{name}: {deltas.Count} distinct deltas");
        }

        codes = values.Select(v => deltas.IndexOf(v)).ToArray();
        Array(deltaName + "s", "Integer", deltas);
    }

    var blocks = new List<int[]>();
    var index = new List<int>();
    for (var b = 0; b < Bmp / BlockSize; b++)
    {
        var block = codes.Skip(b * BlockSize).Take(BlockSize).ToArray();
        var found = blocks.FindIndex(x => x.SequenceEqual(block));
        if (found < 0)
        {
            found = blocks.Count;
            blocks.Add(block);
        }

        index.Add(found);
    }

    if (blocks.Count > 256)
    {
        throw new InvalidOperationException($"{name}: {blocks.Count} distinct blocks");
    }

    Array(name + "BlockIndex", "Byte", index);
    Array(name + "Blocks", "Byte", blocks.SelectMany(x => x).ToList());
}

// the supplementary part of one case table: maximal runs of consecutive code points with the same non-zero delta
int Runs(string name, int[] deltas)
{
    var first = new List<int>();
    var last = new List<int>();
    var delta = new List<int>();
    for (var c = Bmp; c < CodeSpace; c++)
    {
        if (deltas[c] == 0)
        {
            continue;
        }

        if (first.Count > 0 && last[^1] == c - 1 && delta[^1] == deltas[c])
        {
            last[^1] = c;
        }
        else
        {
            first.Add(c);
            last.Add(c);
            delta.Add(deltas[c]);
        }
    }

    Constant(name + "RunCount", first.Count);
    Array(name + "RunFirst", "Cardinal", first, hex: true);
    Array(name + "RunLast", "Cardinal", last, hex: true);
    Array(name + "RunDelta", "Integer", delta);
    return first.Count;
}

void Array(string name, string type, IReadOnlyList<int> items, bool hex = false)
{
    text.Append("  ").Append(name).Append(": array[0..").Append(items.Count - 1).Append("] of ").Append(type).Append(" = (\n");
    var perLine = hex ? 12 : 24;
    for (var i = 0; i < items.Count; i++)
    {
        if (i % perLine == 0)
        {
            text.Append("    ");
        }

        text.Append(hex ? "$" + items[i].ToString("X6", CultureInfo.InvariantCulture) : items[i].ToString(CultureInfo.InvariantCulture));
        if (i < items.Count - 1)
        {
            text.Append(i % perLine == perLine - 1 ? ",\n" : ", ");
        }
    }

    text.Append(");\n\n");
}
