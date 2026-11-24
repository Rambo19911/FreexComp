namespace FreexComp.Policy.Tests;

[Trait("Phase", "8")]
public sealed class PolicyProjectTests
{
    /// <summary>The policy tests read tracked files of the repository: the root is the folder holding the solution.</summary>
    [Fact]
    public void RepositoryRootIsFound()
    {
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "FreexComp.sln")));
        Assert.True(File.Exists(RepoPaths.Combine("LICENSE")));
        Assert.True(Directory.Exists(RepoPaths.Combine("tests", "policy", "FreexComp.Policy.Tests")));
    }
}
