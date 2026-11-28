using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;
using CompilerPaths = FreexComp.Compiler.Text.PathUtil;
using LsNavigation = FreexComp.LanguageService.NavigationService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// A reference row of a References result set (IDE-FREF-004): the place as a <see cref="FileMatch"/> (the identifier's
/// span and its line), the reference kind, the container it lies in (<paramref name="ContainerIndex"/> = the 1-based
/// position of that container among the containers of its file) and whether the file is a library source.
/// </summary>
public sealed record ReferenceMatch(string Path, int Line, int Column, int Length, string LineText, ReferenceKind Kind,
    string Container, int ContainerIndex, bool IsLibrary) : FileMatch(Path, Line, Column, Length, LineText);

/// <summary>Where a reference query was started: the file and the identifier's 1-based line and column at that time (IDE-FREF-008).</summary>
public sealed record ReferenceOrigin(string Path, int Line, int Column);

/// <summary>
/// The rows of one reference query in display order (IDE-FREF-005), at most <see cref="FindInFilesService.MatchCap"/>
/// (IDE-FREF-006). <paramref name="Identifier"/> is the name as written where the query started.
/// </summary>
public sealed record ReferencesResult(string Identifier, IReadOnlyList<ReferenceMatch> Matches, bool IsCapped);

/// <summary>
/// IDE-FREF-001…008 <c>search.findReferences</c> (Shift+F12; I-353, F13-016a): asks the language service for every
/// reference to the symbol of the identifier at the caret (<c>NavigationService.FindReferences</c> over the project
/// <c>Compilation</c>, unsaved buffers included), builds the rows on the code-model worker and hands a non-empty result
/// to the Search Results window as a result set of kind References. One query at a time: a newer one and
/// <c>searchResults.cancel</c> cancel it. UI thread only.
/// </summary>
public sealed class FindReferencesService
{
    public const string CommandId = "search.findReferences";

    /// <summary>IDE-FREF-002: the status bar names a query that runs longer than this.</summary>
    public static readonly TimeSpan SearchingDelay = TimeSpan.FromMilliseconds(400);

    private const string RequestKind = "findReferences";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly CodeModelService _codeModel;
    private readonly ILibrarySourceService _library;
    private readonly IStatusService _status;
    private readonly IClock _clock;
    private readonly SearchResultsViewModel _results;
    private CancellationTokenSource? _cancel;
    private ICommandService? _commands;

