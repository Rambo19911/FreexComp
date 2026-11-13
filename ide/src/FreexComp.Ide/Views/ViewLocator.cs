using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// IDE-ARCH-014: maps <c>XxxViewModel</c> to the control <c>FreexComp.Ide.Views.XxxView</c>. A missing view shows a
/// visible red placeholder instead of failing silently.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private const string Suffix = "ViewModel";

    /// <summary>The view type for <paramref name="viewModelType"/>, or null when there is none.</summary>
    public static Type? ViewTypeFor(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        var name = viewModelType.Name;
        if (!name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var viewType = typeof(ViewLocator).Assembly.GetType($"{typeof(ViewLocator).Namespace}.{name[..^Suffix.Length]}View");
        return viewType is not null && typeof(Control).IsAssignableFrom(viewType) && viewType.GetConstructor(Type.EmptyTypes) is not null
            ? viewType
            : null;
    }

    public bool Match(object? data) => data is ObservableObject && data.GetType().Name.EndsWith(Suffix, StringComparison.Ordinal);

    public Control Build(object? param)
    {
        ArgumentNullException.ThrowIfNull(param);
        if (ViewTypeFor(param.GetType()) is { } viewType)
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        var placeholder = new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, Strings.View_MissingView, param.GetType().FullName),
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcStatusErrorBrush"));
        return placeholder;
    }
}
