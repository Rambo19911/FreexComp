using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>One rendered variant of a subject.</summary>
public sealed record SnapshotFile(string Path, int PixelWidth, int PixelHeight, int DistinctColors, double MeanLuminance);

/// <summary>
/// I-240: renders a window of the headless IDE with the real Skia renderer to PNG files for the visual check of a
/// Phase 8 UI card, and measures its layout. Rendering needs the Skia process of the test (<see cref="SkiaProcess"/>).
/// Files: <c>&lt;repo&gt;/artifacts/visual/&lt;subject&gt;/&lt;theme&gt;-&lt;scale&gt;x-&lt;w&gt;x&lt;h&gt;.png</c>.
/// </summary>
public static class VisualSnapshot
{
    private const double Tolerance = 0.5;

    public static string FolderOf(string subject) => Path.Combine(SpecTables.RepoRootPath(), "artifacts", "visual", subject);

    public static string FileName(string theme, double scale, Size size) => string.Create(CultureInfo.InvariantCulture,
        $"{theme}-{scale:0.###}x-{size.Width:0}x{size.Height:0}.png");

    /// <summary>
    /// Gives the window this client size and render scale and lays it out. A size below the window's minimum lowers
    /// the minimum, like a small working area does (UI-DLG-005).
    /// </summary>
    public static void Arrange(Window window, Size size, double scale)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.MinWidth = Math.Min(window.MinWidth, size.Width);
        window.MinHeight = Math.Min(window.MinHeight, size.Height);
        window.Width = size.Width;
        window.Height = size.Height;
        window.SetRenderScaling(scale);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Renders the window as it is now and writes the PNG of this variant.</summary>
    public static SnapshotFile Capture(Window window, string subject, string theme)
    {
        ArgumentNullException.ThrowIfNull(window);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame was rendered: not in the Skia process of the test (SkiaProcess).");
        var folder = FolderOf(subject);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName(theme, window.RenderScaling, window.ClientSize));
        frame.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        var pixels = Pixels(frame, new PixelRect(frame.PixelSize));
        double luminance = 0;
        foreach (var pixel in pixels)
        {
            // Sum of the three colour bytes: the same for BGRA and RGBA frames.
            luminance += (pixel & 0xFF) + ((pixel >> 8) & 0xFF) + ((pixel >> 16) & 0xFF);
        }

