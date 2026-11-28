using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

public enum CompletionTriggerKind { Invoked, Character }

public readonly record struct CompletionTrigger(CompletionTriggerKind Kind, char Character);

public sealed record CompletionList(TextSpan ReplacementSpan, ImmutableArray<CompletionItem> Items, bool IsIncomplete);

public sealed record CompletionItem(string Label, CompletionItemKind Kind, string InsertText, string SortText, string? Detail, bool IsDeprecated,
                                    string? SymbolId, string Origin = "");

public sealed record CompletionDescription(string Signature, string? Documentation, string Origin = "");

/// <summary>Code completion (language-service.md §4, CMP-720…724, 1421…1426). Returns the whole context set; the IDE filters (CMP-722).</summary>
public static class CompletionService
{
    private static readonly string[] TypeKeywords = ["array", "record", "class", "set of", "string", "procedure", "function", "interface", "packed"];
    private static readonly string[] RoutineDirectives =
        ["virtual", "override", "overload", "abstract", "reintroduce", "dynamic", "final", "static", "inline", "stdcall", "cdecl", "register", "external", "forward", "deprecated", "message"];
    private static readonly string[] PropertySpecifiers = ["read", "write", "stored", "default", "nodefault", "index", "implements"];
    private static readonly string[] DeclarationKeywords = ["const", "type", "var", "procedure", "function", "begin"];
    private static readonly string[] ClassSectionKeywords = ["const", "type", "var", "procedure", "function"];
    private static readonly string[] ClassMemberKeywords = ["private", "protected", "public", "published", "strict", "property", "constructor", "destructor", "class"];
    private static readonly string[] StatementKeywords =
        ["begin", "if", "case", "for", "while", "repeat", "try", "with", "raise", "exit", "inherited", "goto", "var", "const"];
    private static readonly string[] ExpressionKeywords = ["nil", "not", "inherited", "True", "False"];
    private static readonly string[] Snippets =
        ["begin", "if", "ifelse", "for", "forin", "while", "repeat", "case", "tryfinally", "tryexcept", "with", "function", "procedure", "class"];

    /// <summary>Symbols of the items handed out per document snapshot (the opaque <see cref="CompletionItem.SymbolId"/>).</summary>
    private static readonly ConditionalWeakTable<Document, Dictionary<string, Symbol>> Issued = new();

    public static CompletionList GetCompletions(Document doc, int position, CompletionTrigger trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var text = doc.Text.Text;
        position = Math.Clamp(position, 0, text.Length);
        var model = doc.ModelAt(position);                                  // C-244: only the body around the caret is bound
        var tree = model.SyntaxTree;                                        // nodes handed to the model must be its own
        var replacement = WordSpan(text, position);
        var empty = new CompletionList(replacement, [], false);
        if (replacement.Start >= 2 && text[replacement.Start - 1] == '$' && text[replacement.Start - 2] == '{')
        {
            return new CompletionList(replacement, [.. SyntaxFacts.DirectiveNames.Select(n => Keyword(n, CompletionItemKind.Directive))], false);
        }

        if (InCommentStringOrDisabled(tree, position))
        {
            return empty;
        }

        // CMP-720: '(' and ',' trigger signature help, not completion
        if (trigger.Kind == CompletionTriggerKind.Character && trigger.Character is '(' or ',')
        {
            return empty;
        }

        var builder = new ItemBuilder(doc, model, position);
        var previous = PreviousToken(tree, replacement.Start);
        var parent = previous.Parent;
        ct.ThrowIfCancellationRequested();

        if (previous.Kind == SyntaxKind.DotToken && parent is not null)
        {
            Members(builder, parent);
        }
        else if (DigraphDotLeft(text, previous, replacement.Start) is { } left)
        {
            MembersOf(builder, left);
        }
        else if (previous.Kind != SyntaxKind.None && parent?.FirstAncestorOrSelf<UsesClauseSyntax>() is { } uses && !(uses.SemicolonToken is { IsMissing: false } end && end.Span.End <= replacement.Start))
        {
            UnitNames(builder, doc, uses);
        }
        else if (previous.Kind is SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword && InClassBody(parent))
        {
            Overrides(builder, parent!);
        }
        else if (previous.Kind == SyntaxKind.InheritedKeyword)
        {
            Inherited(builder);
        }
        else if (previous.Kind == SyntaxKind.RaiseKeyword || previous.Kind == SyntaxKind.ColonToken && parent is ExceptionHandlerSyntax)
        {
            ExceptionTypes(builder);
        }
        else if (IsTypePosition(previous) || IsInsideEmptyTypeArguments(model, text, previous, position))
        {
            builder.AddVisible(s => s is TypeSymbol or UnitSymbol);
            builder.AddUsedUnits();
            builder.AddKeywords(TypeKeywords);
        }
        else if (parent?.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property && previous.Span.End <= property.SemicolonToken.Span.Start
                 && (property.Type is not null && previous.Span.End >= property.Type.Span.End))
        {
            builder.AddKeywords(PropertySpecifiers);
        }
        else if (previous.Kind == SyntaxKind.SemicolonToken && parent is RoutineDeclarationSyntax or MethodDeclarationSyntax or DirectiveSyntax
                 && (parent is not RoutineDeclarationSyntax routine || routine.Block is null || routine.Block.Span.Start >= replacement.Start))
        {
            builder.AddKeywords(RoutineDirectives);
            DeclarationStart(builder, parent);
        }
        else if (IsDeclarationStart(previous))
        {
            DeclarationStart(builder, parent);
        }
        else if (IsStatementStart(previous))
        {
            builder.Expected = ExpectedType.At(model, previous, replacement.Start);
            var hidden = WithMembers(builder, previous);
            builder.AddVisible(_ => true, hidden);
            builder.AddKeywords(StatementKeywords);
            builder.AddSnippets(Snippets);
        }
        else
        {
            builder.Expected = ExpectedType.At(model, previous, replacement.Start);
            var hidden = WithMembers(builder, previous);
            builder.AddVisible(s => s is not TypeSymbol || s is ClassTypeSymbol or RecordTypeSymbol or EnumTypeSymbol, hidden);
            builder.AddKeywords(ExpressionKeywords);
        }

        return new CompletionList(replacement, builder.Build(), false);
    }

