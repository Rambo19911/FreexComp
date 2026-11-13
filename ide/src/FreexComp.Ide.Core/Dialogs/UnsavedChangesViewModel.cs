using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One row of the Unsaved Changes list (dialogs.md §6): file name, folder (null when untitled), check box.</summary>
public sealed class UnsavedChangesFile : ObservableObject
{
    private bool _isChecked = true;

    internal UnsavedChangesFile(DocumentViewModel document)
    {
        Document = document;
        FileName = document.Title;
        Folder = document.FilePath is { } path ? Path.GetDirectoryName(path) : null;
    }

    public DocumentViewModel Document { get; }

    public string FileName { get; }

    public string? Folder { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>
/// The multi-file Unsaved Changes dialog (dialogs.md §6, UI-DLG-061…063): every listed document checked initially;
/// <c>Save Selected</c> (disabled while nothing is checked) returns the checked documents, <c>Don't Save</c> an empty
/// list, <c>Cancel</c> null. Unchecked documents are treated as Don't Save.
/// </summary>
public sealed class UnsavedChangesViewModel : DialogViewModel<IReadOnlyList<DocumentViewModel>>
{
    public const string DialogName = "UnsavedChanges";

    public UnsavedChangesViewModel(IReadOnlyList<DocumentViewModel> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Files = [.. documents.Select(d => new UnsavedChangesFile(d))];
        Heading = string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_SaveChangesMany, Files.Count);

        Ok = new DialogButtonViewModel("Ok", Strings.Button_SaveSelected, () => Close(Files.Where(f => f.IsChecked).Select(f => f.Document).ToList()))
        {
            IsDefault = true,
            IsAccent = true,
            AccessLabel = "_" + Strings.Button_SaveSelected,
        };
        var dontSave = new DialogButtonViewModel("DontSave", Strings.Button_DontSave, () => Close(Array.Empty<DocumentViewModel>()))
        {
            AccessLabel = MnemonicBefore(Strings.Button_DontSave, 'n'),
        };
        var cancel = new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true };
        Buttons = [Ok, dontSave, cancel];

        foreach (var file in Files)
        {
            file.PropertyChanged += (_, _) => Ok.IsEnabled = Files.Any(f => f.IsChecked);
        }
    }

    public override string Name => DialogName;

    public override string Title => Strings.Msg_File_SaveChangesMany_Title;

    public override double Width => 520;

    public override double? Height => 320;

    public override bool CanResize => true;

    /// <summary><c>Msg.File.SaveChangesMany</c> with the number of files.</summary>
    public string Heading { get; }

    /// <summary>The documents in the order given (tab order; the project file first once projects exist).</summary>
    public IReadOnlyList<UnsavedChangesFile> Files { get; }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    private DialogButtonViewModel Ok { get; }

    /// <summary>Ctrl+A in the list (dialogs.md §6).</summary>
    public void CheckAll()
    {
        foreach (var file in Files)
        {
            file.IsChecked = true;
        }
    }

    /// <summary>dialogs.md §6 gives <c>Do&amp;n't Save</c>: the mnemonic on the first <paramref name="letter"/>.</summary>
    private static string MnemonicBefore(string label, char letter)
    {
        var i = label.IndexOf(letter, StringComparison.Ordinal);
        return i < 0 ? label : label[..i] + "_" + label[i..];
    }
}
