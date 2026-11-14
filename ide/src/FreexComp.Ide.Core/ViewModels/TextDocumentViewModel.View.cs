namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The per-document view commands (IDE-ED-039, IDE-ED-040): zoom changes <see cref="EditorViewState.FontSize"/> by
/// ±1 pt within 6…72, word wrap toggles <see cref="EditorViewState.WordWrap"/>. Both work on read-only documents.
/// </summary>
public partial class TextDocumentViewModel
{
    public const string ZoomInCommandId = "view.zoomIn";
    public const string ZoomOutCommandId = "view.zoomOut";
    public const string ZoomResetCommandId = "view.zoomReset";
    public const string WordWrapCommandId = "view.wordWrap";

    public const int MinFontSize = 6;
    public const int MaxFontSize = 72;

    /// <summary>The view commands of the document (menus.md "text"), routed to the active document.</summary>
    public static readonly IReadOnlyList<string> ViewCommandIds = [ZoomInCommandId, ZoomOutCommandId, ZoomResetCommandId, WordWrapCommandId];

    /// <summary>The font size the editor draws with: the zoomed size, else <c>editor.fontSize</c>.</summary>
    public int EffectiveFontSize => ViewState.FontSize ?? Options.FontSize;

    /// <summary>The session's <c>zoom</c>: round(100 × size / <c>editor.fontSize</c>) (IDE-ED-039).</summary>
    public int ZoomPercent => (int)Math.Round(100.0 * EffectiveFontSize / Math.Max(1, Options.FontSize), MidpointRounding.AwayFromZero);

    /// <summary>Restores a session's <c>zoom</c> percentage (clamped to 6…72 pt).</summary>
    public void SetZoomPercent(int percent) =>
        SetFontSize((int)Math.Round(Options.FontSize * percent / 100.0, MidpointRounding.AwayFromZero));

    /// <summary>
    /// IDE-P1-054: a file above <c>files.largeFileThresholdMb</c> opened after <c>Msg.File.TooLarge</c>: Plain Text, word
    /// wrap forced off (<see cref="EditorViewState.WordWrapAllowed"/>, so neither the setting nor a session turns it on).
    /// </summary>
    public bool IsLargeFile
    {
        get => !ViewState.WordWrapAllowed;
        init => ViewState.WordWrapAllowed = !value;
    }

    private bool CanExecuteView(string commandId) => commandId switch
    {
        ZoomInCommandId => EffectiveFontSize < MaxFontSize,
        ZoomOutCommandId => EffectiveFontSize > MinFontSize,
        ZoomResetCommandId => EffectiveFontSize != Options.FontSize,
        _ => !IsLargeFile, // view.wordWrap
    };

    private bool ExecuteView(string commandId)
    {
        switch (commandId)
        {
            case ZoomInCommandId:
                SetFontSize(EffectiveFontSize + 1);
                break;
            case ZoomOutCommandId:
                SetFontSize(EffectiveFontSize - 1);
                break;
            case ZoomResetCommandId:
                ViewState.FontSize = null;
                break;
            default:
                ViewState.WordWrap = !ViewState.WordWrap;
                break;
        }

        return true;
    }

    /// <summary>A size equal to <c>editor.fontSize</c> is no zoom: the document follows the setting again.</summary>
    private void SetFontSize(int size)
    {
        size = Math.Clamp(size, MinFontSize, MaxFontSize);
        ViewState.FontSize = size == Options.FontSize ? null : size;
    }
}
