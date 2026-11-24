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
    /// <c>&lt;rtl root&gt;\..\winapi\def\&lt;dll-base&gt;.def</c>. Not found → E4006. Names compare case-insensitively.
    /// </summary>
    public static (string? Path, Diagnostic? Diagnostic) FindDefinition(
        IFileSystem fs,
        string dllName,
        IEnumerable<string> unitDefinitionFiles,
        IEnumerable<string> declaringUnitDirectories,
        string rtlDirectory,
        Location location)
    {
        var baseName = BaseName(dllName);
        var fromUnit = unitDefinitionFiles.FirstOrDefault(
            f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase) && fs.FileExists(f));
        if (fromUnit is not null)
        {
            return (fromUnit, null);
        }

        var candidates = declaringUnitDirectories.Append(PathUtil.Normalize(Path.Combine(rtlDirectory, "..", "winapi", "def")));
        foreach (var dir in candidates)
        {
            var path = Path.Combine(dir, baseName + ".def");
            if (fs.FileExists(path))
            {
                return (path, null);
            }
        }

        return (null, Diagnostic.Create("E4006", location, dllName));
    }

    public static string LibraryPath(string objectDirectory, string dllName) =>
        Path.Combine(objectDirectory, "imports", BaseName(dllName) + ".lib");

    public static ImmutableArray<string> BuildArguments(string definitionPath, string dllName, string libraryPath) =>
        ["-m", "i386:x86-64", "-d", definitionPath, "-D", dllName, "-l", libraryPath];

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
        IFileSystem fs, string dllToolPath, string dllName, string definitionPath, string objectDirectory)
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
        return (lib, new BackendJob(null, dllName, dllToolPath, BuildArguments(definitionPath, dllName, lib), objectDirectory, lib), hash);
    }

    /// <summary>The hash of the <c>.def</c> a successfully generated library was made from (CMP-622).</summary>
    public static void Complete(IFileSystem fs, string libraryPath, string hash) =>
        fs.WriteAllBytes(libraryPath + ".sha256", Encoding.UTF8.GetBytes(hash));

    private static string BaseName(string dllName) => Path.GetFileNameWithoutExtension(dllName);
}
