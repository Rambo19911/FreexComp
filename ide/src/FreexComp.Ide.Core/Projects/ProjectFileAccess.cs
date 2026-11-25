using System.Globalization;
using System.Text;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <param name="Project">The loaded project; null on failure.</param>
/// <param name="Error"><see cref="OperationResult.Ok"/>, <c>Msg.Project.NewerFormat</c> or <c>Msg.Project.OpenFailed</c>.</param>
/// <param name="Diagnostic">The serializer's diagnostic on failure.</param>
/// <param name="Position">1-based (line, column) of a parse or validation error; null for file-level errors.</param>
public sealed record ProjectLoadResult(Project? Project, OperationResult Error, Diagnostic? Diagnostic, (int Line, int Column)? Position)
{
    public bool Success => Project is not null;
}

/// <summary>
/// Reads and writes <c>.pproj</c> files with <see cref="ProjectSerializer"/> (IDE-PM-001; the IDE has no reader or
/// writer of its own) and maps its diagnostics to <c>Msg.Project.*</c>.
/// </summary>
public static class ProjectFileAccess
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static ProjectLoadResult Load(string path, IdeFileSystem fileSystem)
    {
        var result = ProjectSerializer.LoadProject(path, new CompilerFileSystemAdapter(fileSystem));
        if (result.Value is { } project && result.Diagnostics.IsEmpty)
        {
            return new(project, OperationResult.Ok, null, null);
        }

        var diagnostic = result.Diagnostics[0];
        if (diagnostic.Code == "E9001")
        {
            // IDE-PM-005: format {0}; this version supports up to {1}.
            var args = diagnostic.Arguments.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "").ToArray();
            return new(null, OperationResult.Fail("Msg.Project.NewerFormat", args), diagnostic, PositionOf(diagnostic));
        }

        return new(null, OpenFailed(diagnostic), diagnostic, diagnostic.Code is "E9003" or "E0003" ? null : PositionOf(diagnostic));
    }

    /// <summary>
    /// <c>Msg.Project.OpenFailed</c> for a serializer diagnostic of a project or group file: the path and, as the reason,
    /// the parser's message with its position in the diagnostic format of compiler-cli.md §2, or the OS reason.
    /// </summary>
    public static OperationResult OpenFailed(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var position = diagnostic.Code is "E9003" or "E0003" ? ((int, int)?)null : PositionOf(diagnostic);
        var reason = position is var (line, column)
            ? string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(diagnostic.Location.Path)}({line},{column}): {diagnostic.Message}")
            : diagnostic.Message;
        return OperationResult.Fail("Msg.Project.OpenFailed", diagnostic.Location.Path, reason);
    }

    /// <summary>Canonical text (UTF-8 without BOM, LF) written atomically; failure → <c>Msg.File.SaveFailed</c>.</summary>
    public static OperationResult Save(Project project, IdeFileSystem fileSystem)
    {
        try
        {
            AtomicFileWriter.Write(fileSystem, project.FilePath, Utf8.GetBytes(ProjectSerializer.Save(project)));
            return OperationResult.Ok;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Msg.File.SaveFailed", project.FilePath, e.Message);
        }
    }

    private static (int, int) PositionOf(Diagnostic diagnostic)
    {
        var start = diagnostic.Location.LineSpan.Start;
        return (start.Line + 1, start.Column + 1);
    }
}
