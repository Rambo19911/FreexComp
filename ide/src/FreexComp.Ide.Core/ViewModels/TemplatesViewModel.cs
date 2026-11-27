using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Templates (code-templates.md §5, UI-TW-1600…1602; F11-052): the code templates of the active document's language with
/// abbreviation, description and origin, filtered by abbreviation and description; the default action inserts the
/// selected template at the caret as Ctrl+J would (IDE-ED-062). Live with <see cref="CodeTemplateService.Changed"/>.
/// Owns <c>templates.insert</c>, <c>templates.edit</c>, <c>templates.new</c>.
/// </summary>
public sealed class TemplatesViewModel : DebugGridViewModel
{
    public const string ToolName = "Templates";

    private readonly CodeTemplateService _templates;
    private readonly IDocumentService _documents;
    private string _filter = "";
    private bool _attached;

    public TemplatesViewModel(ICommandService commands, CodeTemplateService templates, IDocumentService documents)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(documents);
        _templates = templates;
        _documents = documents;
        templates.Changed += (_, _) => Update(); // UI-TW-1602
        documents.ActiveDocumentChanged += (_, _) => Update();
    }

    /// <summary>UI-TW-1600: home dock right (with the Tool Palette), in no profile's default layout, P8.</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_templates", ToolGroupId.RightLower, 8, () => this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Abbreviation", Strings.Templates_Column_Abbreviation, 120),
        new("Description", Strings.Templates_Column_Description, 0),
        new("Origin", Strings.Templates_Column_Origin, 120),
    ];

    public override bool HasFilter => true;

    /// <summary>UI-TW-1600: <c>Filter templates</c>.</summary>
    public override string FilterPlaceholder => Strings.Templates_FilterPlaceholder;

    /// <summary>UI-TW-1600.</summary>
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } = [new("templates.new", "plus"), new("templates.edit", "pencil")];

    /// <summary>UI-TW-1601.</summary>
    public override IReadOnlyList<string?> ContextCommands { get; } = ["templates.insert", null, "templates.edit", "templates.new"];

    /// <summary>The template of the selected row; null when none.</summary>
    public CodeTemplate? SelectedTemplate => SelectedRows.Select(r => r.Tag).OfType<CodeTemplate>().FirstOrDefault();

    /// <summary>The active editor that takes an insertion: a writable document with templates (Pascal); null when none.</summary>
    private TextDocumentViewModel? Target => _documents.ActiveDocument?.VisibleText is { Templates: not null, IsReadOnly: false } text ? text : null;

    /// <summary>UI-TW-1600: the language shown — the active document's, else Pascal.</summary>
    private string Language => _documents.ActiveDocument?.VisibleText is { Templates: not null } text ? text.Language.Id : "pascal";

    public void RegisterCommands()
    {
        Commands.RegisterHandler("templates.insert", new Handler(() => SelectedTemplate is not null && Target is not null, () => InsertAsync(SelectedTemplate!)));
        Commands.RegisterHandler("templates.edit", new Handler(() => SelectedTemplate is not null,
            () => Commands.ExecuteAsync("tools.options", OptionsViewModel.CodeTemplatesPage + OptionsViewModel.TemplateSuffix + SelectedTemplate!.Abbreviation)));
        Commands.RegisterHandler("templates.new", new Handler(() => true,
            () => Commands.ExecuteAsync("tools.options", OptionsViewModel.CodeTemplatesPage + OptionsViewModel.TemplateSuffix + OptionsViewModel.NewTemplate)));
    }

    /// <summary>
    /// UI-TW-1601: inserts <paramref name="template"/> at the caret of the active editor as committing it from Ctrl+J does
    /// (IDE-ED-062); a selection is replaced and becomes <c>$selection$</c>. The editor gets the focus.
    /// </summary>
    public Task InsertAsync(CodeTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (Target is not { } document)
            return Task.CompletedTask;
        var selection = document.Selection;
        var selected = selection.IsEmpty ? "" : document.Buffer.GetText(selection.Start, selection.End - selection.Start);
        document.ExpandTemplate(template, selection.Start, selection.End - selection.Start, selected);
        ReturnFocusToDocument();
        return Task.CompletedTask;
    }

    /// <summary>UI-TW-1601: double-click (and Enter, <c>templates.insert</c>) inserts.</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column) =>
        row?.Tag is CodeTemplate template && Target is not null ? InsertAsync(template) : Task.CompletedTask;

    /// <summary>The rows are built once the window is first shown (nothing reads the templates before).</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        _attached = true;
        Update();
    }

    /// <summary>UI-TW-1600: the filter matches abbreviation and description (case-insensitive substring).</summary>
    protected override void OnFilterApplied(string filter)
    {
        _filter = filter.Trim();
        Update();
    }

    /// <summary>UI-TW-1600: <c>Built-in</c>, <c>User</c>, or the folder file name.</summary>
    public static string OriginText(CodeTemplateOrigin origin) => origin?.Kind switch
    {
        CodeTemplateOriginKind.BuiltIn => Strings.Templates_Origin_BuiltIn,
        CodeTemplateOriginKind.Folder => origin.FileName ?? "",
        _ => Strings.Templates_Origin_User,
    };

    /// <summary>UI-TW-1600, 1602: the rows by abbreviation (ordinal-ignore-case); the selection stays on its abbreviation.</summary>
    private void Update()
    {
        if (!_attached)
            return;
        var language = Language;
        var rows = _templates.Templates
            .Where(t => t.Language == language)
            .Where(t => _filter.Length == 0 || t.Abbreviation.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || t.Description.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Abbreviation, StringComparer.OrdinalIgnoreCase)
            .Select(t => new DebugGridRow(t.Abbreviation,
                [new(t.Abbreviation, DebugCellStyle.Code), new(t.Description), new(OriginText(_templates.OriginOf(t)))])
            {
                Tag = t,
                AutomationName = t.Description.Length == 0 ? t.Abbreviation : t.Abbreviation + ", " + t.Description,
            })
            .ToList();
        SetEmpty(rows.Count == 0 ? Strings.Msg_Templates_Empty : null); // UI-TW-1602 (UI-TW-013)
        SetRows(rows);
    }
}
