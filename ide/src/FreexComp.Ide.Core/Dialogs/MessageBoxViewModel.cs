using System.Globalization;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>How a paragraph of a message is drawn (UI-DLG-021).</summary>
public enum MessageParagraphStyle
{
    /// <summary>Message text: <c>type.body</c>, <c>text.primary</c>.</summary>
    Main,

    /// <summary>A file path: <c>type.codeSmall</c>, <c>text.secondary</c>, one start-trimmed line, full path as tooltip.</summary>
    Path,

    /// <summary>An OS reason or other inserted detail: <c>type.body</c>, <c>text.secondary</c>.</summary>
    Secondary,
}

public sealed record MessageParagraph(string Text, MessageParagraphStyle Style);

/// <summary>
/// The generic message box (dialogs.md §2): text, icon kind, title and buttons of a <see cref="MessageDefinition"/>;
/// the result is the chosen button id (UI-DLG-024).
/// </summary>
public sealed class MessageBoxViewModel : DialogViewModel<string>
{
    public const string DialogName = "Message";
    public const string CancelId = "Cancel";
    public const string CopyDetailsId = "CopyDetails";

    private static readonly HashSet<string> NoMnemonic = new(["OK", CancelId], StringComparer.Ordinal);
    private readonly IClipboardService _clipboard;
    private bool _isDetailsExpanded;

    public MessageBoxViewModel(MessageRequest request, IClipboardService clipboard)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clipboard);
        _clipboard = clipboard;
        Request = request;
        Definition = MessageDefinitions.Get(request.MessageId);
        Paragraphs = Format(Definition.Text, request.Args);
        Text = string.Join(Environment.NewLine, Paragraphs.Select(p => p.Text));
        Buttons = CreateButtons();
    }

    public MessageRequest Request { get; }

    public MessageDefinition Definition { get; }

    public override string Name => DialogName;

    public override string Title => Definition.Title;

    public MessageKind Kind => Definition.Kind;

    /// <summary>Automation name of the icon (dialogs.md §24.2).</summary>
    public string KindName => Kind switch
    {
        MessageKind.Question => Strings.MessageBox_Kind_Question,
        MessageKind.Info => Strings.MessageBox_Kind_Info,
        MessageKind.Warning => Strings.MessageBox_Kind_Warning,
        _ => Strings.MessageBox_Kind_Error,
    };

    public IReadOnlyList<MessageParagraph> Paragraphs { get; }

    /// <summary>The whole message text, paragraphs separated by line breaks.</summary>
    public string Text { get; }

    public string? Details => Request.Details;

    public bool HasDetails => Request.Details is not null;

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set => SetProperty(ref _isDetailsExpanded, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>UI-DLG-024: <c>Dialog.Message.Button.&lt;Id&gt;</c>.</summary>
    public static string ButtonAutomationName(string buttonId) => "Button." + buttonId;

    /// <summary>
    /// ⏎ starts a paragraph (UI-DLG-021). A paragraph that is only a placeholder shows an inserted value: a fully
    /// qualified path is a path paragraph, anything else (an OS reason, a message) is secondary text.
    /// </summary>
    private static List<MessageParagraph> Format(string template, IReadOnlyList<string> args)
    {
        var values = args.Cast<object>().ToArray();
        var result = new List<MessageParagraph>();
        foreach (var paragraph in template.Split('\n'))
        {
            string text;
            try
            {
                text = string.Format(CultureInfo.CurrentCulture, paragraph, values);
            }
            catch (FormatException)
            {
                text = paragraph; // a caller passed too few arguments: show the template rather than fail
            }

            var style = MessageParagraphStyle.Main;
            if (paragraph.Length > 2 && paragraph[0] == '{' && paragraph[^1] == '}' && int.TryParse(paragraph[1..^1], out _))
                style = Path.IsPathFullyQualified(text) ? MessageParagraphStyle.Path : MessageParagraphStyle.Secondary;
            result.Add(new MessageParagraph(text, style));
        }

        return result;
    }

    private List<DialogButtonViewModel> CreateButtons()
    {
        var ids = Definition.Buttons.Where(id => id != CopyDetailsId || HasDetails).ToList();
        var commit = ids.Where(id => id != CopyDetailsId).ToList();
        var defaultId = commit[0];
        var cancelId = commit.Contains(CancelId) ? CancelId : commit[^1];
        var accent = defaultId != CancelId; // UI-DLG-024: a Cancel default is never accent-filled
        var used = new HashSet<char>();
        var buttons = new List<DialogButtonViewModel>();
        if (HasDetails)
        {
            buttons.Add(Mnemonic(new DialogButtonViewModel(CopyDetailsId, MessageDefinitions.ButtonLabel(CopyDetailsId), CopyDetailsAsync) { IsAuxiliary = true }, used));
        }

        foreach (var id in commit)
        {
            var button = new DialogButtonViewModel(id, MessageDefinitions.ButtonLabel(id), () => Close(id))
            {
                IsDefault = id == defaultId,
                IsCancel = id == cancelId,
                IsAccent = accent && id == defaultId,
            };
            buttons.Add(NoMnemonic.Contains(id) ? button : Mnemonic(button, used));
        }

        return buttons;
    }

    /// <summary>UI-DLG-024: the first letter of the label not yet used in the dialog.</summary>
    private static DialogButtonViewModel Mnemonic(DialogButtonViewModel button, HashSet<char> used)
    {
        var label = button.Label;
        for (var i = 0; i < label.Length; i++)
        {
            if (char.IsLetter(label[i]) && used.Add(char.ToUpperInvariant(label[i])))
            {
                button.AccessLabel = label[..i].Replace("_", "__", StringComparison.Ordinal) + "_" + label[i..].Replace("_", "__", StringComparison.Ordinal);
                break;
            }
        }

        return button;
    }

    private Task CopyDetailsAsync() => _clipboard.SetTextAsync(Text + Environment.NewLine + Environment.NewLine + Details);
}