    public static CompletionDescription GetDescription(Document doc, CompletionItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(item);
        if (item.SymbolId is { } id && Issued.TryGetValue(doc, out var symbols))
        {
            lock (symbols)
            {
                if (symbols.TryGetValue(id, out var symbol))
                {
                    return new CompletionDescription(symbol.ToDisplayString(), DocCommentRenderer.Documentation(doc.Compilation, symbol), OriginOf(symbol));
                }
            }
        }

        return new CompletionDescription(item.Detail ?? item.Label, null, item.Origin);
    }

    // ------------------------------------------------------------------------------------------------ contexts

    private static void Members(ItemBuilder builder, SyntaxNode parent)
    {
        SyntaxNode? left = parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            QualifiedNameSyntax qualified => qualified.Left,
            _ => null,
        };
        if (left is not null)
        {
            MembersOf(builder, left);
        }
    }

    /// <summary>
    /// C-401: <c>F(X.)</c> — the caret after a dot that the lexer read together with the following <c>)</c> as the digraph <c>.)</c>
    /// (an alternative <c>]</c>): the expression left of that dot (a name, member access, index, dereference or call ending right
    /// before it), else null.
    /// </summary>
    private static SyntaxNode? DigraphDotLeft(string text, SyntaxToken previous, int position)
    {
        if (position < 1 || text[position - 1] != '.' || position >= text.Length || text[position] != ')'
            || previous.Kind == SyntaxKind.None || previous.Span.End != position - 1 || previous.Parent is not { } node)
        {
            return null;
        }

        if (node is ArgumentListSyntax { Parent: InvocationExpressionSyntax call })
        {
            node = call;
        }

        while (node.Parent is MemberAccessExpressionSyntax access && ReferenceEquals(access.Name, node)
               || node.Parent is QualifiedNameSyntax qualified && ReferenceEquals(qualified.Right, node))
        {
            node = node.Parent;
        }

        return node is ExpressionSyntax or QualifiedNameSyntax ? node : null;
    }

    private static void MembersOf(ItemBuilder builder, SyntaxNode left)
    {
        var model = builder.Model;
        var symbol = model.GetSymbolInfo(left).Symbol;
        switch (symbol)
        {
            case UnitSymbol unit:
                builder.AddSymbols(unit.InterfaceMembers);
                return;
            case EnumTypeSymbol @enum:
                builder.AddSymbols(@enum.Members);
                return;
            case TypeSymbol type:
                // a class name: class methods, constructors, class vars and properties, nested types, constants
                builder.AddSymbols(model.LookupSymbols(builder.Position, container: type).Where(m =>
                    m is MethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.ClassMethod or MethodKind.ClassStatic }
                    or PropertySymbol { IsClassProperty: true } or VariableSymbol or TypeSymbol or ConstantSymbol));
                return;
        }

        if (ValueType(model, left) is { } valueType)
        {
            builder.AddSymbols(model.LookupSymbols(builder.Position, container: valueType));
        }
    }

    private static void UnitNames(ItemBuilder builder, Document doc, UsesClauseSyntax uses)
    {
        var listed = uses.Units.Select(u => u.Name.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = doc.Compilation.Options;
        var folders = new[] { System.IO.Path.GetDirectoryName(options.MainFile) ?? "", System.IO.Path.GetDirectoryName(doc.Path) ?? "", options.RtlDirectory }
            .Concat(options.SearchPaths).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            try
            {
                foreach (var file in doc.Compilation.FileSystem.EnumerateFiles(folder, "*.pas"))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!listed.Contains(name) && !PathUtil.AreEqual(PathUtil.Normalize(file), doc.Path))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (IOException)
            {
                // a search path that does not exist
            }
        }

        foreach (var name in names)
        {
            builder.Add(new CompletionItem(name, CompletionItemKind.Unit, name, "3" + name.ToLowerInvariant(), "unit " + name, false, null));
        }
    }

    private static void Overrides(ItemBuilder builder, SyntaxNode parent)
    {
        var model = builder.Model;
        if (parent.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration || model.GetDeclaredSymbol(declaration) is not ClassTypeSymbol type
            || type.Parent is not { } ancestor)
        {
            return;
        }

        var own = type.Methods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var method in model.LookupInheritedSymbols(builder.Position, ancestor).OfType<MethodSymbol>()
                     .Where(m => m.VmtSlot >= 0 && !own.Contains(m.Name)))
        {
            var heading = method.ToDisplayString();
            heading = heading.Replace(method.ContainingType.Name + ".", "", StringComparison.Ordinal);
            var cut = heading.IndexOf("; ", Math.Max(0, heading.LastIndexOf(')')), StringComparison.Ordinal);    // before "; virtual"
            var insert = (cut < 0 ? heading : heading[..cut])[(heading.IndexOf(' ') + 1)..] + "; override;";
            builder.Add(new CompletionItem(method.Name, CompletionItemKind.Method, insert, "1" + method.Name.ToLowerInvariant(), heading, method.IsDeprecated, builder.Id(method), OriginOf(method)));
        }
    }

    /// <summary>
    /// CMP-1423 (C-401): inside <c>with E1, E2 do</c> the accessible members of each target's type, innermost (last-listed) first, in
    /// group <c>1</c>; a name already taken by a more inner target is not offered again. Returns the names the members took: outer
    /// symbols of those names are hidden (LANG-310, 375), except locals declared inside that <c>with</c> statement.
    /// </summary>
    private static Dictionary<string, WithStatementSyntax> WithMembers(ItemBuilder builder, SyntaxToken previous)
    {
        var taken = new Dictionary<string, WithStatementSyntax>(StringComparer.OrdinalIgnoreCase);
        for (var node = previous.Parent; node is not null; node = node.Parent)
        {
            if (node is not WithStatementSyntax with || with.DoKeyword.IsMissing || with.DoKeyword.Span.End > builder.Position)
            {
                continue;
            }

            for (var i = with.Expressions.Count - 1; i >= 0; i--)
            {
                if (ValueType(builder.Model, with.Expressions[i]) is not { TypeKind: not TypeKind.Error } type)
                {
                    continue;
                }

                foreach (var member in builder.Model.LookupSymbols(builder.Position, container: type))
                {
                    if (!taken.ContainsKey(member.Name) && builder.AddSymbol(member, group: "1"))
                    {
                        taken[member.Name] = with;
                    }
                }
            }
        }

        return taken;
    }

    /// <summary>
    /// The type of the value <paramref name="expression"/> denotes: for a constructor call (<c>TFoo.Create</c>) the class, not the
    /// class reference its receiver has; for a parameterless function name whose call was not bound (<c>F.</c> being typed) its result.
    /// </summary>
    private static TypeSymbol? ValueType(SemanticModel model, SyntaxNode expression)
    {
        var symbol = model.GetSymbolInfo(expression).Symbol;
        return model.GetTypeInfo(expression).Type switch
        {
            ClassRefTypeSymbol reference when symbol is MethodSymbol { MethodKind: MethodKind.Constructor } => reference.Class,
            null when symbol is RoutineSymbol { ReturnType: { } result } => result,
            var type => type,
        };
    }

    private static void DeclarationStart(ItemBuilder builder, SyntaxNode? parent)
    {
        if (InClassBody(parent))
        {
            builder.AddKeywords(ClassSectionKeywords);                     // no 'begin' among the members of a type
            builder.AddKeywords(ClassMemberKeywords);
        }
        else
        {
            builder.AddKeywords(DeclarationKeywords);
        }
    }

    private static void Inherited(ItemBuilder builder)
    {
        var model = builder.Model;
        if (model.GetEnclosingSymbol(builder.Position) is MethodSymbol { ContainingType: ClassTypeSymbol { Parent: { } ancestor } })
        {
            builder.AddSymbols(model.LookupInheritedSymbols(builder.Position, ancestor).OfType<MethodSymbol>());     // through Self (LANG-610)
        }
    }

    private static void ExceptionTypes(ItemBuilder builder)
    {
        var classes = builder.Model.LookupSymbols(builder.Position).OfType<ClassTypeSymbol>().ToList();
        var exception = classes.FirstOrDefault(c => string.Equals(c.Name, "Exception", StringComparison.OrdinalIgnoreCase));
        foreach (var @class in classes)
        {
            builder.AddSymbol(@class, exception is not null && @class.InheritsFrom(exception) ? "!" : null);
        }
    }

    // ------------------------------------------------------------------------------------------------ position tests

    private static bool InCommentStringOrDisabled(SyntaxTree tree, int position)
    {
        var token = tree.FindToken(position);
        foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia).Concat(tree.FindToken(Math.Max(0, position - 1)).TrailingTrivia))
        {
            var inside = trivia.Span.Start < position && (position < trivia.Span.End || trivia.Kind == SyntaxKind.LineCommentTrivia && position == trivia.Span.End);
            if (inside && (SyntaxFacts.IsComment(trivia.Kind) || trivia.Kind == SyntaxKind.DisabledTextTrivia))
            {
                return true;
            }
        }

        var before = tree.FindToken(Math.Max(0, position - 1));
        return before.Kind == SyntaxKind.StringLiteralToken && before.Span.Start < position
               && (position < before.Span.End || !before.Text.EndsWith('\'') || before.Text.Length == 1);
    }

    private static TextSpan WordSpan(string text, int position)
    {
        var start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var end = position;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return TextSpan.FromBounds(start, end);
    }

    /// <summary>The last non-missing token of the tree's own text ending at or before <paramref name="position"/>.</summary>
    private static SyntaxToken PreviousToken(SyntaxTree tree, int position)
    {
        // C-244: from the token at the position backwards instead of a scan from the start of the file
        var token = tree.FindToken(position);
        while (token.Kind != SyntaxKind.None
               && (token.Span.End > position || token.IsMissing || token.Kind == SyntaxKind.EndOfFileToken || token.File is { } file && !ReferenceEquals(file, tree.Text)))
        {
            token = token.GetPreviousToken();
        }

        return token;
    }

    private static bool InClassBody(SyntaxNode? node) =>
        node?.AncestorsAndSelf().FirstOrDefault(n => n is ClassTypeSyntax or RecordTypeSyntax or InterfaceTypeSyntax or DispInterfaceTypeSyntax or RoutineDeclarationSyntax or BlockSyntax)
            is ClassTypeSyntax or RecordTypeSyntax or InterfaceTypeSyntax or DispInterfaceTypeSyntax;

    private static bool IsTypePosition(SyntaxToken previous) => previous.Kind switch
    {
        SyntaxKind.ColonToken => previous.Parent is VarDeclarationSyntax or FieldDeclarationSyntax or ParameterSyntax or ConstDeclarationSyntax
            or RoutineHeadingSyntax or PropertyDeclarationSyntax or InlineVarStatementSyntax,
        SyntaxKind.EqualsToken => previous.Parent is TypeDeclarationSyntax,
        SyntaxKind.OfKeyword or SyntaxKind.CaretToken => previous.Parent is TypeSyntax,
        SyntaxKind.AsKeyword or SyntaxKind.IsKeyword => true,
        SyntaxKind.LessThanToken or SyntaxKind.CommaToken => previous.Parent is TypeArgumentListSyntax,
        _ => false,
    };

    /// <summary>
    /// C-406: the caret between the two characters of <c>&lt;&gt;</c> right after a type name is the empty type argument list of a
    /// generic: the name stands in a type position, or it names a generic type (<c>TBox&lt;|&gt;.Create</c>). The lexer reads
    /// <c>&lt;&gt;</c> as one token (an operator in an expression, skipped text after a type), so the text decides.
    /// </summary>
    private static bool IsInsideEmptyTypeArguments(SemanticModel model, string text, SyntaxToken previous, int position)
    {
        if (position < 1 || position >= text.Length || text[position - 1] != '<' || text[position] != '>'
            || previous.Kind != SyntaxKind.IdentifierToken || !string.IsNullOrWhiteSpace(text[previous.Span.End..(position - 1)]))
        {
            return false;
        }

        var node = previous.Parent;
        while (node is NameSyntax)
        {
            node = node.Parent;
        }

        return node is NamedTypeSyntax || model.LookupSymbols(position, previous.Text).Any(s => s is GenericTypeSymbol);
    }

    private static bool IsDeclarationStart(SyntaxToken previous)
    {
        if (previous.Kind is SyntaxKind.InterfaceKeyword or SyntaxKind.ImplementationKeyword)
        {
            return previous.Parent is InterfaceSectionSyntax or ImplementationSectionSyntax;
        }

        if (previous.Kind != SyntaxKind.SemicolonToken || previous.Parent is null)
        {
            return false;
        }

        // a ';' ending a declaration (not inside a statement)
        return previous.Parent.FirstAncestorOrSelf<StatementSyntax>() is null
               && previous.Parent is ProgramSyntax or LibrarySyntax or ExportsClauseSyntax or UnitSyntax or UsesClauseSyntax or VarDeclarationSyntax or ConstDeclarationSyntax or TypeDeclarationSyntax
                   or FieldDeclarationSyntax or PropertyDeclarationSyntax or MethodDeclarationSyntax or RoutineDeclarationSyntax or DirectiveSyntax;
    }

    private static bool IsStatementStart(SyntaxToken previous) =>
        previous.Kind is SyntaxKind.BeginKeyword or SyntaxKind.ThenKeyword or SyntaxKind.ElseKeyword or SyntaxKind.DoKeyword or SyntaxKind.RepeatKeyword
            or SyntaxKind.TryKeyword or SyntaxKind.FinallyKeyword or SyntaxKind.ExceptKeyword or SyntaxKind.InitializationKeyword
            or SyntaxKind.FinalizationKeyword
        || previous.Kind == SyntaxKind.SemicolonToken && previous.Parent is CompoundStatementSyntax or RepeatStatementSyntax or TryFinallyStatementSyntax
            or TryExceptStatementSyntax or InitializationSectionSyntax or FinalizationSectionSyntax or CaseStatementSyntax
        || previous.Kind == SyntaxKind.ColonToken && previous.Parent is CaseArmSyntax;

    private static bool IsLocal(Symbol symbol) =>
        symbol is ParameterSymbol || symbol is VariableSymbol { IsGlobal: false } && symbol.ContainingSymbol is RoutineSymbol;

    /// <summary>C-255 (UI-ED-113): the declaring unit's name; empty for locals and units. Built-ins without a unit belong to System.</summary>
    private static string OriginOf(Symbol symbol) =>
        IsLocal(symbol) || symbol is UnitSymbol ? "" : symbol.ContainingUnit?.Name ?? "System";

    private static CompletionItem Keyword(string word, CompletionItemKind kind = CompletionItemKind.Keyword) =>
        new(word, kind, word, "5" + word.ToLowerInvariant(), null, false, null);

    // ------------------------------------------------------------------------------------------------ items

    /// <summary>Collects items once per label and kind, with the CMP-723 sort groups.</summary>
    private sealed class ItemBuilder(Document doc, SemanticModel model, int position)
    {
        private readonly List<CompletionItem> _items = [];
        private readonly HashSet<(string, CompletionItemKind)> _seen = [];
        private readonly HashSet<(string, CompletionItemKind)> _ranked = [];
        private readonly Symbol? _enclosing = model.GetEnclosingSymbol(position);

        public SemanticModel Model { get; } = model;

        public int Position { get; } = position;

        /// <summary>CMP-1426: the type the context expects at the caret; null when none is known.</summary>
        public ExpectedType? Expected { get; set; }

        public bool Add(CompletionItem item)
        {
            if (_seen.Add((item.Label.ToLowerInvariant(), item.Kind)))
            {
                _items.Add(item);
                return true;
            }

            return false;
        }

        public void AddKeywords(IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                Add(Keyword(word));
            }
        }

        public void AddSnippets(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                Add(new CompletionItem(name, CompletionItemKind.Snippet, "snippet." + name, "6" + name, null, false, null));
            }
        }

        /// <summary>The units this file's uses clauses name (scopes, so not among the visible symbols).</summary>
        public void AddUsedUnits() => AddSymbols(Model.SyntaxTree.Root.DescendantNodes().OfType<UsedUnitSyntax>().Select(u => Model.GetSymbolInfo(u).Symbol).OfType<UnitSymbol>());

        /// <summary>
        /// The symbols visible at the position, members of the enclosing types only where accessible (LANG-610, CMP-1424). A name in
        /// <paramref name="hidden"/> belongs to a <c>with</c> member: the outer symbol is left out, unless it is a local declared inside
        /// that <c>with</c> statement, which then replaces the member's item (CMP-1423).
        /// </summary>
        public void AddVisible(Func<Symbol, bool> filter, Dictionary<string, WithStatementSyntax>? hidden = null)
        {
            foreach (var symbol in Model.LookupSymbols(Position).Where(filter))
            {
                if (symbol.ContainingSymbol is TypeSymbol && !Model.IsAccessible(Position, symbol))
                {
                    continue;
                }

                if (hidden is not null && hidden.TryGetValue(symbol.Name, out var with))
                {
                    if (!IsLocal(symbol) || !symbol.Locations.Any(l => with.Statement.Span.Contains(l.Span.Start) && PathUtil.AreEqual(l.Path, doc.Path)))
                    {
                        continue;
                    }

                    var name = symbol.Name.ToLowerInvariant();
                    foreach (var member in _items.Where(i => i.Label.ToLowerInvariant() == name && i.SortText.StartsWith('1')).ToList())
                    {
                        _items.Remove(member);                                  // the with member's item gives way to the local
                        _seen.Remove((name, member.Kind));
                    }
                }

                AddSymbol(symbol);
            }
        }

        public void AddSymbols(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                AddSymbol(symbol);
            }
        }

        /// <summary>Adds the symbol's item (in its CMP-723 group, or in <paramref name="group"/>); false when it is not offered.</summary>
        public bool AddSymbol(Symbol symbol, string? prefix = null, string? group = null)
        {
            if (symbol.Name.Length == 0 || symbol.Name[0] is '$' or '.' || symbol.Name.Contains('<'))
            {
                return false;
            }

            if (Expected?.Matches(symbol) == true)
            {
                _ranked.Add((symbol.Name.ToLowerInvariant(), KindOf(symbol)));      // also when an overload of the name came first
            }

            return Add(new CompletionItem(symbol.Name, KindOf(symbol), symbol.Name, (prefix ?? "") + (group ?? Group(symbol)) + symbol.Name.ToLowerInvariant(),
                                          symbol.ToDisplayString(), symbol.IsDeprecated, Id(symbol), OriginOf(symbol)));
        }

        public string Id(Symbol symbol)
        {
            var table = Issued.GetValue(doc, _ => []);
            lock (table)
            {
                var id = symbol.QualifiedName + "#" + symbol.Kind + "#" + table.Count;
                table[id] = symbol;
                return id;
            }
        }

        public ImmutableArray<CompletionItem> Build() => [.. _items.Select(Rank).OrderBy(i => i.SortText, StringComparer.Ordinal)];

        /// <summary>CMP-1426: the prefix <c>!</c> for an item of the expected type, <c>nil</c> and <c>True</c> / <c>False</c> where they fit.</summary>
        private CompletionItem Rank(CompletionItem item)
        {
            if (Expected is not { } expected || item.SortText.StartsWith('!'))
            {
                return item;
            }

            var ranked = item.Kind == CompletionItemKind.Keyword
                ? item.Label switch
                {
                    "nil" => expected.AcceptsNil,
                    "True" or "False" => expected.IsBoolean,
                    _ => false,
                }
                : _ranked.Contains((item.Label.ToLowerInvariant(), item.Kind));
            return ranked ? item with { SortText = "!" + item.SortText } : item;
        }

        /// <summary>CMP-723 group digit.</summary>
        private string Group(Symbol symbol)
        {
            if (IsLocal(symbol))
            {
                return "0";
            }

            var enclosingType = _enclosing is MethodSymbol method ? method.Owner : _enclosing as TypeSymbol;      // a class, record or interface
            if (enclosingType is not null && symbol.ContainingSymbol is TypeSymbol owner && (ReferenceEquals(owner, enclosingType) || enclosingType.InheritsFrom(owner)))
            {
                return "1";
            }

            var unit = symbol as UnitSymbol ?? symbol.ContainingUnit;
            if (unit is null || string.Equals(unit.Name, "System", StringComparison.OrdinalIgnoreCase))
            {
                return "4";
            }

            return ReferenceEquals(unit, Model.Unit) ? "2" : "3";
        }

        private static CompletionItemKind KindOf(Symbol symbol) => SymbolKinds.Of(symbol);
    }
}

