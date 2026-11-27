using System.Text;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-414 (codegen.md CMP-1470…1472): the debug description of the variables anonymous methods capture — one module with every
/// location form: the owner's <c>$closure</c> as a slot and as a field of a frame record, the frame parameter <c>$frame</c> of an
/// anonymous method, and the hidden slots <c>$frame&lt;k&gt;</c> of the outer frames (routine closures and block frames).
/// </summary>
public sealed partial class IrSnapshotTests
{
    internal const string ClosureDebugSource = """
        unit M;
        interface
        type
          TIntFunc = reference to function(Delta: Integer): Integer;
          TProc = reference to procedure;
          TCounter = class
            FBase: Integer;
            function MakeAdder(Start: Integer): TIntFunc;
          end;
        function Sum(const Values: array of Integer; Scale: Integer): TIntFunc;
        implementation
        var Keep: array[0..3] of TIntFunc;

        function TCounter.MakeAdder(Start: Integer): TIntFunc;
        var
          Count: Integer;
        begin
          Count := 0;
          Result := function(Delta: Integer): Integer
            var
              Twice: Integer;
              Log: TProc;
            begin
              Twice := Delta * 2;
              Count := Count + 1;
              Start := Start + Twice;
              Log := procedure
                begin
                  Count := Count + FBase + Start + Twice;
                end;
              Log();
              Result := Start;
            end;
        end;

        function Sum(const Values: array of Integer; Scale: Integer): TIntFunc;
        var
          Total: Integer;
          procedure Add(V: Integer);
          begin
            Total := Total + V * Scale;
          end;
        begin
          Total := 0;
          for var X in Values do
          begin
            var Twice := X * 2;
            Add(Twice);
            Keep[0] := function(Delta: Integer): Integer begin Result := X + Twice + Delta + Total end;
          end;
          Result := function(Delta: Integer): Integer begin Result := Total + Delta end;
        end;
        end.
        """;

    /// <summary>The module of <see cref="ClosureDebugSource"/> with debug information, bound against <see cref="ClosureSystemSource"/>.</summary>
    internal static string EmitClosureDebug(DebugFormat format = DebugFormat.CodeView)
    {
        const string path = @"C:\p\M.pas";
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", ClosureSystemSource)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(path, ClosureDebugSource));
        Assert.Empty(tree.Diagnostics);
        var result = Binder.BindCompilationUnit(tree, types, system);
        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var writer = new LlvmTextWriter();
        var options = new DebugInfoOptions(format, IsOptimized: false) { ReadFile = p => PathUtil.Comparer.Equals(p, path) ? Encoding.UTF8.GetBytes(ClosureDebugSource) : null };
        IrEmitter.EmitUnit(Lowerer.Lower(result), writer, debug: options);
        return writer.GetText();
    }

    /// <summary>CMP-1470, CMP-1471: the module with its debug records and the debug-only slots of the anonymous methods.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1470")]
    [Trait("Req", "CMP-1471")]
    public void ClosureDebug() => Snapshot.Verify("ClosureDebug", EmitClosureDebug());
}
