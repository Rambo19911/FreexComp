using System.Globalization;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService.Formatting;
using CompilerSpan = FreexComp.Compiler.Text.TextSpan;
using IdeTextEdit = FreexComp.Ide.Core.Text.TextEdit;

namespace FreexComp.Ide.Core.Editing;

/// <summary>How a format run ended (IDE-ED-154).</summary>
public enum FormatOutcome
{
    /// <summary>The edits were applied as one undo step.</summary>
    Formatted,

    /// <summary>The text is already formatted.</summary>
    NoChanges,

    /// <summary>The formatter refused the file (a syntax error, CMP-1438); nothing changed.</summary>
    Refused,

    /// <summary>Not a writable Pascal document, or the text changed while the formatter ran; nothing changed.</summary>
    NotApplied,
}

/// <summary>The outcome of <see cref="IFormatterService.FormatAsync"/>; <see cref="RefusalReason"/> with <see cref="FormatOutcome.Refused"/>.</summary>
public sealed record FormatRun(FormatOutcome Outcome, string? RefusalReason = null);

/// <summary>
/// The source formatter of the IDE (architecture.md <c>IFormatterService</c>, IDE-ED-156): Pascal documents through the
/// built-in Object Pascal formatter (<see cref="PascalFormatter"/>, language-service.md §10a).
/// </summary>
public interface IFormatterService
{
    /// <summary>Whether <paramref name="document"/> can be formatted: a writable Object Pascal document.</summary>
    bool CanFormat(TextDocumentViewModel document);

    /// <summary>
    /// Formats the document (or the statements and declarations <paramref name="range"/> touches) off the UI thread with the
    /// <c>.pasformat</c> profile of <paramref name="filePath"/> (default: the document's file; untitled → built-in style) and
    /// applies the edits as one undo step. A profile that is not valid JSON adds one Messages warning row (IDE-ED-155).
    /// </summary>
    Task<FormatRun> FormatAsync(TextDocumentViewModel document, TextSelection? range, string? filePath = null);
}

/// <summary>
/// <see cref="IFormatterService"/> and the commands <c>edit.formatDocument</c> / <c>edit.formatSelection</c> (IDE-ED-154…156):
/// the refusal and no-change status messages are the commands'; format-on-save (IDE-SET-084) reports in the save.
/// </summary>
public sealed class FormatterService : IFormatterService
{
    public const string FormatDocumentCommandId = "edit.formatDocument";
    public const string FormatSelectionCommandId = "edit.formatSelection";

    /// <summary>The profile file name of CMP-1452.</summary>
    public const string ProfileFileName = ".pasformat";

    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IFileSystem _fileSystem;
    private readonly IMessagesService _messages;
    private readonly IStatusService _status;
    private readonly Func<TextDocumentViewModel?> _activeText;

