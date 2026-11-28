using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Forms;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using FreexComp.Compiler.Units;

namespace FreexComp.LanguageService;

/// <summary>Where the occurrences of a rename target can lie (CMP-1500).</summary>
public enum RenameScope
{
    /// <summary>Anywhere in the source units of the compilation.</summary>
    Project,

    /// <summary>Declared in a routine, and every occurrence is in the document's own text.</summary>
    Local,
}

/// <summary>
/// What is under the caret for a rename: <see cref="Span"/> is the whole name (a dotted unit name is one name). The facts of the
/// target (CMP-1500) have their defaults when <see cref="CanRename"/> is false.
/// </summary>
public sealed record RenameInfo(bool CanRename, string? Reason, TextSpan Span, string CurrentName)
{
    /// <summary>The kind of the target; <see cref="CompletionItemKind.Unit"/> for a unit, program or library name.</summary>
    public CompletionItemKind Kind { get; init; }

    /// <summary>
    /// A member: its type, qualified without the unit (<c>TCustomer</c>); a unit-level symbol: its unit; a symbol declared in a
    /// routine: the routine, qualified without the unit (<c>TCustomer.Load</c>; inside an anonymous method: the routine the
    /// anonymous method is written in); a unit: empty.
    /// </summary>
    public string ContainerName { get; init; } = "";

    public RenameScope Scope { get; init; }

    /// <summary>Scope <see cref="RenameScope.Local"/>: the spans <c>Rename</c> edits in the document, in text order; else empty.</summary>
    public ImmutableArray<TextSpan> LocalSpans { get; init; } = [];

    /// <summary>The normalized paths of every declaration site of everything renamed together (without the overload option), in path order.</summary>
    public ImmutableArray<string> DeclarationPaths { get; init; } = [];

    /// <summary>A field or method with visibility <c>published</c>.</summary>
    public bool IsPublished { get; init; }

    /// <summary>A routine or method: the routines of its name declared in its container, itself included; other kinds 0.</summary>
    public int OverloadCount { get; init; }
}

/// <summary>The options of a rename (CMP-1501); they do not apply to a unit rename.</summary>
public sealed record RenameOptions
{
    public static RenameOptions Default { get; } = new();

    /// <summary>CMP-1503: false = no form is read and no edit of a form is returned.</summary>
    public bool Forms { get; init; } = true;

    /// <summary>
    /// CMP-1502: full path → text of forms that are not read from disk (an open form with its present text); the edits of such a
    /// form refer to the given text. Keys are compared as normalized paths; a key that names no embedded form (or is not a full
    /// path) is ignored.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FormTexts { get; init; }

    /// <summary>CMP-1504: a routine or method is renamed with the other routines of its name declared in the same container.</summary>
    public bool Overloads { get; init; }
}

/// <summary>
/// The edits of a rename, per file in path order (changes in text order, against the snapshot's text), or the reason it was
/// refused. <see cref="FileRename"/> is the file operation a unit rename needs; edits name the file by its present path.
/// </summary>
public sealed record RenameResult(bool Success, string? Error, ImmutableArray<DocumentEdit> Edits, FileRename? FileRename = null);

public sealed record FileRename(string OldPath, string NewPath);

/// <summary>
/// Rename (language-service.md §8, CMP-750…753, C-139). Pure: nothing is written; the caller applies the edits and the file
/// rename. The conflict check binds the edited texts in a forked compilation.
/// </summary>
public static class RenameService
{
    private const string NotAnIdentifier = "Only identifiers can be renamed.";

    /// <summary>CMP-750: whether the name at <paramref name="position"/> can be renamed, its span and present spelling.</summary>
    public static RenameInfo GetRenameInfo(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var target = Find(doc, position, overloads: false, ct);
        var info = new RenameInfo(target.Reason is null, target.Reason, target.Span, target.Name);
        if (target.Reason is not null || target.Symbol is not { } symbol)
        {
            return info;
        }

        // CMP-1500: the facts of the target
        var local = LocalSpans(doc, symbol, target.Related, ct);
        return info with
        {
            Kind = SymbolKinds.Of(symbol),
            ContainerName = ContainerName(doc, symbol),
            Scope = local.IsEmpty ? RenameScope.Project : RenameScope.Local,
            LocalSpans = local,
            DeclarationPaths = [.. target.Related.SelectMany(r => r.Locations).Select(l => PathUtil.Normalize(l.Path)).Distinct(PathUtil.Comparer).Order(PathUtil.Comparer)],
            IsPublished = symbol is FieldSymbol or MethodSymbol && symbol.Visibility == Visibility.Published,
            OverloadCount = Overloads(symbol).Count,
        };
    }

