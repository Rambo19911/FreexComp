using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

public sealed record QuickInfo(TextSpan Span, string Signature, string? Documentation, string? DeclaredIn, string? ConstantValue);

/// <summary>Hover (language-service.md §6, CMP-730, 731, C-136).</summary>
public static class QuickInfoService
{
    public static QuickInfo? GetQuickInfo(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        ct.ThrowIfCancellationRequested();
        if (SymbolAt(model, position) is not ({ } symbol, var token))
        {
            return null;
        }

        // C-347: a declaration inside a generic is indexed with an instance's symbol (SemanticModel); it is shown as the generic's
        var inGeneric = ReferenceEquals(model.GetDeclaredSymbol(token), symbol) && new[] { symbol }.Concat(TypeMembers.EnclosingTypes(symbol))
            .OfType<TypeSymbol>().Any(t => t.IsGenericInstance);
        if (inGeneric && symbol is not TypeSymbol)
        {
            return null;
        }

        // CMP-1413 (C-385): a variable whose declaration was rejected (E2075, an unknown type) keeps the error type: nothing to show
        if (symbol is VariableSymbol { Type.TypeKind: TypeKind.Error })
        {
            return null;
        }

        var signature = symbol is TypeSymbol type && !IdentifierComparer.Instance.Equals(type.GenericDefinition?.Name ?? type.Name, token.Value as string ?? token.Text)
            ? $"type {token.Text} = {symbol.Name}"                       // an alias names the type it stands for
            : symbol is TypeSymbol named ? DisplayWithEnclosingTypes(named, inGeneric) : symbol.ToDisplayString();
        var documentation = symbol is UnitSymbol unit
            ? unit.Locations.FirstOrDefault().Path
            : DocCommentRenderer.Documentation(doc.Compilation, symbol);       // CMP-732, CMP-734 (C-393, C-394)
        var value = symbol switch
        {
            ConstantSymbol { Value: { } constant } => constant.ToDisplayString(),
            RecordTypeSymbol record => $"SizeOf = {record.Size}",
            _ => null,
        };
        return new QuickInfo(token.Span, signature, documentation, DeclaredIn(symbol), value);
    }

    /// <summary>The identifier (or label number, C-321) at (or ending at) <paramref name="position"/> and the symbol it declares or refers to.</summary>
    internal static (Symbol Symbol, SyntaxToken Token)? SymbolAt(SemanticModel model, int position)
    {
        var tree = model.SyntaxTree;                                        // nodes handed to the model must be its own
        var token = tree.FindToken(Math.Clamp(position, 0, tree.Text.Text.Length));
        if (!IsTarget(token) || !token.Span.Contains(position))
        {
            var before = position > 0 ? tree.FindToken(position - 1) : default;
            if (!IsTarget(before) || before.Span.End != position)
            {
                return null;
            }

            token = before;
        }

        return SymbolOf(model, token) is { } symbol ? (symbol, token) : null;
    }

    /// <summary>A name, or the keyword of a bare <c>inherited</c> (CMP-1419, C-396: it stands for the ancestor's method it calls).</summary>
    private static bool IsTarget(SyntaxToken token) =>
        SyntaxFacts.IsNameToken(token) || token.Kind == SyntaxKind.InheritedKeyword && token.Parent is InheritedExpressionSyntax { Name: null };

    /// <summary>What an identifier token of the model's tree declares or refers to (a uses entry: its unit).</summary>
    internal static Symbol? SymbolOf(SemanticModel model, SyntaxToken token)
    {
        if (token.IsMissing || token.Parent is null)
        {
            return null;
        }

        return model.GetDeclaredSymbol(token)
            ?? (token.Parent.FirstAncestorOrSelf<UsedUnitSyntax>() is { } used ? model.GetSymbolInfo(used).Symbol : null)
            ?? model.GetSymbolInfo(token.Parent).Symbol;
    }

    /// <summary>
    /// CMP-1394 (C-347): a type's display string with its display name (LANG-775: <c>type TList&lt;Integer&gt;.TEnumerator = class</c>); with
    /// <paramref name="asDeclared"/> every generic instance in the name is shown as its generic (<c>TList&lt;T&gt;.TEnumerator</c>).
    /// </summary>
    private static string DisplayWithEnclosingTypes(TypeSymbol type, bool asDeclared)
    {
        var display = type.ToDisplayString();
        var plain = "type " + type.Name;
        if (!display.StartsWith(plain, StringComparison.Ordinal))
        {
            return display;
        }

        static string NameOf(TypeSymbol t, bool asDeclared) =>
            asDeclared && t.GenericDefinition is GenericTypeSymbol generic ? generic.Name + "<" + string.Join(", ", generic.ParameterNames) + ">" : t.Name;
        var name = string.Join(".", TypeMembers.EnclosingTypes(type).Reverse().Append(type).Select(t => NameOf(t, asDeclared)));
        return "type " + name + display[plain.Length..];
    }

    /// <summary>CMP-730: the unit name, plus the containing type if any (<c>U1.TFoo</c>).</summary>
    private static string? DeclaredIn(Symbol symbol)
    {
        if (symbol is UnitSymbol)
        {
            return null;
        }

        var unit = symbol.ContainingUnit?.Name;
        var type = symbol.ContainingSymbol is TypeSymbol owner ? owner.Name : null;
        return unit is null ? type : type is null ? unit : unit + "." + type;
    }
}
