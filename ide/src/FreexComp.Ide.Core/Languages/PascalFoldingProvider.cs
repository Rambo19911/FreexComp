using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// Phase 1 Object Pascal fold regions from the compiler's token stream (IDE-P1-060, IDE-ED-051/052): keyword pairing
/// with a stack, no syntax tree. Comments and directives come from the tokens' trivia.
/// </summary>
public sealed class PascalFoldingProvider : IFoldingProvider
{
    /// <summary>The lexer needs an absolute path; it does not affect tokens (see <see cref="PascalClassifier"/>).</summary>
    private const string PlaceholderPath = @"C:\Untitled.pas";

    /// <summary><c>asm</c> is not a reserved word of the compiler's lexer (it arrives as an identifier).</summary>
    private const string AsmWord = "asm";

    private sealed record Pending(int HeadingLine, int Depth)
    {
        public bool HasBody { get; set; }
    }

    private sealed record Opener(SyntaxKind Kind, int StartLine, FoldKind RegionKind, Pending? RoutineBody);

    public IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var text = buffer.Text;
        var tokens = Lexer.Tokenize(SourceText.From(PlaceholderPath, text));
        int Line(int offset) => buffer.GetPosition(Math.Clamp(offset, 0, text.Length)).Line;
        var regions = new List<FoldRegion>();
        AddTriviaRegions(buffer, text, tokens, regions, Line);
        AddTokenRegions(buffer, tokens, regions, Line);
        return FoldRegions.Sorted(regions);
    }

    /// <summary>(f) comments, (g) <c>{$REGION}</c> pairs, (h) conditional blocks.</summary>
    internal static void AddTriviaRegions(ITextBuffer buffer, string text, ImmutableArray<SyntaxToken> tokens, List<FoldRegion> regions, Func<int, int> line)
    {
        var regionStack = new Stack<(int Line, string Title)>();
        var conditionalStack = new Stack<int>();
        int runFirst = 0, runLast = -1;

        void FlushRun()
        {
            if (runLast - runFirst >= 2)
                regions.Add(buffer, runFirst, runLast, FoldKind.LineComments);
            runLast = -1;
        }

        foreach (var token in tokens)
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                var span = trivia.Span;
                switch (trivia.Kind)
                {
                    case SyntaxKind.LineCommentTrivia:
                    case SyntaxKind.DocCommentTrivia when trivia.Text.StartsWith("//", StringComparison.Ordinal):
                        int commentLine = line(span.Start);
                        if (!StartsItsLine(text, span.Start))
                            break;
                        if (commentLine != runLast + 1)
                        {
                            FlushRun();
                            runFirst = commentLine;
                        }

                        runLast = commentLine;
                        break;
                    case SyntaxKind.BlockCommentTrivia:
                    case SyntaxKind.DocCommentTrivia:
                        regions.Add(buffer, line(span.Start), line(span.End - 1), FoldKind.Comment);
                        break;
                    case SyntaxKind.DirectiveTrivia when trivia.Directive is { } directive:
                        int directiveLine = line(span.Start);
                        switch (directive.Name)
                        {
                            case "REGION":
                                regionStack.Push((directiveLine, RegionTitle(directive.Argument)));
                                break;
                            case "ENDREGION" when regionStack.Count > 0:
                                var (start, title) = regionStack.Pop();
                                regions.Add(buffer, start, directiveLine, FoldKind.Region, title);
                                break;
                            case "IFDEF" or "IFNDEF" or "IF":
                                conditionalStack.Push(directiveLine);
                                break;
                            case "ENDIF" or "IFEND" when conditionalStack.Count > 0:
                                int conditionalStart = conditionalStack.Pop();
                                if (directiveLine - conditionalStart >= 2)
                                    regions.Add(buffer, conditionalStart, directiveLine, FoldKind.Conditional);
                                break;
                        }

                        break;
                }
            }
        }

        FlushRun();
    }

    /// <summary>Only whitespace before <paramref name="offset"/> on its line.</summary>
    private static bool StartsItsLine(string text, int offset)
    {
        for (int i = offset - 1; i >= 0 && text[i] is not ('\r' or '\n'); i--)
        {
            if (text[i] is not (' ' or '\t'))
                return false;
        }

        return true;
    }

    /// <summary><c>'name'</c> → name (doubled quotes undone); an unquoted argument as written.</summary>
    private static string RegionTitle(string argument)
    {
        var a = argument.Trim();
        return a.Length >= 2 && a[0] == '\'' && a[^1] == '\'' ? a[1..^1].Replace("''", "'", StringComparison.Ordinal) : a;
    }

    /// <summary>(a) routines, (b) types, (c) sections, (d) uses, (e) blocks.</summary>
    private static void AddTokenRegions(ITextBuffer buffer, ImmutableArray<SyntaxToken> tokens, List<FoldRegion> regions, Func<int, int> line)
    {
        var stack = new List<Opener>();
        var pendings = new List<Pending>();
        var sections = new List<int>();
        int finalEndLine = 0;
        bool inInterface = false;
        int count = tokens.Length - 1; // without EndOfFileToken

        SyntaxKind KindAt(int i) => i >= 0 && i < count ? tokens[i].Kind : SyntaxKind.None;
        bool IsWord(int i, string word) => KindAt(i) == SyntaxKind.IdentifierToken && string.Equals(tokens[i].Text, word, StringComparison.OrdinalIgnoreCase);
        SyntaxKind ContextualAt(int i) => KindAt(i) == SyntaxKind.IdentifierToken ? SyntaxFacts.GetKeywordKind(tokens[i].Text) : SyntaxKind.None;
        bool TopIs(params SyntaxKind[] kinds) => stack.Count > 0 && kinds.Contains(stack[^1].Kind);
        bool InTypeBody() => stack.Count > 0 && stack[^1].RegionKind == FoldKind.Type;
        int TypeNameLine(int i) => line(tokens[Math.Max(0, KindAt(i - 1) == SyntaxKind.PackedKeyword ? i - 3 : i - 2)].Span.Start);

        // "class(TBase);" / "interface(IBase);": a complete declaration without a body.
        bool IsShortForm(int i)
        {
            if (KindAt(i + 1) != SyntaxKind.OpenParenToken)
                return false;
            int depth = 0, k = i + 1;
            for (; k < count; k++)
            {
                if (KindAt(k) == SyntaxKind.OpenParenToken) depth++;
                else if (KindAt(k) == SyntaxKind.CloseParenToken && --depth == 0) break;
            }

            return KindAt(k + 1) == SyntaxKind.SemicolonToken;
        }

        void Push(SyntaxKind kind, int startLine, FoldKind regionKind, Pending? body = null) => stack.Add(new Opener(kind, startLine, regionKind, body));

        void PushBlock(SyntaxKind kind, int i)
        {
            Pending? body = null;
            if (pendings.Count > 0 && !pendings[^1].HasBody && pendings[^1].Depth == stack.Count)
            {
                body = pendings[^1];
                body.HasBody = true;
            }

            Push(kind, line(tokens[i].Span.Start), FoldKind.Block, body);
        }

        void Pop(int endLine)
        {
            var opener = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            if (opener.RoutineBody is { } routine)
            {
                pendings.Remove(routine);
                regions.Add(buffer, routine.HeadingLine, endLine, FoldKind.Routine);
            }
            else if (opener.RegionKind == FoldKind.Type || endLine - opener.StartLine >= 2)
                regions.Add(buffer, opener.StartLine, endLine, opener.RegionKind);
        }

        void StartSection(int i, bool clear)
        {
            sections.Add(line(tokens[i].Span.Start));
            pendings.Clear();
            if (clear)
                stack.Clear();
        }

        for (int i = 0; i < count; i++)
        {
            var kind = KindAt(i);
            var prev = KindAt(i - 1);
            var next = KindAt(i + 1);
            switch (kind)
            {
                case SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword:
                case SyntaxKind.IdentifierToken when prev == SyntaxKind.ClassKeyword && ContextualAt(i) == SyntaxKind.OperatorKeyword:
                    // A routine implementation heading: not a declaration in an interface section or a type, not a
                    // procedural type or anonymous method (those follow '=', ':', ':=', '(', 'to', …).
                    if (!inInterface && !InTypeBody()
                        && (i == 0 || prev is SyntaxKind.SemicolonToken or SyntaxKind.ClassKeyword or SyntaxKind.CloseBracketToken or SyntaxKind.ImplementationKeyword))
                    {
                        int headingLine = line(tokens[prev == SyntaxKind.ClassKeyword ? i - 1 : i].Span.Start);
                        pendings.Add(new Pending(headingLine, stack.Count));
                    }

                    break;
                case SyntaxKind.IdentifierToken when IsWord(i, AsmWord):
                    PushBlock(SyntaxKind.IdentifierToken, i);
                    break;
                case SyntaxKind.IdentifierToken when prev == SyntaxKind.SemicolonToken
                                                     && ContextualAt(i) is SyntaxKind.ForwardKeyword or SyntaxKind.ExternalKeyword:
                    if (pendings.Count > 0 && !pendings[^1].HasBody)
                        pendings.RemoveAt(pendings.Count - 1); // a declaration without body
                    break;
                case SyntaxKind.BeginKeyword:
                    PushBlock(kind, i);
                    break;
                case SyntaxKind.TryKeyword or SyntaxKind.RepeatKeyword:
                    Push(kind, line(tokens[i].Span.Start), FoldKind.Block);
                    break;
                case SyntaxKind.CaseKeyword when !TopIs(SyntaxKind.RecordKeyword):
                    Push(kind, line(tokens[i].Span.Start), FoldKind.Block);
                    break;
                case SyntaxKind.RecordKeyword:
                    bool named = prev == SyntaxKind.EqualsToken || (prev == SyntaxKind.PackedKeyword && KindAt(i - 2) == SyntaxKind.EqualsToken);
                    Push(kind, named ? TypeNameLine(i) : line(tokens[i].Span.Start), FoldKind.Type);
                    break;
                case SyntaxKind.ClassKeyword when prev == SyntaxKind.EqualsToken && !IsShortForm(i)
                                                  && next is not (SyntaxKind.SemicolonToken or SyntaxKind.OfKeyword or SyntaxKind.FunctionKeyword
                                                      or SyntaxKind.ProcedureKeyword or SyntaxKind.VarKeyword or SyntaxKind.PropertyKeyword
                                                      or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword)
                                                  && ContextualAt(i + 1) != SyntaxKind.OperatorKeyword:
                case SyntaxKind.ObjectKeyword when prev == SyntaxKind.EqualsToken:
                    Push(kind, TypeNameLine(i), FoldKind.Type);
                    break;
                case SyntaxKind.InterfaceKeyword when prev == SyntaxKind.EqualsToken:
                    if (next != SyntaxKind.SemicolonToken && !IsShortForm(i))
                        Push(kind, TypeNameLine(i), FoldKind.Type);
                    break;
                case SyntaxKind.InterfaceKeyword:
                    StartSection(i, clear: false);
                    inInterface = true;
                    break;
                case SyntaxKind.ImplementationKeyword or SyntaxKind.InitializationKeyword or SyntaxKind.FinalizationKeyword:
                    StartSection(i, clear: true);
                    inInterface = false;
                    break;
                case SyntaxKind.UsesKeyword:
                    int semicolon = i + 1;
                    while (semicolon < count && KindAt(semicolon) != SyntaxKind.SemicolonToken)
                        semicolon++;
                    if (semicolon < count)
                    {
                        int usesLine = line(tokens[i].Span.Start), endLine = line(tokens[semicolon].Span.Start);
                        if (endLine - usesLine >= 2)
                            regions.Add(buffer, usesLine, endLine, FoldKind.Uses);
                    }

                    break;
                case SyntaxKind.EndKeyword:
                    int end = line(tokens[i].Span.Start);
                    if (next == SyntaxKind.DotToken)
                        finalEndLine = end;
                    if (stack.Count > 0 && stack[^1].Kind != SyntaxKind.RepeatKeyword)
                        Pop(end);
                    break;
                case SyntaxKind.UntilKeyword when TopIs(SyntaxKind.RepeatKeyword):
                    Pop(line(tokens[i].Span.Start));
                    break;
            }
        }

        // (c) each section runs to the line before the next section or the final "end."
        int last = finalEndLine > 0 ? finalEndLine : buffer.LineCount + 1;
        for (int s = 0; s < sections.Count; s++)
            regions.Add(buffer, sections[s], (s + 1 < sections.Count ? sections[s + 1] : last) - 1, FoldKind.Section);
    }
}
