using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// Object Pascal indentation (IDE-ED-007, IDE-ED-008) from the compiler's tokens: comments and strings are trivia or
/// literal tokens, so keywords inside them never count. Opener pairing follows IDE-ED-052.
/// </summary>
public sealed class PascalIndentRules : IIndentRules
{
    private const string AsmWord = "asm"; // not a reserved word of the compiler's lexer

    private static ImmutableArray<SyntaxToken> Tokenize(string text)
    {
        var tokens = Lexer.Tokenize(SourceText.From(EditorOperations.LexerPath, text));
        return tokens.RemoveAt(tokens.Length - 1); // without EndOfFileToken
    }

    private static bool IsAsm(SyntaxToken token) =>
        token.Kind == SyntaxKind.IdentifierToken && string.Equals(token.Text, AsmWord, StringComparison.OrdinalIgnoreCase);

    private static SyntaxKind Contextual(SyntaxToken token) =>
        token.Kind == SyntaxKind.IdentifierToken ? SyntaxFacts.GetKeywordKind(token.Text) : SyntaxKind.None;

    public bool IncreasesIndent(string textLeftOfCaret)
    {
        ArgumentNullException.ThrowIfNull(textLeftOfCaret);
        var tokens = Tokenize(textLeftOfCaret);
        if (tokens.Length == 0)
            return false;
        var last = tokens[^1];
        var previous = tokens.Length > 1 ? tokens[^2].Kind : SyntaxKind.None;
        switch (last.Kind)
        {
            case SyntaxKind.BeginKeyword or SyntaxKind.TryKeyword or SyntaxKind.RepeatKeyword or SyntaxKind.RecordKeyword
                or SyntaxKind.ClassKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.ThenKeyword or SyntaxKind.ElseKeyword
                or SyntaxKind.DoKeyword or SyntaxKind.ExceptKeyword or SyntaxKind.FinallyKeyword or SyntaxKind.VarKeyword
                or SyntaxKind.ConstKeyword or SyntaxKind.TypeKeyword or SyntaxKind.UsesKeyword
                or SyntaxKind.InitializationKeyword or SyntaxKind.FinalizationKeyword:
                return true;
            case SyntaxKind.InterfaceKeyword:
                return previous == SyntaxKind.EqualsToken; // a type declaration, not the unit section
            case SyntaxKind.OfKeyword:
                return tokens.Any(t => t.Kind == SyntaxKind.CaseKeyword); // case … of
            case SyntaxKind.ColonToken:
                // A case arm: the line's first token is not 'case' and no ':=' comes before the colon.
                return tokens[0].Kind != SyntaxKind.CaseKeyword && !tokens.Any(t => t.Kind == SyntaxKind.ColonEqualsToken);
            case SyntaxKind.CloseParenToken:
                return IsHeadingWithAncestors(tokens); // "TFoo = class(TBase)" (lead decision pending)
            case SyntaxKind.IdentifierToken:
                return IsAsm(last) || Contextual(last) is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword
                    or SyntaxKind.PublicKeyword or SyntaxKind.PublishedKeyword;
            default:
                return false;
        }
    }

    /// <summary>The tokens end with <c>= class( … )</c> or <c>= interface( … )</c>.</summary>
    private static bool IsHeadingWithAncestors(ImmutableArray<SyntaxToken> tokens)
    {
        int depth = 0, i = tokens.Length - 1;
        for (; i >= 0; i--)
        {
            if (tokens[i].Kind == SyntaxKind.CloseParenToken) depth++;
            else if (tokens[i].Kind == SyntaxKind.OpenParenToken && --depth == 0) break;
        }

        return i >= 2 && tokens[i - 1].Kind is SyntaxKind.ClassKeyword or SyntaxKind.InterfaceKeyword
               && tokens[i - 2].Kind == SyntaxKind.EqualsToken;
    }

