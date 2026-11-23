using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Designer;

/// <summary>What a <see cref="PropertyGridViewModel"/> edits: the selected objects and where their changes go.</summary>
public interface IPropertyGridTarget
{
    ComponentCatalog Catalog { get; }

    /// <summary>The edited objects; the last is the primary.</summary>
    IReadOnlyList<FormObject> Selection { get; }

    bool IsReadOnly { get; }

    /// <summary>Applies the changes as one unit; false when they were refused (<see cref="FailureMessage"/> then says why).</summary>
    bool Apply(IReadOnlyList<FormChange> changes);

    string? FailureMessage { get; }

    /// <summary>The names a component-reference row may take (object-inspector.md §5.1), by name.</summary>
    IReadOnlyList<string> References(PropertyInfo info);
}

/// <summary>
/// The property grid of object-inspector.md §2–§6 over an <see cref="IPropertyGridTarget"/>: rows from
/// <see cref="PropertyGridModel"/>, the current row, expansion, the text edit lifecycle with the value converters'
/// validation (DSG-OI-032 … 035, 092), reset to default (DSG-OI-038), Boolean/set toggles (DSG-OI-050), value cycling and
/// the strings editor (DSG-OI-055). The collection editor embeds it in item mode (DSG-OI-113).
/// </summary>
public sealed class PropertyGridViewModel : ObservableObject
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly IPropertyGridTarget _target;
    private readonly IDialogService? _dialogs;
    private readonly PropertyGridModel _grid;
    private string? _currentPath;
    private string _editText = "";
    private string? _editError;
    private bool _isEditing;
    private string? _original;

    public PropertyGridViewModel(IPropertyGridTarget target, IDialogService? dialogs = null, bool itemMode = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _dialogs = dialogs;
        _grid = new PropertyGridModel(target.Catalog) { ItemMode = itemMode };
    }

    /// <summary>The visible rows (re-read on every access).</summary>
    public IReadOnlyList<PropertyRow> Rows
    {
        get
        {
            _grid.SetSelection(_target.Selection);
            return _grid.Rows;
        }
    }

    /// <summary>The path of the current row (null: none).</summary>
    public string? CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value ?? "");
    }

    /// <summary>The inline error of a refused entry (UI-DSG-109); null without one.</summary>
    public string? EditError
    {
        get => _editError;
        private set => SetProperty(ref _editError, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    private PropertyRow? CurrentRow => Rows.FirstOrDefault(r => r.Kind == PropertyRowKind.Property && Names.Equals(r.Path, CurrentPath));

    private bool CanEdit => _target.Selection.Count > 0 && !_target.IsReadOnly;

    /// <summary>The target's objects or values changed: the rows are re-read.</summary>
    public void Refresh()
    {
        if (CurrentPath is { } path && !Rows.Any(r => Names.Equals(r.Path, path)))
        {
            CurrentPath = null;
        }

        OnPropertyChanged(nameof(Rows));
    }

    public void SetCurrent(string? path)
    {
        if (IsEditing)
        {
            Commit(InspectorCommitCause.FocusLost);
        }

        CurrentPath = path is not null && Rows.FirstOrDefault(r => Names.Equals(r.Path, path)) is { } row ? row.Path : null;
    }

    /// <summary>Up/Down: the previous/next property row (category rows are skipped).</summary>
    public void MoveCurrent(int delta)
    {
        var rows = Rows.Where(r => r.Kind == PropertyRowKind.Property).Select(r => r.Path).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var index = CurrentPath is { } path ? rows.FindIndex(p => Names.Equals(p, path)) : -1;
        SetCurrent(rows[Math.Clamp(index < 0 ? (delta > 0 ? 0 : rows.Count - 1) : index + delta, 0, rows.Count - 1)]);
    }

    public void Expand(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _grid.ExpandedPaths.Add(path);
        Refresh();
    }

    public void Collapse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _grid.ExpandedPaths.Remove(path);
        Refresh();
    }

    /// <summary>DSG-OI-032: edit mode on the current row; <paramref name="entry"/> replaces the shown value.</summary>
    public bool BeginEdit(string? entry = null)
    {
        if (!CanEdit || CurrentRow is not { } row || !IsTextEditable(row))
        {
            return false;
        }

        _original = row.Display;
        EditText = entry ?? row.Display;
        EditError = null;
        IsEditing = true;
        return true;
    }

    /// <summary>
    /// Commits the edit: unchanged text ends it (DSG-OI-034); an entry refused for any object applies nothing (DSG-OI-092)
    /// — Enter keeps the editor with the message, focus loss cancels (DSG-OI-035). True when the edit ended without error.
    /// </summary>
    public bool Commit(InspectorCommitCause cause = InspectorCommitCause.Enter)
    {
        if (!IsEditing)
        {
            return true;
        }

        if (string.Equals(EditText, _original, StringComparison.Ordinal) || CurrentRow is not { } row)
        {
            Cancel();
            return true;
        }

        var error = CommitValue(row, EditText);
        if (error is null || cause == InspectorCommitCause.FocusLost)
        {
            Cancel();
            Refresh();
            return error is null;
        }

        EditError = error;
        return false;
    }

    public void Cancel()
    {
        IsEditing = false;
        EditError = null;
        _original = null;
    }

    /// <summary>DSG-OI-038: a stored value of a writable row can be removed.</summary>
    public bool CanResetToDefault =>
        CanEdit && CurrentRow is { Source: PropertyRowSource.Metadata, IsReadOnly: false, SetMember: null, Info: { } info } row
        && _target.Selection.Any(o => StoredPaths(o, row.Path, info).Any());

    public void ResetToDefault()
    {
        if (!CanResetToDefault || CurrentRow is not { Info: { } info } row)
        {
            return;
        }

        _target.Apply([.. _target.Selection.SelectMany(o => StoredPaths(o, row.Path, info).Select(p => (FormChange)new RemoveProperty(o.Name, p)))]);
        Refresh();
    }

    /// <summary>DSG-OI-050: Space / double-click on a Boolean row or a set member row toggles and commits at once.</summary>
    public void ToggleBoolean()
    {
        if (!CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
        {
            return;
        }

        var changes = new List<FormChange>();
        if (row.SetMember is { } member)
        {
            var path = row.Path[..row.Path.LastIndexOf('.')];
            var members = SetMembers(info);
            foreach (var obj in _target.Selection)
            {
                var chosen = (Effective(obj, path, info) is SetValue s ? s.Elements : []).Where(e => !Names.Equals(e, member)).ToList();
                if (row.Display != "True")
                {
                    chosen.Add(member);
                }

                changes.Add(new SetProperty(obj.Name, path, new SetValue([.. members.Where(m => chosen.Contains(m, Names))])));
            }
        }
        else if (info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            var value = new IdentValue(row.Display == "True" ? "False" : "True");
            changes.AddRange(_target.Selection.Select(o => (FormChange)new SetProperty(o.Name, row.Path, value)));
        }
        else
        {
            return;
        }

        _target.Apply(changes);
        Refresh();
    }

    /// <summary>The drop-down list of a row (§5.1): references, enum members, False/True.</summary>
    public IReadOnlyList<string> ValueList(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_target.Selection.Count == 0 || _target.Selection[^1].Info is not { } info
            || _target.Catalog.FindPropertyPath(info, path) is not { } property)
        {
            return [];
        }

        return property.Kind switch
        {
            PropertyKind.Component => ["(none)", .. _target.References(property)],
            PropertyKind.Enum => [.. _target.Catalog.FindType(property.Type)?.Values ?? []],
            _ when property.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) => ["False", "True"],
            _ => [],
        };
    }

    /// <summary>DSG-OI-032: double-click on an enum or Boolean value cycles to the next value and commits.</summary>
    public bool CycleValue()
    {
        if (!CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row)
        {
            return false;
        }

        if (row.SetMember is not null || info.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            ToggleBoolean();
            return true;
        }

        if (info.Kind != PropertyKind.Enum || ValueList(row.Path) is not { Count: > 0 } list || !BeginEdit())
        {
            return false;
        }

        var index = list.ToList().FindIndex(v => Names.Equals(v, row.Display));
        EditText = list[(index + 1) % list.Count];
        return Commit();
    }

    /// <summary>A <c>TStrings</c> row (<c>editor: "strings"</c>, object-inspector.md §5.1).</summary>
    public static bool IsStringsRow(PropertyRow row) => ObjectInspectorViewModel.IsStringsRow(row);

    /// <summary>DSG-OI-055 (UI-DLG-242): the strings editor on top of the current dialog; OK with a changed text sets the list.</summary>
    public async Task EditStringsAsync()
    {
        if (_dialogs is null || !CanEdit || CurrentRow is not { IsReadOnly: false, Info: { } info } row || !IsStringsRow(row))
        {
            return;
        }

        var selection = _target.Selection;
        var primary = selection[^1];
        var component = selection.Count == 1
            ? primary.Name
            : string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_Components, selection.Count);
        var result = await _dialogs.ShowAsync(new Dialogs.StringsEditorViewModel(row.Name, component, Lines(primary, row.Path, info)));
        if (result is null)
        {
            return;
        }

        var changes = selection.Where(o => !Lines(o, row.Path, info).SequenceEqual(result, StringComparer.Ordinal))
            .Select(o => result.Count == 0
                ? (FormChange)new RemoveProperty(o.Name, row.Path)
                : new SetProperty(o.Name, row.Path, new StringListValue([.. result])))
            .ToList();
        if (changes.Count > 0)
        {
            _target.Apply(changes);
            Refresh();
        }
    }

    /// <summary>Enter on a row: a <c>TStrings</c> row opens its editor, other rows enter edit mode.</summary>
    public async Task ActivateCurrentAsync()
    {
        if (CurrentRow is { } row && IsStringsRow(row))
        {
            await EditStringsAsync();
            return;
        }

        BeginEdit();
    }

    private string? CommitValue(PropertyRow row, string text)
    {
        var converter = new ValueConverter(_target.Catalog);
        var changes = new List<FormChange>();
        foreach (var obj in _target.Selection)
        {
            var info = (obj.Info is { } component ? _target.Catalog.FindPropertyPath(component, row.Path) : null) ?? row.Info!;
            var result = converter.Parse(info, text, Effective(obj, row.Path, info), info.Kind == PropertyKind.Component ? _target.References(info) : null);
            if (!result.Succeeded)
            {
                return DesignerMessages.Format(result.MessageId!, [.. result.Args]);
            }

            changes.Add(new SetProperty(obj.Name, row.Path, result.Value!));
        }

        return _target.Apply(changes) ? null : _target.FailureMessage ?? "";
    }

    private FormValue Effective(FormObject obj, string path, PropertyInfo info) =>
        obj.FindProperty(path)?.Value ?? PropertyAccess.DefaultValue(_target.Catalog, info);

    private IReadOnlyList<string> Lines(FormObject obj, string path, PropertyInfo info) =>
        Effective(obj, path, info) is StringListValue list ? list.Lines : [];

    private ImmutableArray<string> SetMembers(PropertyInfo set) =>
        _target.Catalog.FindType(set.Type) is { Kind: TypeKind.Set, ElementType: { } element } && _target.Catalog.FindType(element) is { } members
            ? members.Values
            : [];

    private static IEnumerable<string> StoredPaths(FormObject obj, string path, PropertyInfo info) =>
        info.Kind == PropertyKind.Object
            ? obj.Properties.Where(p => p.Path.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)).Select(p => p.Path).ToList()
            : obj.FindProperty(path) is { } stored ? [stored.Path] : [];

    private static bool IsTextEditable(PropertyRow row) =>
        !row.IsReadOnly && row.SetMember is null && row.Source == PropertyRowSource.Metadata && row.Info is { } info
        && info.Kind is not (PropertyKind.Object or PropertyKind.Collection)
        && !info.Type.Equals("TStrings", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(info.Editor, "picture", StringComparison.OrdinalIgnoreCase);
}
