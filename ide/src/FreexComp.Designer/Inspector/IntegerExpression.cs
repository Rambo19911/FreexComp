using System.Globalization;

namespace FreexComp.Designer.Inspector;

/// <summary>
/// The Object Inspector's integer expressions (object-inspector.md DSG-OI-040):
/// <c>expr = term {("+" | "-") term}</c>, <c>term = factor {("*" | "/" | "div" | "mod") factor}</c>,
/// <c>factor = ["-" | "+"] (integer | "$" hex | "(" expr ")")</c>. Whitespace is ignored, <c>div</c>/<c>mod</c> are
/// case-insensitive, <c>/</c> is integer division truncating toward zero, <c>mod</c> has the sign of the dividend.
/// Every intermediate value must fit <see cref="long"/>; division by zero and overflow fail. A factor has at most one
/// sign (<c>--5</c> is invalid, <c>-(-5)</c> is 5); parentheses nest at most <see cref="MaxDepth"/> deep.
/// </summary>
public static class IntegerExpression
{
    /// <summary>The deepest parenthesis nesting accepted; deeper input is invalid (no stack overflow).</summary>
    public const int MaxDepth = 64;

    private static readonly Int128 Min = long.MinValue;
    private static readonly Int128 Max = long.MaxValue;

    /// <summary>Evaluates <paramref name="text"/>; false on a syntax error, division by zero or Int64 overflow.</summary>
    public static bool TryEvaluate(string text, out long value)
    {
        var parser = new Parser(text);
        if (parser.Expression() is { } result && parser.AtEnd)
        {
            value = (long)result;
            return true;
        }

        value = 0;
        return false;
    }

    private enum Kind { Number, Plus, Minus, Times, Slash, Div, Mod, Open, Close, End, Bad }

    private sealed class Parser(string text)
    {
        private int _pos;
        private (Kind Kind, Int128 Value) _token;
        private bool _started;
        private int _depth;

        public bool AtEnd => Peek().Kind == Kind.End;

        /// <summary><c>expr</c>; null on any error.</summary>
        public Int128? Expression()
        {
            if (Term() is not { } left)
            {
                return null;
            }

            while (Peek().Kind is Kind.Plus or Kind.Minus)
            {
                var op = Next().Kind;
                if (Term() is not { } right || Checked(op == Kind.Plus ? left + right : left - right) is not { } r)
                {
                    return null;
                }

                left = r;
            }

            return left;
        }

        private Int128? Term()
        {
            if (Factor() is not { } left)
            {
                return null;
            }

            while (Peek().Kind is Kind.Times or Kind.Slash or Kind.Div or Kind.Mod)
            {
                var op = Next().Kind;
                if (Factor() is not { } right)
                {
                    return null;
                }

                if (op != Kind.Times && right == 0)
                {
                    return null; // division by zero
                }

                // Int128 division truncates toward zero and its remainder has the sign of the dividend.
                var r = op switch { Kind.Times => left * right, Kind.Mod => left % right, _ => left / right };
                if (Checked(r) is not { } c)
                {
                    return null;
                }

                left = c;
            }

            return left;
        }

        /// <summary>
        /// One optional sign, then a number, a hex number or a parenthesised expression (at most <see cref="MaxDepth"/>
        /// deep). The literal 2^63 is accepted only under a minus sign (so that <c>-9223372036854775808</c> is the Int64
        /// minimum).
        /// </summary>
        private Int128? Factor()
        {
            var negate = false;
            if (Peek().Kind is Kind.Plus or Kind.Minus)
            {
                negate = Next().Kind == Kind.Minus; // a second sign is no factor start: "--5" fails below
            }

            Int128 value;
            switch (Next())
            {
                case { Kind: Kind.Number, Value: var n }:
                    value = n;
                    break;
                case { Kind: Kind.Open }:
                    if (++_depth > MaxDepth)
                    {
                        return null;
                    }

                    var inner = Expression();
                    _depth--;
                    if (inner is not { } e || Next().Kind != Kind.Close)
                    {
                        return null;
                    }

                    value = e;
                    break;
                default:
                    return null;
            }

            return Checked(negate ? -value : value);
        }

        private static Int128? Checked(Int128 v) => v < Min || v > Max ? null : v;

        private (Kind Kind, Int128 Value) Peek()
        {
            if (!_started)
            {
                _token = Scan();
                _started = true;
            }

            return _token;
        }

        private (Kind Kind, Int128 Value) Next()
        {
            var t = Peek();
            _token = Scan();
            return t;
        }

        private (Kind, Int128) Scan()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }

            if (_pos >= text.Length)
            {
                return (Kind.End, 0);
            }

            var c = text[_pos];
            switch (c)
            {
                case '+': _pos++; return (Kind.Plus, 0);
                case '-': _pos++; return (Kind.Minus, 0);
                case '*': _pos++; return (Kind.Times, 0);
                case '/': _pos++; return (Kind.Slash, 0);
                case '(': _pos++; return (Kind.Open, 0);
                case ')': _pos++; return (Kind.Close, 0);
                case '$':
                    _pos++;
                    return Digits(char.IsAsciiHexDigit, NumberStyles.AllowHexSpecifier);
                case >= '0' and <= '9':
                    return Digits(char.IsAsciiDigit, NumberStyles.None);
                case var _ when char.IsAsciiLetter(c):
                    var start = _pos;
                    while (_pos < text.Length && char.IsAsciiLetterOrDigit(text[_pos]) && !(_pos > start && char.IsAsciiDigit(text[_pos])))
                    {
                        _pos++;
                    }

                    var word = text[start.._pos];
                    return word.Equals("div", StringComparison.OrdinalIgnoreCase) ? (Kind.Div, 0)
                        : word.Equals("mod", StringComparison.OrdinalIgnoreCase) ? (Kind.Mod, 0)
                        : (Kind.Bad, 0);
                default:
                    return (Kind.Bad, 0);
            }
        }

        /// <summary>A run of digits as a number up to 2^63 (larger literals cannot be an Int64 operand).</summary>
        private (Kind, Int128) Digits(Func<char, bool> isDigit, NumberStyles style)
        {
            var start = _pos;
            while (_pos < text.Length && isDigit(text[_pos]))
            {
                _pos++;
            }

            if (_pos == start || !UInt128.TryParse(text.AsSpan(start, _pos - start), style, CultureInfo.InvariantCulture, out var v)
                || v > (UInt128)Max + 1)
            {
                return (Kind.Bad, 0);
            }

            return (Kind.Number, (Int128)v);
        }
    }
}
