unit RaiseInit;

interface

implementation

initialization
  Writeln('RaiseInit init');
  raise TObject.Create;
end.
