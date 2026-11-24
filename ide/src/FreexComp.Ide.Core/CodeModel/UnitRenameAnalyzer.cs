using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>What the language service says about renaming a unit (IDE-PM-130…135).</summary>
public enum UnitRenameOutcome
{
    /// <summary>IDE-PM-135: the unit cannot be analysed; the rename goes on with the lexer and updates no reference.</summary>
    Fallback,

    /// <summary>IDE-PM-134: the rename is refused; <see cref="UnitRenamePlan.Error"/> is the service's sentence.</summary>
    Refused,

    /// <summary>IDE-PM-131: <see cref="UnitRenamePlan.Edits"/> is the edit set.</summary>
    Edits,
}

/// <summary>
/// The edit set of a unit rename: one <see cref="DocumentEdit"/> per file in path order against the snapshot's text
/// (<see cref="Texts"/>, by path), the unit's own file under its old path; <see cref="Heading"/> is the span of the name
/// in the heading of the unit's own file.
/// </summary>
public sealed record UnitRenamePlan(UnitRenameOutcome Outcome, string? Error, ImmutableArray<DocumentEdit> Edits,
    FileRename? FileRename, TextSpan Heading, ImmutableDictionary<string, string> Texts)
{
    public static UnitRenamePlan Fallback { get; } =
        new(UnitRenameOutcome.Fallback, null, [], null, default, ImmutableDictionary<string, string>.Empty);

    public static UnitRenamePlan Refused(string error) =>
        new(UnitRenameOutcome.Refused, error, [], null, default, ImmutableDictionary<string, string>.Empty);
}

/// <summary>
/// IDE-PM-130: asks the language service's rename (<see cref="RenameService"/>, CMP-750…752) for the edit set of a unit
/// rename on the project compilation with the unsaved buffers, on the code-model worker. Nothing is written here.
/// </summary>
public sealed class UnitRenameAnalyzer(CodeModelService codeModel, IToolchainLocator toolchain)
{
    /// <summary>
    /// The plan for renaming the unit in <paramref name="path"/> (whose heading names <paramref name="oldName"/>) to
    /// <paramref name="newName"/>. Called on the UI thread; completes there.
    /// </summary>
    public async Task<UnitRenamePlan> AnalyzeAsync(string path, string oldName, string newName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        // IDE-PM-135: no code model without the toolchain's library sources or without a project compilation.
        if (toolchain.Resolve().RtlDir is null || codeModel.Synchronize() is null)
            return UnitRenamePlan.Fallback;
        var document = codeModel.Workspace.GetDocument(path);
        try
        {
            return await codeModel.Worker.RunAsync("unitRename", token => Analyze(document, oldName, newName, token), ct);
        }
        catch (IOException)
        {
            return UnitRenamePlan.Fallback; // the project cannot be loaded as a compilation
        }
    }

    /// <summary>The files were written or renamed by the rename: the workspace reads them again.</summary>
    public void NotifyChanged(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
            codeModel.Workspace.NotifyFileChangedOnDisk(path);
    }

    /// <summary>IDE-LIB-008: a source of the toolchain's libraries (RTL, UI framework, data access).</summary>
    public bool IsLibrarySource(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var paths = toolchain.Resolve();
        return new[] { paths.RtlDir, paths.FrameworkDir, paths.DataDir }.OfType<string>()
            .Any(dir => PathUtil.Normalize(path).StartsWith(PathUtil.Normalize(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static UnitRenamePlan Analyze(Document document, string oldName, string newName, CancellationToken ct)
    {
        // The heading as IDE-PM-071 finds it: the first tokens of the file. A heading that names something else than the
        // file is left alone, as before.
        var tokens = Lexer.Tokenize(document.SyntaxTree.Text);
        if (tokens.Length < 2
            || tokens[0].Kind is not (SyntaxKind.UnitKeyword or SyntaxKind.ProgramKeyword or SyntaxKind.LibraryKeyword or SyntaxKind.PackageKeyword)
            || tokens[1].Kind != SyntaxKind.IdentifierToken)
            return UnitRenamePlan.Fallback;
        var position = tokens[1].Span.Start;
        var info = RenameService.GetRenameInfo(document, position, ct);
        if (!info.CanRename)
            return info.Span.Length == 0 ? UnitRenamePlan.Fallback : UnitRenamePlan.Refused(info.Reason ?? "");
        if (!string.Equals(info.CurrentName, oldName, StringComparison.OrdinalIgnoreCase))
            return UnitRenamePlan.Fallback;
        var result = RenameService.Rename(document, position, newName, ct);
        if (!result.Success)
            return UnitRenamePlan.Refused(result.Error ?? "");
        var texts = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in result.Edits)
            texts[edit.Path] = document.Compilation.GetSyntaxTree(edit.Path).Text.Text;
        return new UnitRenamePlan(UnitRenameOutcome.Edits, null, result.Edits, result.FileRename, info.Span, texts.ToImmutable());
    }
}
