using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.History;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// History (tool-windows.md §16b, local-history.md §2 UI-TW-1620…1625; F11-060): the local-history revisions of the active
/// text document (its history key, IDE-HIST-002), newest first, after the <c>(current buffer)</c> pseudo-row; live with the
/// active document and <see cref="LocalHistoryService.Changed"/>. <c>history.compare</c> opens a diff document of one
/// revision against the editor buffer or of two rows; <c>history.label</c> edits a label in place; <c>history.revert</c>
/// puts a revision into the buffers (local-history.md IDE-HIST-020, I-333). Owns <c>view.history</c>'s window and the
/// <c>history.*</c> commands.
/// </summary>
public sealed class HistoryViewModel : DebugGridViewModel
{
    public const string ToolName = "History";

    /// <summary>UI-TW-1625: the key of the <c>(current buffer)</c> pseudo-row.</summary>
    public const string CurrentKey = "current";

    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string Section = "history";
    private const string UnitPart = "unit";
    private const string FormPart = "form";
    private static readonly TimeSpan SizeDelay = TimeSpan.FromMilliseconds(300); // the current row's size while typing

    private readonly LocalHistoryService _history;
    private readonly IDocumentService _documents;
    private readonly ILayoutService _layout;
    private readonly ILanguageRegistry _languages;
    private readonly ITextBufferFactory _buffers;
    private readonly IStatusService _status;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService? _clipboard;
    private readonly IClock _clock;
    private readonly ToolWindowToolBarChoice _part;
    private bool _attached;
    private readonly Dictionary<string, DiffDocumentViewModel> _compares = new(StringComparer.Ordinal); // UI-TW-1623: one tab per pair
    private TextDocumentViewModel? _document;
    private IReadOnlyList<HistoryRevision> _revisions = [];
    private IDisposable? _pendingSize;
    private bool _showsRows;
    private string _shown = "";

