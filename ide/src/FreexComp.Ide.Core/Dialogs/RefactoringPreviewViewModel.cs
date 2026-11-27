using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// A row of the Preview dialog's tree (UI-DLG-361, 362): a file or one of its edits. <see cref="Key"/> is the part of the
/// row's AutomationId after <c>Dialog.RefactoringPreview.</c> (UI-DLG-366).
/// </summary>
public abstract class RefactoringPreviewRow : ObservableObject
{
    private protected RefactoringPreviewRow(string key) => Key = key;

    public string Key { get; }

    /// <summary>The file of the row (a file row: itself).</summary>
    public abstract RefactoringPreviewFile File { get; }

    /// <summary>The state of the row's check box; null = a file row whose edits differ (UI-DLG-363).</summary>
    public abstract bool? IsChecked { get; }

    /// <summary>What a screen reader says for the row (UI-DLG-367).</summary>
    public abstract string AutomationName { get; }

    internal void RaiseChecked()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(AutomationName));
    }

    private protected static string StateText(bool? state) => state switch
    {
        true => Strings.Dialog_RefactoringPreview_Checked,
        false => Strings.Dialog_RefactoringPreview_NotChecked,
        null => Strings.Dialog_RefactoringPreview_PartlyChecked,
    };

    private protected static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);
}

/// <summary>
/// A file row (UI-DLG-361): the file's name and directory, its icon (<c>unit</c>, <c>form</c>, else <c>file</c>), its
/// edits by offset, and the tri-state check box that stands for all of them.
/// </summary>
public sealed class RefactoringPreviewFile : RefactoringPreviewRow
{
    internal RefactoringPreviewFile(FileEdit edit, string key)
        : base(key)
    {
        Path = edit.Path;
        BaseText = edit.BaseText;
        FileName = System.IO.Path.GetFileName(edit.Path);
        Directory = System.IO.Path.GetDirectoryName(edit.Path) ?? "";
        Icon = System.IO.Path.GetExtension(edit.Path).ToUpperInvariant() switch
        {
            ".PAS" => "unit",
            ".FORM" => "form",
            _ => "file",
        };
        var lines = LineStarts(edit.BaseText);
        Edits = [.. edit.Edits.OrderBy(e => e.Offset).Select((e, i) => new RefactoringPreviewEdit(this, e, i + 1, lines))];
    }

    public override RefactoringPreviewFile File => this;

    public string Path { get; }

    public string FileName { get; }

    public string Directory { get; }

    public string Icon { get; }

    /// <summary>The text the edits were computed against: the left side of the diff (UI-DLG-364).</summary>
    public string BaseText { get; }

    public ImmutableArray<RefactoringPreviewEdit> Edits { get; }

    public int Count => Edits.Length;

    public int CheckedCount => Edits.Count(e => e.Checked);

    /// <summary>The file's edit rows are shown (UI-DLG-361).</summary>
    public bool IsExpanded { get; internal set; }

    public override bool? IsChecked
    {
        get
        {
            var count = CheckedCount;
            return count == Count ? true : count == 0 ? false : null;
        }
    }

    public override string AutomationName => Count == 1
        ? Format(Strings.Dialog_RefactoringPreview_FileNameOne, FileName, StateText(IsChecked))
        : Format(Strings.Dialog_RefactoringPreview_FileName, FileName, Count, StateText(IsChecked));

    /// <summary>UI-DLG-364: the base text with the file's checked edits applied — the right side of the diff.</summary>
    public string AfterText()
    {
        var text = new System.Text.StringBuilder(BaseText);
        foreach (var edit in Edits.Where(e => e.Checked).Select(e => e.Edit).OrderByDescending(e => e.Offset))
        {
            text.Remove(edit.Offset, edit.Length);
            text.Insert(edit.Offset, edit.NewText);
        }

        return text.ToString();
    }

    /// <summary>The 0-based line of the <see cref="AfterText"/> at which the text of <paramref name="row"/> starts; null when the row is not checked.</summary>
    internal int? AfterLineOf(RefactoringPreviewEdit row)
    {
        if (!row.Checked)
            return null;
        var line = row.Line - 1;
        foreach (var earlier in Edits.TakeWhile(e => !ReferenceEquals(e, row)).Where(e => e.Checked))
            line += Breaks(earlier.Edit.NewText) - Breaks(BaseText.AsSpan(earlier.Edit.Offset, earlier.Edit.Length));
        return line;
    }

