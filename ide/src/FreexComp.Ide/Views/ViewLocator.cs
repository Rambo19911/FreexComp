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

        var viewName = name[..^Suffix.Length] + "View";
        var viewType = typeof(ViewLocator).Assembly.GetType($"{typeof(ViewLocator).Namespace}.{viewName}")
            ?? SiblingView(viewModelType, viewName);
        return viewType is not null && typeof(Control).IsAssignableFrom(viewType) && viewType.GetConstructor(Type.EmptyTypes) is not null
            ? viewType
            : null;
    }

    /// <summary>
    /// A view-model outside the IDE's own assemblies (a later feature or a test, IDE-ARCH-100) brings its view in its own
    /// assembly: namespace <c>X.ViewModels</c> → <c>X.Views</c>.
    /// </summary>
    private static Type? SiblingView(Type viewModelType, string viewName) =>
        viewModelType.Namespace is { } ns && ns.EndsWith(".ViewModels", StringComparison.Ordinal)
            ? viewModelType.Assembly.GetType($"{ns[..^".ViewModels".Length]}.Views.{viewName}")
            : null;

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
