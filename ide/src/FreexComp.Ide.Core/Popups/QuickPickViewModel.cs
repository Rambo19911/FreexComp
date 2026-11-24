using CommunityToolkit.Mvvm.ComponentModel;

namespace FreexComp.Ide.Core.Popups;

/// <summary>
/// A row of a quick-pick popup: <see cref="Id"/> is the <c>.Item.&lt;id&gt;</c> part of its AutomationId, <see cref="Text"/>
/// the left text with the matched character positions <see cref="Highlights"/>, <see cref="Detail"/> the right-aligned text.
/// </summary>
public sealed record QuickPickItem(string Id, string Text, string Detail, IReadOnlyList<int> Highlights, bool IsEnabled, object? Tag);

/// <summary>
/// The popups Go to File and Command Palette (dialogs.md §13/§14): an input line, a filtered list with the first row selected,
/// Up/Down/PageUp/PageDown, Enter (<see cref="CommitAsync"/>), Escape (<see cref="Close"/>) and a disabled row when nothing
/// matches. Shown through <see cref="QuickPickHost"/>.
/// </summary>
public abstract class QuickPickViewModel : ObservableObject
{
    /// <summary>UI-DLG-130: the list shows up to this many rows.</summary>
    public const int VisibleRows = 12;

    private string _input = "";
    private IReadOnlyList<QuickPickItem> _items = [];
    private int _selectedIndex;

    /// <summary>The AutomationId root: <c>GoToFile</c> or <c>CommandPalette</c>.</summary>
    public abstract string Name { get; }

    /// <summary>The automation name of the input and the list (the command title).</summary>
    public abstract string AccessibleName { get; }

    /// <summary>The cell suffixes of a row: the left text and the right text.</summary>
    public abstract (string Text, string Detail) CellNames { get; }

    /// <summary>The title shown in the empty input line (UI-TW-124: "Move to Folder"); null for none.</summary>
    public virtual string? Title => null;

    /// <summary>The rows carry a file icon.</summary>
    public virtual bool ShowsFileIcon => false;

    public bool IsClosed { get; private set; }

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value ?? ""))
                Refresh();
        }
    }

    public IReadOnlyList<QuickPickItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, Items.Count == 0 ? -1 : Math.Clamp(value, 0, Items.Count - 1));
    }

    public QuickPickItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>The disabled row shown instead of an empty list; null while rows exist.</summary>
    public string? NoMatchText => Items.Count == 0 ? NoMatch(FilterText) : null;

    /// <summary>The closing popup restores focus (view) before <see cref="CommitAsync"/> continues.</summary>
    public event EventHandler? Closed;

    /// <summary>Up/Down (±1) and PageUp/PageDown (±<see cref="VisibleRows"/>).</summary>
    public void Move(int delta) => SelectedIndex = SelectedIndex + delta;

    /// <summary>Enter: acts on the selected row.</summary>
    public abstract Task CommitAsync();

    /// <summary>Escape, click outside or focus loss.</summary>
    public void Close()
    {
        if (IsClosed)
            return;
        IsClosed = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The part of the input that filters (Go to File drops a <c>:line</c> suffix).</summary>
    protected virtual string FilterText => Input.Trim();

    protected abstract IReadOnlyList<QuickPickItem> Filter(string filter);

    protected abstract string NoMatch(string filter);

    /// <summary>Re-filters (also called by derived constructors once their sources are set).</summary>
    protected void Refresh()
    {
        Items = Filter(FilterText);
        SelectedIndex = 0;
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(NoMatchText));
    }
}

/// <summary>The one open quick-pick popup of the main window (null = none); a new one replaces the open one.</summary>
public sealed class QuickPickHost : ObservableObject
{
    private QuickPickViewModel? _current;

    public QuickPickViewModel? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public void Show(QuickPickViewModel popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        Current?.Close();
        popup.Closed += (_, _) =>
        {
            if (Current == popup)
                Current = null;
        };
        Current = popup;
    }
}
