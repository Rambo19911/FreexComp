using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary><c>project.checkSyntax</c> over the project <c>Compilation</c> on the <c>CodeModelWorker</c> (I-110, IDE-P3-055).</summary>
[Trait("Phase", "3")]
public sealed class CheckSyntaxTests
{
    /// <summary>The real RTL sources (System.pas and its include files) — Check Syntax needs no LLVM.</summary>
    private static readonly Lazy<(string Name, string Text)[]> Rtl = new(() =>
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        return [.. Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json"))
            .Where(File.Exists).Select(p => (Path.GetFileName(p), File.ReadAllText(p)))];
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-055")]
    [Trait("Req", "IDE-ARCH-087")]
    public async Task UsesUnsavedBuffersAndWritesNothing()
    {
        RecordingFileSystem? recording = null;
        var bed = new BuildTestBed(o => o.FileSystem = recording = new RecordingFileSystem(o.FileSystem));
        foreach (var (name, text) in Rtl.Value)
            bed.File(@"G:\rtl\" + name, text);
        // No LLVM at all: Check Syntax only needs the RTL sources.
        bed.Services.Settings.Set(Core.Settings.SettingsCatalog.ToolchainLlvmBinDir, @"G:\none");
        await bed.OpenHelloAsync();
        var commands = bed.Services.Commands;
        Assert.True(commands.CanExecute("project.checkSyntax"));

        // An unsaved edit with a semantic error: undeclared identifier on line 4 of Unit1.pas.
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var insert = "unit Unit1;\ninterface\nimplementation\n".Length;
        const string Bad = "procedure P; begin Undeclared1 := 1; end;\n";
        BuildTestBed.Type(unit, Bad, insert);
        var diskBefore = bed.Text(BuildTestBed.UnitPath);
        var writesBefore = Writes(recording!).Count;

        var states = new List<IdeStateInfo>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current);
        await commands.ExecuteAsync("project.checkSyntax");

        Assert.Contains(states, s => s is { State: IdeState.Building, BuildKind: BuildKind.CheckSyntax });
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
        var errors = bed.Services.Messages.Items.Where(i => i is { IsSummary: false, Severity: MessageSeverity.Error }).ToList();
        var error = Assert.Single(errors);
        Assert.Equal((BuildTestBed.UnitPath, 4), (error.Location!.FilePath, error.Location.Line));
        Assert.Contains("Undeclared1", error.Text, StringComparison.Ordinal);
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Messages.Items.Last(i => i.IsSummary).Text);
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Build.ResultText);

        // Nothing was saved or written: the buffer is still modified, the disk unchanged, no write call at all.
        Assert.True(unit.IsModified);
        Assert.Equal(diskBefore, bed.Text(BuildTestBed.UnitPath));
        Assert.Equal(writesBefore, Writes(recording!).Count);
        Assert.Empty(bed.Engine.Requests);

        // The fixed buffer is checked again (the compilation is reused with the new override).
        unit.Buffer.ApplyEdits([new TextEdit(insert, Bad.Length, "")]);
        await commands.ExecuteAsync("project.checkSyntax");
        Assert.DoesNotContain(bed.Services.Messages.Items, i => i is { IsSummary: false, Severity: MessageSeverity.Error });
        Assert.Equal(Strings.Msg_Build_SyntaxOk, bed.Services.Messages.Items.Last(i => i.IsSummary).Text);
        Assert.Equal(writesBefore, Writes(recording!).Count);
    }

    private static List<string> Writes(RecordingFileSystem fs) =>
        [.. fs.Calls.Where(c => c.StartsWith("Write", StringComparison.Ordinal) || c.StartsWith("Overwrite", StringComparison.Ordinal)
            || c.StartsWith("Create", StringComparison.Ordinal) || c.StartsWith("Delete", StringComparison.Ordinal)
            || c.StartsWith("Move", StringComparison.Ordinal) || c.StartsWith("Replace", StringComparison.Ordinal))
            .Where(c => c.Contains(@"C:\p\", StringComparison.OrdinalIgnoreCase) || c.Contains(@"G:\", StringComparison.OrdinalIgnoreCase))];
}
