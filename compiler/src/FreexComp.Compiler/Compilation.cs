using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Compiler.Types;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler;

/// <summary>api.md §1.</summary>
public sealed record EmitOptions
{
    public EmitStage Stage { get; init; } = EmitStage.Exe;

    /// <summary>"" → &lt;directory of the main file&gt;\&lt;main&gt;.exe (api.md addendum C-044).</summary>
    public string OutputPath { get; init; } = "";

    public bool Verbose { get; init; }

    /// <summary>Ignore the unit cache.</summary>
    public bool ForceRebuild { get; init; }

    /// <summary>Receives each external command line.</summary>
    public Action<string>? CommandLineSink { get; init; }

    /// <summary>Raw tool stdout/stderr lines.</summary>
    public Action<string>? ToolOutputSink { get; init; }

    /// <summary>
    /// The build engine's process runner (compiler-cli.md §4: <c>BuildEngine(fs, processRunner)</c>, injectable for tests); null →
    /// real processes. Internal: the public <see cref="EmitOptions"/> surface stays that of api.md §1.
    /// </summary>
    internal IProcessRunner? ProcessRunner { get; init; }

    /// <summary>
    /// Progress lines of build-engine.md CMP-802: <c>Compiling &lt;Unit&gt;</c> at the head of each compile job's released buffer
    /// (job-key order, CMP-1138) and <c>Linking &lt;exe name&gt;</c> before the link (C-144).
    /// </summary>
    internal Action<string>? InfoSink { get; init; }

    /// <summary>
    /// CMP-1139: the command line of a back-end job with its unit (compile job) or DLL (import-library job). When set, job
    /// command lines go here instead of <see cref="CommandLineSink"/> (which keeps receiving every other command line).
    /// </summary>
    internal Action<string, string?, string?>? JobCommandLineSink { get; init; }

    /// <summary>The clock of the tool timeouts (CMP-1137); a test seam, null → the system clock.</summary>
    internal TimeProvider? ToolTimeProvider { get; init; }
}

/// <param name="OutputPath">The exe, or null.</param>
/// <param name="Diagnostics">All diagnostics of the compilation incl. back-end ones.</param>
/// <param name="CompiledUnits">Unit names recompiled in this Emit.</param>
public sealed record EmitResult(
    bool Success,
    string? OutputPath,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> CompiledUnits,
    ImmutableArray<string> UpToDateUnits,
    TimeSpan Elapsed);

/// <summary>
/// api.md §1. This first slice (C-044) compiles one program (or unit) with the implicitly used <c>System</c> (LANG-511):
/// parse, bind, lower, emit <c>.ll</c> per unit into the object directory, then <c>clang</c>, import libraries and
/// <c>lld-link</c> (toolchain.md CMP-600…627). Uses clauses, the unit cache and the language-service members follow in
/// their tasks (C-069, C-046, C-150+).
/// </summary>
public sealed class Compilation
{
    private const string SystemUnitName = "System";

    private readonly ImmutableDictionary<string, SourceText> _openFiles;

