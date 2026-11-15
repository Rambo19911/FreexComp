// covers: LANG-731 (class operator In, C-220)
program OperatorIn;

type
  TRange = record
    Lo, Hi: Integer;
    class operator In(const A: Integer; const R: TRange): Boolean;
  end;

  TWords = record
    Items: array[0..2] of string;
    class operator in(const S: string; const W: TWords): Boolean;
  end;

class operator TRange.In(const A: Integer; const R: TRange): Boolean;
begin
  Result := (A >= R.Lo) and (A <= R.Hi);
end;

class operator TWords.in(const S: string; const W: TWords): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to 2 do
    if W.Items[I] = S then
      Exit(True);
end;

var
  R: TRange;
  W: TWords;
  I: Integer;
begin
  R.Lo := 3;
  R.Hi := 6;
  for I := 1 to 8 do
    if I in R then
      Write(' ', I);
  Writeln;
  W.Items[0] := 'red';
  W.Items[1] := 'green';
  W.Items[2] := 'blue';
  Writeln('green' in W, ' ', 'pink' in W);
  Writeln(not (7 in R));
  Writeln(4 in [1..5]);
end.