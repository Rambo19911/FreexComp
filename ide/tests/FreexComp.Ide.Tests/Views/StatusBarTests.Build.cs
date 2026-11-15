using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
}
