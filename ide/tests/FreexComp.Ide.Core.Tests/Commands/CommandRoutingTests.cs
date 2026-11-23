using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>The generic Editor-scope route (I-158): Editor commands reach the active document's command target.</summary>
[Trait("Phase", "1")]
public sealed class CommandRoutingTests
{
    /// <summary>
    /// Every implemented Editor-scope command without an owner of its own is routed to the active document's
    /// <see cref="ICommandTarget"/> (availability, execution and check mark), without a per-command registration.
    /// Kept rules: "Line Endings to CRLF/LF" is disabled when the document already has that kind (menus.md), and the
    /// Word Wrap check mark is the active document's (IDE-ED-040).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-011")]
    [Trait("Req", "IDE-ED-040")]
    public Task EditorScopeCommandsReachActiveDocument() => TestUiThread.RunAsync(() => EditorScopeCommandsReachActiveDocumentOnUiThread()); // I-209

    private async Task EditorScopeCommandsReachActiveDocumentOnUiThread()
    {
        // The route itself: a later editor operation reaches the target with no registration of its own.
        var target = new RecordingTarget("edit.completeCode");
        var keymap = new KeymapService(UserPathsFor(), new InMemoryFileSystem(), new ManualClock(), new RecordingLog());
        keymap.LoadSafeMode();
        var service = new CommandService(keymap, new ImmediateDispatcher(), new ManualClock(), new RecordingLog(), new FakeDialogService(), new RecordingStatusService(), new FocusTracker());
        ICommandTarget? active = null;
        EditorCommandRoute.Register(service, implementedPhase: 3, () => active);
        Assert.True(service.IsHandled("edit.completeCode"));
        Assert.False(service.IsHandled("edit.surroundWith")); // phase 8: left to the later-phase stubs
        Assert.False(service.CanExecute("edit.completeCode")); // no active document
        active = target;
        Assert.True(service.CanExecute("edit.completeCode"));
        Assert.False(service.CanExecute("edit.parameterInfo")); // the target does not handle it
        await service.ExecuteAsync("edit.completeCode", "x");
        Assert.Equal([("edit.completeCode", "x")], target.Executed);
        target.Checked = true;
        Assert.True(service.Get("edit.completeCode").IsChecked);

        // In the composed graph: every P1 Editor-scope command has a handler.
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        var commands = services.Commands;
        var editorCommands = commands.Definitions.Where(d => d.Scope == CommandScope.Editor && d.Phase <= ServiceGraph.ImplementedPhase).ToList();
        Assert.All(editorCommands, d => Assert.True(commands.IsHandled(d.Id), d.Id));
        Assert.False(commands.CanExecute("edit.foldAll"));
        Assert.False(commands.CanExecute("edit.convertLineEndings", "lf"));

        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(@"C:\p\A.pas", Encoding.UTF8.GetBytes("unit A;\r\n\r\nprocedure P;\r\nbegin\r\nend;\r\n\r\nend.\r\n"));
        fs.WriteAllBytes(@"C:\p\b.txt", Encoding.UTF8.GetBytes("a\nb\n"));
        var a = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\p\A.pas")).Value);
        var b = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\p\b.txt")).Value);

        // Line endings: only the other kind is offered, and it converts the active document.
        Assert.Same(b, services.Documents.ActiveDocument);
        Assert.False(commands.CanExecute("edit.convertLineEndings", "lf"));
        Assert.True(commands.CanExecute("edit.convertLineEndings", "crlf"));
        await commands.ExecuteAsync("edit.convertLineEndings", "crlf");
        Assert.Equal(LineEndingKind.CrLf, b.LineEnding.Kind);
        Assert.Equal(LineEndingKind.CrLf, a.LineEnding.Kind);
        Assert.False(commands.CanExecute("edit.convertLineEndings", "crlf"));
        Assert.True(commands.CanExecute("edit.convertLineEndings", "lf"));

        // Folding and editor operations act on the active document only.
        services.Documents.Activate(a);
        Assert.True(commands.CanExecute("edit.foldAll"));
        await commands.ExecuteAsync("edit.foldAll");
        Assert.NotEmpty(a.ViewState.FoldedLines);
        a.ViewState.Caret = new TextPosition(1, 1);
        await commands.ExecuteAsync("edit.duplicateLine");
        Assert.StartsWith("unit A;\r\nunit A;\r\n", a.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal("a\r\nb\r\n", b.Buffer.Text);

        // Word Wrap: the check mark is the active document's state.
        var wrap = commands.Get("view.wordWrap");
        Assert.False(wrap.IsChecked);
        await commands.ExecuteAsync("view.wordWrap");
        Assert.True(a.ViewState.WordWrap);
        Assert.True(wrap.IsChecked);
        services.Documents.Activate(b);
        commands.InvalidateCanExecute();
        Assert.False(wrap.IsChecked);
        services.Documents.Activate(a);
        commands.InvalidateCanExecute();
        Assert.True(wrap.IsChecked);
    }

    private static Core.Settings.UserPaths UserPathsFor() =>
        Core.Settings.UserPaths.Create(@"C:\u", _ => null, _ => throw new InvalidOperationException("Real user folder requested."));

    private sealed class RecordingTarget(string handles) : ICommandTarget
    {
        public List<(string, string?)> Executed { get; } = [];

        public bool Checked { get; set; }

        public bool CanExecute(string commandId, string? argument) => commandId == handles;

        public Task<bool> ExecuteAsync(string commandId, string? argument)
        {
            Executed.Add((commandId, argument));
            return Task.FromResult(true);
        }

        public bool IsChecked(string commandId) => Checked && commandId == handles;
    }
}
