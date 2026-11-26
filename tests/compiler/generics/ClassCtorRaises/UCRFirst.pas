unit UCRFirst;

{ initialised completely before UCRaise fails: finalised, its class destructor runs after its finalization statements }

interface

type
  TFirst = class
    class constructor Create;
    class destructor Destroy;
  end;

implementation

class constructor TFirst.Create;
begin
  Writeln('TFirst cctor');
end;

class destructor TFirst.Destroy;
begin
  Writeln('TFirst cdtor');
end;

initialization
  Writeln('UCRFirst init');
finalization
  Writeln('UCRFirst final');
end.
