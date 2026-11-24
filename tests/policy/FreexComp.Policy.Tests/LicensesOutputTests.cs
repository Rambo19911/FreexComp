namespace FreexComp.Policy.Tests;

/// <summary>Files that the build puts next to the tools (NET-040).</summary>
[Trait("Phase", "8")]
public sealed class LicensesOutputTests
{
    private static readonly string[] Tools = ["FreexComp.Ide", "opc", "pbuild"];

    [Fact]
    [Trait("Req", "NET-040")]
    public void ToolOutputsContainNetworkPolicy()
    {
        var source = File.ReadAllBytes(RepoPaths.Combine("NETWORK-POLICY.md"));
        foreach (var tool in Tools)
        {
            var output = ProductOutputs.OutputDirectory(tool);
            Assert.True(File.Exists(Path.Combine(output, tool + ".dll")), $"{tool} is not built: build the solution first");
            var copy = Path.Combine(output, "NETWORK-POLICY.md");
            Assert.True(File.Exists(copy), $"{copy} is missing");
            Assert.Equal(source, File.ReadAllBytes(copy));
        }

        // libraries and test projects do not get the page
        Assert.False(File.Exists(Path.Combine(ProductOutputs.OutputDirectory("FreexComp.Compiler"), "NETWORK-POLICY.md")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "NETWORK-POLICY.md")));
    }
}
