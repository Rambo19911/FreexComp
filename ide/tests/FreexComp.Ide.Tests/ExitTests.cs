using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>The exit sequence (main-window.md UI-WIN-072, IDE-P1-024, IDE-P1-122, IDE-ARCH-075).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ExitTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-072")]
    [Trait("Req", "IDE-P1-024")]
    [Trait("Req", "IDE-P1-122")]
    [Trait("Req", "IDE-ARCH-075")]
    public async Task PromptsThenSavesState()
    {
        // Loose files: a modified document, a recovery copy on disk.
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["notes.txt"] = "notes\r\n";
            o.Settings[SettingsCatalog.EnvironmentRecoveryIntervalSeconds.Name] = 5;
        }))
        {
            var path = Path.Combine(h.TempDir, "notes.txt");
            var session = Path.Combine(h.UserDir, "local", "session-noproject.json");
            var doc = (TextDocumentViewModel)(await h.Services.Documents.OpenAsync(path)).Value!;
            doc.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
            h.Clock.Advance(TimeSpan.FromSeconds(5));
            await h.Services.Recovery.WhenIdle();
            Assert.True(Directory.Exists(h.Services.Recovery.InstanceDirectory));
            Assert.True(h.Services.Commands.CanExecute("file.exit"));

            // Cancel at the unsaved-changes prompt aborts the exit: nothing is saved, the window stays.
            h.Messages.Answer("Msg.File.SaveChangesOne", "Cancel");
            await h.ExecuteAsync("file.exit");
            Assert.Equal(["Msg.File.SaveChangesOne"], h.Messages.Requests.Select(r => r.MessageId));
            Assert.True(h.Window.IsVisible);
            Assert.False(File.Exists(session));
            Assert.True(Directory.Exists(h.Services.Recovery.InstanceDirectory));
            Assert.Contains(doc, h.Services.Documents.Documents);

            // Don't Save: the session and UI state are written, the recovery folder is deleted, then the window closes.
            h.Messages.Answer("Msg.File.SaveChangesOne", "DontSave");
            await h.ExecuteAsync("file.exit");
            await h.WaitForAsync(() => !h.Window.IsVisible, "main window closed");
            Assert.Equal(2, h.Messages.Requests.Count);
            Assert.Equal("xnotes\r\n", doc.Buffer.Text); // not saved
            Assert.Equal("notes\r\n", File.ReadAllText(path));
            using (var json = JsonDocument.Parse(File.ReadAllText(session)))
            {
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("project").ValueKind);
                Assert.Equal([path], json.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("path").GetString()));
            }

            Assert.True(File.Exists(Path.Combine(h.UserDir, "roaming", "uistate.json")));
            Assert.False(Directory.Exists(h.Services.Recovery.InstanceDirectory));
        }

        // An open project stays flagged for reopening (the exit does not close it as the user would) and keeps its session.
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A/A.pproj"] = """{ "formatVersion": 1, "name": "A", "mainSource": "A.pas", "files": [ { "path": "A.pas" } ] }""";
            o.InitialFiles["A/A.pas"] = "program A;\nbegin\nend.\n";
        }))
        {
            var project = Path.Combine(h.TempDir, "A", "A.pproj");
            Assert.True(await h.Services.Projects.OpenAsync(project));
            await h.ExecuteAsync("file.exit"); // nothing modified: no prompt
            await h.WaitForAsync(() => !h.Window.IsVisible, "main window closed");
            Assert.Empty(h.Messages.Requests);
            Assert.Equal(project, h.Services.RecentItems.LastProject?.Path);
            Assert.True(h.Services.RecentItems.Projects.Single(p => p.Path == project).OpenAtExit);
            Assert.Equal(project, h.Services.Projects.Project?.FilePath);
            Assert.True(File.Exists(Path.Combine(h.TempDir, "A", ".freexcomp", "session.json")));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
