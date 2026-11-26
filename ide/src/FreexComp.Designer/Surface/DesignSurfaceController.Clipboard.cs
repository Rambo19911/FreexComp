using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Surface;

/// <summary>Copy, cut and paste (design-surface.md §10.1, §10.2, DSG-SUR-110 … 128).</summary>
public sealed partial class DesignSurfaceController
{
    // DSG-SUR-125: the last copy from this form (valid while the clipboard sequence number is unchanged): the parent the
    // objects came from; and the consecutive pastes of one clipboard content into one parent.
    private (long Sequence, string ParentName)? _lastCopy;
    private (long Sequence, string TargetName, int Count)? _lastPaste;

    /// <summary>DSG-SUR-112: the effective copy set — the selection after DSG-SUR-024 without the form, in pre-order.</summary>
    private List<FormObject> CopySet() => Selection.GetEffectiveSet().Where(o => o.Parent is not null).ToList();

    private bool CanCopy => !IsPointerActive && CopySet().Count > 0;

    /// <summary>DSG-SUR-113: copy is possible and so is delete.</summary>
    private bool CanCut => CanCopy && CanDelete;

    /// <summary>
    /// DSG-SUR-110, 111: the form text of the copy set in both formats; the custom one starts with the header line
    /// <c>; parent-origin x y</c> (surface coordinates of the client origin of the primary's parent).
    /// </summary>
    private void Copy()
    {
        // DSG-SUR-230: inherited controls leave the frame as ordinary components with their effective values.
        var text = FormWriter.WriteObjects(CopySet().Select(o => o.IsInherited ? FrameCopies.Detach(o) : o));
        var parent = Selection.Primary.Parent ?? Document.Root;
        var origin = Geometry.GetClientRect(parent);
        var clipboard = _host.Clipboard;
        clipboard.SetData(new Dictionary<string, string>
        {
            [ClipboardFormats.Components] = $"; parent-origin {origin.Left} {origin.Top}\n" + text,
            [ClipboardFormats.Text] = text,
        });
        _lastCopy = (clipboard.SequenceNumber, parent.QualifiedName);
    }

    /// <summary>DSG-SUR-113: copy, then delete as one "Cut" unit.</summary>
    private void Cut()
    {
        Copy();
        Delete(UndoTitles.Cut);
    }

    /// <summary>
    /// DSG-SUR-120, 128: not during a drag or in tab-order mode; the clipboard has the custom format, or plain text that
    /// begins (after whitespace and <c>;</c> lines) with the keyword <c>object</c> (or <c>inline</c>, DSG-SUR-230).
    /// </summary>
    private bool CanPaste => !IsPointerActive && State != InteractionState.TabOrder && PasteText() is not null;

    /// <summary>The text to paste: the custom format, else plain text that looks like form objects; null when neither.</summary>
    private string? PasteText()
    {
        var clipboard = _host.Clipboard;
        if (clipboard.GetData(ClipboardFormats.Components) is { } custom)
        {
            return custom;
        }

        return clipboard.GetData(ClipboardFormats.Text) is { } text && StartsWithObject(text) ? text : null;
    }

