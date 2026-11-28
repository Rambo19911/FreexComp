namespace FreexComp.LanguageService.Tests;

/// <summary>
/// CMP-1492…1495 (C-446; phase8/helpers-completion.md §9): the helpers the language-service tests of helper members share — a class
/// helper of <c>TCounter</c> (used on its descendant <c>TChild</c>) with a hidden member, a nested type, a constant, a class var, a
/// class constructor, a property and an overload, and record helpers for <c>Char</c>, <c>string</c>, <c>Integer</c> and <c>Double</c>.
/// </summary>
internal static class HelpersFixture
{
    public const string Unit = """
        unit UH;
        interface
        type
          TCounter = class
            N: Integer;
            procedure Bump;
            procedure Run;
            procedure Put(A: Integer); overload;
            class function Zero: Integer;
          end;
          TChild = class(TCounter)
            procedure Work;
          end;
          TCounterClass = class of TCounter;
          TCounterHelper = class helper for TCounter
          strict private
            procedure Hidden;
          public
            type
              TKind = (kOne, kTwo);
            const
              Limit = 10;
            class var
              Count: Integer;
            class constructor Init;
            procedure Show;
            procedure Bump;
            procedure Put(const S: string); overload;
            function GetSize: Integer;
            class function Make: TCounter;
            property Size: Integer read GetSize;
          end;
          TCharH = record helper for Char
            function IsDigit: Boolean;
            function Len: Integer;
          end;
          TStrH = record helper for string
            function Len: Integer;
            function Upper: string;
            class function Join(const A, B: string): string; static;
          end;
          TIntH = record helper for Integer
            function IsEven: Boolean;
            function Plus(A: Integer): Integer; overload;
            function Plus(A, B: Integer): Integer; overload;
          end;
          TDblH = record helper for Double
            function Half: Double;
          end;
        const
          K = 5;
          Ch = 'x';
        implementation
        procedure TCounter.Bump; begin end;
        procedure TCounter.Run; begin end;
        procedure TCounter.Put(A: Integer); begin end;
        class function TCounter.Zero: Integer; begin Result := 0 end;
        procedure TChild.Work;
        begin
          Show;
        end;
        procedure TCounterHelper.Hidden; begin end;
        class constructor TCounterHelper.Init; begin Count := 0 end;
        procedure TCounterHelper.Show; begin Hidden end;
        procedure TCounterHelper.Bump; begin end;
        procedure TCounterHelper.Put(const S: string); begin end;
        function TCounterHelper.GetSize: Integer; begin Result := N end;
        class function TCounterHelper.Make: TCounter; begin Result := TCounter.Create end;
        function TCharH.IsDigit: Boolean; begin Result := (Self >= '0') and (Self <= '9') end;
        function TCharH.Len: Integer; begin Result := 1 end;
        function TStrH.Len: Integer; begin Result := Length(Self) end;
        function TStrH.Upper: string; begin Result := Self end;
        class function TStrH.Join(const A, B: string): string; begin Result := A + B end;
        function TIntH.IsEven: Boolean; begin Result := not Odd(Self) end;
        function TIntH.Plus(A: Integer): Integer; begin Result := Self + A end;
        function TIntH.Plus(A, B: Integer): Integer; begin Result := Self + A + B end;
        function TDblH.Half: Double; begin Result := Self / 2 end;
        end.
        """;

    /// <summary>A program that uses <c>UH</c>, with <paramref name="body"/> as its statements.</summary>
    public static string Program(string body) =>
        "program P;\nuses UH;\nvar\n  D: TChild;\n  R: TCounterClass;\n  S: string;\n  B: Boolean;\n  X: Integer;\n  E: Double;\nbegin\n" + body + "\nend.";

    public static (string Name, string Text) UnitFile => ("UH.pas", Unit.ReplaceLineEndings("\n"));
}
