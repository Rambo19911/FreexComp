using System.Globalization;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The strings editor (object-inspector.md §5.5, DSG-OI-055): a multi-line text of a <c>TStrings</c> property, resizable,
/// min 480 × 240. The result is the list of lines (CR LF, LF or CR separate them; a single trailing line break adds no
/// line); Cancel, Escape or × give null. Enter inserts a line break, Ctrl+Enter is OK (the window's).
/// <c>Load…</c> replaces the whole text by a text file, <c>Save…</c> writes the text to one (DSG-OI-130 … 134); nothing
/// reaches the form before OK.
/// </summary>
public sealed class StringsEditorViewModel : DialogViewModel<IReadOnlyList<string>>
{
    public const string DialogName = "StringsEditor";

    /// <summary>DSG-OI-131: a larger file is not loaded.</summary>
    public const int MaxLoadBytes = 1_048_576;

    private readonly FilePrompts? _prompts;
    private readonly IDialogService? _dialogs;
    private readonly string _suggestedName;
    private string _text;

    /// <param name="property">The property name (<c>Lines</c>).</param>
    /// <param name="component">The component name, or <c>{n} components</c> for a multi-selection.</param>
    /// <param name="lines">The initial lines (of the primary component).</param>
    /// <param name="prompts">The pickers of <c>Load…</c> / <c>Save…</c>, with the file system and the settings; null: no such buttons.</param>
    /// <param name="dialogs">Shows the message boxes of a refused load and a failed save; null: no such buttons.</param>
    /// <param name="suggestedName">The file name <c>Save…</c> suggests (<see cref="SuggestedFileName"/>); null: from <paramref name="property"/>.</param>
    public StringsEditorViewModel(string property, string component, IReadOnlyList<string> lines, FilePrompts? prompts = null,
        IDialogService? dialogs = null, string? suggestedName = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(lines);
        Title = string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_Title, property, component);
        _text = Join(lines);
        _prompts = prompts;
        _dialogs = dialogs;
        _suggestedName = suggestedName ?? SuggestedFileName(property, null);
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, () => Close(Split(Text))) { IsDefault = true, IsAccent = true };
        var cancel = new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true };
        if (HasFileButtons)
        {
            // UI-DLG-344: left on the strip, outside the commit / cancel set.
            Load = FileButton("Load", Strings.StringsEditor_Load, LoadAsync);
            Save = FileButton("Save", Strings.StringsEditor_Save, SaveAsync);
            Buttons = [Load, Save, Ok, cancel];
        }
        else
        {
            Buttons = [Ok, cancel];
        }
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 480;

    public override double? Height => 240;

    public override bool CanResize => true;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    /// <summary><c>Load…</c> (<c>Dialog.StringsEditor.Load</c>); null without the file services.</summary>
    public DialogButtonViewModel? Load { get; }

    /// <summary><c>Save…</c> (<c>Dialog.StringsEditor.Save</c>); null without the file services.</summary>
    public DialogButtonViewModel? Save { get; }

    /// <summary>True when the editor can load and save text files (it was given the pickers and the message boxes).</summary>
    public bool HasFileButtons => _prompts is not null && _dialogs is not null;

    /// <summary>A file's text has replaced <see cref="Text"/> (the view shows it with the caret at the start, DSG-OI-130).</summary>
    public event EventHandler? TextLoaded;

    /// <summary>A <c>Load…</c> or <c>Save…</c> has ended — done, canceled or refused: the focus returns to the text (UI-DLG-345).</summary>
    public event EventHandler? FileActionCompleted;

    /// <summary>The edited text (<c>Dialog.StringsEditor.Text</c>).</summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(LineCountText));
            }
        }
    }

    /// <summary><c>{n} lines</c> / <c>1 line</c> (<c>Dialog.StringsEditor.LineCount</c>).</summary>
    public string LineCountText => Split(Text).Count is var n && n == 1
        ? Strings.StringsEditor_LineOne
        : string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_LinesMany, n);

    /// <summary>DSG-OI-132: <c>{Component}.{Property}.txt</c>; without a single component (a multi-selection) <c>{Property}.txt</c>.</summary>
    public static string SuggestedFileName(string property, string? component)
    {
        ArgumentNullException.ThrowIfNull(property);
        return (component is null ? property : component + "." + property) + ".txt";
    }

    /// <summary>
    /// DSG-OI-130, 131: the open picker; the chosen file — at most <see cref="MaxLoadBytes"/>, not binary, decoded by the
    /// editor's detection (IDE-P1-071 … 073) — replaces the whole text. A refused file leaves the text and shows one message.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_prompts is not { } prompts || _dialogs is not { } dialogs)
        {
            return;
        }

        try
        {
            if (await prompts.StringsLoadAsync() is not { } path)
            {
                return;
            }

            var fileSystem = prompts.FileSystem;
            var (text, refusal) = await Task.Run(() => Read(fileSystem, path)); // IDE-ARCH-081
            if (refusal is not null)
            {
                await dialogs.ShowMessageAsync(refusal);
                return;
            }

            Text = text;
            TextLoaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            FileActionCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// DSG-OI-132, 133: the save picker; the current text is written — every line followed by the line break of
    /// <c>files.defaultLineEnding</c>, in <c>files.defaultEncoding</c> (UTF-8 instead of a code page that cannot hold the
    /// text), atomically. A failed write shows one message and leaves no file.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_prompts is not { } prompts || _dialogs is not { } dialogs)
        {
            return;
        }

        try
        {
            if (await prompts.StringsSaveAsync(_suggestedName) is not { } path)
            {
                return;
            }

            var lineBreak = prompts.Settings.Get(SettingsCatalog.FilesDefaultLineEnding) == "lf" ? "\n" : "\r\n";
            var encoding = FileEncodings.FromId(prompts.Settings.Get(SettingsCatalog.FilesDefaultEncoding)) ?? FileEncoding.Utf8;
            var text = string.Concat(Split(Text).Select(line => line + lineBreak));
            if (!FileEncodings.CanEncode(text, encoding))
            {
                encoding = FileEncoding.Utf8; // never lossy, no question
            }

            var bytes = FileEncodings.Encode(text, encoding);
            var fileSystem = prompts.FileSystem;
            var reason = await Task.Run(() =>
            {
                try
                {
                    AtomicFileWriter.Write(fileSystem, path, bytes); // IDE-P1-081
                    return null;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return e.Message;
                }
            });
            if (reason is not null)
            {
                await dialogs.ShowMessageAsync(new MessageRequest("Msg.StringsEditor.SaveFailed", [path, reason]));
            }
        }
        finally
        {
            FileActionCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The text of the file as the editor shows it, or the message that refuses it (DSG-OI-131).</summary>
    private static (string Text, MessageRequest? Refusal) Read(Platform.IFileSystem fileSystem, string path)
    {
        try
        {
            if (fileSystem.GetLength(path) > MaxLoadBytes)
            {
                return ("", new MessageRequest("Msg.StringsEditor.FileTooLarge", [path]));
            }

            var detection = EncodingDetector.Detect(fileSystem.ReadAllBytes(path));
            return detection.IsBinary
                ? ("", new MessageRequest("Msg.StringsEditor.NotText", [path]))
                : (Join(Split(detection.Text)), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ("", new MessageRequest("Msg.StringsEditor.LoadFailed", [path, e.Message]));
        }
    }

    private static DialogButtonViewModel FileButton(string id, string label, Func<Task> execute) =>
        new(id, label.Replace("&", "", StringComparison.Ordinal), execute)
        {
            IsAuxiliary = true,
            AccessLabel = label.Replace("_", "__", StringComparison.Ordinal).Replace('&', '_'),
        };

    /// <summary>
    /// The text that shows <paramref name="lines"/>: joined with CR LF; a last line that is empty gets its line break, so
    /// that <see cref="Split"/> gives the same lines back.
    /// </summary>
    public static string Join(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join("\r\n", lines) + (lines is [.., ""] ? "\r\n" : "");
    }

    /// <summary>DSG-OI-055: the lines of <paramref name="text"/>; an empty text has none.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
