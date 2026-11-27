program CapturedForVar;

type
  TFunc = reference to function: Integer;

function Count: Integer;
begin
  Result := 2;
end;

procedure Run;
var
  Fs: array[1..2] of TFunc;
  Total: Integer;
begin
  Total := 0;
  for var I := 1 to Count do
  begin
    Fs[I] := function: Integer begin Result := I * 10 end;
    Total := Total + I;
  end;
  WriteLn(Fs[1](), ' ', Fs[2](), ' ', Total);
end;

begin
  Run;
end.
