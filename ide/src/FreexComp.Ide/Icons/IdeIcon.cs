using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FreexComp.Ide.Icons;

/// <summary>
/// Draws an icon of <see cref="IconCatalog"/> as vector geometry (UI-TOK-042) in its design-token colours, which
/// follow the theme (UI-TOK-003); when disabled every layer uses <c>text.disabled</c> (UI-TOK-041). The 16 × 16 grid
/// is scaled to the control's size; the default size is 16.
/// </summary>
public sealed class IdeIcon : Control
{
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<IdeIcon, string?>(nameof(IconName));

    private const double Grid = 16;
    private const string DisabledToken = "text.disabled";
    private Icon? _icon;

    static IdeIcon()
    {
        AffectsRender<IdeIcon>(IconNameProperty, IsEffectivelyEnabledProperty);
    }

    public IdeIcon()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (_icon is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / Grid, Bounds.Height / Grid)))
        {
            foreach (var layer in _icon.Layers)
            {
                var brush = Brush(IsEffectivelyEnabled ? layer.ColorToken : DisabledToken);
                if (layer.IsFilled)
                {
                    context.DrawGeometry(brush, null, layer.Geometry);
                }
                else
                {
                    context.DrawGeometry(null, new Pen(brush, IconCatalog.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), layer.Geometry);
                }
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Grid, Grid);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
        {
            _icon = IconName is { } name && IconCatalog.TryGet(name, out var icon) ? icon : null;
        }
    }

    private IBrush? Brush(string token) =>
        this.TryFindResource(IconCatalog.BrushKey(token), ActualThemeVariant, out var value) ? value as IBrush : null;
}
