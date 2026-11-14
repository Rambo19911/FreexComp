// covers: LANG-420
program ProcConstParams;

function Sum3(const A, B, C: Integer): Integer;
begin
  Result := A + B + C;
end;

procedure Show(const S: Char; const N: Int64; const B: Boolean);
begin
  Writeln(S, ' ', N, ' ', B);
end;

function Max(const A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

procedure Copy(const Src: Integer; var Dst: Integer);
begin
  Dst := Src + 1;
end;

var
  X: Integer;
begin
  X := 10;
  Writeln(Sum3(1, 2, 3), ' ', Sum3(X, X * 2, -X));
  Show('k', 1234567890123, X > 5);
  Show(Chr(65), X, False);
  Writeln(Max(3, 7), ' ', Max(X, -1), ' ', Max(Max(1, 2), Max(4, 3)));
  Copy(X, X);
  Writeln(X);
end.
