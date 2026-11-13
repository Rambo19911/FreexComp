using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FreexComp.Compiler.Syntax;

/// <summary>The only place where keyword sets are published (CMP-223).</summary>
public static partial class SyntaxFacts
{
    private static readonly (string Text, SyntaxKind Kind)[] ReservedWordTable =
    [
        ("and", SyntaxKind.AndKeyword), ("array", SyntaxKind.ArrayKeyword), ("as", SyntaxKind.AsKeyword),
        ("begin", SyntaxKind.BeginKeyword), ("case", SyntaxKind.CaseKeyword), ("class", SyntaxKind.ClassKeyword),
        ("const", SyntaxKind.ConstKeyword), ("constructor", SyntaxKind.ConstructorKeyword),
        ("destructor", SyntaxKind.DestructorKeyword), ("div", SyntaxKind.DivKeyword), ("do", SyntaxKind.DoKeyword),
        ("downto", SyntaxKind.DownToKeyword), ("else", SyntaxKind.ElseKeyword), ("end", SyntaxKind.EndKeyword),
        ("except", SyntaxKind.ExceptKeyword), ("exports", SyntaxKind.ExportsKeyword), ("file", SyntaxKind.FileKeyword),
        ("finalization", SyntaxKind.FinalizationKeyword), ("finally", SyntaxKind.FinallyKeyword),
        ("for", SyntaxKind.ForKeyword), ("function", SyntaxKind.FunctionKeyword), ("goto", SyntaxKind.GotoKeyword),
        ("if", SyntaxKind.IfKeyword), ("implementation", SyntaxKind.ImplementationKeyword), ("in", SyntaxKind.InKeyword),
        ("inherited", SyntaxKind.InheritedKeyword), ("initialization", SyntaxKind.InitializationKeyword),
        ("inline", SyntaxKind.InlineKeyword), ("interface", SyntaxKind.InterfaceKeyword), ("is", SyntaxKind.IsKeyword),
        ("label", SyntaxKind.LabelKeyword), ("library", SyntaxKind.LibraryKeyword), ("mod", SyntaxKind.ModKeyword),
        ("nil", SyntaxKind.NilKeyword), ("not", SyntaxKind.NotKeyword), ("object", SyntaxKind.ObjectKeyword),
        ("of", SyntaxKind.OfKeyword), ("or", SyntaxKind.OrKeyword), ("packed", SyntaxKind.PackedKeyword),
        ("procedure", SyntaxKind.ProcedureKeyword), ("program", SyntaxKind.ProgramKeyword),
        ("property", SyntaxKind.PropertyKeyword), ("raise", SyntaxKind.RaiseKeyword), ("record", SyntaxKind.RecordKeyword),
        ("repeat", SyntaxKind.RepeatKeyword), ("resourcestring", SyntaxKind.ResourceStringKeyword),
        ("set", SyntaxKind.SetKeyword), ("shl", SyntaxKind.ShlKeyword), ("shr", SyntaxKind.ShrKeyword),
        ("string", SyntaxKind.StringKeyword), ("then", SyntaxKind.ThenKeyword), ("threadvar", SyntaxKind.ThreadVarKeyword),
        ("to", SyntaxKind.ToKeyword), ("try", SyntaxKind.TryKeyword), ("type", SyntaxKind.TypeKeyword),
        ("unit", SyntaxKind.UnitKeyword), ("until", SyntaxKind.UntilKeyword), ("uses", SyntaxKind.UsesKeyword),
        ("var", SyntaxKind.VarKeyword), ("while", SyntaxKind.WhileKeyword), ("with", SyntaxKind.WithKeyword),
        ("xor", SyntaxKind.XorKeyword),
    ];

