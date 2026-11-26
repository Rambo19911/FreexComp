using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// D-137: a component of an ancestor form or frame renamed in the designer is renamed in every descendant of the project
/// (the ancestor view of <see cref="ProjectFrameCatalog"/>, any depth): the inherited component of each descendant's file
/// scope takes the new name with the component references of the descendant's own lines (DSG-FD-082; handler names
/// stay). The descendants change before the ancestor and in the same undo unit (<see cref="ILinkedEdits"/>), deepest
/// first, so that every re-merge (DSG-FD-134) finds the names it expects and no block is preserved with <c>F0109</c>. A
/// descendant open in the IDE follows in its tab (modified, saved with the document); a closed one is written at once and
/// written back on undo. The field rename in code is not part of this (DSG-CS-045). D-139: a renamed frame component is
/// renamed the same way inside every frame instance of the project that shows it (an instance of the frame or of a frame
/// derived from it, placed by a form or frame, inherited by a descendant or nested in another frame): its override block
/// and the references of the placing file to it (the instance scope's plain names and the names qualified by the
/// instance). A document changes before every document it depends on (its ancestor, the frames it contains). No Avalonia
/// types.
/// </summary>
public sealed class AncestorRename
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly IFileSystem _fileSystem;
    private readonly ProjectFrameCatalog _catalog;
    private readonly Func<ComponentCatalog> _components;
    private readonly Func<FormDocument, FormDesignerViewModel?> _open;

    /// <param name="components">The component metadata closed descendants are edited with.</param>
    /// <param name="open">The designer of an open document whose model is the given document, or null.</param>
    public AncestorRename(IFileSystem fileSystem, ProjectFrameCatalog catalog, Func<ComponentCatalog> components,
        Func<FormDocument, FormDesignerViewModel?> open)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    /// <summary>Why the last edits could not be applied (a descendant file could not be written), else null.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// The class of <paramref name="ancestor"/>'s document and the class names of the project's documents that depend on it
    /// (descendants, documents showing an instance of it, any depth), each before the documents it depends on; null when
    /// the document is not the project's definition of its class (not a project unit) or <paramref name="obj"/> is its root.
    /// </summary>
    private (string Own, List<string> Classes)? Followers(FormDesignerViewModel ancestor, FormObject obj)
    {
        if (obj.Parent is null || ancestor.Model is not { } model || _catalog.Ancestors.Find(model.Root.TypeName) is not { } own
            || !ReferenceEquals(own.Document, model))
        {
            return null;
        }

        // The rank of a document: 0 for the renamed one, else 1 + the highest rank of what it depends on (none: unaffected).
        var ranks = new Dictionary<string, int?>(Names);
        var visiting = new HashSet<string>(Names);
        int? Rank(FrameDefinition definition)
        {
            if (Names.Equals(definition.ClassName, own.ClassName))
            {
                return 0;
            }

            if (ranks.TryGetValue(definition.ClassName, out var known) || !visiting.Add(definition.ClassName))
            {
                return known; // a cycle is shown read-only (F0111, F0113): it does not follow
            }

            int? rank = null;
            var document = definition.Document;
            var dependencies = document.AllObjects().Where(o => o.Frame is not null).Select(o => o.Frame!.ClassName)
                .Prepend(document.Ancestor?.ClassName).OfType<string>().Distinct(Names);
            foreach (var dependency in dependencies)
            {
                if (_catalog.Ancestors.Find(dependency) is { } found && Rank(found) is { } level)
                {
                    rank = Math.Max(rank ?? 0, level + 1);
                }
            }

            visiting.Remove(definition.ClassName);
            ranks[definition.ClassName] = rank;
            return rank;
        }

        var followers = _catalog.Ancestors.All.Select(d => (d.ClassName, Rank: Rank(d))).Where(f => f.Rank > 0).ToList();
        return (own.ClassName, [.. followers.OrderByDescending(f => f.Rank).Select(f => f.ClassName)]);
    }

    /// <summary>True when <paramref name="definition"/> is the class <paramref name="own"/> or inherits from it.</summary>
    private static bool Derives(FrameDefinition? definition, string own)
    {
        var seen = new HashSet<FormDocument>(ReferenceEqualityComparer.Instance);
        for (var level = definition; level is not null && seen.Add(level.Document); level = level.Document.Ancestor)
        {
            if (Names.Equals(level.ClassName, own))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The renames that make <paramref name="document"/> follow: its inherited file-scope component when it descends from
    /// <paramref name="own"/> (D-137), and the component inside each instance of <paramref name="own"/> or of a frame derived
    /// from it (D-139), by qualified name.
    /// </summary>
    private static List<FormChange> Renames(FormDocument document, string own, string oldName, string newName)
    {
        var changes = new List<FormChange>();
        if (Derives(document.Ancestor, own) && Inherited(document, oldName) is not null)
        {
            changes.Add(new RenameObject(oldName, newName) { FollowsAncestor = true });
        }

        foreach (var instance in document.AllObjects().Where(o => !o.IsPreserved && Derives(o.Frame, own)))
        {
            var name = instance.QualifiedName + "." + oldName;
            if (document.FindByQualifiedName(name) is { IsInherited: true, IsPreserved: false, Info: not null })
            {
                changes.Add(new RenameObject(name, newName) { FollowsAncestor = true });
            }
        }

        return changes;
    }

    /// <summary>The inherited object of a descendant's file scope named <paramref name="name"/>, or null.</summary>
    private static FormObject? Inherited(FormDocument document, string name) =>
        document.FindByName(name) is { IsInherited: true, Parent: not null, InheritedFrame: null, IsPreserved: false } obj ? obj : null;

    /// <summary>
    /// True when a descendant already has another component named <paramref name="newName"/> (the rename is refused with
    /// <c>Msg.Designer.NameDuplicate</c>, as for a name of the form itself).
    /// </summary>
    public bool IsNameTaken(FormDesignerViewModel ancestor, FormObject obj, string newName)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(newName);
        return Followers(ancestor, obj) is (var own, var classes) && classes.Any(className => _catalog.Ancestors.Find(className)?.Document is { } document
            && Derives(document.Ancestor, own) && document.FindByName(newName) is { } other && !Names.Equals(other.Name, obj.Name));
    }

    /// <summary>
    /// The edits that rename <paramref name="obj"/> of <paramref name="ancestor"/> to <paramref name="newName"/> in every
    /// descendant and frame instance, to be applied with the ancestor's rename
    /// (<see cref="FreexComp.Designer.Surface.DesignSurfaceController.Commit"/>); null when no project document depends on the
    /// document's class.
    /// </summary>
    public ILinkedEdits? Prepare(FormDesignerViewModel ancestor, FormObject obj, string newName)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(newName);
        LastFailure = null;
        return Followers(ancestor, obj) is (var own, var classes) && classes.Count > 0 ? new Edits(this, ancestor, own, classes, obj.Name, newName) : null;
    }

    /// <summary>One descendant changed by the last apply or revert: how to undo that step.</summary>
    private sealed record Step(string ClassName, IReadOnlyList<FormChange> Inverse, byte[]? Before, byte[]? After);

    private sealed class Edits(AncestorRename owner, FormDesignerViewModel ancestor, string own, IReadOnlyList<string> classes, string oldName,
        string newName) : ILinkedEdits
    {
        private List<Step> _applied = [];

        public bool Apply()
        {
            owner.LastFailure = null;
            var steps = new List<Step>();
            foreach (var className in classes)
            {
                if (owner._catalog.Ancestors.Find(className) is not { } definition
                    || Renames(definition.Document, own, oldName, newName) is not { Count: > 0 } renames)
                {
                    continue;
                }

                try
                {
                    steps.Add(owner.Change(definition, renames, null));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    owner.LastFailure = string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_SaveFailed, definition.FormPath, e.Message);
                    Undo(steps);
                    return false;
                }
            }

            _applied = steps;
            return true;
        }

        public void Revert()
        {
            Undo(_applied);
            _applied = [];
        }

        /// <summary>The steps reverted, deepest descendant first (the order they were applied in).</summary>
        private void Undo(List<Step> steps)
        {
            foreach (var step in steps)
            {
                if (owner._catalog.Ancestors.Find(step.ClassName) is { } definition
                    && step.Inverse.OfType<RenameObject>().All(r => definition.Document.FindByQualifiedName(r.OldName) is { IsInherited: true }))
                {
                    try
                    {
                        owner.Change(definition, step.Inverse, step);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        ancestor.ShowStatus("Msg.File.SaveFailed", [definition.FormPath, e.Message]); // the file keeps the new name
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies <paramref name="changes"/> to a descendant: in its tab when it is open, else to the project's definition and
    /// to its file (written only when the written text changes; on a revert the file's earlier bytes when the file still
    /// holds what the apply wrote).
    /// </summary>
    private Step Change(FrameDefinition definition, IReadOnlyList<FormChange> changes, Step? reverting)
    {
        var document = definition.Document;
        if (_open(document) is { } designer)
        {
            return new Step(definition.ClassName, designer.FollowAncestor(changes), null, null);
        }

        var path = definition.FormPath;
        var before = FormWriter.Write(document);
        var bytes = _fileSystem.FileExists(path) ? _fileSystem.ReadAllBytes(path) : null;
        var inverse = new FormEditor(document, _components()).Apply(changes);
        var text = FormWriter.Write(document);
        byte[]? written = null;
        if (reverting is { Before: { } original, After: { } after } && bytes is not null && bytes.AsSpan().SequenceEqual(after))
        {
            written = original;
        }
        else if (!string.Equals(before, text, StringComparison.Ordinal))
        {
            written = FileEncodings.Encode(text, FileEncoding.Utf8);
        }

        if (written is not null)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, path, written);
            }
            catch
            {
                new FormEditor(document, _components()).Apply(inverse);
                throw;
            }
        }

        return new Step(definition.ClassName, inverse, bytes, written);
    }
}
