using System.Globalization;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The IDE side of one form document's design surface (design-surface.md §1): status messages, the armed Tool Palette
/// item (tool-palette.md §4), the clipboard and code sync. The context menu and the jump into the Object Inspector are
/// raised as events for the views.
/// </summary>
public sealed class DesignerHost : IDesignerHost
{
    private readonly FormDesignerViewModel _owner;

    internal DesignerHost(FormDesignerViewModel owner) => _owner = owner;

    /// <summary>The armed item changed (armed, disarmed).</summary>
    public event EventHandler? ArmedChanged;

    /// <summary>DSG-SUR-105: the view moves focus to the Object Inspector and starts editing (property name, typed text).</summary>
    public event EventHandler<(string PropertyName, string Text)>? PropertyEditRequested;

    /// <summary>The view opens the design-surface context menu at a surface point.</summary>
    public event EventHandler<(double X, double Y)>? ContextMenuRequested;

    public string? ArmedPaletteClass { get; private set; }

    public bool ArmedSticky { get; private set; }

    /// <summary>
    /// The clipboard of the surface. In memory until the IDE binds the system clipboard (the desktop view wires it).
    /// </summary>
    public IDesignerClipboard Clipboard { get; set; } = new MemoryDesignerClipboard();

    public ICodeSync CodeSync => _owner.CodeSync;

    public void SetStatus(string messageId, params object[] args) => _owner.ShowStatus(messageId, args);

    public void ShowErrorDialog(string messageId, params object[] args) => _owner.ShowErrorDialog(messageId, args);

    public void ShowContextMenu(double x, double y) => ContextMenuRequested?.Invoke(this, (x, y));

    public void BeginPropertyEdit(string propertyName, string text) => PropertyEditRequested?.Invoke(this, (propertyName, text));

    public void EditCollection(FreexComp.Designer.Model.FormObject component, string propertyName) => _owner.EditCollection(component, propertyName);

    public void EditComponent(FreexComp.Designer.Model.FormObject component) => _owner.EditComponent(component);

    /// <summary>DSG-TP-040, 041: arms <paramref name="className"/> (single-shot, or sticky).</summary>
    public void Arm(string className, bool sticky)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArmedPaletteClass = className;
        ArmedSticky = sticky;
        ArmedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disarm()
    {
        if (ArmedPaletteClass is null)
        {
            return;
        }

        ArmedPaletteClass = null;
        ArmedSticky = false;
        ArmedChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A clipboard inside the process (until the system clipboard is bound).</summary>
public sealed class MemoryDesignerClipboard : IDesignerClipboard
{
    private Dictionary<string, string> _formats = [];

    public long SequenceNumber { get; private set; }

    public string? GetData(string format) => _formats.GetValueOrDefault(format);

    public void SetData(IReadOnlyDictionary<string, string> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = new Dictionary<string, string>(formats);
        SequenceNumber++;
    }
}

/// <summary>The defaults of the <c>designer.*</c> settings (design-surface.md §1, §6).</summary>
public sealed class DefaultDesignerSettings : IDesignerSettings
{
    /// <summary>The settings catalog's default wins (lead decision D-079, D-080, I-130).</summary>
    public bool ShowComponentCaptions => true;

    public int HandleSize => 7;

    public bool ShowGrid => true;

    public bool SnapToGrid => true;

    public int GridSizeX => 8;

    public int GridSizeY => 8;

    public bool UseGuidelines => true;

    public int GuidelineSnapDistance => 4;

    public int GuidelineMargin => 8;

    public bool LockControls => false;
}

/// <summary>The text of a designer message id (<c>Msg.Designer.X</c> → resource <c>Msg_Designer_X</c>) with its arguments.</summary>
public static class DesignerMessages
{
    public static string Format(string messageId, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        var format = Strings.ResourceManager.GetString(messageId.Replace('.', '_'), Strings.Culture) ?? messageId;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
