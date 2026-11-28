namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-431: a method named as a value — where a plain procedural type, a method pointer or a method reference is expected, and as
/// the operand of <c>@</c> — obeys its visibility like a call of it (LANG-610, 612: E2610 at the method name).
/// </summary>
[Trait("Phase", "8")]
public sealed class ProceduralValueTests
{
    private const string UnitU = """
        unit U;
        interface
        type
          TIntFunc = function(X: Integer): Integer;
          TIntMethod = function(X: Integer): Integer of object;
          TIntRef = reference to function(X: Integer): Integer;
          TUtil = class
          strict private
            class function SPriv(X: Integer): Integer; static;
            class function SPrivC(X: Integer): Integer;
            function SPrivI(X: Integer): Integer;
            class function Secret<T>(X: T): T; static;
          private
            class function Priv(X: Integer): Integer; static;
            function PrivI(X: Integer): Integer;
          strict protected
            class function SProt(X: Integer): Integer; static;
            function SProtI(X: Integer): Integer;
          protected
            class function Prot(X: Integer): Integer; static;
            function ProtI(X: Integer): Integer;
          public
            class function Pub(X: Integer): Integer; static;
            function PubI(X: Integer): Integer;
            class function Open<T>(X: T): T; static;
          end;
        implementation
        class function TUtil.SPriv(X: Integer): Integer; begin Result := X end;
        class function TUtil.SPrivC(X: Integer): Integer; begin Result := X end;
        function TUtil.SPrivI(X: Integer): Integer; begin Result := X end;
        class function TUtil.Secret<T>(X: T): T; begin Result := X end;
        class function TUtil.Priv(X: Integer): Integer; begin Result := X end;
        function TUtil.PrivI(X: Integer): Integer; begin Result := X end;
        class function TUtil.SProt(X: Integer): Integer; begin Result := X end;
        function TUtil.SProtI(X: Integer): Integer; begin Result := X end;
        class function TUtil.Prot(X: Integer): Integer; begin Result := X end;
        function TUtil.ProtI(X: Integer): Integer; begin Result := X end;
        class function TUtil.Pub(X: Integer): Integer; begin Result := X end;
        function TUtil.PubI(X: Integer): Integer; begin Result := X end;
        class function TUtil.Open<T>(X: T): T; begin Result := X end;
        var
          F: TIntFunc;
          M: TIntMethod;
          R: TIntRef;
          P: Pointer;
          O: TUtil;
        initialization
          // private and protected are open to the whole unit: nothing here is an error
          F := TUtil.Priv;
          F := @TUtil.Prot;
          P := @TUtil.Priv;
          M := O.PrivI;
          M := O.ProtI;
          R := TUtil.Priv;
          R := O.PrivI;
        end.
        """;

    private const string ProgramP = """
        program P;
        uses U;
        type
          TDer = class(TUtil)
            procedure Run;
            class procedure CRun; static;
          end;
          TLocal = class
          strict private
            class function LSPriv(X: Integer): Integer; static;
          private
            class function LPriv(X: Integer): Integer; static;
          end;
        function Apply(F: TIntFunc; X: Integer): Integer; begin Result := F(X) end;
        function ApplyM(F: TIntMethod; X: Integer): Integer; begin Result := F(X) end;
        function ApplyR(const F: TIntRef; X: Integer): Integer; begin Result := F(X) end;
        class function TLocal.LSPriv(X: Integer): Integer; begin Result := X end;
        class function TLocal.LPriv(X: Integer): Integer; begin Result := X end;
        var
          F: TIntFunc;
          M: TIntMethod;
          R: TIntRef;
          P: Pointer;
          O: TUtil;
          N: Integer;
        procedure TDer.Run;
        begin
          {RUN};
        end;
        class procedure TDer.CRun;
        begin
          {CRUN};
        end;
        begin
          {MAIN};
        end.
        """;

