using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>A unit of the project with a <c>form</c> entry (absolute paths): a possible frame (DSG-FD-103).</summary>
public sealed record FrameSource(string UnitPath, string FormPath);

/// <summary>A frame unit open in the IDE: its unit text and the designer model of its form (DSG-FD-103).</summary>
public sealed record OpenFrameUnit(string UnitText, FormDocument Model);

/// <summary>
/// The frames of the loaded project (frames-designer.md §2, DSG-FD-103…105): the units with a <c>form</c> entry whose
/// form-root class is declared in the unit with the direct ancestor <c>TFrame</c>. A definition's document is the open
/// designer model when the unit is open in the IDE, else the form parsed from disk with this catalog (nested frames
/// resolve). A unit whose source or form cannot be read is left out with one line on the Output channel
/// <c>designer</c>. <see cref="Refresh"/> rebuilds the list and raises <see cref="Changed"/> for the classes added,
/// changed or removed — nothing when nothing differs; an applied change of a definition's document raises it for that
/// class (DSG-FD-104). Group members, other projects and libraries are not listed (DSG-FD-105). No Avalonia types.
/// </summary>
public sealed class ProjectFrameCatalog : IFrameCatalog
{
    /// <summary>The direct ancestor a frame unit's class declares (DSG-FD-103; visual inheritance is F05-007).</summary>
    public const string FrameAncestor = "TFrame";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly IFileSystem _fileSystem;
    private readonly Func<IReadOnlyList<FrameSource>> _sources;
    private readonly Func<ComponentCatalog> _catalog;
    private readonly Func<string, OpenFrameUnit?> _open;
    private readonly Action<string>? _report;
    private readonly HashSet<string> _raising = new(Names);
    private SortedDictionary<string, Entry> _entries = new(Names);
    private Dictionary<string, Candidate>? _pending;
    private Dictionary<string, string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private List<FrameDefinition>? _all;

    /// <param name="sources">The units of the loaded project that have a <c>form</c> entry (none without a project).</param>
    /// <param name="catalog">The component metadata the frames are read with.</param>
    /// <param name="open">The open frame unit of a unit path, or null when it is not open.</param>
    /// <param name="report">Writes one line on the Output channel <c>designer</c>.</param>
    public ProjectFrameCatalog(IFileSystem fileSystem, Func<IReadOnlyList<FrameSource>> sources, Func<ComponentCatalog> catalog,
        Func<string, OpenFrameUnit?>? open = null, Action<string>? report = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _open = open ?? (_ => null);
        _report = report;
    }

    public IReadOnlyList<FrameDefinition> All => _all ??= [.. _entries.Values.Select(e => e.Definition)];

    public event EventHandler<FrameCatalogChangedEventArgs>? Changed;

    public FrameDefinition? Find(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (_pending is not null && _pending.TryGetValue(className, out var pending))
        {
            return Resolve(pending); // during a refresh: a nested frame is read on demand
        }

        return _entries.GetValueOrDefault(className)?.Definition;
    }

    /// <summary>The unit file of <paramref name="className"/>'s frame (absolute), or null.</summary>
    public string? UnitPathOf(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        return _entries.GetValueOrDefault(className)?.UnitPath;
    }

    /// <summary>
    /// DSG-FD-104: reads the project's frame units again (open ones from the IDE, the others from disk; an unchanged unit
    /// keeps its definition) and raises <see cref="Changed"/> once with the classes added, changed or removed.
    /// </summary>
    public void Refresh()
    {
        if (_pending is not null)
        {
            return; // a refresh caused by this one's own reading
        }

        var catalog = _catalog();
        var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, Candidate>(Names);
        foreach (var source in _sources())
        {
            if (Read(source, catalog, problems) is { } candidate && !candidates.ContainsKey(candidate.ClassName))
            {
                candidates[candidate.ClassName] = candidate;
            }
        }

        var next = new SortedDictionary<string, Entry>(Names);
        _pending = candidates;
        try
        {
            foreach (var candidate in candidates.Values)
            {
                if (Resolve(candidate) is not null)
                {
                    next[candidate.ClassName] = candidate.Entry!;
                }
                else if (candidate.Problem is { } problem)
                {
                    problems[candidate.Source.UnitPath] = problem;
                }
            }
        }
        finally
        {
            _pending = null;
        }

        Report(problems);
        var changed = _entries.Keys.Where(k => !next.ContainsKey(k))
            .Concat(next.Where(e => !_entries.TryGetValue(e.Key, out var old) || !ReferenceEquals(old.Definition, e.Value.Definition)).Select(e => e.Key))
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        foreach (var old in _entries.Values.Where(o => !next.Values.Any(n => ReferenceEquals(n.Definition.Document, o.Definition.Document))))
        {
            old.Definition.Document.Changed -= OnDocumentChanged;
        }

        foreach (var added in next.Values.Where(n => !_entries.Values.Any(o => ReferenceEquals(n.Definition.Document, o.Definition.Document))))
        {
            added.Definition.Document.Changed += OnDocumentChanged;
        }

        _entries = next;
        _all = null;
        Raise(changed);
    }

