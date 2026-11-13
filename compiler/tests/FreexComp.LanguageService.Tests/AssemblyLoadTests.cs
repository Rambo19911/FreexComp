using System.Reflection;

namespace FreexComp.LanguageService.Tests;

[Trait("Phase", "1")]
public sealed class AssemblyLoadTests
{
    [Fact]
    public void TargetAssemblyLoads()
    {
        var assembly = Assembly.Load("FreexComp.LanguageService");

        Assert.Equal("FreexComp.LanguageService", assembly.GetName().Name);
    }
}
