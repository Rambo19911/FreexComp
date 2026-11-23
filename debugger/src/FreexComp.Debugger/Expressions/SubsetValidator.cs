using FreexComp.Compiler.Syntax;

namespace FreexComp.Debugger.Expressions;

/// <summary>An expression ready for evaluation.</summary>
/// <param name="Text">The expression text without format suffix, trimmed, as typed.</param>
/// <param name="Tree">The evaluator's tree.</param>
/// <param name="Format">The format suffix, if any.</param>
/// <param name="Count">The element count of a count suffix.</param>
internal sealed record LoweredExpression(string Text, Ast Tree, FormatSpec Format, int Count);

/// <summary>
/// Walks the compiler's syntax nodes of an expression, rejects every node kind outside the accepted subset (Pascal
/// values §9.1) and lowers the rest to the evaluator's <see cref="Ast"/>. <c>Name(…)</c> is a builtin, a typecast (the
/// name of a type of the type information) or not supported.
/// </summary>
internal sealed class SubsetValidator(Func<string, bool> isTypeName)
{
    /// <summary>The builtins of §9.5 under their canonical spelling.</summary>
    public static readonly IReadOnlyList<string> Builtins =
        ["Length", "High", "Low", "Ord", "Chr", "Abs", "SizeOf", "Assigned", "Odd", "Succ", "Pred"];

    /// <summary>Front end + validation + lowering.</summary>
    /// <exception cref="DebuggerException"><c>ExpressionSyntax</c> for a syntax error or a construct outside the subset; <c>ExpressionEvaluation</c> for a call that is neither builtin nor typecast.</exception>
    public static LoweredExpression Parse(string text, Func<string, bool> isTypeName, bool allowFormatSuffix = false)
    {
        var parsed = ExpressionFrontEnd.Parse(text, allowFormatSuffix);
        return new LoweredExpression(parsed.Text, new SubsetValidator(isTypeName).Lower(parsed.Root), parsed.Format, parsed.Count);
    }

    public Ast Lower(ExpressionSyntax syntax)
    {
        switch (syntax)
        {
            case LiteralExpressionSyntax literal:
                return literal.Token.Value switch
                {
                    long value => new Ast.IntLit(value, false),
                    ulong value => new Ast.IntLit(unchecked((long)value), true),
                    double value => new Ast.FloatLit(value),
                    string { Length: 1 } value => new Ast.CharLit(value[0]),
                    string value => new Ast.StrLit(value),
                    _ => new Ast.NilLit(),
                };

            case IdentifierNameSyntax name:
                return new Ast.Name(IdentifierText(name.Identifier));
            case PredefinedTypeExpressionSyntax predefined:
                return new Ast.Name(predefined.Keyword.Text);   // the type "string", e.g. in SizeOf(string)
            case ParenthesizedExpressionSyntax parenthesized:
                return Lower(parenthesized.Expression);

            case UnaryExpressionSyntax unary:
                return new Ast.Unary(OperatorText(unary.OperatorToken), Lower(unary.Operand));
            case AddressOfExpressionSyntax address:
                return new Ast.Unary("@", Lower(address.Operand));

            case BinaryExpressionSyntax binary:
                if (binary.OperatorToken.Kind is SyntaxKind.AsKeyword or SyntaxKind.IsKeyword)
                    throw Rejected(OperatorText(binary.OperatorToken), binary.OperatorToken);
                return new Ast.Binary(OperatorText(binary.OperatorToken), Lower(binary.Left), Lower(binary.Right));

            case MemberAccessExpressionSyntax member:
                if (member.Name is not IdentifierNameSyntax)
                    throw Rejected("generic name", member.Name.Identifier);
                return new Ast.Field(Lower(member.Expression), IdentifierText(member.Name.Identifier));

            case ElementAccessExpressionSyntax element:
            {
                var target = Lower(element.Expression);
                foreach (var index in element.Indexes)
                    target = new Ast.Index(target, Lower(index));
                return target;
            }

            case DereferenceExpressionSyntax dereference:
                return new Ast.Deref(Lower(dereference.Expression));

            case InvocationExpressionSyntax invocation:
                return LowerInvocation(invocation);

            case SetConstructorSyntax set:
                return new Ast.SetCtor([.. set.Elements.Select(e =>
                    new Ast.SetItem(Lower(e.Expression), e.UpperBound is null ? null : Lower(e.UpperBound)))]);

            case GenericNameSyntax:
                throw Rejected("generic name", syntax.FirstToken());
            case InheritedExpressionSyntax:
                throw Rejected("inherited", syntax.FirstToken());
            case StringCastExpressionSyntax:
                throw Rejected("string cast", syntax.FirstToken());
            case AnonymousMethodExpressionSyntax:
                throw Rejected("anonymous method", syntax.FirstToken());
            default:
                throw Rejected(syntax.Kind.ToString(), syntax.FirstToken());
        }
    }

    private Ast LowerInvocation(InvocationExpressionSyntax invocation)
    {
        // Constructs inside the arguments are checked first: a rejected construct is a syntax error wherever it stands.
        var arguments = new List<Ast>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.WidthColonToken is { } colon)
                throw Rejected("argument width", colon);
            arguments.Add(Lower(argument.Expression));
        }

        if (invocation.Expression is GenericNameSyntax generic)
            throw Rejected("generic name", generic.Identifier);
        if (invocation.Expression is IdentifierNameSyntax name)
        {
            var text = IdentifierText(name.Identifier);
            if (Builtins.FirstOrDefault(b => b.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } builtin)
                return new Ast.Call(builtin, arguments);
            if (isTypeName(text))
            {
                return arguments.Count == 1
                    ? new Ast.Cast(text, arguments[0])
                    : throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.WrongArgumentCount(text));
            }
        }
        else
        {
            Lower(invocation.Expression);   // rejects what is outside the subset in the callee
        }

        // Any other call syntax (§9.5).
        throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, Messages.FunctionCallsNotSupported);
    }

    private static string IdentifierText(SyntaxToken identifier) => identifier.Text.TrimStart('&');

    private static string OperatorText(SyntaxToken op) => op.Text.ToLowerInvariant();

    private static DebuggerException Rejected(string construct, SyntaxToken at) =>
        new(DebugErrorCode.ExpressionSyntax, Messages.NotSupportedInExpressions(construct, at.Span.Start + 1));
}
