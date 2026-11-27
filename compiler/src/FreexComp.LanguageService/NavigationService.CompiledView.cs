using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

/// <summary>A generated, read-only interface text of a unit for a symbol without source (CMP-1420); <c>NameSpan</c> = the symbol's name in it.</summary>
public sealed record GeneratedDeclaration(string UnitName, string Text, TextSpan NameSpan);

/// <summary>Generated declarations (CMP-1420, C-397).</summary>
public static partial class NavigationService
{
    /// <summary>
    /// CMP-1420: for an identifier whose symbol has no source location (CMP-740 gives empty: intrinsics, predefined types, built-ins, a
    /// <c>.pcu</c> symbol whose source is missing) the generated interface text of its unit (<c>System</c> for the unit-less built-ins):
    /// the <c>unit</c> line, a comment line, <c>interface</c>, the unit's public symbols without source grouped <c>type</c> / <c>const</c> /
    /// <c>var</c> / routines, one per line in the form of <see cref="QuickInfo.Signature"/>, sorted ordinal-ignore-case within a group
    /// (overloads in declaration order), then <c>implementation</c> and <c>end.</c>. Null when not on an identifier, on a keyword, for a
    /// symbol with source, and for a symbol the text does not show (<c>Self</c>, <c>Result</c>).
    /// </summary>
    public static GeneratedDeclaration? GetGeneratedDeclaration(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();
        var compilation = doc.Compilation;
        if (QuickInfoService.SymbolAt(doc.SemanticModel, position) is not ({ } symbol, var token) || symbol is UnitSymbol or LabelSymbol
            || !Sites(compilation, symbol).IsEmpty)
        {
            return null;
        }

        var unitName = symbol.ContainingUnit?.Name ?? "System";
        var unit = symbol.ContainingUnit ?? compilation.UnitSymbols.FirstOrDefault(u => IdentifierComparer.Instance.Equals(u.Name, unitName));

        // (name, symbol) entries without source: the built-ins belong to System (LANG-900), then the unit's interface
        var entries = new List<(string Name, Symbol Symbol)>();
        if (IdentifierComparer.Instance.Equals(unitName, "System"))
        {
            entries.AddRange(compilation.BuiltIns.Scope.Names.Zip(compilation.BuiltIns.Scope.Symbols).Where(e => Sites(compilation, e.Second).IsEmpty));
        }

        if (unit is not null)
        {
            // an alias declared in the unit (type FixedInt = Integer) is the aliased symbol itself: listed once
            var seen = entries.Select(e => e.Symbol).ToHashSet(ReferenceEqualityComparer.Instance);
            entries.AddRange(unit.InterfaceMembers.Where(m => Sites(compilation, m).IsEmpty && seen.Add(m)).Select(m => (m.Name, m)));
        }

        var text = new StringBuilder();
        var tokenName = token.Value as string ?? token.Text;
        TextSpan? exact = null, other = null;                 // the line under the name written at the position, else any line of the symbol
        void Line(string indent, string line, string entryName, Symbol entrySymbol)
        {
            if ((ReferenceEquals(entrySymbol, symbol) || !entrySymbol.Locations.IsEmpty && SymbolKey.AreSame(entrySymbol, symbol)) && FindName(line, entryName) is var at and >= 0)
            {
                var span = new TextSpan(text.Length + indent.Length + at, entryName.Length);
                if (exact is null && IdentifierComparer.Instance.Equals(entryName, tokenName))
                {
                    exact = span;
                }

                other ??= span;
            }

            text.Append(indent).Append(line).Append('\n');
        }

        text.Append("unit ").Append(unitName).Append(";\n{ Generated from compiled symbols; read only. }\ninterface\n");
        foreach (var (header, group) in new[] { ("type", 0), ("const", 1), ("var", 2), ("", 3) })
        {
            var members = entries.Where(e => GroupOf(e.Symbol) == group).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            text.Append('\n');
            if (header.Length > 0)
            {
                text.Append(header).Append('\n');
            }

            var indent = header.Length > 0 ? "  " : "";
            foreach (var (entryName, entrySymbol) in members)
            {
                List<Symbol> listed = entrySymbol is ClassTypeSymbol or RecordTypeSymbol or InterfaceTypeSymbol
                    ? [.. ((TypeSymbol)entrySymbol).Members.Where(m => m.Visibility is not (Visibility.Private or Visibility.StrictPrivate) && Sites(compilation, m).IsEmpty)]
                    : [];
                var shown = Shown(entryName, entrySymbol, header);
                Line(indent, listed.Count == 0 ? shown + ";" : shown, entryName, entrySymbol);
                foreach (var member in listed)
                {
                    Line(indent + "  ", Member(member) + ";", member.Name, member);
                }

                if (listed.Count > 0)
                {
                    text.Append(indent).Append("end;\n");
                }
            }
        }

        text.Append("\nimplementation\n\nend.\n");
        return (exact ?? other) is { } nameSpan ? new GeneratedDeclaration(unitName, text.ToString(), nameSpan) : null;
    }

    /// <summary>0 type, 1 const, 2 var, 3 routine; -1 not shown.</summary>
    private static int GroupOf(Symbol symbol) => symbol switch
    {
        TypeSymbol => 0,
        ConstantSymbol or EnumMemberSymbol => 1,
        VariableSymbol => 2,
        RoutineSymbol or IntrinsicSymbol => 3,
        _ => -1,
    };

    /// <summary>The <see cref="QuickInfo.Signature"/> form of a unit-level entry: the group keyword dropped, a routine without its unit qualifier.</summary>
    private static string Shown(string entryName, Symbol symbol, string header)
    {
        if (symbol is TypeSymbol type && !IdentifierComparer.Instance.Equals(entryName, type.Name))
        {
            return entryName + " = " + type.Name;                                       // an alias of the built-in scope (LongInt = Integer)
        }

        var signature = symbol.ToDisplayString();
        if (header.Length > 0 && signature.StartsWith(header + " ", StringComparison.Ordinal))
        {
            signature = signature[(header.Length + 1)..];
        }

        if (symbol is RoutineSymbol { ContainingSymbol: UnitSymbol unit })
        {
            var qualified = " " + unit.Name + "." + symbol.Name;
            var at = signature.IndexOf(qualified, StringComparison.Ordinal);
            signature = at < 0 ? signature : signature[..at] + " " + symbol.Name + signature[(at + qualified.Length)..];
        }

        return signature;
    }

    /// <summary>A member's signature without its owner qualifier (and without the word <c>field</c>).</summary>
    private static string Member(Symbol member)
    {
        var signature = member.ToDisplayString();
        if (signature.StartsWith("field ", StringComparison.Ordinal))
        {
            signature = signature["field ".Length..];
        }

        var qualifier = member.ContainingSymbol?.Name + ".";
        var at = signature.IndexOf(qualifier + member.Name, StringComparison.Ordinal);
        return at < 0 ? signature : signature[..at] + signature[(at + qualifier.Length)..];
    }

    /// <summary>The offset of <paramref name="name"/> as a whole word in <paramref name="line"/>, or -1.</summary>
    private static int FindName(string line, string name)
    {
        for (var at = line.IndexOf(name, StringComparison.OrdinalIgnoreCase); at >= 0; at = line.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if ((at == 0 || !IsWordChar(line[at - 1])) && (at + name.Length == line.Length || !IsWordChar(line[at + name.Length])))
            {
                return at;
            }
        }

        return -1;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
