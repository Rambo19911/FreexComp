// The diagnostics of an in-memory compilation are read synchronously; the optional token is left out on purpose.
#pragma warning disable xUnit1051

namespace FreexComp.Compiler.Tests.Binding;

// LANG-996: receivers of record-helper methods (phase8/helpers-completion.md §15.6; card F10-028), C-443 — which methods are
// mutating (b), the kinds of receiver (a) and E2752 for a mutating method on a read-only value (c).
public sealed partial class HelperBindingTests
{
    // ------------------------------------------------------------------ LANG-996 (b): the classification from the body

    /// <summary>
    /// The helper <c>TH</c> for <c>$TYPE$</c> with the method under test <c>M</c> (<c>$M$</c> is its whole implementation), a
    /// mutating method declared and implemented after it (<c>Later</c>), a reading one (<c>Peek</c>) and a property over each;
    /// <c>Use</c> calls <c>M</c> on a <c>const</c> parameter — E2752 exactly when <c>M</c> is mutating.
    /// </summary>
    private const string ClassificationProgram = """
        program P;
        type
          TBox = class
            Value: Integer;
          end;
          TInner = record
            A: Integer;
          end;
          TRec = record
            X, Y: Integer;
            Inner: TInner;
            Items: array[0..2] of Integer;
            Dyn: array of Integer;
            Obj: TBox;
            Ptr: ^Integer;
            S: string;
            procedure Reset;
          end;
          PRec = ^TRec;
          TColor = (Red, Green, Blue);
          TColors = set of TColor;
          TTriple = array[0..2] of Integer;
          TInts = array of Integer;
          TName = string[10];
          TIntH = record helper for Integer
            procedure BumpInt;
            function Twice: Integer;
          end;
          TH = record helper for $TYPE$
            procedure M;
            procedure Later;
            function Peek: Integer;
            function Take: Integer;
            property Next: Integer read Take;
            property Seen: Integer read Peek;
          end;
        procedure TRec.Reset;
        begin
          X := 0;
        end;
        procedure TIntH.BumpInt;
        begin
          Inc(Self);
        end;
        function TIntH.Twice: Integer;
        begin
          Result := Self * 2;
        end;
        procedure ByVar(var N: Integer);
        begin
          N := N + 1;
        end;
        procedure ByOut(out N: Integer);
        begin
          N := 0;
        end;
        procedure ByValue(N: Integer);
        begin
          if N > 0 then
            Writeln(N);
        end;
        procedure ByConst(const N: Integer);
        begin
          if N > 0 then
            Writeln(N);
        end;
        procedure Untyped(var X);
        begin
          FillChar(X, 1, 0);
        end;
        procedure UntypedConst(const X);
        begin
          if @X = nil then
            Writeln;
        end;
        $M$
        procedure TH.Later;
        var
          Old: $TYPE$;
        begin
          Old := Self;
          Self := Old;
        end;
        function TH.Peek: Integer;
        begin
          Result := SizeOf(Self);
        end;
        function TH.Take: Integer;
        begin
          Later;
          Result := 1;
        end;
        procedure Use(const K: $TYPE$);
        begin
          K.M$MARK$;
          if K.Peek + K.Seen > 0 then
            Writeln;
        end;
        begin
        end.
        """;

