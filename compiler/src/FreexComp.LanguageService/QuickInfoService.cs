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

        var signature = symbol is TypeSymbol && !IdentifierComparer.Instance.Equals(symbol.Name, token.Value as string ?? token.Text)
            ? $"type {token.Text} = {symbol.Name}"                       // an alias names the type it stands for
            : symbol.ToDisplayString();
        var documentation = symbol is UnitSymbol unit
            ? unit.Locations.FirstOrDefault().Path
            : symbol.Documentation;
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
        if (!SyntaxFacts.IsNameToken(token) || !token.Span.Contains(position))
        {
            var before = position > 0 ? tree.FindToken(position - 1) : default;
            if (!SyntaxFacts.IsNameToken(before) || before.Span.End != position)
            {
                return null;
            }

            token = before;
        }

        return SymbolOf(model, token) is { } symbol ? (symbol, token) : null;
    }

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
