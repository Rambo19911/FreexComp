using System.Collections.Concurrent;
using FreexComp.Compiler;

namespace FreexComp.LanguageService.Tests;

/// <summary>language-service.md §3 (CMP-715) and the hint H2004 (C-133).</summary>
[Trait("Phase", "3")]
public sealed class DiagnosticServiceTests
{
    private static string Key(Diagnostic d) => d.ToText();

    /// <summary>
    /// CMP-715 over the golden corpus: for every test's main file the language service reports exactly what the <c>--check</c>
    /// stage of the same compilation reports (H2004 aside). Cases with per-test <c>// opc:</c> options are compiled with those
    /// options by the golden harness only and are left out here.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-715")]
    public void LanguageServiceDiagnosticsMatchCli()
    {
        var root = Path.Combine(RepoPaths.Root, "tests", "compiler");
        var mains = Directory.EnumerateDirectories(root).SelectMany(m => Directory.EnumerateFiles(m, "*.pas")
                .Concat(Directory.EnumerateDirectories(m).Select(f => Path.Combine(f, Path.GetFileName(f) + ".pas")).Where(File.Exists)))
            .Where(p => !File.ReadLines(p).TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal)).Any(l => l.Contains("opc:", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToList();
        Assert.True(mains.Count > 300, $"{mains.Count} golden mains");
        var rtl = Path.Combine(RepoPaths.Root, "rtl", "src");
        var failures = new ConcurrentBag<string>();
        var ct = TestContext.Current.CancellationToken;

        Parallel.ForEach(mains, new ParallelOptions { CancellationToken = ct }, main =>
        {
            var options = new CompilationOptions
            {
                MainFile = main, RtlDirectory = rtl, ObjectDirectory = Path.Combine(Path.GetTempPath(), "freexcomp-ls-never-written"), MaxParallelism = 1,
            };
            if (Compilation.Create(options, [], PhysicalFileSystem.Instance).MainModuleKind == ModuleKind.Library)
            {
                options = options with { AppType = AppType.Library };           // CMP-1060: as opc does for a library main file (else E0012)
            }

            var cli = Compilation.Create(options, [], PhysicalFileSystem.Instance).Emit(new EmitOptions { Stage = EmitStage.Check }, ct)
                .Diagnostics.Where(d => string.Equals(d.Location.Path, main, StringComparison.OrdinalIgnoreCase));
            var workspace = new Workspace(PhysicalFileSystem.Instance);
            workspace.SetProject(options);
            var document = workspace.GetDocument(main);
            var service = DiagnosticService.GetSyntaxDiagnostics(document).Concat(DiagnosticService.GetSemanticDiagnostics(document, ct))
                .Where(d => d.Code != "H2004");

            var expected = cli.Select(Key).Order(StringComparer.Ordinal).ToList();
            var actual = service.Select(Key).Order(StringComparer.Ordinal).ToList();
            if (!expected.SequenceEqual(actual))
            {
                failures.Add($"{main}\n  cli: {string.Join(" | ", expected)}\n  ls:  {string.Join(" | ", actual)}");
            }
        });

        Assert.True(failures.IsEmpty, string.Join("\n", failures.Order(StringComparer.Ordinal).Take(10)));
    }

    [Fact]
    public void UnusedUnit_IsHintH2004()
    {
        var workspace = new Workspace(TestFiles.Files(
            ("P.pas", "program P;\nuses U1, U2;\nbegin\n  Writeln(K);\nend."),
            ("U1.pas", "unit U1;\ninterface\nconst K = 1;\nimplementation\nend."),
            ("U2.pas", "unit U2;\ninterface\nconst L = 2;\nimplementation\nend.")));
        workspace.SetProject(TestFiles.Options());

        var hints = DiagnosticService.GetSemanticDiagnostics(workspace.GetDocument(TestFiles.PathOf("P.pas")), TestContext.Current.CancellationToken)
            .Where(d => d.Code == "H2004").ToList();

        var hint = Assert.Single(hints);
        Assert.Equal(DiagnosticSeverity.Hint, hint.Severity);
        Assert.Equal("Unit 'U2' is used but nothing from it is referenced", hint.Message);
        Assert.Equal("P.pas(2,10): hint H2004: Unit 'U2' is used but nothing from it is referenced", hint.ToText()[(TestFiles.Dir.Length + 1)..]);
    }

    /// <summary>C-398: a named pointer type belongs to its declaring unit, so a unit used only for one is no H2004.</summary>
    [Fact]
    public void UnitUsedOnlyForItsPointerType_IsNoH2004()
    {
        var workspace = new Workspace(TestFiles.Files(
            ("P.pas", "program P;\nuses U2;\nvar C: PCount;\nbegin\nend."),
            ("U2.pas", "unit U2;\ninterface\ntype PCount = ^Integer;\nimplementation\nend.")));
        workspace.SetProject(TestFiles.Options());

        var diagnostics = DiagnosticService.GetSemanticDiagnostics(workspace.GetDocument(TestFiles.PathOf("P.pas")), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(diagnostics, d => d.Code == "H2004");
    }

    [Fact]
    public void SyntaxAndSemanticDiagnosticsDoNotOverlap()
    {
        var workspace = new Workspace(TestFiles.Files(("P.pas", "program P;\nbegin\n  X := ;\nend.")));
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));

        var syntax = DiagnosticService.GetSyntaxDiagnostics(document);
        var semantic = DiagnosticService.GetSemanticDiagnostics(document, TestContext.Current.CancellationToken);

        Assert.NotEmpty(syntax);
        Assert.Empty(semantic.Intersect(syntax));
    }

