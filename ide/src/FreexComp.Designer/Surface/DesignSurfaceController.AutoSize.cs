using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>
/// DSG-SUR-088: measured auto-size. The layout never measures (layout.md UIF-263); the designer stores the renderer's
/// preferred size (DSG-RND-016) as ordinary explicit bounds, in the same undo unit, when an auto-size control is created
/// by a palette click, pasted or copied, or one of its text-size properties is edited through the designer.
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>The properties whose edit re-measures an auto-size control (<c>Font.*</c> by prefix).</summary>
    private static readonly HashSet<string> AutoSizeTriggers =
        new(["Caption", "Font", "ParentFont", "WordWrap", "ShowAccelChar", "BorderStyle", "AutoSize"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The canvas the preferred sizes are measured with; default the deterministic rule of DSG-RND-003 (the result is an
    /// approximation of the run-time measurement either way, DSG-SUR-088).
    /// </summary>
    public IDesignCanvas MeasureCanvas { get; set; } = new RecordingCanvas();

    private static bool IsAutoSizeTrigger(string path) =>
        AutoSizeTriggers.Contains(path) || path.StartsWith("Font.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The objects whose text-size properties <paramref name="changes"/> set or remove.</summary>
    private IEnumerable<FormObject> AutoSizeTargets(IEnumerable<FormChange> changes) =>
        changes.Select(c => c switch
            {
                SetProperty s when IsAutoSizeTrigger(s.Path) => s.ObjectName,
                RemoveProperty r when IsAutoSizeTrigger(r.Path) => r.ObjectName,
                _ => null,
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Document.FindByQualifiedName)
            .OfType<FormObject>();

    /// <summary>
    /// Applies the measured bounds of every auto-size control among <paramref name="objects"/> (and their descendants
    /// with <paramref name="withDescendants"/>); returns the redo changes and their undo.
    /// </summary>
    private (IReadOnlyList<FormChange> Redo, IReadOnlyList<FormChange> Undo) ApplyAutoSize(IEnumerable<FormObject> objects,
        bool withDescendants = false)
    {
        IEnumerable<FormObject> Expand(FormObject o) => withDescendants ? [o, .. o.Children.SelectMany(Expand)] : [o];
        var changes = objects.SelectMany(Expand).SelectMany(AutoSizeChanges).ToList();
        return changes.Count == 0 ? ([], []) : (changes, Editor.Apply(changes));
    }

    /// <summary>
    /// The <c>SetProperty</c> changes that store <paramref name="obj"/>'s preferred size, when its renderer measures and
    /// its <c>AutoSize</c> is True: an aligned control takes only the dimension its <c>Align</c> leaves free;
    /// <c>taRightJustify</c> keeps the right edge (layout.md UIF-255).
    /// </summary>
    private IEnumerable<FormChange> AutoSizeChanges(FormObject obj)
    {
        if (obj.Info is null || obj.IsPreserved || obj.Parent is null
            || _layout.Renderers.Resolve(obj.Info.RendererId) is not IAutoSizeRenderer renderer)
        {
            yield break;
        }

        var bounds = PropertyAccess.GetBounds(obj);
        var ctx = new RenderContext(obj, bounds.Width, bounds.Height);
        if (!ctx.Get("AutoSize", false))
        {
            yield break;
        }

        var size = renderer.GetPreferredSize(ctx, MeasureCanvas);
        var align = ctx.Get("Align", "alNone");
        var (width, height) = (size.Width, size.Height);
        if (Is(align, "alClient"))
        {
            yield break;
        }

        if (Is(align, "alTop") || Is(align, "alBottom"))
        {
            width = bounds.Width;
        }
        else if (Is(align, "alLeft") || Is(align, "alRight"))
        {
            height = bounds.Height;
        }

        var left = bounds.Left;
        if (width != bounds.Width && Is(ctx.Get("Alignment", "taLeftJustify"), "taRightJustify"))
        {
            left += bounds.Width - width;
        }

        foreach (var (path, value, old) in new[] { ("Left", left, bounds.Left), ("Width", width, bounds.Width), ("Height", height, bounds.Height) })
        {
            if (value != old)
            {
                yield return new SetProperty(obj.QualifiedName, path, new IntegerValue(value, false));
            }
        }

        static bool Is(string value, string name) => value.Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
