using System.Reflection;

namespace FreexComp.Build.Tests;

[Trait("Phase", "1")]
public sealed class AssemblyLoadTests
{
    [Fact]
    public void TargetAssemblyLoads()
    {
        var assembly = Assembly.Load("FreexComp.Build");

        Assert.Equal("FreexComp.Build", assembly.GetName().Name);
    }
}
