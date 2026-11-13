using System.Reflection;

namespace FreexComp.Compiler.Tests;

[Trait("Phase", "1")]
public sealed class AssemblyLoadTests
{
    [Fact]
    public void TargetAssemblyLoads()
    {
        var assembly = Assembly.Load("FreexComp.Compiler");

        Assert.Equal("FreexComp.Compiler", assembly.GetName().Name);
    }
}
