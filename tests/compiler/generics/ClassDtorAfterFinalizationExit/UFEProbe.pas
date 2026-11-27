unit UFEProbe;

{ initialised first, finalised last: counts the blocks still allocated after the other units' finalizations }

interface

implementation

var
  Before: NativeInt;

initialization
  Before := _LiveAllocCount;
finalization
  Writeln('leaked ', _LiveAllocCount - Before);
end.
