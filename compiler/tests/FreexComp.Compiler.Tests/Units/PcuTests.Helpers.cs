using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

// CMP-1490 (c), (e): the mutating classification of record-helper methods in the .pcu (phase8/helpers-completion.md §8), C-443.
public sealed partial class PcuTests
{
    private const string MutatingHelperUnit = """
        unit U;
        interface
        type
          TPoint = record
            X, Y: Integer;
          end;
          TThing = class
            FSize: Integer;
          end;
          TIntH = record helper for Integer
            function Squared: Integer;
            procedure Chain;
            procedure Bump;
            function Take: Integer;
            class function Zero: Integer; static;
            property Next: Integer read Take;
          end;
          TPointH = record helper for TPoint
            function Sum: Integer;
            procedure Offset(DX, DY: Integer);
          end;
          TThingH = class helper for TThing
            procedure Grow(N: Integer);
          end;
        implementation
        function TIntH.Squared: Integer;
        begin
          Result := Self * Self;
        end;
        procedure TIntH.Chain;
        begin
          Bump;
        end;
        procedure TIntH.Bump;
        begin
          Inc(Self);
        end;
        function TIntH.Take: Integer;
        begin
          Result := Self;
          Chain;
        end;
        class function TIntH.Zero: Integer;
        begin
          Result := 0;
        end;
        function TPointH.Sum: Integer;
        begin
          Result := X + Y;
        end;
        procedure TPointH.Offset(DX, DY: Integer);
        begin
          X := X + DX;
          Y := Y + DY;
        end;
        procedure TThingH.Grow(N: Integer);
        begin
          Inc(FSize, N);
        end;
        end.
        """;

    private const string MutatingHelperProgram = """
        program P;
        uses U;
        procedure Use(const N: Integer; const P: TPoint; T: TThing);
        var
          L: Integer;
        begin
          L := N.Squared + P.Sum + Integer.Zero;
          N.Bump;
          N.Chain;
          L := L + N.Next;
          P.Offset(1, 2);
          T.Grow(L);
          L.Bump;
        end;
        begin
        end.
        """;

    private static readonly string[] MutatingHelperErrors =
    [
        "P.pas(8,5): E2752 Cannot call 'Bump' on a read-only value: the method changes Self",
        "P.pas(9,5): E2752 Cannot call 'Chain' on a read-only value: the method changes Self",
        "P.pas(10,14): E2752 Cannot call 'Take' on a read-only value: the method changes Self",
        "P.pas(11,5): E2752 Cannot call 'Offset' on a read-only value: the method changes Self",
    ];

    private static string[] ErrorLines(EmitResult result) =>
    [
        .. result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"),
    ];

