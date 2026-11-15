using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One row of the Code Templates grid (dialogs.md §9.3): a pending template.</summary>
public sealed class CodeTemplateRow : ObservableObject
{
    private string _abbreviation;
    private string _description;
    private string _body;

    internal CodeTemplateRow(CodeTemplate template)
    {
        _abbreviation = template.Abbreviation;
        _description = template.Description;
        _body = template.Body;
        Language = template.Language;
    }

    public string Abbreviation
    {
        get => _abbreviation;
        set => SetProperty(ref _abbreviation, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Body
    {
        get => _body;
        set => SetProperty(ref _body, value);
    }

    public string Language { get; }

    internal CodeTemplate ToTemplate() => new(Abbreviation, Description, Language, Body);
}

/// <summary>
/// The Options page <c>Editor.CodeTemplates</c> (dialogs.md §9.3, I-119): the pending templates (alphabetical by
/// abbreviation), the selected one's fields with their validation, Add / Delete / Reset; OK and Apply write
/// <c>templates.json</c> through <see cref="CodeTemplateService.Save"/>.
/// </summary>
public sealed class CodeTemplatesPageViewModel : ObservableObject
{
    private readonly CodeTemplateService? _service;
    private CodeTemplateRow? _selected;

    internal CodeTemplatesPageViewModel(CodeTemplateService? service)
    {
        _service = service;
        foreach (var template in (service?.Templates ?? CodeTemplateService.BuiltIns).OrderBy(t => t.Abbreviation, StringComparer.OrdinalIgnoreCase))
            Add(new CodeTemplateRow(template));
        _selected = Rows.FirstOrDefault();
    }

    /// <summary>Any pending value changed (the dialog re-evaluates OK and Apply).</summary>
    public event EventHandler? PendingChanged;

    public ObservableCollection<CodeTemplateRow> Rows { get; } = [];

    public CodeTemplateRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                Changed();
        }
    }

    /// <summary><c>Msg.Options.TemplateAbbrevInvalid</c> / <c>…AbbrevExists</c> for the selected row, else null.</summary>
    public string? AbbreviationError => _selected is null ? null : AbbreviationErrorOf(_selected);

    /// <summary><c>Msg.Options.TemplateBodyEmpty</c> for the selected row, else null.</summary>
    public string? BodyError => _selected is { Body.Length: 0 } ? Strings.Msg_Options_TemplateBodyEmpty : null;

    public bool CanDelete => _selected is not null;

    /// <summary>Reset is enabled for a modified built-in.</summary>
    public bool CanReset => _selected is { } row && CodeTemplateService.BuiltIn(row.Abbreviation, row.Language) is { } builtIn && builtIn != row.ToTemplate();

    public bool HasErrors => Rows.Any(r => AbbreviationErrorOf(r) is not null || r.Body.Length == 0);

    public bool HasChanges
    {
        get
        {
            var saved = _service?.Templates ?? CodeTemplateService.BuiltIns;
            var pending = Rows.Select(r => r.ToTemplate()).ToList();
            return saved.Count != pending.Count || pending.Any(t => !saved.Contains(t));
        }
    }

    /// <summary>The Abbreviation column sorts Z…A (false = the natural alphabetical order).</summary>
    public bool SortDescending { get; private set; }

    /// <summary>
    /// The Abbreviation header was clicked (dialogs.md §9.3 "sortable"): the order reverses and every row, added ones
    /// included, is sorted by abbreviation (case-insensitive); the selection stays on its row. No pending change.
    /// </summary>
    public void SortByAbbreviation()
    {
        SortDescending = !SortDescending;
        var ordered = Rows.OrderBy(r => r.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList();
        if (SortDescending)
            ordered.Reverse();
        for (var i = 0; i < ordered.Count; i++)
        {
            var from = Rows.IndexOf(ordered[i]);
            if (from != i)
                Rows.Move(from, i);
        }

        OnPropertyChanged(nameof(SortDescending));
    }

    /// <summary><c>Templates.Add</c>: appends <c>new1</c> (…<c>new&lt;n&gt;</c>) and selects it.</summary>
    public void AddTemplate()
    {
        var n = 1;
        while (Rows.Any(r => r.Abbreviation.Equals("new" + n, StringComparison.OrdinalIgnoreCase)))
            n++;
        var row = new CodeTemplateRow(new CodeTemplate("new" + n, "", "pascal", ""));
        Add(row);
        Selected = row;
    }

    /// <summary><c>Templates.Delete</c>: no confirmation (Cancel restores); the next row is selected.</summary>
    public void DeleteTemplate()
    {
        if (_selected is not { } row)
            return;
        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        Changed();
    }

    /// <summary><c>Templates.Reset</c>: restores the built-in text of the selected row.</summary>
    public void ResetTemplate()
    {
        if (_selected is not { } row || CodeTemplateService.BuiltIn(row.Abbreviation, row.Language) is not { } builtIn)
            return;
        row.Abbreviation = builtIn.Abbreviation;
        row.Description = builtIn.Description;
        row.Body = builtIn.Body;
    }

    internal void Apply()
    {
        if (_service is null || HasErrors || !HasChanges)
            return;
        _service.Save([.. Rows.Select(r => r.ToTemplate())]);
        Changed();
    }

    private void Add(CodeTemplateRow row)
    {
        Rows.Add(row);
        row.PropertyChanged += (_, _) => Changed();
    }

    private string? AbbreviationErrorOf(CodeTemplateRow row)
    {
        if (!CodeTemplateExpander.IsIdentifier(row.Abbreviation) || row.Abbreviation.Length > 16)
            return Strings.Msg_Options_TemplateAbbrevInvalid;
        return Rows.Any(r => !ReferenceEquals(r, row) && r.Language == row.Language && r.Abbreviation.Equals(row.Abbreviation, StringComparison.OrdinalIgnoreCase))
            ? Strings.Msg_Options_TemplateAbbrevExists
            : null;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(AbbreviationError));
        OnPropertyChanged(nameof(BodyError));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanReset));
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }
}