    public FindReferencesService(IDocumentService documents, IProjectService projects, CodeModelService codeModel, ILibrarySourceService library,
        IStatusService status, IClock clock, SearchResultsViewModel results)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(results);
        _documents = documents;
        _projects = projects;
        _codeModel = codeModel;
        _library = library;
        _status = status;
        _clock = clock;
        _results = results;
        results.References = this;
        projects.ProjectChanged += (_, _) => Cancel(); // IDE-ARCH-084: closing the project cancels its project-wide query
    }

    /// <summary>A query runs.</summary>
    public bool IsSearching => _cancel is not null;

    /// <summary>The result set the running query repeats (<c>searchResults.repeat</c>); null for a new query and when none runs.</summary>
    public object? RepeatTarget { get; private set; }

    /// <summary>The query started last (tests await it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler(CommandId, new Handler(this));
    }

    /// <summary>IDE-FREF-002: cancels the running query; it changes nothing and the status bar says so.</summary>
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Another search takes the place of the running query: it ends without a result and without a message.</summary>
    internal void Abandon()
    {
        var cancel = _cancel;
        _cancel = null;
        RepeatTarget = null;
        cancel?.Cancel();
    }

    /// <summary>IDE-FREF-001: the condition of <c>search.goToDeclaration</c>.</summary>
    private bool CanFind => _documents.ActiveDocument?.VisibleText is { FilePath: not null } d && d.Language.Id == "pascal" && _projects.Project is not null;

    private Task FindFromCaretAsync()
    {
        if (!CanFind || _documents.ActiveDocument?.VisibleText is not { FilePath: { } path } document || _codeModel.Synchronize() is null)
            return Task.CompletedTask;
        var source = _codeModel.Workspace.GetDocument(path);
        if (IdentifierAt(source.SyntaxTree, document.CaretOffset, orEndingThere: true) is not { } identifier)
        {
            _status.ShowMessage(Strings.Msg_Editor_NotAnIdentifier, StatusDuration);
            return Task.CompletedTask;
        }

        // Started, not awaited: the command finishes, a newer query or searchResults.cancel may cancel this one.
        Running = RunAsync(source, identifier, repeatOf: null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// IDE-FREF-008: runs the query of a References result set again where it was started —
    /// <paramref name="offset"/> is the tracked offset of the identifier while its document is open, else the stored line
    /// and column of <paramref name="origin"/> are used. <paramref name="gone"/>: the tracked place was deleted. Without
    /// an identifier there the status bar says so and the rows stay.
    /// </summary>
    internal Task RepeatAsync(ReferenceOrigin origin, int? offset, bool gone, object target)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!gone && _codeModel.Synchronize() is not null)
        {
            var source = _codeModel.Workspace.GetDocument(origin.Path);
            var at = offset ?? OffsetOf(source.SyntaxTree.Text.Text, origin.Line, origin.Column);
            if (at >= 0 && IdentifierAt(source.SyntaxTree, at, orEndingThere: false) is { } identifier)
                return Running = RunAsync(source, identifier, target);
        }

        // No identifier there any more (or no project to ask): the rows stay.
        _status.ShowMessage(Strings.Msg_References_SymbolGone, StatusDuration);
        return Task.CompletedTask;
    }

    private async Task RunAsync(Document source, SyntaxToken identifier, object? repeatOf)
    {
        _cancel?.Cancel(); // IDE-ARCH-084: the newer query replaces the older one
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        RepeatTarget = repeatOf;
        var name = identifier.Text;
        IDisposable? sticky = null;
        var timer = _clock.Schedule(SearchingDelay, () => sticky = _status.ShowStickyMessage(Format(Strings.Msg_References_Searching, name)));
        _results.OnReferenceSearchChanged();

        // UI thread: what the rows need from the project (IDE-FREF-005); the query and the rows are made on the worker.
        var members = MemberOrder();
        var roots = _library.Roots;
        var start = identifier.Span.Start;
        ReferencesResult? result = null;
        var canceled = false;
        try
        {
            result = await _codeModel.Worker.RunAsync(RequestKind,
                ct => ReferenceRows.Build(name, LsNavigation.FindReferences(source, start, ct), source.Compilation, members, roots, ct),
                cancel.Token).WaitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch
        {
            // The query failed: it is over (the failure of a task nobody awaits is logged, IDE-ARCH-091).
            if (ReferenceEquals(_cancel, cancel))
            {
                _cancel = null;
                RepeatTarget = null;
                _results.OnReferenceSearchChanged();
            }

            throw;
        }
        finally
        {
            timer.Dispose();
            sticky?.Dispose();
        }

        if (!ReferenceEquals(_cancel, cancel))
            return; // replaced by a newer search, which reports
        _cancel = null;
        RepeatTarget = null;
        if (canceled)
        {
            _status.ShowMessage(Strings.Msg_References_Canceled, StatusDuration);
        }
        else if (result is null)
        {
            _status.ShowMessage(Format(Strings.Msg_References_None, name), StatusDuration); // IDE-FREF-002: the window is not changed
        }
        else
        {
            var position = source.SyntaxTree.Text.Lines.GetLinePosition(start);
            if (_results.ShowReferences(result, new ReferenceOrigin(source.Path, position.Line + 1, position.Column + 1), start, repeatOf))
                _status.ShowMessage(CountText(result.Matches.Count, result.Matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                    StatusDuration);
        }

        _results.OnReferenceSearchChanged();
        _commands?.InvalidateCanExecute();
    }

    /// <summary><c>Msg.References.DoneOne</c> / <c>DoneOneFile</c> / <c>DoneMany</c>: the status text and the count part of the header (UI-TW-611).</summary>
    internal static string CountText(int references, int files) =>
        references == 1 ? Strings.Msg_References_DoneOne
        : files == 1 ? Format(Strings.Msg_References_DoneOneFile, references)
        : Format(Strings.Msg_References_DoneMany, references, files);

    /// <summary>IDE-FREF-005: the project's files (main source first) with their position, by full path.</summary>
    private Dictionary<string, int> MemberOrder()
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (_projects.Project is not { } project)
            return order;
        foreach (var file in new[] { project.MainSource }.Concat(project.Files.Select(f => f.Path)).Where(p => p.Length > 0))
            order.TryAdd(CompilerPaths.Normalize(Path.GetFullPath(Path.Combine(project.Directory, file.Replace('/', '\\')))), order.Count);
        return order;
    }

    /// <summary>
    /// The identifier token at <paramref name="offset"/>; with <paramref name="orEndingThere"/> also the one that ends
    /// there (the caret rule of IDE-ED-101).
    /// </summary>
    private static SyntaxToken? IdentifierAt(SyntaxTree tree, int offset, bool orEndingThere)
    {
        foreach (var at in orEndingThere ? (int[])[offset, offset - 1] : [offset])
        {
            if (at < 0)
                continue;
            var token = tree.FindToken(at);
            if (token.Kind == SyntaxKind.IdentifierToken && !token.IsMissing && token.Span.Start <= at && at < token.Span.End)
                return token;
        }

        return null;
    }

    /// <summary>The offset of the 1-based <paramref name="line"/> and <paramref name="column"/> in <paramref name="text"/>; -1 beyond its lines.</summary>
    private static int OffsetOf(string text, int line, int column)
    {
        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
                return -1;
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }

        var lineEnd = text.IndexOfAny(['\r', '\n'], start);
        var length = (lineEnd < 0 ? text.Length : lineEnd) - start;
        return column >= 1 && column <= length ? start + column - 1 : -1;
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private sealed class Handler(FindReferencesService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.CanFind;

        public Task ExecuteAsync(string? argument) => owner.FindFromCaretAsync();
    }
}

