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

        // front end: System first (it has no implicit System), then the main file against it (LANG-310, 511)
        var systemPath = FindUnit(SystemUnitName, mainPath);
        if (systemPath is null)
        {
            diagnostics.Add(Diagnostic.Create("E2202", location, SystemUnitName));
            return Done(false, null, []);
        }

        var types = new BuiltInTypes();
        var system = Bind(systemPath, types, null, diagnostics);
        if (system is null)
        {
            return Done(false, null, []);
        }

        var main = Bind(mainPath, types, system, diagnostics);
        if (main is null || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || options.Stage == EmitStage.Check)
        {
            return Done(true, null, []);
        }

        ct.ThrowIfCancellationRequested();
        var objectDirectory = ObjectDirectory(mainPath);
        var units = ImmutableArray.Create(main, system);                         // link order: program, System (CMP-625)
        var llPaths = ImmutableArray.CreateBuilder<string>();
        foreach (var unit in units)
        {
            if (EmitIr(unit, system, location, diagnostics) is not { } text)
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

        if (options.Stage == EmitStage.Obj || !main.Unit.IsProgram || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return Done(true, null, compiled);
        }

        // import libraries (CMP-620…622) and the link (CMP-625…627)
        var unitSymbols = units.Select(u => u.Unit).ToImmutableArray();
        var imports = LinkInputs.Imports(unitSymbols);
        var dllTool = new DllToolInvoker(runner, toolchain.DllTool, FileSystem);
        var libraries = ImmutableArray.CreateBuilder<string>();
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dll in LinkInputs.Dlls(unitSymbols))
        {
            var declaring = units.Where(u => LinkInputs.Imports([u.Unit]).Any(i => i.DllName == dll))
                .Select(u => Path.GetDirectoryName(u.Unit.Locations.IsEmpty ? mainPath : u.Unit.Locations[0].Path)!)
                .Append(Path.GetDirectoryName(systemPath)!)                    // kernel32/ntdll are always linked (CMP-620)
                .Distinct(PathUtil.Comparer);
            var (definition, notFound) = DllToolInvoker.FindDefinition(FileSystem, dll, [], declaring, RtlDirectory(), location);
            if (definition is null)
            {
                diagnostics.Add(notFound!);
                continue;
            }

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
            new LinkRequest(output, objectDirectory, objects)
            {
                AppType = Options.AppType,
                DebugInfo = Options.DebugInfo,
                DebugFormat = Options.DebugFormat,
                ManifestPath = Options.Manifest is "" or "default" or "none" ? null : PathUtil.Normalize(Options.Manifest),   // default manifest: C-167
                ImportLibraries = libraries.ToImmutable(),
                ImportDefinitions = definitions.ToImmutableDictionary(StringComparer.Ordinal),
            },
            ct).ConfigureAwait(false);
        diagnostics.AddRange(linked.Diagnostics);
        return Done(linked.Success, linked.Success ? output : null, compiled);
    }

    /// <summary>Parses and binds one file; null (with E0002/E0003) when it cannot be read.</summary>
    private BindingResult? Bind(string path, BuiltInTypes types, BindingResult? system, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (Read(path, diagnostics) is not { } text)
        {
            return null;
        }

        var defines = Options.AppType == AppType.Console ? Options.Defines.Add("CONSOLE") : Options.Defines;
        // {$IF} conditions are evaluated by the binder in the unit's System scope chain (LANG-862, C-032/C-046)
        var parseOptions = DirectiveConditionEvaluator.WithBinder(
            new ParseOptions(defines) { SearchPaths = [.. Options.SearchPaths.Select(PathUtil.Normalize)] }, system);
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

        var result = Binder.BindCompilationUnit(tree, types, system);
        diagnostics.AddRange(result.Diagnostics);
        diagnostics.AddRange(FlowAnalyzer.Analyze(result));                    // CMP-115: a separate pass after binding (C-031)
        return result;
    }

    private SourceText? Read(string path, ImmutableArray<Diagnostic>.Builder diagnostics)
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

    /// <summary>Lowers and emits one unit; the program gets the unit table (System with null entries until C-161).</summary>
    private static string? EmitIr(BindingResult unit, BindingResult system, Location location, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        try
        {
            var writer = new LlvmTextWriter();
            // the mini RTL's _InitUnits rejects non-null entries (runtime error 204) until procedural types exist (C-161)
            ImmutableArray<UnitTableEntry> table = unit.Unit.IsProgram ? [new UnitTableEntry(system.Unit, HasInitFinal: false)] : [];
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

    /// <summary>LANG-510 for one unit name (C-068): open files are units already part of the compilation (ordinal path order).</summary>
    private string? FindUnit(string name, string mainPath) =>
        new UnitResolver(FileSystem, mainPath, Options.SearchPaths, RtlDirectory(), _openFiles.Keys.Order(StringComparer.Ordinal)).Resolve(name);

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
