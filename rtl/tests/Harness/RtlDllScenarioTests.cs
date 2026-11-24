using FreexComp.Tests.Shared;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// dll-runtime.md §4 (task R-227): the run-time scenarios of card F01-003 under <c>rtl/tests/dll/</c>. Each builds a library
/// and a host with <c>opc</c> as a process against <c>rtl/src</c>, runs the host and compares its stdout, at <c>-O0</c> and
/// <c>-O2</c>, through the scenario harness shared with the compiler tests (<c>steps.txt</c> contract of dll-output.md §10.3).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class RtlDllScenarioTests
{
    public static string ScenarioRoot => Path.Combine(RtlSetup.TestsDirectory, "dll");

    /// <summary>This build's <c>opc</c> with the options every RTL harness command line carries (RTL root, toolchain, target: RTL-976).</summary>
    internal static DllScenarioSetup Setup { get; } = new(RtlSetup.Default.OpcPath, RtlSetup.Default.CompilerOptions);

    public static IEnumerable<TheoryDataRow<string, string>> Scenarios()
    {
        foreach (var name in DllScenario.Discover(ScenarioRoot).Select(Path.GetFileName))
        {
            yield return new TheoryDataRow<string, string>(name!, "O0") { TestDisplayName = "dll/" + name };
            var o2 = new TheoryDataRow<string, string>(name!, "O2") { TestDisplayName = "dll/" + name + " -O2" };
            o2.Traits["Opt"] = ["O2"];
            yield return o2;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Scenario(string name, string optimization)
    {
        // RTL-909: a missing toolchain fails the scenario, it never skips it
        string[] required =
            [Setup.OpcPath, .. new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" }.Select(t => Path.Combine(RtlSetup.Default.ToolchainDirectory, t))];
        var missing = required.Where(p => !File.Exists(p)).ToList();
        Assert.True(missing.Count == 0, $"Toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).");

        var failure = await DllScenario.RunAsync(Path.Combine(ScenarioRoot, name), optimization == "O2", Setup, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }

    /// <summary>The six scenarios of dll-runtime.md §4 exist (a renamed or deleted folder must not silently shrink the theory).</summary>
    [Fact]
    public void Scenarios_AreTheSixOfTheSpec() =>
        Assert.Equal(
            ["Dll_FloatState", "Dll_Halt", "Dll_InitFailure", "Dll_LifeCycle", "Dll_OwnHeapBookkeeping", "Dll_Threads"],
            DllScenario.Discover(ScenarioRoot).Select(Path.GetFileName));
}