    private Compilation(CompilationOptions options, ImmutableDictionary<string, SourceText> openFiles, IFileSystem fs)
    {
        Options = options;
        _openFiles = openFiles;
        FileSystem = fs;
        _analysis = new Lazy<Analysis>(Analyze, LazyThreadSafetyMode.ExecutionAndPublication);
        _standaloneTypes = new Lazy<BuiltInTypes>(() => new BuiltInTypes(options.Target), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // ------------------------------------------------------------------------------------------------ language service (C-129)

    /// <summary>One front-end run of this snapshot for the language service (errors kept), shared by every semantic model.</summary>
    private sealed record Analysis(FrontEnd? Front, ImmutableArray<Diagnostic> Diagnostics);

    private readonly Lazy<Analysis> _analysis;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SemanticModel>> _models = new(PathUtil.Comparer);

    private Analysis Analyze()
    {
        var (baseline, changed) = Baseline();
        _previous = null;                                                   // the chain of snapshots is not kept alive
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var front = RunFrontEnd(diagnostics, baseline is null ? null : ReuseFrom(baseline, changed));
        return new Analysis(front, diagnostics.ToImmutable());
    }

    // ------------------------------------------------------------------------------------------------ snapshots (C-130, CMP-130)

    /// <summary>A unit-level reuse plan: the built-in types of the earlier run and its units that may be taken over.</summary>
    private sealed record Reuse(BuiltInTypes Types, Func<string, LoadedUnit?> Unit);

    /// <summary>The snapshot whose analysis this one may build on (its analysis exists), and the files changed since.</summary>
    private Compilation? _previous;

    private readonly ImmutableHashSet<string> _changed = ImmutableHashSet.Create<string>(PathUtil.Comparer);

    private Compilation(CompilationOptions options, ImmutableDictionary<string, SourceText> openFiles, IFileSystem fs,
                        Compilation? previous, ImmutableHashSet<string> changed)
        : this(options, openFiles, fs)
    {
        _previous = previous;
        _changed = changed;
    }

    /// <summary>CMP-130: adds or replaces an open file; the new snapshot rebinds only what the change can affect.</summary>
    public Compilation WithSourceText(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var path = PathUtil.Normalize(text.Path);
        var source = path == text.Path ? text : SourceText.From(path, text.Text);
        return Changed(_openFiles.SetItem(path, source), path);
    }

    /// <summary>The file is closed: it is read from the file system again.</summary>
    public Compilation WithoutSourceText(string path)
    {
        var normalized = PathUtil.Normalize(path);
        return Changed(_openFiles.Remove(normalized), normalized);
    }

    /// <summary>Other options: nothing of this snapshot is reused.</summary>
    public Compilation WithOptions(CompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Compilation(options, _openFiles, FileSystem);
    }

    /// <summary>The file changed on disk (language-service.md §1 <c>NotifyFileChangedOnDisk</c>): its trees and binding are not reused.</summary>
    public Compilation WithFileChangedOnDisk(string path) => Changed(_openFiles, PathUtil.Normalize(path));

    private Compilation Changed(ImmutableDictionary<string, SourceText> openFiles, string path)
    {
        var previous = _previous;
        var (baseline, changed) = _analysis.IsValueCreated || Volatile.Read(ref _partialFront) is not null ? (this, _changed.Clear()) : (previous, _changed);
        return new Compilation(Options, openFiles, FileSystem, baseline, changed.Add(path));
    }

    /// <summary>
    /// CMP-130: the units of <paramref name="baseline"/> this snapshot can take over. A changed file whose interface part (the text up
    /// to the <c>implementation</c> keyword) is unchanged is bound anew, but the units using it are not; a unit is taken over when its
    /// file is unchanged and no unit it uses (transitively) changed its interface. A changed file the earlier run did not load (a
    /// new file, a file that may now resolve a uses entry) disables reuse.
    /// </summary>
    private Reuse? ReuseFrom(FrontEnd? front, ImmutableHashSet<string> changed)
    {
        if (front is null)
        {
            return null;
        }

        var units = front.Binder.Units.ToDictionary(u => u.Path, PathUtil.Comparer);
        var interfaceChanged = new HashSet<string>(PathUtil.Comparer);
        foreach (var path in changed)
        {
            if (!units.TryGetValue(path, out var old))
            {
                return null;
            }

            if (!SameInterface(old, path))
            {
                interfaceChanged.Add(path);
            }
        }

        var reusable = new Dictionary<LoadedUnit, bool>();
        bool Reusable(LoadedUnit unit)
        {
            if (reusable.TryGetValue(unit, out var known))
            {
                return known;
            }

            reusable[unit] = true;                                           // a cycle through implementation uses: decided by its other edges
            var ok = unit.Phase == UnitPhase.Complete && unit.Result is not null && !unit.IsPrecompiled && !unit.IsPartial && !changed.Contains(unit.Path)
                     && unit.InterfaceUses.Concat(unit.ImplementationUses).All(u => !interfaceChanged.Contains(u.Path) && (changed.Contains(u.Path) || u.IsPartial || Reusable(u)));
            reusable[unit] = ok;
            return ok;
        }

        return new Reuse(front.Binder.Types, path => units.TryGetValue(path, out var unit) && Reusable(unit) ? unit : null);
    }

    // ------------------------------------------------------------------------------------------------ position models (C-244)

    /// <summary>The latest front end of a position model of this snapshot: a baseline for later snapshots (its complete units).</summary>
    private FrontEnd? _partialFront;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, TextSpan Body), Lazy<SemanticModel>> _positionModels = new();

    /// <summary>The front end later snapshots may take units over from: the analysis when it exists, else the latest position model's.</summary>
    /// <summary>What this snapshot's runs build on: its own latest position-model front end (nothing changed since), else the earlier snapshot's.</summary>
    private (FrontEnd? Front, ImmutableHashSet<string> Changed) Baseline() =>
        Volatile.Read(ref _partialFront) is { } own ? (own, _changed.Clear()) : (_previous?.BaselineFront(), _changed);

    private FrontEnd? BaselineFront() => _analysis.IsValueCreated ? _analysis.Value.Front : Volatile.Read(ref _partialFront);

    /// <summary>
    /// C-244 (CMP-702): a semantic model of <paramref name="path"/> for queries at <paramref name="position"/> (completion): the declarations
    /// of every unit, but of this file's bodies only the outermost routine or unit-level body containing the position (with what it
    /// nests); no flow analysis and no diagnostics. Symbols, scopes, types and references inside that body are those of the full model.
    /// The full model is returned when it exists already or the file is not reached from the main file.
    /// </summary>
    internal SemanticModel GetSemanticModel(string path, int position)
    {
        var normalized = PathUtil.Normalize(path);
        if (_analysis.IsValueCreated || _models.ContainsKey(normalized))
        {
            return GetSemanticModel(normalized);
        }

        var tree = GetSyntaxTree(normalized);
        var outermost = OutermostBodyNode(tree, position);
        // C-345: a body bound per instance has the scopes of the instance bodies ā€” those the file's every body makes it use
        var body = outermost is RoutineDeclarationSyntax routine && IsBoundPerInstance(routine) ? tree.Root.FullSpan : outermost?.Span ?? new TextSpan(position, 0);
        return _positionModels.GetOrAdd((normalized, body), key => new Lazy<SemanticModel>(
            () => CreatePositionModel(key.Path, key.Body), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// C-345: a method implementation of a generic type (a generic segment in its path) or a generic method is bound per instance, by
    /// the units that use the instances (C-116): its position model binds the whole file and those instance bodies.
    /// </summary>
    private static bool IsBoundPerInstance(RoutineDeclarationSyntax routine) =>
        routine.Heading.TypeParameterList is not null
        || routine.Heading.Name is QualifiedNameSyntax { Left: var path } && (path is GenericNameSyntax || path.DescendantNodes().OfType<GenericNameSyntax>().Any());

    /// <summary>The outermost routine declaration or unit-level body containing <paramref name="position"/>, if any.</summary>
    private static SyntaxNode? OutermostBodyNode(SyntaxTree tree, int position)
    {
        bool Contains(SyntaxNode node) => node.Span.Start <= position && position <= node.Span.End;
        var token = tree.FindToken(position);
        SyntaxNode? found = null;
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is RoutineDeclarationSyntax { Block: not null } or InitializationSectionSyntax or FinalizationSectionSyntax
                || node is CompoundStatementSyntax { Parent: BlockSyntax { Parent: ProgramSyntax or LibrarySyntax } })
            {
                found = Contains(node) ? node : found;
            }
        }

        return found;
    }

    private SemanticModel CreatePositionModel(string path, TextSpan body)
    {
        var (baseline, changed) = Baseline();
        if (baseline is null || !PathUtil.AreEqual(path, PathUtil.Normalize(Options.MainFile)) && !baseline.Binder.Units.Any(u => PathUtil.AreEqual(u.Path, path)))
        {
            return GetSemanticModel(path);                                  // nothing to build on, or an additional root (CMP-201)
        }

        bool Overlaps(SyntaxNode syntax) => syntax.Span.Start <= body.End && body.Start <= syntax.Span.End;
        var front = RunFrontEnd(ImmutableArray.CreateBuilder<Diagnostic>(), ReuseFrom(baseline, changed), (path, Overlaps));
        if (front is null || Bound(front, path) is not { IsPartial: true } loaded)
        {
            return GetSemanticModel(path);
        }

        Volatile.Write(ref _partialFront, front);
        _previous = null;                                                   // later runs build on this front end; the chain is not kept alive
        return Model(front, loaded, this, []);
    }

    /// <summary>True when the current text of <paramref name="path"/> keeps the earlier unit's interface part character for character.</summary>
    private bool SameInterface(LoadedUnit old, string path)
    {
        if (old.Tree?.Root is ProgramSyntax or LibrarySyntax)
        {
            return true;                                                    // no unit uses a program or a library
        }

        if (old.Tree?.Root is not UnitSyntax { ImplementationSection.ImplementationKeyword: { IsMissing: false } keyword }
            || Read(path, []) is not { } current)
        {
            return false;
        }

        var prefix = old.Tree.Text.Text.AsSpan(0, keyword.Span.Start);
        var text = current.Text.AsSpan();
        return text.StartsWith(prefix, StringComparison.Ordinal)
               && text[prefix.Length..].StartsWith(keyword.Text, StringComparison.OrdinalIgnoreCase)
               && (text.Length == prefix.Length + keyword.Text.Length || !char.IsAsciiLetterOrDigit(text[prefix.Length + keyword.Text.Length]) && text[prefix.Length + keyword.Text.Length] != '_');
    }

    /// <summary>C-130 instrumentation: the units this snapshot's analysis bound itself (load order), the others were taken over.</summary>
    internal ImmutableArray<string> BoundUnits =>
        _analysis.Value.Front is { } front ? [.. front.Binder.Units.Where(u => !front.Binder.Reused.Contains(u)).Select(u => u.Name)] : [];

    /// <summary>C-137 (language service): the units of this snapshot's analysis in load order (taken-over units with their earlier symbols).</summary>
    internal ImmutableArray<UnitSymbol> UnitSymbols =>
        _analysis.Value.Front is { } front ? [.. front.Binder.Units.Select(u => u.Symbol).OfType<UnitSymbol>()] : [];

    /// <summary>C-138 (CMP-742): the files of the analysed units available as source (units imported from a <c>.pcu</c> are left out).</summary>
    internal ImmutableArray<string> SourceUnitPaths =>
        _analysis.Value.Front is { } front ? [.. front.Binder.Units.Where(u => !u.IsPrecompiled && u.Tree is not null && u.Result is not null).Select(u => u.Path)] : [];

    /// <summary>
    /// api.md §1: <c>null</c> → every diagnostic of the program (parse + bind of every unit, as <c>opc --check</c> reports them);
    /// a path → that file's (syntax + semantic).
    /// </summary>
    public ImmutableArray<Diagnostic> GetDiagnostics(string? path = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return path is null ? _analysis.Value.Diagnostics : GetSemanticModel(path).GetDiagnostics(ct: ct);
    }

    /// <summary>The lexical, syntax and directive diagnostics of one file.</summary>
    public ImmutableArray<Diagnostic> GetSyntaxDiagnostics(string path) => GetSyntaxTree(path).Diagnostics;

    /// <summary>
    /// api.md §6: the semantic model of <paramref name="path"/> (cached per snapshot). A file not reachable from
    /// <see cref="CompilationOptions.MainFile"/> is analysed as an additional root (CMP-201). Never throws for malformed code.
    /// </summary>
    public SemanticModel GetSemanticModel(string path)
    {
        var normalized = PathUtil.Normalize(path);
        return _models.GetOrAdd(normalized, p => new Lazy<SemanticModel>(() => CreateModel(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private SemanticModel CreateModel(string path)
    {
        var analysis = _analysis.Value;
        if (Bound(analysis.Front, path) is { } loaded)
        {
            return Model(analysis.Front!, loaded, this, DiagnosticsOf(analysis, loaded.Path));
        }

        if (!PathUtil.AreEqual(path, PathUtil.Normalize(Options.MainFile)))
        {
            var rootAnalysis = RootAnalysis(path);
            if (Bound(rootAnalysis.Front, path) is { } rootUnit)
            {
                return Model(rootAnalysis.Front!, rootUnit, this, DiagnosticsOf(rootAnalysis, rootUnit.Path));
            }
        }

        // no front end (no System, invalid alias table, unreadable file): the file bound on its own against the built-ins
        var tree = GetSyntaxTree(path);
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes(Options.Target));
        ImmutableArray<Diagnostic> diagnostics = [.. analysis.Diagnostics.Where(d => PathUtil.AreEqual(d.Location.Path, path))];
        return new SemanticModel(this, tree, result, diagnostics.IsEmpty ? [.. tree.Diagnostics, .. result.Diagnostics] : diagnostics, _ => null);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Analysis>> _roots = new(PathUtil.Comparer);

    /// <summary>CMP-201: the analysis of a file the main file does not reach, as the root of its own program (cached per snapshot).</summary>
    private Analysis RootAnalysis(string path) =>
        _roots.GetOrAdd(path, p => new Lazy<Analysis>(
            () => new Compilation(Options with { MainFile = p }, _openFiles, FileSystem)._analysis.Value, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>
    /// api.md §1 (C-263): the unit named <paramref name="unitName"/> as a uses entry of the main file would resolve it (LANG-510
    /// search, LANG-533 aliases, LANG-535 unit scope names); a unit the main file does not reach is analysed as an additional root
    /// (CMP-201). Null when the name does not resolve or the unit could not be bound.
    /// </summary>
    public UnitInfo? FindUnit(string unitName) => Unit(unitName) is { } unit ? Info(unit) : null;

    private static UnitInfo Info(LoadedUnit unit) =>
        new(unit.Name, unit.Path, unit.IsProgram, unit.IsPrecompiled, [.. unit.InterfaceUses.Select(u => u.Name)],
            [.. unit.ImplementationUses.Select(u => u.Name)]);

    /// <summary>
    /// api.md §1 (C-264): every unit reachable from <see cref="CompilationOptions.MainFile"/> in initialization order (LANG-520:
    /// <c>System</c> first), the main file last; empty when the front end could not start (no <c>System</c>, invalid alias table).
    /// </summary>
    public ImmutableArray<UnitInfo> Units =>
        _analysis.Value.Front is { } front ? [.. front.InitOrder.Append(front.Binder.Main!).Distinct().Select(Info)] : [];

    private readonly Lazy<BuiltInTypes> _standaloneTypes;

    /// <summary>
    /// api.md §1 (C-264): the predeclared types the units of this snapshot are bound with; a fresh, per-snapshot set when the
    /// front end could not start.
    /// </summary>
    public BuiltInTypes BuiltIns => _analysis.Value.Front?.Binder.Types ?? _standaloneTypes.Value;

    /// <summary>api.md §1 (C-263): the symbol of the unit <see cref="FindUnit"/> finds, or null.</summary>
    public UnitSymbol? GetUnitSymbol(string unitName) => Unit(unitName)?.Symbol;

    /// <summary>
    /// api.md §1 (C-314): the file the unit named <paramref name="unitName"/> would be loaded from, resolved like a uses entry of
    /// the main file (LANG-510 search order, LANG-533 aliases, LANG-535 unit scope names) — its source, or its precompiled unit
    /// where a directory has no source of it. Resolve only: no unit is parsed or bound (the alias table is the only file read).
    /// Null when the name does not resolve or the alias table is invalid.
    /// </summary>
    public string? ResolveUnitPath(string unitName)
    {
        ArgumentNullException.ThrowIfNull(unitName);
        var aliases = UnitAliasTable.Load(FileSystem, RtlDirectory(), out var aliasError);
        return aliasError is null ? Resolver(PathUtil.Normalize(Options.MainFile), aliases).Find(unitName) : null;
    }

    /// <summary>Whether this snapshot has analysed its program (C-314: resolving a unit name does not).</summary>
    internal bool IsAnalyzed => _analysis.IsValueCreated;

    private LoadedUnit? Unit(string unitName)
    {
        if (ResolveUnitPath(unitName) is not { } path)
        {
            return null;
        }

        static LoadedUnit? Of(FrontEnd? front, string path) =>
            front?.Binder.Units.FirstOrDefault(u => PathUtil.AreEqual(u.Path, path) && u.Symbol is not null);

        return Of(_analysis.Value.Front, path) ?? Of(RootAnalysis(path).Front, path);
    }

    private static LoadedUnit? Bound(FrontEnd? front, string path) =>
        front?.Binder.Units.FirstOrDefault(u => PathUtil.AreEqual(u.Path, path) && u.Result is not null && u.Tree is not null);

    private static ImmutableArray<Diagnostic> DiagnosticsOf(Analysis analysis, string path) =>
        [.. analysis.Diagnostics.Where(d => PathUtil.AreEqual(d.Location.Path, path))];

    private static SemanticModel Model(FrontEnd front, LoadedUnit loaded, Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
    {
        var units = front.Binder.Units;
        UnitSymbol? FindUnit(string name) =>
            loaded.UsedAs.FirstOrDefault(e => IdentifierComparer.Instance.Equals(e.Value, name)).Key?.Symbol
            ?? units.FirstOrDefault(u => u.Symbol is not null && IdentifierComparer.Instance.Equals(u.Name, name))?.Symbol;
        var bodies = loaded.IsPartial && front.Binder.PartialBodies is { } partial ? partial.Filter : null;    // C-355
        return new SemanticModel(compilation, loaded.Tree!, loaded.Result!, diagnostics, FindUnit, bodies);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SyntaxTree?>> _trees = new(PathUtil.Comparer);

    /// <summary>
    /// api.md §1: the parse tree of any file (an open file wins over the file system), parsed on demand without binding and
    /// cached per snapshot; an unreadable file gives an empty tree. <c>{$IF}</c> conditions naming declared constants are
    /// evaluated by the semantic model's own parse (its <see cref="SemanticModel.SyntaxTree"/>).
    /// </summary>
    public SyntaxTree GetSyntaxTree(string path) =>
        TryGetSyntaxTree(path, out var tree) ? tree : SyntaxTree.Parse(SourceText.From(PathUtil.Normalize(path), ""));

    /// <summary>
    /// CMP-1056 (LANG-542): the module the main source makes — a library file a DLL, a program file (or a file without
    /// heading) an executable.
    /// </summary>
    public ModuleKind MainModuleKind =>
        TryGetSyntaxTree(Options.MainFile, out var tree) && tree.Root is LibrarySyntax ? ModuleKind.Library : ModuleKind.Program;

    /// <summary>CMP-1056: the exports of a library main file in ordinal order (LANG-545…549); empty for a program.</summary>
    public ImmutableArray<ExportInfo> Exports => _analysis.Value.Front?.Binder.Main?.Symbol?.Exports ?? [];

    /// <summary>False when the file cannot be read.</summary>
    public bool TryGetSyntaxTree(string path, out SyntaxTree tree)
    {
        var normalized = PathUtil.Normalize(path);
        var parsed = _trees.GetOrAdd(normalized, p => new Lazy<SyntaxTree?>(() => Parse(p, null, []), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        tree = parsed!;
        return parsed is not null;
    }

    public CompilationOptions Options { get; }

    public IFileSystem FileSystem { get; }

    /// <summary>CMP-200: no I/O and no parsing. Open files win over the file system.</summary>
    public static Compilation Create(CompilationOptions options, IEnumerable<SourceText> openFiles, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(options);
        var open = ImmutableDictionary.CreateBuilder<string, SourceText>(PathUtil.Comparer);
        foreach (var text in openFiles)
        {
            open[PathUtil.Normalize(text.Path)] = text;
        }

        return new Compilation(options, open.ToImmutable(), fs);
    }

    /// <summary>
    /// The synchronous form of <see cref="EmitAsync"/> for callers that are synchronous by design (the <c>opc</c> command line):
    /// it holds a thread-pool worker until the emit is done. Callers in a long-lived process (the build engine, the IDE) use
    /// <see cref="EmitAsync"/> (C-269).
    /// </summary>
    public EmitResult Emit(EmitOptions options, CancellationToken ct) =>
        Task.Run(() => EmitAsync(options, ct), ct).GetAwaiter().GetResult();

    /// <summary>
    /// C-269: the emit without blocking any thread-pool thread. The front end and IR emission run on the calling thread (with
    /// up to <see cref="CompilationOptions.MaxParallelism"/> pool helpers when workers are free, CMP-156), tool processes are
    /// awaited (exit and output streams), so the emit completes even when no pool worker is free. Callers on a UI thread start
    /// it with <c>Task.Run(() =&gt; compilation.EmitAsync(options, ct))</c> so the front end leaves that thread; cancelling
    /// <paramref name="ct"/> stops the work and kills running tool processes (<see cref="OperationCanceledException"/>).
    /// </summary>
    public async Task<EmitResult> EmitAsync(EmitOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = Stopwatch.StartNew();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var mainPath = PathUtil.Normalize(Options.MainFile);
        var location = Location.ForFile(mainPath);
        EmitResult Done(bool success, string? output, ImmutableArray<string> compiled) =>
            new(success && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), output, diagnostics.ToImmutable(), compiled, [], clock.Elapsed);

        if (AppTypeMismatch(mainPath) is { } mismatch)
        {
            diagnostics.Add(mismatch);                                          // CMP-1060: nothing is built
            return Done(false, null, []);
        }

        if (Bind(diagnostics) is not { } front || options.Stage == EmitStage.Check)
        {
            return Done(true, null, []);
        }

        var (unitBinder, initOrder, systemPath) = front;
        var main = unitBinder.Main!.Result!;
        ct.ThrowIfCancellationRequested();
        var objectDirectory = ObjectDirectory(mainPath);
        // link order (CMP-625): the main file, System, then every other unit in init order (LANG-520); each emitted once
        var linkUnits = initOrder.Where(u => u != unitBinder.Main).Prepend(unitBinder.Main!).ToImmutableArray();
        var sourceUnits = linkUnits.Where(u => !u.IsPrecompiled).ToImmutableArray();          // precompiled units link as they are (C-074)
        var unitTable = UnitTable(main, initOrder);
        var pcus = PcuWriterFor(front);

        // CMP-150…153 (C-075): at the obj/exe stages a unit whose .pcu and .obj in the object directory are up to date is not
        // recompiled; --emit ll always writes every unit's .ll
        var cached = options.Stage is EmitStage.Obj or EmitStage.Exe && !options.ForceRebuild && Options.UsePcuCache
            ? UpToDate(front, pcus, sourceUnits, objectDirectory)
            : [];
        var stale = sourceUnits.Where(u => !cached.Contains(u)).ToImmutableArray();
        ImmutableArray<string> upToDate = [.. sourceUnits.Where(cached.Contains).Select(u => u.Name)];
        EmitResult Finish(bool success, string? output, ImmutableArray<string> compiled) => Done(success, output, compiled) with { UpToDateUnits = upToDate };

        // CMP-156: units lower and emit on up to MaxParallelism threads (each reads only bound symbols and writes its own module);
        // results and failures are then taken in link order, as a sequential run would
        var texts = new (string? Text, string? Sidecar, Diagnostic? Failure)[stale.Length];
        var debug = DebugInfoOptions();
        await ParallelWork.ForAsync(stale.Length, MaxParallelism,
            i => texts[i] = EmitIr(stale[i].Result!, stale[i].Forms, unitTable, location, debug), ct).ConfigureAwait(false);
        for (var i = 0; i < stale.Length; i++)
        {
            var loaded = stale[i];
            if (texts[i] is not { Text: { } text })
            {
                diagnostics.Add(texts[i].Failure!);
                return Finish(false, null, []);
            }

            var path = Path.Combine(objectDirectory, loaded.Name + ".ll");
            FileSystem.Delete(Path.ChangeExtension(path, ".pcu"));        // CMP-141: no .pcu may vouch for an .obj being replaced
            if (!Write(path, text, location, diagnostics))
            {
                return Finish(false, null, []);
            }

            // CMP-425: the unit's sidecar fragment next to its .ll with -g; none without (a stale one would vouch for old code)
            var fragment = Path.Combine(objectDirectory, loaded.Name + PdiFragmentExtension);
            if (texts[i].Sidecar is { } sidecar ? !Write(fragment, sidecar, location, diagnostics) : !Delete(fragment, location, diagnostics))
            {
                return Finish(false, null, []);
            }
        }

        ImmutableArray<string> compiled = [.. stale.Select(u => u.Name)];
        if (options.Stage == EmitStage.Ll)
        {
            return Finish(true, null, compiled);
        }

        // back end (CMP-600…627)
        var (toolchain, missing) = ToolchainLocator.Locate(FileSystem, Options.ToolchainDirectory, ToolchainEnvironment.Current, location);
        if (toolchain is null)
        {
            diagnostics.Add(missing!);
            return Finish(false, null, compiled);
        }

        var processRunner = options.ProcessRunner ?? new ProcessRunner();
        var versionChecked = false;
        async Task<bool> VersionAsync()
        {
            // the first tool process of this Emit; a run where everything is up to date starts none (build-engine.md §5)
            if (!versionChecked && await ToolchainVersionCheck.Shared.CheckAsync(processRunner, toolchain.Clang, location, ct).ConfigureAwait(false) is { } version)
            {
                diagnostics.Add(version);
                if (version.Severity == DiagnosticSeverity.Error)
                {
                    return false;
                }
            }

            versionChecked = true;
            return true;
        }

        if (!stale.IsEmpty && !await VersionAsync().ConfigureAwait(false))
        {
            return Finish(false, null, compiled);
        }

        var toolOptions = new ToolRunOptions(location)
        {
            CommandLineSink = options.CommandLineSink,
            ToolOutputSink = options.ToolOutputSink,
            TimeProvider = options.ToolTimeProvider ?? TimeProvider.System,
        };
        var runner = new ToolRunner(processRunner, toolOptions);

        // the back-end jobs (CMP-1134, 1135): the compile jobs in unit initialisation order, the program last, then the
        // import-library jobs (CMP-620…622; every DLL named by any linked unit, {$L} inputs of every unit, C-077) by DLL name
        var jobs = new List<BackendJob>();
        var compileJobs = initOrder.Where(u => u != unitBinder.Main).Append(unitBinder.Main!).Where(stale.Contains).ToImmutableArray();
        foreach (var unit in compileJobs)
        {
            var ll = Path.Combine(objectDirectory, unit.Name + ".ll");
            jobs.Add(ClangInvoker.Job(toolchain.Clang, unit.Name, ll, Path.ChangeExtension(ll, ".obj"), Options.Optimization, Options.DebugInfo, Options.Target));
        }

        // C-294 (CMP-155): before anything the link consumes is rewritten (an object, an import library, the export definition
        // file) the response file of the last link is removed, so that a run ending before its own link — cancelled (CMP-1142),
        // a failed job (CMP-1140), a stage before the link — leaves nothing the next run could take for an up-to-date link
        bool InvalidateLink() => Delete(Path.Combine(objectDirectory, "link.rsp"), location, diagnostics);

        // CMP-1060: the module kind follows the main source; the default output is next to it
        var isLibrary = main.Unit.IsLibrary;
        var output = string.IsNullOrEmpty(options.OutputPath)
            ? PathUtil.Normalize(Path.Combine(Path.GetDirectoryName(mainPath)!, Path.GetFileNameWithoutExtension(mainPath) + (isLibrary ? ".dll" : ".exe")))
            : PathUtil.Normalize(options.OutputPath);

        // CMP-1061, 1066: the export definition file of a library, from stage obj on; rewritten only when its content changes
        var exportDefinition = isLibrary ? Path.Combine(objectDirectory, Path.GetFileNameWithoutExtension(output) + ".exports.def") : null;
        var exportsChanged = false;
        var exports = unitBinder.Main!.Symbol!.Exports;
        if (exportDefinition is not null)
        {
            var text = ExportDefinitionFile.Render(exports);
            exportsChanged = !FileSystem.FileExists(exportDefinition) || FileSystem.ReadAllText(exportDefinition) != text;
            if (exportsChanged && !(InvalidateLink() && Write(exportDefinition, text, location, diagnostics)))
            {
                return Finish(false, null, compiled);
            }
        }

        var link = options.Stage == EmitStage.Exe && main.Unit.IsProgram;
        var imports = LinkInputs.Imports(linkUnits.Select(u => u.Symbol!));
        var libraries = ImmutableArray.CreateBuilder<string>();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var hashes = new Dictionary<BackendJob, (string Library, string Hash)>();
        var readyMadeHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);      // hash file → new content hash
        ImmutableArray<Diagnostic> notFound = [];
        if (link)
        {
            (var planned, notFound) = ImportLibraryPlan.Create(
                FileSystem,
                [.. linkUnits.Select(u => new LinkUnit(Path.GetDirectoryName(u.Path)!, u.LinkInputs))],
                Path.GetDirectoryName(systemPath)!,
                RtlDirectory(),
                location,
                Options.Target,
                [.. Options.LibraryPaths.Select(PathUtil.Normalize)]);
            var libraryJobs = new List<BackendJob>();
            foreach (var (dll, definition, readyMade) in planned)
            {
                foreach (var import in imports.Where(i => i.DllName == dll))
                {
                    definitions.TryAdd(import.ImportName, definition);
                }

                if (readyMade)
                {
                    // CMP-1071 step 3: used as is by absolute path; other content than at the last successful link relinks
                    libraries.Add(definition);
                    var recorded = DllToolInvoker.ReadyMadeHashPath(objectDirectory, dll);
                    var content = DllToolInvoker.ContentHash(FileSystem, definition);
                    if (!FileSystem.FileExists(recorded) || FileSystem.ReadAllText(recorded) != content)
                    {
                        readyMadeHashes[recorded] = content;
                    }

                    continue;
                }

                var (library, job, hash) = DllToolInvoker.Prepare(FileSystem, toolchain.DllTool, dll, definition, objectDirectory, Options.Target);
                libraries.Add(library);

                if (job is not null)
                {
                    libraryJobs.Add(job);
                    hashes.Add(job, (library, hash));
                }
            }

            jobs.AddRange(libraryJobs.OrderBy(j => j.Dll!.ToLowerInvariant(), StringComparer.Ordinal));
        }

        if (jobs.Count > 0 && !InvalidateLink())
        {
            return Finish(false, null, compiled);
        }

        // CMP-1136…1140: at most MaxParallelism processes; reports are released in job-key order
        var queue = new BackendJobQueue(processRunner, FileSystem, toolOptions, new BackendJobSinks
        {
            Info = options.InfoSink,
            CommandLine = options.JobCommandLineSink is { } jobCommand ? (line, job) => jobCommand(line, job.Unit, job.Dll)
                : options.CommandLineSink is { } command ? (line, _) => command(line)
                : null,
            ToolOutput = options.ToolOutputSink,
            Diagnostic = diagnostics.Add,
        }, MaxParallelism);
        var states = await queue.RunAsync(jobs, ct).ConfigureAwait(false);

        // CMP-141: each unit's .pcu last, after its .obj succeeded (C-073); CMP-622: a library's hash after the library. Both
        // also for the jobs that finished before a cancellation (CMP-1142): their outputs are complete
        var regenerated = false;
        for (var i = 0; i < jobs.Count; i++)
        {
            if (states[i] != BackendJobState.Succeeded)
            {
                continue;
            }

            if (i < compileJobs.Length)
            {
                if (!Write(Path.Combine(objectDirectory, compileJobs[i].Name + ".pcu"), pcus.Write(compileJobs[i]), location, diagnostics))
                {
                    return Finish(false, null, compiled);
                }
            }
            else
            {
                var (library, hash) = hashes[jobs[i]];
                DllToolInvoker.Complete(FileSystem, library, hash);
                regenerated = true;
            }
        }

        ct.ThrowIfCancellationRequested();
        diagnostics.AddRange(notFound);
        if (!link || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return Finish(true, null, compiled);                                    // CMP-1140: no link after a failed job
        }

        // CMP-628 (C-167), CMP-1100…1106: execution level and DPI awareness reach only this file, never a unit's fingerprint
        // CMP-1315: the resource inputs — after every unit compiled without error, before the manifest is prepared
        // then the files named by the {$R} directives of the linked units in unit init order (the main file last)
        var unitResources = initOrder.Where(u => u != unitBinder.Main).Append(unitBinder.Main!).SelectMany(u => u.LinkInputs.Resources);
        var resources = await ResourceInputsAsync(unitResources, objectDirectory, runner, toolchain, options.InfoSink, VersionAsync, location, diagnostics, ct).ConfigureAwait(false);
        if (resources is null)
        {
            return Finish(false, null, compiled);
        }

        var manifestPath = ApplicationManifest.Prepare(Options, objectDirectory, FileSystem, out var generatedManifest);
        if (manifestPath is not null && !FileSystem.FileExists(manifestPath))
        {
            diagnostics.Add(Diagnostic.Create("E0002", Location.ForFile(manifestPath), manifestPath));   // CMP-1114: nothing is linked
            return Finish(false, null, compiled);
        }

        if (manifestPath is not null && resources.Value.WithManifest is { } withManifest)
        {
            diagnostics.Add(Diagnostic.Create("E3115", location, withManifest));                        // CMP-1320: no silent choice
            return Finish(false, null, compiled);
        }

        var resourceLines = resources.Value.Lines;

        var request = new LinkRequest(output, objectDirectory,
            [.. linkUnits.Select(u => u.IsPrecompiled ? u.ObjectPath : Path.Combine(objectDirectory, u.Name + ".obj"))])
        {
            Target = Options.Target,
            AppType = Options.AppType,
            DebugInfo = Options.DebugInfo,
            DebugFormat = Options.DebugFormat,
            ManifestPath = manifestPath,
            ManifestUacLevel = generatedManifest && Options.ManifestExecutionLevel != ManifestExecutionLevel.AsInvoker
                ? ApplicationManifest.LevelWord(Options.ManifestExecutionLevel)
                : null,
            ExtraObjects = [.. linkUnits.SelectMany(u => u.LinkInputs.Objects).Distinct(PathUtil.Comparer)],
            ResourceInputs = resources.Value.Inputs,
            ImportLibraries = libraries.ToImmutable(),
            ImportDefinitions = definitions.ToImmutableDictionary(StringComparer.Ordinal),
            ExportDefinition = exportDefinition,
            ImportLibraryOutput = isLibrary && !exports.IsEmpty ? LinkInvoker.ImportLibraryPath(output) : null,   // CMP-1063
        };
        if (compiled.IsEmpty && !regenerated && !exportsChanged && readyMadeHashes.Count == 0 && !options.ForceRebuild && LinkIsUpToDate(request, resourceLines))
        {
            return Finish(WriteSidecar(output, main.Unit.Name, linkUnits, objectDirectory, location, diagnostics), output, compiled);   // CMP-155
        }

        if (!await VersionAsync().ConfigureAwait(false))
        {
            return Finish(false, null, compiled);
        }

        options.InfoSink?.Invoke("Linking " + Path.GetFileName(output));
        var linked = await new LinkInvoker(runner, toolchain.LldLink, FileSystem).LinkAsync(request, ct).ConfigureAwait(false);
        diagnostics.AddRange(linked.Diagnostics);
        if (linked.Success && request.ManifestPath is not null)
        {
            // CMP-629: the manifest content is part of the CMP-155 check; the copy is what this link embedded
            Write(Path.Combine(objectDirectory, LinkedManifest), FileSystem.ReadAllText(request.ManifestPath), location, diagnostics);
        }

        if (linked.Success)
        {
            // CMP-1318: what this link took as resource inputs (no file for a link without any)
            var linkResources = Path.Combine(objectDirectory, LinkResources);
            if (resourceLines.Length > 0)
            {
                Write(linkResources, resourceLines, location, diagnostics);
            }
            else
            {
                Delete(linkResources, location, diagnostics);
            }

            foreach (var (path, hash) in readyMadeHashes)
            {
                Write(path, hash, location, diagnostics);                           // CMP-1071: what this link used
            }
        }

        var success = linked.Success && WriteSidecar(output, main.Unit.Name, linkUnits, objectDirectory, location, diagnostics);
        return Finish(success, success ? output : null, compiled);
    }

    /// <summary>
    /// CMP-1060: <c>E0012</c> when <see cref="CompilationOptions.AppType"/> and the kind of the main source disagree — a library
    /// file needs <see cref="AppType.Library"/> and nothing else may have it.
    /// </summary>
    private Diagnostic? AppTypeMismatch(string mainPath)
    {
        if (!TryGetSyntaxTree(mainPath, out var tree) || tree.Root is LibrarySyntax == (Options.AppType == AppType.Library))
        {
            return null;                                                        // an unreadable main file is reported by the front end
        }

        var appType = Options.AppType switch { AppType.Library => "library", AppType.Gui => "gui", _ => "console" };
        var kind = tree.Root switch { LibrarySyntax => "library", UnitSyntax => "unit", _ => "program" };
        return Diagnostic.Create("E0012", Location.ForFile(mainPath), appType, Path.GetFileName(mainPath), kind);
    }

    /// <summary>The per-unit sidecar fragment written next to the unit's <c>.ll</c> with <c>-g</c> (CMP-425).</summary>
    internal const string PdiFragmentExtension = ".pdi.json";

    /// <summary>
    /// CMP-425, DBG-147: with <c>-g</c> the program's <c>&lt;output&gt;.pdi</c> is the merge of the fragments of the linked units that
    /// have one (source units: in the object directory; precompiled units: next to their <c>.obj</c>), in link order; without
    /// <c>-g</c> there is no sidecar (a stale one is removed).
    /// </summary>
    private bool WriteSidecar(string output, string program, ImmutableArray<LoadedUnit> linkUnits, string objectDirectory, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var path = Path.ChangeExtension(output, ".pdi");
        if (!Options.DebugInfo)
        {
            return Delete(path, location, diagnostics);
        }

        var fragments = new List<string>();
        foreach (var unit in linkUnits)
        {
            var fragment = unit.IsPrecompiled
                ? Path.ChangeExtension(unit.ObjectPath, PdiFragmentExtension)
                : Path.Combine(objectDirectory, unit.Name + PdiFragmentExtension);
            try
            {
                if (FileSystem.FileExists(fragment))
                {
                    fragments.Add(FileSystem.ReadAllText(fragment));
                }
            }
            catch (IOException e)
            {
                diagnostics.Add(Diagnostic.Create("E0003", location, fragment, e.Message));
                return false;
            }
        }

        return Write(path, PdiSidecar.Merge(Options.Target, program, PdiSidecar.ContainerName(Options.DebugFormat), fragments), location, diagnostics);
    }

    private bool Delete(string path, Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        try
        {
            FileSystem.Delete(path);
            return true;
        }
        catch (IOException e)
        {
            diagnostics.Add(Diagnostic.Create("E0004", location, path, e.Message));
            return false;
        }
    }

    /// <summary>The copy of the manifest the last successful link embedded (CMP-629, CMP-155), in the object directory.</summary>
    private const string LinkedManifest = "link.manifest";

    /// <summary>CMP-1318: one line <c>&lt;SHA-256&gt; &lt;full path&gt;</c> per resource input of the last successful link, in link order.</summary>
    private const string LinkResources = "link.resources";

    /// <summary>
    /// CMP-1313…1321: the resource inputs of this link — <see cref="CompilationOptions.ResourceFiles"/>, then
    /// <paramref name="unitResources"/> (the files of the units' <c>{$R}</c> directives, CMP-1315) — each path once, a script
    /// compiled to <c>&lt;objdir&gt;\&lt;base&gt;.res</c> unless its record says it is up to date (CMP-1317), a compiled resource file
    /// used where it lies; every input is then read (CMP-1321).
    /// </summary>
    /// <returns>
    /// Null after an error (reported; nothing is linked). Otherwise the inputs in link order, the lines of the link check and the
    /// first input that contains an application manifest.
    /// </returns>
    private async Task<(ImmutableArray<string> Inputs, string Lines, string? WithManifest)?> ResourceInputsAsync(
        IEnumerable<string> unitResources, string objectDirectory, ToolRunner runner, ToolchainPaths toolchain, Action<string>? info, Func<Task<bool>> versionAsync, Location location,
        ImmutableArray<Diagnostic>.Builder diagnostics, CancellationToken ct)
    {
        var files = Options.ResourceFiles.Select(PathUtil.Normalize).Concat(unitResources).Distinct(PathUtil.Comparer).ToImmutableArray();
        if (files.IsEmpty)
        {
            return ([], "", null);
        }

        static bool Is(string path, string extension) => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
        var failed = false;
        foreach (var file in files.Where(f => !Is(f, ".rc") && !Is(f, ".res")))
        {
            diagnostics.Add(Diagnostic.Create("E3117", location, file));                                // CMP-1313
            failed = true;
        }

        var scripts = files.Where(f => Is(f, ".rc")).ToImmutableArray();
        for (var i = 0; i < scripts.Length; i++)
        {
            if (scripts.Take(i).FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s), Path.GetFileNameWithoutExtension(scripts[i]), StringComparison.OrdinalIgnoreCase)) is { } first)
            {
                diagnostics.Add(Diagnostic.Create("E3114", location, first, scripts[i]));              // CMP-1316
                failed = true;
            }
        }

        if (failed)
        {
            return null;
        }

        var inputs = ImmutableArray.CreateBuilder<string>();
        var compiler = new ResourceCompiler(runner, toolchain, FileSystem);
        foreach (var file in files)
        {
            if (Is(file, ".res"))
            {
                inputs.Add(file);
                continue;
            }

            // CMP-1314: the configuration's defines and search paths reach the script
            var request = new ResourceCompileRequest(file, Path.Combine(objectDirectory, Path.GetFileNameWithoutExtension(file) + ".res"), objectDirectory)
            {
                IncludeDirectories = Options.SearchPaths,
                Defines = Options.Defines,
            };
            inputs.Add(request.OutputPath);
            if (ResourceBuildRecord.IsUpToDate(FileSystem, PcuWriter.CompilerFingerprint, request))
            {
                continue;                                                   // CMP-1317: no process is started for it
            }

            var record = ResourceBuildRecord.PathFor(request.OutputPath);
            if (!Delete(record, location, diagnostics) || !await versionAsync().ConfigureAwait(false))
            {
                return null;
            }

            info?.Invoke("Compiling resources " + Path.GetFileName(file));
            var result = await compiler.CompileAsync(request, ct).ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Success)
            {
                failed = true;
                continue;
            }

            // CMP-141: the record after the compiled file
            if (!Write(record, ResourceBuildRecord.Render(FileSystem, PcuWriter.CompilerFingerprint, request, result.Dependencies), location, diagnostics))
            {
                return null;
            }
        }

        if (failed)
        {
            return null;
        }

        // CMP-1320, 1321: every input is read before the link
        var lines = new StringBuilder();
        string? withManifest = null;
        foreach (var input in inputs)
        {
            try
            {
                if (!FileSystem.FileExists(input))
                {
                    diagnostics.Add(Diagnostic.Create("E0002", location, input));
                    failed = true;
                    continue;
                }

                var bytes = FileSystem.ReadAllBytes(input);
                if (ResourceFile.Read(bytes).Any(e => e.Type.Number == ResourceFile.ManifestType))
                {
                    withManifest ??= input;
                }

                lines.Append(ResourceBuildRecord.Hash(bytes)).Append(' ').Append(input).Append('\n');
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic.Create("E3116", location, input));
                failed = true;
            }
        }

        return failed ? null : (inputs.ToImmutable(), lines.ToString(), withManifest);
    }

    /// <summary>
    /// CMP-155: the link is skipped when (and only when) the <c>link.rsp</c> it would write is byte-identical to the one in the object
    /// directory, no unit was recompiled (checked by the caller) and the output exe exists. Timestamps are not consulted.
    /// CMP-1065: for a library the DLL and — when it has exports — its import library must exist (the caller checks that the
    /// export definition file did not change in this run).
    /// </summary>
    private bool LinkIsUpToDate(LinkRequest request, string resourceLines)
    {
        var rsp = Path.Combine(request.ObjectDirectory, "link.rsp");
        var linkResources = Path.Combine(request.ObjectDirectory, LinkResources);
        try
        {
            return FileSystem.FileExists(request.OutputPath) && FileSystem.FileExists(rsp)
                && (FileSystem.FileExists(linkResources) ? FileSystem.ReadAllText(linkResources) : "") == resourceLines   // CMP-1318
                && (request.ImportLibraryOutput is null || FileSystem.FileExists(request.ImportLibraryOutput))
                && FileSystem.ReadAllBytes(rsp).AsSpan().SequenceEqual(
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(LinkInvoker.RenderResponseFile(LinkInvoker.BuildArguments(request))))
                && (request.ManifestPath is null
                    || FileSystem.ReadAllText(request.ManifestPath) == FileSystem.ReadAllText(Path.Combine(request.ObjectDirectory, LinkedManifest)));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// CMP-150…153 (C-075): the source units whose <c>.pcu</c> and <c>.obj</c> in the object directory are up to date — rules 1–5 of
    /// <see cref="UnitCache"/> against the units as bound now (a dependency's current interface hash, so an interface edit reaches
    /// every unit that recorded the old hash and an implementation-only edit reaches none) — plus, for the program, the unit table
    /// it was compiled with (<see cref="PcuWriter.UnitTable"/>).
    /// </summary>
    private HashSet<LoadedUnit> UpToDate(FrontEnd front, PcuWriter pcus, ImmutableArray<LoadedUnit> sourceUnits, string objectDirectory)
    {
        var cache = new UnitCache(FileSystem, PcuWriter.CompilerFingerprint, OptionsFingerprint(), path => _openFiles.TryGetValue(path, out var open) ? open.Text : null);
        var upToDate = new HashSet<LoadedUnit>();
        foreach (var unit in sourceUnits)
        {
            var pcuPath = Path.Combine(objectDirectory, unit.Name + ".pcu");
            if (cache.Check(pcuPath, Path.ChangeExtension(pcuPath, ".obj"), name => Dependency(front, pcus, name), out var pcu, unit.Path) is null
                && (!unit.IsProgram || pcu!["unitTable"]?.ToJsonString() == pcus.UnitTable().ToJsonString()))
            {
                upToDate.Add(unit);
            }
        }

        return upToDate;
    }

    /// <summary>CMP-150 rule 5: the unit of this compilation with that canonical name, its file and current interface hash.</summary>
    private static UnitCacheDependency? Dependency(FrontEnd front, PcuWriter pcus, string name) =>
        front.Binder.Units.FirstOrDefault(u => u.Interface is not null && !u.IsProgram && IdentifierComparer.Instance.Equals(u.Name, name)) is { } unit
            ? new UnitCacheDependency(unit.Path, pcus.InterfaceHash(unit))
            : null;

    /// <summary>The bound units of one front-end run.</summary>
    private sealed record FrontEnd(UnitBinder Binder, ImmutableArray<LoadedUnit> InitOrder, string SystemPath);

    /// <summary>
    /// The front end: System first (it has no implicit System), then the main file and every unit it reaches (LANG-310, 511,
    /// C-069), and each unit's link inputs (C-077). Null when it stopped early or any error-severity diagnostic was reported;
    /// <paramref name="diagnostics"/> receives them in CMP-156 order.
    /// </summary>
    private FrontEnd? Bind(ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var front = RunFrontEnd(diagnostics);
        return front is null || front.Binder.Main!.Result is null || front.Binder.SystemUnit!.Result is null && !front.Binder.SystemUnit.IsPrecompiled
               || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? null
            : front;
    }

    /// <summary>The front end whatever its errors; null only when it could not start (alias table invalid, no <c>System</c>).</summary>
    private FrontEnd? RunFrontEnd(ImmutableArray<Diagnostic>.Builder diagnostics, Reuse? reuse = null, (string Path, Func<SyntaxNode, bool> Filter)? partial = null)
    {
        var mainPath = PathUtil.Normalize(Options.MainFile);
        var aliases = UnitAliasTable.Load(FileSystem, RtlDirectory(), out var aliasError);
        if (aliasError is not null)
        {
            diagnostics.Add(aliasError);                                // LANG-534: compilation stops
            return null;
        }

        var resolver = Resolver(mainPath, aliases);
        var systemPath = resolver.Resolve(SystemUnitName);
        if (systemPath is null)
        {
            diagnostics.Add(Diagnostic.Create("E2202", Location.ForFile(mainPath), SystemUnitName));
            return null;
        }

        var optionsFingerprint = Fingerprint.Options(Options, aliases.Hash);
        var types = reuse?.Types ?? new BuiltInTypes(Options.Target) { DefaultRttiScope = Options.Rtti == "published" ? RttiScope.Published : RttiScope.Broad };   // C-169
        var unitBinder = new UnitBinder(types, resolver, Parse, path => ReadPcu(path, optionsFingerprint), reuse?.Unit) { PartialBodies = partial };
        unitBinder.BindAll(systemPath, mainPath, MaxParallelism);
        var initOrder = unitBinder.InitializationOrder();
        CheckPrecompiledDependencies(new FrontEnd(unitBinder, initOrder, systemPath));
        // C-114, C-343: every unit's {$FORM} files are scanned before any is checked, as the root keyword of a descendant's form
        // depends on which classes have a form (LANG-959: this run's scans, the forms of a unit taken over, a .pcu's key)
        var scans = new Dictionary<LoadedUnit, ImmutableArray<Forms.ScannedForm>>();
        foreach (var loaded in unitBinder.Units.Where(u => u.Result is not null && !unitBinder.Reused.Contains(u)))
        {
            scans[loaded] = Forms.FormEmbedding.Scan(loaded.Result!, loaded.Tree!, FileSystem);
            loaded.FormClasses = Forms.FormEmbedding.FormClasses(scans[loaded]);
        }

        foreach (var loaded in unitBinder.Units.Where(u => u.IsPrecompiled))
        {
            loaded.LinkInputs = PcuReader.LinkInputs(loaded.Pcu!);                // as recorded (C-074)
            loaded.FormClasses = PcuReader.Forms(loaded.Pcu!);                    // CMP-1381
        }

        var formClasses = new Dictionary<Symbols.UnitSymbol, ImmutableArray<string>>();
        foreach (var loaded in unitBinder.Units.Where(u => u.Symbol is not null))
        {
            formClasses[loaded.Symbol!] = loaded.FormClasses;
        }

        bool HasForm(Types.ClassTypeSymbol @class) =>
            @class.ContainingUnit is { } unit && formClasses.TryGetValue(unit, out var names) && names.Contains(@class.Name, IdentifierComparer.Instance);

        foreach (var loaded in unitBinder.Units.Where(u => u.Result is not null && !u.IsPartial && !unitBinder.Reused.Contains(u)))
        {
            loaded.LinkInputs = UnitLinkInputs.Collect(loaded.Result!.Unit, loaded.Tree!, FileSystem, loaded.Diagnostics);
            loaded.Forms = Forms.FormEmbedding.Collect(loaded.Result!, scans[loaded], HasForm, loaded.Diagnostics);    // C-114
        }

        // CMP-156: diagnostics in unit init order (the main file last), then by position
        // (a unit taken over from an earlier run may still list the earlier instance of a unit bound anew: only this run's count)
        foreach (var loaded in initOrder.Append(unitBinder.Main!).Concat(unitBinder.Units.Except(initOrder).Where(u => u != unitBinder.Main))
                     .Where(unitBinder.Units.Contains).Distinct())
        {
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return new FrontEnd(unitBinder, initOrder, systemPath);
    }

    /// <summary>The <c>.pcu</c> writer of one front-end run (C-073): fingerprints, source bytes (open files win), sections.</summary>
    private PcuWriter PcuWriterFor(FrontEnd front)
    {
        var main = front.Binder.Main!.Result!;
        return new PcuWriter(new PcuContext(
            PcuWriter.CompilerFingerprint,
            OptionsFingerprint(),
            path => _openFiles.TryGetValue(path, out var open)
                ? (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(open.Text), null)
                : (FileSystem.ReadAllBytes(path), FileSystem.GetLastWriteTimeUtc(path)),
            front.InitOrder,
            Options.AppType,
            unit => ReferenceEquals(unit, front.Binder.Main)
                ? (false, !Lowerer.ManagedGlobals(main.Unit).IsEmpty)
                : UnitTableEntryOf(unit) is var entry ? (entry.HasInit, entry.HasFinal) : default,
            Types: front.Binder.Types));
    }

    /// <summary>
    /// The <c>.pcu</c> text of every unit of the compilation in link order, without writing anything (C-073 tests; the IDE's
    /// in-process view of CMP-154); empty when the front end reported errors.
    /// </summary>
    internal ImmutableArray<(string Unit, string Text)> PcuTexts()
    {
        if (Bind(ImmutableArray.CreateBuilder<Diagnostic>()) is not { } front)
        {
            return [];
        }

        var writer = PcuWriterFor(front);
        return [.. front.InitOrder.Where(u => u != front.Binder.Main).Prepend(front.Binder.Main!).Select(u => (u.Name, writer.Write(u)))];
    }

    /// <summary>
    /// C-074, C-075: a precompiled unit's <c>.pcu</c>, or null (E2202 at the file) when it is unreadable, of another format version
    /// (CMP-160), written by another compiler or ABI (CMP-150 rule 2) or compiled with other code-affecting options (rule 3).
    /// </summary>
    private System.Text.Json.Nodes.JsonObject? ReadPcu(string path, string optionsFingerprint)
    {
        try
        {
            return PcuReader.Parse(FileSystem.ReadAllText(path)) is { } pcu && PcuReader.HasFingerprints(pcu, PcuWriter.CompilerFingerprint, optionsFingerprint)
                ? pcu
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// C-075, CMP-150 rule 5 for precompiled units: every recorded dependency must still have the recorded interface hash (a
    /// precompiled unit cannot be recompiled here, so a stale one is E2202 at its <c>.pcu</c> until a dedicated code exists).
    /// Checked only when the front end is otherwise free of errors (interface hashes need bound interfaces).
    /// </summary>
    private void CheckPrecompiledDependencies(FrontEnd front)
    {
        var units = front.Binder.Units;
        if (front.Binder.Main?.Result is null || !units.Any(u => u.IsPrecompiled)
            || units.Any(u => u.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || u.Phase == UnitPhase.Failed))
        {
            return;
        }

        var pcus = PcuWriterFor(front);
        foreach (var unit in units.Where(u => u.IsPrecompiled))
        {
            if (UnitCache.Dependencies(unit.Pcu!, name => Dependency(front, pcus, name), comparePaths: false) is not null)
            {
                unit.Diagnostics.Add(Diagnostic.Create("E2202", Location.ForFile(unit.Path), unit.Name));
            }
        }
    }

    /// <summary>Parses one file; null (with E0002/E0003) when it cannot be read.</summary>
    private SyntaxTree? Parse(string path, IUsesScopes? uses, List<Diagnostic> diagnostics)
    {
        SyntaxTree tree;
        if (_trees.TryGetValue(path, out var cached) && cached.IsValueCreated && cached.Value is { DependsOnConditionScope: false } shared)
        {
            tree = shared;                                                  // C-244: no {$IF} saw a scope, so the binding parse is the same tree
        }
        else
        {
            if (Read(path, diagnostics) is not { } text)
            {
                return null;
            }

            var defines = Options.AppType == AppType.Console ? Options.Defines.Add("CONSOLE") : Options.Defines;
            // {$IF} conditions are evaluated by the binder in the unit's uses chain (LANG-862, C-032/C-046/C-069)
            var parseOptions = DirectiveConditionEvaluator.WithBinder(
                new ParseOptions(defines) { SearchPaths = [.. Options.SearchPaths.Select(PathUtil.Normalize)], Target = Options.Target, RtlDirectory = RtlDirectory() }, uses);
            tree = SyntaxTree.Parse(text, parseOptions, FileSystem);
            if (!tree.DependsOnConditionScope)
            {
                _trees.TryAdd(path, new Lazy<SyntaxTree?>(tree));            // GetSyntaxTree gives the same tree (C-244)
            }
        }

        diagnostics.AddRange(tree.Diagnostics);
        if (tree.Root is ProgramSyntax { Name: { IsMissing: false } heading })
        {
            // LANG-500: the heading name need not match the file name, but a mismatch is a hint
            var name = string.Join(".", heading.DescendantTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(t => t.Value as string ?? t.Text));
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!IdentifierComparer.Instance.Equals(name, fileName))
            {
                diagnostics.Add(Diagnostic.Create("H1002", DiagnosticBag.LocationOf(heading), name, fileName));
            }
        }

        return tree;
    }

    private SourceText? Read(string path, List<Diagnostic> diagnostics)
    {
        if (_openFiles.TryGetValue(path, out var open))
        {
            return open;
        }

        if (!FileSystem.FileExists(path))
        {
            diagnostics.Add(Diagnostic.Create("E0002", Location.ForFile(path), path));
            return null;
        }

        try
        {
            return SourceText.From(path, FileSystem.ReadAllText(path));
        }
        catch (IOException e)
        {
            diagnostics.Add(Diagnostic.Create("E0003", Location.ForFile(path), path, e.Message));
            return null;
        }
    }

    /// <summary>
    /// CMP-355 entry of one unit: a unit with an initialization or finalization section or with managed globals (released by
    /// its <c>$final</c>, C-071) — System included — gets its <c>$init</c>/<c>$final</c>, which the RTL's <c>_InitUnits</c> /
    /// shutdown call through procedural types (ABI-100, C-161); other units have two nulls.
    /// </summary>
    private static UnitTableEntry UnitTableEntryOf(LoadedUnit loaded)
    {
        if (loaded.Pcu is { } pcu)
        {
            return new(loaded.Symbol!, HasInit: pcu["hasInit"]!.GetValue<bool>(), HasFinal: pcu["hasFinal"]!.GetValue<bool>());     // C-074
        }

        var unit = loaded.Result!;
        var used = !unit.Bodies.IsEmpty || !Lowerer.ManagedGlobals(unit.Unit).IsEmpty;
        return new(unit.Unit, HasInit: used || !Lowerer.ThreadVars(unit.Unit).IsEmpty || !loaded.Forms.IsEmpty, HasFinal: used);     // $init registers threadvars (ABI-180) and forms (ABI-150)
    }

    /// <summary>
    /// CMP-355 table in init order; a program with managed globals adds its own entry last (<c>null</c>, <c>P.$final</c>), so the
    /// shutdown releases them right after the ExitProc chain and before any unit's <c>$final</c> (ABI-104, C-183).
    /// </summary>
    private static ImmutableArray<UnitTableEntry> UnitTable(BindingResult main, IEnumerable<LoadedUnit> initOrder)
    {
        if (!main.Unit.IsProgram)
        {
            return [];
        }

        var table = initOrder.Select(UnitTableEntryOf);
        return Lowerer.ManagedGlobals(main.Unit).IsEmpty ? [.. table] : [.. table, new UnitTableEntry(main.Unit, HasInit: false, HasFinal: true)];
    }

    /// <summary>
    /// CMP-420…424: with <c>-g</c> the metadata options — container, <c>isOptimized</c> (DBG-101) and the source bytes on disk for the
    /// MD5 checksums (DBG-103); null without <c>-g</c>.
    /// </summary>
    private DebugInfoOptions? DebugInfoOptions() => !Options.DebugInfo ? null
        : new DebugInfoOptions(Options.DebugFormat, IsOptimized: Options.Optimization != OptimizationLevel.O0)
        {
            ReadFile = path =>
            {
                try
                {
                    return FileSystem.FileExists(path) ? FileSystem.ReadAllBytes(path) : null;
                }
                catch (IOException)
                {
                    return null;
                }
            },
        };

    /// <summary>Lowers and emits one unit; the program gets the unit table. Thread-safe (CMP-156).</summary>
    private static (string? Text, string? Sidecar, Diagnostic? Failure) EmitIr(BindingResult unit, ImmutableArray<Forms.EmbeddedForm> forms, ImmutableArray<UnitTableEntry> unitTable, Location location,
        DebugInfoOptions? debug)
    {
        try
        {
            var writer = new LlvmTextWriter(unit.Types.Target);
            ImmutableArray<UnitTableEntry> table = unit.Unit.IsProgram ? unitTable : [];
            var sidecar = IrEmitter.EmitUnit(Lowerer.Lower(unit, forms), writer, table, debug);
            return (writer.GetText(), sidecar, null);
        }
        catch (EmitNotSupportedException e)
        {
            return (null, null, Diagnostic.Create("E2900", e.Location, e.Message));                // C-213
        }
        catch (EmitDiagnosticException e)
        {
            return (null, null, Diagnostic.Create(e.Code, e.Location, e.Arguments));               // C-293
        }
        catch (NotSupportedException e)
        {
            // constructs the binder accepts but the back end does not handle yet
            return (null, null, Diagnostic.Create("E2900", location, e.Message));
        }
    }

    /// <summary>CMP-156: <see cref="CompilationOptions.MaxParallelism"/>, 0 → the processor count.</summary>
    private int MaxParallelism => Options.MaxParallelism > 0 ? Options.MaxParallelism : Environment.ProcessorCount;

    private bool Write(string path, string text, Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        try
        {
            FileSystem.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
            return true;
        }
        catch (IOException e)
        {
            diagnostics.Add(Diagnostic.Create("E0004", location, path, e.Message));
            return false;
        }
    }

    /// <summary>
    /// CMP-150 rule 3 (C-164): the options fingerprint, including the content hash of <c>&lt;rtl&gt;\unit-aliases.json</c> (LANG-533;
    /// an invalid table hashes as none). Reads the table: not part of <see cref="Create"/>'s no-I/O contract.
    /// </summary>
    internal string OptionsFingerprint() => Fingerprint.Options(Options, UnitAliasTable.Load(FileSystem, RtlDirectory(), out _).Hash);

    /// <summary>LANG-510 (C-068): open files are units already part of the compilation (ordinal path order); LANG-535 (C-164).</summary>
    private UnitResolver Resolver(string mainPath, UnitAliasTable aliases) =>
        new(FileSystem, mainPath, Options.SearchPaths, RtlDirectory(), _openFiles.Keys.Order(StringComparer.Ordinal), aliases, Options.UnitScopeNames, Options.Target);

    /// <summary><c>--rtl</c>; "" → <c>%FREEXCOMP_HOME%\rtl\src</c>, else <c>rtl\src</c> next to the running executable (compiler-cli.md §1).</summary>
    /// <summary>C-139 (CMP-750): the normalized RTL root this snapshot resolves <c>System</c> and the alias table from.</summary>
    internal string RtlRoot => RtlDirectory();

    private string RtlDirectory()
    {
        if (Options.RtlDirectory.Length > 0)
        {
            return PathUtil.Normalize(Options.RtlDirectory);
        }

        var home = Environment.GetEnvironmentVariable("FREEXCOMP_HOME");
        return PathUtil.Normalize(Path.Combine(string.IsNullOrEmpty(home) ? AppContext.BaseDirectory : home, "rtl", "src"));
    }

    /// <summary>
    /// "" → &lt;dir of MainFile&gt;\obj\Debug for <c>-O0</c>, else <c>\obj\Release</c> (api.md §1, compiler-cli.md §1); a target other
    /// than the baseline has its own folder <c>\obj\&lt;target id&gt;\…</c> (CMP-1020).
    /// </summary>
    private string ObjectDirectory(string mainPath) =>
        PathUtil.Normalize(Options.ObjectDirectory.Length > 0
            ? Options.ObjectDirectory
            : TargetRegistry.ObjectDirectory(Path.GetDirectoryName(mainPath)!, Options.Target, Options.Optimization == OptimizationLevel.O0 ? "Debug" : "Release"));
}