    /// <summary>CMP-751…753: the edits that rename the symbol at <paramref name="position"/>, after the conflict check.</summary>
    public static RenameResult Rename(Document doc, int position, string newName, CancellationToken ct) =>
        Rename(doc, position, newName, RenameOptions.Default, ct);

    /// <summary>CMP-1501: <see cref="Rename(Document, int, string, CancellationToken)"/> with options (forms, form texts, overloads).</summary>
    public static RenameResult Rename(Document doc, int position, string newName, RenameOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(newName);
        ArgumentNullException.ThrowIfNull(options);
        var target = Find(doc, position, options.Overloads, ct);
        if (target.Reason is not null)
        {
            return Fail(target.Reason);
        }

        var unit = target.Symbol as UnitSymbol;
        if (target.Symbol is LabelSymbol)
        {
            if (!SyntaxFacts.IsValidLabelName(newName))
            {
                return Fail($"'{newName}' is not a valid label.");             // LANG-380 (C-321): an identifier or a decimal integer
            }
        }
        else if (unit is null ? !SyntaxFacts.IsValidIdentifier(newName) : !newName.Split('.').All(SyntaxFacts.IsValidIdentifier))
        {
            return Fail(unit is null ? $"'{newName}' is not a valid identifier." : $"'{newName}' is not a valid unit name.");
        }

        if (string.Equals(newName, target.Name, StringComparison.Ordinal))
        {
            return new RenameResult(true, null, []);
        }

        return unit is null ? RenameSymbol(doc, target, newName, options, ct) : RenameUnit(doc, target, unit, newName, ct);
    }

    private static RenameResult Fail(string error) => new(false, error, []);

    // ---------------------------------------------------------------------------------------------------------- the target

    private sealed record Target(TextSpan Span, string Name, Symbol? Symbol, ImmutableArray<Symbol> Related, string? Reason);

    /// <param name="overloads">CMP-1504: a routine is found with the routines of its name (<see cref="Closure"/>).</param>
    private static Target Find(Document doc, int position, bool overloads, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var model = doc.SemanticModel;
        var tree = model.SyntaxTree;
        var token = tree.FindToken(Math.Clamp(position, 0, tree.Text.Text.Length));
        if (!SyntaxFacts.IsNameToken(token) || token.IsMissing || !token.Span.Contains(position))
        {
            var before = position > 0 ? tree.FindToken(position - 1) : default;
            if (!SyntaxFacts.IsNameToken(before) || before.IsMissing || before.Span.End != position)
            {
                return new Target(default, "", null, [], NotAnIdentifier);
            }

            token = before;
        }

        // a unit name: the heading, a uses entry, the qualifier of a qualified name
        if (HeadingName(tree.Root) is { } heading && heading.Span.Contains(token.Span.Start))
        {
            return Unit(doc, heading.Span, UnitBinder.NameOf(heading), model.Unit);
        }

        if (token.Parent?.FirstAncestorOrSelf<UsedUnitSyntax>() is { } used && used.Name.Span.Contains(token.Span.Start))
        {
            var name = UnitBinder.NameOf(used.Name);
            return model.GetSymbolInfo(used, ct).Symbol is UnitSymbol usedUnit
                ? Unit(doc, used.Name.Span, name, usedUnit)
                : new Target(used.Name.Span, name, null, [], $"The unit '{name}' was not found.");
        }

        foreach (var (span, qualified) in Qualifiers(model))
        {
            if (span.Contains(token.Span.Start))
            {
                return Unit(doc, span, tree.Text.Text.Substring(span.Start, span.Length), qualified);
            }
        }

        var text = SyntaxFacts.NameOf(token);
        if (QuickInfoService.SymbolOf(model, token) is not { } symbol)
        {
            return new Target(token.Span, text, null, [], $"'{text}' does not name a declaration.");
        }

        if (symbol is UnitSymbol plainUnit)
        {
            return Unit(doc, token.Span, text, plainUnit);
        }

        if (symbol is IntrinsicSymbol || symbol.Locations.IsEmpty)
        {
            return new Target(token.Span, text, symbol, [], $"'{text}' is built in and cannot be renamed.");
        }

        if (!IdentifierComparer.Instance.Equals(symbol.Name, text))
        {
            return new Target(token.Span, text, symbol, [], $"'{text}' is another name of '{symbol.Name}' and cannot be renamed.");
        }

        var related = Closure(doc.Compilation, symbol, overloads);
        return new Target(token.Span, text, symbol, related, related.Select(r => NotRenamable(doc, r, text)).FirstOrDefault(r => r is not null));
    }