    /// <summary>The offsets at which the lines of <paramref name="text"/> start (breaks <c>\r\n</c>, <c>\n</c>, <c>\r</c>; the first is 0).</summary>
    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            else if (text[i] is not ('\r' or '\n'))
                continue;
            starts.Add(i + 1);
        }

        return starts;
    }

    private static int Breaks(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            else if (text[i] is not ('\r' or '\n'))
                continue;
            count++;
        }

        return count;
    }
}

/// <summary>
/// An edit row (UI-DLG-362): the 1-based line of the edit's start in the base text and that line without its leading
/// white space, in four parts — the text before the replaced range, the replaced range (cut at the end of the line), the
/// inserted text (line breaks shown as <c>⏎</c>, cut after <see cref="MaxInsertedLength"/> characters), the rest of the
/// line. A long line is cut to a window of <see cref="MaxLineLength"/> characters around the edit.
/// </summary>
public sealed class RefactoringPreviewEdit : RefactoringPreviewRow
{
    public const int MaxLineLength = 240;

    public const int MaxInsertedLength = 120;

    internal RefactoringPreviewEdit(RefactoringPreviewFile file, TextEdit edit, int index, List<int> lineStarts)
        : base(file.Key + ".Edit." + index.ToString(CultureInfo.InvariantCulture))
    {
        File = file;
        Edit = edit;
        Index = index;
        var text = file.BaseText;
        var at = lineStarts.BinarySearch(edit.Offset);
        var lineIndex = at >= 0 ? at : ~at - 1;
        Line = lineIndex + 1;
        var lineStart = lineStarts[lineIndex];
        var lineEnd = text.AsSpan(edit.Offset).IndexOfAny('\r', '\n') is var found && found >= 0 ? edit.Offset + found : text.Length;
        var removedEnd = Math.Min(edit.Offset + edit.Length, lineEnd);

        // Leading white space goes, but never a part of the edit.
        var start = lineStart;
        while (start < edit.Offset && char.IsWhiteSpace(text[start]))
            start++;
        var (before, removed, after) = (text[start..edit.Offset], text[edit.Offset..removedEnd], text[removedEnd..lineEnd]);
        var total = before.Length + removed.Length + after.Length;
        if (total > MaxLineLength)
        {
            // As the match rows of Search Results (UI-TW-601): a window around the replaced range, … at the cut ends.
            var line = before + removed + after;
            var length = Math.Min(removed.Length, MaxLineLength);
            var from = Math.Clamp(before.Length - (MaxLineLength - length) / 2, 0, total - MaxLineLength);
            var window = line.Substring(from, MaxLineLength);
            var removedStart = before.Length - from;
            var end = Math.Min(removedStart + removed.Length, MaxLineLength);
            before = (from > 0 ? "…" : "") + window[..removedStart];
            removed = window[removedStart..end];
            after = window[end..] + (from + MaxLineLength < total ? "…" : "");
        }

        var inserted = edit.NewText.Replace("\r\n", "⏎", StringComparison.Ordinal).Replace('\n', '⏎').Replace('\r', '⏎');
        if (inserted.Length > MaxInsertedLength)
            inserted = inserted[..MaxInsertedLength] + "…";
        (Before, Removed, Inserted, After) = (before, removed, inserted, after);
    }

    public override RefactoringPreviewFile File { get; }

    public TextEdit Edit { get; }

    /// <summary>The 1-based position among the file's edits.</summary>
    public int Index { get; }

    /// <summary>The 1-based line of the edit's start in the base text.</summary>
    public int Line { get; }

    public string LineText => Line.ToString(CultureInfo.CurrentCulture);

    public string Before { get; }

    /// <summary>The replaced text shown on <c>diff.removedInline</c>.</summary>
    public string Removed { get; }

    /// <summary>The inserted text shown on <c>diff.addedInline</c>.</summary>
    public string Inserted { get; }

    public string After { get; }

