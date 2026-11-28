using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>One public member of a record helper: its name, its normalised declaration and, in the member list, the
/// task that adds it (<c>// task: R-nnn</c>) and the classic routine it wraps (<c>// = Name</c>).</summary>
public sealed record HelperMember(string Name, string Declaration, string? Task, string? Wraps, SyntaxNode Node);

/// <summary>A record helper: its name, the helped type (normalised text) and its public members.</summary>
public sealed record HelperDeclaration(string Name, string HelpedType, string Unit, IReadOnlyList<HelperMember> Members, HelperTypeSyntax Syntax);

/// <summary>
/// The member list of the library helpers (<c>docs/spec/rtl/helpers.md</c>, RTL-1265) and the helpers the RTL units
/// declare, both read with the compiler's parser, and their comparison (<c>HelperMembersMatchSpec</c>).
/// </summary>
public static partial class HelperMembers
{
    public static string DocumentPath => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "rtl", "helpers.md");

    [GeneratedRegex(@"^Landed tasks:(.*)$", RegexOptions.Multiline)]
    private static partial Regex LandedLine();

    [GeneratedRegex(@"//\s*task:\s*(R-\d+)")]
    private static partial Regex TaskComment();

    [GeneratedRegex(@"//\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*$")]
    private static partial Regex WrapperComment();

    /// <summary>The task ids of the <c>Landed tasks:</c> line.</summary>
    public static HashSet<string> LandedTasks(string markdown)
    {
        var line = LandedLine().Match(markdown.Replace("\r\n", "\n"));
        Assert.True(line.Success, "the member list has no 'Landed tasks:' line");
        return [.. line.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>The text of every <c>```pascal</c> block of a Markdown text.</summary>
    public static List<string> PascalBlocks(string markdown)
    {
        var blocks = new List<string>();
        StringBuilder? block = null;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (block is not null)
                {
                    blocks.Add(block.ToString());
                    block = null;
                }
                else if (line.Trim() == "```pascal")
                {
                    block = new StringBuilder();
                }

                continue;
            }

            block?.Append(line).Append('\n');
        }

        return blocks;
    }

    /// <summary>
    /// The helpers of the member list: every <c>pascal</c> block is parsed inside
    /// <c>unit D; interface type &lt;block&gt; implementation end.</c>; the task of a member is the last
    /// <c>// task:</c> line above it, its wrapped routine the <c>// = Name</c> comment that ends its last line.
    /// </summary>
    public static List<HelperDeclaration> FromDocument(string markdown)
    {
        var result = new List<HelperDeclaration>();
        foreach (var block in PascalBlocks(markdown))
        {
            const string Head = "unit D;\ninterface\ntype\n";
            var text = Head + block + "implementation\nend.\n";
            var tree = SyntaxTree.Parse(SourceText.From(DocumentPath, text), null, PhysicalFileSystem.Instance);
            var errors = tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, "a pascal block of the member list does not parse:\n" + string.Join("\n", errors) + "\n" + block);
            var lines = text.Split('\n');
            var helpers = Declarations(Assert.IsType<UnitSyntax>(tree.Root), "helpers.md").ToList();
            Assert.True(helpers.Count == 1, "a pascal block of the member list must hold exactly one helper:\n" + block);
            var helper = helpers[0];
            var members = helper.Members.Select(m =>
            {
                var first = LineOf(text, m.Node.Span.Start);
                var last = LineOf(text, m.Node.Span.End - 1);
                string? task = null;
                for (var i = first; i >= 0 && task is null; i--)
                {
                    if (TaskComment().Match(lines[i]) is { Success: true } t)
                    {
                        task = t.Groups[1].Value;
                    }
                }

                var wraps = WrapperComment().Match(lines[last]) is { Success: true } w ? w.Groups[1].Value : null;
                return m with { Task = task, Wraps = wraps };
            }).ToList();
            result.Add(helper with { Members = members });
        }

        return result;
    }

    private static int LineOf(string text, int position) => text.AsSpan(0, position).Count('\n');

    /// <summary>The record helpers that a unit declares (in its interface and implementation), with their public members.</summary>
    public static IEnumerable<HelperDeclaration> Declarations(UnitSyntax unit, string unitName) =>
        unit.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => t.Type is HelperTypeSyntax)
            .Select(t => new HelperDeclaration(t.Identifier.Text, Normalize(((HelperTypeSyntax)t.Type).ExtendedType), unitName,
                PublicMembers((HelperTypeSyntax)t.Type).ToList(), (HelperTypeSyntax)t.Type));

    /// <summary>The members of the default (public) part and of the <c>public</c> / <c>published</c> sections.</summary>
    public static IEnumerable<HelperMember> PublicMembers(HelperTypeSyntax helper)
    {
        foreach (var node in helper.Members)
        {
            if (node is VisibilitySectionSyntax section)
            {
                var word = section.VisibilityKeyword.Text.ToLowerInvariant();
                if (section.StrictKeyword is null && word is "public" or "published")
                {
                    foreach (var member in section.Members.SelectMany(Members))
                    {
                        yield return member;
                    }
                }
            }
            else
            {
                foreach (var member in Members(node))
                {
                    yield return member;
                }
            }
        }
    }

    private static IEnumerable<HelperMember> Members(SyntaxNode node)
    {
        switch (node)
        {
            case ConstSectionSyntax constants:
                foreach (var c in constants.Declarations)
                {
                    yield return new HelperMember(c.Identifier.Text, constants.Keyword.Text.ToLowerInvariant() + " " + Normalize(c), null, null, c);
                }

                break;
            case MethodDeclarationSyntax method:
                yield return new HelperMember(LastName(method.Heading.Name), Normalize(method), null, null, method);
                break;
            case PropertyDeclarationSyntax property:
                yield return new HelperMember(property.Identifier.Text, Normalize(property), null, null, property);
                break;
            case AttributeListSyntax:
                break;
            default:
                yield return new HelperMember(node.FirstToken().Text, Normalize(node), null, null, node);
                break;
        }
    }

    /// <summary>
    /// The tokens of a node separated by one space, in lower case (comments and white space are trivia). Built from the
    /// tokens because the text of a node read from an include file is empty (CMP-220).
    /// </summary>
    public static string Normalize(SyntaxNode node) =>
        string.Join(" ", node.DescendantTokens().Where(t => !t.IsMissing && t.Text.Length > 0).Select(t => t.Text.ToLowerInvariant()));

    private static string LastName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        _ => string.Concat(name.DescendantTokens().Select(t => t.Text)),
    };

    /// <summary>
    /// RTL-1265 (b): the differences between the member list and the declared helpers. A member of a landed task must
    /// be declared as listed; a member of a pending task may be absent, but the declarations of a name that is declared
    /// must equal the listed ones; a public member that is not listed and a helper without a block are errors.
    /// </summary>
    public static List<string> Compare(IReadOnlyList<HelperDeclaration> document, IReadOnlySet<string> landed, IReadOnlyList<HelperDeclaration> declared)
    {
        var problems = new List<string>();
        foreach (var helper in declared)
        {
            var spec = document.FirstOrDefault(d => string.Equals(d.Name, helper.Name, StringComparison.OrdinalIgnoreCase));
            if (spec is null)
            {
                problems.Add($"{helper.Unit}: helper {helper.Name} is declared but the member list has no block for it");
                continue;
            }

            if (spec.HelpedType != helper.HelpedType)
            {
                problems.Add($"{helper.Unit}: {helper.Name} helps '{helper.HelpedType}', the member list says '{spec.HelpedType}'");
            }

            var listed = spec.Members.Select(m => m.Declaration).ToHashSet(StringComparer.Ordinal);
            var have = helper.Members.Select(m => m.Declaration).ToHashSet(StringComparer.Ordinal);
            var names = helper.Members.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = spec.Members.Where(m => !have.Contains(m.Declaration)).ToList();
            foreach (var member in helper.Members.Where(m => !listed.Contains(m.Declaration)))
            {
                // a declaration that differs from a listed one of the same name is one difference, not two
                var partner = missing.FirstOrDefault(m => m.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase));
                if (partner is not null)
                {
                    missing.Remove(partner);
                    problems.Add($"{helper.Unit}: {helper.Name}: declared '{member.Declaration}', the member list says '{partner.Declaration}'");
                }
                else
                {
                    problems.Add($"{helper.Unit}: {helper.Name}: '{member.Declaration}' is not in the member list");
                }
            }

            foreach (var member in missing)
            {
                if (member.Task is not null && landed.Contains(member.Task))
                {
                    problems.Add($"{helper.Unit}: {helper.Name}: '{member.Declaration}' (task {member.Task}, landed) is not declared");
                }
                else if (names.Contains(member.Name))
                {
                    problems.Add($"{helper.Unit}: {helper.Name}: '{member.Declaration}' (task {member.Task}) is missing while '{member.Name}' is declared");
                }
            }
        }

        foreach (var spec in document)
        {
            if (!declared.Any(d => string.Equals(d.Name, spec.Name, StringComparison.OrdinalIgnoreCase))
                && spec.Members.Any(m => m.Task is not null && landed.Contains(m.Task)))
            {
                problems.Add($"helper {spec.Name} (landed members) is not declared in the RTL");
            }
        }

        return problems;
    }

    /// <summary>The implementation of a method of a helper: the routine <c>Helper.Name</c> with the same <c>class</c> mark and parameter types.</summary>
    public static RoutineDeclarationSyntax? Implementation(UnitSyntax unit, string helper, MethodDeclarationSyntax method)
    {
        var key = Signature(method.Heading, helper + "." + LastName(method.Heading.Name));
        return unit.ImplementationSection.DescendantNodes().OfType<RoutineDeclarationSyntax>()
            .FirstOrDefault(r => r.Block is not null && r.Heading.Name is QualifiedNameSyntax && Signature(r.Heading, string.Concat(r.Heading.Name.DescendantTokens().Select(t => t.Text))) == key);
    }

    private static string Signature(RoutineHeadingSyntax heading, string name)
    {
        var parameters = heading.ParameterList?.Parameters.SelectMany(p =>
            p.Identifiers.Select(_ => (p.Modifier?.Text ?? "") + " " + (p.Type is null ? "" : Normalize(p.Type)))) ?? [];
        return ((heading.ClassKeyword is null ? "" : "class ") + Regex.Replace(name, @"\s+", "") + "(" + string.Join(";", parameters) + ")").ToLowerInvariant();
    }

    /// <summary>
    /// RTL-1262 (a), RTL-1265 (c): the problems of a wrapper — its declaration lacks <c>inline</c>, its body is not one
    /// statement, or that statement calls no routine named <paramref name="wraps"/> (the last part of a qualified name counts).
    /// </summary>
    public static List<string> WrapperProblems(UnitSyntax unit, string helper, MethodDeclarationSyntax method, string wraps)
    {
        var problems = new List<string>();
        var what = $"{helper}.{LastName(method.Heading.Name)} ({Normalize(method)})";
        if (!method.Directives.Any(d => d.Keyword.Text.Equals("inline", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{what}: a wrapper of {wraps} is not declared inline");
        }

        var body = Implementation(unit, helper, method);
        if (body?.Block is null)
        {
            problems.Add($"{what}: no implementation found");
            return problems;
        }

        var statements = body.Block.Body.Statements.Where(s => s is not EmptyStatementSyntax).ToList();
        if (statements.Count != 1)
        {
            problems.Add($"{what}: the body of a wrapper has {statements.Count} statements, not one");
        }
        else if (!statements[0].DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c => CalleeName(c.Expression) is { } n
                     && n.Equals(wraps, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{what}: the body does not call {wraps}");
        }

        return problems;
    }

    private static string? CalleeName(ExpressionSyntax callee) => callee switch
    {
        SimpleNameSyntax s => s.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        _ => null,
    };

    /// <summary>RTL-1262 (d): a class var, class constructor or class destructor of a helper.</summary>
    public static IEnumerable<string> StateDeclarations(HelperDeclaration helper) =>
        helper.Syntax.DescendantNodes().Select(n => n switch
        {
            ClassVarSectionSyntax => $"{helper.Unit}: {helper.Name} declares a variable section ({Normalize(n)})",
            MethodDeclarationSyntax { Heading.ClassKeyword: not null } m
                when m.Heading.Keyword.Kind is SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword
                => $"{helper.Unit}: {helper.Name} declares a class {m.Heading.Keyword.Text} ({Normalize(n)})",
            _ => null,
        }).OfType<string>();
}
