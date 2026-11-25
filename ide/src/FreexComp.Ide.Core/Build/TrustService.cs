using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Build;

/// <summary>The answer of the trust check of a build (IDE-TRUST-003…009).</summary>
/// <param name="Proceed">false: the user cancelled; nothing is built, run or debugged.</param>
/// <param name="Events">The policy of this one build: <c>RequireTrust</c>, or <c>Skip</c> after "Build Without Events".</param>
public sealed record TrustCheckResult(bool Proceed, EventsPolicy Events);

/// <summary>
/// Trust in the commands of a project (phase8 trust-prompt.md §2): owner of the one trust list of the process and of the
/// question asked before a build. UI thread only.
/// </summary>
public interface ITrustService
{
    /// <summary>IDE-TRUST-001: the per-user trust list, <c>&lt;roaming&gt;\trust.json</c>; read from disk at every use.</summary>
    ITrustStore Store { get; }

    /// <summary>IDE-TRUST-013: the question of <see cref="CheckBeforeBuildAsync"/> is on screen.</summary>
    bool IsPromptOpen { get; }

    /// <summary>
    /// IDE-TRUST-004…009: looks for projects of the build that have commands and are not trusted; none → proceed without
    /// any UI; else the dialog <c>TrustProject</c> decides.
    /// </summary>
    Task<TrustCheckResult> CheckBeforeBuildAsync(ProjectModel project);
}

/// <inheritdoc cref="ITrustService"/>
public sealed class TrustService : ITrustService
{
    /// <summary>File name of the trust list in the roaming directory (IDE-TRUST-001).</summary>
    public const string FileName = "trust.json";

    private readonly FreexComp.Compiler.IFileSystem _files;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IClipboardService _clipboard;
    private readonly ILayoutService? _layout;

    public TrustService(IUserPaths paths, IFileSystem fileSystem, IDialogService dialogs, IFileDialogService pickers,
        IClipboardService clipboard, ILayoutService? layout)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _files = new CompilerFileSystemAdapter(fileSystem);
        _dialogs = dialogs;
        _pickers = pickers;
        _clipboard = clipboard;
        _layout = layout;
        Store = new FileTrustStore(_files, Path.Combine(paths.RoamingDirectory, FileName));
    }

    public ITrustStore Store { get; }

    public bool IsPromptOpen { get; private set; }

    public async Task<TrustCheckResult> CheckBeforeBuildAsync(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = project.FilePath;
        Task<System.Collections.Immutable.ImmutableArray<UntrustedProject>> Find() =>
            Task.Run(() => ProjectTrust.FindUntrusted(_files, path, null, Store));

        var untrusted = await Find();
        if (untrusted.IsEmpty)
            return new TrustCheckResult(true, EventsPolicy.RequireTrust);

        var prompt = new TrustProjectViewModel(project.Name, untrusted, Store, Find, HashOf, _dialogs, _pickers, _clipboard, _layout);
        IsPromptOpen = true;
        TrustPromptOutcome outcome;
        try
        {
            outcome = await _dialogs.ShowAsync(prompt);
        }
        finally
        {
            IsPromptOpen = false;
        }

        return outcome switch
        {
            TrustPromptOutcome.Trusted => new TrustCheckResult(true, EventsPolicy.RequireTrust),
            TrustPromptOutcome.BuildWithoutEvents => new TrustCheckResult(true, EventsPolicy.Skip),
            _ => new TrustCheckResult(false, EventsPolicy.RequireTrust),
        };
    }

    /// <summary>
    /// IDE-EVT-008: the hash of the project file's commands (it covers the lists of configurations, also those without a
    /// command), when the non-empty lists of the file are still the ones the prompt shows; else null.
    /// </summary>
    private string? HashOf(UntrustedProject shown)
    {
        var load = ProjectSerializer.LoadProject(shown.ProjectPath, _files);
        if (load.Value is not { } project || load.Diagnostics.Any(d => d.Severity == FreexComp.Compiler.DiagnosticSeverity.Error))
            return null;
        var lists = ProjectTrust.CommandLists(project).Where(list => !list.Commands.IsEmpty).ToList();
        var listed = shown.Lists.IsDefault ? [] : shown.Lists;
        var same = lists.Count == listed.Length
            && lists.Zip(listed).All(p => p.First.Name == p.Second.Name && p.First.Commands.SequenceEqual(p.Second.Commands, StringComparer.Ordinal));
        return same ? ProjectTrust.CommandsHash(project) : null;
    }
}
