unit Probe;

interface

implementation

var
  Before: NativeInt;

initialization
  Before := _LiveAllocCount;
finalization
  Writeln('leaked: ', _LiveAllocCount - Before);
end.