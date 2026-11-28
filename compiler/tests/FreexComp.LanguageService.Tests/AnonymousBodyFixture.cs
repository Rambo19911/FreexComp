namespace FreexComp.LanguageService.Tests;

/// <summary>
/// generics-closures-completion.md §3.3 (CMP-1473, C-415): a method with a parameter, classic locals, an inline variable declared
/// before and one declared after an anonymous procedure; the procedure has a local and a nested anonymous function whose parameter
/// hides a local of the method; a second anonymous function uses the captured local, a field and calls the captured method
/// reference. A method of a generic that is never instantiated and the main block each have an anonymous method too.
/// <c>{@name}</c> marks a caret position (a comment for every other reader).
/// </summary>
internal static class AnonymousBodyFixture
{
    public const string Program = """
        program P;
        type
          TProc = reference to procedure;
          TIntFunc = reference to function(Delta: Integer): Integer;
          TCounter = class
          private
            FName: string;
          public
            procedure Bump;
            function MakeAdder(Start: Integer): TIntFunc;
          end;
          TBox<T> = class
            Value: T;
            procedure Run(Seed: T);
          end;

        procedure TCounter.Bump;
        begin
        end;

        function TCounter.MakeAdder(Start: Integer): TIntFunc;
        var
          Count: Integer;
          Log: TProc;
        begin
          Count := 0;
          var Before := 1;
          Log := procedure
            var Note: Integer;
            begin
              Note := Count + Start + Before;
              {@procedure}
              var Pick: TIntFunc := function(Count: Integer): Integer
                var Local: Integer;
                begin
                  Local := Count + Note;
                  {@nested}
                  Result := Local + Start;
                end;
              Count := Pick(Note);
            end;
          var After := 2;
          Log();
          Result := function(Delta: Integer): Integer
            begin
              Count := Count + Delta;
              Log();
              {@function}
              Result := Count + After + Length(FName);
            end;
        end;

        procedure TBox<T>.Run(Seed: T);
        var
          Keep: T;
          Later: TProc;
        begin
          Later := procedure
            var Mine: T;
            begin
              Mine := Seed;
              {@generic}
              Keep := Mine;
            end;
          Later();
        end;

        var
          C: TCounter;
          F: TIntFunc;
        begin
          C := TCounter.Create;
          var Outer := 5;
          F := function(Step: Integer): Integer
            begin
              {@main}
              Result := Step + Outer;
            end;
          WriteLn(F(2));
          F := C.MakeAdder(1);
          WriteLn(F(2));
          C.Free;
        end.
        """;

    public static string Text => Program.ReplaceLineEndings("\n");

    public static (string Name, string Text)[] Files => [("P.pas", Text)];

    public static Document Open() => MarkupTestFile.Open("P.pas", Files);

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
