using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>Icon kind of a message box (UI-DLG-022): the word in parentheses of the spec's "Kind" column.</summary>
public enum MessageKind
{
    Question,
    Info,
    Warning,
    Error,
}

/// <summary>
/// A message of kind Dialog (states-and-messages.md): its icon kind and button ids, default first (UI-DLG-024).
/// Text is the resource <c>Msg_…</c>; the title is <c>&lt;MsgKey&gt;_Title</c> or the product name (UI-DLG-023).
/// </summary>
public sealed record MessageDefinition(string Id, MessageKind Kind, IReadOnlyList<string> Buttons)
{
    public string ResourceKey => Id.Replace('.', '_');

    public string Text => Strings.ResourceManager.GetString(ResourceKey, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"No resource {ResourceKey}.");

    public string Title => Strings.ResourceManager.GetString(ResourceKey + "_Title", CultureInfo.CurrentUICulture) ?? Strings.App_ProductName;
}

/// <summary>The code form of every Dialog row of states-and-messages.md (test <c>MessageDefinitionsTests.DialogRowsMatchSpec</c>).</summary>
public static class MessageDefinitions
{
    private const MessageKind Q = MessageKind.Question;
    private const MessageKind W = MessageKind.Warning;
    private const MessageKind E = MessageKind.Error;

    public static readonly IReadOnlyList<MessageDefinition> All =
    [
        new("Msg.App.InternalError", E, ["Continue", "CopyDetails"]),
        new("Msg.File.SaveChangesOne", Q, ["Save", "DontSave", "Cancel"]),
        new("Msg.File.SaveChangesMany", Q, ["SaveSelected", "DontSave", "Cancel"]),
        new("Msg.File.SaveFailed", E, ["Retry", "SaveAs", "Cancel"]),
        new("Msg.File.OpenFailed", E, ["OK"]),
        new("Msg.File.NotFound", E, ["OK"]),
        new("Msg.File.RecentNotFound", Q, ["Remove", "Keep"]),
        new("Msg.File.TooLarge", Q, ["Open", "Cancel"]),
        new("Msg.File.Binary", E, ["OK"]),
        new("Msg.Diff.Binary", E, ["OK"]), // IDE-DIFF-028
        new("Msg.File.OverwriteExisting", Q, ["Cancel", "Replace"]),
        new("Msg.File.MakeWritableFailed", E, ["OK"]),
        new("Msg.File.ConsoleFailed", E, ["OK"]),
        new("Msg.File.EncodingLossy", Q, ["SaveAsUTF8", "Cancel"]),
        new("Msg.File.RevertConfirm", Q, ["Cancel", "Revert"]),
        new("Msg.File.LibraryCopyExists", Q, ["AddAndOpen", "Cancel"]),
        new("Msg.File.LibraryCopyFailed", E, ["OK"]),
        new("Msg.Project.CreateFailed", E, ["OK"]),
        new("Msg.Project.OpenFailed", E, ["OK", "OpenAsText"]),
        new("Msg.Project.NewerFormat", E, ["OK"]),
        new("Msg.Project.RemoveFileConfirm", Q, ["Remove", "Cancel"]),
        new("Msg.Project.RemoveFilesConfirm", Q, ["Remove", "Cancel"]),
        new("Msg.Platform.RemoveConfirm", Q, ["Cancel", "Remove"]), // IDE-PLAT-014: Cancel is the default
        new("Msg.Project.DeleteFileConfirm", W, ["Cancel", "Delete"]),
        new("Msg.Project.DeleteFailed", E, ["OK"]),
        new("Msg.Project.RemoveMainSource", E, ["OK"]),
        new("Msg.Project.DuplicateUnitName", E, ["OK"]),
        new("Msg.Project.RenameFailed", E, ["OK"]),
        new("Msg.Project.RenameConflict", E, ["OK"]),
        new("Msg.Refactoring.Blocked", W, ["OK"]), // IDE-REFAC-006
        new("Msg.Refactoring.ApplyFailed", E, ["OK"]), // IDE-REFAC-008
        new("Msg.Refactoring.RestoreFailed", E, ["OK"]),
        new("Msg.Refactoring.Failed", E, ["OK"]), // IDE-REFAC-002
        new("Msg.Refactoring.Refused", MessageKind.Info, ["OK"]), // IDE-REFAC-031
        new("Msg.Refactoring.UndoBlocked", W, ["OK"]), // IDE-REFAC-021
        new("Msg.Refactoring.UndoBlockedLocal", Q, ["UndoInThisFile", "Cancel"]), // IDE-REFAC-024
        new("Msg.Refactoring.UndoFailed", E, ["OK"]), // IDE-REFAC-021
        new("Msg.Project.NewFolderFailed", E, ["OK"]),
        new("Msg.Project.MoveExists", E, ["OK"]),
        new("Msg.Project.MoveFailed", E, ["OK"]),
        new("Msg.Project.ChangedOnDisk", Q, ["Reload", "Keep"]),
        new("Msg.Build.ToolchainMissing", E, ["OpenOptions", "Cancel"]),
        new("Msg.Build.OutputLocked", E, ["StopAndBuild", "Cancel"]),
        new("Msg.Trust.SaveFailed", E, ["OK"]),
        new("Msg.Group.SaveFailed", E, ["OK"]),
        new("Msg.Group.SaveQuestion", Q, ["Save", "DontSave", "Cancel"]),
        new("Msg.Dependencies.SaveFailed", E, ["OK"]),
        new("Msg.Run.BuildFailedRunPrevious", Q, ["Cancel", "RunLastBuild"]),
        new("Msg.Run.NoExecutable", E, ["OK"]),
        new("Msg.Run.StartFailed", E, ["OK"]),
        new("Msg.Run.WorkingDirMissing", E, ["RunParameters", "Cancel"]),
        new("Msg.Run.AlreadyRunning", Q, ["Restart", "Cancel"]),
        new("Msg.Run.StopOnExit", Q, ["StopAndExit", "Cancel"]),
        new("Msg.Run.StopOnClose", Q, ["StopAndClose", "Cancel"]),
        new("Msg.Run.KillFailed", E, ["OK"]),
        new("Msg.Debug.StartFailed", E, ["RunWithoutDebugging", "Cancel"]),
        new("Msg.Debug.NoDebugInfo", Q, ["RunAnyway", "ProjectOptions", "Cancel"]),
        new("Msg.Debug.Exception", W, ["Break", "Continue"]),
        new("Msg.Debug.DeleteAllBreakpoints", Q, ["Delete", "Cancel"]),
        new("Msg.Designer.FormFileMissing", E, ["OK"]),
        new("Msg.Designer.HandlerSignatureMismatch", E, ["OK"]),
        new("Msg.Designer.MetadataMissing", E, ["OpenOptions", "Cancel"]),
        new("Msg.Designer.CodeSyncConflict", E, ["OK"]),
        new("Msg.Designer.DescendantNotUpdated", E, ["OK"]),
        new("Msg.Options.ResetKeysConfirm", Q, ["Reset", "Cancel"]),
        new("Msg.Options.ResetColorsConfirm", Q, ["Reset", "Cancel"]),
        new("Msg.ProjectOptions.ConfigDeleteConfirm", Q, ["Delete", "Cancel"]),
    ];

