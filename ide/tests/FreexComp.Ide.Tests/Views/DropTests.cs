using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Files dropped from the OS file manager (IDE-P1-031, UI-TW-113).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DropTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-031")]
    [Trait("Req", "UI-TW-113")]
    public async Task DroppedFilesOpen()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["a.txt"] = "a";
            o.InitialFiles["b.pas"] = "unit b;\ninterface\nimplementation\nend.\n";
            o.InitialFiles["c.txt"] = "c";
            o.InitialFiles["P/P.pproj"] = """{ "formatVersion": 1, "name": "P", "mainSource": "P.pas", "files": [ { "path": "P.pas" } ] }""";
            o.InitialFiles["P/P.pas"] = "program P;\nbegin\nend.\n";
            o.InitialFiles["P/extra.txt"] = "extra";
        });
        string Abs(string relative) => Path.Combine(h.TempDir, relative);
        string[] Open() => [.. h.Services.Documents.Documents.Select(d => d.FilePath).OfType<string>()];

        // Files onto the document area open as documents, in drop order; the last one is active.
        await h.DropFilesAsync("DocumentArea", Abs("a.txt"), Abs("b.pas"));
        await h.WaitForAsync(() => Open().Length == 2, "two documents open");
        Assert.Equal([Abs("a.txt"), Abs("b.pas")], Open());
        Assert.Equal(Abs("b.pas"), h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Null(h.Services.Projects.Project);

        // A project file opens the project.
        await h.DropFilesAsync("DocumentArea", Abs(@"P\P.pproj"));
        await h.WaitForAsync(() => h.Services.Projects.Project is not null && Open().Contains(Abs(@"P\P.pas")), "project open");
        Assert.Equal(Abs(@"P\P.pproj"), h.Services.Projects.Project!.FilePath);

        // Onto the document area with a project open: opened, not added.
        await h.DropFilesAsync("DocumentArea", Abs("c.txt"));
        await h.WaitForAsync(() => Open().Contains(Abs("c.txt")), "c.txt open");
        Assert.False(h.Services.Projects.IsMember(Abs("c.txt")));

        // Onto the Project Manager tree: added to the project (project.addExistingFile).
        await h.DropFilesAsync("Tool.ProjectManager.Tree", Abs(@"P\extra.txt"));
        await h.WaitForAsync(() => h.Services.Projects.IsMember(Abs(@"P\extra.txt")), "extra.txt added");
        Assert.Empty(h.Messages.Requests);
    }
}
