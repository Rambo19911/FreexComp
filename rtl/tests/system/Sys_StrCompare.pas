// covers: RTL-076
program Sys_StrCompare;
{ Ordinal comparison (system.md RTL-076): UTF-16 code units for string, unsigned bytes for AnsiString and ShortString;
  a common prefix puts the shorter value first; no locale. Surrogates compare as code units, so U+10000 (D800 DC00)
  sorts before U+FFFF. _AStrCmp and _SStrCmp are called directly on hand-built values. }
type
  TAnsiBlock = record
    RefCount, Len: Integer;
    CodePage, ElemSize: Word;
    Reserved: Integer;
    Data: array[0..15] of Byte;
  end;
  TShort = array[0..255] of Byte;

var
  Base: NativeInt;

function Sign(I: Integer): Integer;
begin
  Result := 0;
  if I < 0 then
    Result := -1
  else if I > 0 then
    Result := 1;
end;

function Raw(const S: string): Pointer;
begin
  Result := PPointer(@S)^;
end;

procedure Compare(const A, B: string);
begin
  Writeln(Sign(_UStrCmp(Raw(A), Raw(B))), ' ', _UStrEq(Raw(A), Raw(B)), ' ', A = B, ' ', A <> B, ' ', A < B, ' ',
    A <= B, ' ', A > B, ' ', A >= B);
end;

{ the low bytes of S's elements }
procedure MakeAnsi(var X: TAnsiBlock; const S: string);
var
  I: Integer;
begin
  X.RefCount := -1;
  X.Len := Length(S);
  X.CodePage := 65001;
  X.ElemSize := 1;
  X.Reserved := 0;
  for I := 1 to Length(S) do
    X.Data[I - 1] := Ord(S[I]) and $FF;
  X.Data[Length(S)] := 0;
end;

procedure MakeShort(var X: TShort; const S: string);
var
  I: Integer;
begin
  X[0] := Length(S);
  for I := 1 to Length(S) do
    X[I] := Ord(S[I]) and $FF;
end;

procedure CompareBytes(const A, B: string);
var
  X, Y: TAnsiBlock;
  P, Q: Pointer;
  SX, SY: TShort;
begin
  MakeAnsi(X, A);
  MakeAnsi(Y, B);
  P := nil;
  Q := nil;
  if Length(A) > 0 then
    P := @X.Data[0];
  if Length(B) > 0 then
    Q := @Y.Data[0];
  MakeShort(SX, A);
  MakeShort(SY, B);
  Writeln('bytes ', Sign(_AStrCmp(P, Q)), ' ', Sign(_SStrCmp(@SX, @SY)));
end;

procedure Run;
var
  S, T: string;
  C: Char;
begin
  Compare('abc', 'abc');
  Compare('abc', 'abd');
  Compare('abd', 'abc');
  Compare('ab', 'abc');
  Compare('abc', 'ab');
  Compare('', 'a');
  Compare('a', '');
  Compare('', '');
  Compare('B', 'a');
  Compare('Z', 'z');
  Compare(#$D800#$DC00, #$FFFF);
  Compare(#$E000, #$D800#$DC00);
  Compare('a'#0'b', 'a'#0'c');
  Compare('a'#0, 'a');
  S := 'abc';
  T := 'ab';
  T := T + 'c';
  Writeln('heap equals literal ', _UStrEq(Raw(S), Raw(T)), ' ', _UStrCmp(Raw(S), Raw(T)), ' same block ',
    _UStrEq(Raw(T), Raw(T)), ' ', _UStrCmp(Raw(T), Raw(T)));
  C := 'c';
  T := T + C;
  Compare(S, T);
  S := T;
  T[4] := 'd';
  Compare(S, T);
  CompareBytes('abc', 'abc');
  CompareBytes('abc', 'abd');
  CompareBytes('abd', 'abc');
  CompareBytes('ab', 'abc');
  CompareBytes('abc', 'ab');
  CompareBytes('', 'a');
  CompareBytes('', '');
  CompareBytes('a', #$C3);
  CompareBytes(#$FF, #$7F);
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('all released ', _LiveAllocCount - Base);
end.
