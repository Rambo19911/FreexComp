unit UCDRaise;

{ the class destructor raises after the finalization statements ran }

interface

type
  TRaise = class
    class destructor Destroy;
  end;

implementation

class destructor TRaise.Destroy;
begin
  Writeln('TRaise cdtor');
  raise TObject.Create;
  Writeln('not reached');
end;

initialization
finalization
  Writeln('UCDRaise final');
end.
