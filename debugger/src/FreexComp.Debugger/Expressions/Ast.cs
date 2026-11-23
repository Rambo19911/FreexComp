using System.Globalization;
using FreexComp.Debugger.Values;

namespace FreexComp.Debugger.Expressions;

/// <summary>
/// The evaluator's expression tree (accepted subset of Pascal values §9.1). Made by <see cref="SubsetValidator"/> from
/// the compiler's syntax nodes; parentheses are not kept (the shape of the tree carries them).
/// </summary>
internal abstract record Ast
{
    /// <summary>The tree in prefix form, for tests and traces: <c>(+ (* Total 2) 1)</c>.</summary>
    public abstract string Dump();

    /// <summary>The expression as normalised Pascal text; parses back to the same tree.</summary>
    public string ToPascal() => Print(this);

    internal sealed record IntLit(long Bits, bool Unsigned) : Ast
    {
        public override string Dump() => Unsigned ? ((ulong)Bits).ToString(CultureInfo.InvariantCulture) : Bits.ToString(CultureInfo.InvariantCulture);
    }

    internal sealed record FloatLit(double Value) : Ast
    {
        public override string Dump() => Value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A string literal of any length but one.</summary>
    internal sealed record StrLit(string Text) : Ast
    {
        public override string Dump() => ValueFormatter.StringLiteral(Text);
    }

    /// <summary>A string literal of exactly one character: a <c>Char</c>.</summary>
    internal sealed record CharLit(char Unit) : Ast
    {
        public override string Dump() => ValueFormatter.StringLiteral([Unit]);
    }

    internal sealed record NilLit : Ast
    {
        public override string Dump() => "nil";
    }

    /// <summary>An identifier: variable, field of Self, global, enum value, True / False, or a type name.</summary>
    internal sealed record Name(string Identifier) : Ast
    {
        public override string Dump() => Identifier;
    }

    /// <summary><c>not</c>, <c>-</c>, <c>+</c> or <c>@</c>.</summary>
    internal sealed record Unary(string Operator, Ast Operand) : Ast
    {
        public override string Dump() => "(" + Operator + " " + Operand.Dump() + ")";
    }

    /// <summary>The operator is its lower-case source text: <c>+</c>, <c>div</c>, <c>&lt;&gt;</c>, <c>in</c> …</summary>
    internal sealed record Binary(string Operator, Ast Left, Ast Right) : Ast
    {
        public override string Dump() => "(" + Operator + " " + Left.Dump() + " " + Right.Dump() + ")";
    }

    internal sealed record Field(Ast Target, string Member) : Ast
    {
        public override string Dump() => "(. " + Target.Dump() + " " + Member + ")";
    }

    /// <summary>One index; <c>A[i, j]</c> is <c>A[i][j]</c>.</summary>
    internal sealed record Index(Ast Target, Ast Position) : Ast
    {
        public override string Dump() => "([] " + Target.Dump() + " " + Position.Dump() + ")";
    }

    internal sealed record Deref(Ast Target) : Ast
    {
        public override string Dump() => "(^ " + Target.Dump() + ")";
    }

    /// <summary>A builtin of §9.5 under its canonical name.</summary>
    internal sealed record Call(string Builtin, IReadOnlyList<Ast> Arguments) : Ast
    {
        public override string Dump() => "(" + Builtin + string.Concat(Arguments.Select(a => " " + a.Dump())) + ")";
    }

    /// <summary><c>TypeName(expression)</c>; the type name as written.</summary>
    internal sealed record Cast(string TypeName, Ast Operand) : Ast
    {
        public override string Dump() => "(cast " + TypeName + " " + Operand.Dump() + ")";
    }

    internal sealed record SetItem(Ast Low, Ast? High);

    internal sealed record SetCtor(IReadOnlyList<SetItem> Items) : Ast
    {
        public override string Dump() =>
            "(set" + string.Concat(Items.Select(i => " " + (i.High is null ? i.Low.Dump() : "(.. " + i.Low.Dump() + " " + i.High.Dump() + ")"))) + ")";
    }

    // ---- normalised text ----

    private const int Relational = 1, Additive = 2, Multiplicative = 3, Prefix = 4, Postfix = 5, Primary = 6;

    private static int Level(Ast node) => node switch
    {
        Binary binary => BinaryLevel(binary.Operator),
        // A sign binds like an additive operator: "-A * B" is "-(A * B)".
        Unary { Operator: "-" or "+" } => Additive,
        Unary => Prefix,
        Name or Field or Index or Deref or Call or Cast => Primary,
        _ => Postfix,   // literals and set constructors: complete factors that take no suffix
    };

    private static int BinaryLevel(string op) => op switch
    {
        "*" or "/" or "div" or "mod" or "and" or "shl" or "shr" => Multiplicative,
        "+" or "-" or "or" or "xor" => Additive,
        _ => Relational,
    };

    private static string Print(Ast node)
    {
        switch (node)
        {
            case IntLit or NilLit or Name or StrLit or CharLit:
                return node.Dump();
            case FloatLit literal:
            {
                var text = literal.Dump();
                return text.Contains('.') || text.Contains('E') ? text : text + ".0";
            }

            case Unary { Operator: "not" } unary:
                return "not " + Wrap(unary.Operand, Prefix);
            case Unary { Operator: "@" } unary:
                return "@" + Wrap(unary.Operand, Prefix);
            case Unary unary:
                return unary.Operator + Wrap(unary.Operand, Multiplicative);
            case Binary binary:
            {
                var level = BinaryLevel(binary.Operator);
                // Left-associative; a relation is not associative at all.
                return Wrap(binary.Left, level == Relational ? Additive : level) + " " + binary.Operator + " " + Wrap(binary.Right, level + 1);
            }

            case Field field:
                return Wrap(field.Target, Primary) + "." + field.Member;
            case Index index:
                return Wrap(index.Target, Primary) + "[" + Print(index.Position) + "]";
            case Deref deref:
                return Wrap(deref.Target, Primary) + "^";
            case Call call:
                return call.Builtin + "(" + string.Join(", ", call.Arguments.Select(Print)) + ")";
            case Cast cast:
                return cast.TypeName + "(" + Print(cast.Operand) + ")";
            case SetCtor set:
                return "[" + string.Join(", ", set.Items.Select(i => i.High is null ? Print(i.Low) : Print(i.Low) + ".." + Print(i.High))) + "]";
            default:
                return node.Dump();
        }
    }

    /// <summary>The operand, in parentheses when it binds weaker than <paramref name="minimum"/>.</summary>
    private static string Wrap(Ast operand, int minimum) => Level(operand) >= minimum ? Print(operand) : "(" + Print(operand) + ")";
}