/// <summary>
/// CMP-1426 (C-402): the type expected at the caret — the target of <c>:=</c> (so also <c>Result :=</c>), the parameter at the argument's
/// position of every candidate overload with that many parameters, the selector of a <c>case</c> at a label (ordinal types only), the
/// left side of <c>=</c> / <c>&lt;&gt;</c>, a set of the left side's type for <c>in</c>, the function result in <c>Exit(</c>.
/// </summary>
internal sealed class ExpectedType
{
    private readonly ImmutableArray<TypeSymbol> _types;
    private readonly TypeSymbol? _setElement;

    private ExpectedType(ImmutableArray<TypeSymbol> types, TypeSymbol? setElement)
    {
        _types = types;
        _setElement = setElement;
    }

    /// <summary><c>nil</c> fits a class, class reference, pointer, interface, dynamic array or procedural type.</summary>
    public bool AcceptsNil => _types.Any(t => t.TypeKind is TypeKind.Class or TypeKind.ClassRef or TypeKind.Pointer or TypeKind.Interface
        or TypeKind.DynamicArray or TypeKind.Procedural or TypeKind.MethodPointer or TypeKind.Reference);

    public bool IsBoolean => _types.Any(t => t.TypeKind == TypeKind.Boolean);

    /// <summary>
    /// True for a value of exactly an expected type (for a class also of a descendant, for a subrange also of its host type): enum member,
    /// constant, variable, parameter, field, property, function result; for <c>in</c> a value of a set of the left side's type.
    /// </summary>
    public bool Matches(Symbol symbol)
    {
        var type = symbol switch
        {
            EnumMemberSymbol member => member.Type,
            ConstantSymbol constant => constant.Type,
            VariableSymbol variable => variable.Type,
            ParameterSymbol parameter => parameter.Type,
            FieldSymbol field => field.Type,
            PropertySymbol property => property.Type,
            RoutineSymbol routine => routine.ReturnType,
            _ => null,
        };
        if (type is null)
        {
            return false;
        }

        if (_setElement is not null)
        {
            return type is SetTypeSymbol { ElementType: { } element } && ReferenceEquals(element, _setElement);
        }

        return _types.Any(t => ReferenceEquals(type, t) || t is ClassTypeSymbol && type is ClassTypeSymbol && type.InheritsFrom(t)
                               || FitsSubrange(symbol, type, t));
    }