    private static Target Unit(Document doc, TextSpan span, string name, UnitSymbol unit) =>
        new(span, name, unit, [unit], unit.Locations.IsEmpty ? $"'{name}' has no heading to rename." : NotRenamable(doc, unit, name));

    /// <summary>CMP-750: declared under the RTL root, or in a file that is not a source unit of the compilation (<c>.pcu</c>, include file).</summary>
    private static string? NotRenamable(Document doc, Symbol symbol, string name)
    {
        var rtl = doc.Compilation.RtlRoot + @"\";
        var sources = Paths(doc);
        foreach (var location in symbol.Locations)
        {
            var path = PathUtil.Normalize(location.Path);
            if (path.StartsWith(rtl, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{name}' is declared in the run-time library and cannot be renamed.";
            }

            if (!sources.Contains(path, PathUtil.Comparer))
            {
                return $"'{name}' is not declared in a source unit of the project and cannot be renamed.";
            }
        }

        return null;
    }

    /// <summary>The source units of the compilation and the document itself (a stand-alone root, CMP-701).</summary>
    private static ImmutableArray<string> Paths(Document doc) => Paths(doc.Compilation, doc.Path);

    private static ImmutableArray<string> Paths(Compilation compilation, string own) =>
        [.. compilation.SourceUnitPaths.Append(own).Select(PathUtil.Normalize).Distinct(PathUtil.Comparer).Order(PathUtil.Comparer)];

    /// <summary>
    /// What is renamed together: the symbol with its override chain and redeclarations (CMP-742), and across interfaces — a
    /// method that implements an interface method goes with it, and so do the other implementations. With
    /// <paramref name="overloads"/> (CMP-1504), for a routine whose name has more than one routine in its container: also those
    /// routines, each with what is renamed together with it, and the routines of that name in the container of everything added.
    /// </summary>
    private static ImmutableArray<Symbol> Closure(Compilation compilation, Symbol symbol, bool overloads)
    {
        overloads = overloads && Overloads(symbol).Count > 1;
        var all = new List<Symbol>();
        var pending = new Queue<Symbol>();
        void Add(Symbol s)
        {
            if (!all.Any(a => SymbolKey.AreSame(a, s)))
            {
                all.Add(s);
                pending.Enqueue(s);
            }
        }

        Add(symbol);
        List<ClassTypeSymbol>? classes = null;
        while (pending.Count > 0)
        {
            var next = pending.Dequeue();
            foreach (var related in NavigationService.Related(compilation, next))
            {
                Add(related);
            }

            if (overloads)
            {
                Overloads(next).ForEach(Add);
            }

            if (next is not MethodSymbol method)
            {
                continue;
            }

            classes ??= [.. NavigationService.AllTypes(compilation).OfType<ClassTypeSymbol>()];
            foreach (var implemented in classes.SelectMany(c => c.Interfaces))
            {
                var methods = implemented.Interface.AllMethods;
                for (var i = 0; i < methods.Length && i < implemented.Implementations.Length; i++)
                {
                    if (SymbolKey.AreSame(methods[i], method))
                    {
                        Add(implemented.Implementations[i]);
                    }
                    else if (SymbolKey.AreSame(implemented.Implementations[i], method))
                    {
                        Add(methods[i]);
                    }
                }
            }
        }

        return [.. all];
    }

    /// <summary>
    /// CMP-1500, 1504: the routines of the routine's name declared in its container, itself included — a unit's two sections are
    /// one container, a type counts its own declarations (generic methods too, LANG-980). Empty for another kind of symbol.
    /// </summary>
    private static List<Symbol> Overloads(Symbol symbol)
    {
        var group = new List<Symbol>();
        if (symbol is not (RoutineSymbol or GenericMethodSymbol))
        {
            return group;
        }

        IEnumerable<Symbol> declared = symbol.ContainingSymbol switch
        {
            UnitSymbol unit => unit.InterfaceMembers.Concat(unit.ImplementationMembers),
            ClassTypeSymbol type => type.Methods.Concat<Symbol>(type.GenericMethods),
            RecordTypeSymbol type => type.Methods.Concat<Symbol>(type.GenericMethods),
            TypeSymbol type => type.Members,
            RoutineSymbol => symbol is RoutineSymbol { DefinitionScope: { } scope } ? scope.LookupRoutinesLocal(symbol.Name) : [],
            _ => [],
        };
        foreach (var routine in declared.Where(d => d is RoutineSymbol or GenericMethodSymbol && IdentifierComparer.Instance.Equals(d.Name, symbol.Name)).Append(symbol))
        {
            if (!group.Any(g => SymbolKey.AreSame(g, routine)))
            {
                group.Add(routine);
            }
        }

        return group;
    }

    // ---------------------------------------------------------------------------------------------------------- facts (CMP-1500)

    private static string ContainerName(Document doc, Symbol symbol)
    {
        if (symbol is UnitSymbol)
        {
            return "";
        }

        if (symbol.ContainingSymbol is { } container)
        {
            return Qualified(container);
        }

        // without a container: a named pointer type (C-366), or an inline variable of a main block - the unit of its file
        var path = symbol.Locations[0].Path;
        var unit = symbol.ContainingUnit
                   ?? doc.Compilation.UnitSymbols.Append(doc.SemanticModel.Unit).FirstOrDefault(u => u.Locations.Any(l => PathUtil.AreEqual(l.Path, path)));
        return unit?.Name ?? "";
    }

    /// <summary>A unit: its name; a type or routine: its name qualified by its types and routines, without the unit.</summary>
    private static string Qualified(Symbol container)
    {
        if (container is MethodSymbol { IsAnonymous: true } anonymous)
        {
            // named after the routine the anonymous method is written in (CMP-1473), or the unit for one of a unit-level body
            var owner = anonymous.AnonymousParent;
            while (owner is MethodSymbol { IsAnonymous: true } outer)
            {
                owner = outer.AnonymousParent;
            }

            return owner is null ? anonymous.ContainingUnit?.Name ?? "" : Qualified(owner);
        }

        return container.ContainingSymbol is null or UnitSymbol ? container.Name : Qualified(container.ContainingSymbol) + "." + container.Name;
    }

    /// <summary>
    /// The occurrences of a target of scope <c>Local</c> in the document, in text order (what <see cref="RenameSymbol"/> edits there);
    /// empty for scope <c>Project</c>. Local: the symbol is declared in a routine, every declaration site of the closure and of the
    /// routines around it is in the document's file, and no token the document reads from an include file names the closure.
    /// </summary>
    private static ImmutableArray<TextSpan> LocalSpans(Document doc, Symbol symbol, ImmutableArray<Symbol> related, CancellationToken ct)
    {
        if (symbol.ContainingSymbol is not RoutineSymbol)
        {
            return [];
        }

        var sites = related.SelectMany(r => r.Locations).ToList();
        for (var routine = symbol.ContainingSymbol as RoutineSymbol; routine is not null;
             routine = routine is MethodSymbol { IsAnonymous: true } anonymous ? anonymous.AnonymousParent : routine.ContainingSymbol as RoutineSymbol)
        {
            sites.AddRange(routine.Locations);
        }

        if (!sites.All(l => PathUtil.AreEqual(l.Path, doc.Path)))
        {
            return [];
        }

        var model = doc.SemanticModel;
        var file = model.SyntaxTree.Text;
        var names = related.Select(r => r.Name).ToHashSet(IdentifierComparer.Instance);
        foreach (var token in model.SyntaxTree.Root.DescendantTokens())
        {
            if (SyntaxNode.IsOwn(token, file) || !SyntaxFacts.IsNameToken(token) || token.IsMissing || !names.Contains(SyntaxFacts.NameOf(token)))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            if (QuickInfoService.SymbolOf(model, token) is { } bound && related.Any(r => SymbolKey.AreSame(r, bound)))
            {
                return [];                                                  // an occurrence in an include file
            }
        }

        return [.. NavigationService.Occurrences(model, [.. related], ct).Select(o => o.Token.Span)];
    }

    // ---------------------------------------------------------------------------------------------------------- symbols

    private static RenameResult RenameSymbol(Document doc, Target target, string newName, RenameOptions options, CancellationToken ct)
    {
        var compilation = doc.Compilation;
        var related = target.Related.ToList();
        var edits = new SortedDictionary<string, List<TextChange>>(PathUtil.Comparer);
        foreach (var path in Paths(doc))
        {
            ct.ThrowIfCancellationRequested();
            var changes = NavigationService.Occurrences(ModelOf(doc, path), related, ct).Select(o => new TextChange(o.Token.Span, newName)).ToList();
            if (changes.Count > 0)
            {
                edits[path] = changes;
            }
        }

        var plan = new Plan(doc, edits, null, newName, target.Name);
        if (plan.Check(related, null, ct) is { } conflict)
        {
            return Fail(conflict);
        }

        if (!options.Forms)
        {
            return new RenameResult(true, null, Edits(edits));              // CMP-1503: no form is read, none is edited
        }

        // CMP-1502: the texts given for forms, by normalized path
        Dictionary<string, string>? formTexts = null;
        if (options.FormTexts is { } given)
        {
            formTexts = new Dictionary<string, string>(PathUtil.Comparer);
            foreach (var (path, text) in given)
            {
                if (Path.IsPathFullyQualified(path))                        // another key names no form
                {
                    formTexts[PathUtil.Normalize(path)] = text;
                }
            }
        }

        // CMP-753: the companion .form of a form class's published field or method
        foreach (var member in related.Where(r => r is FieldSymbol or MethodSymbol && r.Visibility == Visibility.Published && r.ContainingSymbol is ClassTypeSymbol))
        {
            if (FormChanges(compilation, member, newName, formTexts, edits) is { } error)
            {
                return Fail(error);
            }
        }

        return new RenameResult(true, null, Edits(edits));
    }

    private static SemanticModel ModelOf(Document doc, string path) =>
        PathUtil.AreEqual(path, doc.Path) ? doc.SemanticModel : doc.Compilation.GetSemanticModel(path);

    private static ImmutableArray<DocumentEdit> Edits(SortedDictionary<string, List<TextChange>> edits) =>
        [.. edits.Select(e => new DocumentEdit(e.Key, [.. e.Value.OrderBy(c => c.Span.Start)]))];

    /// <summary>
    /// CMP-753: for a published field the object of that name, for a published method every event handler value of that name, in
    /// each form the member's unit embeds (<c>{$FORM}</c>) whose root class is the member's class. Returns an error text when a
    /// form cannot be scanned. CMP-1502: a form with a text in <paramref name="formTexts"/> is that text (the file is not looked at).
    /// </summary>
    private static string? FormChanges(Compilation compilation, Symbol member, string newName, Dictionary<string, string>? formTexts,
        SortedDictionary<string, List<TextChange>> edits)
    {
        var owner = (ClassTypeSymbol)member.ContainingSymbol!;
        var unitPath = PathUtil.Normalize(member.Locations[0].Path);
        foreach (var directive in compilation.GetSyntaxTree(unitPath).Directives.Where(d => d.IsActive && d.Name == "FORM"))
        {
            var name = DirectiveProcessor.Unquote(directive.Argument).Replace('/', '\\');
            if (name.Length == 0)
            {
                continue;
            }

            var path = PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(unitPath)!, name));
            var unreadable = $"The form file '{Path.GetFileName(path)}' cannot be read, so its references cannot be updated.";
            string? text = null;
            if (formTexts is null || !formTexts.TryGetValue(path, out text))
            {
                if (!compilation.FileSystem.FileExists(path))
                {
                    continue;                                               // the build reports the missing file (E3100)
                }

                try
                {
                    text = FormFileScanner.Decode(compilation.FileSystem.ReadAllBytes(path));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return unreadable;
                }
            }

            if (text is null || FormFileScanner.Scan(path, text).Root is not { } root)
            {
                return unreadable;
            }

            if (!IdentifierComparer.Instance.Equals(root.ClassName, owner.Name))
            {
                continue;
            }

            var objects = root.Descendants().Prepend(root).ToList();
            var sites = member is FieldSymbol
                ? objects.Skip(1).Where(o => !o.IsOverride && IdentifierComparer.Instance.Equals(o.Name, member.Name)).Select(o => (o.Line, o.Column, o.Name))
                : objects.SelectMany(o => o.Events).Where(e => IdentifierComparer.Instance.Equals(e.Handler, member.Name)).Select(e => (e.Line, e.Column, Name: e.Handler));
            var starts = LineStarts(text);
            foreach (var (line, column, written) in sites)
            {
                var at = line >= 1 && line <= starts.Count ? starts[line - 1] + column - 1 : -1;
                if (at < 0 || at + written.Length > text.Length || !string.Equals(text.Substring(at, written.Length), written, StringComparison.Ordinal))
                {
                    return unreadable;
                }

                if (!edits.TryGetValue(path, out var changes))
                {
                    edits[path] = changes = [];
                }

                if (!changes.Any(c => c.Span.Start == at))
                {
                    changes.Add(new TextChange(new TextSpan(at, written.Length), newName));
                }
            }
        }

