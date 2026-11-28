using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-454: a write through a property whose <c>write</c> accessor is a field (LANG-641) is checked like the field written directly
/// (LANG-350, LANG-351, LANG-361, LANG-344, LANG-375, LANG-645): the same diagnostic at the same place.
/// </summary>
[Trait("Phase", "8")]
public sealed class AssignmentTargetTests
{
    private const string Prelude = """
        program T;
        type
          TPt = record X: Integer end;
          TIn = record
            FX: Integer;
            property PX: Integer read FX write FX;
          end;
          TRec = record
            FX: Integer;
            FPt: TPt;
            FInner: TIn;
            procedure SetM(V: Integer);
            function GetItem(I: Integer): TIn;
            property PX: Integer read FX write FX;
            property PY: Integer read FPt.X write FPt.X;
            property Inner: TIn read FInner write FInner;
            property PM: Integer read FX write SetM;
            property Items[I: Integer]: TIn read GetItem;
          end;
          TObj = class
            FX: Integer;
            property PX: Integer read FX write FX;
          end;
        const C: TRec = (FX: 1);
        var A: array of TRec;
        procedure TRec.SetM(V: Integer); begin PX := V; Inner.PX := V end;
        function TRec.GetItem(I: Integer): TIn; begin Result := FInner end;

        """;

    private static string[] Diagnostics(string code, string target) =>
        BindDiagnostics(Prelude + code.Replace("$T", target) + "\nbegin end.");

    /// <summary>
    /// Each row writes the property (<paramref name="property"/>) and the field it names (<paramref name="field"/>) in the same
    /// place: both give one and the same E2053 (before C-454 the property form compiled and changed the read-only value).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-641")]
    [Trait("Req", "LANG-350")]
    [InlineData("procedure P(const R: TRec); begin $T := 5 end;", "R.PX", "R.FX")]                       // const parameter
    [InlineData("procedure P; begin $T := 5 end;", "C.PX", "C.FX")]                                       // typed constant (LANG-344)
    [InlineData("procedure P; var R: TRec; begin for R in A do $T := 5 end;", "R.PX", "R.FX")]            // for-in variable (LANG-362)
    [InlineData("procedure P(const R: TRec); begin with R do $T := 5 end;", "PX", "FX")]                  // read-only with target (C-198)
    [InlineData("procedure P; begin with C do $T := 5 end;", "PX", "FX")]                                 // with a typed constant
    [InlineData("procedure P(const R: TRec); begin $T := 5 end;", "R.Inner.PX", "R.FInner.FX")]           // nested properties
    [InlineData("procedure P(const R: TRec); begin $T := 5 end;", "R.PY", "R.FPt.X")]                     // field path (read FPt.X)
    [InlineData("procedure P(const Q: array of TRec); begin $T := 5 end;", "Q[0].PX", "Q[0].FX")]         // element of a const open array
    [InlineData("procedure P(var R: TRec); begin $T := 5 end;", "R.Items[0].PX", "R.Items[0].FX")]        // indexed property read by a getter (LANG-645)
    [InlineData("procedure P(const R: TRec); var I: Integer; begin for I := 0 to 1 do $T := 5 end;", "R.Inner.PX", "R.FInner.FX")]
    public void PropertyWithFieldAccessor_ReadOnlyTarget(string code, string property, string field)
    {
        var direct = Diagnostics(code, field);
        Assert.Single(direct);
        Assert.StartsWith("E2053(", direct[0]);
        Assert.Equal(direct, Diagnostics(code, property));
    }

    /// <summary>Writes that stay allowed: a writable root, a method write accessor (decision 3), a class instance, Self in a method.</summary>
    [Theory]
    [Trait("Req", "LANG-641")]
    [InlineData("procedure P(var R: TRec); begin R.PX := 5; R.PY := 5; R.Inner.PX := 5 end;")]
    [InlineData("procedure P; var R: TRec; begin R.PX := 5; A[0].PX := 5 end;")]
    [InlineData("procedure P(const R: TRec); begin R.PM := 5 end;")]
    [InlineData("procedure P; begin C.PM := 5 end;")]
    [InlineData("procedure P(const O: TObj); begin O.PX := 5; with O do PX := 6 end;")]
    [InlineData("procedure P; var O: TObj; L: array of TObj; begin for O in L do O.PX := 5 end;")]
    public void PropertyWithFieldAccessor_WritableTarget(string code) =>
        Assert.Empty(BindDiagnostics(Prelude + code + "\nbegin end."));
}
