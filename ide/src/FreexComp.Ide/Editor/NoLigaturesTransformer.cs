using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

namespace FreexComp.Ide.Editor;

/// <summary>
/// UI-TOK-021: ligatures are off in the editor. AvaloniaEdit's run properties carry no font features, so the shaper
/// would apply the font's defaults (<c>liga</c>, <c>clig</c>, <c>calt</c> on, e.g. <c>&lt;=</c> as one glyph in a
/// programming font). Installed first among the line transformers, this one gives every element run properties that
/// switch those features off; later transformers (syntax colours) change the same properties object.
/// AvaloniaEdit offers no public setter for an element's run properties, so its internal
/// <c>VisualLineElement.SetTextRunProperties</c> is used (pinned AvaloniaEdit version; <c>EditorHostingTests.LigaturesOff</c>
/// fails if it changes).
/// </summary>
public sealed class NoLigaturesTransformer : IVisualLineTransformer
{
    /// <summary>The features every editor run is shaped with.</summary>
    public static readonly FontFeatureCollection Features =
    [
        new FontFeature { Tag = "liga", Value = 0 },
        new FontFeature { Tag = "clig", Value = 0 },
        new FontFeature { Tag = "calt", Value = 0 },
    ];

    private static readonly Action<VisualLineElement, VisualLineElementTextRunProperties> SetProperties =
        typeof(VisualLineElement).GetMethod("SetTextRunProperties", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<VisualLineElement, VisualLineElementTextRunProperties>>();

    /// <summary>Puts the transformer in front of the text view's line transformers.</summary>
    public static void Install(TextView textView) => textView.LineTransformers.Insert(0, new NoLigaturesTransformer());

    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        foreach (var element in elements)
        {
            if (element.TextRunProperties is not Properties)
                SetProperties(element, new Properties(element.TextRunProperties));
        }
    }

    /// <summary>Run properties with the ligature features off; a clone (an element split by a colorizer) keeps them.</summary>
    private sealed class Properties(TextRunProperties source) : VisualLineElementTextRunProperties(source)
    {
        public override FontFeatureCollection FontFeatures => Features;

        public override VisualLineElementTextRunProperties Clone() => new Properties(this);
    }
}
