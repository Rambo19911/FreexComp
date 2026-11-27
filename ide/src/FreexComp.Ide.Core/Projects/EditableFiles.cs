using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The files a multi-file operation may edit (IDE-REFAC-005; the rule of IDE-PM-133 and IDE-LIB-008): one helper for
/// the unit rename and the refactoring engine.
/// </summary>
internal static class EditableFiles
{
    /// <summary>
    /// With a project open: a project member (the companion form of a member unit is one), or a file under the project
    /// directory or under an effective search path of the active configuration that is not a library source. Without a
    /// project: only a file that is open as a document — the <c>.form</c> of an open form document is one — and is not a
    /// protected library source. UI thread; no disk access.
    /// </summary>
    public static bool MayEdit(string path, IProjectService projects, Func<string, bool> isLibrarySource, IDocumentService documents)
    {
        if (projects.Definition is not { } project)
        {
            return documents.Documents.OfType<TextDocumentViewModel>()
                .Any(d => !d.IsLibraryProtected && (string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)
                    || d is FormDocumentViewModel form && string.Equals(form.FormText.FilePath, path, StringComparison.OrdinalIgnoreCase)));
        }

        if (projects.IsMember(path))
            return true;
        if (isLibrarySource(path))
            return false;
        // The search paths the active configuration is built with (CMP-1160): project level, then its chain.
        var searchPaths = ConfigurationEdits.Effective(project, project.ActiveConfiguration)?.SearchPaths ?? project.SearchPaths;
        return IsInside(path, project.Directory)
            || searchPaths.Any(s => IsInside(path, Path.GetFullPath(Path.Combine(project.Directory, s.Replace('/', Path.DirectorySeparatorChar)))));
    }

    /// <summary>True when <paramref name="path"/> lies in <paramref name="directory"/> or below it.</summary>
    public static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
