using System.Text;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-076, CMP-156: units bind (implementation parts, bodies, flow analysis) and emit on up to
/// <see cref="CompilationOptions.MaxParallelism"/> threads. A 20-unit synthetic project gives exactly the sequential results —
/// every <c>.ll</c> byte for byte, the diagnostics (unit init order, the main file last) and the unit lists — on every run.
/// No timing is asserted (TESTING.md: determinism over speed).
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-156")]
public sealed class ParallelEmitTests
{
    private const int UnitCount = 20;

    private static string Name(int i) => "U" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Unit i: interface uses unit i − 1 (a chain), implementation uses unit i + 7 when it exists (crossing the chain), a managed
    /// global, an initialization section, a routine with a hint (unused local) and, for <paramref name="errorUnits"/>, an error.
    /// </summary>
    private static InMemoryFileSystem Project(params int[] errorUnits)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        for (var i = 1; i <= UnitCount; i++)
        {
            var text = new StringBuilder();
            text.Append("unit ").Append(Name(i)).Append(";\ninterface\n");
            if (i > 1)
            {
                text.Append("uses ").Append(Name(i - 1)).Append(";\n");
            }

            text.Append("const C").Append(i).Append(" = ").Append(i).Append(";\n");
            text.Append("var S").Append(i).Append(": string;\n");
            text.Append("function F").Append(i).Append("(X: Integer): Integer;\n");
            text.Append("implementation\n");
            if (i + 7 <= UnitCount)
            {
                text.Append("uses ").Append(Name(i + 7)).Append(";\n");
            }

            text.Append("function F").Append(i).Append("(X: Integer): Integer;\nvar\n  Unused").Append(i).Append(": Integer;\nbegin\n");
            text.Append("  Result := X * ").Append(i);
            if (i > 1)
            {
                text.Append(" + F").Append(i - 1).Append("(X) + C").Append(i - 1);
            }

            if (i + 7 <= UnitCount)
            {
                text.Append(" + C").Append(i + 7);
            }

            text.Append(";\n");
            if (errorUnits.Contains(i))
            {
                text.Append("  Missing").Append(i).Append(" := 1;\n");
            }

            text.Append("end;\ninitialization\n  S").Append(i).Append(" := 'unit ").Append(i).Append("';\nend.\n");
            fs.Add(Path.Combine(@"C:\p", Name(i) + ".pas"), text.ToString());
        }

        var program = new StringBuilder("program P;\nuses ");
        program.AppendJoin(", ", Enumerable.Range(1, UnitCount).Reverse().Select(Name)).Append(";\nbegin\n");
        program.Append("  Writeln(F").Append(UnitCount).Append("(1), ' ', S1, ' ', S").Append(UnitCount).Append(");\nend.\n");
        return fs.Add(@"C:\p\P.pas", program.ToString());
    }

    private sealed record Outcome(bool Success, string[] Diagnostics, string[] Compiled, string[] Files);

    private static Outcome Emit(InMemoryFileSystem fs, int maxParallelism, EmitStage stage)
    {
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj", MaxParallelism = maxParallelism };
        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = stage }, TestContext.Current.CancellationToken);
        var files = fs.EnumerateFiles(@"C:\p\obj", "*.ll").Order(StringComparer.Ordinal).Select(f => f + "\n" + fs.ReadAllText(f)).ToArray();
        return new Outcome(result.Success, [.. result.Diagnostics.Select(d => d.ToText())], [.. result.CompiledUnits], files);
    }

    private static void AssertSame(Outcome expected, Outcome actual, string what)
    {
        Assert.True(expected.Success == actual.Success, what);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Compiled, actual.Compiled);
        Assert.Equal(expected.Files.Length, actual.Files.Length);
        for (var i = 0; i < expected.Files.Length; i++)
        {
            Assert.True(expected.Files[i] == actual.Files[i], $"{what}: {expected.Files[i][..expected.Files[i].IndexOf('\n', StringComparison.Ordinal)]} differs");
        }
    }

    [Fact]
    public void TwentyUnits_InParallel_GiveTheSequentialResult()
    {
        var sequential = Emit(Project(), maxParallelism: 1, EmitStage.Ll);
        Assert.True(sequential.Success, string.Join("\n", sequential.Diagnostics));
        Assert.Equal(UnitCount + 2, sequential.Files.Length);
        Assert.Equal(UnitCount, sequential.Diagnostics.Count(d => d.Contains(" hint H2001: ", StringComparison.Ordinal)));
        Assert.Equal(["P", "System"], sequential.Compiled.Take(2));
        Assert.Equal(UnitCount + 2, sequential.Compiled.Length);

        foreach (var threads in new[] { 8, 0, 3, 8 })
        {
            AssertSame(sequential, Emit(Project(), threads, EmitStage.Ll), $"MaxParallelism = {threads}");
        }
    }

    /// <summary>Errors in several units: the same diagnostics in the same order (unit init order, then position) for any thread count.</summary>
    [Fact]
    public void ErrorsInSeveralUnits_AreReportedInTheSequentialOrder()
    {
        int[] errors = [17, 3, 12];
        var sequential = Emit(Project(errors), maxParallelism: 1, EmitStage.Check);
        Assert.False(sequential.Success);
        var errorLines = sequential.Diagnostics.Where(d => d.Contains(" error ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, errorLines.Length);
        Assert.All(errorLines, e => Assert.Contains("E2003", e, StringComparison.Ordinal));

        foreach (var threads in new[] { 8, 0, 8 })
        {
            AssertSame(sequential, Emit(Project(errors), threads, EmitStage.Check), $"MaxParallelism = {threads}");
        }
    }

    /// <summary>The bodies of every unit are bound even when they bind on several threads (no unit is left half bound).</summary>
    [Fact]
    public void ParallelBinding_CompletesEveryUnit()
    {
        var fs = Project();
        var resolver = new UnitResolver(fs, @"C:\p\P.pas", [], @"C:\rtl", []);
        var binder = new UnitBinder(new BuiltInTypes(), resolver, (path, _, diagnostics) =>
            SyntaxTree.Parse(SourceText.From(path, fs.ReadAllText(path)), new ParseOptions([]), fs));

        binder.BindAll(@"C:\rtl\System.pas", @"C:\p\P.pas", maxParallelism: 8);

        Assert.Equal(UnitCount + 2, binder.Units.Count);
        Assert.All(binder.Units, u => Assert.Equal(UnitPhase.Complete, u.Phase));
        Assert.All(binder.Units, u => Assert.NotNull(u.Result));
    }
}