    private static readonly (string Text, SyntaxKind Kind)[] ContextualKeywordTable =
    [
        ("absolute", SyntaxKind.AbsoluteKeyword), ("abstract", SyntaxKind.AbstractKeyword),
        ("assembler", SyntaxKind.AssemblerKeyword), ("at", SyntaxKind.AtKeyword), ("automated", SyntaxKind.AutomatedKeyword),
        ("cdecl", SyntaxKind.CdeclKeyword), ("contains", SyntaxKind.ContainsKeyword), ("default", SyntaxKind.DefaultKeyword),
        ("delayed", SyntaxKind.DelayedKeyword), ("deprecated", SyntaxKind.DeprecatedKeyword), ("dispid", SyntaxKind.DispIdKeyword),
        ("dynamic", SyntaxKind.DynamicKeyword), ("experimental", SyntaxKind.ExperimentalKeyword),
        ("export", SyntaxKind.ExportKeyword), ("external", SyntaxKind.ExternalKeyword), ("far", SyntaxKind.FarKeyword),
        ("final", SyntaxKind.FinalKeyword), ("forward", SyntaxKind.ForwardKeyword), ("helper", SyntaxKind.HelperKeyword),
        ("implements", SyntaxKind.ImplementsKeyword), ("index", SyntaxKind.IndexKeyword), ("local", SyntaxKind.LocalKeyword),
        ("message", SyntaxKind.MessageKeyword), ("name", SyntaxKind.NameKeyword), ("near", SyntaxKind.NearKeyword),
        ("nodefault", SyntaxKind.NoDefaultKeyword), ("noreturn", SyntaxKind.NoReturnKeyword), ("on", SyntaxKind.OnKeyword),
        ("operator", SyntaxKind.OperatorKeyword), ("out", SyntaxKind.OutKeyword), ("overload", SyntaxKind.OverloadKeyword),
        ("override", SyntaxKind.OverrideKeyword), ("package", SyntaxKind.PackageKeyword), ("pascal", SyntaxKind.PascalKeyword),
        ("platform", SyntaxKind.PlatformKeyword), ("private", SyntaxKind.PrivateKeyword),
        ("protected", SyntaxKind.ProtectedKeyword), ("public", SyntaxKind.PublicKeyword),
        ("published", SyntaxKind.PublishedKeyword), ("read", SyntaxKind.ReadKeyword), ("readonly", SyntaxKind.ReadOnlyKeyword),
        ("reference", SyntaxKind.ReferenceKeyword), ("register", SyntaxKind.RegisterKeyword),
        ("reintroduce", SyntaxKind.ReintroduceKeyword), ("requires", SyntaxKind.RequiresKeyword),
        ("resident", SyntaxKind.ResidentKeyword), ("safecall", SyntaxKind.SafeCallKeyword), ("sealed", SyntaxKind.SealedKeyword),
        ("static", SyntaxKind.StaticKeyword), ("stdcall", SyntaxKind.StdCallKeyword), ("stored", SyntaxKind.StoredKeyword),
        ("strict", SyntaxKind.StrictKeyword), ("unsafe", SyntaxKind.UnsafeKeyword), ("varargs", SyntaxKind.VarArgsKeyword),
        ("virtual", SyntaxKind.VirtualKeyword), ("winapi", SyntaxKind.WinApiKeyword), ("write", SyntaxKind.WriteKeyword),
        ("writeonly", SyntaxKind.WriteOnlyKeyword),
    ];

    private static readonly (string Text, SyntaxKind Kind)[] SymbolTable =
    [
        ("+", SyntaxKind.PlusToken), ("-", SyntaxKind.MinusToken), ("*", SyntaxKind.AsteriskToken), ("/", SyntaxKind.SlashToken),
        ("=", SyntaxKind.EqualsToken), ("<>", SyntaxKind.LessThanGreaterThanToken), ("<", SyntaxKind.LessThanToken),
        (">", SyntaxKind.GreaterThanToken), ("<=", SyntaxKind.LessThanEqualsToken), (">=", SyntaxKind.GreaterThanEqualsToken),
        (":=", SyntaxKind.ColonEqualsToken), (".", SyntaxKind.DotToken), ("..", SyntaxKind.DotDotToken), (",", SyntaxKind.CommaToken),
        (";", SyntaxKind.SemicolonToken), (":", SyntaxKind.ColonToken), ("(", SyntaxKind.OpenParenToken),
        (")", SyntaxKind.CloseParenToken), ("[", SyntaxKind.OpenBracketToken), ("]", SyntaxKind.CloseBracketToken),
        ("^", SyntaxKind.CaretToken), ("@", SyntaxKind.AtToken), ("#", SyntaxKind.HashToken), ("$", SyntaxKind.DollarToken),
        ("&", SyntaxKind.AmpersandToken),
    ];