    public HistoryViewModel(ICommandService commands, LocalHistoryService history, IDocumentService documents, ILayoutService layout,
        ILanguageRegistry languages, ITextBufferFactory buffers, IStatusService status, IDialogService dialogs, IClock clock,
        IClipboardService? clipboard = null)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(layout);
        _history = history;
        _documents = documents;
        _layout = layout;
        _languages = languages;
        _buffers = buffers;
        _status = status;
        _dialogs = dialogs;
        _clock = clock;
        _clipboard = clipboard;
        _part = new ToolWindowToolBarChoice("Part", Strings.History_Part, 120,
            [new ToolBarChoiceItem(UnitPart, Strings.History_Part_Unit), new ToolBarChoiceItem(FormPart, Strings.History_Part_Form)])
        {
            SelectedIndex = layout.GetUiValue(Section, "part") == FormPart ? 1 : 0, // UI-TW-1622: UI state history.part
            IsVisible = false,
        };
        _part.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ToolWindowToolBarChoice.SelectedIndex))
                return;
            _layout.SetUiValue(Section, "part", _part.SelectedId);
            Update();
        };
        history.Changed += (_, e) =>
        {
            if (_document?.FilePath is { } path && string.Equals(path, e.DocumentPath, StringComparison.OrdinalIgnoreCase))
                Update();
        };
        documents.ActiveDocumentChanged += (_, _) => Follow();
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is DiffDocumentViewModel diff)
            {
                foreach (var key in _compares.Where(c => c.Value == diff).Select(c => c.Key).ToList())
                    _compares.Remove(key);
            }

            if (e.Document == _document)
                Follow(closed: true);
        };
        documents.DocumentSaved += (_, e) =>
        {
            if (e.Document == _document)
                Update(); // Save As: another key
        };
        Follow();
    }

    /// <summary>UI-TW-1625: bottom dock, in no profile's default layout, P8.</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_history", ToolGroupId.Bottom, 8, () => this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Time", Strings.History_Column_Time, 150),
        new("Kind", Strings.History_Column_Kind, 100),
        new("Size", Strings.History_Column_Size, 90, AlignRight: true),
        new("Label", Strings.History_Column_Label, 0),
    ];

    /// <summary>UI-TW-1623: two rows are compared with each other.</summary>
    public override bool MultiSelect => true;

    /// <summary>UI-TW-1625 (<c>pencil</c> is the catalog's edit icon).</summary>
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
        [new("history.compare", "diff"), new("history.label", "pencil"), new("history.revert", "undo"), new("history.refresh", "refresh")];

    /// <summary>UI-TW-1625: the tool-bar commands in that order.</summary>
    public override IReadOnlyList<string?> ContextCommands { get; } = ["history.compare", "history.label", "history.revert", "history.refresh"];

    /// <summary>UI-TW-1622: <c>Part: (•) Unit ( ) Form</c>, shown for form documents with a unit.</summary>
    public override ToolWindowToolBarChoice? ToolBarOptions => _part;

    /// <summary>UI-TW-1620: <c>HISTORY — &lt;file name&gt;</c> (the file name as it is).</summary>
    public override string Caption => _document?.FilePath is { } path
        ? Strings.Cmd_view_history.ToUpper(CultureInfo.CurrentCulture) + " — " + Path.GetFileName(path)
        : base.Caption;

    /// <summary>UI-TW-1621: without revisions the <c>(current buffer)</c> row stays above <c>Msg.History.NoRevisions</c>.</summary>
    public override bool ShowsContentWhenEmpty => _showsRows;

    /// <summary>The text document the window shows; null when none.</summary>
    public TextDocumentViewModel? Document => _document;

    /// <summary>UI-TW-1622: the entry <c>Compare</c> and <c>Size</c> use.</summary>
    public HistoryRole Role => _document is FormDocumentViewModel { HasUnit: true }
        ? _part.SelectedId == FormPart ? HistoryRole.Form : HistoryRole.Unit
        : HistoryRole.Text;

    /// <summary>The revisions of the selected rows, in row order (the pseudo-row has none).</summary>
    public IReadOnlyList<HistoryRevision> SelectedRevisions => [.. SelectedRows.Select(r => r.Tag).OfType<HistoryRevision>()];

    /// <summary>The latest compare (completed when none is under way).</summary>
    public Task Comparing { get; private set; } = Task.CompletedTask;

    /// <summary>The latest revert (completed when none is under way).</summary>
    public Task Reverting { get; private set; } = Task.CompletedTask;

    public void RegisterCommands()
    {
        Commands.RegisterHandler("history.compare", new Handler(CanCompare, () => Comparing = CompareAsync()));
        Commands.RegisterHandler("history.label", new Handler(() => SelectedRows.Count == 1 && SelectedRevisions.Count == 1, () =>
        {
            BeginLabel(SelectedRows[0]);
            return Task.CompletedTask;
        }));
        Commands.RegisterHandler("history.revert", new Handler(CanRevert, () => Reverting = RevertAsync()));
        Commands.RegisterHandler("history.refresh", new Handler(() => true, () =>
        {
            Update(force: true);
            return Task.CompletedTask;
        }));
    }

    /// <summary>IDE-HIST-020: one selected revision row of a document that can be edited.</summary>
    private bool CanRevert() =>
        _document is { FilePath: not null, IsReadOnly: false } && SelectedRows.Count == 1 && SelectedRevisions.Count == 1;

    /// <summary>
    /// IDE-HIST-020: the selected revision replaces the buffer text (decoded as on open), one undo step per buffer; a
    /// modified document's content is first recorded as <c>beforeRevert</c>. A form document with a unit gets both parts
    /// (whatever the Part choice), and the designer reads the form text as after a Text-view edit. Nothing is written.
    /// </summary>
    public async Task RevertAsync()
    {
        if (_document is not { FilePath: { } path } document || !CanRevert())
            return;
        var revision = SelectedRevisions[0];
        var form = document as FormDocumentViewModel;
        HistoryRole[] roles = form is { HasUnit: true } ? [HistoryRole.Unit, HistoryRole.Form] : [HistoryRole.Text];
        var texts = new Dictionary<HistoryRole, string>();
        try
        {
            foreach (var role in roles.Where(r => revision.Entry(r) is not null))
            {
                var bytes = await Task.Run(() => _history.ReadEntry(revision, role));
                texts[role] = EncodingDetector.Detect(bytes).Text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.OpenFailed", [path, ex.Message]));
            return;
        }

        if (_document != document || texts.Count == 0)
            return; // another document became active meanwhile
        if (document.IsModified)
            _ = _history.RecordBeforeRevertAsync(path, CurrentParts(document));
        var title = Strings.Cmd_history_revert;
        if (form is null)
        {
            document.ReplaceText(texts[HistoryRole.Text], title);
            return;
        }

        if (texts.TryGetValue(HistoryRole.Unit, out var unit))
            document.ReplaceText(unit, title);
        if (texts.TryGetValue(form.HasUnit ? HistoryRole.Form : HistoryRole.Text, out var formText))
            form.Designer.RevertFormText(formText, title);
    }

    /// <summary>IDE-HIST-020: the bytes the document's buffers would write now, per history part (IDE-HIST-002).</summary>
    private static IReadOnlyList<HistoryPart> CurrentParts(TextDocumentViewModel document)
    {
        var path = document.FilePath!;
        if (document is not FormDocumentViewModel form)
            return [new HistoryPart(HistoryRole.Text, path, FileEncodings.Encode(document.Buffer.Text, document.Encoding), null)];
        var formBytes = FileEncodings.Encode(form.Designer.CurrentFormText(), FileEncoding.Utf8);
        return form.HasUnit
            ?
            [
                new HistoryPart(HistoryRole.Unit, path, FileEncodings.Encode(document.Buffer.Text, document.Encoding), null),
                new HistoryPart(HistoryRole.Form, form.FormText.FilePath ?? path, formBytes, null),
            ]
            : [new HistoryPart(HistoryRole.Text, path, formBytes, null)];
    }

    /// <summary>UI-TW-1623: one revision row, or two rows; not the pseudo-row alone, not more than two.</summary>
    private bool CanCompare() =>
        _document is not null && SelectedRows.Count is 1 or 2 && SelectedRevisions.Count >= 1;

    /// <summary>UI-TW-1623, 1624: a double-click on the <c>Label</c> cell edits it; elsewhere it compares that row (with the buffer).</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Tag is not HistoryRevision)
            return Task.CompletedTask;
        if (column == "Label")
        {
            BeginLabel(row);
            return Task.CompletedTask;
        }

        Select(row.Key);
        return Comparing = CompareAsync();
    }

    /// <summary>
    /// UI-TW-1623: the older row left, the newer right (the pseudo-row is the live editor buffer, suffix <c>(editor)</c>);
    /// one revision row → it with the buffer. A diff of the same pair already open is activated and refreshed.
    /// </summary>
    public async Task CompareAsync()
    {
        if (_document is not { FilePath: { } } document || !CanCompare())
            return;
        var role = Role;
        var path = PartPath(document, role);
        var name = Path.GetFileName(path);
        var selected = SelectedRows.ToList();
        var newer = selected.Count == 1 ? null : selected[0].Tag as HistoryRevision; // row order: newest first; null = the buffer
        var older = (HistoryRevision)selected[^1].Tag!;
        var language = _languages.ForPath(path);
        var left = RevisionSource(older, role, name, path);
        var buffer = PartBuffer(document, role);
        // IDE-HIST-021: the unit (or single file) buffer is the live right side of Copy Block; the form text is not (the
        // designer owns it outside the Text view).
        var right = newer is null
            ? new DiffSource(name + " " + Strings.Diff_EditorSuffix, path, () => Task.FromResult<DiffInput?>(new DiffInput(buffer.Text)))
            {
                LiveBuffer = role == HistoryRole.Form || document is FormDocumentViewModel { HasUnit: false } ? null : buffer,
            }
            : RevisionSource(newer, role, name, path);
        var pair = string.Join("|", path.ToUpperInvariant(), role, older.Id, newer?.Id.ToString(CultureInfo.InvariantCulture) ?? CurrentKey);
        if (_compares.TryGetValue(pair, out var open) && _documents.Documents.Contains(open))
        {
            _documents.Activate(open);
            await open.RefreshAsync();
            return;
        }

        var diff = new DiffDocumentViewModel(left, right, language, language, _buffers, _layout, _status, _clipboard, showIdenticalBanner: true);
        if (!await diff.LoadAsync())
            return;
        _compares[pair] = diff;
        _documents.OpenDocument(diff);
    }

    /// <summary>A revision's entry as a diff side, decoded as on open (IDE-P1-030); a read failure → <c>Msg.File.OpenFailed</c>.</summary>
    private DiffSource RevisionSource(HistoryRevision revision, HistoryRole role, string name, string path) =>
        new(name + " (" + TimeText(revision) + ")", path, async () =>
        {
            try
            {
                var bytes = await Task.Run(() => _history.ReadEntry(revision, role));
                return new DiffInput(EncodingDetector.Detect(bytes).Text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.OpenFailed", [path, ex.Message]));
                return null;
            }
        });

    /// <summary>UI-TW-1620: local time.</summary>
    public static string TimeText(HistoryRevision revision) =>
        (revision ?? throw new ArgumentNullException(nameof(revision))).TimeUtc.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>UI-TW-1620: <c>Saved</c>, <c>Original</c>, <c>Before revert</c>.</summary>
    public static string KindText(HistoryKind kind) => kind switch
    {
        HistoryKind.Original => Strings.History_Kind_Original,
        HistoryKind.BeforeRevert => Strings.History_Kind_BeforeRevert,
        _ => Strings.History_Kind_Save,
    };

    /// <summary>UI-TW-1624: Enter commits through <see cref="LocalHistoryService.SetLabel"/>, Escape cancels, focus loss commits.</summary>
    private void BeginLabel(DebugGridRow row)
    {
        if (row.Tag is not HistoryRevision revision)
            return;
        Select(row.Key);
        Edit = new DebugGridEdit(row.Key, "Label", revision.Label ?? "", text =>
        {
            Edit = null;
            _history.SetLabel(revision, text);
            return Task.FromResult<string?>(null);
        }, () => Edit = null)
        {
            IdSuffix = ".Label.Edit",
            IsCode = false,
            CommitsOnFocusLoss = true,
        };
    }

    /// <summary>
    /// UI-TW-1620: follows the active text document; while a diff document is active (a compare just opened) the window
    /// keeps the document it shows.
    /// </summary>
    private void Follow(bool closed = false)
    {
        var active = _documents.ActiveDocument;
        TextDocumentViewModel? next = active switch
        {
            TextDocumentViewModel { GeneratedPath: null } text => text,
            DiffDocumentViewModel when !closed => _document,
            _ => null,
        };
        if (next != _document)
        {
            if (_document is not null)
                UnwatchBuffers(_document);
            _document = next;
            if (_document is not null)
                WatchBuffers(_document);
            if (Edit is not null)
                Edit = null;
        }

        Update();
    }

    private void WatchBuffers(TextDocumentViewModel document)
    {
        document.Buffer.Changed += OnBufferChanged;
        if (document is FormDocumentViewModel form)
            form.FormBuffer.Changed += OnBufferChanged;
    }

    private void UnwatchBuffers(TextDocumentViewModel document)
    {
        document.Buffer.Changed -= OnBufferChanged;
        if (document is FormDocumentViewModel form)
            form.FormBuffer.Changed -= OnBufferChanged;
    }

    /// <summary>The <c>(current buffer)</c> size follows typing, a moment later.</summary>
    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        _pendingSize?.Dispose();
        _pendingSize = _clock.Schedule(SizeDelay, () =>
        {
            _pendingSize = null;
            Update();
        });
    }

    /// <summary>The store is read only once the window exists (it was shown): a hidden History window costs nothing.</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        _attached = true;
        Update();
    }

    /// <summary>UI-TW-1620, 1621: the title, the empty state and the rows; an open in-place edit is kept when nothing changed.</summary>
    private void Update(bool force = false)
    {
        if (!_attached)
            return;
        var document = _document;
        _part.IsVisible = document is FormDocumentViewModel { HasUnit: true };
        OnPropertyChanged(nameof(Caption)); // the tab keeps the plain title "History"
        var availability = document?.FilePath is { } documentPath ? _history.Availability(documentPath)
            : document is null ? HistoryAvailability.NoDocument
            : HistoryAvailability.NotInProject; // untitled: not in the project folder
        if (availability != HistoryAvailability.Available)
        {
            _revisions = [];
            _showsRows = false;
            _shown = "";
            SetRows([]);
            SetEmpty(availability switch
            {
                HistoryAvailability.NoDocument => Strings.Msg_History_NoDocument,
                HistoryAvailability.Disabled => Strings.Msg_History_Disabled,
                _ => Strings.Msg_History_NotInProject,
            });
            Commands.InvalidateCanExecute();
            return;
        }

        _revisions = _history.GetRevisions(document!.FilePath!);
        var role = Role;
        var rows = new List<DebugGridRow>
        {
            new(CurrentKey,
            [
                new DebugGridCell(Strings.Msg_History_CurrentRow),
                DebugGridCell.Empty,
                new DebugGridCell(SizeText(CurrentSize(document, role))),
                DebugGridCell.Empty,
            ])
            {
                AutomationName = Strings.Msg_History_CurrentRow,
            },
        };
        foreach (var revision in _revisions)
        {
            var entry = revision.Entry(role);
            var time = TimeText(revision);
            var kind = KindText(revision.Kind);
            rows.Add(new DebugGridRow(revision.Id.ToString(CultureInfo.InvariantCulture),
            [
                new DebugGridCell(time),
                new DebugGridCell(kind),
                new DebugGridCell(entry is null ? "" : SizeText(entry.Size)),
                new DebugGridCell(revision.Label ?? ""),
            ])
            {
                Tag = revision,
                AutomationName = revision.Label is null ? time + ", " + kind : time + ", " + kind + ", " + revision.Label,
            });
        }

        _showsRows = true;
        if (_revisions.Count == 0)
            SetEmpty(Strings.Msg_History_NoRevisions);
        else
            SetEmpty(null);
        var shown = string.Join("\n", rows.Select(r => r.Key + "\t" + string.Join("\t", r.Cells.Select(c => c.Text))));
        if (shown == _shown && !force)
            return;
        _shown = shown;
        SetRows(rows);
    }

    private static string SizeText(long bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The bytes the buffer of <paramref name="role"/> would write now.</summary>
    private static long CurrentSize(TextDocumentViewModel document, HistoryRole role) => role == HistoryRole.Form
        ? FileEncodings.Encode(PartBuffer(document, role).Text, FileEncoding.Utf8).LongLength
        : FileEncodings.Encode(PartBuffer(document, role).Text, PartEncoding(document, role)).LongLength;

    private static FileEncoding PartEncoding(TextDocumentViewModel document, HistoryRole role) =>
        role == HistoryRole.Text && document is FormDocumentViewModel ? FileEncoding.Utf8 : document.Encoding;

    /// <summary>IDE-HIST-002: the unit is the document's buffer; the form (and a form without unit) the form text.</summary>
    private static ITextBuffer PartBuffer(TextDocumentViewModel document, HistoryRole role) =>
        document is FormDocumentViewModel form && (role == HistoryRole.Form || !form.HasUnit) ? form.FormBuffer : document.Buffer;

    private static string PartPath(TextDocumentViewModel document, HistoryRole role) =>
        role == HistoryRole.Form && document is FormDocumentViewModel { FormText.FilePath: { } formPath } ? formPath : document.FilePath!;
}