        return null;
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    // ---------------------------------------------------------------------------------------------------------- units

    private static RenameResult RenameUnit(Document doc, Target target, UnitSymbol unit, string newName, CancellationToken ct)
    {
        var compilation = doc.Compilation;
        var oldPath = PathUtil.Normalize(unit.Locations[0].Path);
        if (compilation.FindUnit(newName) is { } existing && !PathUtil.AreEqual(existing.Path, oldPath))
        {
            return Fail($"A unit named '{newName}' already exists.");
        }

        // CMP-752: a unit's file is named after it; a program's or library's file only when it already is
        var fileName = Path.GetFileName(oldPath);
        FileRename? rename = !unit.IsProgram || IdentifierComparer.Instance.Equals(Path.GetFileNameWithoutExtension(oldPath), unit.Name)
            ? new FileRename(oldPath, Path.Combine(Path.GetDirectoryName(oldPath)!, newName + Path.GetExtension(oldPath)))
            : null;
        var edits = new SortedDictionary<string, List<TextChange>>(PathUtil.Comparer);
        var names = new Dictionary<string, List<TextSpan>>(PathUtil.Comparer);
        foreach (var path in Paths(doc))
        {
            ct.ThrowIfCancellationRequested();
            var model = ModelOf(doc, path);
            var spans = UnitNames(model, unit, ct);
            if (spans.Count == 0)
            {
                continue;
            }

            names[path] = spans;
            var changes = spans.Select(s => new TextChange(s, newName)).ToList();
            if (rename is not null)
            {
                changes.AddRange(InPaths(model, unit, fileName, Path.GetFileName(rename.NewPath)));
            }

            edits[path] = changes;
        }

        var plan = new Plan(doc, edits, rename, newName, target.Name);
        return plan.Check([unit], names, ct) is { } conflict ? Fail(conflict) : new RenameResult(true, null, Edits(edits), rename);
    }