    private static readonly FrozenDictionary<string, SyntaxKind> ReservedByText =
        ReservedWordTable.ToFrozenDictionary(e => e.Text, e => e.Kind, IdentifierComparer.Instance);

    private static readonly FrozenDictionary<string, SyntaxKind> ContextualByText =
        ContextualKeywordTable.ToFrozenDictionary(e => e.Text, e => e.Kind, IdentifierComparer.Instance);

    private static readonly FrozenDictionary<SyntaxKind, string> TextByKind =
        SymbolTable.Concat(ReservedWordTable).Concat(ContextualKeywordTable).ToFrozenDictionary(e => e.Kind, e => e.Text);

    private static readonly FrozenSet<SyntaxKind> ReservedKinds = ReservedWordTable.Select(e => e.Kind).ToFrozenSet();

    private static readonly FrozenSet<SyntaxKind> ContextualKinds = ContextualKeywordTable.Select(e => e.Kind).ToFrozenSet();

    /// <summary>LANG-115, lower-case, sorted ordinally.</summary>
    public static ImmutableArray<string> ReservedWords { get; } = [.. ReservedWordTable.Select(e => e.Text).Order(StringComparer.Ordinal)];

    /// <summary>LANG-116, lower-case, sorted ordinally.</summary>
    public static ImmutableArray<string> ContextualKeywords { get; } = [.. ContextualKeywordTable.Select(e => e.Text).Order(StringComparer.Ordinal)];

    /// <summary>Reserved word (LANG-115), compared case-insensitively for ASCII letters only (LANG-110).</summary>
    public static bool IsReservedWord(string text) => GetReservedWordKind(text) != SyntaxKind.None;

    /// <summary>Contextual keyword (LANG-116), same comparison as <see cref="IsReservedWord(string)"/>.</summary>
    public static bool IsContextualKeyword(string text) => ContextualByText.ContainsKey(text);

    /// <summary>A LANG-110 identifier that is not a reserved word (so usable without '&amp;').</summary>
    public static bool IsValidIdentifier(string text)
    {
        if (text.Length == 0 || IsReservedWord(text))
        {
            return false;
        }

        var tokens = Lexer.Tokenize(SourceText.From(@"C:\identifier.pas", text));
        return tokens.Length == 2 && tokens[0].Kind == SyntaxKind.IdentifierToken && tokens[0].Text == text
               && text[0] != '&' && tokens[0].LeadingTrivia.IsEmpty && tokens[0].TrailingTrivia.IsEmpty;
    }

    /// <summary>The reserved-word kind, else the contextual-keyword kind, else <see cref="SyntaxKind.None"/>.</summary>
    public static SyntaxKind GetKeywordKind(string text) =>
        GetReservedWordKind(text) is var kind && kind != SyntaxKind.None ? kind
        : ContextualByText.TryGetValue(text, out var contextual) ? contextual : SyntaxKind.None;

    /// <summary>The fixed text of a symbol or keyword kind (keywords lower-case, digraphs as <c>[</c> / <c>]</c>); "" otherwise.</summary>
    public static string GetText(SyntaxKind kind) => TextByKind.TryGetValue(kind, out var text) ? text : "";

