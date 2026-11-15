// covers: LANG-701, LANG-440, LANG-714
program GenericMethodOverload;

type
  TArr = class
    class procedure Sort<T>(var A: array of T); overload;
    class procedure Sort<T>(var A: array of T; Descending: Boolean); overload;
    class function Pick<T>(const A, B: T): T; overload;
    class function Pick<T>(const A, B, C: T): T; overload;
    class function Pick<K, V>(const A: K; const B: V; Flag: Boolean): V; overload;
  end;

class procedure TArr.Sort<T>(var A: array of T);
begin
  Sort<T>(A, False);
end;

class procedure TArr.Sort<T>(var A: array of T; Descending: Boolean);
var
  I, J: Integer;
  X: T;
begin
  for I := 0 to High(A) - 1 do
    for J := I + 1 to High(A) do
      if (not Descending and (A[J] < A[I])) or (Descending and (A[J] > A[I])) then
      begin
        X := A[I];
        A[I] := A[J];
        A[J] := X;
      end;
end;

class function TArr.Pick<T>(const A, B: T): T;
begin
  Result := B;
end;

class function TArr.Pick<T>(const A, B, C: T): T;
begin
  Result := C;
end;

class function TArr.Pick<K, V>(const A: K; const B: V; Flag: Boolean): V;
begin
  Result := B;
end;

var
  N: array[0..3] of Integer = (3, 1, 4, 2);
  S: array[0..2] of string = ('b', 'c', 'a');
  I: Integer;
begin
  TArr.Sort<Integer>(N);
  for I := 0 to 3 do
    Write(N[I]);
  Writeln;
  TArr.Sort<string>(S, True);
  Writeln(S[0], S[1], S[2]);
  Writeln(TArr.Pick<Integer>(1, 2), TArr.Pick<Integer>(1, 2, 3), TArr.Pick<Integer, string>(1, 'v', True));
  Writeln(TArr.Pick('x', 'y'), TArr.Pick('x', 'y', 'z'));
end.
