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

/// <summary>A unit of the project with a <c>form</c> entry (absolute paths): a possible frame or ancestor (DSG-FD-103, 123).</summary>
public sealed record AncestorSource(string UnitPath, string FormPath);

/// <summary>A designed unit (frame or form) open in the IDE: its unit text and the designer model of its form (DSG-FD-103).</summary>
public sealed record OpenAncestorUnit(string UnitText, FormDocument Model);

/// <summary>A class of the ancestor view (DSG-FD-123): a form or a frame, and the unit that declares it (absolute).</summary>
public sealed record InheritableClass(string ClassName, bool IsFrame, string UnitPath);

/// <summary>
/// The designed classes of the loaded project (frames-designer.md §2, DSG-FD-103…105; form-inheritance-designer.md §2,
/// DSG-FD-123, 124): every unit with a <c>form</c> entry whose form-root class is declared in the unit. This catalog is
/// the <b>frames view</b> — the classes whose ancestor is <c>TFrame</c> or, transitively, another class of the frames
/// view; <see cref="Ancestors"/> is the <b>ancestor view</b> over the same entries — every class, forms and frames. A
/// definition's document is the open designer model when the unit is open in the IDE, else the form parsed from disk
/// with both views (nested frames and descendants resolve, any depth). A unit whose source or form cannot be read, or
/// whose class's ancestor chain repeats, is left out with one line on the Output channel <c>designer</c>.
/// <see cref="Refresh"/> rebuilds the list and raises <see cref="Changed"/> of each view for its classes added, changed or
/// removed — nothing when nothing differs; an applied change of a definition's document raises it for that class
/// (DSG-FD-104). Group members, other projects and libraries are not listed (DSG-FD-105). No Avalonia types.
/// </summary>
public sealed class ProjectAncestorCatalog : IAncestorCatalog
{
    /// <summary>The ancestor of a base frame's class (DSG-FD-103); a frame may also derive from a frame (DSG-FD-124).</summary>
    public const string FrameAncestor = "TFrame";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly IFileSystem _fileSystem;
    private readonly Func<IReadOnlyList<AncestorSource>> _sources;
    private readonly Func<ComponentCatalog> _catalog;
    private readonly Func<string, OpenAncestorUnit?> _open;
    private readonly Action<string>? _report;
    private readonly HashSet<string> _raising = new(Names);
    private SortedDictionary<string, Entry> _entries = new(Names);
    private Dictionary<string, Candidate>? _pending;
    private Dictionary<string, string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private List<AncestorDefinition>? _all;
    private readonly AncestorView _ancestors;

    /// <param name="sources">The units of the loaded project that have a <c>form</c> entry (none without a project).</param>
    /// <param name="catalog">The component metadata the frames are read with.</param>
    /// <param name="open">The open frame unit of a unit path, or null when it is not open.</param>
    /// <param name="report">Writes one line on the Output channel <c>designer</c>.</param>
    public ProjectAncestorCatalog(IFileSystem fileSystem, Func<IReadOnlyList<AncestorSource>> sources, Func<ComponentCatalog> catalog,
        Func<string, OpenAncestorUnit?>? open = null, Action<string>? report = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _open = open ?? (_ => null);
        _report = report;
        _ancestors = new AncestorView(this);
    }

    /// <summary>The frames view (DSG-FD-103, 124), sorted by class name.</summary>
    public IReadOnlyList<AncestorDefinition> All => _all ??= [.. _entries.Values.Where(e => e.IsFrame).Select(e => e.Definition)];

    /// <summary>Raised for the classes of the frames view added, changed or removed.</summary>
    public event EventHandler<AncestorCatalogChangedEventArgs>? Changed;

    /// <summary>
    /// DSG-FD-123: the ancestor view — every designed class of the project (forms and frames), sorted by class name;
    /// descendant documents are read with it. Same entries, refresh and reports as the frames view.
    /// </summary>
    public IAncestorCatalog Ancestors => _ancestors;

    public AncestorDefinition? Find(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (_pending is not null && _pending.TryGetValue(className, out var pending))
        {
            return pending.IsFrame ? Resolve(pending) : null; // during a refresh: a nested frame is read on demand
        }

        return _entries.GetValueOrDefault(className) is { IsFrame: true } entry ? entry.Definition : null;
    }

    /// <summary>The unit file of <paramref name="className"/> (absolute) for every listed class (DSG-FD-123), or null.</summary>
    public string? UnitPathOf(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        return _entries.GetValueOrDefault(className)?.UnitPath;
    }

