unit UCRaise;

{ TTwo's class constructor raises: the unit's initialization did not complete, so the unit is not finalised and TOne's
  class destructor does not run (LANG-791); its initialization statements never run }

interface

type
  TOne = class
    class constructor Create;
    class destructor Destroy;
  end;

  TTwo = class
    class constructor Create;
  end;

implementation

class constructor TOne.Create;
begin
  Writeln('TOne cctor');
end;

class destructor TOne.Destroy;
begin
  Writeln('TOne cdtor');
end;

class constructor TTwo.Create;
begin
  Writeln('TTwo cctor');
  raise TObject.Create;
  Writeln('not reached');
end;

initialization
  Writeln('UCRaise init');
finalization
  Writeln('UCRaise final');
end.