    /// <summary>A unit and its form as read now; null when it is no frame or cannot be read (then in <paramref name="problems"/>).</summary>
    private Candidate? Read(FrameSource source, ComponentCatalog catalog, Dictionary<string, string> problems)
    {
        string unitText;
        string? formText = null;
        string rootClass;
        var open = _open(source.UnitPath);
        try
        {
            if (open is not null)
            {
                unitText = open.UnitText;
                rootClass = open.Model.Root.TypeName;
            }
            else
            {
                unitText = ReadText(source.UnitPath);
                formText = ReadText(source.FormPath);
                var first = FormParser.Parse(formText, source.FormPath, catalog);
                if (first.Document is null)
                {
                    problems[source.UnitPath] = first.Diagnostics[0].Message; // a syntax error: no root class
                    return null;
                }

                rootClass = first.Document.Root.TypeName;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            problems[source.UnitPath] = e.Message;
            return null;
        }

        // DSG-FD-103: the class the code sync reads for rootAncestorClass (a syntax tree, never text matching).
        var ancestor = FormClassLocator.Locate(unitText, rootClass, source.UnitPath).FormClass?.AncestorName;
        if (!Names.Equals(ancestor, FrameAncestor))
        {
            return null;
        }

        return new Candidate(source, rootClass, new Stamp(catalog, open?.Model, open is null ? unitText : null, formText), open?.Model, formText);
    }

    private string ReadText(string path)
    {
        var detection = EncodingDetector.Detect(_fileSystem.ReadAllBytes(path));
        return detection.IsBinary ? throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Binary, path)) : detection.Text;
    }

    /// <summary>The candidate's definition: the previous one when nothing changed, else built now (null: not readable, or a cycle).</summary>
    private FrameDefinition? Resolve(Candidate candidate)
    {
        if (candidate.Done)
        {
            return candidate.Entry?.Definition;
        }

        if (candidate.Busy)
        {
            return null; // the frame contains itself: its instance stays preserved (F0111 / F0101)
        }

        candidate.Busy = true;
        try
        {
            var unitName = Path.GetFileNameWithoutExtension(candidate.Source.UnitPath);
            if (_entries.TryGetValue(candidate.ClassName, out var old) && old.Stamp == candidate.Stamp
                && string.Equals(old.UnitPath, candidate.Source.UnitPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(old.Definition.FormPath, candidate.Source.FormPath, StringComparison.OrdinalIgnoreCase))
            {
                candidate.Entry = old;
            }
            else if (candidate.Model is { } model)
            {
                candidate.Entry = new Entry(new FrameDefinition(candidate.ClassName, unitName, candidate.Source.FormPath, model),
                    candidate.Source.UnitPath, candidate.Stamp);
            }
            else
            {
                var parsed = FormParser.Parse(candidate.FormText!, candidate.Source.FormPath, candidate.Stamp.Catalog, FrameAncestor, this);
                if (parsed.Document is { } document)
                {
                    candidate.Entry = new Entry(new FrameDefinition(candidate.ClassName, unitName, candidate.Source.FormPath, document),
                        candidate.Source.UnitPath, candidate.Stamp);
                }
                else
                {
                    candidate.Problem = parsed.Diagnostics.FirstOrDefault()?.Message;
                }
            }

            return candidate.Entry?.Definition;
        }
        finally
        {
            candidate.Busy = false;
            candidate.Done = true;
        }
    }

    /// <summary>DSG-FD-103: one Output line per unit and reason (again only after the unit was readable in between).</summary>
    private void Report(Dictionary<string, string> problems)
    {
        foreach (var (unit, reason) in problems)
        {
            if (!_reported.TryGetValue(unit, out var shown) || shown != reason)
            {
                _report?.Invoke(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FrameUnreadable, unit, reason));
            }
        }

        _reported = problems;
    }

    private void OnDocumentChanged(object? sender, FormChangedEventArgs e)
    {
        if (sender is FormDocument document && _entries.Values.FirstOrDefault(v => ReferenceEquals(v.Definition.Document, document)) is { } entry)
        {
            Raise([entry.Definition.ClassName]);
        }
    }

    /// <summary>While <see cref="Changed"/> is raised for a class, further changes of it raise nothing (frames notifying each other).</summary>
    private void Raise(IReadOnlyList<string> classNames)
    {
        var fresh = classNames.Where(_raising.Add).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, new FrameCatalogChangedEventArgs(fresh));
        }
        finally
        {
            foreach (var name in fresh)
            {
                _raising.Remove(name);
            }
        }
    }

    /// <summary>
    /// The project catalog of the IDE (DSG-FD-103, 104): the open project's units with a form, the designer models of the
    /// open form documents, the Output channel <c>designer</c>; refreshed when the project opens, closes or changes
    /// (add, remove, rename), when files are added or renamed, when a document is opened, closed or saved, when an open
    /// document's model is replaced, and when the metadata is reloaded. Window activation calls <see cref="Refresh"/>
    /// through the main window (files changed on disk).
    /// </summary>
    public static ProjectFrameCatalog ForIde(IProjectService projects, DocumentService documents, IFileSystem fileSystem, IOutputService? output)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(documents);
        var frames = new ProjectFrameCatalog(fileSystem, () => Sources(projects), () => documents.DesignCatalog,
            unit => documents.Documents.OfType<FormDocumentViewModel>().FirstOrDefault(d =>
                d.HasUnit && d.Designer.Model is not null && string.Equals(d.FilePath, unit, StringComparison.OrdinalIgnoreCase)) is { } open
                ? new OpenFrameUnit(open.Buffer.Text, open.Designer.Model!)
                : null,
            output is null ? null : line => output.Append(OutputChannels.Designer, line, OutputKind.Error));
        documents.DesignFrames = frames;
        projects.ProjectChanged += (_, _) => frames.Refresh();
        projects.FilesAdded += (_, _) => frames.Refresh();
        projects.FileRenamed += (_, _) => frames.Refresh();
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is FormDocumentViewModel form)
            {
                form.Designer.ModelReplaced += (_, _) => frames.Refresh();
            }

            frames.Refresh();
        };
        documents.DocumentClosed += (_, _) => frames.Refresh();
        documents.DocumentSaved += (_, _) => frames.Refresh();
        return frames;
    }

    private static List<FrameSource> Sources(IProjectService projects)
    {
        if (projects.Project is not { } project)
        {
            return [];
        }

        string Full(string relative) => Path.GetFullPath(Path.Combine(project.Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        return [.. project.Files.Where(f => !string.IsNullOrEmpty(f.Form) && !string.IsNullOrEmpty(f.Path)).Select(f => new FrameSource(Full(f.Path), Full(f.Form!)))];
    }

    /// <summary>What a definition was built from: the catalog, and the open model or the unit and form texts.</summary>
    private sealed record Stamp(ComponentCatalog Catalog, FormDocument? Open, string? UnitText, string? FormText);

    private sealed record Entry(FrameDefinition Definition, string UnitPath, Stamp Stamp);

    private sealed class Candidate(FrameSource source, string className, Stamp stamp, FormDocument? model, string? formText)
    {
        public FrameSource Source { get; } = source;

        public string ClassName { get; } = className;

        public Stamp Stamp { get; } = stamp;

        public FormDocument? Model { get; } = model;

        public string? FormText { get; } = formText;

        public Entry? Entry { get; set; }

        public string? Problem { get; set; }

        public bool Busy { get; set; }

        public bool Done { get; set; }
    }
}
