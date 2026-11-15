using System.Globalization;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Popups;

/// <summary>
/// Go to File (dialogs.md §13, UI-DLG-131/132): the open project's files and the open documents, de-duplicated; empty input
/// lists the open documents most recently used first, then the project files in project order. Typed text filters by
/// subsequence on the file name; a trailing <c>:&lt;line&gt;</c> moves the caret to that line after opening.
/// </summary>
public sealed class GoToFileViewModel : QuickPickViewModel
{
    private readonly IDocumentService _documents;
    private readonly NavigationService _navigation;
    private readonly ILayoutService _layout;
    private readonly IDialogService _dialogs;
    private readonly List<(string Path, string Key)> _candidates = [];
    private readonly Dictionary<string, int> _projectOrder = new(StringComparer.OrdinalIgnoreCase);

    public GoToFileViewModel(IDocumentService documents, IProjectService projects, NavigationService navigation, ILayoutService layout, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(projects);
        _documents = documents;
        _navigation = navigation;
        _layout = layout;
        _dialogs = dialogs;
        var project = projects.Project;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            if (seen.Add(path))
                _candidates.Add((path, KeyOf(path, project?.Directory)));
        }

        foreach (var document in documents.MostRecentlyUsed)
        {
            if (document.FilePath is { } path)
                Add(path);
        }

        if (project is not null)
        {
            foreach (var file in new[] { project.MainSource }.Concat(project.Files.Select(f => f.Path)).Where(p => p.Length > 0))
            {
                var full = Path.GetFullPath(Path.Combine(project.Directory, file.Replace('/', '\\')));
                _projectOrder.TryAdd(full, _projectOrder.Count);
                Add(full);
            }
        }

        Refresh();
    }

    public override string Name => "GoToFile";

    public override string AccessibleName => Strings.Cmd_search_goToFile;

    public override (string Text, string Detail) CellNames => ("Name", "Directory");

    public override bool ShowsFileIcon => true;

    /// <summary>The <c>:&lt;line&gt;</c> suffix of the input, or null.</summary>
    public int? Line => SplitLine(Input.Trim()).Line;

    protected override string FilterText => SplitLine(Input.Trim()).Filter;

    public override async Task CommitAsync()
    {
        if (SelectedItem is not { IsEnabled: true, Tag: string path })
            return;
        var line = Line;
        Close();
        var opened = await _documents.OpenAsync(path);
        if (opened.Value is not TextDocumentViewModel document)
        {
            if (opened.NeedsMessage)
                await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
            return;
        }

        if (line is { } l)
            _navigation.GoToLine(document, Math.Clamp(l, 1, document.Buffer.LineCount), null);
        _layout.Layout.FocusPane(Models.Pane.Documents);
    }

    protected override IReadOnlyList<QuickPickItem> Filter(string filter)
    {
        if (filter.Length == 0)
            return [.. _candidates.Select(c => Item(c, []))];
        return [.. _candidates
            .Select((c, order) => (c, order, match: FuzzyMatch.Match(Path.GetFileName(c.Path), filter, FuzzyMatch.IsFileNameWordStart)))
            .Where(x => x.match is not null)
            .OrderBy(x => x.match!.Value.Tier).ThenBy(x => Path.GetFileName(x.c.Path).Length).ThenBy(x => _projectOrder.GetValueOrDefault(x.c.Path, int.MaxValue)).ThenBy(x => x.order)
            .Select(x => Item(x.c, x.match!.Value.Positions))];
    }

    protected override string NoMatch(string filter) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Nav_FileNotInProject, Input.Trim());

    private static QuickPickItem Item((string Path, string Key) candidate, IReadOnlyList<int> highlights) =>
        new(candidate.Key, Path.GetFileName(candidate.Path), Path.GetDirectoryName(candidate.Path) ?? "", highlights, true, candidate.Path);

    private static string KeyOf(string path, string? root)
    {
        if (root is not null)
        {
            var relative = Path.GetRelativePath(root, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative))
                return relative.Replace('\\', '/');
        }

        return path;
    }

    private static (string Filter, int? Line) SplitLine(string input)
    {
        var colon = input.LastIndexOf(':');
        if (colon > 0 && colon < input.Length - 1 && input[(colon + 1)..].All(char.IsAsciiDigit)
            && int.TryParse(input[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
            return (input[..colon], line);
        return (input, null);
    }
}
