using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>
/// The selected components (design-surface.md §3): an ordered list whose <b>last</b> element is the primary selection.
/// Never empty — "nothing selected" is the form root alone — and the root is never part of a multi-selection
/// (DSG-SUR-022). Changes raise <see cref="Changed"/> and are not undo units (DSG-SUR-029).
/// </summary>
public sealed class Selection
{
    private readonly FormDocument _document;
    private List<FormObject> _items;
    private string[] _names;

    public Selection(FormDocument document)
    {
        _document = document;
        _items = [document.Root];
        _names = [document.Root.Name];
    }

    /// <summary>Raised after every change of the content or order (not when an operation leaves it as it was).</summary>
    public event EventHandler? Changed;

    /// <summary>Selection order; the last element is the primary.</summary>
    public IReadOnlyList<FormObject> Items => _items;

    public FormObject Primary => _items[^1];

    /// <summary>True when only the form root is selected.</summary>
    public bool IsFormOnly => _items.Count == 1 && _items[0].Parent is null;

    public bool Contains(FormObject obj) => _items.Exists(o => ReferenceEquals(o, obj));

    /// <summary>DSG-SUR-020 / DSG-SUR-025: selection = { <paramref name="obj"/> }.</summary>
    public void Select(FormObject obj) => Replace([obj]);

    /// <summary>
    /// Selection = <paramref name="objects"/> in that order (the last becomes primary); the root is dropped from a
    /// multi-selection (DSG-SUR-022); an empty list leaves the selection unchanged.
    /// </summary>
    public void Select(IReadOnlyList<FormObject> objects)
    {
        var items = objects.Distinct(ReferenceEqualityComparer.Instance).Cast<FormObject>().ToList();
        if (items.Count > 1)
        {
            items.RemoveAll(o => o.Parent is null);
        }

        if (items.Count > 0)
        {
            Replace(items);
        }
    }

    /// <summary>
    /// DSG-SUR-021 / DSG-SUR-022 (Shift+click, Ctrl+click): an unselected component is appended (it becomes the primary;
    /// it replaces a selection consisting of the root); a selected one is removed when others remain; the only element
    /// stays. Adding the root to a selection of components is ignored.
    /// </summary>
    public void Toggle(FormObject obj)
    {
        if (Contains(obj))
        {
            if (_items.Count > 1)
            {
                Replace(_items.Where(o => !ReferenceEquals(o, obj)).ToList());
            }

            return;
        }

        if (obj.Parent is null)
        {
            return;
        }

        Replace(IsFormOnly ? [obj] : [.. _items, obj]);
    }

    /// <summary>
    /// DSG-SUR-030: selection from outside by names (the last becomes primary); unknown names are skipped, the root is
    /// dropped from a multi-selection, nothing left selects the form.
    /// </summary>
    public void Set(IEnumerable<string> names) => Set(names, null);

    private void Set(IEnumerable<string> names, Func<FormObject, bool>? keep)
    {
        var objects = new List<FormObject>();
        foreach (var name in names)
        {
            if (_document.FindByQualifiedName(name) is { } obj && (keep is null || obj.Parent is null || keep(obj))
                && !objects.Exists(o => ReferenceEquals(o, obj)))
            {
                objects.Add(obj);
            }
        }

        if (objects.Count > 1)
        {
            objects.RemoveAll(o => o.Parent is null);
        }

        Replace(objects.Count == 0 ? [_document.Root] : objects);
    }

    /// <summary>
    /// DSG-SUR-024: the selected components without those inside the subtree of another selected one (operations that
    /// move, copy or delete act on the ancestor only), in pre-order (creation order, parents before children).
    /// </summary>
    public IReadOnlyList<FormObject> GetEffectiveSet()
    {
        var selected = new HashSet<FormObject>(_items, ReferenceEqualityComparer.Instance);
        return _document.AllObjects().Where(o => selected.Contains(o) && !HasSelectedAncestor(o, selected)).ToList();
    }

    /// <summary>
    /// DSG-FD-111: after a re-merge the inherited objects are new objects: the selection is chosen again by the qualified
    /// names (a component still in the tree by its current name, a replaced one by the name it had when it was selected).
    /// D-135: only components <paramref name="placeable"/> accepts are chosen again (a component of a read-only
    /// <c>F0112</c> document has no layout bounds), else the form root.
    /// </summary>
    public void Rebind(Func<FormObject, bool>? placeable = null)
    {
        var names = _items.Select((o, i) => IsInTree(o) ? o.QualifiedName : _names[i]).ToList();
        Set(names, placeable);
    }

    /// <summary>True when <paramref name="obj"/> is still in the document's tree (its ancestors reach the root).</summary>
    private bool IsInTree(FormObject obj)
    {
        var top = obj;
        while (top.Parent is { } parent)
        {
            top = parent;
        }

        return ReferenceEquals(top, _document.Root);
    }

    /// <summary>The names for an undo unit (form-document.md §8).</summary>
    public SelectionSnapshot ToSnapshot() => new([.. _items.Select(o => o.QualifiedName)], Primary.QualifiedName);

    private static bool HasSelectedAncestor(FormObject obj, HashSet<FormObject> selected)
    {
        for (var p = obj.Parent; p is not null; p = p.Parent)
        {
            if (selected.Contains(p))
            {
                return true;
            }
        }

        return false;
    }

    private void Replace(List<FormObject> items)
    {
        if (items.SequenceEqual(_items, ReferenceEqualityComparer.Instance))
        {
            return;
        }

        _items = items;
        _names = [.. items.Select(o => o.QualifiedName)];
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
