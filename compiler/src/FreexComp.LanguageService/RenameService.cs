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

/// <summary>What is under the caret for a rename: <see cref="Span"/> is the whole name (a dotted unit name is one name).</summary>
public sealed record RenameInfo(bool CanRename, string? Reason, TextSpan Span, string CurrentName);

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
        var target = Find(doc, position, ct);
        return new RenameInfo(target.Reason is null, target.Reason, target.Span, target.Name);
    }

    /// <summary>CMP-751…753: the edits that rename the symbol at <paramref name="position"/>, after the conflict check.</summary>
    public static RenameResult Rename(Document doc, int position, string newName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(newName);
        var target = Find(doc, position, ct);
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

        return unit is null ? RenameSymbol(doc, target, newName, ct) : RenameUnit(doc, target, unit, newName, ct);
    }

    private static RenameResult Fail(string error) => new(false, error, []);

    // ---------------------------------------------------------------------------------------------------------- the target

    private sealed record Target(TextSpan Span, string Name, Symbol? Symbol, ImmutableArray<Symbol> Related, string? Reason);

    private static Target Find(Document doc, int position, CancellationToken ct)
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

        var related = Closure(doc.Compilation, symbol);
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
    /// method that implements an interface method goes with it, and so do the other implementations.
    /// </summary>
    private static ImmutableArray<Symbol> Closure(Compilation compilation, Symbol symbol)
    {
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

    // ---------------------------------------------------------------------------------------------------------- symbols

    private static RenameResult RenameSymbol(Document doc, Target target, string newName, CancellationToken ct)
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

        // CMP-753: the companion .form of a form class's published field or method
        foreach (var member in related.Where(r => r is FieldSymbol or MethodSymbol && r.Visibility == Visibility.Published && r.ContainingSymbol is ClassTypeSymbol))
        {
            if (FormChanges(compilation, member, newName, edits) is { } error)
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
    /// form cannot be scanned.
    /// </summary>
    private static string? FormChanges(Compilation compilation, Symbol member, string newName, SortedDictionary<string, List<TextChange>> edits)
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
            if (!compilation.FileSystem.FileExists(path))
            {
                continue;                                                   // the build reports the missing file (E3100)
            }

            var unreadable = $"The form file '{Path.GetFileName(path)}' cannot be read, so its references cannot be updated.";
            string? text;
            try
            {
                text = FormFileScanner.Decode(compilation.FileSystem.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return unreadable;
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
                return IdentifierComparer.Instance.Equals(before.QualifiedName, after.QualifiedName);
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
