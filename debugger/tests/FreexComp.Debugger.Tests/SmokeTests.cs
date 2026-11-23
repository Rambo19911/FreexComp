using System.Reflection;

namespace FreexComp.Debugger.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Smoke_AssemblyLoads()
    {
        var assembly = Assembly.Load("FreexComp.Debugger");

        Assert.Equal("FreexComp.Debugger", assembly.GetName().Name);
    }
}
