using System.Globalization;
using System.Numerics;
using System.Text;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-059: the exact real formatting of intrinsics.md LANG-930…933 and the real helpers of rtl-abi.md §9.10. A reference
/// formatter written here with exact rational arithmetic (independent of the RTL's digit generator) is checked against
/// the spec's example table; then (<c>Category=Toolchain</c>) a generated program writes several hundred values through the
/// RTL's (rtl/src, C-149) <c>_WriteFloat</c>, <c>_Trunc</c>, <c>_Round</c>, <c>_Int</c>, <c>_Frac</c> and its output must equal the reference.
/// </summary>
[Trait("Phase", "2")]
public sealed class RealFormatTests
{
    // ------------------------------------------------------------------ reference (LANG-930…932)

    /// <summary>
    /// The text of <c>Write(v:width:decimals)</c> (CMP-410 arguments: <paramref name="decimals"/> &lt; 0 = scientific in
    /// <paramref name="width"/>, which the compiler sets to 23 when no width is given), padded per LANG-915.
    /// </summary>
    internal static string Format(double v, int width, int decimals)
    {
        string text;
        if (double.IsNaN(v))
        {
            text = "NAN";
        }
        else if (double.IsInfinity(v))
        {
            text = v < 0 ? "-INF" : "INF";
        }
        else
        {
            var (num, den) = Exact(Math.Abs(v));
            text = decimals < 0 ? Scientific(num, den, double.IsNegative(v) && v != 0, width) : Fixed(num, den, double.IsNegative(v), Math.Min(decimals, 40));
        }

        return text.PadLeft(width);
    }

    /// <summary>|v| = num / den exactly.</summary>
    private static (BigInteger Num, BigInteger Den) Exact(double magnitude)
    {
        var bits = BitConverter.DoubleToInt64Bits(magnitude);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        var e = exponent == 0 ? -1074 : exponent - 1075;
        if (exponent != 0)
        {
            mantissa |= 1L << 52;
        }

        return e >= 0 ? (new BigInteger(mantissa) << e, BigInteger.One) : (new BigInteger(mantissa), BigInteger.One << -e);
    }

    /// <summary>Round half away from zero of num / den (both positive).</summary>
    private static BigInteger RoundHalfUp(BigInteger num, BigInteger den) => (2 * num + den) / (2 * den);

