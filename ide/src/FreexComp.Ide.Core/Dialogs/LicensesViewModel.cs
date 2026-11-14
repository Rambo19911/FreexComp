using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The Third-Party Licenses dialog (dialogs.md §16, UI-DLG-160): components sorted by name, the first selected; the text
/// pane shows <c>&lt;name&gt; &lt;version&gt; — &lt;license&gt;</c>, a blank line and the full license text. 760 × 520, resizable.
/// </summary>
public sealed class LicensesViewModel : DialogViewModel<bool>
{
    public const string DialogName = "Licenses";

    private readonly ILayoutService? _layout;
    private ThirdPartyNotice? _selected;

    public LicensesViewModel(IEnumerable<ThirdPartyNotice> notices, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(notices);
        _layout = layout;
        Components = [.. notices.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)];
        _selected = Components.FirstOrDefault();
        Buttons = [new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(true)) { IsDefault = true, IsCancel = true, IsAccent = true }];
    }

    public override string Name => DialogName;

    public override string Title => Strings.Licenses_Title;

    public override double Width => 760;

    public override double? Height => 520;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public IReadOnlyList<ThirdPartyNotice> Components { get; }

    public ThirdPartyNotice? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(Text));
        }
    }

    public string Text => Selected is { } n
        ? $"{n.Name} {n.Version} — {n.License}{Environment.NewLine}{Environment.NewLine}{n.Text}"
        : "";
}
