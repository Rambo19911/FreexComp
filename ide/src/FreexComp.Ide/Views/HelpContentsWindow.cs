using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Help Contents dialog (<c>help.contents</c>, I-257), 440 x 360, resizable: the page list
/// (<c>Dialog.HelpContents.Pages</c>; rows <c>Dialog.HelpContents.Page.&lt;id&gt;</c> showing the page title). Enter or a
/// double click on a row opens the page. Without pages the list is replaced by <c>Dialog.HelpContents.Empty</c>.
/// </summary>
public sealed class HelpContentsWindow(HelpContentsViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(HelpContentsViewModel.DialogName, field);

    private static Control Body(HelpContentsViewModel model)
    {
        if (model.Pages.Count == 0)
        {
            var empty = new TextBlock { Text = Strings.HelpContents_Empty, Classes = { "body" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            empty.Bind(TextBlock.ForegroundProperty, empty.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(empty, Id("Empty"));
            return empty;
        }

        var list = new ListBox { SelectionMode = SelectionMode.Single, IsTextSearchEnabled = true };
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
        list.DoubleTapped += async (_, _) => await model.PressDefaultAsync();
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
