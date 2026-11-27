using System.Collections.Immutable;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// dll-output.md §10.3 (task C-290): the scenario goldens of card F01-003 under <c>tests/compiler-dll/</c> — each builds a
/// library and a host with <c>opc</c> as a process, runs the host and compares its stdout — at <c>-O0</c> and <c>-O2</c> (CMP-904).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class DllGoldenTests
{
    public static string ScenarioRoot => Path.Combine(GoldenSetup.RepositoryRoot, "tests", "compiler-dll");

    /// <summary>The setup of the compiler's scenarios: this build's <c>opc</c>, the RTL of the golden corpus and the pinned toolchain.</summary>
    internal static DllScenarioSetup Setup { get; } = new(
        GoldenSetup.Default.OpcPath,
        ["--rtl", GoldenSetup.Default.RtlDirectory, "--toolchain", GoldenSetup.Default.ToolchainDirectory]);

    public static IEnumerable<TheoryDataRow<string, string>> Scenarios()
    {
        foreach (var name in DllScenario.Discover(ScenarioRoot).Select(Path.GetFileName))
        {
            yield return new TheoryDataRow<string, string>(name!, "O0") { TestDisplayName = "dll-scenario/" + name };
            var o2 = new TheoryDataRow<string, string>(name!, "O2") { TestDisplayName = "dll-scenario/" + name + " -O2" };
            o2.Traits["Opt"] = ["O2"];
            yield return o2;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Scenario(string name, string optimization)
    {
        // CMP-910: a missing toolchain fails the scenario, it never skips it
        ImmutableArray<string> required =
            [Setup.OpcPath, .. new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" }.Select(t => Path.Combine(GoldenSetup.Default.ToolchainDirectory, t))];
        var missing = required.Where(p => !File.Exists(p)).ToList();
        Assert.True(missing.Count == 0, $"Toolchain missing: {string.Join(", ", missing)} (run scripts/bootstrap.ps1).");

        var failure = await DllScenario.RunAsync(Path.Combine(ScenarioRoot, name), optimization == "O2", Setup, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }

    /// <summary>
    /// The five scenarios of dll-output.md §10.3 and generics.md §14.8.3's <c>ClassCtorLib</c> (C-376) exist (a renamed or deleted
    /// folder must not silently shrink the theory).
    /// </summary>
    [Fact]
    public void Scenarios_AreTheFiveOfTheSpec() =>
        Assert.Equal(["ClassCtorLib", "InitFails", "InitFinal", "MathLib", "OwnRtl", "StaticUnload"], DllScenario.Discover(ScenarioRoot).Select(Path.GetFileName));
}
