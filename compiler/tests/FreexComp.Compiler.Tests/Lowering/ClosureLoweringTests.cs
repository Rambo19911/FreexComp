using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>
/// C-308 (inline-variables.md LANG-566, CMP-1183): a statement list that declares captured inline variables gets a block frame —
/// a closure class made at each execution of the first captured declaration and chained to the frame the list runs on; the
/// anonymous methods written inside the list are methods of the innermost block frame. Everything else stays where it was.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-1183")]
public sealed class ClosureLoweringTests
{
    private const string SystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          IInterface = interface end;
          TInterfacedObject = class(TObject, IInterface) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private static BindingResult BindProgram(string declarations)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, $"program T; type TF = reference to function: Integer; {declarations} begin end."));
        Assert.Empty(tree.Diagnostics);
        var result = Binder.BindCompilationUnit(tree, types, system);
        Assert.Empty(Format(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return result;
    }

    private static BoundRoutineBody Routine(BindingResult result, string name) => result.Routines.Single(r => r.Routine.Name == name);

    private static string Body(BindingResult result, string name)
    {
        var body = Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == name);
        return string.Join(" ", body.Body.Statements.Select(LoweredDump.Of));
    }

    private static string[] Names(IEnumerable<Symbol> symbols) => [.. symbols.Select(s => s.Name)];

    /// <summary>
    /// One block frame per capturing statement list: the captured variable is its field (not the routine closure's), the frame
    /// links to the frame the list runs on — the routine's closure, for a nested list the enclosing block frame — and implements
    /// the interface of the anonymous methods written inside the list. The code makes a new instance where the first captured
    /// variable is declared, on every execution, and the scope releases its reference on every way out.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-566")]
    public void BlockFramePerCapturingScope()
    {
        var result = BindProgram("""
            procedure F(p: Integer);
            var fs: array[0..3] of TF; i, j: Integer;
            begin
              for i := 0 to 1 do
              begin
                var n := i * p;
                fs[i] := function: Integer begin Result := n + p end;
                for j := 2 to 3 do
                begin
                  var m := j;
                  fs[j] := function: Integer begin Result := n + m end;
                end;
              end
            end;
            """);

        var closure = Routine(result, "F").Routine.Closure!;
        Assert.Equal(["p"], Names(closure.Fields.Keys));                        // the parameter keeps one instance per activation
        Assert.Empty(closure.Methods);
        Assert.False(closure.IsBlock);
        Assert.Equal(2, closure.Blocks.Count);
        var (outer, inner) = (closure.Blocks[0], closure.Blocks[1]);

        // the list of the outer loop body: n, on the routine's closure
        Assert.True(outer.IsBlock);
        Assert.Equal("F.$Block2", outer.Class.Name);
        Assert.Equal(["$block2", "$block2Ref"], Names([outer.Local, outer.Keep]));
        Assert.Same(result.Types.TInterfacedObject, outer.Class.Parent);
        Assert.Equal(["n"], Names(outer.Fields.Keys));
        Assert.Equal(["$outer", "$outerRef", "n"], Names(outer.Class.Fields));
        Assert.Same(closure, outer.Enclosing);
        Assert.Same(closure.Class, outer.Outer!.Type);
        Assert.Same(result.Types.IInterface, outer.OuterRef!.Type);
        var first = Assert.Single(outer.Methods);
        Assert.Same(outer.Class, first.Method.ContainingType);
        Assert.Same(outer.Class, first.Method.SelfParameter!.Type);
        Assert.Same(outer, first.Method.AnonymousFrame);
        Assert.Same(first.Interface, Assert.Single(outer.Class.Interfaces).Interface);
        Assert.Same(first.Method, outer.Class.Interfaces[0].Implementations[^1]);

        // the list of the inner loop body: m, on the outer block frame
        Assert.Equal("F.$Block3", inner.Class.Name);
        Assert.Equal(["m"], Names(inner.Fields.Keys));
        Assert.Same(outer, inner.Enclosing);
        Assert.Same(outer.Class, inner.Outer!.Type);
        var second = Assert.Single(inner.Methods);
        Assert.Same(inner.Class, second.Method.ContainingType);
        Assert.NotSame(first.Method, second.Method);
        Assert.All(new[] { "n", "m" }, name =>
            Assert.NotNull(result.Routines.SelectMany(r => r.CapturedVariables).OfType<VariableSymbol>().First(v => v.Name == name).BlockFrame));

        // the classes are emitted with the unit's closure classes
        Assert.Equal(["F.$closure1", "F.$Block2", "F.$Block3"], Names(ClosureInfo.Of(result.Unit)));

        var text = Body(result, "F");
        // a new instance per execution of the declaration, kept by the counted local, linked to the routine's closure
        // (fields of the stub System: $outer at 16, $outerRef at 24, the variable at 32, the interface at 40)
        const string MakeOuter =
            "(:= $block2 (call Create:TObject)) " +
            "(call _IntfAsg (addr $block2Ref) (let $v2 $block2 (select (Equal (conv Pointer $v2) nil:Pointer) nil:IInterface (+ptr $v2 40:Int64)))) " +
            "(:= (deref (+ptr $block2 16:Int64)) $closure) " +
            "(call _IntfAsg (addr (deref (+ptr $block2 24:Int64))) (let $v3 $closure (select (Equal (conv Pointer $v3) nil:Pointer) nil:IInterface (+ptr $v3 8:Int64)))) " +
            "(scope (block (:= (deref (+ptr $block2 32:Int64)) (Multiply:Integer i (deref (+ptr $closure 16:Int64))))";
        Assert.True(text.Contains(MakeOuter, StringComparison.Ordinal), text);
        // … the inner one to the current outer instance
        const string MakeInner =
            "(:= $block3 (call Create:TObject)) " +
            "(call _IntfAsg (addr $block3Ref) (let $v5 $block3 (select (Equal (conv Pointer $v5) nil:Pointer) nil:IInterface (+ptr $v5 40:Int64)))) " +
            "(:= (deref (+ptr $block3 16:Int64)) $block2) " +
            "(call _IntfAsg (addr (deref (+ptr $block3 24:Int64))) (let $v6 $block2 (select (Equal (conv Pointer $v6) nil:Pointer) nil:IInterface (+ptr $v6 40:Int64)))) " +
            "(scope (block (:= (deref (+ptr $block3 32:Int64)) j)";
        Assert.True(text.Contains(MakeInner, StringComparison.Ordinal), text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"\(:= \$block2 "));
        // the scopes release their frames (inner first); the routine's cleanup does not
        Assert.Contains("(block (call _IntfClr (addr $block3Ref)))", text);
        Assert.Contains("(block (call _IntfClr (addr $block2Ref)))", text);
        // the anonymous methods are the interfaces of the current instances
        Assert.Contains("(call _IntfAsg (addr ([] fs (conv Int64 i))) (conv TF (let $v4 $block2 ", text);
        Assert.Contains("(call _IntfAsg (addr ([] fs (conv Int64 j))) (conv TF (let $v7 $block3 ", text);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"\(scope ").Count);
        var lowered = Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == "F");
        var cleanup = LoweredDump.Of(lowered.Cleanup!);
        Assert.DoesNotContain("$block", cleanup);
        Assert.Contains("$closureRef", cleanup);
    }

    /// <summary>
    /// Only the captured variables of a capturing list move: its other inline variables stay locals of the routine, a managed one
    /// finalised by the same region as the frame reference; an anonymous method written before the first captured declaration of
    /// the list (or outside the list) stays a method of the routine's closure.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-561")]
    public void UncapturedInlineStaysInRoutineFrame()
    {
        var result = BindProgram("""
            procedure F(c: Boolean; const a: string);
            var f, g: TF;
            begin
              if c then
              begin
                var plain := 1;
                g := function: Integer begin Result := 7 end;
                var s: string;
                s := a;
                var n := plain + 1;
                f := function: Integer begin Result := n end;
              end
            end;
            """);

        var closure = Routine(result, "F").Routine.Closure!;
        var block = Assert.Single(closure.Blocks);
        Assert.Equal(["n"], Names(block.Fields.Keys));
        Assert.Empty(closure.Fields);
        Assert.Equal("Anon1", Assert.Single(closure.Methods).Method.Name);      // g: before the first captured declaration
        Assert.Equal("Anon2", Assert.Single(block.Methods).Method.Name);

        var lowered = Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == "F");
        var locals = lowered.Locals.Select(l => l.Name).ToArray();
        Assert.Contains("plain", locals);
        Assert.Contains("s", locals);
        Assert.DoesNotContain("n", locals);
        var text = string.Join(" ", lowered.Body.Statements.Select(LoweredDump.Of));
        Assert.Contains("(:= plain 1:Integer)", text);
        // one region for the list, from its first managed declaration: the frame reference, then s
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"\(scope "));
        Assert.Contains("(block (call _IntfClr (addr $block2Ref)) (call _UStrClr (addr s)))", text);
        // g is the routine closure's interface, f the block frame's
        Assert.Matches(@"\(call _IntfAsg \(addr g\) \(conv TF \(let \$v\d+ \$closure ", text);
        Assert.Matches(@"\(call _IntfAsg \(addr f\) \(conv TF \(let \$v\d+ \$block2 ", text);
    }

    /// <summary>
    /// A routine whose anonymous methods capture no inline variable of a statement list (nor a <c>for var</c> variable: LANG-577,
    /// <see cref="CapturedForVarHasAFramePerIteration"/>) compiles as before: one closure with the captured parameter and classic
    /// local, all methods on it, no block frame.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-722")]
    public void NoBlockFrameWithoutCapturedInline()
    {
        var result = BindProgram("""
            procedure F(p: Integer);
            var k: Integer; f, g: TF;
            begin
              k := p;
              for var i := 1 to 2 do
              begin
                var unused := i + k;
                f := function: Integer begin Result := p + k end;
                begin
                  var deeper := unused;
                  g := function: Integer begin Result := k end;
                end;
              end
            end;
            """);

        var closure = Routine(result, "F").Routine.Closure!;
        Assert.Empty(closure.Blocks);
        Assert.Equal(["p", "k"], Names(closure.Fields.Keys));
        Assert.Equal(["Anon1", "Anon2"], Names(closure.Methods.Select(m => m.Method)));
        Assert.All(closure.Methods, m =>
        {
            Assert.Same(closure.Class, m.Method.ContainingType);
            Assert.Same(closure, m.Method.AnonymousFrame);
            Assert.Null(m.Interface.BlockFrame);
        });
        Assert.Equal(["F.$closure1"], Names(ClosureInfo.Of(result.Unit)));

        var lowered = Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == "F");
        var text = string.Join(" ", lowered.Body.Statements.Select(LoweredDump.Of));
        Assert.DoesNotContain("$block", text);
        Assert.DoesNotContain("$Block", text);
        Assert.DoesNotContain("(scope", text);
        Assert.Contains("unused", lowered.Locals.Select(l => l.Name));
        Assert.Contains("deeper", lowered.Locals.Select(l => l.Name));
    }

    /// <summary>
    /// C-387 (LANG-577, CMP-1410): a captured <c>for var</c> variable is the field of the loop's block frame, on the routine's
    /// closure; the anonymous methods of the body — also one inside a nested list that captures nothing — are its methods. The
    /// loop runs on the hidden <c>$for&lt;m&gt;</c>; each iteration makes a new frame and stores <c>$for&lt;m&gt;</c> into it; the
    /// frame slot is nil before the loop, and the loop's region releases <c>$block&lt;m&gt;Ref</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-577")]
    [Trait("Req", "CMP-1410")]
    public void CapturedForVarHasAFramePerIteration()
    {
        var result = BindProgram("""
            procedure F(p: Integer);
            var k: Integer; f, g: TF;
            begin
              k := p;
              for var i := 1 to 2 do
              begin
                var unused := i + k;
                f := function: Integer begin Result := p + k + i end;
                begin
                  var deeper := unused;
                  g := function: Integer begin Result := k end;
                end;
              end
            end;
            """);

        var closure = Routine(result, "F").Routine.Closure!;
        Assert.Equal(["p", "k"], Names(closure.Fields.Keys));
        Assert.Empty(closure.Methods);
        var block = Assert.Single(closure.Blocks);
        Assert.Equal("F.$Block2", block.Class.Name);
        Assert.Equal(["i"], Names(block.Fields.Keys));
        Assert.Equal(["$outer", "$outerRef", "i"], Names(block.Class.Fields));
        Assert.Same(closure, block.Enclosing);
        Assert.Equal(["Anon1", "Anon2"], Names(block.Methods.Select(m => m.Method)));

        var lowered = Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == "F");
        var text = string.Join(" ", lowered.Body.Statements.Select(LoweredDump.Of));
        Assert.Contains("$for2", lowered.Locals.Select(l => l.Name));
        Assert.DoesNotContain("i", lowered.Locals.Select(l => l.Name));
        Assert.Contains("(:= $block2 nil:", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"\(:= \$block2 \(call Create:TObject\)\)"));
        Assert.Contains("(:= (deref (+ptr $block2 32:Int64)) $for2)", text);
        Assert.Contains("(block (call _IntfClr (addr $block2Ref)))", text);
        Assert.True(text.IndexOf("(:= $block2 nil:", StringComparison.Ordinal) < text.IndexOf("(:= $block2 (call Create:TObject))", StringComparison.Ordinal), text);
        Assert.DoesNotContain("$block2Ref", LoweredDump.Of(lowered.Cleanup!));
    }
}
