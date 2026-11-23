using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// Add Breakpoint / Breakpoint Properties (dialogs.md §19), 520 × auto. The result is the breakpoint to add or the new
/// values of the edited one; null = canceled. In Properties mode <c>File</c> and <c>Line</c> are read-only (UI-DLG-190).
/// </summary>
public sealed class BreakpointViewModel : DialogViewModel<BreakpointInfo>
{
    public const string DialogName = "Breakpoint";

    private readonly IdeFileSystem _fileSystem;
    private readonly IFileDialogService _pickers;
    private readonly IDocumentService _documents;
    private readonly HitCountSpec? _otherHitCount;
    private readonly string? _logMessage;
    private string _file;
    private string _line;
    private string _condition;
    private string _passCount;
    private bool _enabled;
    private string? _conditionError;
    private bool _committing;

    /// <summary>Add mode: the active document's path and caret line (either may be empty / 1).</summary>
    public BreakpointViewModel(IdeFileSystem fileSystem, IFileDialogService pickers, IDocumentService documents, IReadOnlyList<string> projectFiles,
        string file, int line)
        : this(fileSystem, pickers, documents, projectFiles, new BreakpointInfo(file, line), isProperties: false)
    {
    }

    /// <summary>Properties mode: the breakpoint as it is.</summary>
    public BreakpointViewModel(IdeFileSystem fileSystem, IFileDialogService pickers, IDocumentService documents, IReadOnlyList<string> projectFiles,
        BreakpointInfo breakpoint)
        : this(fileSystem, pickers, documents, projectFiles, breakpoint, isProperties: true)
    {
    }

