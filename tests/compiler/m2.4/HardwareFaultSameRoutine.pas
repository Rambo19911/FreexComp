// covers: LANG-830, CMP-398
program HardwareFaultSameRoutine;

{ a nil write between two calls is caught by the try statement of the same routine, also under optimisation }

uses
  SysUtils;

type
  PInteger = ^Integer;

var
  Calls: Integer;

procedure Step;
begin
  Inc(Calls);
end;

procedure Run;
var
  P: PInteger;
begin
  P := nil;
  try
    Step;
    P^ := 7;
    Step;
    Writeln('not reached');
  except
    Writeln('caught');
  end;
end;

begin
  Run;
end.
