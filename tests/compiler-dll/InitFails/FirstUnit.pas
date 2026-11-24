unit FirstUnit;
// initialised before the failing unit: it must be finalised when the load fails

interface

procedure Mark(const Name: string);

implementation

procedure Mark(const Name: string);
var
  F: Text;
begin
  Assign(F, Name);
  Rewrite(F);
  Close(F);
end;

initialization
  Mark('first-init.txt');
finalization
  Mark('first-final.txt');
end.
