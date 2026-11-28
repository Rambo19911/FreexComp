using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-640 (task R-194), RTL-1277 (tasks R-266, R-267): the committed character tables - <c>rtl/src/CharTables.inc</c>
/// (general categories, unit <c>Character</c>) and <c>rtl/src/CaseTables.inc</c> (simple case mappings, unit
/// <c>SysUtils</c>) - are the pinned <c>third_party/unicode/UnicodeData.txt</c>, for every code point U+0000 .. U+10FFFF.
/// The test reads the files itself (it does not run the generator), so a data file that moved without regenerated
/// tables, or a table edited by hand, fails here.
/// </summary>
[Trait("Phase", "2")]
[Trait("Category", "Integration")]
[Trait("Req", "RTL-640")]
[Trait("Req", "RTL-1277")]
public sealed class CharTablesTests
{
    private static readonly string[] CategoryCodes =
    [
        "Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co",
        "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "Sm", "Sc", "Sk", "So", "Cn",
    ];

    private const int Bmp = 0x10000;

    private const int CodeSpace = 0x110000;

    private static string TablePath => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "CharTables.inc");

    private static string CaseTablePath => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "CaseTables.inc");

    private static string DataPath => Path.Combine(RtlSetup.RepositoryRoot, "third_party", "unicode", "UnicodeData.txt");

    /// <summary>
    /// Every code point U+0000 .. U+10FFFF: category, simple uppercase and simple lowercase computed from the tables the
    /// way the Pascal lookups compute them (the BMP two-level, the supplementary planes from the runs) equal the data file.
    /// </summary>
    [Fact]
    public void Tables_EqualThePinnedUnicodeData()
    {
        var table = File.ReadAllText(TablePath);
        var caseTable = File.ReadAllText(CaseTablePath);
        var category = Lookup(table, "Cat", null, Constant(table, "CharBlockShift"));
        var caseShift = Constant(caseTable, "CaseBlockShift");
        var upper = Lookup(caseTable, "Upper", "UpperDeltas", caseShift);
        var lower = Lookup(caseTable, "Lower", "LowerDeltas", caseShift);
        CategoryRuns(table, category);
        CaseRuns(caseTable, "Upper", upper);
        CaseRuns(caseTable, "Lower", lower);

        var (expectedCategory, expectedUpper, expectedLower) = ReadData();

        var differences = new List<string>();
        for (var c = 0; c < CodeSpace; c++)
        {
            if (category[c] != expectedCategory[c] || upper[c] != expectedUpper[c] || lower[c] != expectedLower[c])
            {
                differences.Add(string.Create(CultureInfo.InvariantCulture,
                    $"U+{c:X4}: category {category[c]} (data {expectedCategory[c]}), upper {upper[c]:+0;-0;0} (data {expectedUpper[c]:+0;-0;0}), lower {lower[c]:+0;-0;0} (data {expectedLower[c]:+0;-0;0})"));
            }
        }

        Assert.True(differences.Count == 0,
            $"{differences.Count} code points differ; run rtl/tools/GenCharTables.cs\n" + string.Join("\n", differences.Take(20)));
    }

    /// <summary>
    /// RTL-1277 (d): no simple case mapping of the data file leads from the BMP into a supplementary plane or the other
    /// way, so <c>AnsiUpperCase</c> / <c>AnsiLowerCase</c> keep the length of the string (RTL-1276 (b)). A later pin
    /// that breaks this fails here first.
    /// </summary>
    [Fact]
    public void CaseMappings_NeverCrossTheBmpBorder()
    {
        var (_, upper, lower) = ReadData();
        var crossing = new List<string>();
        var supplementaryMappings = 0;
        for (var c = 0; c < CodeSpace; c++)
        {
            foreach (var (delta, name) in new[] { (upper[c], "upper"), (lower[c], "lower") })
            {
                if (delta == 0)
                {
                    continue;
                }

                if (c >= Bmp)
                {
                    supplementaryMappings++;
                }

                if ((c < Bmp) != (c + delta < Bmp))
                {
                    crossing.Add(string.Create(CultureInfo.InvariantCulture, $"U+{c:X4} {name} -> U+{c + delta:X4}"));
                }
            }
        }

        Assert.True(crossing.Count == 0, "simple case mappings that cross the border of the BMP:\n" + string.Join("\n", crossing));
        Assert.True(supplementaryMappings > 0, "the data file has no supplementary case mapping: the check would prove nothing");
    }

    /// <summary>
    /// Each table file names the database version and the SHA-256 of the data file it was generated from, and, right
    /// after the licence line of the file, the origin and the licence of its numbers.
    /// </summary>
    [Fact]
    public void Table_RecordsVersionAndHashOfTheData()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(DataPath)));
        foreach (var path in new[] { TablePath, CaseTablePath })
        {
            Assert.True(File.Exists(path), path + " does not exist");
            var header = File.ReadAllText(path);
            header = header[..header.IndexOf("const", StringComparison.Ordinal)];
            var lines = header.Split('\n');

            Assert.Equal("// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception", lines[0].TrimEnd('\r'));
            Assert.Equal(
                "// The table values are derived from the Unicode Character Database 18.0.0 (UnicodeData.txt), Copyright (c) Unicode, Inc., used under the Unicode License V3 (Unicode-3.0); see THIRD-PARTY-NOTICES.txt.",
                lines[1].TrimEnd('\r'));
            Assert.Contains("UnicodeData.txt of the Unicode Character Database 18.0.0,", header, StringComparison.Ordinal);
            Assert.Contains("SHA-256 " + hash + ".", header, StringComparison.Ordinal);
        }
    }

    /// <summary>Category index and the two case deltas of every code point, from the data file.</summary>
    private static (int[] Category, int[] Upper, int[] Lower) ReadData()
    {
        var category = Enumerable.Repeat(CategoryCodes.Length - 1, CodeSpace).ToArray(); // no line: unassigned
        var upper = new int[CodeSpace];
        var lower = new int[CodeSpace];
        var rangeFirst = 0;
        foreach (var line in File.ReadLines(DataPath))
        {
            var fields = line.Split(';');
            var code = int.Parse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (fields[1].EndsWith(", First>", StringComparison.Ordinal))
            {
                rangeFirst = code;
                continue;
            }

            var first = fields[1].EndsWith(", Last>", StringComparison.Ordinal) ? rangeFirst : code;
            for (var c = first; c <= code; c++)
            {
                category[c] = Array.IndexOf(CategoryCodes, fields[2]);
            }

            upper[code] = fields[12].Length == 0 ? 0 : int.Parse(fields[12], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) - code;
            lower[code] = fields[13].Length == 0 ? 0 : int.Parse(fields[13], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) - code;
        }

        return (category, upper, lower);
    }

    /// <summary>
    /// The value of each of the 65536 code units of one two-level table: <c>&lt;name&gt;Blocks[&lt;name&gt;BlockIndex[c shr
    /// shift] shl shift + c and mask]</c>, taken through the table of deltas when there is one. The result covers the
    /// whole code space; the supplementary planes are filled from the runs.
    /// </summary>
    private static int[] Lookup(string table, string name, string? deltas, int shift)
    {
        var index = Numbers(table, name + "BlockIndex");
        var blocks = Numbers(table, name + "Blocks");
        var values = deltas is null ? null : Numbers(table, deltas);
        var result = new int[CodeSpace];
        for (var c = 0; c < Bmp; c++)
        {
            var code = blocks[(index[c >> shift] << shift) + (c & ((1 << shift) - 1))];
            result[c] = values is null ? code : values[code];
        }

        return result;
    }

    /// <summary>
    /// The categories of U+10000 .. U+10FFFF from <c>CatRuns</c> (each entry = first code point shl 5 or category,
    /// sorted; a run lasts up to the next entry, the last one up to U+10FFFF), as <c>Character</c> reads them.
    /// </summary>
    private static void CategoryRuns(string table, int[] category)
    {
        var runs = Numbers(table, "CatRuns");
        Assert.Equal(Constant(table, "CatRunCount"), runs.Length);
        Assert.Equal(Bmp, runs[0] >> 5);
        for (var i = 0; i < runs.Length; i++)
        {
            var end = i + 1 < runs.Length ? runs[i + 1] >> 5 : CodeSpace;
            Assert.True(end > runs[i] >> 5, "CatRuns is not sorted at entry " + i);
            for (var c = runs[i] >> 5; c < end; c++)
            {
                category[c] = runs[i] & 31;
            }
        }
    }

    /// <summary>
    /// The case deltas of U+10000 .. U+10FFFF from <c>&lt;name&gt;RunFirst</c>, <c>RunLast</c>, <c>RunDelta</c>
    /// (sorted runs that do not overlap; a code point outside every run maps to itself), as <c>SysUtils</c> reads them.
    /// </summary>
    private static void CaseRuns(string table, string name, int[] deltas)
    {
        var first = Numbers(table, name + "RunFirst");
        var last = Numbers(table, name + "RunLast");
        var delta = Numbers(table, name + "RunDelta");
        var count = Constant(table, name + "RunCount");
        Assert.Equal(count, first.Length);
        Assert.Equal(count, last.Length);
        Assert.Equal(count, delta.Length);
        for (var i = 0; i < count; i++)
        {
            Assert.True(first[i] >= Bmp && first[i] <= last[i] && last[i] < CodeSpace && (i == 0 || first[i] > last[i - 1]),
                $"{name} run {i} is not a sorted supplementary range");
            for (var c = first[i]; c <= last[i]; c++)
            {
                deltas[c] = delta[i];
            }
        }
    }

    private static int Constant(string table, string name)
    {
        var match = Regex.Match(table, @"\b" + name + @" = (\d+);");
        Assert.True(match.Success, name + " not found");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>The elements of the typed constant array <paramref name="name"/> (decimal, or hexadecimal with <c>$</c>).</summary>
    private static int[] Numbers(string table, string name)
    {
        var match = Regex.Match(table, @"\b" + name + @": array\[0\.\.(\d+)\] of \w+ = \(([^)]*)\);");
        Assert.True(match.Success, name + " not found in the table file");
        var numbers = match.Groups[2].Value.Split(',').Select(n => n.Trim()).Select(n => n.StartsWith('$')
            ? int.Parse(n[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
            : int.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1, numbers.Length);
        return numbers;
    }
}