    private BreakpointViewModel(IdeFileSystem fileSystem, IFileDialogService pickers, IDocumentService documents, IReadOnlyList<string> projectFiles,
        BreakpointInfo breakpoint, bool isProperties)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        _fileSystem = fileSystem;
        _pickers = pickers;
        _documents = documents;
        IsProperties = isProperties;
        ProjectFiles = projectFiles;
        _file = breakpoint.Path;
        _line = breakpoint.Line.ToString(CultureInfo.InvariantCulture);
        _condition = breakpoint.Condition ?? "";
        _enabled = breakpoint.Enabled;
        _logMessage = breakpoint.LogMessage;
        // The debug-session interface §6.1: hit counts of the other modes are shown read-only and kept.
        _otherHitCount = breakpoint.HitCount is { Mode: not HitCountMode.GreaterOrEqual } other ? other : null;
        _passCount = BreakpointModel.PassCountOf(breakpoint.HitCount).ToString(CultureInfo.InvariantCulture);
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, CommitAsync) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
        Revalidate();
    }

    public override string Name => DialogName;

    public override string Title => IsProperties ? Strings.Breakpoint_Title_Properties : Strings.Breakpoint_Title_Add;

    public override double Width => 520;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public bool IsProperties { get; }

    /// <summary>The project's files (full paths) listed by the <c>File</c> combo.</summary>
    public IReadOnlyList<string> ProjectFiles { get; }

    /// <summary>While a session is paused: evaluates the condition once on OK; its message blocks OK.</summary>
    public Func<string, Task<string?>>? ValidateCondition { get; set; }

    public string File
    {
        get => _file;
        set
        {
            if (!IsProperties && SetProperty(ref _file, value ?? ""))
                Revalidate();
        }
    }

    public string Line
    {
        get => _line;
        set
        {
            if (!IsProperties && SetProperty(ref _line, value ?? ""))
                Revalidate();
        }
    }

    public string Condition
    {
        get => _condition;
        set
        {
            if (!SetProperty(ref _condition, value ?? ""))
                return;
            _conditionError = null;
            Revalidate();
        }
    }

    public string PassCount
    {
        get => _passCount;
        set
        {
            if (!IsPassCountReadOnly && SetProperty(ref _passCount, value ?? ""))
                Revalidate();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    /// <summary>A hit count of the mode <c>Equal</c> / <c>Multiple</c>: shown as <c>= v</c> / <c>every v</c>, not editable.</summary>
    public bool IsPassCountReadOnly => _otherHitCount is not null;

    /// <summary>The text of the <c>PassCount</c> field.</summary>
    public string PassCountText => _otherHitCount switch
    {
        { Mode: HitCountMode.Equal } h => string.Format(CultureInfo.CurrentCulture, Strings.Breakpoint_PassCountEqual, h.Value),
        { } h => string.Format(CultureInfo.CurrentCulture, Strings.Breakpoint_PassCountEvery, h.Value),
        null => _passCount,
    };

    public string? FileError { get; private set; }

    public string? LineError { get; private set; }

    public string? ConditionError => _conditionError;

    public string? PassCountError { get; private set; }

    /// <summary>The explanation right of the pass count; null while it is invalid or read-only.</summary>
    public string? PassCountHint { get; private set; }

    /// <summary>The <c>File.Browse</c> button: the file picker.</summary>
    public async Task BrowseAsync()
    {
        if (IsProperties)
            return;
        string? folder = null;
        try
        {
            folder = File.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(File)) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        var picked = await _pickers.PickOpenFilesAsync(new FilePickerRequest(DialogName + ".File", Strings.Breakpoint_FilePicker, [], folder));
        if (picked is [var path, ..])
            File = path;
    }

    private void Revalidate()
    {
        var path = FullPath(File);
        FileError = path is null || !_fileSystem.FileExists(path) ? Strings.Msg_Breakpoint_FileMissing : null;
        var lineCount = FileError is null ? LineCountOf(path!) : null;
        var lineOk = int.TryParse(Line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var line) && line >= 1
            && (lineCount is null || line <= lineCount);
        LineError = lineOk ? null
            : lineCount is { } count ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_GoToLine_OutOfRange, count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_GoToLine_OutOfRange, int.MaxValue);
        if (IsPassCountReadOnly)
        {
            (PassCountError, PassCountHint) = (null, null);
        }
        else if (int.TryParse(PassCount.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pass) && pass <= BreakpointModel.MaxPassCount)
        {
            PassCountError = null;
            PassCountHint = pass == 0 ? Strings.Msg_Breakpoint_PassCountNone
                : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Breakpoint_PassCountSome, pass);
        }
        else
        {
            (PassCountError, PassCountHint) = (Strings.Msg_Breakpoint_PassCountRange, null);
        }

        Ok.IsEnabled = FileError is null && LineError is null && PassCountError is null && _conditionError is null;
        OnPropertyChanged(nameof(FileError));
        OnPropertyChanged(nameof(LineError));
        OnPropertyChanged(nameof(PassCountError));
        OnPropertyChanged(nameof(PassCountHint));
        OnPropertyChanged(nameof(ConditionError));
    }

    private static string? FullPath(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;
        try
        {
            return Path.IsPathFullyQualified(file.Trim()) ? Path.GetFullPath(file.Trim()) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>The line count of the file when it is open or readable; null = unknown (no upper limit).</summary>
    private int? LineCountOf(string path)
    {
        if (_documents.Documents.OfType<TextDocumentViewModel>().FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } open)
            return open.Buffer.LineCount;
        try
        {
            var bytes = _fileSystem.ReadAllBytes(path);
            return bytes.Count(b => b == (byte)'\n') + 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task CommitAsync()
    {
        if (!Ok.IsEnabled || _committing)
            return;
        var condition = Condition.Trim();
        if (condition.Length > 0 && ValidateCondition is { } validate)
        {
            _committing = true;
            string? problem;
            try
            {
                problem = await validate(condition);
            }
            finally
            {
                _committing = false;
            }

            if (problem is not null)
            {
                _conditionError = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ConditionInvalid, problem);
                Revalidate();
                return;
            }
        }

        var hitCount = _otherHitCount ?? BreakpointModel.HitCountOf(int.Parse(PassCount.Trim(), NumberStyles.None, CultureInfo.InvariantCulture));
        Close(new BreakpointInfo(FullPath(File)!, int.Parse(Line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture), Enabled,
            condition.Length == 0 ? null : condition, hitCount, _logMessage));
    }
}
