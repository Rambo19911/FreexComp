// covers: RTL-072, RTL-073, RTL-074, RTL-075
program Sys_StrCow;
{$POINTERMATH ON}
{ Copy-on-write: writing into a shared or literal string copies it first and leaves the other owners intact;
  UniqueString; SetLength keeps the prefix, sets a terminator and makes the result unique; concatenation sums the
  lengths, gives nil for an empty result and accepts the destination as an operand. }
var
  Base: NativeInt;

function Raw(const S: string): Pointer;
begin
  Result := PPointer(@S)^;
end;

{ the element after the last one is #0 }
function Terminated(const S: string): Boolean;
begin
  Result := PWord(Raw(S))[Length(S)] = 0;
end;

function Literal: string;
begin
  Result := 'literal';
end;

{ temporaries live until the end of their routine, so the work runs in a procedure of its own }
procedure Run;
var
  A, B, C, D: string;
  I, Bad: Integer;
  P: Pointer;
begin
  { writes into shared blocks }
  A := 'hel';
  A := A + 'lo';
  B := A;
  B[1] := 'j';
  Writeln(A, ' ', B, ' ', StringRefCount(A), ' ', StringRefCount(B), ' ', Raw(A) <> Raw(B));
  A := Literal;
  A[1] := 'L';
  Writeln(A, ' ', Literal, ' ', StringRefCount(A));

  { UniqueString }
  A := 'shared';
  UniqueString(A);
  Writeln('literal made unique ', StringRefCount(A), ' ', A);
  B := A;
  UniqueString(B);
  Writeln('shared made unique ', StringRefCount(A), ' ', StringRefCount(B), ' ', Raw(A) <> Raw(B), ' ', B);
  C := B;
  C := '';
  P := Raw(B);
  UniqueString(B);
  Writeln('unique stays ', Raw(B) = P, ' ', StringRefCount(B));
  D := '';
  UniqueString(D);
  Writeln('empty stays nil ', Raw(D) = nil);

  { SetLength }
  A := 'abcdef';
  SetLength(A, 3);
  Writeln('shrunk literal ', A, ' ', Length(A), ' ', StringRefCount(A), ' ', Terminated(A));
  B := A;
  SetLength(A, 5);
  A[4] := 'X';
  A[5] := 'Y';
  Writeln('grown ', A, ' ', Length(A), ' ', Terminated(A), ' other ', B, ' ', StringRefCount(B));
  B := A;
  SetLength(B, 5);
  Writeln('same length unique ', StringRefCount(A), ' ', StringRefCount(B), ' ', Raw(A) <> Raw(B), ' ', B);
  SetLength(A, 100000);
  Writeln('large ', Length(A), ' ', Copy(A, 1, 5), ' ', Terminated(A));
  SetLength(A, 2);
  Writeln('back ', A, ' ', Terminated(A));
  SetLength(A, 0);
  Writeln('zero is nil ', Raw(A) = nil);
  A := 'x';
  SetLength(A, -3);
  Writeln('negative is nil ', Raw(A) = nil);
  SetLength(A, 1);
  Writeln('from nil ', Length(A), ' ', StringRefCount(A), ' ', Terminated(A));

  { concatenation }
  A := '';
  B := '';
  C := A + B;
  Writeln('empty + empty nil ', Raw(C) = nil);
  C := A + B + A;
  Writeln('three empties nil ', Raw(C) = nil);
  C := 'abc';
  D := C + A;
  Writeln('x + empty ', D, ' ', Length(D));
  D := A + C;
  Writeln('empty + x ', D, ' ', Length(D));
  A := 'ab';
  A := A + 'cd';
  A := A + A;
  Writeln('self twice ', A, ' ', Length(A), ' ', Terminated(A));
  A := '<' + A;
  Writeln('dest as right operand ', A);
  A := A + '|' + A;
  Writeln('dest twice in a chain ', A, ' ', Length(A), ' ', Terminated(A));
  B := A;
  A := A + '!';
  Writeln('shared dest ', B, ' ', A, ' ', StringRefCount(B));
  B := '';
  A := '';
  for I := 1 to 2000 do
    A := A + Chr(Ord('a') + I mod 26);
  Bad := 0;
  for I := 1 to 2000 do
    if A[I] <> Chr(Ord('a') + I mod 26) then
      Bad := Bad + 1;
  Writeln('appended ', Length(A), ' bad ', Bad, ' ', StringRefCount(A), ' ', Terminated(A));
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('all released ', _LiveAllocCount - Base);
end.
