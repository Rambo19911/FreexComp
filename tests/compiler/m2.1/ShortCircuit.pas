// covers: LANG-305
program ShortCircuit;
var
  Calls: Integer;
  T, F, B: Boolean;

function Side(V: Boolean): Boolean;
begin
  Calls := Calls + 1;
  Result := V;
end;

begin
  T := True;
  F := False;
  Calls := 0;
  if F and Side(True) then
    Writeln('wrong 1');
  Writeln(Calls);
  if T or Side(True) then
    Writeln('or taken');
  Writeln(Calls);
  if T and Side(False) then
    Writeln('wrong 2');
  Writeln(Calls);
  B := F or Side(True);
  Writeln(B, ' ', Calls);
  B := F and Side(True) and Side(True);
  Writeln(B, ' ', Calls);
{$B+}
  Calls := 0;
  if F and Side(True) then
    Writeln('wrong 3');
  Writeln(Calls);
  if T or Side(True) then
    Writeln('or taken');
  Writeln(Calls);
  B := F and Side(True) and Side(True);
  Writeln(B, ' ', Calls);
{$B-}
  Calls := 0;
  B := T or Side(False);
  Writeln(B, ' ', Calls);
end.
