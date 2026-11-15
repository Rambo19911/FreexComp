using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService;

public enum WorkspaceChangeKind { ProjectChanged, DocumentOpened, DocumentChanged, DocumentClosed, FileChangedOnDisk }

/// <summary>What changed; <see cref="Path"/> is null for a project change.</summary>
public sealed class WorkspaceChangedEventArgs(WorkspaceChangeKind kind, string? path, Compilation compilation) : EventArgs
{
    public WorkspaceChangeKind Kind { get; } = kind;

    public string? Path { get; } = path;

    /// <summary>The snapshot after the change.</summary>
    public Compilation Compilation { get; } = compilation;
}

/// <summary>
/// The open documents and the active project (language-service.md §1): the only stateful class of the language service,
/// thread-safe. Every change produces a new immutable <see cref="Compilation"/> snapshot (CMP-130); readers keep using the
/// snapshot they took.
/// </summary>
public sealed class Workspace
{
    private readonly object _gate = new();
    private readonly IFileSystem _fileSystem;
    private CompilationOptions? _project;
    private Compilation _compilation;

    public Workspace(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        _fileSystem = fs;
        _compilation = Compilation.Create(new CompilationOptions { MainFile = PathUtil.Normalize(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "untitled.pas")) }, [], fs);
    }

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    /// <summary>The current snapshot; never null.</summary>
    public Compilation CurrentCompilation
    {
        get
        {
            lock (_gate)
            {
                return _compilation;
            }
        }
    }

    /// <summary>The options of the active <c>.pproj</c> and configuration (build-engine.md CMP-812); nothing open is reused.</summary>
    public void SetProject(CompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Apply(WorkspaceChangeKind.ProjectChanged, null, current =>
        {
            _project = options;
            return current.WithOptions(options);
        });
    }

    public void OpenDocument(string path, string text) =>
        Apply(WorkspaceChangeKind.DocumentOpened, path, current => current.WithSourceText(SourceText.From(PathUtil.Normalize(path), text)));

    /// <summary>Whole-text replace (CMP-130).</summary>
    public void UpdateDocument(string path, string text) =>
        Apply(WorkspaceChangeKind.DocumentChanged, path, current => current.WithSourceText(SourceText.From(PathUtil.Normalize(path), text)));

    /// <summary>Changes against the document's current text (all spans refer to it; they must not overlap).</summary>
    public void UpdateDocument(string path, IReadOnlyList<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Apply(WorkspaceChangeKind.DocumentChanged, path, current =>
        {
            var normalized = PathUtil.Normalize(path);
            var builder = new StringBuilder(current.GetSyntaxTree(normalized).Text.Text);
            foreach (var change in changes.OrderByDescending(c => c.Span.Start))
            {
                builder.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
            }

            return current.WithSourceText(SourceText.From(normalized, builder.ToString()));
        });
    }

    /// <summary>The file is read from the file system again.</summary>
    public void CloseDocument(string path) =>
        Apply(WorkspaceChangeKind.DocumentClosed, path, current => current.WithoutSourceText(path));

    /// <summary>Invalidates what the snapshot knows about that file (trees, binding).</summary>
    public void NotifyFileChangedOnDisk(string path) =>
        Apply(WorkspaceChangeKind.FileChangedOnDisk, path, current => current.WithFileChangedOnDisk(path));

    /// <summary>
    /// A snapshot handle of the document. With no project set, the file is analysed as the root of its own compilation
    /// (CMP-701); otherwise as part of the project, or as an additional root when the project does not reach it (CMP-201).
    /// </summary>
    public Document GetDocument(string path)
    {
        var normalized = PathUtil.Normalize(path);
        Compilation compilation;
        lock (_gate)
        {
            compilation = _project is null && !PathUtil.AreEqual(_compilation.Options.MainFile, normalized)
                ? _compilation.WithOptions(_compilation.Options with { MainFile = normalized })
                : _compilation;
            if (_project is null)
            {
                _compilation = compilation;                                 // later edits build on this root
            }
        }

        return new Document(normalized, compilation);
    }

    private void Apply(WorkspaceChangeKind kind, string? path, Func<Compilation, Compilation> change)
    {
        Compilation next;
        lock (_gate)
        {
            next = change(_compilation);
            _compilation = next;
        }

        Changed?.Invoke(this, new WorkspaceChangedEventArgs(kind, path is null ? null : PathUtil.Normalize(path), next));
    }
}

/// <summary>An immutable snapshot of one document (language-service.md §1).</summary>
public sealed class Document
{
    private readonly Lazy<SemanticModel> _model;

    internal Document(string path, Compilation compilation)
    {
        Path = path;
        Compilation = compilation;
        SyntaxTree = compilation.GetSyntaxTree(path);
        _model = new Lazy<SemanticModel>(() => compilation.GetSemanticModel(path), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Path { get; }

    public SourceText Text => SyntaxTree.Text;

    /// <summary>The parse tree (syntax only); the semantic services work on <see cref="SemanticModel"/>'s own tree.</summary>
    public SyntaxTree SyntaxTree { get; }

    /// <summary>Bound on first use.</summary>
    public SemanticModel SemanticModel => _model.Value;

    /// <summary>
    /// C-244: <see cref="SemanticModel"/> once it exists, else a model that binds only the body around <paramref name="position"/>
    /// (no diagnostics): for queries about that position (completion).
    /// </summary>
    internal SemanticModel ModelAt(int position) => _model.IsValueCreated ? _model.Value : Compilation.GetSemanticModel(Path, position);

    public Compilation Compilation { get; }
}