    /// <summary>The classes of the ancestor view with their kind and unit (the New Inherited Form dialog, UI-DLG-316).</summary>
    public IReadOnlyList<InheritableClass> Inheritable() =>
        [.. _entries.Values.Select(e => new InheritableClass(e.Definition.ClassName, e.IsFrame, e.UnitPath))];

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

        Classify(candidates);
        var next = new SortedDictionary<string, Entry>(Names);
        _pending = candidates;
        try
        {
            foreach (var candidate in candidates.Values)
            {
                if (candidate.Repeated is { } repeated)
                {
                    // DSG-FD-123: broken source — the class is left out (its descendants' chains repeat too).
                    problems[candidate.Source.UnitPath] = string.Format(CultureInfo.CurrentCulture, Strings.Designer_AncestorChainRepeats,
                        candidate.ClassName, repeated);
                }
                else if (Resolve(candidate) is not null)
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
        var framesChanged = _entries.Where(e => e.Value.IsFrame && (!next.TryGetValue(e.Key, out var now) || !now.IsFrame)).Select(e => e.Key)
            .Concat(next.Where(e => e.Value.IsFrame && (!_entries.TryGetValue(e.Key, out var old) || !old.IsFrame
                || !ReferenceEquals(old.Definition, e.Value.Definition))).Select(e => e.Key))
            .ToList();
        if (changed.Count == 0 && framesChanged.Count == 0)
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
        _ancestors.Reset();
        Raise(changed, framesChanged);
    }

    /// <summary>
    /// DSG-FD-123, 124: walks each candidate's ancestor chain through the other candidates — a frame when it reaches
    /// <c>TFrame</c>; <see cref="Candidate.Repeated"/> when a class comes back.
    /// </summary>
    private static void Classify(Dictionary<string, Candidate> candidates)
    {
        foreach (var candidate in candidates.Values)
        {
            var seen = new HashSet<string>(Names) { candidate.ClassName };
            for (var ancestor = candidate.AncestorClass; ancestor is not null;)
            {
                if (Names.Equals(ancestor, FrameAncestor))
                {
                    candidate.IsFrame = true;
                    break;
                }

                if (!candidates.TryGetValue(ancestor, out var next))
                {
                    break;
                }

                if (!seen.Add(next.ClassName))
                {
                    candidate.Repeated = next.ClassName;
                    break;
                }

                ancestor = next.AncestorClass;
            }
        }
    }

    /// <summary>A unit and its form as read now; null when it is no frame or cannot be read (then in <paramref name="problems"/>).</summary>
    private Candidate? Read(AncestorSource source, ComponentCatalog catalog, Dictionary<string, string> problems)
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

        // DSG-FD-103, 123: the class the code sync reads for rootAncestorClass (a syntax tree, never text matching); a unit
        // that does not declare its form's class is no designed unit.
        if (FormClassLocator.Locate(unitText, rootClass, source.UnitPath).FormClass is not { } formClass)
        {
            return null;
        }

        return new Candidate(source, rootClass, formClass.AncestorName, new Stamp(catalog, open?.Model, open is null ? unitText : null, formText),
            open?.Model, formText);
    }

