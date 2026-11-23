using System.Reflection;

namespace FreexComp.Debugger.IntegrationTests;

[Trait("Kind", "Debug")]
[Trait("Category", "Integration")]
[Trait("Category", "Toolchain")]
public sealed class IntegrationSmokeTests
{
    [Fact]
    public void Smoke_AssemblyLoads()
    {
        var assembly = Assembly.Load("FreexComp.Debugger");

        Assert.Equal("FreexComp.Debugger", assembly.GetName().Name);
    }

    /// <summary>Integration tests fail, never skip, when the toolchain is missing (testing rules, category Toolchain).</summary>
    [Fact]
    public void Toolchain_DebugAdapter_IsPresent()
    {
        var adapter = Path.Combine(RepositoryRoot(), "third_party", "llvm", "bin", "lldb-dap.exe");

        Assert.True(File.Exists(adapter), $"Debug adapter not found: {adapter} (run scripts/bootstrap.ps1).");
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