    private static bool StartsWithObject(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim(' ', '\t', '\r', '﻿');
            if (trimmed.Length == 0 || trimmed[0] == ';')
            {
                continue;
            }

            // DSG-SUR-230: a frame instance (inline) is form text too.
            return ((string[])["object", "inline"]).Any(keyword => trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == keyword.Length || !(char.IsAsciiLetterOrDigit(trimmed[keyword.Length]) || trimmed[keyword.Length] == '_')));
        }

        return false;
    }

    /// <summary>
    /// DSG-SUR-120 … 127: parses the clipboard fragment (a syntax error or no object → <c>Msg.Designer.PasteNothing</c>),
    /// skips unknown classes (DSG-SUR-122), names (DSG-SUR-123), filters events (DSG-SUR-124), positions (DSG-SUR-125,
    /// 126) and appends the objects with their tab positions (DSG-SUR-127) as one "Paste" unit; the pasted top-level
    /// objects become the selection.
    /// </summary>
    private void Paste()
    {
        var fragment = FormParser.ParseFragment(PasteText()!, "clipboard", Catalog, Document.Frames, Document.Root.TypeName);
        if (fragment.HasSyntaxErrors || fragment.Objects.IsEmpty)
        {
            _host.SetStatus(StatusIds.PasteNothing);
            return;
        }

        var objects = fragment.Objects.Where(o => !IsSkipped(o) && !RefusedAsCycle(o)).ToList();
        var skipped = fragment.Objects.Sum(SkippedRoots);
        if (skipped > 0)
        {
            _host.SetStatus(StatusIds.PasteUnknownClass, skipped);
        }

        if (objects.Count == 0)
        {
            return;
        }

        var target = PasteTarget();
        if (RefusesNewChildren(target))
        {
            ReportFixed(target, newChildren: true); // DSG-SUR-227, 237: not into an instance or an object inside one
            return;
        }

        var sequence = _host.Clipboard.SequenceNumber;
        var k = _lastPaste is { } last && last.Sequence == sequence && last.TargetName.Equals(target.QualifiedName, StringComparison.OrdinalIgnoreCase)
            ? last.Count + 1
            : 1;
        var copiedFrom = _lastCopy is { } copy && copy.Sequence == sequence ? Document.FindByQualifiedName(copy.ParentName) : null;

        var data = objects.Select(o => (Object: o, Parent: PasteParent(o, target))).ToList();

        // DSG-SUR-125: top-level visual objects per destination parent — offset when it is the parent they were copied
        // from, else their bounding box goes to (gridX, gridY); every repetition adds one more grid step.
        var (gx, gy) = (_settings.GridSizeX, _settings.GridSizeY);
        var offsets = new Dictionary<FormObject, (int Dx, int Dy)>(ReferenceEqualityComparer.Instance);
        foreach (var group in data.Where(d => !IsNonVisual(d.Object)).GroupBy(d => d.Parent, ReferenceEqualityComparer.Instance))
        {
            var parent = (FormObject)group.Key!;
            var members = group.Select(d => d.Object).ToList();
            (int Dx, int Dy) offset;
            if (copiedFrom is not null && ReferenceEquals(parent, copiedFrom))
            {
                offset = (gx * k, gy * k);
            }
            else
            {
                var left = members.Min(o => Coordinate(o, "Left"));
                var top = members.Min(o => Coordinate(o, "Top"));
                offset = (gx - left + gx * (k - 1), gy - top + gy * (k - 1));
            }

            foreach (var member in members)
            {
                offsets[member] = offset;
            }
        }

        // DSG-SUR-126: non-visual components take the next free tray slots.
        var slots = TraySlots(data.Count(d => IsNonVisual(d.Object)));
        var slot = 0;
        var placements = data.Select(d => new Placement(d.Object, d.Parent, IsNonVisual(d.Object)
                ? slots[slot++]
                : (Coordinate(d.Object, "Left") + offsets[d.Object].Dx, Coordinate(d.Object, "Top") + offsets[d.Object].Dy)))
            .ToList();
        AddCopies(placements, UndoTitles.Paste);
        _lastPaste = (sequence, target.QualifiedName, k);
    }

    /// <summary>A copy to create: the source object, its new parent and its new Left/Top (DesignLeft/DesignTop for a tray item).</summary>
    private readonly record struct Placement(FormObject Source, FormObject Parent, (int X, int Y) Position);

    /// <summary>
    /// Creates copies of the sources as one undo unit (paste DSG-SUR-127, Ctrl-drag copy DSG-SUR-057): named by
    /// DSG-SUR-123, references and events filtered by DSG-SUR-123/124, appended to their parents in the given order,
    /// focusable ones numbered after the existing siblings (DSG-SUR-096), the layout committed; the copies become the
    /// selection (last = primary).
    /// </summary>
    private void AddCopies(IReadOnlyList<Placement> placements, string title)
    {
        if (placements.Select(p => p.Parent).FirstOrDefault(RefusesNewChildren) is { } fixedParent)
        {
            ReportFixed(fixedParent, newChildren: true); // DSG-SUR-227: a Ctrl-drag copy of inherited controls stays out of the frame
            return;
        }

        var names = PasteNames(placements.Select(p => p.Source));
        var changes = new List<AddObject>();
        var appended = new Dictionary<FormObject, int>(ReferenceEqualityComparer.Instance);
        foreach (var (obj, parent, position) in placements)
        {
            var snapshot = Transform(obj, names);
            var (xPath, yPath) = IsNonVisual(obj) ? ("DesignLeft", "DesignTop") : ("Left", "Top");
            snapshot = WithProperty(WithProperty(snapshot, xPath, position.X), yPath, position.Y);
            var count = appended.GetValueOrDefault(parent);
            changes.Add(new AddObject(parent.QualifiedName, parent.Children.Count + count, snapshot));
            appended[parent] = count + 1;
        }

        // DSG-SUR-127: the fields of every copy (with its subtree) and their units, in the same unit; refused → no copies.
        if (!AddFieldsFor(changes.Select(c => c.Data), out var edits, out var ops))
        {
            return;
        }

        var before = Selection.ToSnapshot();
        var undo = new List<FormChange>(ApplyEdit(changes));
        var copies = placements.Select(p => Document.FindByQualifiedName(names[p.Source])!).ToList();

        // DSG-SUR-127: focusable objects take the tab positions after the existing ones, in copied order.
        var renumber = copies.Where(TabOrderRules.IsFocusable).GroupBy(o => o.Parent!, ReferenceEqualityComparer.Instance)
            .SelectMany(g => TabOrderRules.Renumber((FormObject)g.Key!, appended: g))
            .ToList();
        undo.InsertRange(0, ApplyEdit(renumber));
        var (sizeRedo, sizeUndo) = ApplyAutoSize(copies, withDescendants: true); // DSG-SUR-088: pasted and copied controls are measured
        undo.InsertRange(0, sizeUndo);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Selection.Select(copies);
        PushUnit(new DesignerUndoUnit(title, [.. changes, .. renumber, .. sizeRedo, .. commitRedo], [.. commitUndo, .. undo], edits,
            before, Selection.ToSnapshot()) { CodeOps = ops });
    }

    /// <summary>DSG-SUR-122: an object of a class the catalog does not know (or otherwise preserved) is not pasted.</summary>
    private static bool IsSkipped(FormObject obj) => obj.Kind != FormObjectKind.Inline && (obj.IsPreserved || obj.Info is null);

    /// <summary>The number of skipped subtrees in <paramref name="obj"/>'s tree (a skipped object counts once with its subtree).</summary>
    private static int SkippedRoots(FormObject obj) => IsSkipped(obj) ? 1 : obj.Children.Sum(SkippedRoots);

    private static bool IsNonVisual(FormObject obj) => obj.Info?.Kind == ComponentKind.NonVisual;

    /// <summary>
    /// DSG-SUR-121: the primary if it is a container that accepts children; else its parent; for a tray item or the form:
    /// the form.
    /// </summary>
    private FormObject PasteTarget()
    {
        var primary = Selection.Primary;
        if (primary.Parent is null || Geometry.IsTrayItem(primary))
        {
            return Document.Root;
        }

        return primary is { Info.AcceptsChildren: true, IsPreserved: false } && !RefusesNewChildren(primary) ? primary : primary.Parent;
    }

    /// <summary>
    /// DSG-SUR-121, 122: non-visual components go to the form; a visual object goes to the target, or the nearest ancestor
    /// of it that can hold it (metadata <c>acceptsChildren</c>, <c>childClasses</c>, <c>parentClasses</c>), ultimately the form.
    /// </summary>
    private FormObject PasteParent(FormObject obj, FormObject target)
    {
        if (IsNonVisual(obj))
        {
            return Document.Root;
        }

        var parent = target;
        while (parent.Parent is not null && !CanHold(parent, obj.Info!))
        {
            parent = parent.Parent;
        }

        return parent;
    }

    private bool CanHold(FormObject parent, ComponentInfo child) =>
        parent is { IsPreserved: false, Info: { AcceptsChildren: true } p } && !RefusesNewChildren(parent)
        && (p.ChildClasses.IsEmpty || p.ChildClasses.Any(c => Catalog.IsAssignable(child.ClassName, c)))
        && (child.ParentClasses.IsEmpty || child.ParentClasses.Any(c => Catalog.IsAssignable(p.ClassName, c)));

    /// <summary>
    /// DSG-SUR-123: the new name of every pasted object (pre-order): kept when free — not used in the form nor earlier in
    /// this paste (case-insensitive) and no member of the form class (DSG-CS-031) — else <c>&lt;base&gt;&lt;n&gt;</c> with the
    /// smallest free n ≥ 1, base = the name without trailing digits (empty: the class's <c>namePrefix</c>).
    /// </summary>
    private Dictionary<FormObject, string> PasteNames(IEnumerable<FormObject> objects)
    {
        // DSG-FD-126: the file scope — the inherited components of a descendant too, not those inside instances.
        var used = new HashSet<string>(Document.AllObjects().Where(o => !o.IsInherited || o.Scope is null).Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<FormObject, string>(ReferenceEqualityComparer.Instance);
        bool Free(string name) => !used.Contains(name) && _host.CodeSync.LookupMember(name) == MemberLookup.None;

        void Assign(FormObject obj)
        {
            var name = obj.Name;
            if (!Free(name))
            {
                var stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (stem.Length == 0)
                {
                    stem = obj.Info!.NamePrefix;
                }

                var n = 1;
                while (!Free(stem + n))
                {
                    n++;
                }

                name = stem + n;
            }

            used.Add(name);
            names[obj] = name;
            if (obj.Kind == FormObjectKind.Inline)
            {
                return; // DSG-SUR-230: the inherited names of an instance are kept
            }

            foreach (var child in obj.Children.Where(c => !IsSkipped(c)))
            {
                Assign(child);
            }
        }

        foreach (var obj in objects)
        {
            Assign(obj);
        }

        return names;
    }

    /// <summary>
    /// The snapshot to add for a pasted object: new names (DSG-SUR-123), component references (<see cref="PastedReference"/>),
    /// event properties kept only when the form class has a compatible method of that name (DSG-SUR-124), skipped
    /// children left out.
    /// </summary>
    private FormObjectData Transform(FormObject obj, Dictionary<FormObject, string> names)
    {
        var pasted = new Dictionary<string, FormObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in names.Keys)
        {
            pasted.TryAdd(original.Name, original);
        }

        return TransformTree(obj, names, pasted);
    }

    private FormObjectData TransformTree(FormObject obj, Dictionary<FormObject, string> names, Dictionary<string, FormObject> pasted)
    {
        if (obj.Kind == FormObjectKind.Inline)
        {
            // DSG-SUR-230: an instance keeps its lines and override blocks (the snapshot is read again on add), their
            // references filtered by the scope rules (D-143); a preserved one keeps its structure so the new name and
            // position are written.
            var lines = obj.IsFrameInstance ? InstanceLines(obj, names, pasted) : FormObjectData.Capture(obj);
            return new FormObjectData(FormObjectKind.Inline, names[obj], obj.TypeName, lines.Properties, lines.Children);
        }

        var info = obj.Info!;
        var properties = new List<FormPropertyData>();
        foreach (var property in FormObjectData.Capture(obj).Properties)
        {
            if (property.Path.Contains('.', StringComparison.Ordinal) || info.FindEvent(property.Path) is not { } eventInfo)
            {
                if (PastedLine(null, info, property, names, pasted) is { } line)
                {
                    properties.Add(line);
                }

                continue;
            }

            if (property.Value is IdentValue { Name: var method }
                && _host.CodeSync.GetCompatibleMethods(eventInfo.Signature).Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                properties.Add(property);
            }
        }

        var children = obj.Children.Where(c => !IsSkipped(c)).Select(c => TransformTree(c, names, pasted));
        return new FormObjectData(obj.Kind, names[obj], obj.TypeName, [.. properties], [.. children]);
    }

    /// <summary>
    /// D-143: the own and override lines of a pasted instance <paramref name="obj"/> and of the inherited objects below it,
    /// their component references filtered by <see cref="PastedReference"/> in the scope of each line's owner; preserved
    /// blocks and objects unchanged.
    /// </summary>
    private FormObjectData InstanceLines(FormObject obj, Dictionary<FormObject, string> names, Dictionary<string, FormObject> pasted)
    {
        if (obj.IsPreserved || obj.Info is not { } info)
        {
            return FormObjectData.Capture(obj);
        }

        var scope = ReferenceLookup.Scope(obj);
        var properties = FormObjectData.Capture(obj).Properties.Select(p => PastedLine(scope, info, p, names, pasted)).OfType<FormPropertyData>();
        return new FormObjectData(obj.Kind, obj.Name, obj.TypeName, [.. properties],
            [.. obj.Children.Select(c => InstanceLines(c, names, pasted)), .. obj.PreservedBlocks.Select(FormObjectData.Capture)])
        {
            PreservedText = obj.PreservedText,
        };
    }

    /// <summary>A pasted property line: a component reference as <see cref="PastedReference"/> makes it (null: removed), any other line unchanged.</summary>
    private FormPropertyData? PastedLine(FormObject? scope, ComponentInfo info, FormPropertyData property, Dictionary<FormObject, string> names,
        Dictionary<string, FormObject> pasted)
    {
        if (property.Value is not (IdentValue or QualifiedIdentValue) || Catalog.FindPropertyPath(info, property.Path) is not { Kind: PropertyKind.Component })
        {
            return property;
        }

        return PastedReference(scope, property.Value, names, pasted) switch
        {
            null => null,
            var same when ReferenceEquals(same, property.Value) => property,
            var renamed => new FormPropertyData(property.Path, renamed),
        };
    }

    /// <summary>
    /// DSG-SUR-123, D-142, D-143: a component reference of a pasted line whose owner's reference scope is
    /// <paramref name="scope"/> (a pasted instance; null for the file scope), resolved as in the source by the scope rules
    /// (<see cref="ReferenceLookup"/>, DSG-FD-108). A name, or the first part of a qualified name, that the scope resolves
    /// (the instance by name, its components) or that names a pasted object reaches the pasted set; so does a qualified
    /// name whose second part names a pasted object after a first part that names nothing of this form but its root (the
    /// source form's root name: it becomes this form's). A reference into the pasted set names the copies (renamed parts
    /// replaced; removed when a further part does not resolve); any other reference is kept only when it resolves in this
    /// form's file scope. Null = removed.
    /// </summary>
    private FormValue? PastedReference(FormObject? scope, FormValue value, Dictionary<FormObject, string> names, Dictionary<string, FormObject> pasted)
    {
        if (value is IdentValue { Name: var name })
        {
            if (name.Equals("nil", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if ((ReferenceLookup.ResolveInScope(scope, name) ?? pasted.GetValueOrDefault(name)) is { } inSet)
            {
                return names.TryGetValue(inSet, out var newName) && newName != name ? new IdentValue(newName) : value;
            }

            return ReferenceLookup.ResolvePlain(Document, null, name) is not null ? value : null;
        }

        var parts = ((QualifiedIdentValue)value).Parts;
        var first = 0;
        var current = ReferenceLookup.ResolveInScope(scope, parts[0]) ?? pasted.GetValueOrDefault(parts[0]);
        if (current is null && pasted.GetValueOrDefault(parts[1]) is { } afterRoot
            && Document.FindByName(parts[0]) is var named && (named is null || ReferenceEquals(named, Document.Root)))
        {
            (current, first) = (afterRoot, 1);
        }

        if (current is null)
        {
            return ReferenceLookup.ResolveQualified(Document, null, parts) is not null ? value : null;
        }

        var result = parts.ToBuilder();
        if (first == 1)
        {
            result[0] = Document.Root.Name;
        }

        for (var i = first; ; i++)
        {
            if (names.TryGetValue(current, out var newName))
            {
                result[i] = newName;
            }

            if (i + 1 == parts.Length)
            {
                break;
            }

            current = current.IsFrameInstance ? current.FindInScope(parts[i + 1]) : null;
            if (current is null)
            {
                return null;
            }
        }

        return result.SequenceEqual(parts, StringComparer.Ordinal) ? value : new QualifiedIdentValue(result.ToImmutable());
    }

    /// <summary>The snapshot with <paramref name="path"/> set to an integer (replacing the stored line or appending one).</summary>
    private static FormObjectData WithProperty(FormObjectData data, string path, int value)
    {
        var line = new FormPropertyData(path, new IntegerValue(value, false));
        var index = data.Properties.ToList().FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return data with { Properties = index >= 0 ? data.Properties.SetItem(index, line) : data.Properties.Add(line) };
    }

    /// <summary>A copied integer coordinate (0 when absent or not an integer).</summary>
    private static int Coordinate(FormObject obj, string path) =>
        obj.FindProperty(path) is { Value: IntegerValue { Value: >= int.MinValue and <= int.MaxValue } v } ? (int)v.Value : 0;

    /// <summary>
    /// DSG-TP-043: the next <paramref name="count"/> free tray slots, at (8 + 80·i, 8 + 64·j) row by row, i below
    /// ⌊(trayWidth − 8) / 80⌋ (at least one column); a slot is free when no tray item's cell (nor a slot taken before)
    /// intersects its 72 × 50 rectangle. Tray coordinates.
    /// </summary>
    private List<(int X, int Y)> TraySlots(int count)
    {
        var g = Geometry;
        var taken = g.TrayItems.Select(item =>
        {
            var cell = g.GetTrayCell(item);
            var (x, y) = g.SurfaceToTray((cell.Left, cell.Top));
            return new Rect(x, y, cell.Width, cell.Height);
        }).ToList();
        var columns = Math.Max(1, (g.TrayRect.Width - 2 - 8) / 80);
        var slots = new List<(int X, int Y)>();
        for (var index = 0; slots.Count < count; index++)
        {
            var slot = new Rect(8 + 80 * (index % columns), 8 + 64 * (index / columns), SurfaceGeometry.TrayCellWidth, SurfaceGeometry.TrayCellHeight);
            if (!taken.Exists(t => Intersects(t, slot)))
            {
                taken.Add(slot);
                slots.Add((slot.Left, slot.Top));
            }
        }

        return slots;
    }
}
