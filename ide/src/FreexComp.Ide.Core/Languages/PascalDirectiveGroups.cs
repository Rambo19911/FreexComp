using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// The directive stacks shared by folding (IDE-ED-051 (g), (h)) and directive matching (IDE-ED-075, 076): conditional
/// openers <c>{$IFDEF}</c>, <c>{$IFNDEF}</c>, <c>{$IF}</c>, <c>{$IFOPT}</c> push a group, <c>{$ELSEIF}</c> / <c>{$ELSE}</c>
/// join the innermost open one, <c>{$ENDIF}</c> / <c>{$IFEND}</c> end it; <c>{$REGION}</c> / <c>{$ENDREGION}</c> pair on
/// a stack of their own. Directives come from the tokens' trivia (both the <c>{$…}</c> and the <c>(*$…*)</c> form; the
/// lexer upper-cases the name), so directives in comments and strings never count. The text of an inactive branch is one
/// disabled-text trivia; it is lexed again on its own, so the groups nested in it count too.
/// </summary>
internal static class PascalDirectiveGroups
{
    /// <summary>One directive of a group: its span in the text, and the index of the (outermost) token that carries it.</summary>
    internal readonly record struct Member(int Start, int Length, DirectiveTriviaInfo Directive, int TokenIndex);

    /// <summary>
    /// A conditional group or a region pair. <see cref="IsClosed"/> false: an opener never closed, or a closer or branch
    /// without opener (a group of one) — its members have no partner.
    /// </summary>
    internal sealed class Group(bool isRegion)
    {
        public bool IsRegion { get; } = isRegion;

        public List<Member> Members { get; } = [];

        public bool IsClosed { get; set; }

        public Member Opener => Members[0];

        public Member Closer => Members[^1];
    }

    /// <summary>Every directive group of <paramref name="tokens"/>, ordered by the position of the group's first member.</summary>
    public static List<Group> Pair(ImmutableArray<SyntaxToken> tokens)
    {
        var groups = new List<Group>();
        var conditionals = new Stack<Group>();
        var regions = new Stack<Group>();

        void Single(Member member, bool isRegion)
        {
            var stray = new Group(isRegion);
            stray.Members.Add(member);
            groups.Add(stray);
        }

        void Open(Stack<Group> stack, Member member, bool isRegion)
        {
            var group = new Group(isRegion);
            group.Members.Add(member);
            groups.Add(group);
            stack.Push(group);
        }

        void Walk(ImmutableArray<SyntaxToken> lexed, int shift, int? carrier)
        {
            for (int i = 0; i < lexed.Length; i++)
            {
                foreach (var trivia in lexed[i].LeadingTrivia.Concat(lexed[i].TrailingTrivia))
                {
                    if (trivia.Kind == SyntaxKind.DisabledTextTrivia)
                    {
                        Walk(Lexer.Tokenize(SourceText.From(EditorOperations.LexerPath, trivia.Text)), shift + trivia.Span.Start, carrier ?? i);
                        continue;
                    }

                    if (trivia.Kind != SyntaxKind.DirectiveTrivia || trivia.Directive is not { } directive)
                        continue;
                    var member = new Member(shift + trivia.Span.Start, trivia.Span.Length, directive, carrier ?? i);
                    switch (directive.Name)
                    {
                        case "REGION":
                            Open(regions, member, isRegion: true);
                            break;
                        case "ENDREGION" when regions.Count > 0:
                            var region = regions.Pop();
                            region.Members.Add(member);
                            region.IsClosed = true;
                            break;
                        case "ENDREGION":
                            Single(member, isRegion: true);
                            break;
                        case "IFDEF" or "IFNDEF" or "IF" or "IFOPT":
                            Open(conditionals, member, isRegion: false);
                            break;
                        case "ELSEIF" or "ELSE" when conditionals.Count > 0:
                            conditionals.Peek().Members.Add(member);
                            break;
                        case "ENDIF" or "IFEND" when conditionals.Count > 0:
                            var conditional = conditionals.Pop();
                            conditional.Members.Add(member);
                            conditional.IsClosed = true;
                            break;
                        case "ELSEIF" or "ELSE" or "ENDIF" or "IFEND":
                            Single(member, isRegion: false);
                            break;
                    }
                }
            }
        }

        Walk(tokens, 0, null);
        return groups;
    }
}