    /// <summary>
    /// C-408: an expected subrange of an enumeration or character type also takes the values of its host type; an enum member or a
    /// constant of the host type only when its ordinal lies inside the subrange (<c>Mon..Fri</c>: not <c>Sat</c>).
    /// </summary>
    private static bool FitsSubrange(Symbol symbol, TypeSymbol type, TypeSymbol expected)
    {
        var host = Ordinals.Host(expected);
        if (ReferenceEquals(host, expected) || !ReferenceEquals(type, host) || Ordinals.Bounds(expected) is not var (low, high))
        {
            return false;
        }

        var ordinal = symbol switch
        {
            EnumMemberSymbol member => member.Ordinal,
            ConstantSymbol { Value.Value: BigInteger value } => value,
            _ => (BigInteger?)null,
        };
        return ordinal is not { } known || low <= known && known <= high;
    }

    /// <summary>The expected type for an item starting right after <paramref name="previous"/>; null when none is known.</summary>
    public static ExpectedType? At(SemanticModel model, SyntaxToken previous, int position)
    {
        var parent = previous.Parent;
        switch (previous.Kind)
        {
            case SyntaxKind.ColonEqualsToken when parent is AssignmentStatementSyntax assignment:
                return Of(TypeOf(model, assignment.Target));
            case SyntaxKind.OpenParenToken or SyntaxKind.CommaToken when parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list:
                return Argument(model, call, list, position);
            case SyntaxKind.OfKeyword when parent is CaseStatementSyntax @case:
                return CaseLabel(model, @case);
            case SyntaxKind.SemicolonToken when parent is CaseStatementSyntax @case
                                                && (@case.ElseKeyword is not { IsMissing: false } elseKeyword || previous.Span.End <= elseKeyword.Span.Start):
                return CaseLabel(model, @case);                                 // between two arms, not among the else statements
            case SyntaxKind.CommaToken when parent is CaseArmSyntax { Parent: CaseStatementSyntax @case }:
                return CaseLabel(model, @case);
            case SyntaxKind.DotDotToken when parent is CaseLabelSyntax { Parent: CaseArmSyntax { Parent: CaseStatementSyntax @case } }:
                return CaseLabel(model, @case);
            case SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken when parent is BinaryExpressionSyntax binary:
                return Of(TypeOf(model, binary.Left));
            case SyntaxKind.InKeyword when parent is BinaryExpressionSyntax binary:
                return TypeOf(model, binary.Left) is { IsOrdinal: true } element ? new ExpectedType([], element) : null;
            default:
                return null;
        }
    }

