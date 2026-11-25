unit HardwareFaultUnit;

{ the faulting routine lives in another unit (module): the optimiser can neither inline it into the caller nor see
  that the pointer is nil }

interface

type
  PInteger = ^Integer;

procedure Faulting(P: PInteger);

implementation

uses
  SysUtils;

{ a fault outside every try statement of the routine: its managed variables are finalised on the way out }
procedure Faulting(P: PInteger);
var
  S: string;
begin
  S := 'value ' + IntToStr(12345);
  P^ := Length(S);
  Writeln('guard: not reached ', S);
end;

end.
