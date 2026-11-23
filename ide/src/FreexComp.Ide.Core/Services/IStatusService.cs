namespace FreexComp.Ide.Core.Services;

/// <summary>Line-ending state of the active document (main-window.md §5 R6).</summary>
public enum LineEndingKind
{
    CrLf,
    Lf,
    Mixed,
}

/// <summary>
/// What the active text editor shows in the status bar (main-window.md §5 R2–R8): caret (1-based line and column),
/// selection length in characters and the number of lines it spans, insert/overwrite, indentation, line ending,
/// encoding id (<c>files.defaultEncoding</c> values: <c>utf-8</c>, <c>utf-8-bom</c>, <c>utf-16le</c>, <c>utf-16be</c>,
/// <c>windows-1252</c>) and the language's display name.
/// </summary>
public sealed record EditorStatus(
    int Line,
    int Column,
    int SelectedChars,
    int SelectedLines,
    bool Overwrite,
    bool InsertSpaces,
    int IndentSize,
    LineEndingKind LineEnding,
    string EncodingId,
    string Language);

/// <summary>Status-bar state (architecture.md §4): the message field and the active editor's fields. UI thread only.</summary>
public interface IStatusService
{
    /// <summary>The message shown in the message field, or null.</summary>
    string? Message { get; }

    /// <summary>The active text editor's fields; null while no text editor view is the active document view (UI-WIN-040).</summary>
    EditorStatus? Editor { get; }

    /// <summary>
    /// UI-DSG-130: the selection text of the active Design view (<c>StatusBar.DesignerSelection</c>); null while no Design
    /// view is the active document view.
    /// </summary>
    string? DesignerSelection { get; }

    /// <summary>Raised when <see cref="Message"/>, <see cref="Editor"/> or <see cref="DesignerSelection"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Shows <paramref name="text"/> for <paramref name="duration"/> (UI-MSG-003 "Status"), replacing the current message.</summary>
    void ShowMessage(string text, TimeSpan duration);

    /// <summary>
    /// Shows a sticky message (UI-WIN-043): it stays until disposed (its operation ended) or replaced by a newer
    /// message. Disposing after it was replaced changes nothing.
    /// </summary>
    IDisposable ShowStickyMessage(string text);

    /// <summary>The active text editor reports its fields; null when the active document view is not a text editor.</summary>
    void SetEditor(EditorStatus? editor);

    /// <summary>The active Design view reports its selection text; null when the active document view is not a Design view.</summary>
    void SetDesignerSelection(string? text);
}
