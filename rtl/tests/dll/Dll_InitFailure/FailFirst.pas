unit FailFirst;
// initialised before the failing unit: finalised when the load fails. Its line on ErrOutput shows that the library
// writes to the standard error handle of the host, so "nothing else was written" can be measured there.

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
  Writeln(ErrOutput, 'FailFirst initialised');
finalization
  Mark('first-final.txt');
end.