    private string ReadText(string path)
    {
        var detection = EncodingDetector.Detect(_fileSystem.ReadAllBytes(path));
        return detection.IsBinary ? throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Binary, path)) : detection.Text;
    }

    /// <summary>The candidate's definition: the previous one when nothing changed, else built now (null: not readable, or a cycle).</summary>
    private AncestorDefinition? Resolve(Candidate candidate)
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
                candidate.Entry = old with { IsFrame = candidate.IsFrame };
            }
            else if (candidate.Model is { } model)
            {
                candidate.Entry = new Entry(new AncestorDefinition(candidate.ClassName, unitName, candidate.Source.FormPath, model),
                    candidate.Source.UnitPath, candidate.Stamp, candidate.IsFrame);
            }
            else
            {
                // DSG-FD-123: read with both views, so a descendant's definition is itself merged (any depth).
                var parsed = FormParser.Parse(candidate.FormText!, candidate.Source.FormPath, candidate.Stamp.Catalog, candidate.AncestorClass,
                    this, _ancestors);
                if (parsed.Document is { } document)
                {
                    candidate.Entry = new Entry(new AncestorDefinition(candidate.ClassName, unitName, candidate.Source.FormPath, document),
                        candidate.Source.UnitPath, candidate.Stamp, candidate.IsFrame);
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
            Raise([entry.Definition.ClassName], entry.IsFrame ? [entry.Definition.ClassName] : []);
        }
    }

    /// <summary>
    /// Raises <see cref="Changed"/> of the frames view for <paramref name="frameClasses"/> and that of the ancestor view for
    /// <paramref name="classNames"/> (and the frame classes). While a class is being raised, further changes of it raise
    /// nothing (frames and descendants notifying each other).
    /// </summary>
    private void Raise(IReadOnlyList<string> classNames, IReadOnlyList<string> frameClasses)
    {
        var fresh = classNames.Concat(frameClasses).Distinct(Names).Where(_raising.Add).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        try
        {
            var frames = fresh.Where(n => frameClasses.Contains(n, Names)).ToList();
            if (frames.Count > 0)
            {
                Changed?.Invoke(this, new AncestorCatalogChangedEventArgs(frames));
            }

            _ancestors.Raise(fresh);
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
    public static ProjectAncestorCatalog ForIde(IProjectService projects, DocumentService documents, IFileSystem fileSystem, IOutputService? output)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(documents);
        var frames = new ProjectAncestorCatalog(fileSystem, () => Sources(projects), () => documents.DesignCatalog,
            unit => documents.Documents.OfType<FormDocumentViewModel>().FirstOrDefault(d =>
                d.HasUnit && d.Designer.Model is not null && string.Equals(d.FilePath, unit, StringComparison.OrdinalIgnoreCase)) is { } open
                ? new OpenAncestorUnit(open.Buffer.Text, open.Designer.Model!)
                : null,
            output is null ? null : line => output.Append(OutputChannels.Designer, line, OutputKind.Error));
        documents.DesignFrames = frames;
        documents.DesignAncestors = frames.Ancestors; // DSG-FD-123 (D-132)
        documents.DesignAncestorRename = new AncestorRename(fileSystem, frames, () => documents.DesignCatalog, model => // D-137
            documents.Documents.OfType<FormDocumentViewModel>().FirstOrDefault(d => ReferenceEquals(d.Designer.Model, model))?.Designer);
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

    private static List<AncestorSource> Sources(IProjectService projects)
    {
        if (projects.Project is not { } project)
        {
            return [];
        }

        string Full(string relative) => Path.GetFullPath(Path.Combine(project.Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        return [.. project.Files.Where(f => !string.IsNullOrEmpty(f.Form) && !string.IsNullOrEmpty(f.Path)).Select(f => new AncestorSource(Full(f.Path), Full(f.Form!)))];
    }

    /// <summary>What a definition was built from: the catalog, and the open model or the unit and form texts.</summary>
    private sealed record Stamp(ComponentCatalog Catalog, FormDocument? Open, string? UnitText, string? FormText);

    private sealed record Entry(AncestorDefinition Definition, string UnitPath, Stamp Stamp, bool IsFrame);

    /// <summary>DSG-FD-123: the ancestor view over the entries of its frames view.</summary>
    private sealed class AncestorView(ProjectAncestorCatalog owner) : IAncestorCatalog
    {
        private List<AncestorDefinition>? _all;

        public IReadOnlyList<AncestorDefinition> All => _all ??= [.. owner._entries.Values.Select(e => e.Definition)];

        public event EventHandler<AncestorCatalogChangedEventArgs>? Changed;

        public AncestorDefinition? Find(string className)
        {
            ArgumentNullException.ThrowIfNull(className);
            if (owner._pending is not null && owner._pending.TryGetValue(className, out var pending))
            {
                return pending.Repeated is null ? owner.Resolve(pending) : null; // during a refresh: read on demand
            }

            return owner._entries.GetValueOrDefault(className)?.Definition;
        }

        public void Reset() => _all = null;

        public void Raise(IReadOnlyList<string> classNames) => Changed?.Invoke(this, new AncestorCatalogChangedEventArgs(classNames));
    }

    private sealed class Candidate(AncestorSource source, string className, string? ancestorClass, Stamp stamp, FormDocument? model, string? formText)
    {
        public AncestorSource Source { get; } = source;

        public string ClassName { get; } = className;

        /// <summary>The ancestor the unit declares (<c>class(…)</c>), or null.</summary>
        public string? AncestorClass { get; } = ancestorClass;

        /// <summary>DSG-FD-124: the ancestor chain reaches <c>TFrame</c>.</summary>
        public bool IsFrame { get; set; }

        /// <summary>DSG-FD-123: the class that comes back in the ancestor chain, or null.</summary>
        public string? Repeated { get; set; }

        public Stamp Stamp { get; } = stamp;

        public FormDocument? Model { get; } = model;

        public string? FormText { get; } = formText;

        public Entry? Entry { get; set; }

        public string? Problem { get; set; }

        public bool Busy { get; set; }

        public bool Done { get; set; }
    }
}
