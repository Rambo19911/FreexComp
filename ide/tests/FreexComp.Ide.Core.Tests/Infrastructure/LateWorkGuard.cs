using FreexComp.Ide.TestSupport;

[assembly: AssemblyFixture(typeof(FreexComp.Ide.Core.Tests.Infrastructure.LateWorkGuard))]

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// I-210: fails the run when a test left work behind that was posted to its <see cref="TestUiThread"/> after it ended (a
/// worker result it did not await). That work is never run; the failure names the test and the work so the test can
/// await it.
/// </summary>
public sealed class LateWorkGuard : IDisposable
{
    public void Dispose()
    {
        var late = TestUiThread.TakeAllLatePosts();
        if (late.Count > 0)
            throw new InvalidOperationException("Work posted after its test ended (I-210; await it in the test):\n" + string.Join("\n", late));
    }
}
