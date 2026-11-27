namespace FreexComp.LanguageService.Tests;

/// <summary>
/// inline-variables.md §12 (C-385, CMP-1413, 1414): a program with inferred inline and <c>for var</c> variables, declarations with a
/// written type, an inline constant and two declarations rejected with E2075.
/// </summary>
internal static class InlineInferenceFixture
{
    public const string Program = """
        program P;
        type
          TBox<T> = class
            Value: T;
          end;
        var
          U: Integer;
          Bytes: array of Byte;
        procedure Run;
        begin
          var N := 5;
          var L := TBox<Integer>.Create;
          var W: Integer := 7;
          for var I := 0 to High(U) do
            N := N + I;
          for var B in Bytes do
            W := W + B;
          for var J: Integer := 1 to 2 do
            W := W + J;
          for var C: Byte in Bytes do
            W := W + C;
          const K = 3;
          L.Free;
          WriteLn(N + W + K);
        end;
        procedure Rejected;
        begin
          var X := nil;
          var A := [1, 2];
          X := A;
        end;
        begin
          Run;
          Rejected;
        end.
        """;

    public static string Text => Program.ReplaceLineEndings("\n");

    public static Document Open() => MarkupTestFile.Open("P.pas", ("P.pas", Text));

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="piece"/> in <see cref="Text"/>.</summary>
    public static int Offset(string piece, int occurrence = 1)
    {
        var at = -1;
        for (var i = 0; i < occurrence; i++)
        {
            at = Text.IndexOf(piece, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, piece);
        }

        return at;
    }
}