    private static ExpectedType? Of(TypeSymbol? type) => type is null ? null : new ExpectedType([type], null);

    private static ExpectedType? CaseLabel(SemanticModel model, CaseStatementSyntax @case) =>
        TypeOf(model, @case.Selector) is { IsOrdinal: true } selector ? Of(selector) : null;

    /// <summary>The parameter types at the argument's position of the candidates; in <c>Exit(</c> the enclosing function's result type.</summary>
    private static ExpectedType? Argument(SemanticModel model, InvocationExpressionSyntax call, ArgumentListSyntax list, int position)
    {
        var active = list.Arguments.Separators.Count(s => !s.IsMissing && s.Span.End <= position);
        var routines = model.GetOverloadCandidates(call).AddRange(model.GetGenericMethodCandidates(call));
        if (routines.IsEmpty)
        {
            return active == 0 && call.Expression is SimpleNameSyntax simple
                   && string.Equals(simple.Identifier.Text, "Exit", StringComparison.OrdinalIgnoreCase)
                   && model.LookupSymbols(call.Span.Start, simple.Identifier.Text).FirstOrDefault() is IntrinsicSymbol
                   && model.GetEnclosingResultType(position) is { } result          // CMP-1473: of the anonymous function the caret is in
                ? Of(result)
                : null;
        }

        var types = routines.Where(r => r.Parameters.Length > active && !r.Parameters[active].IsUntyped).Select(r => r.Parameters[active].Type)
            .Where(t => t.TypeKind != TypeKind.Error).Distinct().ToImmutableArray();
        return types.IsEmpty ? null : new ExpectedType(types, null);
    }

