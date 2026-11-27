using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>Auto-recovery in the real shell (phase1-shell.md §12): the editor's buffer and the info bar's buttons.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class RecoveryTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50); // IDE-P1-128

    /// <summary>
    /// IDE-P1-128 (F11-017 acceptance 3): the recovery copy of a 10 MB buffer. What the UI thread does for it is O(1)
    /// (no megabytes allocated there: the text is neither materialized nor encoded on it), and a heartbeat on the
    /// dispatcher — a beat before the copy starts, then one after every pass of the dispatcher until the copy is on
    /// disk — never pauses for more than 50 ms. Load from other processes can only lengthen a pause, so the copy is
    /// taken up to three times and the calmest run counts; the allocation limit holds for every run.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-128")]
    public async Task LargeBufferSnapshotDoesNotBlockDispatcher()
    {
        const int size = 10 * 1024 * 1024;
        var text = string.Concat(Enumerable.Repeat(new string('x', 62) + "\r\n", size / 64));
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["big.txt"] = text);
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "big.txt"))).Value);
        Assert.Equal(size, document.Buffer.Length);
        Dispatcher.UIThread.RunJobs();
        var recovery = h.Services.Recovery;
        var copy = Path.Combine(recovery.InstanceDirectory, document.Id.Value.ToString("D") + ".txt");

        var calmest = TimeSpan.MaxValue;
        var edits = 0;
        for (var run = 0; run < 3 && calmest > Limit; run++)
        {
            document.Buffer.ApplyEdits([new TextEdit(0, 0, "y")]);
            edits++;
            // What else follows an edit (status bar, folding, change markers …) is over before the heartbeat starts.
            h.Clock.Advance(TimeSpan.FromMilliseconds(1900));
            Dispatcher.UIThread.RunJobs();
            await recovery.WhenIdle();
            var writtenBefore = File.Exists(copy) ? File.GetLastWriteTimeUtc(copy) : (DateTime?)null;

            var watch = Stopwatch.StartNew();
            var (last, longest) = (TimeSpan.Zero, TimeSpan.Zero);
            void Beat()
            {
                var now = watch.Elapsed;
                if (now - last > longest)
                    longest = now - last;
                last = now;
            }

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            h.Clock.Advance(TimeSpan.FromMilliseconds(100)); // two seconds after the edit: the idle copy is taken (IDE-P1-126)
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Beat();
            var written = recovery.WhenIdle();
            while (!written.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
                Beat();
            }

            Assert.True(File.Exists(copy), "the idle copy of the large buffer");
            Assert.NotEqual(writtenBefore, File.GetLastWriteTimeUtc(copy));
            Assert.True(allocated < 1024 * 1024, $"{allocated:N0} bytes allocated on the UI thread for the copy of a {size:N0} character buffer");
            if (longest < calmest)
                calmest = longest;
        }

        Assert.True(calmest <= Limit, $"the dispatcher paused for {calmest.TotalMilliseconds:F1} ms during the recovery copy (limit {Limit.TotalMilliseconds:F0} ms)");

        // The copy is the whole buffer: UTF-8 with BOM.
        var bytes = await File.ReadAllBytesAsync(copy);
        Assert.Equal(3 + size + edits, bytes.Length);
        Assert.Equal(
            SHA256.HashData([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(new string('y', edits) + text)]),
            SHA256.HashData(bytes));
        Assert.Equal(document.Buffer.Text, new string('y', edits) + text);
    }

    /// <summary>
    /// IDE-P1-125 / IDE-P1-127 / IDE-P1-129 in the window: the bar of a recovered file that changed on disk is a warning
    /// with Keep, Show Differences, Discard; Show Differences opens the diff tab and leaves the bar; Discard removes the
    /// bar and every copy.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-125")]
    [Trait("Req", "IDE-P1-127")]
    [Trait("Req", "IDE-P1-129")]
    public async Task RecoveredBarShowsDifferencesAndDiscards()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "work", "Unit1.pas");
        var saved = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        const string disk = "unit Unit1;\r\ninterface\r\nimplementation\r\nend.\r\n";
        const string recovered = "unit Unit1;\r\ninterface\r\nprocedure Mine;\r\nimplementation\r\nend.\r\n";
        var orphan = Path.Combine("local", "recovery", "dead-instance");
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Root = root;
            o.InitialFiles["Unit1.pas"] = disk;
            o.UserFiles[Path.Combine(orphan, "owner.json")] =
                JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, processId = int.MaxValue, startTimeTicks = 1L });
            o.UserFiles[Path.Combine(orphan, "0f8fad5b-d9cb-469f-a165-70867728950e.txt")] = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(recovered)];
            o.UserFiles[Path.Combine(orphan, "0f8fad5b-d9cb-469f-a165-70867728950e.json")] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                formatVersion = 1, path, title = "Unit1.pas", encoding = "utf-8", lineEnding = "crlf", savedUtc = saved,
                baseTimestampUtc = (DateTime?)null,
                baseSha256 = Convert.ToHexStringLower(SHA256.HashData("what the edits were based on"u8)),
            });
        });
        await h.Startup;
        await h.WaitForAsync(() => h.Services.Documents.Documents.Any(d => d.Title == "Unit1.pas"), "recovered copy opened");
        var document = h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single(d => d.Title == "Unit1.pas");
        await h.Services.Recovery.WhenIdle();
        Dispatcher.UIThread.RunJobs();

        var when = saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_RecoveredDiskChanged, when), h.Find<TextBlock>("InfoBar.Recovered.Text").Text);
        Assert.Equal(InfoBarSeverity.Warning, document.InfoBars.Single(b => b.Kind == "Recovered").Severity);
        var buttons = h.FindAll("InfoBar.Recovered.Action.").OfType<Button>().Where(b => b.IsEffectivelyVisible)
            .OrderBy(b => b.TranslatePoint(default, h.Window)!.Value.X).ToList();
        Assert.Equal(
            ["InfoBar.Recovered.Action.KeepRecovered", "InfoBar.Recovered.Action.ShowDifferences", "InfoBar.Recovered.Action.DiscardAndReload"],
            buttons.Select(AutomationProperties.GetAutomationId));
        Assert.Equal(["Keep Recovered Version", "Show Differences", "Discard and Reload from Disk"], buttons.Select(b => b.Content));

        h.Click("InfoBar.Recovered.Action.ShowDifferences");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is DiffDocumentViewModel, "the diff tab");
        Dispatcher.UIThread.RunJobs();
        var diff = (DiffDocumentViewModel)h.Services.Documents.ActiveDocument!;
        Assert.Equal("Unit1.pas (on disk) ↔ Unit1.pas (recovered)", diff.Title);
        Assert.NotNull(h.Find<Control>(AutomationIds.DocumentTab(diff.Title)));
        Assert.Equal("Unit1.pas (on disk)", h.Find<TextBlock>("Diff.LeftLabel").Text);
        Assert.Equal("Unit1.pas (recovered)", h.Find<TextBlock>("Diff.RightLabel").Text);
        Assert.Equal("1 change", h.Find<TextBlock>("Diff.Changes").Text);

        // The bar stays with its document; Discard removes it and every copy.
        h.Services.Documents.Activate(document);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>("InfoBar.Recovered").IsEffectivelyVisible);
        Assert.True(document.IsModified);
        h.Click("InfoBar.Recovered.Action.DiscardAndReload");
        await h.WaitForAsync(() => !document.IsModified, "reloaded from disk");
        await h.Services.Recovery.WhenIdle();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(disk, document.Buffer.Text);
        Assert.Null(h.TryFind<Control>("InfoBar.Recovered"));
        Assert.False(Directory.Exists(Path.Combine(h.UserDir, orphan)));
        Assert.Empty(Directory.GetFiles(h.Services.Recovery.InstanceDirectory, document.Id.Value.ToString("D") + ".*"));
    }
}
