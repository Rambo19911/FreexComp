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
    /// Progress lines of build-engine.md CMP-802 as they occur: <c>Compiling &lt;Unit&gt;</c> before each recompiled unit (link
    /// order) and <c>Linking &lt;exe name&gt;</c> before the link (C-144).
    /// </summary>
    internal Action<string>? InfoSink { get; init; }
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

        var body = OutermostBody(GetSyntaxTree(normalized), position);
        return _positionModels.GetOrAdd((normalized, body), key => new Lazy<SemanticModel>(
            () => CreatePositionModel(key.Path, key.Body), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>The span of the outermost routine declaration or unit-level body containing <paramref name="position"/>; else empty at it.</summary>
    private static TextSpan OutermostBody(SyntaxTree tree, int position)
    {
        bool Contains(SyntaxNode node) => node.Span.Start <= position && position <= node.Span.End;
        var token = tree.FindToken(position);
        SyntaxNode? found = null;
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is RoutineDeclarationSyntax { Block: not null } or InitializationSectionSyntax or FinalizationSectionSyntax
                || node is CompoundStatementSyntax { Parent: BlockSyntax { Parent: ProgramSyntax } })
            {
                found = Contains(node) ? node : found;
            }
        }

        return found?.Span ?? new TextSpan(position, 0);
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
        if (old.Tree?.Root is ProgramSyntax)
        {
            return true;                                                    // no unit uses a program
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
            var root = new Compilation(Options with { MainFile = path }, _openFiles, FileSystem);
            var rootAnalysis = root._analysis.Value;
            if (Bound(rootAnalysis.Front, path) is { } rootUnit)
            {
                return Model(rootAnalysis.Front!, rootUnit, this, DiagnosticsOf(rootAnalysis, rootUnit.Path));
            }
        }

        // no front end (no System, invalid alias table, unreadable file): the file bound on its own against the built-ins
        var tree = GetSyntaxTree(path);
        var result = Binder.BindCompilationUnit(tree, new BuiltInTypes());
        ImmutableArray<Diagnostic> diagnostics = [.. analysis.Diagnostics.Where(d => PathUtil.AreEqual(d.Location.Path, path))];
        return new SemanticModel(this, tree, result, diagnostics.IsEmpty ? [.. tree.Diagnostics, .. result.Diagnostics] : diagnostics, _ => null);
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
        return new SemanticModel(compilation, loaded.Tree!, loaded.Result!, diagnostics, FindUnit);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SyntaxTree?>> _trees = new(PathUtil.Comparer);

    /// <summary>
    /// api.md §1: the parse tree of any file (an open file wins over the file system), parsed on demand without binding and
    /// cached per snapshot; an unreadable file gives an empty tree. <c>{$IF}</c> conditions naming declared constants are
    /// evaluated by the semantic model's own parse (its <see cref="SemanticModel.SyntaxTree"/>).
    /// </summary>
    public SyntaxTree GetSyntaxTree(string path) =>
        TryGetSyntaxTree(path, out var tree) ? tree : SyntaxTree.Parse(SourceText.From(PathUtil.Normalize(path), ""));

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

    public EmitResult Emit(EmitOptions options, CancellationToken ct) =>
        Task.Run(() => EmitAsync(options, ct), ct).GetAwaiter().GetResult();

    private async Task<EmitResult> EmitAsync(EmitOptions options, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var mainPath = PathUtil.Normalize(Options.MainFile);
        var location = Location.ForFile(mainPath);
        EmitResult Done(bool success, string? output, ImmutableArray<string> compiled) =>
            new(success && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), output, diagnostics.ToImmutable(), compiled, [], clock.Elapsed);

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
        var texts = new (string? Text, Diagnostic? Failure)[stale.Length];
        foreach (var unit in stale)
        {
            options.InfoSink?.Invoke("Compiling " + unit.Name);
        }

        Parallel.For(0, stale.Length, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
            i => texts[i] = EmitIr(stale[i].Result!, stale[i].Forms, unitTable, location));
        var llPaths = ImmutableArray.CreateBuilder<string>();
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

            llPaths.Add(path);
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

        var runner = new ToolRunner(processRunner, new ToolRunOptions(location)
        {
            CommandLineSink = options.CommandLineSink,
            ToolOutputSink = options.ToolOutputSink,
        });
        var clang = new ClangInvoker(runner, toolchain.Clang);
        var objects = llPaths.Select(ll => Path.ChangeExtension(ll, ".obj")).ToImmutableArray();
        using var slots = new SemaphoreSlim(MaxParallelism);                       // CMP-156: up to MaxParallelism clang processes
        var results = await Task.WhenAll(llPaths.Select(async (ll, i) =>
        {
            await slots.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await clang.CompileAsync(ll, objects[i], Options.Optimization, Options.DebugInfo, ct).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(false);                                                  // CMP-606: units in parallel
        foreach (var failure in results.Select(r => r.Failure).OfType<Diagnostic>())
        {
            diagnostics.Add(failure);
        }

        // CMP-141: each unit's .pcu last, after its .obj succeeded (C-073)
        for (var i = 0; i < stale.Length; i++)
        {
            if (results[i].Failure is null && !Write(Path.Combine(objectDirectory, stale[i].Name + ".pcu"), pcus.Write(stale[i]), location, diagnostics))
            {
                return Finish(false, null, compiled);
            }
        }

        if (options.Stage == EmitStage.Obj || !main.Unit.IsProgram || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return Finish(true, null, compiled);
        }

        // import libraries (CMP-620…622) and the link (CMP-625…627)
        // every DLL named by any linked unit, {$L} inputs of every unit (C-077)
        var imports = LinkInputs.Imports(linkUnits.Select(u => u.Symbol!));
        var dllTool = new DllToolInvoker(runner, toolchain.DllTool, FileSystem);
        var libraries = ImmutableArray.CreateBuilder<string>();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var (planned, notFound) = ImportLibraryPlan.Create(
            FileSystem,
            [.. linkUnits.Select(u => new LinkUnit(Path.GetDirectoryName(u.Path)!, u.LinkInputs))],
            Path.GetDirectoryName(systemPath)!,
            RtlDirectory(),
            location);
        diagnostics.AddRange(notFound);
        var regenerated = false;
        foreach (var (dll, definition) in planned)
        {
            var library = await dllTool.BuildAsync(dll, definition, objectDirectory, ct).ConfigureAwait(false);
            regenerated |= library.Regenerated;
            if (library.Failure is { } failure)
            {
                diagnostics.Add(failure);
                continue;
            }

            libraries.Add(library.LibraryPath);
            foreach (var import in imports.Where(i => i.DllName == dll))
            {
                definitions.TryAdd(import.ImportName, definition);
            }
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return Finish(false, null, compiled);
        }

        var output = string.IsNullOrEmpty(options.OutputPath)
            ? PathUtil.Normalize(Path.Combine(Path.GetDirectoryName(mainPath)!, Path.GetFileNameWithoutExtension(mainPath) + ".exe"))
            : PathUtil.Normalize(options.OutputPath);
        var request = new LinkRequest(output, objectDirectory,
            [.. linkUnits.Select(u => u.IsPrecompiled ? u.ObjectPath : Path.Combine(objectDirectory, u.Name + ".obj"))])
        {
            AppType = Options.AppType,
            DebugInfo = Options.DebugInfo,
            DebugFormat = Options.DebugFormat,
            ManifestPath = ApplicationManifest.Prepare(Options.Manifest, Options.AppType, objectDirectory, FileSystem),   // CMP-628 (C-167)
            ExtraObjects = [.. linkUnits.SelectMany(u => u.LinkInputs.Objects).Distinct(PathUtil.Comparer)],
            ImportLibraries = libraries.ToImmutable(),
            ImportDefinitions = definitions.ToImmutableDictionary(StringComparer.Ordinal),
        };
        if (compiled.IsEmpty && !regenerated && !options.ForceRebuild && LinkIsUpToDate(request))
        {
            return Finish(true, output, compiled);                                  // CMP-155
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

        return Finish(linked.Success, linked.Success ? output : null, compiled);
    }

    /// <summary>The copy of the manifest the last successful link embedded (CMP-629, CMP-155), in the object directory.</summary>
    private const string LinkedManifest = "link.manifest";

    /// <summary>
    /// CMP-155: the link is skipped when (and only when) the <c>link.rsp</c> it would write is byte-identical to the one in the object
    /// directory, no unit was recompiled (checked by the caller) and the output exe exists. Timestamps are not consulted.
    /// </summary>
    private bool LinkIsUpToDate(LinkRequest request)
    {
        var rsp = Path.Combine(request.ObjectDirectory, "link.rsp");
        try
        {
            return FileSystem.FileExists(request.OutputPath) && FileSystem.FileExists(rsp)
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
        var types = reuse?.Types ?? new BuiltInTypes { DefaultRttiScope = Options.Rtti == "published" ? RttiScope.Published : RttiScope.Broad };   // C-169
        var unitBinder = new UnitBinder(types, resolver, Parse, path => ReadPcu(path, optionsFingerprint), reuse?.Unit) { PartialBodies = partial };
        unitBinder.BindAll(systemPath, mainPath, MaxParallelism);
        var initOrder = unitBinder.InitializationOrder();
        CheckPrecompiledDependencies(new FrontEnd(unitBinder, initOrder, systemPath));
        foreach (var loaded in unitBinder.Units.Where(u => u.Result is not null && !u.IsPartial && !unitBinder.Reused.Contains(u)))
        {
            loaded.LinkInputs = UnitLinkInputs.Collect(loaded.Result!.Unit, loaded.Tree!, FileSystem, loaded.Diagnostics);
            loaded.Forms = Forms.FormEmbedding.Collect(loaded.Result!, loaded.Tree!, FileSystem, loaded.Diagnostics);    // C-114
        }

        foreach (var loaded in unitBinder.Units.Where(u => u.IsPrecompiled))
        {
            loaded.LinkInputs = PcuReader.LinkInputs(loaded.Pcu!);                // as recorded (C-074)
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
                new ParseOptions(defines) { SearchPaths = [.. Options.SearchPaths.Select(PathUtil.Normalize)] }, uses);
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

    /// <summary>Lowers and emits one unit; the program gets the unit table. Thread-safe (CMP-156).</summary>
    private static (string? Text, Diagnostic? Failure) EmitIr(BindingResult unit, ImmutableArray<Forms.EmbeddedForm> forms, ImmutableArray<UnitTableEntry> unitTable, Location location)
    {
        try
        {
            var writer = new LlvmTextWriter();
            ImmutableArray<UnitTableEntry> table = unit.Unit.IsProgram ? unitTable : [];
            IrEmitter.EmitUnit(Lowerer.Lower(unit, forms), writer, table);
            return (writer.GetText(), null);
        }
        catch (EmitNotSupportedException e)
        {
            return (null, Diagnostic.Create("E2900", e.Location, e.Message));                // C-213
        }
        catch (NotSupportedException e)
        {
            // constructs the binder accepts but the back end does not handle yet
            return (null, Diagnostic.Create("E2900", location, e.Message));
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
        new(FileSystem, mainPath, Options.SearchPaths, RtlDirectory(), _openFiles.Keys.Order(StringComparer.Ordinal), aliases, Options.UnitScopeNames);

    /// <summary><c>--rtl</c>; "" → <c>%FREEXCOMP_HOME%\rtl\src</c>, else <c>rtl\src</c> next to the running executable (compiler-cli.md §1).</summary>
    private string RtlDirectory()
    {
        if (Options.RtlDirectory.Length > 0)
        {
            return PathUtil.Normalize(Options.RtlDirectory);
        }

        var home = Environment.GetEnvironmentVariable("FREEXCOMP_HOME");
        return PathUtil.Normalize(Path.Combine(string.IsNullOrEmpty(home) ? AppContext.BaseDirectory : home, "rtl", "src"));
    }

    /// <summary>"" → &lt;dir of MainFile&gt;\obj\Debug for <c>-O0</c>, else <c>\obj\Release</c> (api.md §1, compiler-cli.md §1).</summary>
    private string ObjectDirectory(string mainPath) =>
        PathUtil.Normalize(Options.ObjectDirectory.Length > 0
            ? Options.ObjectDirectory
            : Path.Combine(Path.GetDirectoryName(mainPath)!, "obj", Options.Optimization == OptimizationLevel.O0 ? "Debug" : "Release"));
}
