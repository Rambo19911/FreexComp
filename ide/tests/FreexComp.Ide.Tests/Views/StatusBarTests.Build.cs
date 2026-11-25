using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class StatusBarTests
{
    /// <summary>IDE-P3-005, IDE-P3-040, R1: the state text and colour follow the build; the result stays; counts appear.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-005")]
    [Trait("Req", "IDE-P3-040")]
    [Trait("Req", "UI-WIN-040")]
    public async Task StateAndDiagnosticsFollowBuild()
    {
        await using var h = await BuildScenario.StartAsync();
        Assert.Null(h.Status.Field("Diagnostics"));
        var unit = BuildScenario.UnitPath(h);
        var held = h.Build.Enqueue(FakeBuildEngine.Failure(BuildScenario.ProjectPath(h),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", unit, 6, 3, 3),
            FakeBuildEngine.Diag("W1002", DiagnosticSeverity.Warning, "Variable 'Foo1' is declared but never used", unit, 4, 5)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_State_Building, "Hello", "Debug"), h.Status.State);
        var view = h.Find<Control>("StatusBar");
        Assert.Equal(StatusBarState.Building, ((StatusBarViewModel)view.DataContext!).StateKind);

        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Build_FailedOne, h.Status.State); // the result stays in L1
        Assert.Equal("⊗ 1 ⚠ 1", h.Status.Field("Diagnostics"));
        Assert.Equal(StatusBarState.Idle, ((StatusBarViewModel)view.DataContext!).StateKind);

        // messages.clear hides R1; the next state change replaces the result text.
        await h.ExecuteAsync("messages.clear");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.Status.Field("Diagnostics"));
        h.Press("Ctrl+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running, "running");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_State_Running, "Hello.exe"), h.Status.State);
        Assert.Equal(StatusBarState.Running, ((StatusBarViewModel)view.DataContext!).StateKind);
        await h.ExecuteAsync("run.stop");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "stopped");
    }

    /// <summary>
    /// I-296, main-window.md §5 UI-WIN-045: at the 800 px minimum width a long L1 text (here the Building text of a project with a
    /// long name, as a group-build result can be) shrinks with an ellipsis before the right group is cut: every shown
    /// field of the right group, R9 Configuration last, is drawn completely inside the status bar.
    /// </summary>
    /// <summary>
    /// I-299, UI-WIN-045, UI-WIN-042: screen readers and UI Automation reach the status-bar fields in their visual order —
    /// L1 first, then L3, then the right group from left to right — also while the right group is laid out first so that
    /// it is never cut.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-045")]
    [Trait("Req", "UI-WIN-042")]
    public async Task AutomationOrderIsTheVisualOrder()
    {
        await using var h = await BuildScenario.StartAsync();
        Assert.True((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Success);
        VisualSnapshot.Arrange(h.Window, new Size(800, 600), 1.0);
        var bar = h.Find<Control>("StatusBar");

        // The fields as the automation tree gives them, depth first.
        var inTree = new List<string>();
        void Walk(AutomationPeer peer)
        {
            if (peer.GetAutomationId() is { } id && id.StartsWith("StatusBar.", StringComparison.Ordinal) && !inTree.Contains(id))
                inTree.Add(id);
            foreach (var child in peer.GetChildren())
                Walk(child);
        }

        Walk(ControlAutomationPeer.CreatePeerForElement(bar));

        // The shown fields from left to right.
        var shown = bar.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && AutomationProperties.GetAutomationId(c) is { } id && id.StartsWith("StatusBar.", StringComparison.Ordinal))
            .Where(c => c is not TextBlock || c.Parent is not Button) // a field button's own text has no id; ids are on the fields
            .OrderBy(c => c.TranslatePoint(default, bar)!.Value.X)
            .Select(c => AutomationProperties.GetAutomationId(c)!)
            .ToList();
        Assert.Equal("StatusBar.State", inTree[0]);
        Assert.Contains("StatusBar.Configuration", shown);
        Assert.Equal(shown, inTree.Where(shown.Contains));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-045")]
    public async Task LongStateText_ShrinksBeforeTheRightGroupIsCut()
    {
        const string LongName = "AnApplicationWithAVeryLongProjectNameThatFillsTheStateField";
        await using var h = await BuildScenario.StartAsync(o => o.InitialFiles[@"Hello\Hello.pproj"] = $$"""
            { "formatVersion": 1, "name": "{{LongName}}", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ] }
            """);
        Assert.True((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Success);
        VisualSnapshot.Arrange(h.Window, new Avalonia.Size(800, 600), 1.0);
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(LongName, h.Status.State, StringComparison.Ordinal);

        var bar = h.Find<Control>("StatusBar");
        var state = Assert.IsType<TextBlock>(h.Find<Button>("StatusBar.State").Content);
        Assert.True(state.TextLayout.TextLines[0].HasCollapsed, "the long state text ends in an ellipsis");
        string[] right = ["Diagnostics", "CaretPosition", "Selection", "InsertMode", "Indent", "LineEnding", "Encoding", "Language", "Configuration"];
        Assert.True(h.Find<Control>("StatusBar.Configuration").IsEffectivelyVisible, "R9 is shown");
        foreach (var name in right)
        {
            if (h.TryFind<Control>("StatusBar." + name) is not { IsEffectivelyVisible: true } field)
                continue;
            var box = new Avalonia.Rect(field.TranslatePoint(default, bar)!.Value, field.Bounds.Size);
            Assert.True(box.Right <= bar.Bounds.Width + 0.5 && box.X >= state.TranslatePoint(new Avalonia.Point(state.Bounds.Width, 0), bar)!.Value.X,
                $"StatusBar.{name} {box} is cut (status bar {bar.Bounds.Width} wide, L1 {state.Bounds.Width} wide)");
            Assert.Null(VisualSnapshot.HiddenPart(field, h.Window));
        }

        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
    }

    /// <summary>
    /// UI-WIN-041: the status-bar background is the <c>statusbar.&lt;state&gt;.bg</c> token of the IDE state, switched
    /// immediately. UI-A11Y-023: the state field is a polite live region whose accessible name is the state text, so each
    /// state change raises a name change on the live element.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-041")]
    [Trait("Req", "UI-A11Y-023")]
    public async Task BackgroundAndLiveRegionFollowState()
    {
        await using var h = await BuildScenario.StartAsync();
        var view = h.Find<Control>("StatusBar");
        var state = h.Find<Button>(AutomationIds.StatusBarField("State"));
        IBrush Token(string name) => Assert.IsAssignableFrom<IBrush>(view.TryFindResource(name, view.ActualThemeVariant, out var brush) ? brush : null);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(state));
        var peer = ControlAutomationPeer.CreatePeerForElement(state);
        var names = new List<string?>();
        peer.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty)
                names.Add(e.NewValue as string);
        };
        Assert.Same(Token("FxcStatusbarIdleBgBrush"), ((Panel)view).Background);

        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Token("FxcStatusbarBuildingBgBrush"), ((Panel)view).Background);
        Assert.Equal(h.Status.State, peer.GetName());
        Assert.Contains(h.Status.State, names);

        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Token("FxcStatusbarIdleBgBrush"), ((Panel)view).Background);
        Assert.Equal(h.Status.State, peer.GetName());
        Assert.Contains(h.Status.State, names);

        h.Press("Ctrl+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running, "running");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Token("FxcStatusbarRunningBgBrush"), ((Panel)view).Background);
        await h.ExecuteAsync("run.stop");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "stopped");
    }
}
