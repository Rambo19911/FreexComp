using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

public sealed record SignatureHelp(ImmutableArray<SignatureInfo> Signatures, int ActiveSignature, int ActiveParameter, TextSpan ApplicableSpan);

public sealed record SignatureInfo(string Label, string? Documentation, ImmutableArray<ParameterInfo> Parameters);

public sealed record ParameterInfo(string Label, TextSpan LabelSpanInSignature, string? Documentation);

/// <summary>Signature help (language-service.md §5, CMP-728, C-135).</summary>
public static class SignatureHelpService
{
    public static SignatureHelp? GetSignatureHelp(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        var tree = model.SyntaxTree;                                        // nodes handed to the model must be its own
        position = Math.Clamp(position, 0, tree.Text.Text.Length);
        var token = tree.FindToken(position);
        if (position > 0 && token.Span.Start >= position)
        {
            token = tree.FindToken(position - 1);                           // the caret right after the token it ends
        }

        ct.ThrowIfCancellationRequested();
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case InvocationExpressionSyntax call when Inside(call.ArgumentList.OpenParenToken, call.ArgumentList.CloseParenToken, position):
                    if (ForCall(doc, model, call, position) is { } help)
                    {
                        return help;
                    }

                    break;
                case ElementAccessExpressionSyntax access when Inside(access.OpenBracketToken, access.CloseBracketToken, position):
                    if (ForArrayProperty(doc, model, access, position) is { } indexed)
                    {
                        return indexed;
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>After the opening token and up to (including) the closing one; a missing closing token extends to the end.</summary>
    private static bool Inside(SyntaxToken open, SyntaxToken close, int position) =>
        !open.IsMissing && position >= open.Span.End && (close.IsMissing || position <= close.Span.Start);

    private static SignatureHelp? ForCall(Document doc, SemanticModel model, InvocationExpressionSyntax call, int position)
    {
        var list = call.ArgumentList;
        var applicable = Applicable(list.OpenParenToken, list.CloseParenToken, call.Span.End);
        var active = ActiveParameter(list.Arguments.Separators, position);
        // CMP-729: constructors, inherited calls and generic methods (an explicit type-argument list names generic methods only)
        var generics = model.GetGenericMethodCandidates(call);
        var routines = Callee(call.Expression) is GenericNameSyntax && !generics.IsEmpty
            ? [.. generics.Cast<RoutineSymbol>()]
            : model.GetOverloadCandidates(call).AddRange(generics);
        if (!routines.IsEmpty)
        {
            var arguments = list.Arguments.ToList();
            // CMP-1478 (C-419): for a name with generic methods, the candidate the call binds to (LANG-980), else the first that fits
            var chosen = !generics.IsEmpty && model.GetSymbolInfo(call.Expression).Symbol is RoutineSymbol bound ? routines.IndexOf(bound) : -1;
            var best = chosen >= 0 && routines[chosen].Parameters.Length > active ? chosen : Best(model, routines, arguments, active);
            return new SignatureHelp([.. routines.Select(r => Routine(doc, r))], best, active, applicable);
        }

        var name = call.Expression switch
        {
            SimpleNameSyntax simple => simple.Identifier,
            _ => default,
        };
        var intrinsic = model.GetSymbolInfo(call.Expression).Symbol as IntrinsicSymbol
            ?? (name.Kind == SyntaxKind.IdentifierToken ? model.LookupSymbols(call.Span.Start, name.Text).OfType<IntrinsicSymbol>().FirstOrDefault() : null);
        return intrinsic is null ? null : new SignatureHelp([Intrinsic(intrinsic)], 0, active, applicable);
    }

    private static SimpleNameSyntax? Callee(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple,
        MemberAccessExpressionSyntax access => access.Name,
        _ => null,
    };

    private static SignatureHelp? ForArrayProperty(Document doc, SemanticModel model, ElementAccessExpressionSyntax access, int position)
    {
        var property = model.GetSymbolInfo(access.Expression).Symbol as PropertySymbol;
        if (property is not { IsArrayProperty: true } && model.GetTypeInfo(access.Expression).Type is { } type
            && type is ClassTypeSymbol or InterfaceTypeSymbol or RecordTypeSymbol)
        {
            property = TypeMembers.SelfAndAncestors(type).SelectMany(t => t.Members).OfType<PropertySymbol>().FirstOrDefault(p => p.IsDefault);
        }

        if (property is not { IsArrayProperty: true })
        {
            return null;
        }

        var active = ActiveParameter(access.Indexes.Separators, position);
        var label = property.ToDisplayString();
        var rendered = DocCommentRenderer.Of(doc.Compilation, property);
        return new SignatureHelp([new SignatureInfo(label, rendered?.Summary, Parameters(label, property.Parameters, rendered))], 0, active,
            Applicable(access.OpenBracketToken, access.CloseBracketToken, access.Span.End));
    }

    private static TextSpan Applicable(SyntaxToken open, SyntaxToken close, int end) =>
        TextSpan.FromBounds(open.Span.End, close.IsMissing ? Math.Max(open.Span.End, end) : close.Span.Start);

    /// <summary>CMP-728: the number of top-level commas before the caret.</summary>
    private static int ActiveParameter(IEnumerable<SyntaxToken> separators, int position) =>
        separators.Count(s => !s.IsMissing && s.Span.End <= position);

    /// <summary>CMP-728: the first candidate applicable to the arguments typed so far (LANG-442), else the first with enough parameters.</summary>
    private static int Best(SemanticModel model, ImmutableArray<RoutineSymbol> routines, IReadOnlyList<ArgumentSyntax> arguments, int active)
    {
        var count = Math.Max(arguments.Count, active + 1);
        var conversions = new Conversions();
        for (var i = 0; i < routines.Length; i++)
        {
            var routine = routines[i];
            if (routine.Parameters.Length < count)
            {
                continue;
            }

            var applicable = true;
            for (var a = 0; a < arguments.Count && applicable; a++)
            {
                if (arguments[a].Expression.IsMissing || model.GetTypeInfo(arguments[a].Expression).Type is not { TypeKind: not TypeKind.Error } type)
                {
                    continue;                                               // not typed yet: does not decide
                }

                applicable = conversions.Classify(type, routine.Parameters[a].Type).Exists || routine.Parameters[a].IsUntyped;
            }

            if (applicable)
            {
                return i;
            }
        }

        var first = routines.Select((r, i) => (r, i)).FirstOrDefault(p => p.r.Parameters.Length >= count);
        return first.r is null ? 0 : first.i;
    }

    private static SignatureInfo Routine(Document doc, RoutineSymbol routine)
    {
        var label = routine.ToDisplayString();
        var rendered = DocCommentRenderer.Of(doc.Compilation, routine);   // CMP-733: the summary; each parameter its <param>
        if (routine is MethodSymbol { GenericMethodDefinition: { } generic } method && !method.GenericMethodArguments.IsEmpty)
        {
            // CMP-729 (c, d): Name<Integer>(…) with the type arguments, Name<T>(…) while they are open
            var at = label.IndexOf("." + method.Name, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                at += 1 + method.Name.Length;
                label = label[..at] + "<" + string.Join(", ", method.GenericMethodArguments.Select(a => a.Name)) + ">" + label[at..];
            }

            rendered ??= DocCommentRenderer.Of(doc.Compilation, generic);
        }

        return new SignatureInfo(label, rendered?.Summary, Parameters(label, routine.Parameters, rendered));
    }

    /// <summary>The parameters as they appear in the label (in order, each searched after the previous one).</summary>
    private static ImmutableArray<ParameterInfo> Parameters(string label, ImmutableArray<ParameterSymbol> parameters, DocCommentRenderer.Rendered? rendered)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterInfo>();
        var from = Math.Max(0, label.IndexOfAny(['(', '[']));
        foreach (var parameter in parameters)
        {
            var text = ParameterSymbol.Declaration(parameter);
            var at = label.IndexOf(text, from, StringComparison.Ordinal);
            if (at < 0)
            {
                builder.Add(new ParameterInfo(text, new TextSpan(from, 0), rendered?.Parameter(parameter.Name)));
                continue;
            }

            builder.Add(new ParameterInfo(text, new TextSpan(at, text.Length), rendered?.Parameter(parameter.Name)));
            from = at + text.Length;
        }

        return builder.ToImmutable();
    }

    /// <summary>intrinsics.md informal signature: the pieces between the outer parentheses, split at <c>;</c> and <c>,</c>.</summary>
    private static SignatureInfo Intrinsic(IntrinsicSymbol intrinsic)
    {
        var label = intrinsic.ToDisplayString();
        var open = label.IndexOf('(', StringComparison.Ordinal);
        var close = label.LastIndexOf(')');
        var parameters = ImmutableArray.CreateBuilder<ParameterInfo>();
        if (open >= 0 && close > open)
        {
            var start = open + 1;
            for (var i = start; i <= close; i++)
            {
                if (i < close && label[i] is not (';' or ','))
                {
                    continue;
                }

                var s = start;
                var e = i;
                while (s < e && label[s] is ' ' or '[' or ']')
                {
                    s++;
                }

                while (e > s && label[e - 1] is ' ' or '[' or ']')
                {
                    e--;
                }

                if (e > s)
                {
                    parameters.Add(new ParameterInfo(label[s..e], TextSpan.FromBounds(s, e), null));
                }

                start = i + 1;
            }
        }

        return new SignatureInfo(label, null, parameters.ToImmutable());
    }
}
