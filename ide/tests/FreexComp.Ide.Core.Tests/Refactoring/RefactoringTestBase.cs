using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// The fixture of the refactoring-engine tests (I-347 … I-351): an in-memory project of four units, optionally with a form
/// unit, the engine of a test composition, and the helpers that build edits and compare the state of files and buffers.
/// </summary>
public abstract class RefactoringTestBase
{
    protected const string Dir = @"C:\p\App";
    protected const string ProjectPath = Dir + @"\App.pproj";
    protected const string A = Dir + @"\A.pas";
    protected const string B = Dir + @"\B.pas";
    protected const string C = Dir + @"\C.pas";
    protected const string D = Dir + @"\D.pas";
    protected const string Title = "Rename Symbol";

    protected const string ProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas" }, { "path": "B.pas" }, { "path": "C.pas" }, { "path": "D.pas" } ] }
        """;

    protected static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    protected readonly InMemoryFileSystem _fs = new();
    protected readonly RecordingFileSystem _recording;
    protected readonly ManualClock _clock = new();
    protected IdeServices _services;

    protected RefactoringTestBase()
    {
        _recording = new RecordingFileSystem(_fs);
        _services = Compose();
    }

    /// <summary>A composition over the fixture's file system and clock; <paramref name="configure"/> adds to it (the refactorings of I-351).</summary>
    protected IdeServices Compose(Action<TestCompositionOptions>? configure = null) => TestComposition.Build(o =>
    {
        o.FileSystem = _recording;
        o.Clock = _clock;
        configure?.Invoke(o);
    });

    /// <summary>The toolchain under <c>G:\</c> with the real system unit, so that the project binds (as <c>UnitRenameTests</c>).</summary>
    protected void WriteToolchain()
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(System.IO.File.Exists))
            File(@"G:\rtl\" + Path.GetFileName(file), System.IO.File.ReadAllText(file));
        foreach (var tool in (string[])[@"G:\llvm\clang.exe", @"G:\llvm\lld-link.exe", @"G:\llvm\llvm-dlltool.exe"])
            File(tool, "");
        _services.Settings.Set(SettingsCatalog.ToolchainLlvmBinDir, @"G:\llvm");
        _services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
    }

    protected IRefactoringService Engine => _services.Refactoring;

    protected FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    protected RecordingLog Log => (RecordingLog)_services.Log;

    /// <summary>The number of operations that can be undone one after the other (IDE-REFAC-020).</summary>
    protected int HistoryCount => ((RefactoringService)Engine).HistoryCount;

    protected static string Unit(string name, string newLine = "\n") =>
        $"unit {name};\n\ninterface\n\nprocedure Old;\n\nimplementation\n\nprocedure Old;\nbegin\nend;\n\nend.\n".Replace("\n", newLine, StringComparison.Ordinal);

    protected void Write(string path, byte[] bytes)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, bytes);
    }

    protected void File(string path, string text) => Write(path, Encoding.UTF8.GetBytes(text));

    protected byte[] Bytes(string path) => _fs.ReadAllBytes(path);

    protected string Text(string path) => EncodingDetector.Detect(Bytes(path)).Text;

    /// <summary>The project at <see cref="Dir"/> with the units A … D (UTF-8, LF), open, no document open.</summary>
    protected async Task OpenProjectAsync(string? projectFile = null)
    {
        File(ProjectPath, projectFile ?? ProjectFile);
        File(Dir + @"\App.pas", "program App;\n\nuses A, B, C, D;\n\nbegin\nend.\n");
        foreach (var path in (string[])[A, B, C, D])
            File(path, Unit(Path.GetFileNameWithoutExtension(path)));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync()); // the main source opened with the project
        await _services.ProjectManager.MissingFileChecks;
    }

    protected async Task<TextDocumentViewModel> OpenAsync(string path) =>
        Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(path)).Value);

    /// <summary>The file edit that replaces every <c>Old</c> of <paramref name="baseText"/> by <c>Renamed</c>.</summary>
    protected static FileEdit Rename(string path, string baseText)
    {
        var edits = ImmutableArray.CreateBuilder<TextEdit>();
        for (var at = baseText.IndexOf("Old", StringComparison.Ordinal); at >= 0; at = baseText.IndexOf("Old", at + 3, StringComparison.Ordinal))
            edits.Add(new TextEdit(at, 3, "Renamed"));
        Assert.NotEmpty(edits);
        return new FileEdit(path, baseText, edits.ToImmutable());
    }

    protected static string Renamed(string text) => text.Replace("Old", "Renamed", StringComparison.Ordinal);

    /// <summary>A file edit that inserts <c>{x}</c> at the start of the current text of an open document or of a file.</summary>
    protected FileEdit Insert(string path)
    {
        var open = _services.Documents.Documents.OfType<TextDocumentViewModel>()
            .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        return new FileEdit(path, open?.Buffer.Text ?? Text(path), [new TextEdit(0, 0, "{x}")]);
    }

    protected static WorkspaceEdit Edit(params FileEdit[] files) => new(Title, [.. files]);

    /// <summary>The state before the operation of <see cref="ApplyToThreeFilesAsync"/>: the two open documents with their texts, the closed file with its text and bytes.</summary>
    protected sealed record ThreeFiles(TextDocumentViewModel A, TextDocumentViewModel B, string AText, string BText, string CText, byte[] CBytes);

    /// <summary>
    /// The operation of F13-003 acceptance 1, applied: A.pas open and unmodified, B.pas open with an unsaved change, C.pas
    /// closed (UTF-8 with a byte-order mark, CRLF); every <c>Old</c> of the three is renamed.
    /// </summary>
    protected async Task<ThreeFiles> ApplyToThreeFilesAsync()
    {
        await OpenProjectAsync();
        var cText = Unit("C", "\r\n");
        Write(C, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(cText)]);
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]);
        var before = new ThreeFiles(a, b, a.Buffer.Text, b.Buffer.Text, cText, Bytes(C));
        var result = await Engine.ApplyAsync(Edit(Rename(B, before.BText), Rename(C, cText), Rename(A, before.AText)), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal((Renamed(before.AText), Renamed(before.BText), Renamed(cText)), (a.Buffer.Text, b.Buffer.Text, Text(C)));
        return before;
    }

    /// <summary>The three files are as before the operation: C byte for byte, A unmodified without a redo, B with its unsaved text.</summary>
    protected void AssertRestored(ThreeFiles before)
    {
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.Equal(before.AText, before.A.Buffer.Text);
        Assert.False(before.A.IsModified);
        Assert.False(before.A.Buffer.CanRedo);
        Assert.Equal(before.BText, before.B.Buffer.Text);
        Assert.True(before.B.IsModified);
        Assert.False(before.B.Buffer.CanRedo);
    }

    protected static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);

    /// <summary>Every file with its bytes: equal before and after = no file changed and no temporary file is left.</summary>
    protected string FilesSnapshot() =>
        string.Join('\n', _fs.AllFiles.Order(StringComparer.OrdinalIgnoreCase).Select(p => p + " = " + Convert.ToHexString(_fs.ReadAllBytes(p))));

    /// <summary><see cref="FilesSnapshot"/> and every open text document with its buffer version: equal before and after = nothing changed.</summary>
    protected string Snapshot() => FilesSnapshot() + "\n--\n"
        + string.Join('\n', _services.Documents.Documents.OfType<TextDocumentViewModel>().Select(d => d.FilePath + " @ " + d.Buffer.Version));

    protected static bool IsWrite(string call) => ((string[])["WriteAllBytes", "ReplaceFile", "OverwriteFile", "MoveFile", "DeleteFile"])
        .Any(m => call.StartsWith(m, StringComparison.Ordinal));

    /// <summary>The calls since <paramref name="from"/> that replaced <paramref name="path"/> (the atomic writer's last step).</summary>
    protected int Replacements(int from, string path) =>
        _recording.Calls.Skip(from).Count(c => c.StartsWith("ReplaceFile ", StringComparison.Ordinal) && c.EndsWith(" -> " + path, StringComparison.Ordinal));

    /// <summary>
    /// While <paramref name="path"/> is written for the first time (thread pool), <paramref name="action"/> runs on the
    /// UI thread; the write goes on only after it ran.
    /// </summary>
    protected void DuringFirstWriteOf(string path, Action action)
    {
        var ui = SynchronizationContext.Current!;
        var ran = 0;
        _recording.FailWhen = call =>
        {
            if (!call.StartsWith("WriteAllBytesDurable " + path, StringComparison.Ordinal) || Interlocked.Exchange(ref ran, 1) != 0)
                return false;
            using var done = new ManualResetEventSlim();
            ui.Post(_ =>
            {
                try
                {
                    action();
                }
                finally
                {
                    done.Set();
                }
            }, null);
            done.Wait();
            return false;
        };
    }

    /// <summary>Opens <paramref name="gate"/> when the test ends, however it ends: the held write is let go.</summary>
    protected sealed class Releaser(ManualResetEventSlim gate) : IDisposable
    {
        public void Dispose() => gate.Set();
    }

    protected const string FormUnitPath = Dir + @"\MainForm.pas";
    protected const string FormPath = Dir + @"\MainForm.form";
    protected const string Z = Dir + @"\Z.pas";

    protected const string FormProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas" }, { "path": "B.pas" }, { "path": "C.pas" }, { "path": "D.pas" },
                     { "path": "MainForm.pas", "form": "MainForm.form" } ] }
        """;