    /// <summary>The rows of <see cref="Mutating_Classification"/>: what, the helped type, the implementation of <c>TH.M</c>, mutating.</summary>
    public static TheoryData<string, string, string, bool> ClassificationRows() => new()
    {
        // rule 1: a part of Self is the target of an assignment
        { "1: Self := of a scalar", "Integer", "procedure TH.M; begin Self := 1 end;", true },
        { "1: a field without Self.", "TRec", "procedure TH.M; begin X := X + 1 end;", true },
        { "1: a field with Self.", "TRec", "procedure TH.M; begin Self.Y := 2 end;", true },
        { "1: a field of a record field", "TRec", "procedure TH.M; begin Inner.A := 1 end;", true },
        { "1: an element of a static-array field", "TRec", "procedure TH.M; begin Items[1] := 1 end;", true },
        { "1: an element of a static array", "TTriple", "procedure TH.M; begin Self[1] := 0 end;", true },
        { "1: an element of a string", "string", "procedure TH.M; begin Self[1] := 'x' end;", true },
        { "1: an element of a short string", "TName", "procedure TH.M; begin Self[1] := 'x' end;", true },
        { "1: a dynamic-array field itself", "TRec", "procedure TH.M; begin Dyn := nil end;", true },
        { "1: a variable typecast of Self", "Cardinal", "procedure TH.M; begin Integer(Self) := 1 end;", true },
        { "1, no part: an element of a dynamic array", "TInts", "procedure TH.M; begin Self[High(Self)] := 0 end;", false },
        { "1, no part: behind a dereference", "PRec", "procedure TH.M; begin Self^.X := 1 end;", false },
        { "1, no part: a field of an object field", "TRec", "procedure TH.M; begin Obj.Value := 1 end;", false },
        { "1, no part: an element of a dynamic-array field", "TRec", "procedure TH.M; begin Dyn[0] := 1 end;", false },
        { "1, no part: behind a pointer field", "TRec", "procedure TH.M; begin Ptr^ := 1 end;", false },

        // rule 2: var / out / untyped var arguments and the operands that need a variable reference
        { "2: Inc", "Integer", "procedure TH.M; begin Inc(Self) end;", true },
        { "2: Dec", "Integer", "procedure TH.M; begin Dec(Self, 2) end;", true },
        { "2: Include", "TColors", "procedure TH.M; begin Include(Self, Red) end;", true },
        { "2: Exclude", "TColors", "procedure TH.M; begin Exclude(Self, Red) end;", true },
        { "2: SetLength of a dynamic array", "TInts", "procedure TH.M; begin SetLength(Self, 1) end;", true },
        { "2: SetLength of a string", "string", "procedure TH.M; begin SetLength(Self, 1) end;", true },
        { "2: SetLength of a short string", "TName", "procedure TH.M; begin SetLength(Self, 1) end;", true },
        { "2: SetLength of a string field", "TRec", "procedure TH.M; begin SetLength(S, 1) end;", true },
        { "2: Delete", "string", "procedure TH.M; begin Delete(Self, 1, 1) end;", true },
        { "2: Insert", "string", "procedure TH.M; begin Insert('a', Self, 1) end;", true },
        { "2: Val", "Integer", "procedure TH.M; var Code: Integer; begin Val('1', Self, Code); if Code > 0 then Writeln end;", true },
        { "2: Str", "string", "procedure TH.M; begin Str(1, Self) end;", true },
        { "2: Readln", "Integer", "procedure TH.M; begin Readln(Self) end;", true },
        { "2: New", "PRec", "procedure TH.M; begin New(Self) end;", true },
        { "2: FillChar", "TRec", "procedure TH.M; begin FillChar(Self, SizeOf(Self), 0) end;", true },
        { "2: a var argument", "Integer", "procedure TH.M; begin ByVar(Self) end;", true },
        { "2: an out argument", "Integer", "procedure TH.M; begin ByOut(Self) end;", true },
        { "2: an untyped var argument", "TRec", "procedure TH.M; begin Untyped(Self) end;", true },
        { "2: a field as var argument", "TRec", "procedure TH.M; begin ByVar(X) end;", true },
        { "2, no write: a value argument", "Integer", "procedure TH.M; begin ByValue(Self) end;", false },
        { "2, no write: a const argument", "Integer", "procedure TH.M; begin ByConst(Self) end;", false },
        { "2, no write: an untyped const argument", "TRec", "procedure TH.M; begin UntypedConst(Self) end;", false },
        { "2, no part: Inc behind a pointer field", "TRec", "procedure TH.M; begin Inc(Ptr^) end;", false },

        // rule 3: the operand of @ or Addr
        { "3: @Self", "Integer", "procedure TH.M; var Q: Pointer; begin Q := @Self; if Q = nil then Writeln end;", true },
        { "3: Addr(Self)", "Integer", "procedure TH.M; var Q: Pointer; begin Q := Addr(Self); if Q = nil then Writeln end;", true },
        { "3: @ of a field", "TRec", "procedure TH.M; var Q: Pointer; begin Q := @X; if Q = nil then Writeln end;", true },
        { "3: @ of an element", "TTriple", "procedure TH.M; var Q: Pointer; begin Q := @Self[1]; if Q = nil then Writeln end;", true },
        { "3, no part: @ behind a dereference", "PRec", "procedure TH.M; var Q: Pointer; begin Q := @Self^.X; if Q = nil then Writeln end;", false },
        { "3, no part: @ of a field of an object field", "TRec", "procedure TH.M; var Q: Pointer; begin Q := @Obj.Value; if Q = nil then Writeln end;", false },

        // rule 4: the receiver of a mutating record-helper method
        { "4: a mutating method declared later, implicit Self", "Integer", "procedure TH.M; begin Later end;", true },
        { "4: a mutating method on Self.", "Integer", "procedure TH.M; begin Self.Later end;", true },
        { "4: a field as receiver", "TRec", "procedure TH.M; begin X.BumpInt end;", true },
        { "4: an element of a static-array field as receiver", "TRec", "procedure TH.M; begin Items[0].BumpInt end;", true },
        { "4: a property whose accessor is mutating", "Integer", "procedure TH.M; begin if Next > 0 then Writeln end;", true },
        { "4, no write: a method that is not mutating", "Integer", "procedure TH.M; begin if Peek > 0 then Writeln end;", false },
        { "4, no write: a property whose accessor is not mutating", "Integer", "procedure TH.M; begin if Seen > 0 then Writeln end;", false },
        { "4, no write: a reading method on a field", "TRec", "procedure TH.M; begin if X.Twice > 0 then Writeln end;", false },
        { "4, no write: a method of the record itself", "TRec", "procedure TH.M; begin Reset end;", false },
        { "4, no part: an element of a dynamic-array field as receiver", "TRec", "procedure TH.M; begin Dyn[0].BumpInt end;", false },
        { "4, no part: a receiver behind a dereference", "PRec", "procedure TH.M; begin Self^.X.BumpInt end;", false },

        // rule 5: the designator of a with statement
        { "5: with Self", "TRec", "procedure TH.M; begin with Self do if X > 0 then Writeln end;", true },
        { "5: with a record field", "TRec", "procedure TH.M; begin with Inner do if A > 0 then Writeln end;", true },
        { "5: with an object field (the reference is a field of Self)", "TRec", "procedure TH.M; begin with Obj do if Value > 0 then Writeln end;", true },
        { "5, no part: with behind a dereference", "PRec", "procedure TH.M; begin with Self^ do X := 1 end;", false },

        // routines nested in the method; reading
        { "nested: a nested routine assigns Self", "Integer", "procedure TH.M; procedure Deep; begin Self := 2 end; begin Deep end;", true },
        { "nested: two levels down, through a mutating method", "Integer",
            "procedure TH.M; procedure Outer; procedure Deep; begin Later end; begin Deep end; begin Outer end;", true },
        { "nested: a nested routine reads Self", "Integer",
            "procedure TH.M; function Deep: Integer; begin Result := Self end; begin if Deep > 0 then Writeln end;", false },
        { "reading: Self in an expression", "Integer", "procedure TH.M; var L: Integer; begin L := Self + 1; ByValue(L) end;", false },
        { "reading: a field, an element, the length", "TRec", "procedure TH.M; begin ByValue(X + Items[0] + Length(Dyn) + Length(S)) end;", false },
    };

