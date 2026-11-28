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
public sealed record RenameResult(bool Success, string? Error, ImmutableArray<DocumentEdit> Edits, FileRename? FileRename = null)
{
    /// <summary>
    /// CMP-1505: every name the rename would give another meaning — files in path order, then position; at most
    /// <see cref="RenameService.MaxConflicts"/>. With conflicts the rename is refused and <see cref="RenameResult.Error"/> is the
    /// first one's message; a refusal of another kind has none.
    /// </summary>
    public ImmutableArray<RenameConflict> Conflicts { get; init; } = [];
}

/// <summary>
/// CMP-1505: a name whose meaning the rename would change. <see cref="Location"/> is the name in the snapshot's text,
/// <see cref="Name"/> the name as written there — for a name the rename edits, the new name — and <see cref="Message"/> the
/// sentence <c>Renaming 'A' to 'B' changes the meaning of 'N' at File(line,col).</c>
/// </summary>
public sealed record RenameConflict(Location Location, string Name, string Message);

public sealed record FileRename(string OldPath, string NewPath);

/// <summary>
/// Rename (language-service.md §8, CMP-750…753, C-139). Pure: nothing is written; the caller applies the edits and the file
/// rename. The conflict check binds the edited texts in a forked compilation.
/// </summary>
public static class RenameService
{
    /// <summary>CMP-1505: the most conflicts a rename lists.</summary>
    public const int MaxConflicts = 100;

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

    /// <summary>CMP-1505: refused for its conflicts; the error is the first conflict's sentence.</summary>
    private static RenameResult Fail(ImmutableArray<RenameConflict> conflicts) => new(false, conflicts[0].Message, []) { Conflicts = conflicts };

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
        var conflicts = plan.Check(related, null, ct);
        if (!conflicts.IsEmpty)
        {
            return Fail(conflicts);
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

        var forms = new Forms(doc, formTexts, newName, edits);
        return forms.Rename(related, ct) is { } error ? Fail(error) : new RenameResult(true, null, Edits(edits));
    }

    private static SemanticModel ModelOf(Document doc, string path) =>
        PathUtil.AreEqual(path, doc.Path) ? doc.SemanticModel : doc.Compilation.GetSemanticModel(path);

    private static ImmutableArray<DocumentEdit> Edits(SortedDictionary<string, List<TextChange>> edits) =>
        [.. edits.Select(e => new DocumentEdit(e.Key, [.. e.Value.OrderBy(c => c.Span.Start)]))];

    /// <summary>
    /// The form edits of a rename (CMP-753, 1507, 1508). A form is the text given for its path (CMP-1502) or the file, read once per
    /// rename; a missing file is skipped (the build reports it, E3100), one that cannot be scanned fails the rename.
    /// </summary>
    private sealed class Forms(Document doc, Dictionary<string, string>? formTexts, string newName, SortedDictionary<string, List<TextChange>> edits)
    {
        private readonly Dictionary<string, (string Text, FormObject Root)?> _read = new(PathUtil.Comparer);
        private readonly HashSet<string> _unreadable = new(PathUtil.Comparer);

        private static string Unreadable(string path) => $"The form file '{Path.GetFileName(path)}' cannot be read, so its references cannot be updated.";

        /// <summary>The edits for everything renamed together; an error text when a form cannot be scanned.</summary>
        public string? Rename(List<Symbol> related, CancellationToken ct)
        {
            // CMP-753: a published field - the object of that name in the forms of its own class
            foreach (var field in related.OfType<FieldSymbol>().Where(f => f.Visibility == Visibility.Published && f.ContainingSymbol is ClassTypeSymbol))
            {
                if (FieldChanges(field) is { } error)
                {
                    return error;
                }
            }

            // CMP-753, 1508: a published method - the handler values in the forms of its class and of every class derived from it
            var methods = related.OfType<MethodSymbol>().Where(m => m.Visibility == Visibility.Published && m.ContainingSymbol is ClassTypeSymbol).ToList();
            if (methods.Count > 0 && HandlerChanges(methods, related, ct) is { } handlerError)
            {
                return handlerError;
            }

            // CMP-1507: a class - the class name of every object header that names it
            var types = related.OfType<ClassTypeSymbol>().ToList();
            return types.Count > 0 ? ClassChanges(types, ct) : null;
        }