/// <summary>
/// IDE-FREF-004…006: the rows of a reference query — per file the references by position, each with its container, the
/// files in the order project members, other files, library sources. Pure; runs on the code-model worker.
/// </summary>
internal static class ReferenceRows
{
    /// <summary>Null when the query found nothing (the identifier binds to no symbol).</summary>
    public static ReferencesResult? Build(string identifier, ImmutableArray<ReferenceLocation> references, FreexComp.Compiler.Compilation compilation,
        IReadOnlyDictionary<string, int> memberOrder, IReadOnlyList<string> libraryRoots, CancellationToken ct)
    {
        if (references.IsEmpty)
            return null;
        var files = references.GroupBy(r => r.Location.Path, CompilerPaths.Comparer)
            .Select(g =>
            {
                var member = memberOrder.TryGetValue(CompilerPaths.Normalize(g.Key), out var position) ? position : -1;
                var library = member < 0 && LibrarySourceService.IsUnder(libraryRoots, g.Key);
                return (Path: g.Key, Rank: member >= 0 ? 0 : library ? 2 : 1, Member: member, Library: library, References: g);
            })
            .OrderBy(f => f.Rank).ThenBy(f => f.Member).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase);

        var matches = new List<ReferenceMatch>();
        var total = references.Length;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (matches.Count >= FindInFilesService.MatchCap)
                break;
            var tree = compilation.GetSyntaxTree(file.Path);
            var text = tree.Text.Text;
            var symbols = LsNavigation.GetDocumentSymbols(tree);

            // Containers by the position of their first reference, references by position.
            var containers = new List<(int Id, string Name, List<ReferenceLocation> References)>();
            foreach (var reference in file.References.OrderBy(r => r.Location.Span.Start))
            {
                var (id, name) = ContainerOf(tree, symbols, reference.Location.Span.Start);
                var index = containers.FindIndex(c => c.Id == id);
                if (index < 0)
                {
                    containers.Add((id, name, []));
                    index = containers.Count - 1;
                }

                containers[index].References.Add(reference);
            }

            for (var c = 0; c < containers.Count; c++)
            {
                foreach (var reference in containers[c].References)
                {
                    if (matches.Count >= FindInFilesService.MatchCap)
                        break;
                    var location = reference.Location;
                    matches.Add(new ReferenceMatch(file.Path, location.LineSpan.Start.Line + 1, location.LineSpan.Start.Column + 1, location.Span.Length,
                        LineAt(text, location.Span.Start), reference.Kind, containers[c].Name, c + 1, file.Library));
                }
            }
        }

        return new ReferencesResult(identifier, matches, total > matches.Count);
    }

    /// <summary>
    /// IDE-FREF-004: the innermost routine with a body around <paramref name="offset"/> (a nested routine: the names from
    /// the outermost routine joined by <c>.</c>), else the innermost type declaration, else the unit level. A heading
    /// without a body — a method inside its type, a routine of the interface section, a forward declaration — is no
    /// container: it belongs to what it is declared in. The id tells two containers of one name apart: the start of the
    /// container's declaration, -1 for the unit level.
    /// </summary>
    private static (int Id, string Name) ContainerOf(SyntaxTree tree, ImmutableArray<DocumentSymbol> symbols, int offset)
    {
        DocumentSymbol? type = null;
        DocumentSymbol? routine = null;
        var names = new List<string>();
        var level = symbols;
        while (true)
        {
            DocumentSymbol? inside = null;
            foreach (var symbol in level)
            {
                if (symbol.Span.Start <= offset && offset < symbol.Span.End)
                {
                    inside = symbol;
                    break;
                }
            }

            if (inside is null)
                break;
            if (inside.Kind is CompletionItemKind.Class or CompletionItemKind.Interface or CompletionItemKind.Type or CompletionItemKind.Enum)
            {
                type = inside;
            }
            else if (inside.Kind is CompletionItemKind.Method or CompletionItemKind.Routine && HasBody(tree, inside))
            {
                routine = inside;
                names.Add(inside.Name);
            }

            level = inside.Children;
        }

        if (routine is not null)
            return (routine.Span.Start, string.Join(".", names));
        return type is not null ? (type.Span.Start, type.Name) : (-1, Strings.Msg_References_UnitLevel);
    }

    /// <summary>The routine declaration of the symbol has a block (it is an implementation, not only a heading).</summary>
    private static bool HasBody(SyntaxTree tree, DocumentSymbol routine) =>
        tree.FindToken(routine.SelectionSpan.Start).Parent?.FirstAncestorOrSelf<RoutineDeclarationSyntax>() is { Block: not null };

    /// <summary>The line of <paramref name="text"/> that holds <paramref name="offset"/>, without its line break.</summary>
    private static string LineAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset == 0 ? 0 : text.LastIndexOfAny(['\r', '\n'], offset - 1) + 1;
        var end = text.IndexOfAny(['\r', '\n'], offset);
        return text[start..(end < 0 ? text.Length : end)];
    }
}
