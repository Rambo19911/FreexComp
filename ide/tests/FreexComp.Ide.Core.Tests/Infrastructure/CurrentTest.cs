using System.Reflection;
using FreexComp.Ide.TestSupport;
using Xunit.v3;

[assembly: FreexComp.Ide.Core.Tests.Infrastructure.RegisterTestDeadline]

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>I-205: registers every test of the assembly with its own timeout for <see cref="TestDeadline"/>.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class RegisterTestDeadlineAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestDeadline.Begin(test.UniqueID, test.Timeout, TestContext.Current.CancellationToken);

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => TestDeadline.End(test.UniqueID);
}

/// <summary>The running test.</summary>
internal static class CurrentTest
{
    /// <summary>The wall time the running test has left before its own timeout (I-205).</summary>
    public static TestDeadline Deadline => TestDeadline.Of(TestContext.Current.Test?.UniqueID, (TestContext.Current.Test as IXunitTest)?.Timeout ?? 0,
        TestContext.Current.CancellationToken);
}
