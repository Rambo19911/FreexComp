using FreexComp.Designer.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Model;

/// <summary>Merging frame definitions into instances and following later definition changes (frames-designer.md §3).</summary>
internal static class FrameInstances
{
    /// <summary>
    /// DSG-FD-106: <paramref name="instance"/> becomes a resolved instance of <paramref name="frame"/> with one inherited
    /// object per object of the definition's tree (same nesting and creation order). Preserved objects of the definition
    /// are copied as read-only boxes without children.
    /// </summary>
    public static void Merge(FormObject instance, FrameDefinition frame)
    {
        instance.Frame = frame;
        instance.InheritedFrom = frame.Document.Root;
        foreach (var child in frame.Document.Root.Children)
        {
            instance.AddChild(Copy(child));
        }
    }

    /// <summary>
    /// DSG-FD-125: <paramref name="root"/> becomes the root of a resolved descendant of <paramref name="ancestor"/>: it
    /// inherits from the ancestor's root and gets one inherited object per child of that root, with the definition's
    /// nesting and creation order (inherited frame instances stay instances with their merged children).
    /// </summary>
    public static void MergeAncestor(FormObject root, FrameDefinition ancestor)
    {
        root.SetAncestor(ancestor);
        foreach (var child in ancestor.Document.Root.Children)
        {
            root.AddChild(Copy(child));
        }
    }

    private static FormObject Copy(FormObject definition)
    {
        var copy = new FormObject(FormObjectKind.Inherited, definition.Name, definition.TypeName, definition.Info, default)
        {
            Catalog = definition.Catalog,
            IsInherited = true,
            InheritedFrom = definition,
            Frame = definition.Frame,
            IsPreserved = definition.IsPreserved,
            HeaderSpan = definition.HeaderSpan,
        };
        if (!definition.IsPreserved)
        {
            foreach (var child in definition.Children)
            {
                copy.AddChild(Copy(child));
            }
        }

        return copy;
    }

    /// <summary>
    /// DSG-FD-107 (<c>F0111</c>): the class that repeats when an instance of <paramref name="frameClass"/> is constructed in
    /// a document of <paramref name="rootClass"/> — the frame contains itself or the root class, directly or through other
    /// frames — or null. Classes are compared ASCII case-insensitively.
    /// </summary>
    public static string? RepeatedClass(IFrameCatalog frames, string frameClass, string? rootClass)
    {
        var path = new List<string>();
        if (rootClass is not null)
        {
            path.Add(rootClass);
        }

        return Visit(frames, frameClass, path);
    }

    private static string? Visit(IFrameCatalog frames, string className, List<string> path)
    {
        if (path.Contains(className, StringComparer.OrdinalIgnoreCase))
        {
            return frames.Find(className)?.ClassName ?? className;
        }

        if (frames.Find(className) is not { } definition)
        {
            return null;
        }

        path.Add(definition.ClassName);
        foreach (var nested in ContainedClasses(definition))
        {
            if (Visit(frames, nested, path) is { } repeated)
            {
                return repeated;
            }
        }

        path.RemoveAt(path.Count - 1);
        return null;
    }

