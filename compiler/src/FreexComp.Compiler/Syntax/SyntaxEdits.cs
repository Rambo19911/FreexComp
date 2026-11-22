using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>Formatting of inserted text (api.md §4.2).</summary>
public sealed record EditOptions(string LineEnding = "\r\n", string IndentUnit = "  ", int RightMargin = 0);

/// <summary>
/// Text-edit producing helpers for designer code sync and refactorings (api.md §4.2). Pure: they inspect an immutable
/// tree and return <see cref="TextChange"/>s against <c>tree.Text</c>; the caller applies them and re-parses. A target
/// that cannot be found gives an empty change / empty array. Identifier comparison is ordinal case-insensitive.
/// </summary>
public static class SyntaxEdits
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private const string DefaultIndentUnit = "  ";

    // ------------------------------------------------------------------ locating (C-152)

    /// <summary>
    /// The first type declaration named <paramref name="className"/> whose type is a <c>class</c> with a body (not a
    /// forward declaration, not <c>class of</c>, not a record or interface): in the interface section of a unit, or also in
    /// its implementation section when <paramref name="interfaceSectionOnly"/> is false; in a program, its block.
    /// </summary>
    public static TypeDeclarationSyntax? FindClass(CompilationUnitSyntax root, string className, bool interfaceSectionOnly = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(className);
        IEnumerable<SyntaxNode> declarations = root switch
        {
            UnitSyntax unit when interfaceSectionOnly => unit.InterfaceSection.Declarations,
            UnitSyntax unit => unit.InterfaceSection.Declarations.Concat(unit.ImplementationSection.Declarations),
            ProgramSyntax program => program.Block.Declarations,
            _ => [],
        };
        return declarations.OfType<TypeSectionSyntax>()
            .SelectMany(s => s.Declarations)
            .FirstOrDefault(t => t.Type is ClassTypeSyntax { EndKeyword: not null } && Names.Equals(t.Identifier.Text, className));
    }

    /// <summary>The members before the first visibility specifier (the designer's managed section, DSG-CS-013).</summary>
    public static ImmutableArray<MemberDeclarationSyntax> GetDefaultSectionMembers(ClassTypeSyntax cls)
    {
        ArgumentNullException.ThrowIfNull(cls);
        return [.. DefaultSection(cls.Members).OfType<MemberDeclarationSyntax>()];
    }

    /// <summary>
    /// The field (also one name of a multi-name declaration or of a <c>class var</c> section), method or property named
    /// <paramref name="name"/> in any section of the class; null when there is none.
    /// </summary>
    public static MemberDeclarationSyntax? FindMember(ClassTypeSyntax cls, string name)
    {
        ArgumentNullException.ThrowIfNull(cls);
        ArgumentNullException.ThrowIfNull(name);
        return Members(cls.Members).FirstOrDefault(m => MemberNames(m).Any(n => Names.Equals(n.Text, name)));
    }

    /// <summary>The routine in the implementation section whose heading is <c>className.methodName</c>; null when there is none.</summary>
    public static RoutineDeclarationSyntax? FindMethodImplementation(UnitSyntax unit, string className, string methodName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(methodName);
        return unit.ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>().FirstOrDefault(r =>
            r.Heading.Name is QualifiedNameSyntax q && IsQualifiedWith(q, className) && Names.Equals(q.Right.Identifier.Text, methodName));
    }

    /// <summary>
    /// DSG-CS-070: a body <c>begin … end</c> with no statements and no comments or directives (only whitespace trivia) and no
    /// local declarations between heading and <c>begin</c>. A routine without body is not empty.
    /// </summary>
    public static bool IsEmptyMethodBody(RoutineDeclarationSyntax implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        if (implementation.Block is not { } block || block.Declarations.Count > 0 || block.Body.Statements.Count > 0)
        {
            return false;
        }

        return block.Body.BeginKeyword.TrailingTrivia.Concat(block.Body.EndKeyword.LeadingTrivia)
            .All(t => t.Kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia);
    }

    /// <summary>
    /// The first active directive named <paramref name="directiveName"/> (case-insensitive) inside <paramref name="scope"/>:
    /// from its first token (a root: from the start of the text) up to the next token after it, so the trivia in front of
    /// the token that follows a section (e.g. <c>end.</c> after an implementation section) belongs to the section.
    /// </summary>
    public static DirectiveTriviaInfo? FindDirective(SyntaxNode scope, string directiveName)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(directiveName);
        var start = scope.Parent is null ? scope.FullSpan.Start : scope.Span.Start;
        var next = scope.LastToken().GetNextToken();
        var end = scope.Parent is null || next.Kind == SyntaxKind.None ? scope.FullSpan.End : next.Span.Start;
        return scope.SyntaxTree.Directives.FirstOrDefault(d =>
            Names.Equals(d.Name, directiveName) && d.Span.Start >= start && d.Span.End <= end);
    }

    // ------------------------------------------------------------------ fields (C-152)

    /// <summary>
    /// CMP-250: inserts <c>&lt;line ending&gt;&lt;indent&gt;name: typeName;</c> after the last field of the default section,
    /// else after the line that ends the class heading. Indent: of the previous field line, else of the first member line
    /// of the class, else class-heading indent + <see cref="EditOptions.IndentUnit"/>.
    /// </summary>
    public static TextChange AddField(TypeDeclarationSyntax type, string name, string typeName, EditOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(typeName);
        return InsertIntoDefaultSection(type, name + ": " + typeName + ";", options ?? new EditOptions(), afterMethods: false);
    }

    /// <summary>
    /// CMP-251: a single-name field declaration is removed as its whole line(s) (with a trailing same-line comment); in a
    /// multi-name declaration only the name and one adjacent comma (the following one, the preceding one for the last
    /// name). Every section is searched; not found → empty.
    /// </summary>
    public static ImmutableArray<TextChange> RemoveField(TypeDeclarationSyntax type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        if (FindField(type, name) is not { } found)
        {
            return [];
        }

        var (field, index) = found;
        var identifiers = field.Identifiers;
        if (identifiers.Count == 1)
        {
            var change = RemoveMember(field);
            return change.IsEmpty ? [] : [change];
        }

        var text = type.SyntaxTree.Text.Text;
        var separators = identifiers.Separators.ToList();
        TextSpan span;
        if (index < identifiers.Count - 1)
        {
            var comma = separators[index];
            var next = identifiers[index + 1];
            var end = IsBlank(text, comma.Span.End, next.Span.Start) ? next.Span.Start : comma.Span.End;
            span = TextSpan.FromBounds(identifiers[index].Span.Start, end);
        }
        else
        {
            var comma = separators[index - 1];
            var previous = identifiers[index - 1];
            var start = IsBlank(text, previous.Span.End, comma.Span.Start) ? previous.Span.End : comma.Span.Start;
            span = TextSpan.FromBounds(start, identifiers[index].Span.End);
        }

        return [new TextChange(span, "")];
    }

    /// <summary>CMP-252: replaces only the identifier token of the field declaration; references are not touched.</summary>
    public static TextChange RenameField(TypeDeclarationSyntax type, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        return FindField(type, oldName) is var (field, index)
            ? new TextChange(field.Identifiers[index].Identifier.Span, newName)
            : default;
    }

    // ------------------------------------------------------------------ methods (C-152)

    /// <summary>
    /// CMP-253: inserts <c>&lt;line ending&gt;&lt;indent&gt;headingText</c> after the last method of the default section,
    /// else after its last field, else at the start of the section as <see cref="AddField"/>.
    /// </summary>
    public static TextChange AddMethodDeclaration(TypeDeclarationSyntax type, string headingText, EditOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(headingText);
        return InsertIntoDefaultSection(type, headingText, options ?? new EditOptions(), afterMethods: true);
    }

    /// <summary>
    /// CMP-256: removes the method's declaration line(s) (as <see cref="RemoveMember"/>) and its implementation from the
    /// start of the heading line to the end of the <c>end;</c> line, plus one adjacent blank line (the following one if
    /// present, else the preceding one). 0, 1 or 2 changes in ascending order.
    /// </summary>
    public static ImmutableArray<TextChange> RemoveMethod(UnitSyntax unit, string className, string methodName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(methodName);
        var changes = ImmutableArray.CreateBuilder<TextChange>();
        if (FindClassDeclaration(unit, className) is { } type && MembersOf(type.Type).Members is { } members)
        {
            var declaration = AllMembers(members).OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Heading.Name is SimpleNameSyntax name && Names.Equals(name.Identifier.Text, methodName));
            if (declaration is not null && RemoveMember(declaration) is { IsEmpty: false } change)
            {
                changes.Add(change);
            }
        }

        if (FindMethodImplementation(unit, className, methodName) is { } implementation)
        {
            var text = unit.SyntaxTree.Text;
            var firstLine = text.Lines.GetLinePosition(implementation.Span.Start).Line;
            var lastLine = text.Lines.GetLinePosition(implementation.Span.End).Line;
            var start = text.Lines.GetLineSpan(firstLine).Start;
            var end = NextLineStart(text, lastLine);
            if (IsBlankLine(text, lastLine + 1))
            {
                end = NextLineStart(text, lastLine + 1);
            }
            else if (IsBlankLine(text, firstLine - 1))
            {
                start = text.Lines.GetLineSpan(firstLine - 1).Start;
            }

            changes.Add(new TextChange(TextSpan.FromBounds(start, end), ""));
        }

        return [.. changes.OrderBy(c => c.Span.Start)];
    }

    // ------------------------------------------------------------------ uses clause and class heading (C-152)

    /// <summary>
    /// True when the <c>uses</c> clause of the section (of a program: its only clause) names <paramref name="unitName"/>
    /// (full dotted name, case-insensitive).
    /// </summary>
    public static bool UsesContains(CompilationUnitSyntax root, string unitName, bool interfaceSection)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(unitName);
        return UsesClauseOf(root, interfaceSection) is { } clause && clause.Units.Any(u => Names.Equals(DottedName(u.Name), unitName));
    }

    /// <summary>
    /// CMP-258: renames the class declaration identifier, the leading qualifier <c>TOld</c> of every implementation-section
    /// routine heading <c>TOld.…</c>, the type identifier of interface-section variable declarations typed <c>TOld</c>, and
    /// every forward declaration <c>TOld = class;</c> of the class in either section (C-267). Nothing else. Class not found → empty.
    /// </summary>
    public static ImmutableArray<TextChange> RenameClass(UnitSyntax unit, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        if (FindClass(unit, oldName, interfaceSectionOnly: false) is not { } type)
        {
            return [];
        }

        var changes = ImmutableArray.CreateBuilder<TextChange>();
        changes.Add(new TextChange(type.Identifier.Span, newName));

        // C-267: every forward declaration `TOld = class;` (no heritage, no body) in either section
        foreach (var forward in unit.InterfaceSection.Declarations.Concat(unit.ImplementationSection.Declarations)
                     .OfType<TypeSectionSyntax>().SelectMany(s => s.Declarations))
        {
            if (forward.Type is ClassTypeSyntax { EndKeyword: null, Heritage: null, Modifier: null }
                && Names.Equals(forward.Identifier.Text, oldName))
            {
                changes.Add(new TextChange(forward.Identifier.Span, newName));
            }
        }

        foreach (var routine in unit.ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>())
        {
            if (routine.Heading.Name is QualifiedNameSyntax qualified && Leftmost(qualified) is { } qualifier
                && Names.Equals(qualifier.Identifier.Text, oldName))
            {
                changes.Add(new TextChange(qualifier.Identifier.Span, newName));
            }
        }

        foreach (var variable in unit.InterfaceSection.Declarations.OfType<VarSectionSyntax>().SelectMany(s => s.Declarations))
        {
            if (variable.Type is NamedTypeSyntax { Name: IdentifierNameSyntax typeName } && Names.Equals(typeName.Identifier.Text, oldName))
            {
                changes.Add(new TextChange(typeName.Identifier.Span, newName));
            }
        }

        return [.. changes.OrderBy(c => c.Span.Start)];
    }

    // ------------------------------------------------------------------ C-152 helpers

    /// <summary>The CMP-250 / CMP-253 insertion into the default section.</summary>
    private static TextChange InsertIntoDefaultSection(TypeDeclarationSyntax type, string memberText, EditOptions options, bool afterMethods)
    {
        var (members, end) = MembersOf(type.Type);
        if (members is null || end is not { IsMissing: false } || HeadingEnd(type.Type) is not { } headingEnd)
        {
            return default;
        }

        var text = type.SyntaxTree.Text;
        var section = DefaultSection(members.Value).ToList();
        var previous = (afterMethods ? section.LastOrDefault(m => m is MethodDeclarationSyntax) : null)
                       ?? section.LastOrDefault(m => m is FieldDeclarationSyntax);
        SyntaxToken anchor;
        string indent;
        if (previous is not null)
        {
            anchor = previous.LastToken();
            indent = IndentOf(text, previous.Span.Start);
        }
        else
        {
            anchor = headingEnd;
            var first = Members(members.Value).FirstOrDefault();
            indent = first is not null ? IndentOf(text, first.Span.Start) : IndentOf(text, headingEnd.Span.Start) + options.IndentUnit;
        }

        var lineText = options.LineEnding + indent + memberText;
        return InsertAfterLineOf(text, anchor, lineText, lineText);
    }

    /// <summary>The last token of a class, record or helper heading (before the first member).</summary>
    private static SyntaxToken? HeadingEnd(TypeSyntax type) => type switch
    {
        ClassTypeSyntax c => c.Heritage?.CloseParenToken ?? c.Modifier ?? c.ClassKeyword,
        RecordTypeSyntax r => r.RecordKeyword,
        HelperTypeSyntax h => h.ExtendedType.LastToken(),
        _ => null,
    };

    private static IEnumerable<SyntaxNode> DefaultSection(SyntaxList<SyntaxNode> members) =>
        members.TakeWhile(m => m is not VisibilitySectionSyntax);

    /// <summary>Every member declaration of every section, the fields of a <c>class var</c> section one by one.</summary>
    private static IEnumerable<MemberDeclarationSyntax> Members(SyntaxList<SyntaxNode> members) =>
        AllMembers(members).OfType<MemberDeclarationSyntax>()
            .SelectMany(m => m is ClassVarSectionSyntax vars ? vars.Fields : (IEnumerable<MemberDeclarationSyntax>)[m]);

    private static IEnumerable<SyntaxToken> MemberNames(MemberDeclarationSyntax member) => member switch
    {
        FieldDeclarationSyntax field => field.Identifiers.Select(i => i.Identifier),
        MethodDeclarationSyntax { Heading.Name: SimpleNameSyntax name } => [name.Identifier],
        PropertyDeclarationSyntax property => [property.Identifier],
        _ => [],
    };

    private static (FieldDeclarationSyntax Field, int Index)? FindField(TypeDeclarationSyntax type, string name)
    {
        if (MembersOf(type.Type).Members is not { } members)
        {
            return null;
        }

        foreach (var field in Members(members).OfType<FieldDeclarationSyntax>())
        {
            for (var i = 0; i < field.Identifiers.Count; i++)
            {
                if (Names.Equals(field.Identifiers[i].Identifier.Text, name))
                {
                    return (field, i);
                }
            }
        }

        return null;
    }

    private static UsesClauseSyntax? UsesClauseOf(CompilationUnitSyntax root, bool interfaceSection) => root switch
    {
        UnitSyntax unit => interfaceSection ? unit.InterfaceSection.UsesClause : unit.ImplementationSection.UsesClause,
        ProgramSyntax program => program.UsesClause,
        _ => null,
    };

    private static SimpleNameSyntax? Leftmost(NameSyntax name) => name switch
    {
        QualifiedNameSyntax q => Leftmost(q.Left),
        SimpleNameSyntax s => s,
        _ => null,
    };

    /// <summary>A line that exists, has text (or a line break) and holds blanks only.</summary>
    private static bool IsBlankLine(SourceText text, int line)
    {
        if (line < 0 || line >= text.Lines.LineCount)
        {
            return false;
        }

        var span = text.Lines.GetLineSpan(line);
        return span.Start < text.Text.Length && IsBlank(text.Text, span.Start, span.End);
    }

    // ------------------------------------------------------------------ members

    /// <summary>
    /// Inserts <paramref name="memberText"/> (a complete member incl. its <c>;</c>) into the explicit visibility section
    /// <paramref name="section"/> of a class, record or helper: on a new line after the section's last member (indent of
    /// that member's line), or after the visibility line (its indent + one indent unit) when the section is empty. When
    /// the type has no such section, the section is created right before the type's <c>end</c>; the visibility word gets
    /// the indent of the type declaration line. Line ending = the text's first line break (CR LF when it has none).
    /// </summary>
    public static TextChange InsertMember(TypeDeclarationSyntax type, string memberText, Visibility section)
    {
        ArgumentNullException.ThrowIfNull(memberText);
        return InsertMembers(type, [memberText], section);
    }

    /// <summary><see cref="InsertMember"/> for several members in one change (one per line, in the given order).</summary>
    internal static TextChange InsertMembers(TypeDeclarationSyntax type, IReadOnlyList<string> memberTexts, Visibility section)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(memberTexts);
        var (members, end) = MembersOf(type.Type);
        if (memberTexts.Count == 0 || members is null || end is not { IsMissing: false } endKeyword)
        {
            return default;
        }

        var text = type.SyntaxTree.Text;
        var lineEnding = DetectLineEnding(text.Text);
        var keyword = VisibilityText(section);
        var target = members.Value.OfType<VisibilitySectionSyntax>().FirstOrDefault(s => Names.Equals(SectionText(s), keyword));
        if (target is not null)
        {
            SyntaxNode? last = target.Members.Count > 0 ? target.Members[^1] : null;
            var anchor = last?.LastToken() ?? target.VisibilityKeyword;
            var indent = last is not null ? IndentOf(text, last.Span.Start) : IndentOf(text, target.Span.Start) + DefaultIndentUnit;
            return InsertAfterLineOf(text, anchor, string.Concat(memberTexts.Select(m => lineEnding + indent + m)),
                string.Concat(memberTexts.Select(m => " " + m)));
        }

        var headIndent = IndentOf(text, type.Span.Start);
        var sectionText = headIndent + keyword + lineEnding
                          + string.Concat(memberTexts.Select(m => headIndent + DefaultIndentUnit + m + lineEnding));
        var lineStart = LineStart(text, endKeyword.Span.Start);
        if (IsBlank(text.Text, lineStart, endKeyword.Span.Start))
        {
            return new TextChange(new TextSpan(lineStart, 0), sectionText);
        }

        // 'end' shares its line with the heading: put it on a line of its own
        var previous = endKeyword.GetPreviousToken();
        var gapStart = previous.Kind != SyntaxKind.None && IsBlank(text.Text, previous.Span.End, endKeyword.Span.Start)
            ? previous.Span.End
            : endKeyword.Span.Start;
        return new TextChange(TextSpan.FromBounds(gapStart, endKeyword.Span.Start), lineEnding + sectionText + headIndent);
    }

    /// <summary>
    /// Removes a member or declaration: the whole line(s) from the start of the line of its first token to the end of
    /// the line break after its last token (a trailing same-line comment included) when nothing else is on those lines;
    /// otherwise only the node and the blanks that separate it from its neighbour on the line.
    /// </summary>
    public static TextChange RemoveMember(SyntaxNode member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var text = member.SyntaxTree.Text;
        var span = member.Span;
        var last = member.LastToken();
        if (span.Length == 0 || last.Kind == SyntaxKind.None)
        {
            return default;
        }

        var lineStart = LineStart(text, span.Start);
        var aloneAtStart = IsBlank(text.Text, lineStart, span.Start);
        var endsLine = last.TrailingTrivia.Any(t => t.Kind == SyntaxKind.EndOfLineTrivia)
                       || last.GetNextToken().Kind is SyntaxKind.EndOfFileToken or SyntaxKind.None;
        if (aloneAtStart && endsLine)
        {
            return new TextChange(TextSpan.FromBounds(lineStart, last.FullSpan.End), "");
        }

        if (endsLine)
        {
            // others before it on the line: remove it with the blanks in front, keep the line break
            var previous = member.FirstToken().GetPreviousToken();
            var start = previous.Kind != SyntaxKind.None && IsBlank(text.Text, previous.Span.End, span.Start) ? previous.Span.End : span.Start;
            return new TextChange(TextSpan.FromBounds(start, span.End), "");
        }

        // others after it on the line: remove it with the blanks behind
        var next = last.GetNextToken();
        var end = next.Kind != SyntaxKind.None && IsBlank(text.Text, span.End, next.Span.Start) ? next.Span.Start : span.End;
        return new TextChange(TextSpan.FromBounds(span.Start, end), "");
    }

    // ------------------------------------------------------------------ methods

    /// <summary>
    /// CMP-254: inserts <paramref name="methodText"/> after the last routine implementation qualified with
    /// <paramref name="className"/>; else after the <c>{$FORM}</c> directive line; else after the implementation
    /// <c>uses</c> clause; else after the <c>implementation</c> line. The blank lines around the insertion become
    /// exactly one before and one after.
    /// </summary>
    /// <remarks>
    /// C-245: in a program the anchor is the last routine implementation qualified with <paramref name="className"/>, else the
    /// program block's last declaration (so the bodies see every global), else the heading or <c>uses</c> clause, before the main
    /// <c>begin</c> in every case.
    /// </remarks>
    public static TextChange InsertMethodImplementation(CompilationUnitSyntax root, string className, string methodText)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(methodText);
        var text = root.SyntaxTree.Text;
        int anchor;
        if (root is ProgramSyntax program)
        {
            var declarations = program.Block.Declarations;
            if (declarations.OfType<RoutineDeclarationSyntax>().LastOrDefault(r => IsQualifiedWith(r.Heading.Name, className)) is { } own)
            {
                anchor = own.Span.End;
            }
            else if (declarations.Count > 0)
            {
                anchor = declarations[declarations.Count - 1].Span.End;
            }
            else if (program.UsesClause is { } uses)
            {
                anchor = uses.Span.End;
            }
            else if (program.SemicolonToken is { IsMissing: false } semicolon)
            {
                anchor = semicolon.Span.End;
            }
            else
            {
                return default;
            }

            return InsertAfterLine(text, anchor, methodText);
        }

        if (root is not UnitSyntax unit)
        {
            return default;
        }

        var implementation = unit.ImplementationSection;
        if (implementation.ImplementationKeyword.IsMissing)
        {
            return default;
        }

        var lastMethod = implementation.Declarations.OfType<RoutineDeclarationSyntax>()
            .LastOrDefault(r => IsQualifiedWith(r.Heading.Name, className));
        if (lastMethod is not null)
        {
            anchor = lastMethod.Span.End;
        }
        else if (FindFormDirective(unit) is { } form)
        {
            anchor = form.Span.End;
        }
        else if (implementation.UsesClause is { } uses)
        {
            anchor = uses.Span.End;
        }
        else
        {
            anchor = implementation.ImplementationKeyword.Span.End;
        }

        return InsertAfterLine(text, anchor, methodText);
    }

    /// <summary>The CMP-254 insertion at the start of the line after <paramref name="anchor"/>'s, blank lines normalised.</summary>
    private static TextChange InsertAfterLine(SourceText text, int anchor, string methodText)
    {
        var lineEnding = DetectLineEnding(text.Text);
        var line = text.Lines.GetLinePosition(anchor).Line;
        var insertAt = NextLineStart(text, line);
        var prefix = insertAt == text.Text.Length && !EndsWithLineBreak(text.Text) ? lineEnding : "";

        // skip the blank lines that follow; the new text brings exactly one before and one after
        var skipTo = insertAt;
        for (var l = line + 1; l < text.Lines.LineCount; l++)
        {
            var lineSpan = text.Lines.GetLineSpan(l);
            if (!IsBlank(text.Text, lineSpan.Start, lineSpan.End))
            {
                break;
            }

            skipTo = NextLineStart(text, l);
        }

        var body = methodText.Trim('\r', '\n');
        return new TextChange(TextSpan.FromBounds(insertAt, skipTo), prefix + lineEnding + body + lineEnding + lineEnding);
    }

    /// <summary>
    /// CMP-255: renames the method-name identifier in the class declaration and in the implementation heading
    /// (<c>TClass.Old</c> → <c>TClass.New</c>); 0, 1 or 2 changes. Calls are not touched.
    /// </summary>
    public static ImmutableArray<TextChange> RenameMethod(UnitSyntax unit, string className, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        var changes = ImmutableArray.CreateBuilder<TextChange>();
        var type = FindClassDeclaration(unit, className);
        if (type is not null && MembersOf(type.Type).Members is { } members)
        {
            var declaration = AllMembers(members).OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Heading.Name is SimpleNameSyntax name && Names.Equals(name.Identifier.Text, oldName));
            if (declaration is not null)
            {
                changes.Add(new TextChange(((SimpleNameSyntax)declaration.Heading.Name).Identifier.Span, newName));
            }
        }

        var implementation = unit.ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>()
            .Select(r => r.Heading.Name)
            .OfType<QualifiedNameSyntax>()
            .FirstOrDefault(q => IsQualifiedWith(q, className) && Names.Equals(q.Right.Identifier.Text, oldName));
        if (implementation is not null)
        {
            changes.Add(new TextChange(implementation.Right.Identifier.Span, newName));
        }

        return [.. changes.OrderBy(c => c.Span.Start)];
    }

    // ------------------------------------------------------------------ uses clause

    /// <summary>
    /// CMP-257: appends <c>, Unit</c> after the last unit of the section's clause (wrapped to the column of the first
    /// unit when the line would exceed <see cref="EditOptions.RightMargin"/>); without a clause inserts
    /// <c>uses / IndentUnit Unit;</c> after the line of the section keyword (of a program: its heading, else at the start).
    /// Unit already present (full dotted name, case-insensitive) → empty change. A program has one clause:
    /// <paramref name="interfaceSection"/> is ignored there.
    /// </summary>
    public static TextChange AddUsesEntry(CompilationUnitSyntax root, string unitName, bool interfaceSection, EditOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(unitName);
        options ??= new EditOptions();
        var text = root.SyntaxTree.Text;
        UsesClauseSyntax? clause;
        SyntaxToken? anchor;
        switch (root)
        {
            case UnitSyntax unit:
                clause = interfaceSection ? unit.InterfaceSection.UsesClause : unit.ImplementationSection.UsesClause;
                anchor = interfaceSection ? unit.InterfaceSection.InterfaceKeyword : unit.ImplementationSection.ImplementationKeyword;
                break;
            case ProgramSyntax program:
                clause = program.UsesClause;
                anchor = program.SemicolonToken;
                break;
            default:
                return default;
        }

        if (clause is not null && clause.Units.Count > 0)
        {
            if (UsesContains(root, unitName, interfaceSection))
            {
                return default;
            }

            var lastUnit = clause.Units[^1];
            var line = text.Lines.GetLineSpan(text.Lines.GetLinePosition(lastUnit.Span.End).Line);
            var insertion = ", " + unitName;
            if (options.RightMargin > 0 && line.Length + insertion.Length > options.RightMargin)
            {
                var column = text.Lines.GetLinePosition(clause.Units[0].Span.Start).Column;
                insertion = "," + options.LineEnding + new string(' ', column) + unitName;
            }

            return new TextChange(new TextSpan(lastUnit.Span.End, 0), insertion);
        }

        if (anchor is not { IsMissing: false } keyword)
        {
            var start = root.FirstToken().Span.Start;
            return new TextChange(new TextSpan(start, 0),
                "uses" + options.LineEnding + options.IndentUnit + unitName + ";" + options.LineEnding + options.LineEnding);
        }

        var keywordLine = text.Lines.GetLineSpan(text.Lines.GetLinePosition(keyword.Span.End).Line);
        return new TextChange(new TextSpan(keywordLine.End, 0),
            options.LineEnding + options.LineEnding + "uses" + options.LineEnding + options.IndentUnit + unitName + ";");
    }

    // ------------------------------------------------------------------ helpers

    private static (SyntaxList<SyntaxNode>? Members, SyntaxToken? End) MembersOf(TypeSyntax type) => type switch
    {
        ClassTypeSyntax c when c.EndKeyword is not null => (c.Members, c.EndKeyword),
        RecordTypeSyntax r => (r.Members, r.EndKeyword),
        HelperTypeSyntax h => (h.Members, h.EndKeyword),
        _ => (null, null),
    };

    private static IEnumerable<SyntaxNode> AllMembers(SyntaxList<SyntaxNode> members) =>
        members.SelectMany(m => m is VisibilitySectionSyntax section ? section.Members : (IEnumerable<SyntaxNode>)[m]);

    private static TypeDeclarationSyntax? FindClassDeclaration(UnitSyntax unit, string className) =>
        unit.InterfaceSection.Declarations.Concat(unit.ImplementationSection.Declarations)
            .OfType<TypeSectionSyntax>()
            .SelectMany(s => s.Declarations)
            .FirstOrDefault(t => Names.Equals(t.Identifier.Text, className) && MembersOf(t.Type).Members is not null);

    private static bool IsQualifiedWith(NameSyntax name, string className) =>
        name is QualifiedNameSyntax qualified && Names.Equals(DottedName(qualified.Left), className);

    internal static string DottedName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax q => DottedName(q.Left) + "." + q.Right.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        _ => name.ToString(),
    };

    private static string VisibilityText(Visibility visibility) => visibility switch
    {
        Visibility.StrictPrivate => "strict private",
        Visibility.Private => "private",
        Visibility.StrictProtected => "strict protected",
        Visibility.Protected => "protected",
        Visibility.Public => "public",
        _ => "published",
    };

    private static string SectionText(VisibilitySectionSyntax section) =>
        section.StrictKeyword is { } strict ? strict.Text + " " + section.VisibilityKeyword.Text : section.VisibilityKeyword.Text;

    private static DirectiveTriviaInfo? FindFormDirective(UnitSyntax unit) => FindDirective(unit.ImplementationSection, "FORM");

    /// <summary>
    /// After the line of <paramref name="anchor"/> (before its line break) when the next token starts on a later line;
    /// else right after the anchor with <paramref name="sameLineText"/>.
    /// </summary>
    private static TextChange InsertAfterLineOf(SourceText text, SyntaxToken anchor, string lineText, string sameLineText)
    {
        var lineSpan = text.Lines.GetLineSpan(text.Lines.GetLinePosition(anchor.Span.End).Line);
        var next = anchor.GetNextToken();
        if (next.Kind == SyntaxKind.None || next.Span.Start >= lineSpan.End)
        {
            return new TextChange(new TextSpan(lineSpan.End, 0), lineText);
        }

        return new TextChange(new TextSpan(anchor.Span.End, 0), sameLineText);
    }

    internal static string DetectLineEnding(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        if (index < 0)
        {
            return "\r\n";
        }

        return text[index] == '\n' ? "\n" : index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }

    private static bool EndsWithLineBreak(string text) => text.Length > 0 && text[^1] is '\r' or '\n';

    private static int LineStart(SourceText text, int position) =>
        text.Lines.GetLineSpan(text.Lines.GetLinePosition(position).Line).Start;

    private static int NextLineStart(SourceText text, int line) =>
        line + 1 < text.Lines.LineCount ? text.Lines.GetLineSpan(line + 1).Start : text.Text.Length;

    internal static string IndentOf(SourceText text, int position)
    {
        var start = LineStart(text, position);
        var end = start;
        while (end < text.Text.Length && text.Text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.Text[start..end];
    }

    internal static bool IsBlank(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (text[i] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }
}
