using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// Walks the syntax tree once, top-down, and fills a <see cref="LayoutPlan"/> (CMP-1440, CMP-1441, CMP-1443). Every node is
/// visited with the level <c>L</c> of the line its construct starts on; a token no rule names keeps its line break as written
/// and is indented as a continuation line (<c>L + 1</c>).
/// </summary>
internal sealed class LayoutPlanner
{
    private readonly LayoutPlan _plan;
    private readonly bool _sameLine;
    private readonly UsesWrap _uses;
    private readonly Dictionary<SyntaxNode, int> _levelOf = new(ReferenceEqualityComparer.Instance);
    private readonly CancellationToken _ct;
    private int _visited;

    private LayoutPlanner(LayoutPlan plan, FormatProfile profile, CancellationToken ct)
    {
        _plan = plan;
        _sameLine = profile.Begin == BeginStyle.SameLine;
        _uses = profile.Uses;
        _ct = ct;
    }

    public static void Plan(LayoutPlan plan, SyntaxNode root, FormatProfile profile, CancellationToken ct)
    {
        var planner = new LayoutPlanner(plan, profile, ct);
        planner.Visit(root, 0);
        plan.Complete();
    }

    // ------------------------------------------------------------------ setters

    private void Line(SyntaxToken? t, int level, BlankRule blank = BlankRule.Keep)
    {
        _plan.Set(t, GapRule.Line, level);
        if (blank != BlankRule.Keep)
        {
            _plan.SetBlank(t, blank);
        }
    }

    private void Join(SyntaxToken? t, int level) => _plan.Set(t, GapRule.Join, level);

    private void Free(SyntaxToken? t, int level) => _plan.Set(t, GapRule.Free, level);

    /// <summary>A closing keyword on its own line: comments before it at the body level; no blank line before it unless kept.</summary>
    private void Closer(SyntaxToken? t, int level, bool noBlank = true)
    {
        Line(t, level, noBlank ? BlankRule.None : BlankRule.Keep);
        _plan.SetCommentLevel(t, level + 1);
    }

    private void At(SyntaxNode? node, int level)
    {
        if (node is not null)
        {
            _levelOf[node] = level;
        }
    }

    /// <summary>A node that starts its own line at <paramref name="level"/>.</summary>
    private void LineNode(SyntaxNode? node, int level, BlankRule blank = BlankRule.Keep)
    {
        if (node is null)
        {
            return;
        }

        Line(node.FirstToken(), level, blank);
        At(node, level);
    }

    // ------------------------------------------------------------------ walk

