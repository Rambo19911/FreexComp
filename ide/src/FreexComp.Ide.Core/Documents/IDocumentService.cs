using System.Collections.ObjectModel;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Documents;

public sealed class DocumentEventArgs(DocumentViewModel document) : EventArgs
{
    public DocumentViewModel Document { get; } = document;
}

/// <summary><see cref="IDocumentService.DocumentClosing"/>: a handler sets <see cref="Cancel"/> to keep the document open.</summary>
public sealed class DocumentClosingEventArgs(DocumentViewModel document) : EventArgs
{
    public DocumentViewModel Document { get; } = document;

    public bool Cancel { get; set; }
}

/// <summary>
/// The open documents (architecture.md §4, §5; IDE-ARCH-020…023): tab order, the active document and the
/// most-recently-used order; loading and saving through <c>IFileSystem</c>. Feeds the status bar's editor fields for
/// the active text document. UI thread only. Lives outside <c>Services</c> because it owns view-models (IDE-ARCH-004).
/// </summary>
public interface IDocumentService
{
    /// <summary>Open documents in tab order.</summary>
    ReadOnlyObservableCollection<DocumentViewModel> Documents { get; }

    /// <summary>Open documents, most recently activated first.</summary>
    IReadOnlyList<DocumentViewModel> MostRecentlyUsed { get; }

    DocumentViewModel? ActiveDocument { get; }

    event EventHandler<DocumentEventArgs>? DocumentOpened;

    event EventHandler<DocumentClosingEventArgs>? DocumentClosing;

    event EventHandler<DocumentEventArgs>? DocumentClosed;

    event EventHandler? ActiveDocumentChanged;

    event EventHandler<DocumentEventArgs>? DocumentSaved;

    /// <summary>
    /// Opens <paramref name="path"/> and activates it; a file that is already open is only activated (IDE-ARCH-020).
    /// Failures: <c>Msg.File.NotFound</c> [path], <c>Msg.File.Binary</c> [path], <c>Msg.File.OpenFailed</c> [path, reason].
    /// </summary>
    Task<OperationResult<DocumentViewModel>> OpenAsync(string path);

    /// <summary>A new untitled unit <c>Unit&lt;N&gt;.pas</c>, opened and activated (IDE-P1-033).</summary>
    TextDocumentViewModel NewUntitledUnit();

    void Activate(DocumentViewModel document);

    /// <summary>
    /// Activates the next/previous document in MRU order (<paramref name="mru"/>) or tab order (IDE-P1-038).
    /// </summary>
    void ActivateNext(bool forward, bool mru);

    /// <summary>
    /// Closes without asking (prompts for modified documents: <c>file.close</c>); false when a
    /// <see cref="DocumentClosing"/> handler canceled. The most recently used remaining document becomes active.
    /// </summary>
    bool Close(DocumentViewModel document);

    /// <summary>
    /// <c>file.close</c> (IDE-P1-035): a modified document asks <c>Msg.File.SaveChangesOne</c> first — <c>Save</c> saves
    /// (<see cref="SaveAsync"/>) and closes only when that succeeded, <c>Don't Save</c> closes, <c>Cancel</c> keeps it.
    /// True when the document was closed.
    /// </summary>
    Task<bool> CloseAsync(DocumentViewModel document);

    /// <summary>
    /// <c>file.closeAll</c> / <c>file.closeOthers</c> (IDE-P1-036): every document except <paramref name="except"/>,
    /// after one <see cref="PromptSaveChangesAsync"/>. True when they were closed.
    /// </summary>
    Task<bool> CloseAllAsync(DocumentViewModel? except = null);

    /// <summary>
    /// The once-per-operation question for the modified ones among <paramref name="documents"/> (IDE-P1-036,
    /// UI-DLG-060…063): none → true; one → <c>Msg.File.SaveChangesOne</c>; several → the Unsaved Changes dialog. True
    /// when the operation may go on (chosen documents saved, the rest discarded); false on Cancel, a failed save or a
    /// canceled Save As picker — documents saved until then stay saved.
    /// </summary>
    Task<bool> PromptSaveChangesAsync(IReadOnlyList<DocumentViewModel> documents);

    /// <summary>
    /// <c>file.save</c> of one document (phase1-shell.md §8): the save pipeline of <see cref="SaveAsAsync"/> plus its
    /// questions — <c>Msg.File.EncodingLossy</c> (<c>Save as UTF-8</c> switches the encoding and saves) and
    /// <c>Msg.File.SaveFailed</c> (<c>Retry</c> repeats, <c>Save As…</c> picks another path, <c>Cancel</c> gives up).
    /// An untitled document needs the Save As picker (IDE-P1-034). True when it was written.
    /// </summary>
    Task<bool> SaveAsync(DocumentViewModel document);

    /// <summary>
    /// <c>file.saveAll</c> (IDE-P1-085): every modified document in tab order through <see cref="SaveAsync"/>'s
    /// questions; a canceled Save As picker skips that document, a failure answered with Cancel stops (false). Status
    /// <c>Msg.File.SavedAll</c> with the number of files written, or <c>Msg.File.NothingToSave</c>.
    /// </summary>
    Task<bool> SaveAllAsync();

    /// <summary>
    /// The save pipeline without questions (IDE-P1-080, 081, 084): trim / final line break per settings (one undo
    /// step), encode, atomic write to <paramref name="path"/>, which becomes the document's file (IDE-P1-034); then
    /// <see cref="DocumentSaved"/> and status <c>Msg.File.Saved</c>. Failures: <c>Msg.File.EncodingLossy</c>
    /// [encoding display name] (nothing written); <c>Msg.File.SaveFailed</c> [path, reason], also for a path open in
    /// another document.
    /// </summary>
    Task<OperationResult> SaveAsAsync(TextDocumentViewModel document, string path);
}
