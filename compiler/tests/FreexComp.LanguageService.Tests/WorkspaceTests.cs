// Diagnostics and model lookups of an in-memory snapshot are synchronous; the optional token is left out on purpose.
#pragma warning disable xUnit1051

using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §1 (CMP-700, 701): workspace snapshots and documents (C-130).</summary>
[Trait("Phase", "3")]
public sealed class WorkspaceTests
{
    private const string Program = "program P;\nvar X: Integer;\nbegin\n  X := 1;\nend.";

    private static Workspace ProjectWorkspace(InMemoryFileSystem? fs = null)
    {
        var workspace = new Workspace(fs ?? TestFiles.Files(("P.pas", Program)));
        workspace.SetProject(TestFiles.Options());
        return workspace;
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void OpenDocument_GivesASnapshotWithTreeAndModel()
    {
        var workspace = ProjectWorkspace();
        workspace.OpenDocument(@"c:\p\P.pas", Program.Replace("X := 1", "X := 2", StringComparison.Ordinal));

        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));

        Assert.Equal(TestFiles.PathOf("P.pas"), document.Path);
        Assert.Contains("X := 2", document.Text.Text, StringComparison.Ordinal);
        Assert.IsType<ProgramSyntax>(document.SyntaxTree.Root);
        Assert.Equal("P", document.SemanticModel.Unit.Name);
        Assert.Same(workspace.CurrentCompilation, document.Compilation);
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void UpdateDocument_LeavesEarlierSnapshotsUnchanged()
    {
        var workspace = ProjectWorkspace();
        var path = TestFiles.PathOf("P.pas");
        workspace.OpenDocument(path, Program);
        var before = workspace.GetDocument(path);

        workspace.UpdateDocument(path, Program.Replace("X := 1", "X := 3", StringComparison.Ordinal));
        var after = workspace.GetDocument(path);

        Assert.Contains("X := 1", before.Text.Text, StringComparison.Ordinal);
        Assert.Contains("X := 3", after.Text.Text, StringComparison.Ordinal);
        Assert.NotSame(before.Compilation, after.Compilation);
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void UpdateDocument_WithTextChanges()
    {
        var workspace = ProjectWorkspace();
        var path = TestFiles.PathOf("P.pas");
        workspace.OpenDocument(path, Program);
        var x = Program.IndexOf("X := 1", StringComparison.Ordinal);

        workspace.UpdateDocument(path, [new TextChange(new TextSpan(x + 5, 1), "42"), new TextChange(new TextSpan(x, 1), "X")]);
        workspace.UpdateDocument(path, [new TextChange(new TextSpan(0, 0), "{ c }\n")]);

        Assert.StartsWith("{ c }\nprogram P;", workspace.GetDocument(path).Text.Text, StringComparison.Ordinal);
        Assert.Contains("X := 42;", workspace.GetDocument(path).Text.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void CloseDocument_FallsBackToTheFileSystem()
    {
        var workspace = ProjectWorkspace();
        var path = TestFiles.PathOf("P.pas");
        workspace.OpenDocument(path, "program P; begin Nope := 1 end.");
        Assert.Contains(workspace.GetDocument(path).SemanticModel.GetDiagnostics(), d => d.Code == "E2003");

        workspace.CloseDocument(path);

        Assert.Equal(Program, workspace.GetDocument(path).Text.Text);
        Assert.DoesNotContain(workspace.GetDocument(path).SemanticModel.GetDiagnostics(), d => d.Code == "E2003");
    }

    /// <summary>CMP-200 (C-452): an open include file is what the units that include it read; closed again, the disk is.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1509")]
    public void OpenIncludeText_IsWhatTheIncludingUnitReads()
    {
        var workspace = ProjectWorkspace(TestFiles.Files(("P.pas", "program P;\nvar X: Integer;\nbegin\n  {$I Body.inc}\nend."), ("Body.inc", "X := 1;\n")));
        var include = TestFiles.PathOf("Body.inc");
        Assert.DoesNotContain(workspace.CurrentCompilation.GetDiagnostics(), d => d.Code == "E2003");

        workspace.OpenDocument(include, "Nope := 1;\n");

        Assert.Contains(workspace.CurrentCompilation.GetDiagnostics(), d => d.Code == "E2003");
        workspace.CloseDocument(include);
        Assert.DoesNotContain(workspace.CurrentCompilation.GetDiagnostics(), d => d.Code == "E2003");
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void NotifyFileChangedOnDisk_ReadsTheNewContent()
    {
        var fs = TestFiles.Files(("P.pas", Program));
        var workspace = ProjectWorkspace(fs);
        var path = TestFiles.PathOf("P.pas");
        Assert.Empty(workspace.GetDocument(path).SemanticModel.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        fs.Add(path, "program P; begin Nope := 1 end.");
        workspace.NotifyFileChangedOnDisk(path);

        Assert.Contains(workspace.GetDocument(path).SemanticModel.GetDiagnostics(), d => d.Code == "E2003");
    }

    [Fact]
    public void Changed_IsRaisedForEveryChange()
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", Program)));
        var events = new List<(WorkspaceChangeKind, string?)>();
        workspace.Changed += (_, e) => events.Add((e.Kind, e.Path));
        var path = TestFiles.PathOf("P.pas");

        workspace.SetProject(TestFiles.Options());
        workspace.OpenDocument(path, Program);
        workspace.UpdateDocument(path, Program + " ");
        workspace.NotifyFileChangedOnDisk(path);
        workspace.CloseDocument(path);

        Assert.Equal(
            [(WorkspaceChangeKind.ProjectChanged, null), (WorkspaceChangeKind.DocumentOpened, path), (WorkspaceChangeKind.DocumentChanged, path),
             (WorkspaceChangeKind.FileChangedOnDisk, path), (WorkspaceChangeKind.DocumentClosed, path)],
            events);
    }

    [Fact]
    [Trait("Req", "CMP-701")]
    public void WithoutAProject_TheDocumentIsItsOwnRoot()
    {
        var workspace = new Workspace(TestFiles.Files());
        var path = TestFiles.PathOf("Lone.pas");
        workspace.OpenDocument(path, "unit Lone;\ninterface\nconst K = 1;\nimplementation\nend.");

        var document = workspace.GetDocument(path);

        Assert.Equal(path, document.Compilation.Options.MainFile);
        Assert.Equal("Lone", document.SemanticModel.Unit.Name);
    }

    [Fact]
    [Trait("Req", "CMP-701")]
    public void AFileOutsideTheProject_IsAnAdditionalRoot()
    {
        var workspace = ProjectWorkspace(TestFiles.Files(("P.pas", Program), ("Other.pas", "unit Other;\ninterface\nconst K = 1;\nimplementation\nend.")));

        var document = workspace.GetDocument(TestFiles.PathOf("Other.pas"));

        Assert.Equal("Other", document.SemanticModel.Unit.Name);
        Assert.Equal(TestFiles.PathOf("P.pas"), document.Compilation.Options.MainFile);
    }

    [Fact]
    [Trait("Req", "CMP-700")]
    public void ConcurrentUpdatesAndReads_AreSafe()
    {
        var workspace = ProjectWorkspace();
        var path = TestFiles.PathOf("P.pas");
        workspace.OpenDocument(path, Program);

        Parallel.For(0, 8, i =>
        {
            if (i % 2 == 0)
            {
                workspace.UpdateDocument(path, Program.Replace("X := 1", "X := " + i, StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal("P", workspace.GetDocument(path).SemanticModel.Unit.Name);
            }
        });

        Assert.StartsWith("program P;", workspace.GetDocument(path).Text.Text, StringComparison.Ordinal);
    }
}