    /// <summary>The classes of the <c>inline</c> objects in a definition's file (resolved or not).</summary>
    private static IEnumerable<string> ContainedClasses(FrameDefinition definition) =>
        definition.Document.AllObjects().Where(o => o.Kind == FormObjectKind.Inline && !o.IsInherited && o.Parent is not null)
            .Select(o => o.TypeName).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="className"/> is one of <paramref name="changed"/> or contains one of them.</summary>
    private static bool Affected(IFrameCatalog frames, string className, IReadOnlyCollection<string> changed, HashSet<string> visiting)
    {
        if (changed.Contains(className, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!visiting.Add(className) || frames.Find(className) is not { } definition)
        {
            return false;
        }

        return ContainedClasses(definition).Any(c => Affected(frames, c, changed, visiting));
    }

    /// <summary>
    /// DSG-FD-111: every instance of the file whose frame is one of <paramref name="changed"/> or contains one of them is
    /// merged again with the current definitions — written as it is and read back, so its own lines and override
    /// blocks are kept, blocks that no longer match become <c>F0109</c> preserved blocks, and an instance whose frame is no
    /// longer known (or now contains itself) is preserved. The file model does not change: the document raises
    /// <see cref="FormDocument.Changed"/> once with <see cref="FormChangeFlags.Structure"/> and
    /// <see cref="FormChangedEventArgs.IsRemerge"/>; nothing is raised when no instance is affected.
    /// </summary>
    public static void Remerge(FormDocument document, IFrameCatalog frames, ComponentCatalog catalog, IReadOnlyCollection<string> changed)
    {
        if (document.FollowsAncestor)
        {
            // DSG-FD-134: a descendant contains inherited instances too; it is merged again from its root.
            if (document.ContainsFrame(changed))
            {
                RemergeRoot(document, catalog);
            }

            return;
        }

        var instances = document.AllObjects()
            .Where(o => o.Kind == FormObjectKind.Inline && !o.IsInherited && o.Parent is not null && !InsidePreserved(o)
                && Affected(frames, o.TypeName, changed, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (instances.Count == 0)
        {
            return;
        }

        var names = new List<string>();
        foreach (var instance in instances)
        {
            var (parsed, diagnostics) = FormParser.ParseInstance(FormWriter.WriteObjects([instance]), document.SourcePath, catalog, frames,
                document.Root.TypeName);
            if (parsed is null)
            {
                continue;
            }

            document.ParseDiagnostics.RemoveAll(d => Within(d.Owner, instance));
            instance.ReplaceContents(parsed);

            // Positions: the instance was written at level 0; in the file it starts at its header.
            var (line, col) = document.Lines.LineCol(instance.HeaderSpan.Start);
            foreach (var (diagnostic, owner, property) in diagnostics)
            {
                var moved = diagnostic with
                {
                    Line = diagnostic.Line + line - 1,
                    Col = diagnostic.Col + col - 1,
                    EndLine = diagnostic.EndLine + line - 1,
                    EndCol = diagnostic.EndCol + col - 1,
                };
                document.ParseDiagnostics.Add(new ParseDiagnostic(moved, ReferenceEquals(owner, parsed) ? instance : owner, property));
            }

            names.Add(instance.Name);
        }

        FormValidator.Validate(document, catalog);
        document.RaiseChanged(new FormChangedEventArgs(names, FormChangeFlags.Structure, isRemerge: true));
    }

    /// <summary>
    /// DSG-FD-134: the whole document is merged again with the current ancestor and frame catalogs — written as it is and
    /// read back, so its own lines, override blocks and new components are kept, blocks that no longer match become
    /// <c>F0109</c> preserved blocks, a new component whose name an ancestor component now has becomes <c>F0106</c>, and a
    /// descendant whose ancestor is no longer listed becomes read-only (<c>F0112</c>). The file model does not change: the
    /// document raises <see cref="FormDocument.Changed"/> once with <see cref="FormChangeFlags.Structure"/> and
    /// <see cref="FormChangedEventArgs.IsRemerge"/>; changes recorded before still apply (they name objects by name).
    /// </summary>
    public static void RemergeRoot(FormDocument document, ComponentCatalog catalog)
    {
        var parsed = FormParser.ParseAgain(FormWriter.Write(document), document.SourcePath, catalog, document.RootAncestorClass,
            document.Frames, document.Ancestors);
        if (parsed is null)
        {
            return; // the writer's text always reads back; nothing is replaced otherwise
        }

        document.ReplaceTree(parsed);
        FormValidator.Validate(document, catalog);
        document.RaiseChanged(new FormChangedEventArgs([document.Root.Name], FormChangeFlags.Structure, isRemerge: true));
    }

    private static bool InsidePreserved(FormObject obj)
    {
        for (var p = obj.Parent; p is not null; p = p.Parent)
        {
            if (p.IsPreserved)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Within(FormObject obj, FormObject ancestor)
    {
        for (var o = obj; o is not null; o = o.Parent)
        {
            if (ReferenceEquals(o, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A descendant document's link to its ancestor catalog (DSG-FD-134), held like <see cref="FrameSubscription"/>: when a
/// class of the document's ancestor chain or a frame it contains changes, the document is merged again from its root.
/// </summary>
internal sealed class AncestorSubscription
{
    private readonly WeakReference<FormDocument> _document;
    private readonly IFrameCatalog _ancestors;
    private readonly ComponentCatalog _catalog;

    public AncestorSubscription(FormDocument document, IFrameCatalog ancestors, ComponentCatalog catalog)
    {
        _document = new WeakReference<FormDocument>(document);
        _ancestors = ancestors;
        _catalog = catalog;
        ancestors.Changed += OnChanged;
    }

    private void OnChanged(object? sender, FrameCatalogChangedEventArgs e)
    {
        if (!_document.TryGetTarget(out var document))
        {
            _ancestors.Changed -= OnChanged;
        }
        else if (document.Root.Kind == FormObjectKind.Inherited && document.DependsOn(e.ClassNames))
        {
            FrameInstances.RemergeRoot(document, _catalog);
        }
    }
}

/// <summary>
/// A document's link to its frame catalog (DSG-FD-111). The catalog holds this object, the object holds the document
/// weakly: a document that is no longer used is not kept alive by the catalog, and its link removes itself on the next
/// change.
/// </summary>
internal sealed class FrameSubscription
{
    private readonly WeakReference<FormDocument> _document;
    private readonly IFrameCatalog _frames;
    private readonly ComponentCatalog _catalog;

    public FrameSubscription(FormDocument document, IFrameCatalog frames, ComponentCatalog catalog)
    {
        _document = new WeakReference<FormDocument>(document);
        _frames = frames;
        _catalog = catalog;
        frames.Changed += OnChanged;
    }

    private void OnChanged(object? sender, FrameCatalogChangedEventArgs e)
    {
        if (_document.TryGetTarget(out var document))
        {
            FrameInstances.Remerge(document, _frames, _catalog, e.ClassNames);
        }
        else
        {
            _frames.Changed -= OnChanged;
        }
    }
}
