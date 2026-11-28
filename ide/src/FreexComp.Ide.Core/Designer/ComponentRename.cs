using System.Text;
using Compilation = FreexComp.Compiler.Compilation;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// DSG-CS-130…134 (I-383): the rename of a component (not the form root) in the designer — the Object Inspector's
/// <c>Name</c>, the menu designer — that updates the component's references in code. The validation of DSG-OI-070 is
/// the caller's; this prepares the rename asynchronously (the plan of the language service with forms off, the engine's
/// checks) and commits one designer undo unit: today's parts through <see cref="PropertyGridViewModel.RenameUnit"/> and
/// every other edit of the plan as a linked participant that undo and redo revert and apply by text. Where the code
/// model cannot help (no unit, code sync not Ok, no project or library, the field does not bind) the rename is the
/// declaration alone, as before (DSG-CS-045). UI thread.
/// </summary>
public sealed partial class ComponentRename
{
    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly CodeModelService _codeModel;
    private readonly RenamePlanner _planner;
    private readonly IRefactoringService _engine;
    private readonly IToolchainLocator _toolchain;
    private readonly IFileSystem _fileSystem;
    private readonly HashSet<FormDesignerViewModel> _preparing = [];
    private FormDesignerViewModel? _renamingFromCode; // IDE-REFAC-060: the designer whose rename RenameFromCodeAsync runs
    private bool _countShown; // that rename showed its count of code references (DSG-CS-134)

    public ComponentRename(IDocumentService documents, IProjectService projects, CodeModelService codeModel, RenamePlanner planner,
        IRefactoringService engine, IToolchainLocator toolchain, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _documents = documents;
        _projects = projects;
        _codeModel = codeModel;
        _planner = planner;
        _engine = engine;
        _toolchain = toolchain;
        _fileSystem = fileSystem;
    }

    /// <summary>DSG-CS-131: a rename of <paramref name="designer"/>'s document is being prepared; a further one is not started.</summary>
    public bool IsPreparing(FormDesignerViewModel designer) => _preparing.Contains(designer);

    /// <summary>
    /// Renames <paramref name="obj"/> (validated) to <paramref name="name"/> as <see cref="PropertyGridViewModel.RenameUnit"/>
    /// does, with its references in code (DSG-CS-130). True or false: the rename was made, or refused, at once — the code
    /// model cannot take part, or a rename of the document is being prepared (false). Null: accepted; the prepare runs and
    /// <see cref="FormDesignerViewModel.RenameWork"/> completes after the commit, true when the rename was made.
    /// </summary>
    public bool? Rename(FormDesignerViewModel designer, FormObject obj, string name, IReadOnlyList<FormChange> before, string titleKey)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(before);
        if (_preparing.Contains(designer))
        {
            return false;
        }

        if (obj.Parent is null || Begin(designer, obj) is not { } start)
        {
            return PropertyGridViewModel.RenameUnit(designer, obj, name, before, titleKey);
        }

