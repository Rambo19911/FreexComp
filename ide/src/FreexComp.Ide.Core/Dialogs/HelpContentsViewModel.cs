using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The Help Contents dialog (<c>help.contents</c>, I-257): the installed help pages by title, the first selected;
/// Open (default; needs a selected page) closes with that page, Cancel with none. 440 x 360, resizable.
/// </summary>
public sealed class HelpContentsViewModel : DialogViewModel<HelpPage>
{
    public const string DialogName = "HelpContents";

    private readonly ILayoutService? _layout;
    private readonly DialogButtonViewModel _open;
    private HelpPage? _selected;

    public HelpContentsViewModel(IReadOnlyList<HelpPage> pages, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _layout = layout;
        Pages = pages;
        _selected = pages.Count > 0 ? pages[0] : null;
        _open = new DialogButtonViewModel("Open", Strings.Button_Open, () => Close(Selected))
        {
            IsDefault = true,
            IsAccent = true,
            IsEnabled = _selected is not null,
        };
        Buttons = [_open, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => Strings.HelpContents_Title;

    public override double Width => 440;

    public override double? Height => 360;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>The pages in list order (by title).</summary>
    public IReadOnlyList<HelpPage> Pages { get; }

    /// <summary>The page Open shows; null only when there is no page.</summary>
    public HelpPage? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _open.IsEnabled = value is not null;
        }
    }
}
