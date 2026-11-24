using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Units;

/// <summary>
/// LANG-510: locates the source file of one candidate unit name (which candidates are tried is LANG-535, C-164). In order:
/// (1) an <c>in 'path'</c> clause (relative to the main file's directory; a missing file is not found, no fall-through);
/// (2) the units already part of the compilation (open files, project files) whose file is <c>&lt;Name&gt;.pas</c>, in the given
/// order; (3) the main file's directory; (4) each <c>-U</c> directory in order; (5) the target's sub-directory of the RTL root
/// (CMP-1022; skipped when missing), the RTL root, then its other immediate sub-directories in ordinal name order (never the
/// sub-directory of another built-in target), then <c>&lt;rtl root&gt;\..\winapi\src</c> (C-184, the Win32 binding units, like the
/// CMP-620 <c>.def</c> lookup). File names match case-insensitively; dots in a name are part of the file name.
/// A compilation file in a searched directory counts even when it is not on disk. The main file is never a candidate.
/// The caller checks the heading name (E2201) and reports E2202.
/// </summary>
internal sealed class UnitResolver
{
    private readonly IFileSystem _fileSystem;
    private readonly string _mainFile;
    private readonly ImmutableArray<string> _searchPaths;
    private readonly string _rtlRoot;
    private readonly ImmutableArray<string> _compilationFiles;
    private readonly ImmutableHashSet<string> _compilationFileSet;
    private readonly UnitAliasTable _aliases;
    private readonly ImmutableArray<string> _unitScopeNames;
    private readonly TargetInfo _target;
    private ImmutableArray<string> _directories;

    /// <param name="mainFile">Absolute path of the program (or unit) being compiled.</param>
    /// <param name="searchPaths"><c>-U</c> / <c>searchPaths</c> directories, absolute, in order.</param>
    /// <param name="rtlRoot">The RTL root (<c>--rtl</c>), or "" for none.</param>
    /// <param name="compilationFiles">Files already part of the compilation (open files, project files), in priority order.</param>
    /// <param name="aliases">The unit alias table (LANG-533) for <see cref="Find"/>; null → none.</param>
    /// <param name="unitScopeNames">The unit scope names (LANG-535 step 4) for <see cref="Find"/>, in order.</param>
    /// <param name="target">The target whose RTL sub-directory is searched first (CMP-1022); null → <see cref="TargetRegistry.Default"/>.</param>
    public UnitResolver(
        IFileSystem fileSystem, string mainFile, ImmutableArray<string> searchPaths, string rtlRoot, IEnumerable<string> compilationFiles,
        UnitAliasTable? aliases = null, ImmutableArray<string> unitScopeNames = default, TargetInfo? target = null)
    {
        _target = target ?? TargetRegistry.Default;
        _fileSystem = fileSystem;
        _mainFile = PathUtil.Normalize(mainFile);
        _searchPaths = [.. searchPaths.Select(PathUtil.Normalize)];
        _rtlRoot = rtlRoot.Length == 0 ? "" : PathUtil.Normalize(rtlRoot);
        _compilationFiles = [.. compilationFiles.Select(PathUtil.Normalize).Where(p => !PathUtil.AreEqual(p, _mainFile))];
        _compilationFileSet = _compilationFiles.ToImmutableHashSet(PathUtil.Comparer);
        _aliases = aliases ?? UnitAliasTable.Empty;
        _unitScopeNames = unitScopeNames.IsDefault ? [] : unitScopeNames;
    }

    /// <summary>
    /// LANG-535 (C-164): the file of the unit a uses entry <paramref name="name"/> names, or null (E2202). (1) <c>in 'path'</c>
    /// only; (2) <paramref name="name"/> exactly; (3) its alias target; (4) per unit scope name <c>S</c> in order, <c>S.name</c>
    /// exactly, then <c>S.name</c> as an alias. Each candidate is looked up by <see cref="Resolve"/>; the first found wins.
    /// </summary>
    public string? Find(string name, string? inPath = null)
    {
        if (inPath is not null)
        {
            return Resolve(name, inPath);
        }

        if (ExactOrAlias(name) is { } found)
        {
            return found;
        }

        foreach (var scope in _unitScopeNames)
        {
            if (ExactOrAlias(scope + "." + name) is { } scoped)
            {
                return scoped;
            }
        }

        return null;

        string? ExactOrAlias(string candidate) =>
            Resolve(candidate) ?? (_aliases.TryGetCanonical(candidate, out var canonical) ? Resolve(canonical) : null);
    }

