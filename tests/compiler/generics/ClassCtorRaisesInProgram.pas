// covers: LANG-789, LANG-791, CMP-1396, CMP-1397, ABI-104
program ClassCtorRaisesInProgram;

{ a program type's class constructor that raises ends the program with exit code 217 before the main block; the class
  destructors of the program's types whose class constructor completed still run, and the program's managed globals
  are released afterwards (the program's own unit table entry) }

type
  TTracker = class(TInterfacedObject)
    destructor Destroy; override;
  end;

  TOk = class
    class constructor Create;
    class destructor Destroy;
  end;

  TBad = class
    class constructor Create;
    class destructor Destroy;
  end;

var
  G: IInterface;

destructor TTracker.Destroy;
begin
  Writeln('released');
  inherited Destroy;
end;

class constructor TOk.Create;
begin
  Writeln('TOk cctor');
  G := TTracker.Create;
end;

class destructor TOk.Destroy;
begin
  Writeln('TOk cdtor');
end;

class constructor TBad.Create;
begin
  Writeln('TBad cctor');
  raise TObject.Create;
end;

class destructor TBad.Destroy;
begin
  Writeln('TBad cdtor');
end;

begin
  Writeln('main');
end.
