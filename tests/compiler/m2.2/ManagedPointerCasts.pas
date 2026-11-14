// covers: LANG-265, LANG-290
program ManagedPointerCasts;
type
  PStr = ^string;
  PAStr = ^AnsiString;
  PInt = ^Integer;
var
  A, B: array of Integer;
  S, T: string;
  X: AnsiString;
  P: PStr;
  Q: PAStr;
begin
  Writeln(Pointer(A) = nil, ' ', NativeInt(A) = 0);
  SetLength(A, 2);
  B := A;
  Writeln(Pointer(A) <> nil, ' ', NativeInt(A) = NativeInt(Pointer(A)), ' ', Pointer(A) = Pointer(B));
  PInt(A)^ := 7;
  Writeln(A[0]);
  S := 'ab';
  Writeln(Pointer(S) <> nil, ' ', NativeInt(S) = NativeInt(Pointer(S)), ' ', PChar(S)^);
  P := @S;
  P^ := P^ + 'cd';
  T := P^;
  Writeln(S, ' ', T, ' ', Length(P^));
  Q := @X;
  Q^ := 'xyz';
  Writeln(X, ' ', Pointer(X) <> nil);
end.