    private void Visit(SyntaxNode node, int level)
    {
        if ((++_visited & 1023) == 0)
        {
            _ct.ThrowIfCancellationRequested();
        }

        Decide(node, level);
        MarkBreakable(node);
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode() is { } inner)
            {
                Visit(inner, _levelOf.TryGetValue(inner, out var l) ? l : level);
            }
            else
            {
                Default(child.AsToken(), level);
            }
        }
    }

    private void Default(SyntaxToken t, int level)
    {
        if (t.Kind is SyntaxKind.CommaToken or SyntaxKind.SemicolonToken or SyntaxKind.ColonToken)
        {
            Join(t, level + 1);
        }
        else
        {
            Free(t, level + 1);
        }
    }

    private void Decide(SyntaxNode node, int l)
    {
        switch (node)
        {
            case ProgramSyntax p:
                Line(p.ProgramKeyword, 0);
                LineNode(p.UsesClause, 0);
                Block(p.Block, 0, nestedRoutines: false);
                Join(p.DotToken, 0);
                break;

            case LibrarySyntax lib:
                Line(lib.LibraryKeyword, 0);
                LineNode(lib.UsesClause, 0);
                Block(lib.Block, 0, nestedRoutines: false);
                Join(lib.DotToken, 0);
                break;

            case UnitSyntax u:
                Line(u.UnitKeyword, 0);
                Line(u.InterfaceSection.InterfaceKeyword, 0);
                Line(u.ImplementationSection.ImplementationKeyword, 0, BlankRule.One);
                if (u.InitializationSection is { } init)
                {
                    Line(init.Keyword, 0, init.Keyword.Kind == SyntaxKind.InitializationKeyword ? BlankRule.One : BlankRule.Keep);
                }

                Line(u.FinalizationSection?.FinalizationKeyword, 0, BlankRule.One);
                Line(u.EndKeyword, 0);
                Join(u.DotToken, 0);
                break;

            case UsesClauseSyntax u:
                Uses(u, l);
                break;

            case InterfaceSectionSyntax s:
                LineNode(s.UsesClause, l);
                Declarations(s.Declarations, l, nestedRoutines: false);
                break;

            case ImplementationSectionSyntax s:
                LineNode(s.UsesClause, l);
                Declarations(s.Declarations, l, nestedRoutines: false);
                break;

            case InitializationSectionSyntax s:
                Statements(s.Statements, l + 1);
                break;

            case FinalizationSectionSyntax s:
                Statements(s.Statements, l + 1);
                break;

            // --- declarations
            case ConstSectionSyntax s:
                Section(s, s.Declarations, l);
                break;

            case TypeSectionSyntax s:
                Section(s, s.Declarations, l);
                break;

            case VarSectionSyntax s:
                Section(s, s.Declarations, l);
                break;

            case ClassVarSectionSyntax s:
                Section(s, s.Fields, l);
                break;

            case LabelSectionSyntax s:
                if (s.Labels.Count > 0)
                {
                    Line(s.Labels[0].FirstToken(), l + 1);
                }

                break;

            case AttributeListSyntax a when a.Parent is not ParameterSyntax:
                // the declaration continues on the attribute list's level when it starts a line after it
                Free(a.LastToken().GetNextToken(), l);
                break;

            case RoutineDeclarationSyntax r:
                if (r.Block is { } block)
                {
                    Block(block, l, nestedRoutines: true);
                }

                break;

            case AnonymousMethodExpressionSyntax a:
                Block(a.Block, l + 1, nestedRoutines: true);
                break;

            // --- types
            case ClassTypeSyntax c:
                Members(c.Members, l);
                Closer(c.EndKeyword, l);
                break;

            case InterfaceTypeSyntax i:
                LineNode(i.Guid, l + 1);
                Members(i.Members, l);
                Closer(i.EndKeyword, l);
                break;

            case DispInterfaceTypeSyntax i:
                LineNode(i.Guid, l + 1);
                Members(i.Members, l);
                Closer(i.EndKeyword, l);
                break;

            case HelperTypeSyntax h:
                Members(h.Members, l);
                Closer(h.EndKeyword, l);
                break;

            case RecordTypeSyntax r:
                Members(r.Members, l);
                if (r.VariantPart is { } variants)
                {
                    LineNode(variants, l + 1);
                }

                Closer(r.EndKeyword, l);
                break;

            case VariantPartSyntax v when v.Parent is RecordTypeSyntax:
                foreach (var variant in v.Variants)
                {
                    LineNode(variant, l + 1);
                }

                break;

            // --- statements
            case CompoundStatementSyntax c:
                _plan.SetNoBlankAfter(c.BeginKeyword);
                Statements(c.Statements, l + 1);
                Closer(c.EndKeyword, l);
                break;

            case IfStatementSyntax s:
                Controlled(s.Statement, l, s.ThenKeyword);
                if (s.ElseKeyword is { } elseKeyword)
                {
                    if (s.Statement is CompoundStatementSyntax)
                    {
                        if (_sameLine)
                        {
                            Join(elseKeyword, l);
                        }
                        else
                        {
                            Line(elseKeyword, l);
                        }
                    }
                    else
                    {
                        Free(elseKeyword, l);
                    }

                    if (s.ElseStatement is IfStatementSyntax elseIf)
                    {
                        Join(elseIf.IfKeyword, l);
                        At(elseIf, l);
                    }
                    else
                    {
                        Controlled(s.ElseStatement, l, elseKeyword);
                    }
                }

                break;

            case CaseStatementSyntax s:
                foreach (var arm in s.Arms)
                {
                    LineNode(arm, l + 1);
                }

                Line(s.ElseKeyword, l);
                Statements(s.ElseStatements, l + 1);
                Closer(s.EndKeyword, l);
                break;

            case CaseArmSyntax arm:
                foreach (var label in arm.Labels)
                {
                    Free(label.FirstToken(), l);    // a label list continued on the next line stays on the labels' level
                }

                if (arm.Statement is CompoundStatementSyntax armBlock)
                {
                    if (_sameLine)
                    {
                        Join(armBlock.BeginKeyword, l);
                        At(armBlock, l);
                    }
                    else
                    {
                        Line(armBlock.BeginKeyword, l + 1);
                        At(armBlock, l + 1);
                    }
                }
                else
                {
                    ControlledSimple(arm.Statement, l + 1, arm.ColonToken);
                }

                break;

            case RepeatStatementSyntax s:
                Statements(s.Statements, l + 1);
                Closer(s.UntilKeyword, l, noBlank: false);
                break;

            case WhileStatementSyntax s:
                Controlled(s.Statement, l, s.DoKeyword);
                break;

            case ForStatementSyntax s:
                Controlled(s.Statement, l, s.DoKeyword);
                break;

            case ForInStatementSyntax s:
                Controlled(s.Statement, l, s.DoKeyword);
                break;

            case WithStatementSyntax s:
                Controlled(s.Statement, l, s.DoKeyword);
                break;

            case TryFinallyStatementSyntax s:
                Statements(s.Statements, l + 1);
                Closer(s.FinallyKeyword, l, noBlank: false);
                Statements(s.FinallyStatements, l + 1);
                Closer(s.EndKeyword, l);
                break;

            case TryExceptStatementSyntax s:
                Statements(s.Statements, l + 1);
                Closer(s.ExceptKeyword, l, noBlank: false);
                foreach (var handler in s.Handlers)
                {
                    LineNode(handler, l + 1);
                }

                Line(s.ElseKeyword, l);
                Statements(s.ExceptStatements, l + 1);
                Closer(s.EndKeyword, l);
                break;

            case ExceptionHandlerSyntax h:
                Controlled(h.Statement, l, h.DoKeyword);
                break;

            case LabeledStatementSyntax s:
                if (s.Statement.FirstToken() is var first && first.Kind != SyntaxKind.None)
                {
                    Free(first, l);
                    At(s.Statement, l);
                }

                break;
        }
    }

    /// <summary>
    /// CMP-1450: <see cref="UsesWrap.OnePerLine"/> puts each unit on its own line one level in; <see cref="UsesWrap.Fill"/> puts
    /// the first unit on its own line one level in and fills the next ones after it up to the maximum line length. In both the
    /// <c>in 'path'</c> part stays with its unit. <see cref="UsesWrap.AsIs"/> keeps the line breaks (spacing only).
    /// </summary>
    private void Uses(UsesClauseSyntax uses, int l)
    {
        if (_uses == UsesWrap.AsIs)
        {
            return;
        }

        var items = uses.Units.GetWithSeparators();
        for (var i = 0; i < items.Length; i += 2)
        {
            var unit = (UsedUnitSyntax)items[i].AsNode()!;
            if (i == 0 || _uses == UsesWrap.OnePerLine)
            {
                Line(unit.FirstToken(), l + 1);
                if (_plan.IndexOf(unit.FirstToken()) is var index and >= 0)
                {
                    _plan.CommentsToNext[index] = true;
                }
            }
            else
            {
                _plan.SetWrap(unit.FirstToken(), l + 1, i + 1 < items.Length ? items[i + 1].AsToken() : uses.SemicolonToken);
            }

            Join(unit.InKeyword, l + 1);
            Join(unit.Path, l + 1);
        }
    }

    /// <summary>
    /// CMP-1451: the separators a long line may be broken after — of argument and index lists, formal parameter lists (their
    /// <c>;</c>), the name lists of variables, fields and parameters, set / array constructors, array constants, and of
    /// <c>uses</c> clauses laid out <see cref="UsesWrap.AsIs"/>.
    /// </summary>
    private void MarkBreakable(SyntaxNode node)
    {
        var separators = node switch
        {
            ArgumentListSyntax a => a.Arguments.Separators,
            ElementAccessExpressionSyntax e => e.Indexes.Separators,
            ParameterListSyntax p => p.Parameters.Separators,
            PropertyDeclarationSyntax p => p.Parameters.Separators,
            ParameterSyntax p => p.Identifiers.Separators,
            VarDeclarationSyntax v => v.Identifiers.Separators,
            FieldDeclarationSyntax f => f.Identifiers.Separators,
            InlineVarStatementSyntax v => v.Identifiers.Separators,
            SetConstructorSyntax s => s.Elements.Separators,
            TypedConstantAggregateSyntax c => c.Elements.Separators.Where(t => t.Kind == SyntaxKind.CommaToken),
            UsesClauseSyntax u when _uses == UsesWrap.AsIs => u.Units.Separators,
            _ => null,
        };
        if (separators is null)
        {
            return;
        }

        foreach (var separator in separators)
        {
            _plan.SetBreakAfter(separator);
        }
    }

    /// <summary>Declarations of a program, library, routine or anonymous method body, then its <c>begin … end</c>.</summary>
    private void Block(BlockSyntax block, int l, bool nestedRoutines)
    {
        At(block, l);
        Declarations(block.Declarations, l, nestedRoutines);
        Line(block.Body.BeginKeyword, l);
        At(block.Body, l);
    }

    /// <summary>
    /// Each declaration starts a line; routines nested in a routine are one level in; exactly one blank line between two
    /// routine implementations (CMP-1443).
    /// </summary>
    private void Declarations(SyntaxList<SyntaxNode> declarations, int l, bool nestedRoutines)
    {
        var previousWasBody = false;
        foreach (var d in declarations)
        {
            var isBody = d is RoutineDeclarationSyntax { Block: not null };
            var level = nestedRoutines && d is RoutineDeclarationSyntax ? l + 1 : l;
            LineNode(d, level, isBody && previousWasBody ? BlankRule.One : BlankRule.Keep);
            previousWasBody = isBody;
        }
    }

    /// <summary>
    /// The declarations of a <c>const</c> / <c>type</c> / <c>var</c> / <c>class var</c> section, each on its own line one level
    /// in; in a class, record, interface or helper body the first one may stay after the keyword (<c>class var FCount: Integer;</c>).
    /// </summary>
    private void Section<T>(SyntaxNode section, SyntaxList<T> declarations, int l)
        where T : SyntaxNode
    {
        var inType = section is ClassVarSectionSyntax
            || section.Parent is VisibilitySectionSyntax or ClassTypeSyntax or RecordTypeSyntax or HelperTypeSyntax or InterfaceTypeSyntax;
        for (var i = 0; i < declarations.Count; i++)
        {
            if (i == 0 && inType)
            {
                Free(declarations[i].FirstToken(), l + 1);
                At(declarations[i], l + 1);
            }
            else
            {
                LineNode(declarations[i], l + 1);
            }
        }
    }

    /// <summary>Class, record, interface and helper bodies: visibility keywords at the type's level, members one level in.</summary>
    private void Members(SyntaxList<SyntaxNode> members, int l)
    {
        foreach (var m in members)
        {
            if (m is VisibilitySectionSyntax v)
            {
                LineNode(v, l);
                foreach (var inner in v.Members)
                {
                    LineNode(inner, l + 1);
                }
            }
            else
            {
                LineNode(m, l + 1);
            }
        }
    }

    private void Statements(SeparatedSyntaxList<StatementSyntax> statements, int level)
    {
        foreach (var s in statements)
        {
            LineNode(s, level);
        }
    }

    /// <summary>
    /// The statement after <c>then</c> / <c>else</c> / <c>do</c>: a compound statement on the controlling statement's level (its
    /// <c>begin</c> on its own line, or after the keyword with <see cref="BeginStyle.SameLine"/>), any other statement one level in.
    /// </summary>
    private void Controlled(StatementSyntax? statement, int l, SyntaxToken? keyword)
    {
        if (statement is CompoundStatementSyntax c)
        {
            if (_sameLine)
            {
                Join(c.BeginKeyword, l);
            }
            else
            {
                Line(c.BeginKeyword, l);
            }

            At(c, l);
        }
        else
        {
            ControlledSimple(statement, l + 1, keyword);
        }
    }

    /// <summary>
    /// A controlled statement that is not compound: as written, at <paramref name="level"/> when on its own line; <c>try</c>,
    /// <c>repeat</c> and <c>case</c> always start a line. An empty statement leaves the <c>;</c> after <paramref name="keyword"/> as written
    /// (<c>while P do</c> / <c>;</c> on the next line).
    /// </summary>
    private void ControlledSimple(StatementSyntax? statement, int level, SyntaxToken? keyword)
    {
        if (statement is null || statement.FirstToken() is not { Kind: not SyntaxKind.None } first)
        {
            if (keyword is { } k && k.GetNextToken() is { Kind: SyntaxKind.SemicolonToken } semicolon)
            {
                Free(semicolon, level);
            }

            return;
        }

        if (first.Kind is SyntaxKind.TryKeyword or SyntaxKind.RepeatKeyword or SyntaxKind.CaseKeyword)
        {
            Line(first, level);
        }
        else
        {
            Free(first, level);
        }

        At(statement, level);
    }
}