    /// <summary>The value type of an expression (a property access included); null when unknown.</summary>
    private static TypeSymbol? TypeOf(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression.IsMissing)
        {
            return null;
        }

        var type = model.GetTypeInfo(expression).Type ?? (model.GetSymbolInfo(expression).Symbol as PropertySymbol)?.Type;
        return type is null || type.TypeKind == TypeKind.Error ? null : type;
    }
}

/// <summary>The item kind of a symbol (completion, workspace symbols).</summary>
internal static class SymbolKinds
{
    public static CompletionItemKind Of(Symbol symbol) => symbol switch
    {
        UnitSymbol => CompletionItemKind.Unit,
        ClassTypeSymbol => CompletionItemKind.Class,
        InterfaceTypeSymbol => CompletionItemKind.Interface,
        EnumTypeSymbol => CompletionItemKind.Enum,
        TypeSymbol => CompletionItemKind.Type,
        EnumMemberSymbol => CompletionItemKind.EnumMember,
        ConstantSymbol => CompletionItemKind.Constant,
        ParameterSymbol => CompletionItemKind.Parameter,
        FieldSymbol => CompletionItemKind.Field,
        PropertySymbol => CompletionItemKind.Property,
        MethodSymbol => CompletionItemKind.Method,
        RoutineSymbol or IntrinsicSymbol => CompletionItemKind.Routine,
        { Kind: SymbolKind.Label } => CompletionItemKind.Label,
        _ => CompletionItemKind.Variable,
    };
}