    /// <summary>
    /// LANG-996 (b): an instance method of a record helper is mutating when its body — the routines nested in it included —
    /// holds a write use of a part of <c>Self</c>: (1) the target of an assignment, (2) a <c>var</c> / <c>out</c> / untyped
    /// <c>var</c> argument or an operand that must be a variable reference, (3) the operand of <c>@</c> / <c>Addr</c>, (4) the
    /// receiver of a mutating record-helper method, (5) the designator of <c>with</c>. A part is <c>Self</c> or a path through
    /// record fields, elements of static arrays and of strings; what lies behind a dereference, an object reference or an element
    /// of a dynamic array is other storage. Reading, value and <c>const</c> arguments and calls of methods that are not mutating
    /// (also of the record itself) are no write uses.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassificationRows))]
    [Trait("Req", "LANG-996")]
    public void Mutating_Classification(string what, string type, string method, bool mutating)
    {
        _ = what;
        AssertMarkers(
        [
            ("P.pas", ClassificationProgram.ReplaceLineEndings("\n")
                .Replace("$TYPE$", type, StringComparison.Ordinal)
                .Replace("$M$", method, StringComparison.Ordinal)
                .Replace("$MARK$", mutating ? "{!E2752}" : "", StringComparison.Ordinal)),
        ]);
    }

