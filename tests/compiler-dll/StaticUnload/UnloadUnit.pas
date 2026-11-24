unit UnloadUnit;

interface

implementation

var
  F: Text;

initialization
finalization
  Assign(F, 'unloaded.txt');
  Rewrite(F);
  Close(F);
end.