    /// <summary>The edit is applied by <c>Apply</c> (UI-DLG-365).</summary>
    public bool Checked { get; internal set; } = true;

    public override bool? IsChecked => Checked;

    public override string AutomationName => Edit.Length == 0
        ? Format(Strings.Dialog_RefactoringPreview_Inserted, Line, Inserted, StateText(Checked))
        : Edit.NewText.Length == 0
            ? Format(Strings.Dialog_RefactoringPreview_Removed, Line, Removed, StateText(Checked))
            : Format(Strings.Dialog_RefactoringPreview_Replaced, Line, Removed, Inserted, StateText(Checked));
}

/// <summary>
/// The Preview dialog of a refactoring (<c>RefactoringPreview</c>, UI-DLG-360…367): the files and edits of a workspace
/// edit as a two-level tree with check boxes, the summary line, and the diff view of the current row's file — the base
/// text against the base text with the file's checked edits. <c>Apply</c> closes with the edit reduced to the checked
/// edits (files without one are left out); <c>Cancel</c> closes with null. 960 × 600, resizable down to 720 × 420; the
/// size and the tree width are remembered (<c>dialogs.RefactoringPreview.size</c>, <c>.split</c>).
/// </summary>
public sealed class RefactoringPreviewViewModel : DialogViewModel<WorkspaceEdit>
{
    public const string DialogName = "RefactoringPreview";

    /// <summary>UI-DLG-360: the tree's width without a remembered one, its minimum and the minimum of the diff.</summary>
    public const double DefaultTreeWidth = 320;

    public const double MinimumTreeWidth = 200;

    public const double MinimumDiffWidth = 300;

    /// <summary>UI-DLG-361: an edit with more files than this opens with every file collapsed.</summary>
    public const int MaxExpandedFiles = 20;

    private readonly WorkspaceEdit _edit;
    private readonly ITextBufferFactory _buffers;
    private readonly ILanguageRegistry _languages;
    private readonly ILayoutService? _layout;
    private readonly IClipboardService? _clipboard;
    private readonly DialogButtonViewModel _apply;
    private IReadOnlyList<RefactoringPreviewRow> _rows = [];
    private IReadOnlyList<RefactoringPreviewRow> _selected = [];
    private RefactoringPreviewRow? _current;
    private DiffDocumentViewModel? _diff;
    private RefactoringPreviewFile? _shown;
    private string? _shownChecks;
    private bool _reveal;
    private int _request;

    /// <param name="edit">The normalised, valid workspace edit to show.</param>
    /// <param name="activePath">The file of the active document (its row comes first, UI-DLG-361); null without one.</param>
    public RefactoringPreviewViewModel(WorkspaceEdit edit, string? activePath, ITextBufferFactory buffers, ILanguageRegistry languages,
        ILayoutService? layout = null, IClipboardService? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentNullException.ThrowIfNull(languages);
        _edit = edit;
        _buffers = buffers;
        _languages = languages;
        _layout = layout;
        _clipboard = clipboard;
        Title = Format(Strings.Dialog_RefactoringPreview_Title, edit.Title);

        // UI-DLG-366: equal file names in different folders are numbered @2, @3 in path order.
        var byPath = edit.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new List<RefactoringPreviewFile>();
        foreach (var file in byPath)
        {
            var name = Path.GetFileName(file.Path);
            var occurrence = seen[name] = seen.GetValueOrDefault(name) + 1;
            files.Add(new RefactoringPreviewFile(file, "File." + name + (occurrence > 1 ? "@" + occurrence.ToString(CultureInfo.InvariantCulture) : ""))
            {
                IsExpanded = byPath.Count <= MaxExpandedFiles,
            });
        }

        // UI-DLG-361: the file of the active document first, then the others by path.
        Files = [.. files.OrderBy(f => !string.Equals(f.Path, activePath, StringComparison.OrdinalIgnoreCase))];

        _apply = new DialogButtonViewModel("Ok", Strings.Button_Apply, () => Close(Chosen()))
        {
            IsDefault = true,
            IsAccent = true,
            AccessLabel = "_" + Strings.Button_Apply,
        };
        Buttons =
        [
            Auxiliary("SelectAll", Strings.Dialog_RefactoringPreview_SelectAll, () => SetAll(true)),
            Auxiliary("SelectNone", Strings.Dialog_RefactoringPreview_SelectNone, () => SetAll(false)),
            _apply,
            new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true },
        ];
        BuildRows();
        _current = Files.FirstOrDefault();
        _selected = _current is null ? [] : [_current];
        _apply.IsEnabled = CheckedCount > 0;
        RequestDiff(reveal: true);
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 960;

