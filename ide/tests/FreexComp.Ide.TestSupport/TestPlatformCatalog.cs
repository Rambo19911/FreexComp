using FreexComp.Compiler.Targets;
using FreexComp.Ide.Core.Projects;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// The platform catalogue of the platform tests (platform-selector.md IDE-PLAT-001): the product's platforms and a
/// second descriptor <c>test32</c> — the 64-bit descriptor with another id, display name and the predefined symbols of
/// a 32-bit target — so that two installed platforms can be selected before a second real target exists.
/// </summary>
public sealed class TestPlatformCatalog : IPlatformCatalog
{
    public const string SecondId = "test32";

    /// <summary>The second descriptor: <c>WIN32</c>, <c>CPUX86</c>, <c>CPU32BITS</c> in place of the 64-bit symbols.</summary>
    public static TargetInfo Second { get; } = TargetInfo.Win64 with
    {
        Id = SecondId,
        DisplayName = "Test 32-bit",
        PredefinedSymbols = ["MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS"],
    };

    public IReadOnlyList<PlatformDescriptor> Installed { get; } =
    [
        .. BuiltInPlatformCatalog.Instance.Installed,
        new PlatformDescriptor(Second.Id, Second.DisplayName, Second),
    ];

    public PlatformDescriptor? Find(string id) => Installed.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}
