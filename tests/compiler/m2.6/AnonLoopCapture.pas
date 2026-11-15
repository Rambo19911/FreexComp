// covers: LANG-721
program AnonLoopCapture;

type
  TGet = reference to function: Integer;

procedure Run;
var
  I: Integer;
  Fs: array[1..3] of TGet;
begin
  for I := 1 to 3 do
  begin
    Fs[I] := function: Integer begin Result := I * 10 end;
    Write(Fs[I](), ' ');
  end;
  Writeln;
  I := 7;
  Writeln(Fs[1](), ' ', Fs[2](), ' ', Fs[3]());
end;

begin
  Run;
end.