unit SUFaults;

{ Faulting memory accesses for SU_OSExceptions, in their own module so the optimiser cannot see the pointer value
  (LANG-830: faults in a called routine are guaranteed to be catchable). }

interface

function PeekAt(P: PInteger): Integer;
procedure PokeAt(P: PInteger);

implementation

function PeekAt(P: PInteger): Integer;
begin
  Result := P^;
end;

procedure PokeAt(P: PInteger);
begin
  P^ := 1;
end;

end.
