unit InitFails;

{ no finalization section: whether a unit whose initialization raised is finalised is not observed here }

interface

implementation

initialization
  Writeln('InitFails init');
  raise TObject.Create;
  Writeln('not reached');
end.