    public override double? Height => 600;

    public override bool CanResize => true;

    public override (double Width, double Height)? MinimumSize => (720, 420);

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>The label of the tree, in the notation of dialogs.md (<c>&amp;</c> marks the mnemonic).</summary>
    public string ChangesLabel => Strings.Dialog_RefactoringPreview_Changes;

    /// <summary>UI-DLG-360: the tree's width (UI state <c>dialogs.RefactoringPreview.split</c>; 320 by default, at least 200).</summary>
    public double TreeWidth
    {
        get => double.TryParse(_layout?.GetDialogValue(DialogName, "split"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            ? Math.Max(width, MinimumTreeWidth)
            : DefaultTreeWidth;
        set => _layout?.SetDialogValue(DialogName, "split", Math.Max(value, MinimumTreeWidth).ToString("0", CultureInfo.InvariantCulture));
    }

    /// <summary>The file rows in the order shown (UI-DLG-361).</summary>
    public IReadOnlyList<RefactoringPreviewFile> Files { get; }

    /// <summary>The rows of the tree: every file row, each followed by its edit rows while it is expanded.</summary>
    public IReadOnlyList<RefactoringPreviewRow> Rows => _rows;

    /// <summary>The selected rows (multi-selection, UI-DLG-363); the view sets them.</summary>
    public IReadOnlyList<RefactoringPreviewRow> SelectedRows
    {
        get => _selected;
        set => _selected = value ?? [];
    }

    /// <summary>The focused row: the diff shows its file (UI-DLG-364). The view sets it.</summary>
    public RefactoringPreviewRow? CurrentRow
    {
        get => _current;
        set
        {
            if (!SetProperty(ref _current, value) || value is null)
                return;
            RequestDiff(reveal: true);
        }
    }

    private int EditCount => Files.Sum(f => f.Count);

    private int CheckedCount => Files.Sum(f => f.CheckedCount);

    /// <summary><c>Msg.Refactoring.PreviewSummary</c>: checked edits, all edits, files with a checked edit.</summary>
    public string Summary => Format(Strings.Msg_Refactoring_PreviewSummary, CheckedCount, EditCount, Files.Count(f => f.CheckedCount > 0));

    /// <summary>The diff of the current row's file; null until the first one is computed.</summary>
    public DiffDocumentViewModel? Diff
    {
        get => _diff;
        private set => SetProperty(ref _diff, value);
    }

    /// <summary>The file <see cref="Diff"/> shows.</summary>
    public RefactoringPreviewFile? DiffFile => _shown;

    /// <summary>The pending update of <see cref="Diff"/> after a row was selected or a check box changed (the view or a test may await it).</summary>
    public Task DiffUpdate { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// UI-DLG-363: toggles the check box of <paramref name="row"/> — a file row checks all its edits unless all are
    /// checked. With <paramref name="withSelection"/> (the Space key) every selected row gets the state the row gets,
    /// when the row is one of several selected rows.
    /// </summary>
    public void Toggle(RefactoringPreviewRow row, bool withSelection = false)
    {
        ArgumentNullException.ThrowIfNull(row);
        var state = row.IsChecked != true;
        IReadOnlyList<RefactoringPreviewRow> rows = withSelection && _selected.Count > 1 && _selected.Contains(row) ? _selected : [row];
        foreach (var edit in rows.SelectMany(r => r is RefactoringPreviewFile file ? file.Edits.AsEnumerable() : [(RefactoringPreviewEdit)r]))
            edit.Checked = state;
        ChecksChanged();
    }

    /// <summary>UI-DLG-361: shows or hides the edit rows of <paramref name="file"/>.</summary>
    public void SetExpanded(RefactoringPreviewFile file, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.IsExpanded == expanded)
            return;
        file.IsExpanded = expanded;
        BuildRows();

        // A hidden row is neither selected nor current: its file row stands for it.
        _selected = [.. _selected.Where(_rows.Contains)];
        if (_current is not null && !_rows.Contains(_current))
            _current = _current.File;
        OnPropertyChanged(nameof(Rows));
    }

    private void SetAll(bool state)
    {
        foreach (var edit in Files.SelectMany(f => f.Edits))
            edit.Checked = state;
        ChecksChanged();
    }

    private void ChecksChanged()
    {
        foreach (var row in Files.SelectMany(f => f.Edits.Cast<RefactoringPreviewRow>().Prepend(f)))
            row.RaiseChecked();
        OnPropertyChanged(nameof(Summary));
        _apply.IsEnabled = CheckedCount > 0;
        RequestDiff(reveal: false);
    }

    private void BuildRows() =>
        _rows = [.. Files.SelectMany(f => f.IsExpanded ? f.Edits.Cast<RefactoringPreviewRow>().Prepend(f) : [f])];

    /// <summary>UI-DLG-365: the edit reduced to the checked edits, files and edits in the order they were given.</summary>
    private WorkspaceEdit Chosen()
    {
        var files = ImmutableArray.CreateBuilder<FileEdit>();
        foreach (var file in _edit.Files)
        {
            var row = Files.First(f => string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            var kept = row.Edits.Where(e => e.Checked).Select(e => e.Edit).ToHashSet();
            var edits = file.Edits.Where(kept.Contains).ToImmutableArray();
            if (edits.Length > 0)
                files.Add(file with { Edits = edits });
        }

        return _edit with { Files = files.ToImmutable() };
    }

    private void RequestDiff(bool reveal)
    {
        _reveal |= reveal;
        DiffUpdate = UpdateDiffAsync(DiffUpdate, ++_request);
    }

    /// <summary>
    /// UI-DLG-364: one update at a time, the newest request wins. Another file → a new diff with its first change block
    /// selected; other check boxes in the shown file → the right side is computed again, the scroll position kept by
    /// line; a newly selected edit row → the block that shows its line.
    /// </summary>
    private async Task UpdateDiffAsync(Task previous, int request)
    {
        await previous;
        if (request != _request || IsClosed || _current is not { } row)
            return;
        var file = row.File;
        var checks = string.Concat(file.Edits.Select(e => e.Checked ? '1' : '0'));
        DiffDocumentViewModel? loaded = null;
        if (!ReferenceEquals(file, _shown))
        {
            var language = _languages.ForPath(file.Path);
            var left = new DiffSource(file.FileName, file.Path, () => Task.FromResult<DiffInput?>(new DiffInput(file.BaseText)));
            var right = new DiffSource(Format(Strings.Dialog_RefactoringPreview_After, file.FileName), file.Path,
                () => Task.FromResult<DiffInput?>(new DiffInput(file.AfterText())));
            var diff = new DiffDocumentViewModel(left, right, language, language, _buffers, _layout, status: null, _clipboard, showIdenticalBanner: true)
            {
                CanRefresh = false,
            };
            await diff.LoadAsync();
            (_shown, _shownChecks) = (file, checks);
            Diff = loaded = diff;
            OnPropertyChanged(nameof(DiffFile));
        }
        else if (checks != _shownChecks && _diff is not null)
        {
            _shownChecks = checks;
            await _diff.RefreshAsync();
        }

        // A request that came while this one computed has its own row to show.
        if (!_reveal || _diff is null || request != _request)
            return;
        _reveal = false;
        if (row is RefactoringPreviewEdit edit)
            _diff.SelectBlockAt(edit.Line - 1, file.AfterLineOf(edit));
        else if (!ReferenceEquals(_diff, loaded))
            _diff.SelectBlock(0); // a file row: its first change block (a diff just loaded has it selected)
    }

    private static DialogButtonViewModel Auxiliary(string id, string label, Action execute) =>
        new(id, label.Replace("&", "", StringComparison.Ordinal), execute)
        {
            IsAuxiliary = true,
            AccessLabel = label.Replace("_", "__", StringComparison.Ordinal).Replace('&', '_'),
        };

    private static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);
}
