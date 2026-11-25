using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Tests.EndToEnd;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Policy.Tests;
using FreexComp.Tests.Shared;

namespace FreexComp.E2E.Tests;

/// <summary>
/// Phase 8 cross-area checks. L-015 (card F02-004): with "Step Into Library Source" on, Step Into on a call into the
/// runtime library stops in the protected library document; with it off, the call is stepped over (scenario body shared
/// with the IDE area's <c>Phase8LibraryTests</c>). L-012 (card F02-006): a first start with default settings, a project
/// from a template built and run with the real toolchain, then exit — no network use and no first-start dialog.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
[Collection(NetworkProbeCollection.Name)]
public sealed class Phase8Tests
{
    [AvaloniaFact(Timeout = 180000)]
    public Task E2E_Phase8_StepIntoLibrarySource() => LibrarySteppingScenario.StepIntoSysUtilsAsync();

    /// <summary>
    /// The documented per-user files (settings.md §1), relative to the profile folder (<c>roaming</c> = R, <c>local</c> = L).
    /// </summary>
    private static readonly Regex DocumentedUserFile = new(
        @"^(roaming\\(settings|keymap|recent|templates|uistate)\.json|local\\session-noproject\.json|local\\logs\\ide-\d{8}\.log|local\\recovery\\.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [AvaloniaFact(Timeout = 180000)]
    [Trait("Req", "NET-022")]
    [Trait("Req", "NET-023")]
    [Trait("Req", "NET-024")]
    public async Task E2E_Phase8_DefaultSettings_OpenNoConnection()
    {
        using var temp = new TempDirectory();
        using var probe = new NetworkProbe();
        List<(string Relative, string Text)> profile;
        string output;
        string profileDir;
        await using (var h = await HeadlessHarness.StartAsync(o =>
                     {
                         o.Root = temp.Combine("ide");
                         o.ImplementedPhase = null;
                         o.RealBuild = true;
                         o.ComponentMetadata = false; // no test metadata copied into the profile
                         // First start: the welcome page as without a settings file; only the program's output goes to the
                         // Output pane so that the test can read it (a local option, no network).
                         o.Settings.Remove("environment.showWelcomeOnStartup");
                     }))
        {
            profileDir = h.UserDir;
            Assert.StartsWith(temp.Path, profileDir, StringComparison.OrdinalIgnoreCase);

            // (b) No registration or sign-in step: no message box and no dialog window between start and the main window.
            Assert.Empty(h.Messages.Requests);
            Assert.Equal([h.Window], h.AllWindows);

            var toolchain = h.Services.Toolchain.Resolve();
            if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
                Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

            // (c) A project from the console template, built with the real toolchain and run without debugging.
            Assert.Equal(NewProjectOutcome.Created,
                await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", h.TempDir)));
            h.Press("Ctrl+F5");
            string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
            await h.WaitForAsync(() => Output().Contains("Program exited with code", StringComparison.Ordinal) || h.Services.Messages.ErrorCount > 0,
                "build and run finished");
            Assert.True(h.Services.Messages.ErrorCount == 0,
                "Build errors: " + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}")));
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "idle after the run");
            output = Output();

            // Close the project, then exit the IDE.
            Assert.True(await h.Services.Projects.CloseAsync());
            await h.ExecuteAsync("file.exit");
            await h.WaitForAsync(() => !h.Window.IsVisible, "main window closed");
            Assert.Empty(h.Messages.Requests);
            await h.Services.Settings.FlushAsync();
            await h.Services.Keymap.FlushAsync();

            // The profile as the IDE left it (the harness deletes its root at dispose).
            profile = [.. Directory.EnumerateFiles(profileDir, "*", SearchOption.AllDirectories)
                .Select(f => (Path.GetRelativePath(profileDir, f), File.ReadAllText(f)))];
        }

        Assert.Contains("Program exited with code 0 after ", output, StringComparison.Ordinal);
        Assert.Contains("Hello, world", output, StringComparison.Ordinal);

        // (a) No socket, name resolution, HTTP request or TLS handshake in this process while the IDE ran.
        var events = probe.Events;
        Assert.True(events.Count == 0, "Network events: " + string.Join(", ", events.Select(e => e.Source + "/" + e.Event).Distinct()));

        // NET-023: only the documented per-user files, none with licensing wording in its name or content.
        Assert.Contains(profile, f => f.Relative.Equals(@"roaming\settings.json", StringComparison.OrdinalIgnoreCase));
        var unexpected = profile.Where(f => !DocumentedUserFile.IsMatch(f.Relative)).Select(f => f.Relative).ToList();
        Assert.True(unexpected.Count == 0, "Undocumented files in the profile: " + string.Join(", ", unexpected));
        var terms = LicensingTerms.Check(profile.Select(f => (f.Relative, f.Relative + "\n" + f.Text)), "");
        Assert.True(terms.Count == 0, "Licensing wording in the profile: " + string.Join("; ", terms));
    }
}