    private static NameSyntax? HeadingName(SyntaxNode root) => root switch
    {
        UnitSyntax { Name.IsMissing: false } unit => unit.Name,
        LibrarySyntax { Name.IsMissing: false } library => library.Name,
        ProgramSyntax { Name: { IsMissing: false } name } => name,
        _ => null,
    };

    /// <summary>The whole-name spans in the model's file that name <paramref name="unit"/>: heading, uses entries, qualifiers; in text order.</summary>
    private static List<TextSpan> UnitNames(SemanticModel model, UnitSymbol unit, CancellationToken ct)
    {
        var spans = new List<TextSpan>();
        if (SymbolKey.AreSame(model.Unit, unit) && HeadingName(model.SyntaxTree.Root) is { } heading)
        {
            spans.Add(heading.Span);
        }

        spans.AddRange(model.SyntaxTree.Root.DescendantNodes().OfType<UsedUnitSyntax>()
            .Where(u => !u.Name.IsMissing && SymbolKey.AreSame(model.GetSymbolInfo(u, ct).Symbol, unit)).Select(u => u.Name.Span));
        spans.AddRange(Qualifiers(model).Where(q => SymbolKey.AreSame(q.Unit, unit)).Select(q => q.Span));
        return [.. spans.Distinct().OrderBy(s => s.Start)];
    }

