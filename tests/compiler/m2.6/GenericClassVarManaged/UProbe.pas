unit UProbe;

interface

implementation

var
  Before: NativeInt;

initialization
  Before := _LiveAllocCount;
finalization
  Writeln('after finalization leaked: ', _LiveAllocCount - Before);
end.
