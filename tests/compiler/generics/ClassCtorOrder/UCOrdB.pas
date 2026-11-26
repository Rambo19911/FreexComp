unit UCOrdB;

{ a unit that uses UCOrdA: its class constructor sees TA's class variable already set (LANG-790 a) }

interface

uses
  UCOrdA;

type
  TB = class
    class constructor Create;
    class destructor Destroy;
  end;

implementation

class constructor TB.Create;
begin
  Writeln('TB cctor ', TA.Value);
end;

class destructor TB.Destroy;
begin
  Writeln('TB cdtor');
end;

initialization
  Writeln('UCOrdB init');
finalization
  Writeln('UCOrdB final');
end.
