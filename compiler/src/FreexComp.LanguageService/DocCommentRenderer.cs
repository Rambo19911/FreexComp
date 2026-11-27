using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

/// <summary>
/// CMP-732…734 (C-393, C-394): the documentation the editor shows for a symbol — its doc comment (LANG-142), else the plain comment
/// directly above its source declaration — with XML documentation tags rendered to plain text.
/// </summary>
internal static class DocCommentRenderer
{
    private const char ParagraphBreak = '\u0001';
    private const char LineBreak = '\u0002';

    private static readonly string[] Sections = ["summary", "param", "returns", "remarks"];

    /// <summary>A rendered comment: the full text (CMP-732), the summary alone and the parameter texts (CMP-733).</summary>
    internal sealed record Rendered(string Text, string? Summary, IReadOnlyList<(string Name, string Text)> Parameters)
    {
        /// <summary>CMP-733: the text of the <c>param</c> element named <paramref name="name"/> (case-insensitive), else null.</summary>
        public string? Parameter(string name) =>
            Parameters.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Text).FirstOrDefault();
    }

    /// <summary>The documentation of <paramref name="symbol"/> rendered (CMP-732); null without any.</summary>
    public static string? Documentation(Compilation compilation, Symbol symbol) => Of(compilation, symbol)?.Text;

    /// <summary>The rendered documentation of <paramref name="symbol"/>: its doc comment, else the plain-comment fallback (CMP-734).</summary>
    public static Rendered? Of(Compilation compilation, Symbol symbol) =>
        Render(symbol.Documentation ?? PlainComment(compilation, symbol));

    /// <summary>CMP-732: <paramref name="text"/> (stripped as CMP-730) rendered; shown as it is when it has no top-level section or is not well-formed.</summary>
    public static Rendered? Render(string? text)
    {
        if (text is null)
        {
            return null;
        }

        XElement root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreProcessingInstructions = true };
            using var reader = XmlReader.Create(new StringReader("<doc>" + text + "</doc>"), settings);
            root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return new Rendered(text, text, []);
        }

        if (!root.Elements().Any(e => IsSection(e, out _)))
        {
            return new Rendered(text, text, []);
        }

        var summary = new StringBuilder();
        var outside = new StringBuilder();
        var parameters = new List<(string Name, string Text)>();
        string? returns = null;
        string? remarks = null;
        foreach (var node in root.Nodes())
        {
            if (node is XElement element && IsSection(element, out var section))
            {
                switch (section)
                {
                    case "summary":
                        Inline(element.Nodes(), summary.Append(' '));
                        break;
                    case "param":
                        parameters.Add(((string?)element.Attribute("name") ?? "", Paragraphs(element.Nodes()).Replace("\n\n", " ", StringComparison.Ordinal).Replace('\n', ' ')));
                        break;
                    case "returns":
                        returns = Paragraphs(element.Nodes());
                        break;
                    default:
                        remarks = Paragraphs(element.Nodes());
                        break;
                }
            }
            else
            {
                Inline([node], outside);
            }
        }

        var summaryText = Finish(outside.Append(' ').Append(summary).ToString());
        var parts = new List<string>();
        if (summaryText.Length > 0)
        {
            parts.Add(summaryText);
        }

        if (parameters.Count > 0)
        {
            parts.Add("Parameters:\n" + string.Join("\n", parameters.Select(p => "  " + p.Name + " – " + p.Text)));
        }

        if (returns is not null)
        {
            parts.Add("Returns: " + returns);
        }

        if (remarks is not null)
        {
            parts.Add("Remarks: " + remarks);
        }

        return new Rendered(string.Join("\n\n", parts), summaryText.Length > 0 ? summaryText : null, parameters);
    }

    private static bool IsSection(XElement element, out string name)
    {
        name = Sections.FirstOrDefault(s => string.Equals(s, element.Name.LocalName, StringComparison.OrdinalIgnoreCase)) ?? "";
        return name.Length > 0;
    }

    private static string Paragraphs(IEnumerable<XNode> nodes)
    {
        var builder = new StringBuilder();
        Inline(nodes, builder);
        return Finish(builder.ToString());
    }

    /// <summary>The inline content of a section: <c>c</c>, <c>see</c>, <c>paramref</c>… as plain text; <c>para</c> and <c>br</c> as break marks.</summary>
    private static void Inline(IEnumerable<XNode> nodes, StringBuilder builder)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement element:
                    switch (element.Name.LocalName.ToLowerInvariant())
                    {
                        case "para":
                            builder.Append(ParagraphBreak);
                            Inline(element.Nodes(), builder);
                            builder.Append(ParagraphBreak);
                            break;
                        case "br":
                            builder.Append(LineBreak);
                            Inline(element.Nodes(), builder);
                            break;
                        case "see" or "seealso" when !element.Nodes().Any():
                            builder.Append((string?)element.Attribute("cref") ?? (string?)element.Attribute("langword") ?? (string?)element.Attribute("href"));
                            break;
                        case "paramref" or "typeparamref" when !element.Nodes().Any():
                            builder.Append((string?)element.Attribute("name"));
                            break;
                        default:
                            Inline(element.Nodes(), builder);    // other elements: dropped, their content kept
                            break;
                    }

                    break;
            }
        }
    }

    /// <summary>White space collapsed inside each line; paragraphs joined by a blank line, empty ones dropped.</summary>
    private static string Finish(string marked)
    {
        var paragraphs = marked.Split(ParagraphBreak)
            .Select(p => string.Join("\n", p.Split(LineBreak).Select(Collapse)).Trim('\n'))
            .Where(p => p.Length > 0);
        return string.Join("\n\n", paragraphs);
    }

    private static string Collapse(string line) => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ------------------------------------------------------------------------------------------------ CMP-734 fallback

    /// <summary>
    /// CMP-734 (C-394): the ordinary comment ending on the line directly above a source declaration of <paramref name="symbol"/> — a block,
    /// or a run of consecutive <c>//</c> lines — that starts its line; stripped as CMP-730. A file that cannot be read gives none.
    /// </summary>
    private static string? PlainComment(Compilation compilation, Symbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!compilation.TryGetSyntaxTree(location.Path, out var tree) || location.Span.End > tree.Text.Text.Length)
            {
                continue;
            }

            var node = tree.FindToken(location.Span.Start).Parent?.AncestorsAndSelf().FirstOrDefault(n => n is RoutineDeclarationSyntax
                or MethodDeclarationSyntax or TypeDeclarationSyntax or VarDeclarationSyntax or ConstDeclarationSyntax or FieldDeclarationSyntax
                or PropertyDeclarationSyntax or EnumMemberSyntax);
            if (node is null || !Declares(node, symbol) || Above(tree, node.FirstToken().LeadingTrivia) is not { } pieces)
            {
                continue;
            }

            return DocCommentText.Strip(pieces);
        }

        return null;
    }

    /// <summary>The declaration node is one of the symbol's own kind (a parameter or a type parameter is not its routine or type).</summary>
    private static bool Declares(SyntaxNode node, Symbol symbol) => symbol switch
    {
        RoutineSymbol or GenericMethodSymbol => node is RoutineDeclarationSyntax or MethodDeclarationSyntax,
        TypeSymbol => node is TypeDeclarationSyntax,
        ConstantSymbol => node is ConstDeclarationSyntax,
        EnumMemberSymbol => node is EnumMemberSyntax,
        VariableSymbol or FieldSymbol => node is VarDeclarationSyntax or FieldDeclarationSyntax,
        PropertySymbol => node is PropertyDeclarationSyntax,
        _ => false,
    };

    /// <summary>The comment pieces ending on the line directly above the token whose leading trivia this is; null without one.</summary>
    private static List<string>? Above(SyntaxTree tree, IReadOnlyList<SyntaxTrivia> trivia)
    {
        var i = trivia.Count - 1;
        if (!LineBreakBefore(trivia, ref i) || i < 0 || trivia[i].Kind is not (SyntaxKind.BlockCommentTrivia or SyntaxKind.LineCommentTrivia))
        {
            return null;
        }

        var pieces = new List<SyntaxTrivia> { trivia[i] };
        while (trivia[i].Kind == SyntaxKind.LineCommentTrivia)
        {
            var j = i - 1;
            if (!LineBreakBefore(trivia, ref j) || j < 0 || trivia[j].Kind != SyntaxKind.LineCommentTrivia)
            {
                break;
            }

            i = j;
            pieces.Insert(0, trivia[i]);
        }

        // (b) the comment starts its line: only white space before it on its first line
        var text = tree.Text.Text;
        var start = pieces[0].Span.Start;
        var lineStart = text.LastIndexOfAny(['\r', '\n'], Math.Max(0, start - 1)) + 1;
        if (start > 0 && text.AsSpan(lineStart, start - lineStart).Trim(" \t").Length > 0)
        {
            return null;
        }

        return [.. pieces.Select(p => p.Text)];
    }

    /// <summary>
    /// Steps <paramref name="i"/> back over white space and exactly one line break (to the trivia before them); false when there is no
    /// line break or more than one (a blank line).
    /// </summary>
    private static bool LineBreakBefore(IReadOnlyList<SyntaxTrivia> trivia, ref int i)
    {
        var breaks = 0;
        for (; i >= 0; i--)
        {
            if (trivia[i].Kind == SyntaxKind.EndOfLineTrivia)
            {
                breaks++;
            }
            else if (trivia[i].Kind != SyntaxKind.WhitespaceTrivia)
            {
                break;
            }
        }

        return breaks == 1;
    }
}
