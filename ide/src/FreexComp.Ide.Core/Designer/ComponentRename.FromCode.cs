using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Designer;

/// <summary>IDE-REFAC-059, 060 (I-384): a component renamed from code.</summary>
public sealed partial class ComponentRename
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// IDE-REFAC-059: the form file whose designer renames the target of <paramref name="plan"/>, or null when the rename is
    /// simple (not a component, or the designer's rename would change the project's forms in no more than the object headers
    /// the plan edits). A component needs the designer when a descendant form or a frame instance follows it (the project's
    /// ancestor catalog, here) or a line of its own form refers to it (a dry run of the designer's rename on a copy of the
    /// plan's form text, off the UI thread); a form text that cannot be parsed counts as "needs the designer". UI thread.
    /// </summary>
    public async Task<string?> DesignerFormAsync(RenameInfo info, RenamePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(plan);
        if (info.Kind != CompletionItemKind.Field || !info.IsPublished || plan.Edit is not { } edit || info.DeclarationPaths.IsEmpty)
        {
            return null;
        }

        var forms = edit.Files.Where(f => f.Path.EndsWith(".form", StringComparison.OrdinalIgnoreCase)).ToList();
        if (forms.Count == 0)
        {
            return null;
        }

        var documents = _documents as DocumentService;
        var className = info.ContainerName.Split('.')[^1];
        if (documents?.DesignAncestorRename?.HasFollowers(className, info.CurrentName) == true)
        {
            return forms[0].Path;
        }

        var catalog = documents?.DesignCatalog ?? ComponentCatalog.Empty;
        var frames = documents?.DesignFrames;
        var ancestors = documents?.DesignAncestors;
        var unitPath = info.DeclarationPaths[0];
        var unitText = edit.Files.FirstOrDefault(f => Names.Equals(f.Path, unitPath))?.BaseText;
        foreach (var form in forms)
        {
            var text = form.BaseText;
            if (await Task.Run(() => IsReferenced(text, form.Path, info.CurrentName, catalog, frames, ancestors, unitText, unitPath), ct))
            {
                return form.Path;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the designer's rename of <paramref name="name"/> in the form <paramref name="text"/> changes more lines than
    /// its object header (DSG-FD-082: a component reference names it), or the text cannot be judged. Any thread: the form is
    /// a private copy.
    /// </summary>
    private static bool IsReferenced(string text, string path, string name, ComponentCatalog catalog, IAncestorCatalog? frames,
        IAncestorCatalog? ancestors, string? unitText, string unitPath)
    {
        try
        {
            var parsed = FormParser.Parse(text, path, catalog, null, frames, ancestors);
            if (parsed.Document is { } first && catalog.Find(first.Root.TypeName) is null && unitText is not null
                && FormClassLocator.Locate(unitText, first.Root.TypeName, unitPath).FormClass?.AncestorName is { } ancestor)
            {
                parsed = FormParser.Parse(text, path, catalog, ancestor, frames, ancestors);
            }

            if (parsed.Document is not { } document || document.FindByName(name) is not { Parent: not null } obj)
            {
                return true;
            }

            var temporary = "RenameCheck";
            for (var i = 1; document.FindByName(temporary) is not null; i++)
            {
                temporary = "RenameCheck" + i.ToString(CultureInfo.InvariantCulture);
            }

            var renamed = obj.QualifiedName;
            var touched = new List<string>();
            document.Changed += (_, e) => touched.AddRange(e.ObjectNames);
            new FormEditor(document, catalog).Apply(new RenameObject(renamed, temporary));
            return touched.Any(n => !Names.Equals(n, renamed) && !Names.Equals(n, obj.QualifiedName));
        }
        catch (InvalidFormChangeException)
        {
            return true; // the designer refuses it there: its own message tells why
        }
        catch (InvalidOperationException)
        {
            return true; // the catalog changed while it was read: judged as not simple
        }
    }

    /// <summary>
    /// IDE-REFAC-060: the designer's rename of <paramref name="oldName"/> to <paramref name="newName"/> in the form of
    /// <paramref name="unitPath"/> (<paramref name="formPath"/>): the document opened when needed and activated, its Design
    /// view shown, the component selected; then the validation of DSG-OI-070 (a refusal is shown as
    /// <c>Msg.Designer.RenameRefused</c>) and the rename with its code references (DSG-CS-130…134, one designer undo unit);
    /// on success the status of the designer's count of code references (<c>Msg.Designer.RenameCodeRefs*</c>, DSG-CS-134;
    /// I-410), or <c>Msg.Rename.DesignerDone</c> when it changed no code reference. True when the rename was made.
    /// </summary>
    /// <exception cref="RefactoringException">
    /// <c>Reason_Rename_FormNotLoaded</c>: the document cannot be opened, has no usable Design view or is read only; nothing changed.
    /// </exception>
    public async Task<bool> RenameFromCodeAsync(string unitPath, string formPath, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unitPath);
        ArgumentNullException.ThrowIfNull(formPath);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        var formFile = Path.GetFileName(formPath);
        RefactoringException NotLoaded(string reason) =>
            new(string.Format(CultureInfo.CurrentCulture, Strings.Reason_Rename_FormNotLoaded, formFile, reason));

        var opened = await _documents.OpenAsync(unitPath);
        if (opened.Value is not FormDocumentViewModel form)
        {
            throw NotLoaded(opened.Value is null && opened.MessageId is { } id
                ? DesignerMessages.Format(id, [.. opened.Args])
                : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormFileMissing, formPath));
        }

        _documents.Activate(form);
        var designer = form.Designer;
        if (form.IsReadOnly)
        {
            throw NotLoaded(Strings.Msg_File_ReadOnly);
        }

        if (designer.IsMetadataLoading)
        {
            throw NotLoaded(Strings.Reason_Rename_MetadataLoading);
        }

        if (!form.ShowView(DocumentView.Design) || designer.Model is not { } model || designer.Surface is not { } surface)
        {
            throw NotLoaded(form.FormText.InfoBars.FirstOrDefault(b => b.Kind == FormDesignerViewModel.FormSyntaxInfoBarKind)?.Text
                ?? Strings.Msg_File_ReadOnly);
        }

        if (surface.IsReadOnly)
        {
            throw NotLoaded(designer.DesignInfoBars.FirstOrDefault()?.Text ?? Strings.Msg_File_ReadOnly);
        }

        if (model.FindByName(oldName) is not { Parent: not null } obj)
        {
            throw NotLoaded(DesignerMessages.Format("Msg.Designer.ReferenceNotFound", oldName));
        }

        surface.Selection.Select(obj);
        var old = obj.Name;
        if (PropertyGridViewModel.ValidateName(designer, obj, newName) is { } error)
        {
            designer.ShowErrorDialog("Msg.Designer.RenameRefused", [old, newName, error]);
            return false;
        }

        if (newName == old)
        {
            return true;
        }

        (_renamingFromCode, _countShown) = (designer, false);
        try
        {
            var started = Rename(designer, obj, newName, [], UndoTitles.Rename);
            if (started == false)
            {
                designer.ShowErrorDialog("Msg.Designer.RenameRefused", [old, newName, designer.AncestorRename?.LastFailure ?? ""]);
                return false;
            }

            if (started is null && !await designer.RenameWork!)
            {
                return false; // the prepare showed why
            }

            // I-410: the designer's own count of code references stays in the status line; without one, the designer note.
            if (!_countShown)
            {
                designer.ShowStatus("Msg.Rename.DesignerDone", [old, newName]);
            }

            return true;
        }
        finally
        {
            _renamingFromCode = null;
        }
    }
}
