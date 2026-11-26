// covers: LANG-786, LANG-787, LANG-788, LANG-789, LANG-790, CMP-1395, CMP-1396, CMP-1397
program ClassCtorOrder;

{ class constructors run once per type, in unit initialization order, before each unit's initialization statements; the
  program's own ones run after all units, before the main block. Class destructors run in reverse order, after each
  unit's finalization statements; the ordinary constructor named like the class constructor is the one a call finds. }

uses
  UCOrdA, UCOrdB;

type
  TP = record
    X: Integer;
    class constructor Create;
    class destructor Destroy;
  end;

  TD = class(TA)
    class constructor Create;
  end;

class constructor TP.Create;
begin
  Writeln('TP cctor');
end;

class destructor TP.Destroy;
begin
  Writeln('TP cdtor');
end;

class constructor TD.Create;
begin
  Writeln('TD cctor ', TA.Value);
end;

var
  A: TA;

begin
  Writeln('main ', TA.Value);
  A := TA.Create;
  A.Free;
  Writeln('main end');
end.
