using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Tests.Fakes;

/// <summary><see cref="IUiDispatcher"/> that runs posted work inline; the test thread is the UI thread.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action, UiPriority priority = UiPriority.Normal) => action();
}
