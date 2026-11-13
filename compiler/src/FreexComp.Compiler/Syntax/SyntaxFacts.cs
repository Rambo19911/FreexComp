using System.Collections.Frozen;

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

    private static readonly FrozenDictionary<string, SyntaxKind> ReservedByText =
        ReservedWordTable.ToFrozenDictionary(e => e.Text, e => e.Kind, IdentifierComparer.Instance);

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