    /// <summary>The file name inside <c>in '…'</c> of the uses entries naming <paramref name="unit"/>.</summary>
    private static IEnumerable<TextChange> InPaths(SemanticModel model, UnitSymbol unit, string oldFile, string newFile)
    {
        foreach (var used in model.SyntaxTree.Root.DescendantNodes().OfType<UsedUnitSyntax>())
        {
            if (used.Path is not { IsMissing: false } literal || !SymbolKey.AreSame(model.GetSymbolInfo(used).Symbol, unit)
                || literal.Value is not string value || !PathUtil.Comparer.Equals(Path.GetFileName(value.Replace('/', '\\')), oldFile))
            {
                continue;
            }

            var at = literal.Text.LastIndexOf(oldFile, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                yield return new TextChange(new TextSpan(literal.Span.Start + at, oldFile.Length), newFile);
            }
        }
    }

    /// <summary>
    /// LANG-311, 538: the unit qualifiers of the model's file — for each designator or type name <c>a.b.….n</c> the longest
    /// proper prefix that is the name of a used unit (as written in a uses clause, or its own name) or of the file's own unit,
    /// unless <c>a</c> binds to a nearer declaration.
    /// </summary>
    private static List<(TextSpan Span, UnitSymbol Unit)> Qualifiers(SemanticModel model)
    {
        var root = model.SyntaxTree.Root;
        var units = new Dictionary<string, UnitSymbol>(IdentifierComparer.Instance);
        if (!model.Unit.Locations.IsEmpty)
        {
            units[model.Unit.Name] = model.Unit;
        }

        foreach (var used in root.DescendantNodes().OfType<UsedUnitSyntax>())
        {
            if (!used.Name.IsMissing && model.GetSymbolInfo(used).Symbol is UnitSymbol unit)
            {
                units[unit.Name] = unit;
                units[UnitBinder.NameOf(used.Name)] = unit;
            }
        }

        var heading = HeadingName(root);
        var found = new List<(TextSpan, UnitSymbol)>();
        var parts = new List<SyntaxToken>();
        foreach (var first in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (first.Identifier.IsMissing
                || first.Parent is MemberAccessExpressionSyntax member && ReferenceEquals(member.Name, first)
                || first.Parent is QualifiedNameSyntax qualifiedName && ReferenceEquals(qualifiedName.Right, first)
                || heading is not null && heading.Span.Contains(first.Span.Start)
                || first.FirstAncestorOrSelf<UsedUnitSyntax>() is not null)
            {
                continue;
            }

            parts.Clear();
            parts.Add(first.Identifier);
            for (SyntaxNode node = first; ;)
            {
                SimpleNameSyntax? next = node.Parent switch
                {
                    MemberAccessExpressionSyntax access when ReferenceEquals(access.Expression, node) => access.Name,
                    QualifiedNameSyntax qualified when ReferenceEquals(qualified.Left, node) => qualified.Right,
                    _ => null,
                };
                if (next is null || next.Identifier.IsMissing)
                {
                    break;
                }

                parts.Add(next.Identifier);
                node = node.Parent!;
                if (next is not IdentifierNameSyntax)
                {
                    break;
                }
            }

            for (var length = parts.Count - 1; length >= 1; length--)
            {
                var name = string.Join(".", parts.Take(length).Select(p => p.Value as string ?? p.Text));
                if (units.TryGetValue(name, out var unit))
                {
                    if (QuickInfoService.SymbolOf(model, parts[0]) is null or UnitSymbol)
                    {
                        found.Add((TextSpan.FromBounds(parts[0].Span.Start, parts[length - 1].Span.End), unit));
                    }

                    break;
                }
            }
        }

        return found;
    }

