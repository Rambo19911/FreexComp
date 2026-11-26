unit UCDFirst;

{ finalised after UCDRaise although UCDRaise's class destructor raised (ABI-104 step 2 goes on) }

interface

type
  TFirst = class
    class destructor Destroy;
  end;

implementation

class destructor TFirst.Destroy;
begin
  Writeln('TFirst cdtor');
end;

initialization
finalization
  Writeln('UCDFirst final');
end.
