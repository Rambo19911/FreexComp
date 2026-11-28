using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-640 (task R-194), RTL-1277 (task R-266): the committed character tables - <c>rtl/src/CharTables.inc</c>
/// (general categories, unit <c>Character</c>) and <c>rtl/src/CaseTables.inc</c> (simple case mappings, unit
/// <c>SysUtils</c>) - are the pinned <c>third_party/unicode/UnicodeData.txt</c>. The test reads the files itself (it does
/// not run the generator), so a data file that moved without regenerated tables, or a table edited by hand, fails here.
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

    private static string TablePath => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "CharTables.inc");

    private static string CaseTablePath => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "src", "CaseTables.inc");

    private static string DataPath => Path.Combine(RtlSetup.RepositoryRoot, "third_party", "unicode", "UnicodeData.txt");

    /// <summary>Every BMP code unit: category, simple uppercase and simple lowercase of the table equal the data file.</summary>
    [Fact]
    public void Tables_EqualThePinnedUnicodeData()
    {
        var table = File.ReadAllText(TablePath);
        var caseTable = File.ReadAllText(CaseTablePath);
        var category = Lookup(table, "Cat", null, Shift(table, "CharBlockShift"));
        var caseShift = Shift(caseTable, "CaseBlockShift");
        var upper = Lookup(caseTable, "Upper", "UpperDeltas", caseShift);
        var lower = Lookup(caseTable, "Lower", "LowerDeltas", caseShift);

        var expectedCategory = Enumerable.Repeat(CategoryCodes.Length - 1, 65536).ToArray(); // no line: unassigned
        var expectedUpper = new int[65536];
        var expectedLower = new int[65536];
        var rangeFirst = 0;
        foreach (var line in File.ReadLines(DataPath))
        {
            var fields = line.Split(';');
            var code = int.Parse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (code > 0xFFFF)
            {
                break; // the file is sorted by code point
            }

            if (fields[1].EndsWith(", First>", StringComparison.Ordinal))
            {
                rangeFirst = code;
                continue;
            }

            var first = fields[1].EndsWith(", Last>", StringComparison.Ordinal) ? rangeFirst : code;
            for (var c = first; c <= code; c++)
            {
                expectedCategory[c] = Array.IndexOf(CategoryCodes, fields[2]);
            }

            expectedUpper[code] = fields[12].Length == 0 ? 0 : int.Parse(fields[12], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) - code;
            expectedLower[code] = fields[13].Length == 0 ? 0 : int.Parse(fields[13], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) - code;
        }

        var differences = new List<string>();
        for (var c = 0; c < 65536; c++)
        {
            if (category[c] != expectedCategory[c] || upper[c] != expectedUpper[c] || lower[c] != expectedLower[c])
            {
                differences.Add(string.Create(CultureInfo.InvariantCulture,
                    $"U+{c:X4}: category {category[c]} (data {expectedCategory[c]}), upper {upper[c]:+0;-0;0} (data {expectedUpper[c]:+0;-0;0}), lower {lower[c]:+0;-0;0} (data {expectedLower[c]:+0;-0;0})"));
            }
        }

        Assert.True(differences.Count == 0,
            $"{differences.Count} code units differ; run rtl/tools/GenCharTables.cs\n" + string.Join("\n", differences.Take(20)));
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

    private static int Shift(string table, string name)
    {
        var match = Regex.Match(table, @"\b" + name + @" = (\d+);");
        Assert.True(match.Success, name + " not found");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The value of each of the 65536 code units of one two-level table: <c>&lt;name&gt;Blocks[&lt;name&gt;BlockIndex[c shr
    /// shift] shl shift + c and mask]</c>, taken through the table of deltas when there is one.
    /// </summary>
    private static int[] Lookup(string table, string name, string? deltas, int shift)
    {
        var index = Numbers(table, name + "BlockIndex");
        var blocks = Numbers(table, name + "Blocks");
        var values = deltas is null ? null : Numbers(table, deltas);
        var result = new int[65536];
        for (var c = 0; c < 65536; c++)
        {
            var code = blocks[(index[c >> shift] << shift) + (c & ((1 << shift) - 1))];
            result[c] = values is null ? code : values[code];
        }

        return result;
    }

    private static int[] Numbers(string table, string name)
    {
        var match = Regex.Match(table, @"\b" + name + @": array\[0\.\.(\d+)\] of \w+ = \(([^)]*)\);");
        Assert.True(match.Success, name + " not found in the table file");
        var numbers = match.Groups[2].Value.Split(',').Select(n => int.Parse(n.Trim(), CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1, numbers.Length);
        return numbers;
    }
}