    /// <summary>Steps 3–5 (+ winapi\src): the directories searched, in order, without duplicates (the RTL sub-directories are enumerated once).</summary>
    public ImmutableArray<string> Directories
    {
        get
        {
            if (_directories.IsDefault)
            {
                var directories = new List<string> { Path.GetDirectoryName(_mainFile)! };
                directories.AddRange(_searchPaths);
                if (_rtlRoot.Length > 0)
                {
                    // CMP-1022: the target's own sub-directory wins over the root; another target's sub-directory is never searched
                    var own = PathUtil.Normalize(Path.Combine(_rtlRoot, _target.RtlSubdirectory));
                    var foreign = TargetRegistry.BuiltIn.Select(t => t.RtlSubdirectory)
                        .Where(d => !string.Equals(d, _target.RtlSubdirectory, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    directories.Add(own);
                    directories.Add(_rtlRoot);
                    directories.AddRange(SubDirectories(_rtlRoot).Where(d => !foreign.Contains(Path.GetFileName(d))));
                    directories.Add(PathUtil.Normalize(Path.Combine(_rtlRoot, "..", "winapi", "src")));     // C-184, like CMP-620
                }

                _directories = [.. directories.Distinct(PathUtil.Comparer)];
            }

            return _directories;
        }
    }

    /// <summary>The normalised path of unit <paramref name="name"/>'s source file, or null when not found.</summary>
    /// <param name="inPath">The <c>in 'path'</c> text of a program's uses entry (LANG-502), or null.</param>
    public string? Resolve(string name, string? inPath = null)
    {
        if (inPath is not null)
        {
            var path = PathUtil.Normalize(Path.Combine(Path.GetDirectoryName(_mainFile)!, inPath));
            return Exists(path) ? OnDisk(path) : null;
        }

        var fileName = name + ".pas";
        foreach (var file in _compilationFiles)
        {
            if (PathUtil.Comparer.Equals(Path.GetFileName(file), fileName))
            {
                return file;
            }
        }

        foreach (var directory in Directories)
        {
            var path = PathUtil.Normalize(Path.Combine(directory, fileName));
            if (Exists(path))
            {
                return OnDisk(path);
            }

            // C-074: a precompiled unit (its .pcu with the .obj next to it) where the directory has no source of it
            var pcu = PathUtil.Normalize(Path.Combine(directory, name + ".pcu"));
            if (_fileSystem.FileExists(pcu) && _fileSystem.FileExists(Path.ChangeExtension(pcu, ".obj")))
            {
                return OnDisk(pcu);
            }
        }

        return null;
    }

    /// <summary>
    /// C-164: <paramref name="path"/> with its file name spelled as on disk (diagnostic paths, unit identity by path); a compilation
    /// file keeps its given spelling. Only the file name is corrected; directories keep the spelling they were given.
    /// </summary>
    private string OnDisk(string path)
    {
        if (_compilationFileSet.TryGetValue(path, out var given))
        {
            return given;
        }

        var name = Path.GetFileName(path);
        try
        {
            var actual = _fileSystem.EnumerateFiles(Path.GetDirectoryName(path)!, name).FirstOrDefault(f => PathUtil.Comparer.Equals(Path.GetFileName(f), name));
            return actual is null ? path : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(actual));
        }
        catch (IOException)
        {
            return path;
        }
    }

    private bool Exists(string path) =>
        !PathUtil.AreEqual(path, _mainFile) && (_compilationFileSet.Contains(path) || _fileSystem.FileExists(path));

    private IEnumerable<string> SubDirectories(string directory)
    {
        try
        {
            return [.. _fileSystem.EnumerateDirectories(directory).Select(PathUtil.Normalize).OrderBy(Path.GetFileName, StringComparer.Ordinal)];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