    /// <summary>Binary operator precedence (LANG-170): 4 multiplicative, 3 additive, 2 relational, 0 not binary.</summary>
    public static int GetBinaryPrecedence(SyntaxKind operatorKind) => operatorKind switch
    {
        SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.DivKeyword or SyntaxKind.ModKeyword
            or SyntaxKind.AndKeyword or SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword or SyntaxKind.AsKeyword => 4,
        SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword => 3,
        SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken
            or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken or SyntaxKind.InKeyword or SyntaxKind.IsKeyword => 2,
        _ => 0,
    };

    /// <summary>Reserved-word token kinds (LANG-115).</summary>
    public static bool IsKeyword(SyntaxKind kind) => ReservedKinds.Contains(kind);

    /// <summary>Contextual-keyword kinds (LANG-116), as used in <see cref="SyntaxToken.ContextualKind"/>.</summary>
    public static bool IsContextualKeyword(SyntaxKind kind) => ContextualKinds.Contains(kind);

    /// <summary><c>. , ; : ( ) [ ]</c>.</summary>
    public static bool IsPunctuation(SyntaxKind kind) => kind is SyntaxKind.DotToken or SyntaxKind.CommaToken
        or SyntaxKind.SemicolonToken or SyntaxKind.ColonToken or SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken
        or SyntaxKind.OpenBracketToken or SyntaxKind.CloseBracketToken;

    /// <summary><c>+ - * / = &lt;&gt; &lt; &gt; &lt;= &gt;= := @ ^ ..</c>.</summary>
    public static bool IsOperator(SyntaxKind kind) => kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken
        or SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken
        or SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken
        or SyntaxKind.GreaterThanEqualsToken or SyntaxKind.ColonEqualsToken or SyntaxKind.AtToken or SyntaxKind.CaretToken
        or SyntaxKind.DotDotToken;

    public static bool IsLiteral(SyntaxKind kind) =>
        kind is SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken or SyntaxKind.StringLiteralToken;

    public static bool IsTrivia(SyntaxKind kind) => kind is >= SyntaxKind.WhitespaceTrivia and <= SyntaxKind.SkippedTokensTrivia;

    /// <summary>Line, block and documentation comments (not directives).</summary>
    public static bool IsComment(SyntaxKind kind) =>
        kind is SyntaxKind.LineCommentTrivia or SyntaxKind.BlockCommentTrivia or SyntaxKind.DocCommentTrivia;

    /// <summary>The reserved-word kind for <paramref name="text"/> (ASCII case-insensitive), else <see cref="SyntaxKind.None"/>.</summary>
    internal static SyntaxKind GetReservedWordKind(ReadOnlySpan<char> text)
    {
        // Every reserved word is 2..14 ASCII letters; skip the lookup for anything else.
        if (text.Length is < 2 or > 14)
        {
            return SyntaxKind.None;
        }

        return ReservedByText.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out var kind) ? kind : SyntaxKind.None;
    }
}

/// <summary>
/// Identifier comparison of LANG-110: ordinal, with upper-casing of ASCII letters only (non-ASCII letters must
/// match exactly).
/// </summary>
internal sealed class IdentifierComparer : IEqualityComparer<string>, IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    public static readonly IdentifierComparer Instance = new();

    public bool Equals(string? x, string? y) => x is null ? y is null : y is not null && Equals(x.AsSpan(), y);

    public int GetHashCode(string obj) => GetHashCode(obj.AsSpan());

    public bool Equals(ReadOnlySpan<char> alternate, string other)
    {
        if (alternate.Length != other.Length)
        {
            return false;
        }

        for (var i = 0; i < alternate.Length; i++)
        {
            if (Fold(alternate[i]) != Fold(other[i]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(ReadOnlySpan<char> alternate)
    {
        var hash = new HashCode();
        foreach (var c in alternate)
        {
            hash.Add(Fold(c));
        }

        return hash.ToHashCode();
    }

    public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();

    private static char Fold(char c) => c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
}