    private static string Scientific(BigInteger num, BigInteger den, bool negative, int width)
    {
        var d = Math.Min(Math.Max(width - 9, 1), 16);
        BigInteger digits;
        var exp = 0;
        if (num.IsZero)
        {
            digits = BigInteger.Zero;
        }
        else
        {
            // 10^exp <= num/den < 10^(exp+1), found by exact comparison
            exp = (int)Math.Floor(BigInteger.Log10(num) - BigInteger.Log10(den));
            while (Compare(num, den, exp) < 0)
            {
                exp--;
            }

            while (Compare(num, den, exp + 1) >= 0)
            {
                exp++;
            }

            var shift = d - exp;
            digits = shift >= 0 ? RoundHalfUp(num * BigInteger.Pow(10, shift), den) : RoundHalfUp(num, den * BigInteger.Pow(10, -shift));
            if (digits == BigInteger.Pow(10, d + 1))
            {
                digits /= 10;
                exp++;
            }
        }

        var mantissa = digits.ToString(CultureInfo.InvariantCulture).PadLeft(d + 1, '0');
        return (negative ? "-" : " ") + mantissa[0] + "." + mantissa[1..] + "E" + (exp < 0 ? "-" : "+") +
               Math.Abs(exp).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Sign of num/den − 10^exp.</summary>
    private static int Compare(BigInteger num, BigInteger den, int exp) =>
        exp >= 0 ? num.CompareTo(den * BigInteger.Pow(10, exp)) : (num * BigInteger.Pow(10, -exp)).CompareTo(den);

    private static string Fixed(BigInteger num, BigInteger den, bool negative, int decimals)
    {
        var scaled = RoundHalfUp(num * BigInteger.Pow(10, decimals), den);
        var digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        var text = decimals == 0 ? digits : digits[..^decimals] + "." + digits[^decimals..];
        return (negative && !scaled.IsZero ? "-" : "") + text;
    }

    /// <summary>The LANG-930/931 example table and the worked width cases of intrinsics.md (□ = space).</summary>
    [Theory]
    [Trait("Req", "LANG-930")]
    [Trait("Req", "LANG-931")]
    [Trait("Req", "LANG-932")]
    [InlineData(1.5, 23, -1, " 1.50000000000000E+0000")]
    [InlineData(-1234.5678, 23, -1, "-1.23456780000000E+0003")]
    [InlineData(0.0, 23, -1, " 0.00000000000000E+0000")]
    [InlineData(1e-10, 23, -1, " 1.00000000000000E-0010")]
    [InlineData(0.1, 23, -1, " 1.00000000000000E-0001")]
    [InlineData(2.0 / 3, 23, -1, " 6.66666666666667E-0001")]
    [InlineData(1.5, 10, -1, " 1.5E+0000")]
    [InlineData(1.5, 12, -1, " 1.500E+0000")]
    [InlineData(1.5, 0, -1, " 1.5E+0000")]
    [InlineData(1.5, 30, -1, "      1.5000000000000000E+0000")]
    [InlineData(3.14159, 0, 2, "3.14")]
    [InlineData(3.14159, 8, 2, "    3.14")]
    [InlineData(2.5, 0, 0, "3")]
    [InlineData(0.5, 0, 0, "1")]
    [InlineData(-0.001, 0, 2, "0.00")]
    [InlineData(-0.005, 0, 2, "-0.01")]
    [InlineData(1.005, 0, 2, "1.00")]
    [InlineData(0.125, 0, 2, "0.13")]
    [InlineData(1e20, 0, 0, "100000000000000000000")]
    [InlineData(123.456, 0, 1, "123.5")]
    [InlineData(9.96, 0, 1, "10.0")]
    [InlineData(9.9999999999999999e22, 23, -1, " 1.00000000000000E+0023")]
    [InlineData(double.NaN, 23, -1, "                    NAN")]
    [InlineData(double.PositiveInfinity, 0, 2, "INF")]
    [InlineData(double.NegativeInfinity, 6, 2, "  -INF")]
    public void Reference_MatchesTheSpecExamples(double value, int width, int decimals, string expected) =>
        Assert.Equal(expected, Format(value, width, decimals));

    // ------------------------------------------------------------------ the RTL against the reference

    private static readonly (int Width, int Decimals)[] Formats =
        [(23, -1), (10, -1), (30, -1), (0, -1), (0, 0), (0, 2), (12, 5), (0, 17), (0, 40)];

    /// <summary>Edge cases (ties, carries, powers of ten, subnormals, extremes) and 400 seeded random values.</summary>
    internal static List<double> Values()
    {
        var values = new List<double>
        {
            0.0, -0.0, 1, -1, 0.5, -0.5, 1.5, 2.5, -2.5, 3.5, 0.125, 0.375, 0.0625, 0.005, -0.005, 1.005, 0.95, 9.5, 99.5,
            9.96, 0.1, 0.2, 0.3, 1.0 / 3, 2.0 / 3, 1e-5, 12345.678, 1e15, 1e16, 1e17, 1e21, 1e22, 1e23, 9.9999999999999999e22,
            123456789012345678, 9007199254740992, 9007199254740993, 4611686018427387904.0, 9.2233720368547758e18, -9.2233720368547758e18,
            double.Epsilon, -double.Epsilon, 2.2250738585072014e-308, 2.2250738585072009e-308, double.MaxValue, -double.MaxValue,
            1e308, 1e-308, 1e-320, 4.35, 0.000001, 999999.5, 0.9999999999999999, 99999999999999.95, 5e-324 * 3,
        };
        var random = new Random(20261114);
        for (var i = 0; i < 200; i++)
        {
            // any finite bit pattern
            var bits = random.NextInt64(0, 0x7FF0000000000000L) | (random.Next(2) == 0 ? 0 : long.MinValue);
            values.Add(BitConverter.Int64BitsToDouble(bits));
        }

        for (var i = 0; i < 200; i++)
        {
            // short decimals, where ties and near-ties are frequent
            values.Add(random.Next(-2_000_000, 2_000_000) / Math.Pow(10, random.Next(0, 8)));
        }

        return values;
    }

    internal static string Program(List<double> values)
    {
        var text = new StringBuilder("program RealFormat;\ntype PUInt64 = ^UInt64;\nvar D: Double;\nbegin\n");
        foreach (var value in values)
        {
            text.Append(CultureInfo.InvariantCulture, $"  PUInt64(@D)^ := ${BitConverter.DoubleToInt64Bits(value):X16};\n  Writeln(");
            text.Append(string.Join(", '|', ", Formats.Select(f => f.Decimals < 0 ? $"D:{f.Width}" : $"D:{f.Width}:{f.Decimals}")));
            text.Append(", '|');\n");
            if (Math.Abs(value) < 4e18)
            {
                text.Append("  Writeln(Trunc(D), '|', Round(D), '|', Int(D), '|', Frac(D), '|');\n");
            }
        }

        return text.Append("end.\n").ToString();
    }

    internal static List<string> Expected(List<double> values)
    {
        var lines = new List<string>();
        foreach (var value in values)
        {
            lines.Add(string.Concat(Formats.Select(f => Format(value, f.Width, f.Decimals) + "|")));
            if (Math.Abs(value) < 4e18)
            {
                var integral = Math.Truncate(value);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{(long)integral}|{(long)Math.Round(value, MidpointRounding.ToEven)}|{Format(integral, 23, -1)}|{Format(value - integral, 23, -1)}|"));
            }
        }

        return lines;
    }

    [Theory]
    [InlineData(OptimizationLevel.O0)]
    [InlineData(OptimizationLevel.O2)]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "LANG-930")]
    [Trait("Req", "LANG-931")]
    [Trait("Req", "LANG-932")]
    [Trait("Req", "LANG-933")]
    public async Task Rtl_WritesEveryValueLikeTheReference(OptimizationLevel optimization)
    {
        var values = Values();

        var (exitCode, stdout, stderr) = await EmitTests.BuildAndRun("RealFormat", Program(values), optimization);

        Assert.Empty(stderr);
        Assert.Equal(0, exitCode);
        var expected = Expected(values);
        Assert.Equal(expected.Count, stdout.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == stdout[i], $"line {i + 1}:\nexpected: {expected[i]}\nactual:   {stdout[i]}");
        }
    }

    /// <summary>rtl-abi.md §9.10: <c>Trunc</c>/<c>Round</c> outside Int64 (and NaN) are runtime error 207.</summary>
    [Theory]
    [InlineData("Trunc", "1e19")]
    [InlineData("Round", "-1e19")]
    [InlineData("Trunc", "Z / Z")]
    [Trait("Category", "Toolchain")]
    public async Task Rtl_TruncRoundOutOfRange_IsRuntimeError207(string function, string value)
    {
        var source = $"program R; var Z, D: Double; begin Z := 0; D := {value}; Writeln({function}(D)) end.";

        var (exitCode, stdout, stderr) = await EmitTests.BuildAndRun("R", source, OptimizationLevel.O0);

        Assert.Equal(207, exitCode);
        Assert.Empty(stdout);
        Assert.Matches(@"^Runtime error 207 at \$[0-9A-F]{16}$", Assert.Single(stderr));          // the address: ABI-101, C-173
    }
}