        _preparing.Add(designer);
        designer.RenameWork = PrepareAndCommitAsync(designer, obj, name, before, titleKey, start);
        return null;
    }

    /// <summary>What the prepare starts from (DSG-CS-131 (1)): the snapshot, the field's declared name, the unit's text and the states that must not change.</summary>
    private sealed record Start(Document Source, int Offset, int Length, string Text, long Version, int UndoCount, DesignerUndoUnit? Top);

    /// <summary>DSG-CS-130, 131 (1): the conditions of the code model on the UI thread, then the starting point; null: the rename is the declaration alone.</summary>
    private Start? Begin(FormDesignerViewModel designer, FormObject obj)
    {
        if (!designer.HasUnit || designer.CodeSync.State != CodeSyncState.Ok || designer.Surface is not { IsReadOnly: false }
            || _toolchain.Resolve().RtlDir is null || FieldEdits.FieldNameSpan(designer.Unit, obj.Name) is not var (offset, length)
            || _codeModel.Synchronize() is not { } compilation || !SpelledElsewhere(compilation, designer.Unit.Path, offset, obj.Name))
        {
            return null;
        }

        var source = _codeModel.Workspace.GetDocument(designer.Unit.Path);
        var text = designer.Unit.Buffer.Text;
        return string.Equals(source.Text.Text, text, StringComparison.Ordinal)
            ? new Start(source, offset, length, text, designer.Unit.Buffer.Version, designer.Undo.UndoCount, designer.Undo.Peek)
            : null;
    }

    /// <summary>
    /// Whether an identifier of the project's sources other than the field's declaration spells <paramref name="name"/>:
    /// otherwise the plan would edit the declaration alone, and the rename is made at once as before — a fresh menu item
    /// or a component the code never names keeps the synchronous designer gesture.
    /// </summary>
    private bool SpelledElsewhere(Compilation compilation, string unitPath, int declaration, string name)
    {
        var paths = new List<string> { unitPath };
        if (_projects.Project is { } project)
        {
            paths.AddRange(project.Files.Where(f => string.Equals(Path.GetExtension(f.Path), ".pas", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFullPath(Path.Combine(project.Directory, f.Path.Replace('/', Path.DirectorySeparatorChar)))));
        }

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var own = string.Equals(path, unitPath, StringComparison.OrdinalIgnoreCase);
            if (compilation.GetSyntaxTree(path).Root.DescendantTokens().Any(t => t.Kind == SyntaxKind.IdentifierToken
                && string.Equals(t.Value as string ?? t.Text, name, StringComparison.OrdinalIgnoreCase) && !(own && t.Span.Start == declaration)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> PrepareAndCommitAsync(FormDesignerViewModel designer, FormObject obj, string name, IReadOnlyList<FormChange> before,
        string titleKey, Start start)
    {
        var old = obj.Name;
        try
        {
            RenamePlan? plan = null;
            try
            {
                var info = await _planner.GetInfoAsync(start.Source, start.Offset, CancellationToken.None);
                if (info.CanRename && info.Kind == CompletionItemKind.Field)
                {
                    plan = await _planner.PlanAsync(new RenameRequest(start.Source, start.Offset, info.CurrentName, name, Overloads: false, Forms: false),
                        CancellationToken.None);
                }
            }
            catch (IOException)
            {
                // The code model cannot read the project now: the declaration alone (DSG-CS-045).
            }

            // DSG-CS-131 (5) is judged here too: a unit changed meanwhile makes the plan's refusals and checks moot.
            if (Dropped(designer, obj, old, start))
            {
                return false;
            }

            if (plan is null)
            {
                return PropertyGridViewModel.RenameUnit(designer, obj, name, before, titleKey);
            }

            if (!plan.Result.Success || plan.Edit is null)
            {
                var reason = plan.Result.Conflicts.Length > 0 ? plan.Result.Conflicts[0].Message : plan.Result.Error ?? "";
                designer.ShowErrorDialog("Msg.Designer.RenameRefused", [old, name, reason]); // DSG-CS-131 (3)
                return false;
            }

            var edit = plan.Edit.Normalize();
            var blocked = await _engine.CheckAsync(edit);
            if (blocked.Count > 0)
            {
                designer.ShowErrorDialog("Msg.Refactoring.Blocked", [edit.Title, RefactoringService.BlockedLines(blocked)]); // DSG-CS-131 (4)
                return false;
            }

            if (Dropped(designer, obj, old, start))
            {
                return false;
            }

            if (References(designer, edit, start) is not { } references)
            {
                return PropertyGridViewModel.RenameUnit(designer, obj, name, before, titleKey); // the plan does not hold the declaration: as before
            }

            if (!PropertyGridViewModel.RenameUnit(designer, obj, name, before, titleKey, references.Count > 0 ? references : null, out var failure))
            {
                if (failure is var (messageId, args))
                {
                    designer.ShowErrorDialog(messageId, args); // DSG-CS-132: a write that failed, or a descendant (D-144)
                }

                return false;
            }

            ShowCount(designer, old, name, references); // DSG-CS-134
            return true;
        }
        finally
        {
            _preparing.Remove(designer);
        }
    }

    /// <summary>
    /// DSG-CS-131 (5): the unit or the designer changed during the prepare (another buffer version or undo unit, the
    /// object renamed or gone, the form read only, the document closed): nothing is renamed, status <c>Msg.Designer.RenameDropped</c>.
    /// </summary>
    private bool Dropped(FormDesignerViewModel designer, FormObject obj, string old, Start start)
    {
        var unchanged = designer.Unit.Buffer.Version == start.Version && designer.Undo.UndoCount == start.UndoCount
            && ReferenceEquals(designer.Undo.Peek, start.Top) && obj.Name == old
            && designer.Model is { } model && ReferenceEquals(model.FindByQualifiedName(obj.QualifiedName), obj)
            && designer.Surface is { IsReadOnly: false } && designer.CodeSync.State == CodeSyncState.Ok
            && _documents.Documents.OfType<FormDocumentViewModel>().Any(d => ReferenceEquals(d.Designer, designer));
        if (!unchanged)
        {
            designer.ShowStatus("Msg.Designer.RenameDropped", [old]);
        }

        return !unchanged;
    }

    /// <summary>
    /// DSG-CS-132: the plan's edits other than the field's declaration, as the linked participant; null when the plan
    /// does not edit the declaration in the text it was computed on.
    /// </summary>
    private CodeReferences? References(FormDesignerViewModel designer, WorkspaceEdit edit, Start start)
    {
        var unit = edit.Files.FirstOrDefault(f => string.Equals(f.Path, designer.Unit.Path, StringComparison.OrdinalIgnoreCase));
        if (unit is null || !string.Equals(unit.BaseText, start.Text, StringComparison.Ordinal)
            || unit.Edits.Where(e => e.Offset == start.Offset && e.Length == start.Length).Select(e => (TextEdit?)e).FirstOrDefault() is not { } declaration)
        {
            return null;
        }

        var others = edit.Files.Where(f => !ReferenceEquals(f, unit)).ToList();
        return new CodeReferences(this, edit.Title, designer.Unit.Buffer, designer.Unit.Path, start.Text, declaration,
            [.. unit.Edits.Where(e => e != declaration)], others);
    }

    private void ShowCount(FormDesignerViewModel designer, string old, string name, CodeReferences references)
    {
        var (count, files) = (references.Count, references.FileCount);
        if (count > 0 && ReferenceEquals(designer, _renamingFromCode))
        {
            _countShown = true; // a rename from code keeps this status (I-410)
        }

        if (count == 1)
        {
            designer.ShowStatus("Msg.Designer.RenameCodeRefsOne", [old, name]);
        }
        else if (count > 1 && files == 1)
        {
            designer.ShowStatus("Msg.Designer.RenameCodeRefsOneFile", [old, name, count]);
        }
        else if (count > 1)
        {
            designer.ShowStatus("Msg.Designer.RenameCodeRefsMany", [old, name, count, files]);
        }
    }

    /// <summary>The buffer of the open text document of <paramref name="path"/>, or null when it is closed.</summary>
    private ITextBuffer? BufferOf(string path) => _documents.Documents.OfType<TextDocumentViewModel>()
        .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase))?.Buffer;

    /// <summary>
    /// The text with <paramref name="edits"/> (non-overlapping, offsets into <paramref name="text"/>) applied.
    /// </summary>
    internal static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Offset))
        {
            builder.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.NewText);
        }

        return builder.ToString();
    }

    /// <summary>The edits that take the text <paramref name="edits"/> made of <paramref name="text"/> back to <paramref name="text"/>.</summary>
    internal static List<TextEdit> Inverse(string text, IEnumerable<TextEdit> edits)
    {
        var result = new List<TextEdit>();
        var delta = 0;
        foreach (var edit in edits.OrderBy(e => e.Offset))
        {
            result.Add(new TextEdit(edit.Offset + delta, edit.NewText.Length, text.Substring(edit.Offset, edit.Length)));
            delta += edit.NewText.Length - edit.Length;
        }

        return result;
    }

    /// <summary>
    /// DSG-CS-132, 133: the references of the plan as the linked participant of the designer's undo unit. The unit's own
    /// buffer has four texts — O (as planned), D (the declaration renamed by code sync), A (all renamed) and R (the
    /// references renamed, the declaration not: a redo applies this before code sync's <c>RenameFieldOp</c> renames the
    /// declaration again, DSG-CS-102); every other file has its text before and after. Each step judges every target by
    /// its text first — one in the target state is skipped, one in neither state blocks the step — and then changes the
    /// closed files (all or nothing) and the open buffers (one undo group each, not saved).
    /// </summary>
    private sealed class CodeReferences : ILinkedEdits
    {
        private readonly ComponentRename _owner;
        private readonly string _title;
        private readonly ITextBuffer _unit;
        private readonly string _unitPath;
        private readonly string _o;
        private readonly string _d;
        private readonly string _a;
        private readonly string _r;
        private readonly List<TextEdit> _references;
        private readonly List<TextEdit> _shifted;
        private readonly List<Other> _others;
        private bool _committed;

        public CodeReferences(ComponentRename owner, string title, ITextBuffer unit, string unitPath, string original, TextEdit declaration,
            List<TextEdit> references, List<FileEdit> others)
        {
            _owner = owner;
            _title = title;
            _unit = unit;
            _unitPath = unitPath;
            _o = original;
            _references = references;
            var delta = declaration.NewText.Length - declaration.Length;
            _shifted = [.. references.Select(e => e.Offset > declaration.Offset ? e with { Offset = e.Offset + delta } : e)];
            _d = ComponentRename.Apply(original, [declaration]);
            _a = ComponentRename.Apply(original, [declaration, .. references]);
            _r = ComponentRename.Apply(original, references);
            _others = [.. others.Select(f => new Other(f.Path, f.BaseText, ComponentRename.Apply(f.BaseText, f.Edits), [.. f.Edits]))];
        }

        /// <summary>The number of edits outside the declaration (DSG-CS-134).</summary>
        public int Count => _references.Count + _others.Sum(o => o.Edits.Count);

        /// <summary>The number of files those edits are in.</summary>
        public int FileCount => (_references.Count > 0 ? 1 : 0) + _others.Count;

        public (string MessageId, object[] Args)? Failure { get; private set; }

        public bool Apply() => Step(forward: true);

        public bool Revert() => Step(forward: false);

        /// <summary>One file other than the unit: its text before and after, and the bytes of the last write (closed files).</summary>
        private sealed class Other(string path, string before, string after, List<TextEdit> edits)
        {
            public string Path { get; } = path;

            public string Before { get; } = before;

            public string After { get; } = after;

            public List<TextEdit> Edits { get; } = edits;

            /// <summary>The bytes the file had before the last write of this participant, and the bytes written then.</summary>
            public (byte[] Previous, byte[] Written)? LastWrite { get; set; }
        }

        /// <summary>What a step does to a target: the buffer and its edits, or the file and the bytes it gets.</summary>
        private sealed record Change(Other? Other, ITextBuffer? Buffer, IReadOnlyList<TextEdit> Edits, byte[]? Current, byte[]? Bytes);

        private bool Step(bool forward)
        {
            Failure = null;
            var actions = new List<Change>();
            var conflicts = new List<BlockedFile>();
            var reason = _committed ? BlockReason.ChangedAfterwards : BlockReason.ChangedSinceComputed;
            if (_references.Count > 0)
            {
                var text = _unit.Text;
                var edits = (forward, text) switch
                {
                    (true, var t) when t == _d => _shifted,
                    (true, var t) when t == _o => _references,
                    (false, var t) when t == _a => Inverse(_d, _shifted),
                    (false, var t) when t == _r => Inverse(_o, _references),
                    _ => null,
                };
                if (edits is not null)
                {
                    actions.Add(new Change(null, _unit, edits, null, null));
                }
                else if (!(forward ? text == _a || text == _r : text == _d || text == _o))
                {
                    conflicts.Add(new BlockedFile(_unitPath, reason));
                }
            }

            foreach (var other in _others)
            {
                var (from, to) = forward ? (other.Before, other.After) : (other.After, other.Before);
                if (_owner.BufferOf(other.Path) is { } buffer)
                {
                    if (buffer.Text == from)
                    {
                        actions.Add(new Change(other, buffer, forward ? other.Edits : Inverse(other.Before, other.Edits), null, null));
                    }
                    else if (buffer.Text != to)
                    {
                        conflicts.Add(new BlockedFile(other.Path, reason));
                    }

                    continue;
                }

                byte[] current;
                try
                {
                    current = _owner._fileSystem.ReadAllBytes(other.Path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return Fail(forward, other.Path, e.Message);
                }

                var detection = EncodingDetector.Detect(current);
                if (!forward && other.LastWrite is { } last && current.AsSpan().SequenceEqual(last.Written))
                {
                    actions.Add(new Change(other, null, [], current, last.Previous)); // untouched since: its bytes come back
                }
                else if (!detection.IsBinary && detection.Text == from)
                {
                    try
                    {
                        actions.Add(new Change(other, null, [], current, FileEncodings.Encode(to, detection.Encoding)));
                    }
                    catch (EncoderFallbackException e)
                    {
                        return Fail(forward, other.Path, e.Message);
                    }
                }
                else if (detection.IsBinary || detection.Text != to)
                {
                    conflicts.Add(new BlockedFile(other.Path, reason));
                }
            }

            if (conflicts.Count > 0)
            {
                var lines = RefactoringService.BlockedLines(conflicts);
                Failure = (_committed ? "Msg.Refactoring.UndoBlocked" : "Msg.Refactoring.Blocked", [_title, lines]);
                return false;
            }

            // The closed files first (a write can fail: the ones written are put back), then the buffers.
            var done = new List<Change>();
            foreach (var action in actions.Where(a => a.Buffer is null))
            {
                try
                {
                    AtomicFileWriter.Write(_owner._fileSystem, action.Other!.Path, action.Bytes!);
                    done.Add(action);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    foreach (var back in done.AsEnumerable().Reverse())
                    {
                        TryWrite(back.Other!.Path, back.Current!);
                    }

                    return Fail(forward, action.Other!.Path, e.Message);
                }
            }

            foreach (var action in done)
            {
                action.Other!.LastWrite = (action.Current!, action.Bytes!);
            }

            foreach (var action in actions.Where(a => a.Buffer is not null))
            {
                action.Buffer!.ApplyEdits(action.Edits, _title);
            }

            _committed = true;
            return true;
        }

        private void TryWrite(string path, byte[] bytes)
        {
            try
            {
                AtomicFileWriter.Write(_owner._fileSystem, path, bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The file keeps the new text; the failure that is reported names the file that could not be written.
            }
        }

        private bool Fail(bool forward, string path, string reason)
        {
            Failure = (forward ? "Msg.Refactoring.ApplyFailed" : "Msg.Refactoring.UndoFailed", [_title, path, reason]);
            return false;
        }
    }
}

/// <summary>D-137 with DSG-CS-132: two linked participants of one undo unit — applied in order, reverted in reverse; one that fails puts the other back.</summary>
internal sealed class LinkedPair(ILinkedEdits first, ILinkedEdits second) : ILinkedEdits
{
    public (string MessageId, object[] Args)? Failure { get; private set; }

    /// <summary>The pair, or the one that is not null.</summary>
    public static ILinkedEdits? Of(ILinkedEdits? first, ILinkedEdits? second) =>
        first is null ? second : second is null ? first : new LinkedPair(first, second);

    public bool Apply()
    {
        Failure = null;
        if (!first.Apply())
        {
            Failure = first.Failure;
            return false;
        }

        if (!second.Apply())
        {
            Failure = second.Failure;
            first.Revert();
            return false;
        }

        return true;
    }

    public bool Revert()
    {
        Failure = null;
        if (!second.Revert())
        {
            Failure = second.Failure;
            return false;
        }

        if (!first.Revert())
        {
            Failure = first.Failure;
            second.Apply();
            return false;
        }

        return true;
    }
}