    // ---------------------------------------------------------------------------------------------------------- conflict check

    /// <summary>
    /// CMP-751: the edits applied to a forked compilation. Every edited name must bind to the renamed symbol, and every other
    /// identifier of the edited files that was bound must still bind to the same declaration.
    /// </summary>
    private sealed class Plan(Document doc, SortedDictionary<string, List<TextChange>> edits, FileRename? rename, string newName, string oldName)
    {
        private string NewPath(string path) => rename is not null && PathUtil.AreEqual(path, rename.OldPath) ? PathUtil.Normalize(rename.NewPath) : path;

        /// <summary>The offset after the edits of an offset that is not inside an edited span.</summary>
        private int Map(string path, int position)
        {
            if (!edits.TryGetValue(path, out var changes))
            {
                return position;
            }

            return position + changes.Where(c => c.Span.End <= position).Sum(c => c.NewText.Length - c.Span.Length);
        }

        private bool Same(Symbol before, Symbol after)
        {
            if (before.Kind != after.Kind || before.Locations.IsEmpty != after.Locations.IsEmpty)
            {
                return false;
            }

            if (before.Locations.IsEmpty)
            {
                // Result and Self have no declaration site: the same name of the same routine (CMP-1473, C-415: a renamed
                // routine, class or unit - or an anonymous method inside one - keeps them, whatever their qualified names say)
                return before.ContainingSymbol is RoutineSymbol routine && after.ContainingSymbol is RoutineSymbol other
                    ? IdentifierComparer.Instance.Equals(before.Name, after.Name) && Same(routine, other)
                    : IdentifierComparer.Instance.Equals(before.QualifiedName, after.QualifiedName);
            }

            var path = PathUtil.Normalize(before.Locations[0].Path);
            return PathUtil.AreEqual(NewPath(path), after.Locations[0].Path) && Map(path, before.Locations[0].Span.Start) == after.Locations[0].Span.Start;
        }