    private const string UPoke = """
        unit UPoke;
        interface
        type
          TPokeH = record helper for Integer
            procedure Chain;
            function Twice: Integer;
            procedure Bump;
          end;
        implementation
        procedure TPokeH.Chain;
        begin
          Bump;
        end;
        function TPokeH.Twice: Integer;
        begin
          Result := Self * 2;
        end;
        procedure TPokeH.Bump;
        begin
          Inc(Self);
        end;
        end.
        """;

    /// <summary>The rows of <see cref="Mutating_Classification_AcrossBodiesAndUnits"/> (files with <c>{!E2752}</c> markers).</summary>
    private static readonly Dictionary<string, (string File, string Text)[]> ClassificationCases = new()
    {
        ["mutual recursion without a write"] =
        [
            ("P.pas", """
                program P;
                type
                  TH = record helper for Integer
                    function Even(N: Integer): Boolean;
                    function Odd(N: Integer): Boolean;
                  end;
                function TH.Even(N: Integer): Boolean;
                begin
                  Result := (N = 0) or Self.Odd(N - 1);
                end;
                function TH.Odd(N: Integer): Boolean;
                begin
                  Result := (N <> 0) and Self.Even(N - 1);
                end;
                procedure Use(const K: Integer);
                begin
                  if K.Even(2) or K.Odd(3) then
                    Writeln;
                end;
                begin
                end.
                """),
        ],
        ["mutual recursion with a write in one of the two"] =
        [
            ("P.pas", """
                program P;
                type
                  TH = record helper for Integer
                    function Even(N: Integer): Boolean;
                    function Odd(N: Integer): Boolean;
                    function Peek: Integer;
                  end;
                function TH.Even(N: Integer): Boolean;
                begin
                  Result := (N = 0) or Self.Odd(N - 1);
                end;
                function TH.Odd(N: Integer): Boolean;
                begin
                  Dec(Self);
                  Result := (N <> 0) and Self.Even(N - 1);
                end;
                function TH.Peek: Integer;
                begin
                  Result := Self;
                end;
                procedure Use(const K: Integer);
                begin
                  if K.Even{!E2752}(2) or K.Odd{!E2752}(3) or (K.Peek > 0) then
                    Writeln;
                end;
                begin
                end.
                """),
        ],
        ["a method of another unit, classified through a body that comes later there"] =
        [
            ("UPoke.pas", UPoke),
            ("P.pas", """
                program P;
                uses UPoke;
                type
                  TRec = record
                    N: Integer;
                  end;
                  TRecH = record helper for TRec
                    procedure Through;
                    function Reads: Integer;
                  end;
                procedure TRecH.Through;
                begin
                  N.Chain;
                end;
                function TRecH.Reads: Integer;
                begin
                  Result := N.Twice;
                end;
                procedure Use(const K: Integer; const R: TRec);
                begin
                  K.Chain{!E2752};
                  K.Bump{!E2752};
                  R.Through{!E2752};
                  if K.Twice + R.Reads > 0 then
                    Writeln;
                end;
                begin
                end.
                """),
        ],
        ["an implementation-uses cycle: UOne.Go -> UTwo.Poke, UTwo.Run -> UOne.Go"] =
        [
            ("UOne.pas", """
                unit UOne;
                interface
                type
                  TOne = record
                    V: Integer;
                  end;
                  TOneH = record helper for TOne
                    procedure Go;
                    function Count: Integer;
                  end;
                implementation
                uses UTwo;
                procedure TOneH.Go;
                begin
                  V.Poke;
                end;
                function TOneH.Count: Integer;
                begin
                  Result := V.Seen;
                end;
                end.
                """),
            ("UTwo.pas", """
                unit UTwo;
                interface
                uses UOne;
                type
                  TTwo = record
                    One: TOne;
                  end;
                  TTwoH = record helper for TTwo
                    procedure Run;
                    function Total: Integer;
                  end;
                  TIntH = record helper for Integer
                    procedure Poke;
                    function Seen: Integer;
                  end;
                implementation
                procedure TTwoH.Run;
                begin
                  One.Go;
                end;
                function TTwoH.Total: Integer;
                begin
                  Result := One.Count;
                end;
                procedure TIntH.Poke;
                begin
                  Inc(Self);
                end;
                function TIntH.Seen: Integer;
                begin
                  Result := Self;
                end;
                end.
                """),
            ("P.pas", """
                program P;
                uses UOne, UTwo;
                procedure Use(const A: TOne; const B: TTwo);
                begin
                  A.Go{!E2752};
                  B.Run{!E2752};
                  if A.Count + B.Total > 0 then
                    Writeln;
                end;
                begin
                end.
                """),
        ],
    };

