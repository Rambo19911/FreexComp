using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
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
        var units = sourceUnits.Select(u => u.Result!).ToImmutableArray();
        var unitTable = UnitTable(main, initOrder);
        var llPaths = ImmutableArray.CreateBuilder<string>();
        foreach (var unit in units)
        {
            if (EmitIr(unit, unitTable, location, diagnostics) is not { } text)
            {
                return Done(false, null, []);
            }

            var path = Path.Combine(objectDirectory, unit.Unit.Name + ".ll");
            if (!Write(path, text, location, diagnostics))
            {
                return Done(false, null, []);
            }

            llPaths.Add(path);
        }

        ImmutableArray<string> compiled = [.. units.Select(u => u.Unit.Name)];
        if (options.Stage == EmitStage.Ll)
        {
            return Done(true, null, compiled);
        }

        // back end (CMP-600…627)
        var (toolchain, missing) = ToolchainLocator.Locate(FileSystem, Options.ToolchainDirectory, ToolchainEnvironment.Current, location);
        if (toolchain is null)
        {
            diagnostics.Add(missing!);
            return Done(false, null, compiled);
        }

        var processRunner = new ProcessRunner();
        if (await ToolchainVersionCheck.Shared.CheckAsync(processRunner, toolchain.Clang, location, ct).ConfigureAwait(false) is { } version)
        {
            diagnostics.Add(version);
            if (version.Severity == DiagnosticSeverity.Error)
            {
                return Done(false, null, compiled);
            }
        }

        var runner = new ToolRunner(processRunner, new ToolRunOptions(location)
        {
            CommandLineSink = options.CommandLineSink,
            ToolOutputSink = options.ToolOutputSink,
        });
        var clang = new ClangInvoker(runner, toolchain.Clang);
        var objects = llPaths.Select(ll => Path.ChangeExtension(ll, ".obj")).ToImmutableArray();
        var results = await Task.WhenAll(llPaths.Select((ll, i) => clang.CompileAsync(ll, objects[i], Options.Optimization, Options.DebugInfo, ct)))
            .ConfigureAwait(false);                                                 // CMP-606: units in parallel
        foreach (var failure in results.Select(r => r.Failure).OfType<Diagnostic>())
        {
            diagnostics.Add(failure);
        }

        // CMP-141: each unit's .pcu last, after its .obj succeeded (C-073)
        var pcus = PcuWriterFor(front);
        for (var i = 0; i < sourceUnits.Length; i++)
        {
            if (results[i].Failure is null && !Write(Path.Combine(objectDirectory, sourceUnits[i].Name + ".pcu"), pcus.Write(sourceUnits[i]), location, diagnostics))
            {
                return Done(false, null, compiled);
            }
        }

        if (options.Stage == EmitStage.Obj || !main.Unit.IsProgram || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return Done(true, null, compiled);
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
        foreach (var (dll, definition) in planned)
        {
            var library = await dllTool.BuildAsync(dll, definition, objectDirectory, ct).ConfigureAwait(false);
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
            return Done(false, null, compiled);
        }

        var output = string.IsNullOrEmpty(options.OutputPath)
            ? PathUtil.Normalize(Path.Combine(Path.GetDirectoryName(mainPath)!, Path.GetFileNameWithoutExtension(mainPath) + ".exe"))
            : PathUtil.Normalize(options.OutputPath);
        var linked = await new LinkInvoker(runner, toolchain.LldLink, FileSystem).LinkAsync(
            new LinkRequest(output, objectDirectory, [.. linkUnits.Select(u => u.IsPrecompiled ? u.ObjectPath : objects[sourceUnits.IndexOf(u)])])
            {
                AppType = Options.AppType,
                DebugInfo = Options.DebugInfo,
                DebugFormat = Options.DebugFormat,
                ManifestPath = Options.Manifest is "" or "default" or "none" ? null : PathUtil.Normalize(Options.Manifest),   // default manifest: C-167
                ExtraObjects = [.. linkUnits.SelectMany(u => u.LinkInputs.Objects).Distinct(PathUtil.Comparer)],
                ImportLibraries = libraries.ToImmutable(),
                ImportDefinitions = definitions.ToImmutableDictionary(StringComparer.Ordinal),
            },
            ct).ConfigureAwait(false);
        diagnostics.AddRange(linked.Diagnostics);
        return Done(linked.Success, linked.Success ? output : null, compiled);
    }

    /// <summary>The bound units of one front-end run.</summary>
    private sealed record FrontEnd(UnitBinder Binder, ImmutableArray<LoadedUnit> InitOrder, string SystemPath);

    /// <summary>
    /// The front end: System first (it has no implicit System), then the main file and every unit it reaches (LANG-310, 511,
    /// C-069), and each unit's link inputs (C-077). Null when it stopped early or any error-severity diagnostic was reported;
    /// <paramref name="diagnostics"/> receives them in CMP-156 order.
    /// </summary>
    private FrontEnd? Bind(ImmutableArray<Diagnostic>.Builder diagnostics)
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

        var unitBinder = new UnitBinder(new BuiltInTypes(), resolver, Parse, ReadPcu);
        unitBinder.BindAll(systemPath, mainPath);
        var initOrder = unitBinder.InitializationOrder();
        foreach (var loaded in unitBinder.Units.Where(u => u.Result is not null))
        {
            loaded.LinkInputs = UnitLinkInputs.Collect(loaded.Result!.Unit, loaded.Tree!, FileSystem, loaded.Diagnostics);
        }

        foreach (var loaded in unitBinder.Units.Where(u => u.IsPrecompiled))
        {
            loaded.LinkInputs = PcuReader.LinkInputs(loaded.Pcu!);                // as recorded (C-074)
        }

        // CMP-156: diagnostics in unit init order (the main file last), then by position
        foreach (var loaded in initOrder.Append(unitBinder.Main!).Concat(unitBinder.Units.Except(initOrder).Where(u => u != unitBinder.Main)))
        {
            diagnostics.AddRange(loaded.Diagnostics);
        }

        return unitBinder.Main!.Result is null || unitBinder.SystemUnit!.Result is null && !unitBinder.SystemUnit.IsPrecompiled
               || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? null
            : new FrontEnd(unitBinder, initOrder, systemPath);
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
    /// C-074: a precompiled unit's <c>.pcu</c>, or null when it is unreadable, of another format version (CMP-160) or written by
    /// another compiler or ABI (CMP-150 rule 2). The options fingerprint is not compared: such a unit cannot be recompiled here.
    /// </summary>
    private System.Text.Json.Nodes.JsonObject? ReadPcu(string path)
    {
        try
        {
            return PcuReader.Parse(FileSystem.ReadAllText(path)) is { } pcu && PcuReader.HasFingerprints(pcu, PcuWriter.CompilerFingerprint) ? pcu : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Parses one file; null (with E0002/E0003) when it cannot be read.</summary>
    private SyntaxTree? Parse(string path, IUsesScopes? uses, List<Diagnostic> diagnostics)
    {
        if (Read(path, diagnostics) is not { } text)
        {
            return null;
        }

        var defines = Options.AppType == AppType.Console ? Options.Defines.Add("CONSOLE") : Options.Defines;
        // {$IF} conditions are evaluated by the binder in the unit's uses chain (LANG-862, C-032/C-046/C-069)
        var parseOptions = DirectiveConditionEvaluator.WithBinder(
            new ParseOptions(defines) { SearchPaths = [.. Options.SearchPaths.Select(PathUtil.Normalize)] }, uses);
        var tree = SyntaxTree.Parse(text, parseOptions, FileSystem);
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
        return new(unit.Unit, HasInit: used, HasFinal: used);
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

    /// <summary>Lowers and emits one unit; the program gets the unit table.</summary>
    private static string? EmitIr(BindingResult unit, ImmutableArray<UnitTableEntry> unitTable, Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        try
        {
            var writer = new LlvmTextWriter();
            ImmutableArray<UnitTableEntry> table = unit.Unit.IsProgram ? unitTable : [];
            IrEmitter.EmitUnit(Lowerer.Lower(unit), writer, table);
            return writer.GetText();
        }
        catch (NotSupportedException e)
        {
            // constructs the binder accepts but the back end does not handle yet
            diagnostics.Add(Diagnostic.Create("E2900", location, e.Message));
            return null;
        }
    }

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
