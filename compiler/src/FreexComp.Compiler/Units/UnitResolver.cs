using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Units;

/// <summary>
/// LANG-510: locates the source file of one candidate unit name (which candidates are tried is LANG-535, C-164). In order:
/// (1) an <c>in 'path'</c> clause (relative to the main file's directory; a missing file is not found, no fall-through);
/// (2) the units already part of the compilation (open files, project files) whose file is <c>&lt;Name&gt;.pas</c>, in the given
/// order; (3) the main file's directory; (4) each <c>-U</c> directory in order; (5) the RTL root, then its immediate
/// sub-directories in ordinal name order. File names match case-insensitively; dots in a name are part of the file name.
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
    private ImmutableArray<string> _directories;

    /// <param name="mainFile">Absolute path of the program (or unit) being compiled.</param>
    /// <param name="searchPaths"><c>-U</c> / <c>searchPaths</c> directories, absolute, in order.</param>
    /// <param name="rtlRoot">The RTL root (<c>--rtl</c>), or "" for none.</param>
    /// <param name="compilationFiles">Files already part of the compilation (open files, project files), in priority order.</param>
    public UnitResolver(IFileSystem fileSystem, string mainFile, ImmutableArray<string> searchPaths, string rtlRoot, IEnumerable<string> compilationFiles)
    {
        _fileSystem = fileSystem;
        _mainFile = PathUtil.Normalize(mainFile);
        _searchPaths = [.. searchPaths.Select(PathUtil.Normalize)];
        _rtlRoot = rtlRoot.Length == 0 ? "" : PathUtil.Normalize(rtlRoot);
        _compilationFiles = [.. compilationFiles.Select(PathUtil.Normalize).Where(p => !PathUtil.AreEqual(p, _mainFile))];
        _compilationFileSet = _compilationFiles.ToImmutableHashSet(PathUtil.Comparer);
    }

    /// <summary>Steps 3–5: the directories searched, in order, without duplicates (the RTL sub-directories are enumerated once).</summary>
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
                    directories.Add(_rtlRoot);
                    directories.AddRange(SubDirectories(_rtlRoot));
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
            return Exists(path) ? path : null;
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
                return path;
            }
        }

        return null;
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