    public static TheoryData<string> ClassificationCaseRows => [.. ClassificationCases.Keys];

    /// <summary>
    /// LANG-996 (b), last paragraph: the mutating methods are the smallest set closed under the five rules — mutual recursion
    /// without a write stays reading — and the result depends neither on the order of the method bodies nor on the unit a called
    /// method lives in, also across an implementation-uses cycle.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClassificationCaseRows))]
    [Trait("Req", "LANG-996")]
    public void Mutating_Classification_AcrossBodiesAndUnits(string row) => AssertMarkers(ClassificationCases[row]);

    // ------------------------------------------------------------------ LANG-996 (a), (c): the kinds of receiver and E2752

    /// <summary>
    /// A helper for <c>Integer</c> with a reading method (<c>Squared</c>), a mutating one (<c>Bump</c>) and a property over each
    /// (<c>Sq</c>, <c>Next</c>), helpers for a string, an enumeration, a function type without parameters and a class-reference
    /// type; <c>$S$</c> is the statement under test inside <c>Use</c>.
    /// </summary>
    private const string ReceiverProgram = """
        program P;
        type
          TPoint = record
            X, Y: Integer;
          end;
          TInts = array of Integer;
          TColor = (Red, Green, Blue);
          TGet = function: Integer;
          TObjClass = class of TObject;
          TBox = class
            F: Integer;
            function Get: Integer;
            property Value: Integer read F;
            property Computed: Integer read Get;
          end;
          TIntH = record helper for Integer
            function Squared: Integer;
            procedure Bump;
            function Take: Integer;
            property Next: Integer read Take;
            property Sq: Integer read Squared;
          end;
          TStrH = record helper for string
            function Len: Integer;
            procedure Append(const Suffix: string);
          end;
          TColorH = record helper for TColor
            function Name: string;
            procedure Paint;
          end;
          TGetH = record helper for TGet
            function Twice: Integer;
            procedure Clear;
          end;
          TObjClassH = record helper for TObjClass
            function Depth: Integer;
            procedure Reset;
          end;
        function TBox.Get: Integer;
        begin
          Result := F;
        end;
        function TIntH.Squared: Integer;
        begin
          Result := Self * Self;
        end;
        procedure TIntH.Bump;
        begin
          Inc(Self);
        end;
        function TIntH.Take: Integer;
        begin
          Result := Self;
          Bump;
        end;
        function TStrH.Len: Integer;
        begin
          Result := Length(Self);
        end;
        procedure TStrH.Append(const Suffix: string);
        begin
          Self := Self + Suffix;
        end;
        function TColorH.Name: string;
        begin
          if Self = Red then
            Result := 'red'
          else
            Result := 'other';
        end;
        procedure TColorH.Paint;
        begin
          Self := Blue;
        end;
        function TGetH.Twice: Integer;
        begin
          Result := Self() + Self();
        end;
        procedure TGetH.Clear;
        begin
          Self := nil;
        end;
        function TObjClassH.Depth: Integer;
        begin
          if Self = nil then
            Result := 0
          else
            Result := 1;
        end;
        procedure TObjClassH.Reset;
        begin
          Self := nil;
        end;
        const
          Five = 5;
          Hello = 'hello';
          Typed: Integer = 7;
          Origin: TPoint = (X: 1; Y: 2);
        var
          G: Integer;
          GP: TPoint;
          GS: string;
          Box: TBox;
          Dyn: TInts;
          PInt: ^Integer;
        function Make: Integer;
        begin
          Result := G;
        end;
        function GetSeven: Integer;
        begin
          Result := 7;
        end;
        procedure Use(const C: Integer; const CP: TPoint; const CS: string; const CA: array of Integer; const CG: TGet;
          const CK: TObjClass; var V: Integer; out O: Integer; A: Integer);
        var
          L, I, E: Integer;
          Paint: TColor;
        begin
          L := A;
          O := A;
          Paint := Red;
          for I := 0 to 0 do
            G := G + I;
          for E in CA do
            G := G + E;
          $S$;
          Writeln(L + V + O, Ord(Paint), C, CP.X, CS, Length(CA), CG() + Ord(CK = nil));
        end;
        begin
        end.
        """;

