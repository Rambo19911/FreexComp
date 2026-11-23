using System.Reflection;
using FreexComp.Ide.TestSupport;
using Xunit.v3;

[assembly: FreexComp.Ide.Core.Tests.Infrastructure.RegisterTestDeadline]

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// I-205: registers every test of the assembly with its own timeout for <see cref="TestDeadline"/>; I-210: and as the owner
/// of the background work registered while it runs (<see cref="TestUiThread.RegisterWork"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class RegisterTestDeadlineAttribute : BeforeAfterTestAttribute
{
    // I-210: set before any test class is constructed (a composition built in a constructor registers its work).
    public RegisterTestDeadlineAttribute() => TestUiThread.CurrentTest = static () => TestContext.Current.Test?.UniqueID;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        TestDeadline.Begin(test.UniqueID, test.Timeout, TestContext.Current.CancellationToken);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        TestDeadline.End(test.UniqueID);
        TestUiThread.EndTest(test.UniqueID);
        ThrowIfLateWork(test.TestDisplayName, TestUiThread.TakeLateWork(test.UniqueID));
    }

    /// <summary>
    /// I-221: fails the ending test when work was posted to a test UI thread after its test ended (I-210): the message names
    /// that test and the work, so it reaches the test output (an assembly fixture's failure shows only its exception type).
    /// </summary>
    internal static void ThrowIfLateWork(string testName, IReadOnlyList<string> late)
    {
        if (late.Count > 0)
            throw new InvalidOperationException(
                $"{testName}: work posted to a test UI thread after its test ended (I-210; await it in the test named first on its line):\n" + string.Join("\n", late));
    }
}

/// <summary>The running test.</summary>
internal static class CurrentTest
{
    /// <summary>The wall time the running test has left before its own timeout (I-205).</summary>
    public static TestDeadline Deadline => TestDeadline.Of(TestContext.Current.Test?.UniqueID, (TestContext.Current.Test as IXunitTest)?.Timeout ?? 0,
        TestContext.Current.CancellationToken);
}