    public int? MatchingOpenerLine(ITextBuffer buffer, int closerEnd)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!EndsCloserWord(buffer, closerEnd))
            return null; // cheap pre-check before lexing the document on every typed character
        var tokens = Tokenize(buffer.Text);
        int k = -1;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Span.End == closerEnd)
            {
                k = i;
                break;
            }
        }

        if (k < 0 || tokens[k].Kind is not (SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword or SyntaxKind.ExceptKeyword
                or SyntaxKind.FinallyKeyword or SyntaxKind.ElseKeyword))
            return null;
        int Line(int index) => buffer.GetPosition(tokens[index].Span.Start).Line;
        if (k > 0 && Line(k - 1) == Line(k))
            return null; // not the first token of its line
        if (tokens[k].Kind == SyntaxKind.ElseKeyword)
            return ElseOpener(tokens, k) is { } opener ? Line(opener) : null;

        var stack = OpenBlocks(tokens, k);
        if (stack.Count == 0)
            return null;
        var top = tokens[stack[^1]].Kind;
        var matches = tokens[k].Kind switch
        {
            SyntaxKind.EndKeyword => top != SyntaxKind.RepeatKeyword,
            SyntaxKind.UntilKeyword => top == SyntaxKind.RepeatKeyword,
            _ => top == SyntaxKind.TryKeyword, // except, finally
        };
        return matches ? Line(stack[^1]) : null;
    }

    private static readonly string[] CloserWords = ["end", "until", "except", "finally", "else"];

    /// <summary>The identifier characters ending at <paramref name="offset"/> spell a closer (the lexer decides afterwards).</summary>
    private static bool EndsCloserWord(ITextBuffer buffer, int offset)
    {
        if (offset <= 0 || offset > buffer.Length || (offset < buffer.Length && IsWordChar(buffer.GetText(offset, 1)[0])))
            return false;
        int start = offset;
        while (start > 0 && offset - start < 8 && IsWordChar(buffer.GetText(start - 1, 1)[0]))
            start--;
        var word = buffer.GetText(start, offset - start);
        return (start == 0 || !IsWordChar(buffer.GetText(start - 1, 1)[0]))
               && CloserWords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsWordChar(char c) => c == '_' || char.IsLetterOrDigit(c);

    /// <summary>IDE-ED-052 pairing of the tokens before <paramref name="end"/>: the indices of the still open openers.</summary>
    private static List<int> OpenBlocks(ImmutableArray<SyntaxToken> tokens, int end)
    {
        var stack = new List<int>();
        for (int i = 0; i < end; i++)
        {
            var top = stack.Count > 0 ? tokens[stack[^1]].Kind : SyntaxKind.None;
            if (IsBlockOpener(tokens, i, top))
                stack.Add(i);
            else if (ClosesBlock(tokens[i].Kind, top))
                stack.RemoveAt(stack.Count - 1);
        }

        return stack;
    }

    /// <summary>IDE-ED-052: the token at <paramref name="i"/> opens a block while <paramref name="top"/> is the innermost open one.</summary>
    internal static bool IsBlockOpener(ImmutableArray<SyntaxToken> tokens, int i, SyntaxKind top)
    {
        SyntaxKind KindAt(int k) => k >= 0 && k < tokens.Length ? tokens[k].Kind : SyntaxKind.None;
        var prev = KindAt(i - 1);
        var next = KindAt(i + 1);
        return tokens[i].Kind switch
        {
            SyntaxKind.BeginKeyword or SyntaxKind.TryKeyword or SyntaxKind.RecordKeyword or SyntaxKind.RepeatKeyword => true,
            SyntaxKind.CaseKeyword => top != SyntaxKind.RecordKeyword,
            SyntaxKind.ObjectKeyword => prev == SyntaxKind.EqualsToken,
            SyntaxKind.IdentifierToken => IsAsm(tokens[i]),
            SyntaxKind.ClassKeyword => prev == SyntaxKind.EqualsToken && !IsShortForm(tokens, i)
                && next is not (SyntaxKind.SemicolonToken or SyntaxKind.OfKeyword or SyntaxKind.FunctionKeyword
                    or SyntaxKind.ProcedureKeyword or SyntaxKind.VarKeyword or SyntaxKind.PropertyKeyword
                    or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword)
                && (i + 1 >= tokens.Length || Contextual(tokens[i + 1]) != SyntaxKind.OperatorKeyword),
            SyntaxKind.InterfaceKeyword => prev == SyntaxKind.EqualsToken && next != SyntaxKind.SemicolonToken && !IsShortForm(tokens, i),
            _ => false,
        };
    }

    /// <summary>IDE-ED-052: <c>end</c> closes any open block but <c>repeat</c>, <c>until</c> only <c>repeat</c>.</summary>
    internal static bool ClosesBlock(SyntaxKind kind, SyntaxKind top) => kind switch
    {
        SyntaxKind.EndKeyword => top is not (SyntaxKind.None or SyntaxKind.RepeatKeyword),
        SyntaxKind.UntilKeyword => top == SyntaxKind.RepeatKeyword,
        _ => false,
    };

    /// <summary><c>class(TBase);</c> / <c>interface(IBase);</c>: a complete declaration without a body.</summary>
    private static bool IsShortForm(ImmutableArray<SyntaxToken> tokens, int i)
    {
        if (i + 1 >= tokens.Length || tokens[i + 1].Kind != SyntaxKind.OpenParenToken)
            return false;
        int depth = 0, k = i + 1;
        for (; k < tokens.Length; k++)
        {
            if (tokens[k].Kind == SyntaxKind.OpenParenToken) depth++;
            else if (tokens[k].Kind == SyntaxKind.CloseParenToken && --depth == 0) break;
        }

        return k + 1 < tokens.Length && tokens[k + 1].Kind == SyntaxKind.SemicolonToken;
    }

    /// <summary>
    /// The <c>if</c> or <c>case</c> an <c>else</c> belongs to: scanning back over closed blocks, the nearest <c>if</c> of
    /// the same statement without an <c>else</c> of its own, else the enclosing <c>case</c>.
    /// </summary>
    private static int? ElseOpener(ImmutableArray<SyntaxToken> tokens, int elseIndex)
    {
        int depth = 0, elses = 0;
        var statementEnded = false;
        for (int i = elseIndex - 1; i >= 0; i--)
        {
            switch (tokens[i].Kind)
            {
                case SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword:
                    depth++;
                    break;
                case SyntaxKind.BeginKeyword or SyntaxKind.TryKeyword or SyntaxKind.RepeatKeyword or SyntaxKind.RecordKeyword
                    or SyntaxKind.ClassKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.CaseKeyword:
                case SyntaxKind.IdentifierToken when IsAsm(tokens[i]):
                    if (depth > 0)
                    {
                        depth--;
                        break;
                    }

                    return tokens[i].Kind == SyntaxKind.CaseKeyword ? i : null; // the enclosing block
                case SyntaxKind.SemicolonToken when depth == 0:
                    statementEnded = true; // an 'if' before it is complete
                    break;
                case SyntaxKind.ElseKeyword when depth == 0:
                    elses++;
                    break;
                case SyntaxKind.IfKeyword when depth == 0 && !statementEnded:
                    if (elses == 0)
                        return i;
                    elses--;
                    break;
            }
        }

        return null;
    }
}
