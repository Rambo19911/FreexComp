namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// The golden theory (CMP-901…904, compiler-cli.md §5): one case per test of <c>tests/compiler/**</c>, at <c>-O0</c>
/// and (trait <c>Opt=O2</c>) at <c>-O2</c>. Phase 2 exit check: <c>--filter "Phase=2&amp;Kind=Golden"</c>. CMP-911: one
/// test class (= one xUnit collection, run in parallel with the others) per milestone directory; each class runs only
/// its milestone's cases, and <see cref="GoldenHarnessTests"/> checks that every milestone directory has its class.
/// </summary>
public static class GoldenTheory
{
    /// <summary>
    /// The milestone directories that have a test class below (CMP-900: <c>m2.1</c> … <c>m2.6</c>; <c>dll</c>: dll-output.md §10.2; <c>com</c>: com-interfaces.md §9.2; <c>inline</c>: inline-variables.md §9.2; <c>m8</c>: Phase 8 language additions; <c>res</c>: resource-compiler.md §13, card F09-017; <c>types</c>: type-system cases outside a milestone, first case C-313).
    /// </summary>
    public static readonly IReadOnlyList<string> Milestones = ["com", "dll", "inline", "m2.1", "m2.2", "m2.3", "m2.4", "m2.5", "m2.6", "m8", "res", "types"];

    public static IEnumerable<TheoryDataRow<string, string>> CasesOf(string milestone) =>
        GoldenCorpus.Rows(GoldenCorpus.Discover(GoldenSetup.CorpusDirectory).Where(c => c.Id.StartsWith(milestone + "/", StringComparison.Ordinal)));

    public static async Task RunAsync(string id, string optimization)
    {
        var @case = GoldenCorpus.Discover(GoldenSetup.CorpusDirectory).Single(c => c.Id == id);

        var failure = await GoldenRunner.RunAsync(@case, optimization == "O2", GoldenSetup.Default, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }
}

/// <summary>
/// Card F01-003 — library files and <c>exports</c> (dll-output.md §10.2; CMP-911 collection). A case whose main file is a library
/// file has no run step: it expects diagnostics (<c>.err</c>, or <c>.diag</c> with exit code 0).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenDllTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("dll");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.1 — core (CMP-911 collection).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.1");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.2 — data types (CMP-911 collection).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM22Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.2");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.3 — units (CMP-911 collection; multi-unit cases are folders, CMP-900).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM23Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.3");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.4 — classes and exceptions (CMP-911 collection).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM24Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.4");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.5 — interfaces, routines, statements (CMP-911 collection).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM25Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.5");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Milestone M2.6 — modern features (CMP-911 collection).</summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM26Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m2.6");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Phase 8 language additions outside a card folder (CMP-911 collection; first cases: C-311).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenM8Tests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("m8");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Card F10-016 — inline variable declarations: block lifetime, capture, diagnostics (inline-variables.md §9.2; CMP-911 collection).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenInlineTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("inline");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Card F06-001 — COM-compatible interfaces (com-interfaces.md §9.2; CMP-911 collection).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenComTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("com");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Type-system cases outside a milestone or card folder (CMP-911 collection; first case: C-313).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenTypesTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("types");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}

/// <summary>Card F09-017 — resource scripts and compiled resource files named by <c>{$R}</c> (resource-compiler.md §13; CMP-911 collection).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenResTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenTheory.CasesOf("res");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Golden(string id, string optimization) => GoldenTheory.RunAsync(id, optimization);
}
