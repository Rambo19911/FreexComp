// covers: LANG-260, CMP-310, CMP-326
program RecordParamsResults;
// ABI-080 / ABI-081: records, static arrays and big sets by value, var and const, and as function results;
// sizes 4 and 8 travel as integers, 12 and 24 through a pointer (value: a copy made by the caller).
type
  TSmall = record
    A, B: Word;
  end;
  TPoint = record
    X, Y: Integer;
  end;
  TTriple = record
    X, Y, Z: Integer;
  end;
  TBox = record
    Lo, Hi: TTriple;
  end;
  TBytes3 = array[0..2] of Byte;
  TLetters = set of Char;

var
  GP: TPoint;
  GT: TTriple;
  GB: TBox;

function MakeSmall(A, B: Word): TSmall;
begin
  Result.A := A;
  Result.B := B;
end;

function MakePoint(X, Y: Integer): TPoint;
begin
  Result.X := X;
  Result.Y := Y;
end;

function MakeTriple(X, Y, Z: Integer): TTriple;
var
  T: TTriple;
begin
  T.X := X;
  T.Y := Y;
  T.Z := Z;
  MakeTriple := T;
end;

function MakeBox(const Lo, Hi: TTriple): TBox;
begin
  Result.Lo := Lo;
  Result.Hi := Hi;
end;

function SumSmall(S: TSmall): Integer;
begin
  Result := S.A + S.B;
end;

procedure ShowPoint(P: TPoint);
begin
  Writeln('(', P.X, ', ', P.Y, ')');
end;

procedure ShowTriple(const T: TTriple);
begin
  Writeln('(', T.X, ', ', T.Y, ', ', T.Z, ')');
end;

procedure ShowBox(B: TBox);
begin
  Write('box ');
  ShowTriple(B.Lo);
  Write('    ');
  ShowTriple(B.Hi);
end;

// value parameters are copies: changing them does not change the argument
procedure Clobber(P: TPoint; T: TTriple; B: TBox);
begin
  P.X := -1;
  T.X := -2;
  B.Lo.X := -3;
  Writeln('inside ', P.X, ' ', T.X, ' ', B.Lo.X);
end;

procedure Shift(var T: TTriple; D: Integer);
begin
  T.X := T.X + D;
  T.Y := T.Y + D;
  T.Z := T.Z + D;
end;

function Shifted(T: TTriple; D: Integer): TTriple;
begin
  Shift(T, D);
  Result := T;
end;

function Swapped(P: TPoint): TPoint;
begin
  Exit(MakePoint(P.Y, P.X));
end;

function Biggest(const A, B: TBox): TBox;
begin
  if A.Hi.X >= B.Hi.X then
    Result := A
  else
    Result := B;
end;

function Total(B: TBox): Integer;

  function Part(const T: TTriple): Integer;
  begin
    Result := T.X + T.Y + T.Z;
  end;

  function Width: Integer;
  begin
    Result := B.Hi.X - B.Lo.X;     // B is captured by the nested routine
  end;

begin
  Result := Part(B.Lo) + Part(B.Hi) + Width;
end;

function Reversed(A: TBytes3): TBytes3;
begin
  Result[0] := A[2];
  Result[1] := A[1];
  Result[2] := A[0];
end;

function CountLetters(const S: TLetters): Integer;
var
  C: Char;
begin
  Result := 0;
  for C := 'a' to 'z' do
    if C in S then
      Result := Result + 1;
end;

function WithVowels(S: TLetters): TLetters;
begin
  Result := S + ['a', 'e', 'i', 'o', 'u'];
end;

var
  S: TSmall;
  P: TPoint;
  T: TTriple;
  B: TBox;
  A3: TBytes3;
  L: TLetters;
begin
  S := MakeSmall(3, 4);
  Writeln(S.A, ' ', S.B, ' ', SumSmall(S), ' ', SumSmall(MakeSmall(10, 20)));
  P := MakePoint(1, 2);
  ShowPoint(P);
  ShowPoint(Swapped(P));
  Writeln(MakePoint(7, 8).Y);
  T := MakeTriple(1, 2, 3);
  ShowTriple(T);
  ShowTriple(MakeTriple(4, 5, 6));
  B := MakeBox(T, MakeTriple(10, 20, 30));
  ShowBox(B);
  Clobber(P, T, B);
  Writeln('after ', P.X, ' ', T.X, ' ', B.Lo.X);
  Shift(T, 100);
  ShowTriple(T);
  ShowTriple(Shifted(T, 1));
  ShowTriple(T);
  GT := Shifted(GT, 5);
  ShowTriple(GT);
  GB := Biggest(B, MakeBox(GT, GT));
  ShowBox(GB);
  Writeln(Total(B), ' ', Total(GB));
  GP := MakePoint(5, 6);
  GP := Swapped(GP);
  ShowPoint(GP);
  MakeTriple(0, 0, 0);
  A3[0] := 1;
  A3[1] := 2;
  A3[2] := 3;
  A3 := Reversed(A3);
  Writeln(A3[0], A3[1], A3[2]);
  L := ['b', 'c', 'e'];
  Writeln(CountLetters(L), ' ', CountLetters(WithVowels(L)), ' ', CountLetters(L));
end.
