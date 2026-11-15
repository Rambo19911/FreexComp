// covers: LANG-721, LANG-722
program AnonSharedCapture;

type
  TProc = reference to procedure;
  TGet = reference to function: Integer;

var
  Inc1, Inc2: TProc;
  Get: TGet;

procedure Setup;
var
  Count: Integer;
begin
  Count := 0;
  Inc1 := procedure begin Count := Count + 1 end;
  Inc2 := procedure begin Count := Count + 10 end;
  Get := function: Integer begin Result := Count end;
  Inc1();
  Inc2();
  Writeln('in Setup: ', Count);
  Count := 100;
end;

procedure Same;
var
  K: Integer;
  Ps: array[1..2] of TProc;
begin
  for K := 1 to 2 do
    Ps[K] := procedure begin Writeln('same') end;
  Writeln(Ps[1] = Ps[2]);
  Ps[2]();
end;

begin
  Setup;
  Writeln(Get());
  Inc1();
  Inc2;
  Writeln(Get);
  Same;
end.