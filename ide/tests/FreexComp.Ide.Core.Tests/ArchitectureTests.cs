using System.Reflection;

namespace FreexComp.Ide.Core.Tests;

// IDE-ARCH-001: Core and Designer must not reference any Avalonia, AvaloniaEdit or Dock assembly.
public sealed class ArchitectureTests
{
    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-001")]
    public void CoreHasNoUiReferences()
    {
        Assert.Empty(UiReferences(Assembly.Load("FreexComp.Ide.Core")));
    }

    [Fact]
    [Trait("Phase", "1")]
    [Trait("Req", "IDE-ARCH-001")]
    public void DesignerHasNoUiReferences()
    {
        Assert.Empty(UiReferences(Assembly.Load("FreexComp.Designer")));
    }

    private static List<string> UiReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Avalonia", StringComparison.Ordinal)
                     || n.StartsWith("Dock", StringComparison.Ordinal))
            .ToList();
}
