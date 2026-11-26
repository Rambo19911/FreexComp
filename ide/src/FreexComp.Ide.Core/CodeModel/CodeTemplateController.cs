using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// I-118: gives every open Pascal document the code templates (<see cref="CodeTemplateService"/>, Tab expansion per
/// <c>editor.templates.expandOnTab</c>) and handles <c>edit.insertTemplate</c> (Ctrl+J, IDE-ED-062).
/// </summary>
public sealed class CodeTemplateController
{
    public const string CommandId = "edit.insertTemplate";

    private readonly IDocumentService _documents;
    private readonly CodeTemplateService _templates;
    private readonly ISettingsService _settings;

    public CodeTemplateController(IDocumentService documents, CodeTemplateService templates, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _templates = templates;
        _settings = settings;
        documents.DocumentOpened += (_, e) => Attach(e.Document);
        foreach (var document in documents.Documents)
            Attach(document);
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(CommandId, new Handler(this));
    }

    private sealed class Handler(CodeTemplateController owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) =>
            owner._documents.ActiveDocument?.VisibleText is { Templates: not null, Completion: not null, IsReadOnly: false }; // I-302

        public Task ExecuteAsync(string? argument)
        {
            if (owner._documents.ActiveDocument?.VisibleText is { Completion: { } session })
                session.ShowTemplates();
            return Task.CompletedTask;
        }
    }

    private void Attach(DocumentViewModel document)
    {
        if (document is not TextDocumentViewModel text || text.Language.Id != "pascal" || text.Templates is not null)
            return;
        text.Templates = _templates;
        text.ExpandOnTab = () => _settings.Get(SettingsCatalog.EditorTemplatesExpandOnTab);
    }
}
