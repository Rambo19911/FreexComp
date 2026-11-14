using FreexComp.Compiler.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-090…C-092: binding of <c>raise</c>, <c>try…except</c> with <c>on</c> handlers and <c>try…finally</c> (LANG-805…826), and their flow analysis.</summary>
[Trait("Phase", "2")]
public sealed class ExceptionBindingTests
{
    private const string Classes = "type EA = class constructor Create; end; EB = class(EA) end; constructor EA.Create; begin end; ";

    private static string[] Flow(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return [.. FlowAnalyzer.Analyze(result).Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1})")];
    }

    /// <summary>"CODE(1,col)" at the first occurrence of <paramref name="needle"/>'s text, the column marked by <c>|</c>.</summary>
    private static string At(string code, string text, string needle)
    {
        var index = text.IndexOf(needle.Replace("|", ""), StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{needle}' not found");
        return $"{code}(1,{index + needle.IndexOf('|') + 1})";
    }

    [Fact]
    [Trait("Req", "LANG-805")]
    [Trait("Req", "LANG-806")]
    public void Raise_AcceptsClassInstancesNilAndAnAddress()
    {
        Assert.Empty(BindDiagnostics(Classes + "var P: Pointer; E: EA; begin raise EA.Create; raise E at P; raise nil; raise EB.Create at nil end."));
        var text = Classes + "var S: string; begin raise S end.";
        Assert.Equal([At("E2800", text, "raise |S") + ": Class instance required after 'raise'"], BindDiagnostics(text));
        text = Classes + "var E: EA; begin raise E at 1 end.";
        Assert.Equal([At("E2010", text, "at |1") + ": Incompatible types: 'Integer' and 'Pointer'"], BindDiagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-807")]
    public void Reraise_IsAllowedOnlyLexicallyInsideAHandler()
    {
        Assert.Empty(BindDiagnostics(Classes + "begin try except raise end; try except on EA do raise; else raise end end."));
        var text = Classes + "begin try raise finally end end.";
        Assert.Equal([At("E2801", text, "try |raise") + ": Re-raising an exception is only allowed inside an exception handler"], BindDiagnostics(text));
        text = Classes + "procedure P; begin raise end; begin try except P end end.";        // a routine called from a handler is not inside it
        Assert.Equal([At("E2801", text, "begin |raise") + ": Re-raising an exception is only allowed inside an exception handler"], BindDiagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-821")]
    public void HandlerVariable_IsReadOnlyAndScopedToItsHandler()
    {
        Assert.Empty(BindDiagnostics(Classes + "var E: Integer; X: EA; begin try except on E: EB do X := E; end; E := 1 end."));
        var text = Classes + "begin try except on E: EA do E := nil end end.";
        Assert.Equal([At("E2053", text, "do |E :=") + ": Left side cannot be assigned to"], BindDiagnostics(text));
        text = Classes + "var X: EA; begin try except on E: EA do ; end; X := E end.";
        Assert.Equal([At("E2003", text, "X := |E end") + ": Undeclared identifier 'E'"], BindDiagnostics(text));
    }

    [Fact]
    [Trait("Req", "LANG-822")]
    public void HandlerTypes_AreClasses_AndAnUnreachableHandlerIsW1080()
    {
        var text = Classes + "begin try except on Integer do ; end end.";
        Assert.Equal([At("E2802", text, "on |Integer") + ": Class type required in exception handler"], BindDiagnostics(text));
        text = Classes + "begin try except on EX do ; end end.";
        Assert.Equal([At("E2003", text, "on |EX") + ": Undeclared identifier 'EX'"], BindDiagnostics(text));
        text = Classes + "begin try except on EA do ; on EB do ; end end.";
        Assert.Equal([At("W1080", text, "on |EB") + ": Exception handler for 'EB' is never reached"], BindDiagnostics(text));
        Assert.Empty(BindDiagnostics(Classes + "begin try except on EB do ; on EA do ; end end."));
    }

    [Fact]
    [Trait("Req", "LANG-812")]
    public void LeavingAFinallyPart_IsE2067_ButLoopsInsideItMayBreak()
    {
        Assert.Empty(BindDiagnostics("var I: Integer; begin try finally for I := 1 to 2 do begin if I = 1 then Continue; Break end end end."));
        var text = "var I: Integer; begin for I := 1 to 2 do try finally Break end end.";
        Assert.Equal([At("E2067", text, "finally |Break") + ": Cannot leave a 'finally' part with 'Break'"], BindDiagnostics(text));
        text = "procedure P; begin try finally Exit end end; begin end.";
        Assert.Equal([At("E2067", text, "finally |Exit") + ": Cannot leave a 'finally' part with 'Exit'"], BindDiagnostics(text));
        Assert.Empty(BindDiagnostics("procedure P; begin try Exit finally end; try except Exit end end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void Flow_AHandlerMayStartBeforeAnyAssignmentOfTheTryPart()
    {
        // x is assigned in the try part: maybe unassigned in the handler, assigned after the statement on the normal path
        var text = "procedure P; var x, y: Integer; begin try x := 1; y := x; Writeln(y) except Writeln(x) end end; begin P end.";
        Assert.Equal([At("W1041", text, "except Writeln(|x)")], Flow(text));
        Assert.Empty(Flow("procedure P; var x: Integer; begin try x := 1 finally end; Writeln(x) end; begin P end."));
        Assert.Empty(Flow("procedure P; var x: Integer; begin x := 0; try x := 1 finally Writeln(x) end end; begin P end."));
    }

    [Fact]
    [Trait("Req", "LANG-808")]
    public void Flow_RaiseEndsThePath()
    {
        Assert.Empty(Flow(Classes + "function F: Integer; begin raise EA.Create end; begin Writeln(F) end."));
    }
}