    private static EmitResult CheckAgainstPcu(string pcu, string program)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), pcu).Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");       // U.pcu only: no U.pas
        return Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs)
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
    }

    /// <summary>The <c>flags</c> of every method of the helper <paramref name="helper"/> in a <c>.pcu</c>, by method name.</summary>
    private static Dictionary<string, string[]> HelperFlags(JsonObject pcu, string helper) =>
        ((JsonArray)HelperEntry(pcu, helper)["methods"]!).ToDictionary(m => (string)m!["name"]!, m => ((JsonArray)m!["flags"]!).Select(f => (string)f!).ToArray());

    private static JsonObject HelperEntry(JsonObject pcu, string helper) =>
        (JsonObject)((JsonArray)pcu["interface"]!["types"]!).Single(t => (string?)t!["name"] == helper)!;

    /// <summary>
    /// CMP-1490 (c), (e): every instance method of a record helper that is mutating (LANG-996 (b)) carries the flag
    /// <c>mutating</c> in the <c>.pcu</c> and the record-helper entry the key <c>classified</c>; a class helper has neither. A
    /// program gets the same E2752 list (code, text, position) against the <c>.pcu</c> as against the source. The flag is part of
    /// the interface hash: a body edit that turns a method mutating changes it, another body edit does not. An entry written
    /// without <c>classified</c>: with the unit's source present its <c>.pcu</c> is out of date (the unit is compiled again);
    /// for a <c>.pcu</c>-only unit every instance method of the helper counts as mutating — and written again, that
    /// <c>.pcu</c> keeps its interface hash.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1490")]
    [Trait("Req", "LANG-996")]
    public void HelperMutatingFlagFromPcu_SameDiagnostics()
    {
        var sources = Files(u: MutatingHelperUnit, program: MutatingHelperProgram);
        var fromSource = Compilation.Create(Options(), [], sources).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.Equal(MutatingHelperErrors, ErrorLines(fromSource));

        // the .pcu: flags and key
        const string Empty = "program P;\nuses U;\nbegin\nend.\n";
        var pcu = Pcu("U", Files(u: MutatingHelperUnit, program: Empty));
        var flags = HelperFlags(pcu, "TIntH");
        Assert.Equal(["Bump", "Chain", "Take"], flags.Where(f => f.Value.Contains("mutating")).Select(f => f.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["Offset"], HelperFlags(pcu, "TPointH").Where(f => f.Value.Contains("mutating")).Select(f => f.Key));
        Assert.Equal(["class"], flags["Zero"]);
        Assert.True((bool)HelperEntry(pcu, "TIntH")["classified"]!);
        Assert.True((bool)HelperEntry(pcu, "TPointH")["classified"]!);
        Assert.False(HelperEntry(pcu, "TThingH").ContainsKey("classified"));
        Assert.DoesNotContain("mutating", HelperFlags(pcu, "TThingH")["Grow"]);

        // the same diagnostics against the .pcu
        Assert.Equal(MutatingHelperErrors, ErrorLines(CheckAgainstPcu(pcu.ToJsonString() + "\n", MutatingHelperProgram)));

        // the interface hash follows the classification, not the bodies
        var hash = (string)pcu["interfaceHash"]!;
        var sameClasses = MutatingHelperUnit.Replace("Result := Self * Self;", "Result := Self * Self * 1;", StringComparison.Ordinal);
        var turnedMutating = MutatingHelperUnit.Replace("Result := Self * Self;", "Result := Self * Self;\n  Bump;", StringComparison.Ordinal);
        Assert.Equal(hash, Hash("U", Files(u: sameClasses, program: Empty)));
        Assert.NotEqual(hash, Hash("U", Files(u: turnedMutating, program: Empty)));
        Assert.Contains("mutating", HelperFlags(Pcu("U", Files(u: turnedMutating, program: Empty)), "TIntH")["Squared"]);

        // an entry written without `classified` (and so without the flags)
        var old = (JsonObject)pcu.DeepClone();
        foreach (var helper in new[] { "TIntH", "TPointH" })
        {
            Assert.True(HelperEntry(old, helper).Remove("classified"));
            foreach (var method in ((JsonArray)HelperEntry(old, helper)["methods"]!).OfType<JsonObject>())
            {
                method["flags"] = new JsonArray([.. ((JsonArray)method["flags"]!).Select(f => (string)f!).Where(f => f != "mutating").Select(f => (JsonNode?)f)]);
            }
        }

        // … for a .pcu-only unit: every instance method of the record helpers is mutating; static and class-helper methods are not
        string[] expectedOld =
        [
            "P.pas(7,10): E2752 Cannot call 'Squared' on a read-only value: the method changes Self",
            "P.pas(7,22): E2752 Cannot call 'Sum' on a read-only value: the method changes Self",
            .. MutatingHelperErrors,
        ];
        Assert.Equal(expectedOld, ErrorLines(CheckAgainstPcu(old.ToJsonString() + "\n", MutatingHelperProgram)));

        // … read and written again it is the same entry — no key, no flag — so its interface hash is the one its users recorded
        var oldFs = new InMemoryFileSystem().Add(@"C:\p\P.pas", Empty).AddSystem()
            .Add(Path.Combine(PcuImportTests.Lib, "U.pcu"), old.ToJsonString() + "\n").Add(Path.Combine(PcuImportTests.Lib, "U.obj"), "");
        var rewritten = (JsonObject)JsonNode.Parse(Pcus(oldFs, Options() with { SearchPaths = [PcuImportTests.Lib] })["U"])!;
        Assert.False(HelperEntry(rewritten, "TIntH").ContainsKey("classified"));
        Assert.DoesNotContain("mutating", HelperFlags(rewritten, "TIntH")["Bump"]);
        Assert.DoesNotContain("mutating", HelperFlags(rewritten, "TPointH")["Offset"]);

        // … with the source present: the cached .pcu is out of date, the unit is compiled again (nothing else is)
        var cached = Files(u: MutatingHelperUnit, program: Empty);
        foreach (var (unit, text) in Pcus(cached))
        {
            cached.Add(Path.Combine(@"C:\p\obj", unit + ".pcu"), text).Add(Path.Combine(@"C:\p\obj", unit + ".obj"), "");
        }

        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            cached.Add(Path.Combine(@"C:\llvm", tool), "");
        }

        var options = Options() with { ToolchainDirectory = @"C:\llvm" };
        var upToDate = Compilation.Create(options, [], cached).Emit(new EmitOptions { Stage = EmitStage.Obj }, TestContext.Current.CancellationToken);
        Assert.Empty(upToDate.CompiledUnits);

        var stale = (JsonObject)JsonNode.Parse(cached.ReadAllText(@"C:\p\obj\U.pcu"))!;
        Assert.True(HelperEntry(stale, "TIntH").Remove("classified"));
        cached.Add(@"C:\p\obj\U.pcu", stale.ToJsonString() + "\n");
        var again = Compilation.Create(options, [], cached).Emit(new EmitOptions { Stage = EmitStage.Obj }, TestContext.Current.CancellationToken);
        Assert.Equal(["U"], again.CompiledUnits);
        Assert.Equal(["P", "System"], again.UpToDateUnits);
        Assert.DoesNotContain(again.Diagnostics, d => d.Code == "E2202");               // out of date, never an error (CMP-160)
    }
}
