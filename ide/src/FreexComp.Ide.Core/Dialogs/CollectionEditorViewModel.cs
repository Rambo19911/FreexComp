using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The collection editor (object-inspector.md §5.7, DSG-OI-110 … 116; dialogs.md §25): the item list of a working copy
/// (<see cref="CollectionEditorModel"/>), Add / Delete / Move Up / Move Down and the embedded property grid of the selected
/// item (<see cref="Grid"/>, item mode). The result is the one change OK applies (null: Cancel, or nothing changed); the
/// form is not touched while the dialog is open (DSG-OI-116).
/// </summary>
public sealed class CollectionEditorViewModel : DialogViewModel<FormChange>
{
    public const string DialogName = "CollectionEditor";

    private readonly FormDocument _document;
    private IReadOnlyList<int> _selected = [];

    /// <param name="document">The form (component references of the item grid).</param>
    /// <param name="component">The one selected component (DSG-OI-110).</param>
    /// <param name="propertyName">Its <c>"kind": "collection"</c> property.</param>
    /// <param name="status">Where an item value refused at focus loss is reported (DSG-OI-035); null: nowhere.</param>
    public CollectionEditorViewModel(ComponentCatalog catalog, FormDocument document, FormObject component, string propertyName,
        IDialogService? dialogs = null, IStatusService? status = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        Model = new CollectionEditorModel(catalog, component, propertyName);
        Title = string.Format(CultureInfo.CurrentCulture, Strings.CollectionEditor_Title, component.Name, Model.Property.Name);
        Grid = new PropertyGridViewModel(new ItemTarget(this, catalog, status), dialogs, itemMode: true);
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, () => Close(Model.CommitChange())) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
        // UI-DLG-243: OK is always enabled, except while a grid editor is open with a refused (invalid) entry.
        Grid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PropertyGridViewModel.IsEditing) or nameof(PropertyGridViewModel.EditError))
                Ok.IsEnabled = !(Grid.IsEditing && Grid.EditError is not null);
        };
        _selected = Model.Items.Count > 0 ? [0] : [];
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 640;

    public override double? Height => 400;

    public override bool CanResize => true;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public CollectionEditorModel Model { get; }

    /// <summary>The embedded grid (DSG-OI-113): the selected item's properties; empty with zero or several items selected.</summary>
    public PropertyGridViewModel Grid { get; }

    /// <summary>The list rows <c>{index} – {text}</c> (DSG-OI-111).</summary>
    public IReadOnlyList<string> ItemTexts => [.. Enumerable.Range(0, Model.Items.Count).Select(Model.ItemText)];

    /// <summary>The selected item indices in ascending order.</summary>
    public IReadOnlyList<int> SelectedIndices => _selected;

    /// <summary>The empty list shows <c>No items. Choose Add to create one.</c>.</summary>
    public bool IsEmpty => Model.Items.Count == 0;

    public string EmptyText => Strings.CollectionEditor_Empty;

    public bool CanDelete => _selected.Count > 0;

    public bool CanMoveUp => _selected is [var i] && Model.CanMove(i, -1);

    public bool CanMoveDown => _selected is [var i] && Model.CanMove(i, +1);

    /// <summary>Ctrl/Shift-click selection of the list (extended selection).</summary>
    public void Select(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var selected = indices.Where(i => i >= 0 && i < Model.Items.Count).Distinct().Order().ToList();
        if (selected.SequenceEqual(_selected))
        {
            return;
        }

        Grid.Cancel();
        _selected = selected;
        Changed(items: false);
    }

    /// <summary>Add (Insert): a new default item after the last one, selected.</summary>
    public void Add()
    {
        var index = Model.Add();
        _selected = [index];
        Changed(items: true);
    }

    /// <summary>Delete (Delete key): removes the selected items; the following one (else the previous) is selected.</summary>
    public void Delete()
    {
        if (!CanDelete)
        {
            return;
        }

        var next = Model.Delete(_selected);
        _selected = next < 0 ? [] : [next];
        Changed(items: true);
    }

    /// <summary>Move Up (Ctrl+Up).</summary>
    public void MoveUp() => Move(-1);

    /// <summary>Move Down (Ctrl+Down).</summary>
    public void MoveDown() => Move(+1);

    private void Move(int delta)
    {
        if (_selected is not [var index] || !Model.CanMove(index, delta))
        {
            return;
        }

        _selected = [Model.Move(index, delta)];
        Changed(items: true);
    }

    private void Changed(bool items)
    {
        if (items)
        {
            OnPropertyChanged(nameof(ItemTexts));
            OnPropertyChanged(nameof(IsEmpty));
        }

        OnPropertyChanged(nameof(SelectedIndices));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        Grid.Refresh();
    }

    /// <summary>The grid edits the selected item of the working copy.</summary>
    private sealed class ItemTarget(CollectionEditorViewModel owner, ComponentCatalog catalog, IStatusService? status) : IPropertyGridTarget
    {
        private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

        public ComponentCatalog Catalog => catalog;

        public IReadOnlyList<FormObject> Selection => owner._selected is [var i] ? [owner.Model.Items[i]] : [];

        public bool IsReadOnly => false;

        public string? FailureMessage => null;

        public bool Apply(IReadOnlyList<FormChange> changes)
        {
            owner.Model.Apply(changes);
            owner.OnPropertyChanged(nameof(ItemTexts)); // UI-DLG-241
            return true;
        }

        /// <summary>DSG-OI-035 in the dialog: the refused entry's message goes to the status bar, as for the Object Inspector.</summary>
        public void ReportError(string message) => status?.ShowMessage(message, StatusDuration);

        public IReadOnlyList<string> References(PropertyInfo info) =>
            [.. owner._document.AllObjects()
                .Where(o => o.Parent is not null && o.Info is { } c && catalog.IsAssignable(c.ClassName, info.Type))
                .Select(o => o.Name)
                .Order(StringComparer.OrdinalIgnoreCase)];
    }
}
