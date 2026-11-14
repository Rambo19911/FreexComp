using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-070: the unit graph of a compilation (in memory, mini RTL <c>System</c>) — the initialisation order of LANG-520 (seen as
/// the link order of <see cref="EmitResult.CompiledUnits"/>: the program, then the units in init order, CMP-625) for the
/// LANG-520 example and further shapes, and interface-uses cycle detection (LANG-513): <c>E2205</c> with the path in
/// canonical names from the cycle's unit first reached from the program, reported in that unit at its uses entry that
/// leads into the cycle.
/// </summary>
[Trait("Phase", "2")]
public sealed class UnitGraphTests
{
    private const string Dir = @"C:\p";

    private static InMemoryFileSystem Files(string program, params (string Name, string Source)[] units)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, "P.pas"), program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var (name, source) in units)
        {
            fs.Add(Path.Combine(Dir, name), source);
        }

        return fs;
    }

    private static EmitResult Run(InMemoryFileSystem fs, EmitStage stage)
    {
        var options = new CompilationOptions { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = stage }, TestContext.Current.CancellationToken);
    }

    /// <summary>The link order (program first, then LANG-520) of a program and its units, which must bind without errors.</summary>
    private static string[] Order(string program, params (string Name, string Source)[] units)
    {
        var result = Run(Files(program, units), EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        return [.. result.CompiledUnits];
    }

    /// <summary>"File(line,col): CODE message" of every error.</summary>
    private static string[] Errors(string program, params (string Name, string Source)[] units) =>
        [.. Run(Files(program, units), EmitStage.Check).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}")];

    private static (string, string) Unit(string name, string interfaceUses = "", string implementationUses = "") =>
        ($"{name}.pas", $"unit {name}; interface {Uses(interfaceUses)} implementation {Uses(implementationUses)} end.");

    private static string Uses(string names) => names.Length == 0 ? "" : $"uses {names};";

    // ------------------------------------------------------------------ initialisation order (LANG-520)

    /// <summary>The LANG-520 example: P uses A, B; A interface-uses C; B implementation-uses A → System, C, A, B.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    public void InitOrder_LanguageExample() =>
        Assert.Equal(["P", "System", "C", "A", "B"],
            Order("program P; uses A, B; begin end.", Unit("A", interfaceUses: "C"), Unit("B", implementationUses: "A"), Unit("C")));

    /// <summary>A chain is initialised from its end: post-order.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    public void InitOrder_Chain_IsPostOrder() =>
        Assert.Equal(["P", "System", "C", "B", "A"],
            Order("program P; uses A; begin end.", Unit("A", interfaceUses: "B"), Unit("B", interfaceUses: "C"), Unit("C")));

    /// <summary>A diamond: the shared unit once, before both users; the users in the program's uses order.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    public void InitOrder_Diamond_SharedUnitOnce() =>
        Assert.Equal(["P", "System", "D", "B", "C"],
            Order("program P; uses B, C; begin end.", Unit("B", interfaceUses: "D"), Unit("C", interfaceUses: "D"), Unit("D")));

    /// <summary>A unit's interface uses are visited before its implementation uses, each left to right.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    public void InitOrder_InterfaceUsesBeforeImplementationUses() =>
        Assert.Equal(["P", "System", "C", "B", "E", "D", "A"],
            Order("program P; uses A; begin end.", Unit("A", interfaceUses: "C, B", implementationUses: "E, D"),
                Unit("B"), Unit("C"), Unit("D"), Unit("E")));

    /// <summary>An implementation-uses cycle: the unit being visited is skipped, so the inner unit comes first.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    [Trait("Req", "LANG-513")]
    public void InitOrder_ImplementationCycle_SkipsTheUnitBeingVisited() =>
        Assert.Equal(["P", "System", "B", "A"],
            Order("program P; uses A, B; begin end.", Unit("A", implementationUses: "B"), Unit("B", implementationUses: "A")));

    /// <summary>A unit already initialised through an earlier entry is not moved by a later one; System stays first even when named.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    [Trait("Req", "LANG-511")]
    public void InitOrder_AlreadyVisitedUnit_KeepsItsPlace_SystemFirst() =>
        Assert.Equal(["P", "System", "B", "A", "C"],
            Order("program P; uses A, System, C; begin end.", Unit("A", interfaceUses: "B"), Unit("B", interfaceUses: "System"),
                Unit("C", interfaceUses: "A, B")));

    // ------------------------------------------------------------------ interface-uses cycles (LANG-513, E2205)

    /// <summary>Two units: reported in the first unit reached from the program, at its entry that opens the cycle.</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void Cycle_OfTwo_IsReportedInTheFirstReachedUnit() =>
        Assert.Equal(["X.pas(1,24): E2205 Circular unit reference: X → Y → X"],
            Errors("program P; uses X; begin end.", Unit("X", interfaceUses: "Y"), Unit("Y", interfaceUses: "X")));

    /// <summary>Three units: the whole path, in traversal order.</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void Cycle_OfThree_ShowsTheWholePath() =>
        Assert.Equal(["A.pas(1,27): E2205 Circular unit reference: A → B → C → A"],
            Errors("program P; uses A; begin end.", Unit("A", interfaceUses: "Z, B"), Unit("B", interfaceUses: "C"), Unit("C", interfaceUses: "A"),
                Unit("Z")));

    /// <summary>The path starts at the cycle's own first-reached unit, not at a unit leading to the cycle.</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void Cycle_BehindAnotherUnit_StartsAtTheCycle() =>
        Assert.Equal(["A.pas(1,24): E2205 Circular unit reference: A → B → A"],
            Errors("program P; uses Q; begin end.", Unit("Q", interfaceUses: "A"), Unit("A", interfaceUses: "B"), Unit("B", interfaceUses: "A")));

    /// <summary>Which unit is "first reached" follows the program's uses order: here B, so B's entry is the one reported.</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void Cycle_FirstReachedFollowsTheProgramUsesOrder() =>
        Assert.Equal(["B.pas(1,24): E2205 Circular unit reference: B → A → B"],
            Errors("program P; uses B, A; begin end.", Unit("A", interfaceUses: "B"), Unit("B", interfaceUses: "A")));

    /// <summary>The path uses canonical names (LANG-539) even when the entries are written differently.</summary>
    [Fact]
    [Trait("Req", "LANG-539")]
    public void Cycle_PathUsesCanonicalNames() =>
        Assert.Equal(["Lib.pas(1,26): E2205 Circular unit reference: Lib → Core → Lib"],
            Errors("program P; uses Lib; begin end.", ("Lib.pas", "unit Lib; interface uses CORE; implementation end."),
                ("Core.pas", "unit Core; interface uses LIB; implementation end.")));

    /// <summary>A cycle through an implementation uses clause is no error (LANG-513).</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void Cycle_ThroughImplementationUses_IsAllowed() =>
        Assert.Empty(Errors("program P; uses A; begin end.", Unit("A", interfaceUses: "B"), Unit("B", implementationUses: "A")));
}
