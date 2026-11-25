using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Help Contents dialog (<c>help.contents</c>, dialogs.md §28), 440 x 360, resizable: the page list
/// (<c>Dialog.HelpContents.Pages</c>; rows <c>Dialog.HelpContents.Page.&lt;id&gt;</c> showing the page title; UI-DLG-280).
/// Enter or a double click on a row opens the page (UI-DLG-281). Without pages the list is replaced by the centred
/// text <c>Dialog.HelpContents.Empty</c> (UI-DLG-282).
/// </summary>
public sealed class HelpContentsWindow : DialogWindow
{
    public HelpContentsWindow(HelpContentsViewModel model)
        : this(model, model.Pages.Count == 0 ? null : new ListBox { SelectionMode = SelectionMode.Single, IsTextSearchEnabled = true })
    {
    }

    private HelpContentsWindow(HelpContentsViewModel model, ListBox? list)
        : base(model, Body(model, list))
    {
        // Initial focus on the list — on its selected row, so that the arrow keys move from there.
        if (list is not null)
            Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSelected(list), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(HelpContentsViewModel.DialogName, field);

    private static void FocusSelected(ListBox list)
    {
        if (list.SelectedIndex >= 0)
            list.ContainerFromIndex(list.SelectedIndex)?.Focus(Avalonia.Input.NavigationMethod.Tab);
    }

    private static Control Body(HelpContentsViewModel model, ListBox? list)
    {
        if (list is null)
        {
            var empty = new TextBlock
            {
                Text = Strings.HelpContents_Empty,
                Classes = { "body" },
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            empty.Bind(TextBlock.ForegroundProperty, empty.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(empty, Id("Empty"));
            return empty;
        }

        AutomationProperties.SetAutomationId(list, Id("Pages"));
        AutomationProperties.SetName(list, Strings.HelpContents_Pages);
        foreach (var page in model.Pages)
        {
            list.Items.Add(CreateRow(page));
        }

        list.SelectedIndex = IndexOf(model, model.Selected);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                model.Selected = model.Pages[list.SelectedIndex];
        };
        list.DoubleTapped += async (_, e) =>
        {
            // UI-DLG-281: a double-click on a row (it is the selected one by then), not on the free area below the rows.
            if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
                await model.PressDefaultAsync();
        };
        return list;
    }

    private static int IndexOf(HelpContentsViewModel model, HelpPage? page)
    {
        for (var i = 0; i < model.Pages.Count; i++)
        {
            if (ReferenceEquals(model.Pages[i], page))
                return i;
        }

        return -1;
    }

    private static ListBoxItem CreateRow(HelpPage page)
    {
        var title = new TextBlock
        {
            Text = page.Title,
            Classes = { "body" },
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("FxcTextPrimaryBrush"));
        var item = new ListBoxItem { Content = title, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(item, Id("Page." + page.Id));
        AutomationProperties.SetName(item, page.Title);
        TextSearch.SetText(item, page.Title);
        return item;
    }
}
