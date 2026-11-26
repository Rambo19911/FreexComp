using System.Collections.Immutable;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>One model mutation, applied by <see cref="FormEditor"/> (form-document.md §7).</summary>
public abstract record FormChange;

/// <summary>Sets a property; a value equal to the default removes it instead (DSG-FD-080).</summary>
public sealed record SetProperty(string ObjectName, string Path, FormValue Value) : FormChange
{
    /// <summary>Set on inverses only: the exact earlier property lines to put back (raw text, position, preserved state).</summary>
    internal ImmutableArray<PropertySlot> Restore { get; init; }
}

/// <summary>Removes a stored property (= reset to default).</summary>
public sealed record RemoveProperty(string ObjectName, string Path) : FormChange
{
    /// <inheritdoc cref="SetProperty.Restore"/>
    internal ImmutableArray<PropertySlot> Restore { get; init; }
}

/// <summary>Inserts a new object (with its subtree) as child <paramref name="Index"/> of <paramref name="ParentName"/>.</summary>
public sealed record AddObject(string ParentName, int Index, FormObjectData Data) : FormChange;

/// <summary>Removes an object and its subtree.</summary>
public sealed record RemoveObject(string ObjectName) : FormChange;

/// <summary>Re-parents and/or reorders an object; <paramref name="NewIndex"/> counts after removal from the old position (DSG-FD-085).</summary>
public sealed record MoveObject(string ObjectName, string NewParentName, int NewIndex) : FormChange
{
    /// <summary>
    /// Set on inverses only: an index below the inherited children of the new parent is clamped, not rejected (DSG-FD-134).
    /// Not part of equality: an inverse equals the move a caller would write.
    /// </summary>
    internal bool IsInverse { get; init; }

    public bool Equals(MoveObject? other) =>
        other is not null && ObjectName == other.ObjectName && NewParentName == other.NewParentName && NewIndex == other.NewIndex;

    public override int GetHashCode() => HashCode.Combine(ObjectName, NewParentName, NewIndex);
}

/// <summary>Renames an object and updates the component references to it (DSG-FD-082).</summary>
public sealed record RenameObject(string OldName, string NewName) : FormChange
{
    /// <summary>
    /// Set on inverses only: the exact reference lines to put back instead of re-deriving them, by the owners' qualified
    /// names (a re-merge replaces the objects of a descendant, DSG-FD-134).
    /// </summary>
    internal ImmutableArray<(string Owner, ImmutableArray<PropertySlot> Slots)> RestoreReferences { get; init; }

    /// <summary>
    /// D-137: the object is an inherited one of a descendant's file scope whose ancestor component was renamed; it follows
    /// (header and component references) instead of being refused (DSG-FD-132). The inverse carries the flag too.
    /// </summary>
    public bool FollowsAncestor { get; init; }
}

/// <summary>
/// Changes the type name of the form root (code-sync.md DSG-CS-046: the form's type follows the renamed form class); the
/// root stays bound to the same metadata.
/// </summary>
public sealed record RetypeObject(string ObjectName, string NewTypeName) : FormChange;

/// <summary>A deep snapshot of an object: header, stored properties in order, children (clipboard, undo).</summary>
public sealed record FormObjectData(FormObjectKind Kind, string Name, string TypeName,
    ImmutableArray<FormPropertyData> Properties, ImmutableArray<FormObjectData> Children)
{
    /// <summary>The verbatim text of a preserved object (form-document.md §1), else null.</summary>
    public string? PreservedText { get; init; }

    /// <summary>Set on inverses of <see cref="RemoveObject"/>: the detached object itself, put back unchanged.</summary>
    internal FormObject? Original { get; init; }

    /// <summary>The snapshot of <paramref name="obj"/> and its subtree.</summary>
    public static FormObjectData Capture(FormObject obj) =>
        new(obj.Kind, obj.Name, obj.TypeName,
            [.. obj.Properties.Select(p => new FormPropertyData(p.Path, p.Value, p.RawText) { RawLines = p.RawLines })],
            [.. obj.Children.Select(Capture), .. obj.PreservedBlocks.Select(Capture)])
        {
            PreservedText = obj.PreservedText,
        };
}

/// <summary>One property line of a <see cref="FormObjectData"/>; <paramref name="RawText"/> keeps the text as read.</summary>
public sealed record FormPropertyData(string Path, FormValue Value, string? RawText = null)
{
    /// <summary>The line structure of <see cref="RawText"/> (DSG-FD-046).</summary>
    internal ImmutableArray<(int Level, string Text)> RawLines { get; init; }
}

/// <summary>A stored property line of an object, or its absence (<see cref="Property"/> null), at its list position.</summary>
internal readonly record struct PropertySlot(string Path, int Index, FormProperty? Property);

/// <summary>
/// A change that cannot be applied (DSG-FD-083). This is a programming error of the caller, which validates first and
/// shows the user-facing messages; it is never shown to the user.
/// </summary>
public sealed class InvalidFormChangeException(string message) : InvalidOperationException(message);