    /// <summary>The rows of <see cref="ReadOnlyReceiver_E2752"/>: the kind of receiver, the statement, the method named by E2752 (null: none).</summary>
    public static TheoryData<string, string, string?> ReceiverRows() => new()
    {
        // read-only locations
        { "read-only: a const parameter, mutating", "C.Bump{!E2752}", "Bump" },
        { "read-only: a const parameter, reading", "G := C.Squared", null },
        { "read-only: a const string parameter, mutating", "CS.Append{!E2752}('x')", "Append" },
        { "read-only: a const string parameter, reading", "G := CS.Len", null },
        { "read-only: a field of a const record parameter, mutating", "CP.X.Bump{!E2752}", "Bump" },
        { "read-only: a field of a const record parameter, reading", "G := CP.X.Squared", null },
        { "read-only: an element of a const open array, mutating", "CA[0].Bump{!E2752}", "Bump" },
        { "read-only: an element of a const open array, reading", "G := CA[0].Squared", null },
        { "read-only: a typed constant, mutating", "Typed.Bump{!E2752}", "Bump" },
        { "read-only: a typed constant, reading", "G := Typed.Squared", null },
        { "read-only: a field of a typed constant, mutating", "Origin.Y.Bump{!E2752}", "Bump" },
        { "read-only: a for loop variable inside the loop, mutating", "for I := 1 to 3 do I.Bump{!E2752}", "Bump" },
        { "read-only: a for loop variable inside the loop, reading", "for I := 1 to 3 do G := I.Squared", null },
        { "read-only: a for-in loop variable inside the loop, mutating", "for E in CA do E.Bump{!E2752}", "Bump" },
        { "read-only: the target of a read-only with, mutating", "with CP do Y.Bump{!E2752}", "Bump" },
        { "read-only: the target of a read-only with, reading", "with Origin do G := Y.Squared", null },
        { "read-only: a property with a mutating accessor", "G := C.Next{!E2752}", "Take" },
        { "read-only: a property with a reading accessor", "G := C.Sq", null },
        { "read-only: a const value of a function type is the receiver, not called", "CG.Clear{!E2752}", "Clear" },
        { "read-only: a const value of a function type, reading", "G := CG.Twice", null },
        { "read-only: a const class-reference value, mutating", "CK.Reset{!E2752}", "Reset" },
        { "read-only: a const class-reference value, reading", "G := CK.Depth", null },
        { "read-only: the declared spelling of the method is reported", "c.bump{!E2752}", "Bump" },

        // constant receivers
        { "constant: a true constant, mutating", "Five.Bump{!E2752}", "Bump" },
        { "constant: a true constant, reading", "G := Five.Squared", null },
        { "constant: a string constant, mutating", "Hello.Append{!E2752}('x')", "Append" },
        { "constant: a string constant, reading", "G := Hello.Len", null },
        { "constant: an enumeration element, mutating", "Green.Paint{!E2752}", "Paint" },
        { "constant: an enumeration element, reading", "GS := Green.Name", null },
        { "constant: a folded expression, mutating", "(Five + 1).Bump{!E2752}", "Bump" },
        { "constant: a property with a mutating accessor", "G := Five.Next{!E2752}", "Take" },

        // locations: the method works on the receiver itself
        { "location: a local variable", "L.Bump", null },
        { "location: a global variable", "G.Bump", null },
        { "location: a var parameter", "V.Bump", null },
        { "location: an out parameter", "O.Bump", null },
        { "location: a value parameter", "A.Bump", null },
        { "location: a field of a variable", "GP.X.Bump", null },
        { "location: an element of a dynamic array", "Dyn[0].Bump", null },
        { "location: a dereference", "PInt^.Bump", null },
        { "location: a field of an object", "Box.F.Bump", null },
        { "location: the target of a with on a variable", "with GP do X.Bump", null },
        { "location: a for loop variable after its loop", "begin I := 5; I.Bump; G := I end", null },
        { "location: a property with a mutating accessor", "G := V.Next", null },

        // value receivers: a temporary, no diagnostic
        { "value: a function result", "Make.Bump", null },
        { "value: the result of a function type's function, called", "GetSeven.Bump", null },
        { "value: a property read through a field", "Box.Value.Bump", null },
        { "value: a property read through a method", "Box.Computed.Bump", null },
        { "value: the result of an operator", "(L + 1).Bump", null },
        { "value: the result of a function on a constant", "G := Abs(L).Next", null },
        { "value: a string function result", "(CS + 'x').Append('y')", null },
    };

