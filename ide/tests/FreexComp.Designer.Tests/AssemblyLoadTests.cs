using System.Reflection;

namespace FreexComp.Designer.Tests;

public sealed class AssemblyLoadTests
{
    [Fact]
    [Trait("Phase", "1")]
    public void DesignerAssemblyLoads()
    {
        Assert.Equal("FreexComp.Designer", Assembly.Load("FreexComp.Designer").GetName().Name);
    }
}