        private string Conflict(SyntaxTree tree, int position, string text)
        {
            var at = tree.Text.Lines.GetLinePosition(position);
            return $"Renaming '{oldName}' to '{newName}' changes the meaning of '{text}' at {Path.GetFileName(tree.Text.Path)}({at.Line + 1},{at.Column + 1}).";
        }

        /// <summary>The first conflict (files in path order, then text order), or null. <paramref name="unitNames"/>: the name spans of a unit rename.</summary>
        public string? Check(IReadOnlyList<Symbol> renamed, Dictionary<string, List<TextSpan>>? unitNames, CancellationToken ct)
        {
            var compilation = doc.Compilation;
            var fork = compilation;
            foreach (var (path, changes) in edits)
            {
                var text = new System.Text.StringBuilder(compilation.GetSyntaxTree(path).Text.Text);
                foreach (var change in changes.OrderByDescending(c => c.Span.Start))
                {
                    text.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
                }

                fork = fork.WithSourceText(SourceText.From(NewPath(path), text.ToString()));
            }

            if (rename is not null && PathUtil.AreEqual(compilation.Options.MainFile, rename.OldPath))
            {
                fork = fork.WithOptions(compilation.Options with { MainFile = PathUtil.Normalize(rename.NewPath) });
            }

            var last = newName[(newName.LastIndexOf('.') + 1)..];
            foreach (var (path, changes) in edits)
            {
                ct.ThrowIfCancellationRequested();
                var before = PathUtil.AreEqual(path, doc.Path) ? doc.SemanticModel : compilation.GetSemanticModel(path);
                var after = fork.GetSemanticModel(NewPath(path));
                var newTree = after.SyntaxTree;
                var names = unitNames is not null && unitNames.TryGetValue(path, out var spans) ? spans : [];
                var expected = unitNames is null ? null : UnitNames(after, after.Compilation.GetSemanticModel(NewPath(PathUtil.Normalize(renamed[0].Locations[0].Path))).Unit, ct);
                foreach (var token in before.SyntaxTree.Root.OwnTokens())
                {
                    if (!SyntaxFacts.IsNameToken(token) || token.IsMissing)
                    {
                        continue;
                    }

                    var start = token.Span.Start;
                    if (names.FirstOrDefault(s => s.Contains(start)) is { Length: > 0 } unitName)
                    {
                        // a unit name: after the edit the same place must still name the renamed unit
                        if (unitName.Start == start && !expected!.Contains(new TextSpan(Map(path, start), newName.Length)))
                        {
                            return Conflict(before.SyntaxTree, start, newName);
                        }

                        continue;
                    }

                    var edit = changes.FirstOrDefault(c => c.Span.Contains(start));
                    if (edit.Span.Length > 0 && edit.Span.Start != start)
                    {
                        continue;                                           // inside an edited span that is not a name (none today)
                    }

                    ct.ThrowIfCancellationRequested();
                    if (QuickInfoService.SymbolOf(before, token) is not { } bound)
                    {
                        continue;
                    }

                    var edited = edit.Span.Length > 0;
                    var newToken = newTree.FindToken(Map(path, start));
                    var now = SyntaxFacts.IsNameToken(newToken) && newToken.Span.Start == Map(path, start) ? QuickInfoService.SymbolOf(after, newToken) : null;
                    if (now is null || !Same(bound, now) || edited && !IdentifierComparer.Instance.Equals(now.Name, last))
                    {
                        return Conflict(before.SyntaxTree, start, edited ? newName : token.Text);
                    }
                }
            }

            return null;
        }
    }
}
