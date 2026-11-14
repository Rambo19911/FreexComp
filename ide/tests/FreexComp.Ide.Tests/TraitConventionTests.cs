using System.Reflection;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>
/// docs/process/TESTING.md "Traits": <c>Kind</c> and <c>Category</c> take only the values of the traits table, so
/// <c>--filter "Kind=Ui"</c> / <c>"Category=Integration"</c> find every test of this project (I-175).
/// </summary>
[Trait("Phase", "1")]
public sealed class TraitConventionTests
{
    private static readonly string[] Kinds = ["Unit", "Golden", "Snapshot", "Ui", "Native", "Debug", "E2E"];
    private static readonly string[] Categories = ["Integration", "Toolchain", "Performance", "Foreground", "Spec"];

    private static IEnumerable<(MethodInfo Method, ILookup<string, string> Traits)> TestMethods() =>
        typeof(TraitConventionTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any())
            .Select(m => (m, m.GetCustomAttributes<TraitAttribute>(inherit: true)
                .Concat(m.DeclaringType!.GetCustomAttributes<TraitAttribute>(inherit: true))
                .ToLookup(a => a.Name, a => a.Value, StringComparer.Ordinal)));

    [Fact]
    public void KindAndCategoryUseTestingTableValues()
    {
        var methods = TestMethods().ToList();
        Assert.True(methods.Count > 100, $"only {methods.Count} test methods seen");
        var wrong = methods
            .SelectMany(m => m.Traits["Kind"].Where(v => !Kinds.Contains(v)).Select(v => $"{m.Method.DeclaringType!.Name}.{m.Method.Name}: Kind={v}")
                .Concat(m.Traits["Category"].Where(v => !Categories.Contains(v)).Select(v => $"{m.Method.DeclaringType!.Name}.{m.Method.Name}: Category={v}")))
            .Distinct()
            .ToList();
        Assert.Empty(wrong);
    }

    /// <summary>
    /// IDE-P1-001: Phase 1 needs no LLVM toolchain, <c>opc</c>, <c>pbuild</c> or RTL — no Phase 1 test of the IDE is a
    /// <c>Toolchain</c> test, and no IDE test source names the toolchain folder.
    /// </summary>
    [Fact]
    [Trait("Req", "IDE-P1-001")]
    public void PhaseOneTestsNeedNoToolchain()
    {
        var toolchain = TestMethods()
            .Where(m => m.Traits["Phase"].Contains("1") && m.Traits["Category"].Contains("Toolchain"))
            .Select(m => $"{m.Method.DeclaringType!.Name}.{m.Method.Name}").ToList();
        Assert.Empty(toolchain);

        var testsRoot = Path.Combine(SpecTables.RepoRootPath(), "ide", "tests");
        var naming = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin"))
            .Where(f => Path.GetFileName(f) != nameof(TraitConventionTests) + ".cs")
            .Where(f => File.ReadAllText(f).Contains("third_party", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(naming);
    }

    /// <summary>IDE-TEST-006: at most 30 s per test, 120 s for <c>Toolchain</c> tests (gate item N7).</summary>
    [Fact]
    public void TimeoutsStayWithinTheLimits()
    {
        var over = TestMethods()
            .Select(m => (m, Timeout: m.Method.GetCustomAttributes<FactAttribute>(inherit: true).Single().Timeout))
            .Where(x => x.Timeout > (x.m.Traits["Category"].Contains("Toolchain") ? 120000 : 30000))
            .Select(x => $"{x.m.Method.DeclaringType!.Name}.{x.m.Method.Name}: {x.Timeout} ms").ToList();
        Assert.Empty(over);
    }

    [Fact]
    public void RealWatcherTestIsUiAndIntegration()
    {
        var watcher = TestMethods().Single(m => m.Method.DeclaringType == typeof(Platform.FileChangeTests)
            && m.Method.Name == nameof(Platform.FileChangeTests.RealWatcherDetectsChange));
        Assert.Contains("Ui", watcher.Traits["Kind"]);
        Assert.Contains("Integration", watcher.Traits["Category"]);
    }
}