        /// <summary>The paths of the forms a source unit embeds: its active <c>{$FORM}</c> directives, relative to the unit's folder.</summary>
        private IEnumerable<string> FormPaths(string unitPath)
        {
            foreach (var directive in doc.Compilation.GetSyntaxTree(unitPath).Directives.Where(d => d.IsActive && d.Name == "FORM"))
            {
                var name = DirectiveProcessor.Unquote(directive.Argument).Replace('/', '\\');
                if (name.Length > 0)
                {
                    yield return PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(unitPath)!, name));
                }
            }
        }

        /// <summary>The form's text and root; null for a missing file (<paramref name="error"/> null) or a form that cannot be read or scanned.</summary>
        private (string Text, FormObject Root)? Read(string path, out string? error)
        {
            if (!_read.TryGetValue(path, out var form))
            {
                string? text = null;
                if (formTexts is null || !formTexts.TryGetValue(path, out text))
                {
                    if (!doc.Compilation.FileSystem.FileExists(path))
                    {
                        _read[path] = null;
                        error = null;
                        return null;
                    }

                    try
                    {
                        text = FormFileScanner.Decode(doc.Compilation.FileSystem.ReadAllBytes(path));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        text = null;
                    }
                }

                form = text is not null && FormFileScanner.Scan(path, text).Root is { } root ? (text, root) : null;
                _read[path] = form;
                if (form is null)
                {
                    _unreadable.Add(path);
                }
            }

            error = _unreadable.Contains(path) ? Unreadable(path) : null;
            return form;
        }

        /// <summary>Adds the change of the name written at a 1-based position of a form; false when the text there is not that name.</summary>
        private bool Add(string path, string text, int line, int column, string written)
        {
            var starts = LineStarts(text);
            var at = line >= 1 && line <= starts.Count ? starts[line - 1] + column - 1 : -1;
            if (at < 0 || at + written.Length > text.Length || !string.Equals(text.Substring(at, written.Length), written, StringComparison.Ordinal))
            {
                return false;
            }

            if (!edits.TryGetValue(path, out var changes))
            {
                edits[path] = changes = [];
            }

            if (!changes.Any(c => c.Span.Start == at))
            {
                changes.Add(new TextChange(new TextSpan(at, written.Length), newName));
            }

            return true;
        }

        /// <summary>CMP-753: the object headers named like the field in the forms of its unit whose root class is the field's class.</summary>
        private string? FieldChanges(FieldSymbol field)
        {
            var owner = (ClassTypeSymbol)field.ContainingSymbol!;
            foreach (var path in FormPaths(PathUtil.Normalize(field.Locations[0].Path)))
            {
                if (Read(path, out var error) is not var (text, root))
                {
                    if (error is not null)
                    {
                        return error;
                    }

                    continue;
                }

                if (!IdentifierComparer.Instance.Equals(root.ClassName, owner.Name))
                {
                    continue;
                }

                foreach (var item in root.Descendants().Where(o => !o.IsOverride && IdentifierComparer.Instance.Equals(o.Name, field.Name)))
                {
                    if (!Add(path, text, item.Line, item.Column, item.Name))
                    {
                        return Unreadable(path);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// CMP-1508: in every form embedded by a source unit whose root class (a class the unit declares, as the binder finds it) is a
        /// method's class or derived from it, the handler values whose method - the first published method of that name along the root
        /// class and its ancestors, as the binder resolves it (LANG-671) - is renamed. A unit that declares no such class is not read.
        /// </summary>
        private string? HandlerChanges(List<MethodSymbol> methods, List<Symbol> related, CancellationToken ct)
        {
            var owners = methods.Select(m => (ClassTypeSymbol)m.ContainingSymbol!).ToList();
            bool Derived(ClassTypeSymbol c) => c.SelfAndAncestors().Any(a => owners.Any(o => SymbolKey.AreSame(a, o)));
            foreach (var unitPath in Paths(doc))
            {
                ct.ThrowIfCancellationRequested();
                var paths = FormPaths(unitPath).ToList();
                if (paths.Count == 0)
                {
                    continue;
                }

                var classes = DeclarationBinder.ClassesOf(ModelOf(doc, unitPath).Unit).Where(Derived).ToList();
                if (classes.Count == 0)
                {
                    continue;
                }

                foreach (var path in paths)
                {
                    if (Read(path, out var error) is not var (text, root))
                    {
                        if (error is not null)
                        {
                            return error;
                        }

                        continue;
                    }

                    if (classes.FirstOrDefault(c => IdentifierComparer.Instance.Equals(c.Name, root.ClassName)) is not { } rootClass)
                    {
                        continue;
                    }

                    foreach (var e in root.Descendants().Prepend(root).SelectMany(o => o.Events))
                    {
                        var handler = rootClass.SelfAndAncestors().SelectMany(c => c.Methods)
                            .FirstOrDefault(m => m.Visibility == Visibility.Published && IdentifierComparer.Instance.Equals(m.Name, e.Handler));
                        if (handler is not null && related.Any(r => SymbolKey.AreSame(r, handler)) && !Add(path, text, e.Line, e.Column, e.Handler))
                        {
                            return Unreadable(path);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// CMP-1507: in every form a source unit embeds, the class name of each object header (root, <c>object</c>, <c>inherited</c>,
        /// <c>inline</c>, override blocks) spelled like the class, when that name - looked up in the unit's scope, where the binder
        /// resolves it - is the renamed class. A unit is bound only when one of its forms spells the name.
        /// </summary>
        private string? ClassChanges(List<ClassTypeSymbol> types, CancellationToken ct)
        {
            var name = types[0].Name;
            foreach (var unitPath in Paths(doc))
            {
                ct.ThrowIfCancellationRequested();
                SemanticModel? model = null;
                foreach (var path in FormPaths(unitPath))
                {
                    if (Read(path, out var error) is not var (text, root))
                    {
                        if (error is not null)
                        {
                            return error;
                        }

                        continue;
                    }

                    foreach (var item in root.Descendants().Prepend(root).Where(o => o.ClassLine > 0 && IdentifierComparer.Instance.Equals(o.ClassName, name)))
                    {
                        model ??= ModelOf(doc, unitPath);
                        if (model.LookupInUnitScope(item.ClassName) is ClassTypeSymbol found && types.Any(t => SymbolKey.AreSame(t, found))
                            && !Add(path, text, item.ClassLine, item.ClassColumn, item.ClassName))
                        {
                            return Unreadable(path);
                        }
                    }
                }
            }

            return null;
        }
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
        var conflicts = plan.Check([unit], names, ct);
        return conflicts.IsEmpty ? new RenameResult(true, null, Edits(edits), rename) : Fail(conflicts);
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
    /// identifier of the edited files that was bound must still bind to the same declaration. CMP-1506: so must, in every other
    /// source unit, each name spelled like the new name. CMP-1505: every conflict is listed.
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

        /// <summary>CMP-1505: the conflict of the name at <paramref name="span"/> of the snapshot's text; <paramref name="text"/> is the name as written after the rename.</summary>
        private RenameConflict Conflict(SyntaxTree tree, TextSpan span, string text)
        {
            var at = tree.Text.Lines.GetLinePosition(span.Start);
            return new RenameConflict(NavigationService.LocationOf(tree, span), text,
                $"Renaming '{oldName}' to '{newName}' changes the meaning of '{text}' at {Path.GetFileName(tree.Text.Path)}({at.Line + 1},{at.Column + 1}).");
        }

        /// <summary>
        /// The conflicts (files in path order, then text order; at most <see cref="MaxConflicts"/>). <paramref name="unitNames"/>: the
        /// name spans of a unit rename. CMP-1506: besides the edited files, every source unit whose own text spells the new name is
        /// checked — only such a name can be captured or hidden by the renamed declaration in a file the rename does not edit.
        /// </summary>
        public ImmutableArray<RenameConflict> Check(IReadOnlyList<Symbol> renamed, Dictionary<string, List<TextSpan>>? unitNames, CancellationToken ct)
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
            var conflicts = ImmutableArray.CreateBuilder<RenameConflict>();
            foreach (var path in edits.Keys.Concat(Paths(doc)).Distinct(PathUtil.Comparer).Order(PathUtil.Comparer))
            {
                if (conflicts.Count >= MaxConflicts)
                {
                    break;
                }

                ct.ThrowIfCancellationRequested();
                var before = ModelOf(doc, path);
                if (edits.TryGetValue(path, out var changes))
                {
                    CheckEdited(path, changes, before, fork, renamed, unitNames, last, conflicts, ct);
                }
                else
                {
                    CheckUnedited(path, before, fork, last, conflicts, ct);
                }
            }

            return conflicts.ToImmutable();
        }

        /// <summary>CMP-751: every edited name binds to the renamed symbol, every other name of the file that was bound to the same declaration.</summary>
        private void CheckEdited(string path, List<TextChange> changes, SemanticModel before, Compilation fork, IReadOnlyList<Symbol> renamed,
            Dictionary<string, List<TextSpan>>? unitNames, string last, ImmutableArray<RenameConflict>.Builder conflicts, CancellationToken ct)
        {
            var after = fork.GetSemanticModel(NewPath(path));
            var newTree = after.SyntaxTree;
            var names = unitNames is not null && unitNames.TryGetValue(path, out var spans) ? spans : [];
            var expected = unitNames is null ? null : UnitNames(after, after.Compilation.GetSemanticModel(NewPath(PathUtil.Normalize(renamed[0].Locations[0].Path))).Unit, ct);
            foreach (var token in before.SyntaxTree.Root.OwnTokens())
            {
                if (conflicts.Count >= MaxConflicts)
                {
                    return;
                }

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
                        conflicts.Add(Conflict(before.SyntaxTree, unitName, newName));
                    }

                    continue;
                }

                var edit = changes.FirstOrDefault(c => c.Span.Contains(start));
                if (edit.Span.Length > 0 && edit.Span.Start != start)
                {
                    continue;                                               // inside an edited span that is not a name (none today)
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
                    conflicts.Add(Conflict(before.SyntaxTree, token.Span, edited ? newName : token.Text));
                }
            }
        }

        /// <summary>
        /// CMP-1506: a file the rename does not edit. Its own name tokens spelled like the new name (a dotted unit name: its last
        /// part) that were bound must bind to the same declaration; a file that does not spell the name is not bound again.
        /// </summary>
        private void CheckUnedited(string path, SemanticModel before, Compilation fork, string last, ImmutableArray<RenameConflict>.Builder conflicts, CancellationToken ct)
        {
            SemanticModel? after = null;
            foreach (var token in before.SyntaxTree.Root.OwnTokens())
            {
                if (conflicts.Count >= MaxConflicts)
                {
                    return;
                }

                if (!SyntaxFacts.IsNameToken(token) || token.IsMissing || !IdentifierComparer.Instance.Equals(SyntaxFacts.NameOf(token), last))
                {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                if (QuickInfoService.SymbolOf(before, token) is not { } bound)
                {
                    continue;
                }

                after ??= fork.GetSemanticModel(path);
                var newToken = after.SyntaxTree.FindToken(token.Span.Start);
                var now = SyntaxFacts.IsNameToken(newToken) && newToken.Span == token.Span ? QuickInfoService.SymbolOf(after, newToken) : null;
                if (now is null || !Same(bound, now))
                {
                    conflicts.Add(Conflict(before.SyntaxTree, token.Span, token.Text));
                }
            }
        }
    }
}
