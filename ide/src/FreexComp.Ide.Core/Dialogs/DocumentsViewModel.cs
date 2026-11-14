using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One row of the Documents dialog: an open document, its name (modified dot prefixed) and folder.</summary>
public sealed class DocumentRow(DocumentViewModel document) : ObservableObject
{
    public DocumentViewModel Document { get; } = document;

    /// <summary><c>Dialog.Documents.Row.&lt;Key&gt;</c>: the file name, plus the distinguishing folders of same-name files.</summary>
    public string Key => Document.TitleSuffix is { } suffix ? Document.Title + " " + suffix : Document.Title;

    public string Name => (Document.IsModified ? Strings.Documents_ModifiedPrefix : "") + Document.Title;

    public string Folder => Document.FilePath is { } path ? Path.GetDirectoryName(path) ?? "" : "";

    internal void Refresh() => OnPropertyChanged(nameof(Name));
}

/// <summary>Sort column of the Documents list.</summary>
public enum DocumentsSort
{
    TabOrder,
    Name,
    Folder,
}

/// <summary>
/// The Documents dialog (dialogs.md section 17): the open documents in tab order (sortable by name or folder), multi-
/// selection starting at the active document; Activate (exactly one row; closes the dialog), Save (any selected row
/// modified), Close (the unsaved-changes prompt over the selection; rows disappear), Done. 600 x 380, resizable.
/// </summary>
public sealed class DocumentsViewModel : DialogViewModel<bool>
{
    public const string DialogName = "Documents";

    private readonly IDocumentService _documents;
    private readonly ILayoutService? _layout;
    private IReadOnlyList<DocumentRow> _selection = [];
    private DocumentsSort _sort = DocumentsSort.TabOrder;
    private bool _descending;

    public DocumentsViewModel(IDocumentService documents, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _layout = layout;
        Rows = [];
        foreach (var document in documents.Documents)
        {
            var row = new DocumentRow(document);
            void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DocumentViewModel.IsModified))
                {
                    row.Refresh();
                    UpdateButtons();
                }
            }

            document.PropertyChanged += OnChanged;
            CloseRequested += (_, _) => document.PropertyChanged -= OnChanged;
            Rows.Add(row);
        }

        Activate = new DialogButtonViewModel("Activate", Strings.Documents_Activate, ActivateSelected) { IsAuxiliary = true, AccessLabel = "_" + Strings.Documents_Activate };
        Save = new DialogButtonViewModel("Save", Strings.Button_Save, SaveSelectedAsync) { IsAuxiliary = true, AccessLabel = "_" + Strings.Button_Save };
        CloseDocs = new DialogButtonViewModel("CloseDocs", Strings.Button_Close, CloseSelectedAsync) { IsAuxiliary = true, AccessLabel = "_" + Strings.Button_Close };
        Buttons = [new DialogButtonViewModel("Close", Strings.Documents_Done, () => Close(true)) { IsDefault = true, IsCancel = true, IsAccent = true }];
        Selection = Rows.Where(r => r.Document == documents.ActiveDocument).ToList();
    }

    public override string Name => DialogName;

    public override string Title => Strings.Documents_Title;

    public override double Width => 600;

    public override double? Height => 380;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    /// <summary>The strip holds only Done; Activate / Save / Close sit beside the list (dialogs.md section 17).</summary>
    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Activate { get; }

    public DialogButtonViewModel Save { get; }

    /// <summary>The <c>CloseDocs</c> button (also <c>Delete</c> in the list).</summary>
    public DialogButtonViewModel CloseDocs { get; }

    /// <summary>The rows in the current sort order.</summary>
    public ObservableCollection<DocumentRow> Rows { get; }

    public IReadOnlyList<DocumentRow> Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _selection = value;
            OnPropertyChanged();
            UpdateButtons();
        }
    }

    /// <summary>A column header was clicked: sorts by it, a second click reverses (tab order is the natural order).</summary>
    public void SortBy(DocumentsSort sort)
    {
        _descending = _sort == sort && !_descending;
        _sort = sort;
        IEnumerable<DocumentRow> ordered = _documents.Documents.Select(d => Rows.First(r => r.Document == d));
        if (sort == DocumentsSort.Name)
            ordered = ordered.OrderBy(r => r.Document.Title, StringComparer.CurrentCultureIgnoreCase);
        else if (sort == DocumentsSort.Folder)
            ordered = ordered.OrderBy(r => r.Folder, StringComparer.CurrentCultureIgnoreCase);
        var list = (_descending ? ordered.Reverse() : ordered).ToList();
        Rows.Clear();
        foreach (var row in list)
        {
            Rows.Add(row);
        }
    }

    private void UpdateButtons()
    {
        Activate.IsEnabled = _selection.Count == 1;
        Save.IsEnabled = _selection.Any(r => r.Document.IsModified);
        CloseDocs.IsEnabled = _selection.Count > 0;
    }

    private void ActivateSelected()
    {
        if (_selection is not [var row])
            return;
        _documents.Activate(row.Document);
        Close(true);
    }

    private async Task SaveSelectedAsync()
    {
        foreach (var row in _selection.Where(r => r.Document.IsModified).ToList())
        {
            if (!await _documents.SaveAsync(row.Document))
                return;
        }
    }

    private async Task CloseSelectedAsync()
    {
        var closing = _selection.Select(r => r.Document).ToList();
        if (closing.Count == 0 || !await _documents.PromptSaveChangesAsync(closing))
            return;
        foreach (var document in closing)
        {
            _documents.Close(document);
        }

        foreach (var row in Rows.Where(r => !_documents.Documents.Contains(r.Document)).ToList())
        {
            Rows.Remove(row);
        }

        Selection = [];
    }
}