    /// <summary>
    /// CMP-1476 (LANG-977 rule 3): for a file whose generic instances prune statements the language service reports exactly what the
    /// <c>--check</c> stage reports — nothing for a pruned statement (no E2710, no hint for a name only it uses), the error of a kept
    /// one once, and the error of a statement behind a plain constant condition.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1476")]
    [InlineData("Result := Length(string(V))", "if GetTypeKind(T) = tkInteger then", "")]
    [InlineData("Result := V + 1", "if GetTypeKind(T) = tkInteger then", "(16,17): error E2710: In instantiation 'TOps<string>': ")]
    [InlineData("Result := 0", "if SizeOf(T) = 4 then", "(13,15): error E2710: In instantiation 'TOps<string>': ")]
    public void TypeQueryPruning_SameAsCompiler(string otherwise, string condition, string expected)
    {
        var text = $$"""
            program P;
            type
              TOps<T> = class
                class function Bump(const V: T): Integer;
              end;
            class function TOps<T>.Bump(const V: T): Integer;
            var
              Extra: Integer;
            begin
              {{condition}}
              begin
                Extra := 1;
                Result := Integer(V) + Extra;
              end
              else
                {{otherwise}};
            end;
            begin
              Writeln(TOps<Integer>.Bump(1), TOps<string>.Bump('ab'));
            end.
            """.ReplaceLineEndings("\n");
        var fs = TestFiles.Files(("P.pas", text));
        var ct = TestContext.Current.CancellationToken;

        var cli = Compilation.Create(TestFiles.Options(), [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, ct).Diagnostics
            .Where(d => string.Equals(d.Location.Path, TestFiles.PathOf("P.pas"), StringComparison.OrdinalIgnoreCase)).Select(Key).ToList();
        var workspace = new Workspace(fs);
        workspace.SetProject(TestFiles.Options());
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        var service = DiagnosticService.GetSyntaxDiagnostics(document).Concat(DiagnosticService.GetSemanticDiagnostics(document, ct))
            .Where(d => d.Code != "H2004").Select(Key).ToList();

        if (expected.Length == 0)
        {
            Assert.Empty(cli);
        }
        else
        {
            Assert.StartsWith(TestFiles.PathOf("P.pas") + expected, Assert.Single(cli), StringComparison.Ordinal);
        }

        Assert.Equal(cli, service);
    }
}