    /// <summary>
    /// LANG-996 (a), (c): a call of a mutating record-helper method — also through a property whose accessor it is — on a
    /// read-only location (a <c>const</c> parameter, a typed constant, a loop variable inside its loop, the target of a read-only
    /// <c>with</c>, a field or element of these) or on a constant is E2752 at the member name, naming the method as declared; a
    /// method that is not mutating is accepted there; locations and plain values (function results, property reads, operator
    /// results) take every method.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReceiverRows))]
    [Trait("Req", "LANG-996")]
    public void ReadOnlyReceiver_E2752(string what, string statement, string? method)
    {
        _ = what;
        var source = ReceiverProgram.ReplaceLineEndings("\n").Replace("$S$", statement, StringComparison.Ordinal);
        AssertMarkers([("P.pas", source)]);

        var messages = MarkerCompilation([("P.pas", source)]).GetDiagnostics().Select(d => d.Message);
        Assert.Equal(method is null ? [] : [$"Cannot call '{method}' on a read-only value: the method changes Self"], messages);
    }

    /// <summary>
    /// LANG-996 (c): nothing is reported inside a helper method for calls on its own <c>Self</c> or on parts of it, and a record
    /// method calling a helper method on its <c>Self</c> is a call on a location; E2752 is reported once per call, also for a
    /// call bound as an argument of an overloaded routine.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-996")]
    public void ReadOnlyReceiver_E2752_OwnSelfAndOverloadedArguments() => AssertMarkers(
    [
        ("P.pas", """
            program P;
            type
              TInts = array of Integer;
              TNames = array of string;
              TRec = record
                N: Integer;
                procedure Grow;
              end;
              TIntH = record helper for Integer
                procedure Bump;
                procedure Twice;
                function Take: Integer;
              end;
              TRecH = record helper for TRec
                procedure Both;
              end;
            procedure TIntH.Bump;
            begin
              Inc(Self);
            end;
            procedure TIntH.Twice;
            begin
              Bump;
              Self.Bump;
            end;
            function TIntH.Take: Integer;
            begin
              Result := Self;
              Twice;
            end;
            procedure TRec.Grow;
            begin
              N.Bump;
            end;
            procedure TRecH.Both;
            begin
              N.Twice;
              Self.N.Bump;
            end;
            procedure Show(N: Integer); overload;
            begin
              Writeln(N);
            end;
            procedure Show(const S: string); overload;
            begin
              Writeln(S);
            end;
            procedure Total(const A: TInts); overload;
            begin
              Writeln(Length(A));
            end;
            procedure Total(const A: TNames); overload;
            begin
              Writeln(Length(A));
            end;
            procedure Use(const K: Integer; const R: TRec);
            begin
              Show(K.Take{!E2752});
              Total([K.Take{!E2752}, 2]);
              R.Both{!E2752};
              R.Grow;
            end;
            begin
            end.
            """),
    ]);
}
