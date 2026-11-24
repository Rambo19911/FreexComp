using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Tests;

/// <summary>
/// targets.md CMP-1010 (task C-280), the language-service side: diagnostics and the inactive-code classification follow the
/// predefined symbols of the project's target (the lexer and compilation sides are the compiler's test of the same name).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class TargetSymbolsTests
{
    private const string Program = """
        program P;
        begin
          {$IFDEF CPU32BITS}
          Only32;
          {$ENDIF}
          {$IFDEF CPU64BITS}
          Only64;
          {$ENDIF}
        end.
        """;

    private static readonly TargetInfo Fake32 = TargetInfo.Win64 with
    {
        Id = "fake32",
        Triple = "i686-pc-windows-msvc",
        PointerSize = 4,
        PointerAlign = 4,
        PredefinedSymbols = ["MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS"],
    };

    private static (string[] Undeclared, string[] Disabled) Analyse(TargetInfo? target)
    {
        var text = Program.ReplaceLineEndings("\n");
        var fs = new InMemoryFileSystem()
            .Add(Path.Combine(TestFiles.RtlDir, "System.pas"), "unit System; interface type TObject = class end; var Input, Output: Text; implementation end.")
            .Add(TestFiles.PathOf("P.pas"), text);
        var workspace = new Workspace(fs);
        workspace.SetProject(target is null ? TestFiles.Options() : TestFiles.Options() with { Target = target });
        var document = workspace.GetDocument(TestFiles.PathOf("P.pas"));
        Assert.Empty(DiagnosticService.GetSyntaxDiagnostics(document));
        var undeclared = DiagnosticService.GetSemanticDiagnostics(document, TestContext.Current.CancellationToken)
            .Where(d => d.Code == "E2003").Select(d => d.Message).ToArray();
        var disabled = Classifier.GetSyntacticClassifications(document.SyntaxTree, new TextSpan(0, text.Length))
            .Where(s => s.Kind == ClassificationKind.DisabledText).Select(s => text.Substring(s.Span.Start, s.Span.Length).Trim()).ToArray();
        return (undeclared, disabled);
    }

    [Fact]
    [Trait("Req", "CMP-1010")]
    public void FakeTarget_DefinesItsOwnSymbols()
    {
        var fake = Analyse(Fake32);
        Assert.Equal(["Undeclared identifier 'Only32'"], fake.Undeclared);
        Assert.Equal(["Only64;"], fake.Disabled);

        // the baseline target, named or by default (a project without a target): unchanged
        var win64 = Analyse(TargetInfo.Win64);
        Assert.Equal(["Undeclared identifier 'Only64'"], win64.Undeclared);
        Assert.Equal(["Only32;"], win64.Disabled);
        var byDefault = Analyse(null);
        Assert.Equal(win64.Undeclared, byDefault.Undeclared);
        Assert.Equal(win64.Disabled, byDefault.Disabled);
    }
}
