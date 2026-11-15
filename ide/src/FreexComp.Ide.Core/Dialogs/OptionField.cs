using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>How an Options field is edited (dialogs.md §9).</summary>
public enum OptionFieldKind
{
    /// <summary>Check box of a <c>bool</c> key.</summary>
    Check,

    /// <summary>Integer input of an <c>int</c> key; typed text is validated against the key's range.</summary>
    Integer,

    /// <summary>Combo of a <c>string</c> key; the entries are <see cref="OptionField.Choices"/>.</summary>
    Choice,

    /// <summary>Folder text field with a Browse button of a <c>string</c> key; typed text is checked by <see cref="OptionField.TextValidator"/>.</summary>
    Path,
}

/// <summary>One entry of a combo field: the stored value and its display text.</summary>
public sealed record OptionChoice(string Value, string Display);

/// <summary>
/// One field of an Options page, bound to exactly one settings key (UI-DLG-094). <see cref="Value"/> is the pending
/// value (working copy, UI-DLG-093); <see cref="Text"/> the integer field's text, which sets <see cref="Value"/> only when
/// it is valid, else <see cref="Error"/>. <see cref="Label"/> carries the mnemonic as <c>_x</c>.
/// </summary>
public sealed class OptionField : ObservableObject
{
    private readonly Func<string>? _rangeMessage;
    private object _value;
    private string _text;
    private string? _error;
    private bool _isHighlighted;
    private bool _isEnabled = true;

