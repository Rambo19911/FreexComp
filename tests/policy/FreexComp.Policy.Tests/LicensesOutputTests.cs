namespace FreexComp.Policy.Tests;

/// <summary>Files that the build puts next to the tools (NET-040).</summary>
[Trait("Phase", "8")]
public sealed class LicensesOutputTests
{
    private static readonly string[] Tools = ["FreexComp.Ide", "opc", "pbuild", "prc"];

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

    private static readonly string[] LicenseFiles = ["LICENSE", "LICENSE-RUNTIME", "NOTICE", "THIRD-PARTY-NOTICES.txt"];

    [Fact]
    [Trait("Req", "LIC-040")]
    [Trait("Req", "LIC-041")]
    public void ToolOutputsContainLicensesFolder()
    {
        foreach (var tool in Tools)
        {
            var output = ProductOutputs.OutputDirectory(tool);
            Assert.True(File.Exists(Path.Combine(output, tool + ".dll")), $"{tool} is not built: build the solution first");
            foreach (var name in LicenseFiles)
            {
                var copy = Path.Combine(output, "licenses", name);
                Assert.True(File.Exists(copy), $"{copy} is missing");
                Assert.Equal(File.ReadAllBytes(RepoPaths.Combine(name)), File.ReadAllBytes(copy));
            }
        }

        // libraries and test projects do not get the folder
        Assert.False(Directory.Exists(Path.Combine(ProductOutputs.OutputDirectory("FreexComp.Compiler"), "licenses")));
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "licenses")));
    }
}
