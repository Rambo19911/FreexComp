using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

public sealed partial class PdiSidecarTests
{
    /// <summary>
    /// CMP-1393 (C-344): nested classes and records get the sidecar type entries and debug-info nodes of unit-level types of their
    /// kind, named with their display name <c>TOuter.TInner</c>; a nested method's routine is <c>TOuter.TInner.M</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1393")]
    [Trait("Req", "LANG-775")]
    public void NestedClassTypeNames()
    {
        const string Source = """
            program P;
            type
              TOuter = class
              public
                type
                  TPoint = record
                    X, Y: Integer;
                  end;
                  TInner = class
                    Where: TPoint;
                    function M: Integer;
                  end;
              end;
            function TOuter.TInner.M: Integer;
            begin
              Result := Where.X;
            end;
            var
              G: TOuter.TInner;
            begin
              G := nil;
            end.
            """;
        var tree = SyntaxTree.Parse(SourceText.From(Path, Source));
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", ClosureSystemSource)), types);
        var result = Binder.BindCompilationUnit(tree, types, system);
        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(DebugFormat.CodeView, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, Path) ? Encoding.UTF8.GetBytes(Source) : null };

        var fragment = IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);

        var pdi = JsonNode.Parse(fragment!)!.AsObject();
        SidecarTests.ValidateSchema(pdi);
        var entries = pdi["types"]!.AsArray().Select(t => t!.AsObject()).ToList();
        var inner = Assert.Single(entries, t => t["name"]!.GetValue<string>() == "TOuter.TInner");
        Assert.Equal("class", inner["kind"]!.GetValue<string>());
        Assert.Equal("P", inner["unit"]!.GetValue<string>());
        var point = Assert.Single(entries, t => t["name"]!.GetValue<string>() == "TOuter.TPoint");
        Assert.Equal("record", point["kind"]!.GetValue<string>());
        Assert.DoesNotContain(entries, t => t["name"]!.GetValue<string>() is "TInner" or "TPoint");
        var unit = pdi["units"]!.AsArray().Single()!.AsObject();
        Assert.NotNull(Routine(unit, "TOuter.TInner.M"));
        var ir = writer.GetText();
        Assert.Contains("name: \"TOuter.TInner\"", ir, StringComparison.Ordinal);
        Assert.Contains("name: \"TOuter.TPoint\"", ir, StringComparison.Ordinal);
        Assert.Contains("!DISubprogram(name: \"TOuter.TInner.M\"", ir, StringComparison.Ordinal);
    }
}