    /// <summary>
    /// The message boxes specified outside the Dialog tables (dialogs.md §27.1, kind "Message box"): shown like the others,
    /// not part of <see cref="All"/> (which mirrors the Dialog rows of the spec).
    /// </summary>
    public static readonly IReadOnlyList<MessageDefinition> MessageBoxes =
    [
        new("Msg.MenuDesigner.DeleteWithSubItems", W, ["Cancel", "Delete"]), // UI-DLG-251: destructive, Cancel is the default
    ];

    private static readonly Dictionary<string, MessageDefinition> ById = All.Concat(MessageBoxes).ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static MessageDefinition? TryGet(string id) => ById.GetValueOrDefault(id);

    /// <exception cref="ArgumentException">Not a message of kind Dialog.</exception>
    public static MessageDefinition Get(string id) =>
        TryGet(id) ?? throw new ArgumentException($"{id} is not a message of kind Dialog.", nameof(id));

    /// <summary>
    /// UI-DLG-024: the label reduced to PascalCase letters (and digits): apostrophes and ellipses dropped, every word
    /// capitalized (<c>Don't Save</c> → <c>DontSave</c>, <c>Save as UTF-8</c> → <c>SaveAsUTF8</c>).
    /// </summary>
    public static string ButtonId(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var id = new StringBuilder();
        var wordStart = true;
        foreach (var c in label.Replace("'", "", StringComparison.Ordinal))
        {
            if (char.IsLetterOrDigit(c))
            {
                id.Append(wordStart ? char.ToUpperInvariant(c) : c);
                wordStart = false;
            }
            else
            {
                wordStart = true;
            }
        }

        return id.ToString();
    }

    /// <summary>The button's label, resource <c>Button_&lt;id&gt;</c>.</summary>
    public static string ButtonLabel(string buttonId) =>
        Strings.ResourceManager.GetString("Button_" + buttonId, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"No resource Button_{buttonId}.");
}
