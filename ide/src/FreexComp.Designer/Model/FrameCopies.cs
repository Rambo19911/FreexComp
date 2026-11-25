using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Model;

/// <summary>
/// Frame instances and inherited objects leaving or entering a document (frames-designer.md DSG-SUR-230): the clipboard
/// copy of inherited controls, and instances rebuilt from snapshots (paste, redo).
/// </summary>
internal static class FrameCopies
{
    /// <summary>
    /// DSG-SUR-230: an inherited object as an ordinary component with its <b>effective</b> lines and its inherited children
    /// likewise; an inherited instance (a frame nested in the frame) stays an instance with its effective own lines and its
    /// effective override blocks. The copy hangs under a holder so the writer treats it as a child of a root.
    /// </summary>
    public static FormObject Detach(FormObject obj)
    {
        var holder = new FormObject(FormObjectKind.Object, "Holder", "THolder", null, default) { Catalog = obj.Catalog };
        holder.AddChild(DetachTree(obj));
        return holder.Children[0];
    }

    private static FormObject DetachTree(FormObject obj)
    {
        if (obj.IsFrameInstance)
        {
            // An instance's own lines come from the instances along the chain, not from the frame's root.
            var instance = Copy(obj, FormObjectKind.Inline, Chain(obj).TakeWhile(o => o.Parent is not null));
            instance.InheritedFrom = obj.InheritedFrom; // written with override blocks (DSG-FD-113)
            foreach (var child in obj.Children)
            {
                instance.AddChild(Block(child));
            }

            return instance;
        }

        var copy = Copy(obj, FormObjectKind.Object, Chain(obj));
        foreach (var child in obj.Children)
        {
            copy.AddChild(DetachTree(child));
        }

        return copy;
    }

    /// <summary>An override block of a detached instance: the lines the chain overrides above the frame's own object.</summary>
    private static FormObject Block(FormObject obj)
    {
        var block = Copy(obj, FormObjectKind.Inherited, Chain(obj).TakeWhile(o => o.IsInherited));
        block.IsInherited = true;
        block.InheritedFrom = obj.InheritedFrom;
        foreach (var child in obj.Children)
        {
            block.AddChild(Block(child));
        }

        return block;
    }

    /// <summary>The object and the frame objects it shows, nearest first.</summary>
    private static IEnumerable<FormObject> Chain(FormObject obj)
    {
        for (var o = obj; o is not null; o = o.InheritedFrom)
        {
            yield return o;
        }
    }

    /// <summary>A detached object with the lines of <paramref name="chain"/>: the nearest object's line of a path wins.</summary>
    private static FormObject Copy(FormObject obj, FormObjectKind kind, IEnumerable<FormObject> chain)
    {
        var copy = new FormObject(kind, obj.Name, obj.TypeName, obj.Info, default) { Catalog = obj.Catalog };
        var lines = new List<FormProperty>();
        foreach (var source in chain.Reverse())
        {
            foreach (var property in source.Properties)
            {
                var at = lines.FindIndex(p => string.Equals(p.Path, property.Path, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    lines[at] = property;
                }
                else
                {
                    lines.Add(property);
                }
            }
        }

        foreach (var line in lines)
        {
            copy.AddProperty(line);
        }

        return copy;
    }

    /// <summary>
    /// DSG-SUR-230: an <c>inline</c> snapshot as a resolved instance of <paramref name="document"/>'s frames (a preserved
    /// <c>F0101</c> instance when the catalog does not know the frame): its text is read the way the parser reads the file.
    /// Null when the text does not read (the caller builds the snapshot as preserved objects then).
    /// </summary>
    public static FormObject? BuildInstance(FormObjectData data, FormDocument document, ComponentCatalog catalog)
    {
        var text = new StringBuilder();
        Write(text, data, catalog, 0);
        var (obj, _) = FormParser.ParseInstance(text.ToString(), document.SourcePath, catalog, document.Frames!, document.Root.TypeName);
        return obj;
    }

    private static void Write(StringBuilder output, FormObjectData data, ComponentCatalog catalog, int level)
    {
        var indent = new string(' ', level * 2);
        if (data.PreservedText is { } preserved)
        {
            foreach (var line in preserved.Split('\n'))
            {
                if (line.TrimEnd(' ', '\t', '\r') is { Length: > 0 } trimmed)
                {
                    output.Append(indent).Append(trimmed).Append('\n');
                }
            }

            return;
        }

        var keyword = data.Kind switch
        {
            FormObjectKind.Inline => "inline",
            FormObjectKind.Inherited => "inherited",
            _ => "object",
        };
        output.Append(indent).Append(keyword).Append(' ').Append(data.Name).Append(": ").Append(data.TypeName).Append('\n');
        var info = catalog.Find(data.TypeName);
        foreach (var property in data.Properties)
        {
            IReadOnlyList<(int Level, string Text)> lines = property.RawText is not null && !property.RawLines.IsDefault
                ? property.RawLines
                : property.RawText is { } raw && !raw.Contains('\n', StringComparison.Ordinal)
                    ? [(0, raw)]
                    : FormWriter.FormatValue(property.Value, info is null ? null : catalog.FindPropertyPath(info, property.Path), catalog);
            output.Append(indent).Append("  ").Append(property.Path).Append(" = ").Append(lines[0].Text).Append('\n');
            foreach (var (lineLevel, lineText) in lines.Skip(1))
            {
                output.Append(indent).Append("  ").Append(' ', lineLevel * 2).Append(lineText).Append('\n');
            }
        }

        foreach (var child in data.Children)
        {
            Write(output, child, catalog, level + 1);
        }

        output.Append(indent).Append("end\n");
    }
}
