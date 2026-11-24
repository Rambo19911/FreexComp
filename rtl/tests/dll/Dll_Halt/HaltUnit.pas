unit HaltUnit;
// its finalization runs once when the library halts the process: a marker file and a line on the library's Output

interface

implementation

var
  F: Text;

initialization
finalization
  Assign(F, 'halt-final.txt');
  Rewrite(F);
  Close(F);
  Writeln('library: finalization of HaltUnit');
end.
