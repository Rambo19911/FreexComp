unit Faults;

{ the faulting store lives in another unit (module), so the optimiser can neither inline it into the try statement's
  routine nor see that the pointer is nil (LANG-830: guaranteed for faults in a called routine) }

interface

type
  PInteger = ^Integer;

procedure Poke(P: PInteger);

implementation

procedure Poke(P: PInteger);
begin
  P^ := 1;
end;

end.