        return new SnapshotFile(path, frame.PixelSize.Width, frame.PixelSize.Height, pixels.Distinct().Count(), luminance / (3.0 * pixels.Length));
    }

    /// <summary>
    /// The number of different colours the window draws inside the bounds of <paramref name="control"/> (1 = a flat
    /// area: nothing is drawn there).
    /// </summary>
    public static int DistinctColors(Window window, Control control)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(control);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame was rendered: not in the Skia process of the test (SkiaProcess).");
        var origin = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("The control is not in the window.");
        var area = PixelRect.FromRect(new Rect(origin, control.Bounds.Size), window.RenderScaling).Intersect(new PixelRect(frame.PixelSize));
        return Pixels(frame, area).Distinct().Count();
    }

    private static int[] Pixels(Avalonia.Media.Imaging.WriteableBitmap frame, PixelRect area)
    {
        using var locked = frame.Lock();
        var pixels = new int[area.Width * area.Height];
        for (var y = 0; y < area.Height; y++)
            Marshal.Copy(locked.Address + (area.Y + y) * locked.RowBytes + area.X * 4, pixels, y * area.Width, area.Width);
        return pixels;
    }

    /// <summary>
    /// Null when the whole box of <paramref name="control"/> lies inside every control above it up to the window (so
    /// that no ancestor can cut it off); otherwise the box (window coordinates) and the first ancestor it leaves.
    /// Scrolled content may leave its view port in the direction in which it scrolls.
    /// </summary>
    public static string? HiddenPart(Control control, Window window)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(window);
        Dispatcher.UIThread.RunJobs();
        if (control.TranslatePoint(default, window) is not { } origin)
            return "not in the window";
        var box = new Rect(origin, control.Bounds.Size);
        var (scrollsX, scrollsY) = (false, false);
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor.TranslatePoint(default, window) is not { } at)
                continue;
            var room = new Rect(at, ancestor.Bounds.Size).Inflate(Tolerance);
            if (ancestor is ScrollContentPresenter port)
            {
                scrollsX |= port.Extent.Width > port.Viewport.Width + Tolerance;
                scrollsY |= port.Extent.Height > port.Viewport.Height + Tolerance;
            }

            if (scrollsX)
                room = room.WithX(box.X).WithWidth(box.Width);
            if (scrollsY)
                room = room.WithY(box.Y).WithHeight(box.Height);

            if (!room.Contains(box))
                return $"{box} outside {Describe(ancestor)} {new Rect(at, ancestor.Bounds.Size)}";
        }

        return null;
    }

    /// <summary>The width the whole text of the block needs on one line, in its own font (no trimming, no wrapping).</summary>
    public static double TextWidth(TextBlock text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        return new FormattedText(text.Text ?? "", CultureInfo.CurrentCulture, text.FlowDirection, typeface, text.FontSize, null).WidthIncludingTrailingWhitespace;
    }

    /// <summary>Width and height in the header of a PNG file.</summary>
    public static (int Width, int Height) PngSize(string path)
    {
        var header = new byte[24];
        using (var stream = File.OpenRead(path))
            stream.ReadExactly(header);
        if (!header.AsSpan(0, 8).SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            throw new InvalidDataException(path + " is not a PNG file.");
        return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }

    /// <summary>
    /// The measurable layout rules of the visual check, as one line per violation (empty = clean):
    /// every visible control lies inside its parent (content of a scroll viewer may extend beyond the view port);
    /// no text block without trimming is arranged smaller than its text; no two children of a stacking, docking or
    /// grid panel that are in different cells overlap; the value of a numeric field fits its text box.
    /// </summary>
    public static IReadOnlyList<string> LayoutDefects(Window window) => LayoutDefects((Control)window);

    /// <summary>The same rules for the controls inside <paramref name="root"/> only.</summary>
    public static IReadOnlyList<string> LayoutDefects(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Dispatcher.UIThread.RunJobs();
        var defects = new List<string>();
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Where(IsShown))
        {
            // Not rules of a parent: scrolled content, the scaled child of a view box, a negative margin (the
            // control is meant to cover its parent's edge by that much), and the parts of a scroll bar track (the
            // toolkit places the thumb and the page areas itself from the scroll offset; the invisible page area can
            // end a rounded pixel past the track).
            if (control.GetVisualParent() is Control parent and not ScrollContentPresenter and not Viewbox and not Avalonia.Controls.Primitives.Track)
            {
                var margin = control.Margin;
                var room = new Rect(parent.Bounds.Size).Inflate(new Thickness(
                    Tolerance + Math.Max(0, -margin.Left), Tolerance + Math.Max(0, -margin.Top),
                    Tolerance + Math.Max(0, -margin.Right), Tolerance + Math.Max(0, -margin.Bottom)));
                if (!room.Contains(control.Bounds))
                    defects.Add($"clipped: {Describe(control)} bounds {control.Bounds} outside parent {Describe(parent)} {parent.Bounds.Size}");
            }

            // A numeric field shows its whole value: the text does not scroll inside the text box.
            if (control is NumericUpDown numeric && numeric.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } view
                && view.Extent.Width > view.Viewport.Width + Tolerance)
            {
                defects.Add($"truncated: {Describe(numeric)} value '{numeric.Text}' needs {view.Extent.Width}, has {view.Viewport.Width}");
            }

            // The arranged box holds the text: measured against the desired size (width) and against the lines the text
            // block really lays out (a text block measured with too little height still reports that height as desired).
            if (control is TextBlock { TextTrimming: var trimming } text && trimming == TextTrimming.None)
            {
                var desired = text.DesiredSize.Deflate(text.Margin);
                var lines = text.TextLayout;
                var box = text.Bounds.Size.Deflate(text.Padding);
                if (desired.Width > text.Bounds.Width + Tolerance || desired.Height > text.Bounds.Height + Tolerance
                    || lines.Width > box.Width + Tolerance || lines.Height > box.Height + Tolerance)
                {
                    defects.Add($"truncated: {Describe(text)} needs {Math.Max(desired.Width, lines.Width):0.#} x {Math.Max(desired.Height, lines.Height):0.#}, has {text.Bounds.Size}");
                }
            }

            if (control is StackPanel or DockPanel or WrapPanel or Grid)
                AddOverlaps((Panel)control, defects);
        }

        return defects;
    }

    private static void AddOverlaps(Panel panel, List<string> defects)
    {
        var children = panel.Children.Where(IsShown).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            for (var j = i + 1; j < children.Count; j++)
            {
                var (a, b) = (children[i], children[j]);
                if (panel is Grid && SharesCell(a, b))
                    continue; // layered on purpose
                var common = a.Bounds.Intersect(b.Bounds);
                if (common.Width > Tolerance && common.Height > Tolerance)
                    defects.Add($"overlap: {Describe(a)} {a.Bounds} and {Describe(b)} {b.Bounds} in {Describe(panel)}");
            }
        }
    }

    private static bool SharesCell(Control a, Control b) =>
        Grid.GetRow(a) < Grid.GetRow(b) + Grid.GetRowSpan(b) && Grid.GetRow(b) < Grid.GetRow(a) + Grid.GetRowSpan(a)
        && Grid.GetColumn(a) < Grid.GetColumn(b) + Grid.GetColumnSpan(b) && Grid.GetColumn(b) < Grid.GetColumn(a) + Grid.GetColumnSpan(a);

    private static bool IsShown(Control control) => control is not Avalonia.Controls.Primitives.Popup && control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0;

    /// <summary>Type, own text or name, and the nearest AutomationId at or above the control.</summary>
    private static string Describe(Control control)
    {
        var id = control.GetSelfAndVisualAncestors().OfType<StyledElement>().Select(AutomationIds.Get).FirstOrDefault(i => !string.IsNullOrEmpty(i));
        var text = control switch
        {
            TextBlock block => $" '{block.Text}'",
            { Name.Length: > 0 } => $" #{control.Name}",
            _ => "",
        };
        return $"{control.GetType().Name}{text} [{id}]";
    }
}
