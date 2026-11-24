using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <param name="Regenerated">False when the recorded hash matched and <c>llvm-dlltool</c> was not run.</param>
/// <param name="Failure">E4004 when <c>llvm-dlltool</c> failed; null otherwise.</param>
internal sealed record ImportLibraryResult(string LibraryPath, bool Regenerated, Diagnostic? Failure);

/// <summary>CMP-620…622: <c>.def</c> lookup and import libraries <c>&lt;objdir&gt;\imports\&lt;dll-base&gt;.lib</c>.</summary>
internal sealed class DllToolInvoker(ToolRunner runner, string dllToolPath, IFileSystem fs)
{
    /// <summary>
    /// CMP-620 order: a <c>{$L x.def}</c> file of a unit whose base name is the DLL's base name; then
    /// <c>&lt;dir&gt;\&lt;dll-base&gt;.def</c> for each declaring unit's directory in order; then
    /// <c>&lt;rtl root&gt;\..\winapi\def\&lt;target sub-directory&gt;\&lt;dll-base&gt;.def</c>, then
    /// <c>&lt;rtl root&gt;\..\winapi\def\&lt;dll-base&gt;.def</c> (CMP-1023; a missing sub-directory is skipped). Not found → E4006.
    /// Names compare case-insensitively.
    /// </summary>
    /// <param name="target">null → <see cref="TargetRegistry.Default"/>.</param>
    public static (string? Path, Diagnostic? Diagnostic) FindDefinition(
        IFileSystem fs,
        string dllName,
        IEnumerable<string> unitDefinitionFiles,
        IEnumerable<string> declaringUnitDirectories,
        string rtlDirectory,
        Location location,
        TargetInfo? target = null)
    {
        var (path, _, diagnostic) = Find(fs, dllName, unitDefinitionFiles, declaringUnitDirectories, [], rtlDirectory, location, target);
        return (path, diagnostic);
    }

    /// <summary>
    /// CMP-1071, the whole order: (1) a <c>{$L x.def}</c> of a unit, (2) the declaring units' directories, (3) a ready-made
    /// <c>&lt;dll-base&gt;.lib</c> in a library path, in order (<c>ReadyMade</c>: used as is, never generated), (4) the RTL
    /// definitions of <see cref="FindDefinition"/>. Not found → E4006.
    /// </summary>
    public static (string? Path, bool ReadyMade, Diagnostic? Diagnostic) Find(
        IFileSystem fs,
        string dllName,
        IEnumerable<string> unitDefinitionFiles,
        IEnumerable<string> declaringUnitDirectories,
        IEnumerable<string> libraryPaths,
        string rtlDirectory,
        Location location,
        TargetInfo? target = null)
    {
        var baseName = BaseName(dllName);
        var fromUnit = unitDefinitionFiles.FirstOrDefault(
            f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase) && fs.FileExists(f));
        if (fromUnit is not null)
        {
            return (fromUnit, false, null);
        }

        string? In(IEnumerable<string> directories, string extension) =>
            directories.Select(d => Path.Combine(d, baseName + extension)).FirstOrDefault(fs.FileExists);

        var flat = PathUtil.Normalize(Path.Combine(rtlDirectory, "..", "winapi", "def"));
        if (In(declaringUnitDirectories, ".def") is { } unitDefinition)
        {
            return (unitDefinition, false, null);
        }

        if (In(libraryPaths, ".lib") is { } library)
        {
            return (PathUtil.Normalize(library), true, null);
        }

        return In([Path.Combine(flat, (target ?? TargetRegistry.Default).ImportDefSubdirectory), flat], ".def") is { } rtlDefinition
            ? (rtlDefinition, false, null)
            : (null, false, Diagnostic.Create("E4006", location, dllName));
    }

    /// <summary>
    /// CMP-1071 step 3: where the SHA-256 of a ready-made import library is recorded after a successful link
    /// (<c>&lt;objdir&gt;\imports\&lt;dll-base&gt;.ext.sha256</c>), so that a rebuilt library with other content relinks.
    /// </summary>
    public static string ReadyMadeHashPath(string objectDirectory, string dllName) =>
        Path.Combine(objectDirectory, "imports", BaseName(dllName) + ".ext.sha256");

    /// <summary>The content hash of a ready-made import library, as recorded in <see cref="ReadyMadeHashPath"/>.</summary>
    public static string ContentHash(IFileSystem fs, string path) => Convert.ToHexStringLower(SHA256.HashData(fs.ReadAllBytes(path)));

    public static string LibraryPath(string objectDirectory, string dllName) =>
        Path.Combine(objectDirectory, "imports", BaseName(dllName) + ".lib");

    /// <param name="target">CMP-1015: the machine of the import library; null → <see cref="TargetRegistry.Default"/>.</param>
    public static ImmutableArray<string> BuildArguments(string definitionPath, string dllName, string libraryPath, TargetInfo? target = null) =>
        ["-m", (target ?? TargetRegistry.Default).DllToolMachine, "-d", definitionPath, "-D", dllName, "-l", libraryPath];

    /// <summary>
    /// Runs <c>llvm-dlltool</c> unless <c>&lt;lib&gt;.sha256</c> holds the SHA-256 of the <c>.def</c> and the
    /// <c>.lib</c> exists (CMP-622). The hash is written only after a successful run and removed on failure.
    /// </summary>
    public async Task<ImportLibraryResult> BuildAsync(string dllName, string definitionPath, string objectDirectory, CancellationToken ct)
    {
        var (lib, job, hash) = Prepare(fs, dllToolPath, dllName, definitionPath, objectDirectory);
        if (job is null)
        {
            return new ImportLibraryResult(lib, Regenerated: false, null);
        }

        var result = await runner.RunAsync(job.ToolPath, job.Arguments, job.WorkingDirectory, ct).ConfigureAwait(false);
        if (result.Failure is null)
        {
            Complete(fs, lib, hash);
        }

        return new ImportLibraryResult(lib, Regenerated: true, result.Failure);
    }

    /// <summary>
    /// CMP-622 as a back-end job (CMP-1134): null <c>Job</c> when the recorded hash matches and the <c>.lib</c> exists; otherwise
    /// the <c>llvm-dlltool</c> job, with the recorded hash already deleted (CMP-1141: no hash may vouch for a library that is being
    /// replaced). <see cref="Complete"/> records <c>Hash</c> after the job succeeded.
    /// </summary>
    public static (string LibraryPath, BackendJob? Job, string Hash) Prepare(
        IFileSystem fs, string dllToolPath, string dllName, string definitionPath, string objectDirectory, TargetInfo? target = null)
    {
        var lib = LibraryPath(objectDirectory, dllName);
        var hashFile = lib + ".sha256";
        var hash = Convert.ToHexStringLower(SHA256.HashData(fs.ReadAllBytes(definitionPath)));
        if (fs.FileExists(lib) && fs.FileExists(hashFile) && fs.ReadAllText(hashFile) == hash)
        {
            return (lib, null, hash);
        }

        fs.Delete(hashFile);
        fs.CreateDirectory(Path.GetDirectoryName(lib)!);
        return (lib, new BackendJob(null, dllName, dllToolPath, BuildArguments(definitionPath, dllName, lib, target), objectDirectory, lib), hash);
    }

    /// <summary>The hash of the <c>.def</c> a successfully generated library was made from (CMP-622).</summary>
    public static void Complete(IFileSystem fs, string libraryPath, string hash) =>
        fs.WriteAllBytes(libraryPath + ".sha256", Encoding.UTF8.GetBytes(hash));

    private static string BaseName(string dllName) => Path.GetFileNameWithoutExtension(dllName);
}