    internal OptionField(string id, string label, ISettingKey key, OptionFieldKind kind, object value, int phase, Func<string>? rangeMessage)
    {
        Id = id;
        Label = label;
        Key = key;
        Kind = kind;
        Phase = phase;
        _rangeMessage = rangeMessage;
        _value = value;
        _text = Format(value);
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>The label without the mnemonic marker (filter text, UI-DLG-091).</summary>
    public string LabelText => Label.Replace("__", "\0", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace('\0', '_');

    public ISettingKey Key { get; }

    public OptionFieldKind Kind { get; }

    /// <summary>The phase the field takes effect in; later phases are shown disabled.</summary>
    public int Phase { get; }

    /// <summary>A group heading (<c>type.bodyStrong</c>) shown above this field, or null.</summary>
    public string? GroupHeading { get; init; }

    /// <summary>Id of a check-box field that must be checked for this field to be enabled, or null.</summary>
    public string? EnabledBy { get; init; }

    /// <summary>A <see cref="OptionFieldKind.Path"/> field: true for an acceptable text (it becomes the value).</summary>
    public Func<string, bool>? TextValidator { get; init; }

    /// <summary>Placeholder of a <see cref="OptionFieldKind.Path"/> field (shown while it is empty).</summary>
    public string? Placeholder { get; init; }

    /// <summary>
    /// A toolchain folder field (dialogs.md §9.9): checks the effective folder of a text (explicit, or the automatic one for
    /// an empty text; null = not found). An invalid explicit folder is an <see cref="Error"/> (blocks OK); a valid one or a
    /// failing automatic one is the <see cref="Status"/> line.
    /// </summary>
    public Func<string, Build.FolderCheck?>? FolderCheck
    {
        get => _folderCheck;
        init
        {
            _folderCheck = value;
            _error = CheckFolder(_text) ? null : _folderError;
        }
    }

    private readonly Func<string, Build.FolderCheck?>? _folderCheck;
    private string? _status;
    private bool _statusIsWarning;

    /// <summary>The status line under a toolchain folder field (<c>&lt;Field&gt;.Status</c>), or null.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>The status is the warning <c>Msg.Options.ToolchainAutoNotFound</c> (else a success).</summary>
    public bool StatusIsWarning
    {
        get => _statusIsWarning;
        private set => SetProperty(ref _statusIsWarning, value);
    }

    /// <summary>Applies <see cref="FolderCheck"/> to <paramref name="text"/>: true when the text is acceptable.</summary>
    private bool CheckFolder(string text)
    {
        _folderError = null;
        if (_folderCheck is null)
            return true;
        var check = _folderCheck(text.Trim());
        string Message(Build.FolderCheck c) =>
            string.Format(CultureInfo.CurrentCulture, Strings.ResourceManager.GetString(c.MessageId.Replace('.', '_'), CultureInfo.CurrentCulture) ?? c.MessageId,
                [.. c.Args]);
        if (text.Trim().Length > 0 && check is { Valid: false })
        {
            Status = null;
            _folderError = Message(check);
            return false;
        }

        StatusIsWarning = check is null or { Valid: false };
        Status = StatusIsWarning ? Strings.Msg_Options_ToolchainAutoNotFound : Message(check!);
        return true;
    }

    private string? _folderError;

    /// <summary>The combo entries of a <see cref="OptionFieldKind.Choice"/> field.</summary>
    public System.Collections.ObjectModel.ObservableCollection<OptionChoice> Choices { get; } = [];

    /// <summary>The pending value (<c>bool</c>, <c>int</c> or <c>string</c>).</summary>
    public object Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Key.IsValid(value))
                throw new ArgumentException($"Invalid value for '{Key.Name}'.", nameof(value));
            var changed = !SettingValues.AreEqual(_value, value);
            _value = value;
            if (Kind == OptionFieldKind.Choice && value is string choice && Choices.All(c => c.Value != choice))
                Choices.Add(new OptionChoice(choice, choice.Length == 0 ? Strings.Options_DefaultChoice : choice));
            SetProperty(ref _text, Format(value), nameof(Text));
            Error = CheckFolder(_text) ? null : _folderError;
            if (changed)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanReset));
            }
        }
    }

    /// <summary>The integer or path field's text; a valid number in range (a valid path) becomes <see cref="Value"/>.</summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _text, value))
                return;
            object? parsed = Kind switch
            {
                OptionFieldKind.Integer when int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    && Key.IsValid(number) => number,
                OptionFieldKind.Path when TextValidator?.Invoke(value) != false && CheckFolder(value) => value,
                _ => null,
            };
            if (parsed is not null)
            {
                var changed = !SettingValues.AreEqual(_value, parsed);
                _value = parsed;
                Error = null;
                if (changed)
                    OnPropertyChanged(nameof(Value));
            }
            else
            {
                Error = _folderError ?? _rangeMessage?.Invoke() ?? "";
            }

            OnPropertyChanged(nameof(CanReset));
        }
    }

    /// <summary>The inline message under the field (UI-DLG-006), or null.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The label matches the filter (UI-DLG-091).</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        internal set => SetProperty(ref _isHighlighted, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        internal set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>UI-DLG-094: the ↺ button is shown while the pending value differs from the key's default.</summary>
    public bool CanReset => Error is not null || !SettingValues.AreEqual(_value, Key.DefaultValue);

    /// <summary>"Reset to default: &lt;default&gt;".</summary>
    public string ResetToolTip => string.Format(CultureInfo.CurrentCulture, Strings.Options_ResetToDefault, DefaultText);

    /// <summary>The ↺ button: the pending value becomes the key's default.</summary>
    public void Reset() => Value = Key.DefaultValue;

    private string DefaultText => Key.DefaultValue switch
    {
        bool b => b ? Strings.Options_On : Strings.Options_Off,
        "" when Kind == OptionFieldKind.Choice => Choices.FirstOrDefault(c => c.Value.Length == 0)?.Display ?? Strings.Options_DefaultChoice,
        string s when Kind == OptionFieldKind.Choice && Choices.FirstOrDefault(c => c.Value == s) is { } choice => choice.Display,
        "" when Kind == OptionFieldKind.Path => Placeholder ?? "",
        var v => Format(v),
    };

    private static string Format(object value) => value switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => "",
    };
}