    /// <summary>
    /// LANG-610, 612: every form that names a method as a value checks its visibility as a call of it does — E2610 at the method
    /// name, with the receiver the name is written on (<c>TDer.Prot</c> is open to a unit declaring the descendant <c>TDer</c>,
    /// <c>TUtil.Prot</c> is not; an inherited method named inside a descendant follows the rules of <c>Self</c>). The forms: a plain
    /// procedural value (LANG-480: <c>TClass.Static</c>, <c>@TClass.Static</c>, unit-qualified, unqualified inside a method), a
    /// method pointer (LANG-481), a method reference (LANG-485; the forwarder is not made for an inaccessible method), <c>@</c> with
    /// no procedural type expected (LANG-265), each also as an argument; instances of generic methods the same (LANG-981).
    /// <paramref name="expected"/>: <c>column|visibility|member</c> of the one E2610, or null when the code is accepted.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-610")]
    [Trait("Req", "LANG-612")]
    [Trait("Req", "LANG-480")]
    [Trait("Req", "LANG-481")]
    [Trait("Req", "LANG-485")]
    // the call: what every row below is measured against (a call reports at its start, a value at the method name)
    [InlineData("MAIN", "N := TUtil.SPriv(1)", "8|strict private|SPriv")]
    [InlineData("MAIN", "N := O.PrivI(1)", "8|private|PrivI")]
    [InlineData("RUN", "N := SPriv(1)", "8|strict private|SPriv")]
    // a plain procedural value: TClass.StaticMethod, also with @ and through the unit name
    [InlineData("MAIN", "F := TUtil.SPriv", "14|strict private|SPriv")]
    [InlineData("MAIN", "F := TUtil.Priv", "14|private|Priv")]
    [InlineData("MAIN", "F := TUtil.SProt", "14|strict protected|SProt")]
    [InlineData("MAIN", "F := TUtil.Prot", "14|protected|Prot")]
    [InlineData("MAIN", "F := @TUtil.SPriv", "15|strict private|SPriv")]
    [InlineData("MAIN", "F := @TUtil.Priv", "15|private|Priv")]
    [InlineData("MAIN", "F := U.TUtil.Priv", "16|private|Priv")]
    [InlineData("MAIN", "F := (TUtil.Priv)", "15|private|Priv")]
    [InlineData("MAIN", "N := Apply(TUtil.SPriv, 1)", "20|strict private|SPriv")]
    [InlineData("MAIN", "N := Apply(@TUtil.Priv, 1)", "21|private|Priv")]
    [InlineData("MAIN", "F := TUtil.Pub", null)]
    [InlineData("MAIN", "F := @TUtil.Pub", null)]
    [InlineData("MAIN", "F := TDer.Prot", null)]                                // through the descendant this unit declares (C-260)
    [InlineData("MAIN", "F := TDer.Priv", "13|private|Priv")]
    [InlineData("MAIN", "F := TLocal.LPriv", null)]                             // private: the same unit
    [InlineData("MAIN", "F := TLocal.LSPriv", "15|strict private|LSPriv")]      // strict private: outside the class, even in its unit
    // @ where no procedural type is expected
    [InlineData("MAIN", "P := @TUtil.SPriv", "15|strict private|SPriv")]
    [InlineData("MAIN", "P := @TUtil.Priv", "15|private|Priv")]
    [InlineData("MAIN", "P := @TUtil.Prot", "15|protected|Prot")]
    [InlineData("MAIN", "P := @TUtil.Pub", null)]
    [InlineData("MAIN", "P := @TDer.Prot", null)]
    // a method pointer
    [InlineData("MAIN", "M := O.SPrivI", "10|strict private|SPrivI")]
    [InlineData("MAIN", "M := O.PrivI", "10|private|PrivI")]
    [InlineData("MAIN", "M := O.ProtI", "10|protected|ProtI")]
    [InlineData("MAIN", "M := @O.SPrivI", "11|strict private|SPrivI")]
    [InlineData("MAIN", "M := TUtil.SPrivC", "14|strict private|SPrivC")]
    [InlineData("MAIN", "N := ApplyM(O.PrivI, 1)", "17|private|PrivI")]
    [InlineData("MAIN", "M := O.PubI", null)]
    // a method reference: from a class static method and from a method of an instance
    [InlineData("MAIN", "R := TUtil.SPriv", "14|strict private|SPriv")]
    [InlineData("MAIN", "R := TUtil.Priv", "14|private|Priv")]
    [InlineData("MAIN", "R := @TUtil.SPriv", "15|strict private|SPriv")]
    [InlineData("MAIN", "R := O.SPrivI", "10|strict private|SPrivI")]
    [InlineData("MAIN", "R := O.PrivI", "10|private|PrivI")]
    [InlineData("MAIN", "N := ApplyR(TUtil.SPriv, 1)", "21|strict private|SPriv")]
    [InlineData("MAIN", "N := ApplyR(O.PrivI, 1)", "17|private|PrivI")]
    [InlineData("MAIN", "R := TUtil.Pub", null)]
    [InlineData("MAIN", "R := O.PubI", null)]
    // an inherited method named inside a descendant of another unit: (strict) protected is open, (strict) private is not
    [InlineData("RUN", "F := SPriv", "8|strict private|SPriv")]
    [InlineData("RUN", "F := Priv", "8|private|Priv")]
    [InlineData("RUN", "F := @SPriv", "9|strict private|SPriv")]
    [InlineData("RUN", "F := SProt", null)]
    [InlineData("RUN", "F := Prot", null)]
    [InlineData("RUN", "P := @SPriv", "9|strict private|SPriv")]
    [InlineData("RUN", "P := @Priv", "9|private|Priv")]
    [InlineData("RUN", "P := @SProt", null)]
    [InlineData("RUN", "M := SPrivI", "8|strict private|SPrivI")]
    [InlineData("RUN", "M := PrivI", "8|private|PrivI")]
    [InlineData("RUN", "M := SProtI", null)]
    [InlineData("RUN", "R := SPriv", "8|strict private|SPriv")]
    [InlineData("RUN", "R := Priv", "8|private|Priv")]
    [InlineData("RUN", "R := SPrivI", "8|strict private|SPrivI")]
    [InlineData("RUN", "R := SProt", null)]
    [InlineData("RUN", "R := SProtI", null)]
    [InlineData("RUN", "N := Apply(SPriv, 1)", "14|strict private|SPriv")]
    [InlineData("CRUN", "F := SPriv", "8|strict private|SPriv")]
    [InlineData("CRUN", "P := @Priv", "9|private|Priv")]
    [InlineData("CRUN", "R := Priv", "8|private|Priv")]
    [InlineData("CRUN", "F := Prot", null)]
    // named inside a with statement: the receiver is the designator's type (LANG-375)
    [InlineData("MAIN", "with O do F := Prot", "18|protected|Prot")]
    [InlineData("MAIN", "with O do P := @Prot", "19|protected|Prot")]
    [InlineData("MAIN", "with O do M := PrivI", "18|private|PrivI")]
    [InlineData("MAIN", "with O do R := Priv", "18|private|Priv")]
    [InlineData("MAIN", "with TDer(O) do F := Prot", null)]
    [InlineData("MAIN", "with TDer(O) do P := @Prot", null)]
    [InlineData("MAIN", "with TDer(O) do R := Prot", null)]
    // instances of generic methods (LANG-981): the same check in every form
    [InlineData("MAIN", "F := TUtil.Secret<Integer>", "14|strict private|Secret")]
    [InlineData("MAIN", "F := @TUtil.Secret<Integer>", "15|strict private|Secret")]
    [InlineData("MAIN", "P := @TUtil.Secret<Integer>", "15|strict private|Secret")]
    [InlineData("MAIN", "R := TUtil.Secret<Integer>", "14|strict private|Secret")]
    [InlineData("MAIN", "N := Apply(TUtil.Secret<Integer>, 1)", "20|strict private|Secret")]
    [InlineData("MAIN", "N := ApplyR(TUtil.Secret<Integer>, 1)", "21|strict private|Secret")]
    [InlineData("RUN", "F := Secret<Integer>", "8|strict private|Secret")]
    [InlineData("RUN", "R := Secret<Integer>", "8|strict private|Secret")]
    [InlineData("MAIN", "F := TUtil.Open<Integer>", null)]
    [InlineData("MAIN", "R := TUtil.Open<Integer>", null)]
    public void MethodValueChecksVisibility(string where, string statement, string? expected)
    {
        var program = ProgramP;
        foreach (var marker in new[] { "RUN", "CRUN", "MAIN" })
        {
            program = program.Replace("{" + marker + "}", marker == where ? statement : "", StringComparison.Ordinal);
        }

        var line = ProgramP[..ProgramP.IndexOf("{" + where + "}", StringComparison.Ordinal)].Count(c => c == '\n') + 1;
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\U.pas", UnitU).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        string[] wanted = [];
        if (expected is not null)
        {
            var parts = expected.Split('|');
            var owner = parts[2].StartsWith('L') ? "TLocal" : "TUtil";
            wanted = [$"P.pas({line},{parts[0]}): E2610 Cannot access {parts[1]} member '{parts[2]}' of '{owner}'"];
        }

        Assert.Equal(wanted, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}"));
    }
}
