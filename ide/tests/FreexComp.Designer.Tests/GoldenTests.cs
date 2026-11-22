namespace FreexComp.Designer.Tests;

/// <summary>The golden-file helper itself (testing.md DSG-TEST-011; D-101).</summary>
public sealed class GoldenTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TEST-011")]
    public void UpdateModeFailsUnderCi()
    {
        // The environment is injected: changing the real process environment would race with other golden tests.
        var golden = TestPaths.Combine("forms/canonical/hello.form");
        var before = File.ReadAllText(golden);
        var environment = new Dictionary<string, string> { ["FXC_UPDATE_GOLDENS"] = "1", ["CI"] = "true" };

        var failure = Assert.ThrowsAny<Exception>(() => Golden.Check(golden, "something else\n", environment.GetValueOrDefault));

        Assert.Contains("FXC_UPDATE_GOLDENS", failure.Message, StringComparison.Ordinal);
        Assert.Contains("CI", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(golden));

        // Without CI the same call is an ordinary comparison (no update variable) and passes on equal text.
        Golden.Check(golden, before, new Dictionary<string, string>().GetValueOrDefault);
    }
}