    /// <param name="activeText">The text of the active document (the commands' target); null when none.</param>
    public FormatterService(IFileSystem fileSystem, IMessagesService messages, IStatusService status, Func<TextDocumentViewModel?> activeText)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(activeText);
        _fileSystem = fileSystem;
        _messages = messages;
        _status = status;
        _activeText = activeText;
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(FormatDocumentCommandId, new Handler(this, selection: false));
        commands.RegisterHandler(FormatSelectionCommandId, new Handler(this, selection: true));
    }

    public bool CanFormat(TextDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Language.Id == "pascal" && !document.Buffer.IsReadOnly;
    }

    public async Task<FormatRun> FormatAsync(TextDocumentViewModel document, TextSelection? range, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!CanFormat(document))
            return new FormatRun(FormatOutcome.NotApplied);

        var buffer = document.Buffer;
        var version = buffer.Version;
        var text = buffer.Text;
        var path = filePath ?? document.FilePath;
        CompilerSpan? span = range is { } r ? new CompilerSpan(r.Start, r.End - r.Start) : null;
        var (result, invalidProfile) = await Task.Run(() =>
        {
            var (profile, invalid) = LoadProfile(path);
            return (PascalFormatter.Format(FreexComp.Compiler.SourceText.From(path ?? Path.GetFullPath(document.Title), text), profile, span, CancellationToken.None), invalid);
        });

        if (invalidProfile is not null) // IDE-ED-155: one row per run
        {
            var message = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Format_ProfileInvalid, invalidProfile);
            _messages.AddToolMessage(MessageItem.ToolRow(MessageSeverity.Warning, message, new MessageLocation(invalidProfile, 1, 1, 1, 1)));
        }

        if (buffer.Version != version || !CanFormat(document))
            return new FormatRun(FormatOutcome.NotApplied); // edited (or made read-only) meanwhile: the edits no longer fit
        if (result.Refused)
            return new FormatRun(FormatOutcome.Refused, result.RefusalReason);
        if (result.Edits.IsEmpty)
            return new FormatRun(FormatOutcome.NoChanges);

        return document.ApplyFormatEdits(Merge(result.Edits)) ? new FormatRun(FormatOutcome.Formatted) : new FormatRun(FormatOutcome.NotApplied);
    }

    /// <summary>
    /// The formatter's edits (sorted, non-overlapping) as buffer edits; edits that touch (one ends where the next starts — a
    /// line break inserted before a word whose case changes) become one edit, since the buffer refuses two at one offset.
    /// </summary>
    private static List<IdeTextEdit> Merge(IReadOnlyList<FreexComp.LanguageService.Formatting.TextEdit> edits)
    {
        var merged = new List<IdeTextEdit>(edits.Count);
        foreach (var edit in edits)
        {
            if (merged.Count > 0 && merged[^1] is var last && last.Offset + last.Length == edit.Span.Start)
                merged[^1] = new IdeTextEdit(last.Offset, last.Length + edit.Span.Length, last.NewText + edit.NewText);
            else
                merged.Add(new IdeTextEdit(edit.Span.Start, edit.Span.Length, edit.NewText));
        }

        return merged;
    }

    /// <summary>The status-bar text of a refusal (<c>Msg.Format.Refused</c>).</summary>
    public static string RefusedMessage(FormatRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return string.Format(CultureInfo.CurrentCulture, Strings.Msg_Format_Refused, run.RefusalReason);
    }

    /// <summary>
    /// CMP-1452 through <see cref="PascalFormatter.LoadProfile"/>; the nearest <c>.pasformat</c> that gives the built-in
    /// style (no <see cref="FormatProfile.SourcePath"/>) is not valid JSON and is returned as <c>Invalid</c>.
    /// </summary>
    private (FormatProfile Profile, string? Invalid) LoadProfile(string? sourcePath)
    {
        if (sourcePath is null || !Path.IsPathFullyQualified(sourcePath))
            return (FormatProfile.Default, null); // untitled: the built-in style
        var profile = PascalFormatter.LoadProfile(sourcePath);
        if (profile.SourcePath is not null)
            return (profile, null);
        for (var dir = Path.GetDirectoryName(sourcePath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, ProfileFileName);
            if (_fileSystem.FileExists(candidate))
                return (profile, candidate);
        }

        return (profile, null);
    }

    private sealed class Handler(FormatterService owner, bool selection) : ICommandHandler
    {
        public bool CanExecute(string? argument) =>
            owner._activeText() is { } document && owner.CanFormat(document) && (!selection || !document.Selection.IsEmpty);

        public async Task ExecuteAsync(string? argument)
        {
            if (!CanExecute(argument) || owner._activeText() is not { } document)
                return;
            var run = await owner.FormatAsync(document, selection ? document.Selection : null);
            switch (run.Outcome)
            {
                case FormatOutcome.Refused:
                    owner._status.ShowMessage(RefusedMessage(run), StatusMessageDuration);
                    break;
                case FormatOutcome.NoChanges:
                    owner._status.ShowMessage(Strings.Msg_Format_NoChanges, StatusMessageDuration);
                    break;
            }
        }
    }
}
