using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace FreexComp.Ide.Designer;

/// <summary>
/// An indeterminate progress ring (DSG-TP-003: the Design view of a form whose component metadata is still loading):
/// a <c>bg.sunken</c> track with an <c>accent.default</c> quarter arc that turns once a second while it is shown
/// (UI-TOK-051 allows progress animations).
/// </summary>
public sealed class ProgressRing : Control
{
    private const double Size = 32;
    private const double Stroke = 3;

    /// <summary>The turn of the arc in degrees (animated while the ring is shown).</summary>
    public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<ProgressRing, double>(nameof(Angle));

    private CancellationTokenSource? _spin;

    static ProgressRing() => AffectsRender<ProgressRing>(AngleProperty);

    public ProgressRing()
    {
        Width = Size;
        Height = Size;
    }

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _spin = new CancellationTokenSource();
        var turn = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(AngleProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(AngleProperty, 360d) } },
            },
        };
        _ = turn.RunAsync(this, _spin.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _spin?.Cancel();
        _spin?.Dispose();
        _spin = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var radius = (Size - Stroke) / 2;
        var center = new Point(Size / 2, Size / 2);
        if (this.TryFindResource("FxcBgSunkenBrush", ActualThemeVariant, out var track) && track is IBrush trackBrush)
            context.DrawEllipse(null, new Pen(trackBrush, Stroke), center, radius, radius);
        if (this.TryFindResource("FxcAccentDefaultBrush", ActualThemeVariant, out var accent) && accent is IBrush accentBrush)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(center.X, center.Y - radius), false);
                g.ArcTo(new Point(center.X + radius, center.Y), new Avalonia.Size(radius, radius), 0, false, SweepDirection.Clockwise);
            }

            var turn = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(Matrix.ToRadians(Angle))
                * Matrix.CreateTranslation(center.X, center.Y);
            using (context.PushTransform(turn))
                context.DrawGeometry(null, new Pen(accentBrush, Stroke, lineCap: PenLineCap.Round), arc);
        }
    }
}
