using System.Collections.Immutable;
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
                                    string? SymbolId);

public sealed record CompletionDescription(string Signature, string? Documentation);

/// <summary>Code completion (language-service.md §4, CMP-720…724). Returns the whole context set; the IDE filters (CMP-722).</summary>
public static class CompletionService
{
    private static readonly string[] TypeKeywords = ["array", "record", "class", "set of", "string", "procedure", "function", "interface", "packed"];
    private static readonly string[] RoutineDirectives =
        ["virtual", "override", "overload", "abstract", "reintroduce", "dynamic", "final", "static", "inline", "stdcall", "cdecl", "register", "external", "forward", "deprecated", "message"];
    private static readonly string[] PropertySpecifiers = ["read", "write", "stored", "default", "nodefault", "index", "implements"];
    private static readonly string[] DeclarationKeywords = ["const", "type", "var", "procedure", "function", "begin"];
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
        var tree = doc.SemanticModel.SyntaxTree;                            // nodes handed to the model must be its own
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

        var builder = new ItemBuilder(doc, position);
        var previous = PreviousToken(tree, replacement.Start);
        var parent = previous.Parent;
        ct.ThrowIfCancellationRequested();

        if (previous.Kind == SyntaxKind.DotToken && parent is not null)
        {
            Members(builder, parent);
        }
        else if (previous.Kind != SyntaxKind.None && parent?.FirstAncestorOrSelf<UsesClauseSyntax>() is { } uses && !(uses.SemicolonToken is { IsMissing: false } end && end.Span.End <= replacement.Start))
        {
            UnitNames(builder, doc, uses);
        }
        else if (previous.Kind is SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword && InClassBody(parent))
        {
            Overrides(builder, doc, parent!);
        }
        else if (previous.Kind == SyntaxKind.InheritedKeyword)
        {
            Inherited(builder, doc);
        }
        else if (previous.Kind == SyntaxKind.RaiseKeyword || previous.Kind == SyntaxKind.ColonToken && parent is ExceptionHandlerSyntax)
        {
            ExceptionTypes(builder, doc);
        }
        else if (IsTypePosition(previous))
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
            builder.AddKeywords(DeclarationKeywords);
        }
        else if (IsDeclarationStart(previous))
        {
            builder.AddKeywords(DeclarationKeywords);
            if (InClassBody(parent))
            {
                builder.AddKeywords(ClassMemberKeywords);
            }
        }
        else if (IsStatementStart(previous))
        {
            builder.AddVisible(_ => true);
            builder.AddKeywords(StatementKeywords);
            builder.AddSnippets(Snippets);
        }
        else
        {
            builder.AddVisible(s => s is not TypeSymbol || s is ClassTypeSymbol or RecordTypeSymbol or EnumTypeSymbol);
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
                    return new CompletionDescription(symbol.ToDisplayString(), symbol.Documentation);
                }
            }
        }

        return new CompletionDescription(item.Detail ?? item.Label, null);
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
        if (left is null)
        {
            return;
        }

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

        if (model.GetTypeInfo(left).Type is { } valueType)
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

    private static void Overrides(ItemBuilder builder, Document doc, SyntaxNode parent)
    {
        var model = doc.SemanticModel;
        if (parent.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration || model.GetDeclaredSymbol(declaration) is not ClassTypeSymbol type
            || type.Parent is not { } ancestor)
        {
            return;
        }

        var own = type.Methods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var method in model.LookupSymbols(builder.Position, container: ancestor).OfType<MethodSymbol>()
                     .Where(m => m.VmtSlot >= 0 && !own.Contains(m.Name)))
        {
            var heading = method.ToDisplayString();
            heading = heading.Replace(method.ContainingType.Name + ".", "", StringComparison.Ordinal);
            var cut = heading.IndexOf("; ", Math.Max(0, heading.LastIndexOf(')')), StringComparison.Ordinal);    // before "; virtual"
            var insert = (cut < 0 ? heading : heading[..cut])[(heading.IndexOf(' ') + 1)..] + "; override;";
            builder.Add(new CompletionItem(method.Name, CompletionItemKind.Method, insert, "1" + method.Name.ToLowerInvariant(), heading, method.IsDeprecated, builder.Id(method)));
        }
    }

    private static void Inherited(ItemBuilder builder, Document doc)
    {
        var model = doc.SemanticModel;
        if (model.GetEnclosingSymbol(builder.Position) is MethodSymbol { ContainingType: ClassTypeSymbol { Parent: { } ancestor } })
        {
            builder.AddSymbols(model.LookupSymbols(builder.Position, container: ancestor).OfType<MethodSymbol>());
        }
    }

    private static void ExceptionTypes(ItemBuilder builder, Document doc)
    {
        var classes = doc.SemanticModel.LookupSymbols(builder.Position).OfType<ClassTypeSymbol>().ToList();
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
        var previous = default(SyntaxToken);
        foreach (var token in tree.Root.OwnTokens())
        {
            if (token.Span.End > position || token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }

            if (!token.IsMissing)
            {
                previous = token;
            }
        }

        return previous;
    }

    private static bool InClassBody(SyntaxNode? node) =>
        node?.AncestorsAndSelf().FirstOrDefault(n => n is ClassTypeSyntax or RecordTypeSyntax or InterfaceTypeSyntax or RoutineDeclarationSyntax or BlockSyntax)
            is ClassTypeSyntax or RecordTypeSyntax or InterfaceTypeSyntax;

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
               && previous.Parent is ProgramSyntax or UnitSyntax or UsesClauseSyntax or VarDeclarationSyntax or ConstDeclarationSyntax or TypeDeclarationSyntax
                   or FieldDeclarationSyntax or PropertyDeclarationSyntax or MethodDeclarationSyntax or RoutineDeclarationSyntax or DirectiveSyntax;
    }

    private static bool IsStatementStart(SyntaxToken previous) =>
        previous.Kind is SyntaxKind.BeginKeyword or SyntaxKind.ThenKeyword or SyntaxKind.ElseKeyword or SyntaxKind.DoKeyword or SyntaxKind.RepeatKeyword
            or SyntaxKind.TryKeyword or SyntaxKind.FinallyKeyword or SyntaxKind.ExceptKeyword or SyntaxKind.InitializationKeyword
            or SyntaxKind.FinalizationKeyword
        || previous.Kind == SyntaxKind.SemicolonToken && previous.Parent is CompoundStatementSyntax or RepeatStatementSyntax or TryFinallyStatementSyntax
            or TryExceptStatementSyntax or InitializationSectionSyntax or FinalizationSectionSyntax or CaseStatementSyntax
        || previous.Kind == SyntaxKind.ColonToken && previous.Parent is CaseArmSyntax;

    private static CompletionItem Keyword(string word, CompletionItemKind kind = CompletionItemKind.Keyword) =>
        new(word, kind, word, "5" + word.ToLowerInvariant(), null, false, null);

    // ------------------------------------------------------------------------------------------------ items

    /// <summary>Collects items once per label and kind, with the CMP-723 sort groups.</summary>
    private sealed class ItemBuilder(Document doc, int position)
    {
        private readonly List<CompletionItem> _items = [];
        private readonly HashSet<(string, CompletionItemKind)> _seen = [];
        private readonly Symbol? _enclosing = doc.SemanticModel.GetEnclosingSymbol(position);

        public SemanticModel Model { get; } = doc.SemanticModel;

        public int Position { get; } = position;

        public void Add(CompletionItem item)
        {
            if (_seen.Add((item.Label.ToLowerInvariant(), item.Kind)))
            {
                _items.Add(item);
            }
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

        public void AddVisible(Func<Symbol, bool> filter) => AddSymbols(Model.LookupSymbols(Position).Where(filter));

        public void AddSymbols(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                AddSymbol(symbol);
            }
        }

        public void AddSymbol(Symbol symbol, string? prefix = null)
        {
            if (symbol.Name.Length == 0 || symbol.Name[0] is '$' or '.' || symbol.Name.Contains('<'))
            {
                return;
            }

            Add(new CompletionItem(symbol.Name, KindOf(symbol), symbol.Name, (prefix ?? "") + Group(symbol) + symbol.Name.ToLowerInvariant(),
                                   symbol.ToDisplayString(), symbol.IsDeprecated, Id(symbol)));
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

        public ImmutableArray<CompletionItem> Build() => [.. _items.OrderBy(i => i.SortText, StringComparer.Ordinal)];

        /// <summary>CMP-723 group digit.</summary>
        private string Group(Symbol symbol)
        {
            if (symbol is ParameterSymbol || symbol is VariableSymbol { IsGlobal: false } && symbol.ContainingSymbol is RoutineSymbol)
            {
                return "0";
            }

            var enclosingType = _enclosing is MethodSymbol method ? method.ContainingType : _enclosing as TypeSymbol;
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
