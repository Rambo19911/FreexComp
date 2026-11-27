using System.Globalization;
using System.Numerics;
using System.Text;

namespace FreexComp.Compiler.Types;

/// <summary>
/// A compile-time value (api.md §5): <see cref="BigInteger"/> for ordinals (characters: the code), <see cref="double"/>, <see cref="string"/>,
/// <see cref="bool"/>, null for <c>nil</c> (also the empty dynamic-array constant <c>[]</c>); static arrays, records and non-empty typed
/// dynamic-array constants (typed constants, LANG-344, LANG-274) hold an <c>ImmutableArray&lt;ConstantValue?&gt;</c> of element / field values
/// (null = zero). CLR integer values passed in are stored as <see cref="BigInteger"/>.
/// </summary>
public sealed class ConstantValue
{
    internal ConstantValue(object? value, TypeSymbol type)
    {
        Value = value switch
        {
            int i => new BigInteger(i),
            long l => new BigInteger(l),
            ulong u => new BigInteger(u),
            uint u => new BigInteger(u),
            short s => new BigInteger(s),
            ushort s => new BigInteger(s),
            byte b => new BigInteger(b),
            sbyte b => new BigInteger(b),
            _ => value,
        };
        Type = type;
    }

    public object? Value { get; }

    public TypeSymbol Type { get; }

    /// <summary>Pascal literal syntax.</summary>
    public string ToDisplayString() => Value switch
    {
        null => "nil",
        bool b => b ? "True" : "False",
        BigInteger i when Type is CharTypeSymbol => StringText(((char)(int)i).ToString()),
        BigInteger i => i.ToString(CultureInfo.InvariantCulture),
        double d => RealText(d),
        string s => StringText(s),
        System.Collections.Immutable.ImmutableArray<ConstantValue?> fields when Type is RecordTypeSymbol record =>
            "(" + string.Join("; ", fields.Select((f, i) => (f, i)).Where(p => p.f is not null).Select(p => record.Fields[p.i].Name + ": " + p.f!.ToDisplayString())) + ")",
        System.Collections.Immutable.ImmutableArray<ConstantValue?> elements when Type is DynamicArrayTypeSymbol =>
            "[" + string.Join(", ", elements.Select(e => e?.ToDisplayString() ?? "0")) + "]",          // LANG-274 (C-389)
        System.Collections.Immutable.ImmutableArray<ConstantValue?> elements => "(" +string.Join(", ", elements.Select(e => e?.ToDisplayString() ?? "0")) + ")",
        _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "",
    };

    public override string ToString() => ToDisplayString();

    private static string RealText(double d)
    {
        var text = d.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || !double.IsFinite(d) ? text : text + ".0";
    }

    /// <summary>Quoted runs with doubled quotes; control characters as <c>#n</c>.</summary>
    private static string StringText(string s)
    {
        if (s.Length == 0)
        {
            return "''";
        }

        var sb = new StringBuilder();
        var quoted = false;
        foreach (var c in s)
        {
            if (c < ' ')
            {
                if (quoted)
                {
                    sb.Append('\'');
                    quoted = false;
                }

                sb.Append('#').Append(((int)c).ToString(CultureInfo.InvariantCulture));
                continue;
            }

            if (!quoted)
            {
                sb.Append('\'');
                quoted = true;
            }

            sb.Append(c == '\'' ? "''" : c.ToString());
        }

        if (quoted)
        {
            sb.Append('\'');
        }

        return sb.ToString();
    }
}