    protected static string Form => DesignerFixture.Form;

    /// <summary>The file edit that makes <paramref name="handler"/> the handler of <c>GreetButton.OnClick</c> in the form text <paramref name="baseText"/>.</summary>
    protected static FileEdit Handler(string path, string baseText, string handler)
    {
        const string Value = "OnClick = ";
        var at = baseText.IndexOf(Value, StringComparison.Ordinal) + Value.Length;
        var end = baseText.IndexOf('\n', at);
        Assert.True(at >= Value.Length && end > at);
        return new FileEdit(path, baseText, [new TextEdit(at, end - at, handler)]);
    }

    protected static string WithHandler(string formText, string handler) =>
        formText.Replace("OnClick = GreetButtonClick", "OnClick = " + handler, StringComparison.Ordinal);

    protected static string? OnClick(FormDocumentViewModel document) => DesignerFixture.Stored(document.Designer, "GreetButton", "OnClick");

    /// <summary>A change made in the Design view: the form's caption, one unit on the designer's undo stack.</summary>
    protected static void SetCaption(FormDocumentViewModel document, string caption)
    {
        var change = new SetProperty("MainForm", "Caption", new StringValue(caption));
        var inverse = document.Designer.Editor!.Apply([change]);
        document.Designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
    }

    /// <summary>The form unit with its form on disk; with <paramref name="project"/> the project of <see cref="Dir"/> is open and lists them.</summary>
    protected async Task FormFilesAsync(bool project = true)
    {
        ((DocumentService)_services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        File(FormUnitPath, DesignerFixture.Unit);
        File(FormPath, Form);
        if (project)
            await OpenProjectAsync(FormProjectFile);
    }

    protected async Task<FormDocumentViewModel> OpenFormAsync(string path = FormUnitPath)
    {
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(path)).Value);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.False(document.IsModified);
        return document;
    }
}
