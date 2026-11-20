using System.Reflection;

namespace FreexComp.Designer.Tests;

/// <summary>DSG-ARCH-001: the designer model has no UI and no IDE references.</summary>
public sealed class ArchitectureTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-ARCH-001")]
    public void DesignerHasNoUiReferences()
    {
        var designer = Assembly.Load("FreexComp.Designer");
        var forbidden = ReferenceClosure(designer)
            .Where(n => n.StartsWith("Avalonia", StringComparison.Ordinal)
                     || n.StartsWith("Dock", StringComparison.Ordinal)
                     || n.StartsWith("FreexComp.Ide", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(forbidden.Count == 0, string.Join(Environment.NewLine, forbidden));

        // The only product assembly it may use is the compiler (shared diagnostic type, identifier tables).
        var product = designer.GetReferencedAssemblies().Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("FreexComp.", StringComparison.Ordinal))
            .ToList();
        Assert.All(product, n => Assert.Equal("FreexComp.Compiler", n));
    }

    /// <summary>Names of every assembly reachable from <paramref name="root"/> through product assemblies.</summary>
    private static HashSet<string> ReferenceClosure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([root]);
        while (pending.TryPop(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? "";
                if (!seen.Add(name) || !name.StartsWith("FreexComp.", StringComparison.Ordinal))
                {
                    continue;
                }

                pending.Push(Assembly.Load(reference));
            }
        }

        return seen;
    }
}
