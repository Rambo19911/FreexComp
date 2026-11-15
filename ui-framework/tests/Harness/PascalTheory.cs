namespace FreexComp.UiFramework.Tests;

/// <summary>
/// Group A (testing.md §1, UIF-1200, UIF-1209): one row per <c>pascal/&lt;Name&gt;.pas</c>, display name
/// <c>pascal/&lt;Name&gt;</c>, in the theory of its phase (<see cref="UiCorpus.PhaseOf"/>). Programs marked
/// <c>// needs: desktop</c> run in a <c>Category=Foreground</c> theory that calls <see cref="Harness.RequireDesktop"/>.
/// A theory method for another phase or for the desktop is added with its first program (xUnit fails a theory without rows).
/// </summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class PascalTheory
{
    public static IEnumerable<TheoryDataRow<string>> Phase4() => UiCorpus.Rows(4, desktop: false);

    [Theory]
    [Trait("Phase", "4")]
    [MemberData(nameof(Phase4))]
    public Task Pascal_Phase4(string id) => RunAsync(id);

    public static IEnumerable<TheoryDataRow<string>> Phase4Desktop() => UiCorpus.Rows(4, desktop: true);

    /// <summary>Programs that create window handles or need a window station (UIF-1203).</summary>
    [Theory]
    [Trait("Phase", "4")]
    [Trait("Category", "Foreground")]
    [MemberData(nameof(Phase4Desktop))]
    public Task Pascal_Phase4_Desktop(string id)
    {
        Harness.RequireDesktop();
        return RunAsync(id);
    }

    private static async Task RunAsync(string id)
    {
        var failure = await UiCorpus.RunAsync(id, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }
}
