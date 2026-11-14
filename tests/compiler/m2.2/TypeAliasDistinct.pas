// covers: LANG-205
program TypeAliasDistinct;
type
  TAlias = Integer;
  TCount = type Integer;
  TLetter = type Char;
  TColor = (Red, Green, Blue);
  THue = type TColor;
  TRatio = type Double;
  TFlag = type Boolean;
var
  A: TAlias;
  N: TCount;
  I: Integer;
  L: TLetter;
  H: THue;
  R: TRatio;
  F: TFlag;

procedure AddOne(var X: Integer);
begin
  X := X + 1;
end;

function Twice(X: TCount): TCount;
begin
  Result := X * 2;
end;

begin
  { an alias is the same type: accepted for a var parameter of the original }
  A := 5;
  AddOne(A);
  Writeln(A, ' ', SizeOf(TAlias));
  { a distinct type is assignment-compatible with its source in both directions }
  N := A;
  I := N + 1;
  Writeln(N, ' ', I, ' ', Twice(I), ' ', SizeOf(TCount));
  N := High(TCount);
  Writeln(N);
  L := 'x';
  Writeln(L, ' ', Ord(L), ' ', L = 'x', ' ', SizeOf(TLetter));
  H := Blue;
  Writeln(Ord(H), ' ', H > Red, ' ', Ord(High(THue)));
  R := 1.5;
  Writeln(R * 2 = 3, ' ', SizeOf(TRatio));
  F := I > 3;
  if F then
    Writeln('flag');
end.
