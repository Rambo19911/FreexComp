unit UCOrdA;

{ own items in declaration order: TA, its nested TA.TN, the record TR, then TUnused (never referenced, still run, LANG-790 c);
  constructor parts before the initialization statements, destructor parts after the finalization statements in reverse }

interface

type
  TA = class
    class var Value: Integer;
    class constructor Create;
    class destructor Destroy;
    constructor Create;
  public
    type
      TN = class
        class constructor Init;
        class destructor Done;
      end;
  end;

  TR = record
    X: Integer;
    class constructor Init;
  end;

implementation

type
  TUnused = class
    class constructor Create;
    class destructor Destroy;
  end;

class constructor TA.Create;
begin
  Value := 42;
  Writeln('TA cctor');
end;

class destructor TA.Destroy;
begin
  Writeln('TA cdtor');
end;

constructor TA.Create;
begin
  inherited Create;
  Writeln('TA.Create');
end;

class constructor TA.TN.Init;
begin
  Writeln('TA.TN cctor');
end;

class destructor TA.TN.Done;
begin
  Writeln('TA.TN cdtor');
end;

class constructor TR.Init;
begin
  Writeln('TR cctor');
end;

class constructor TUnused.Create;
begin
  Writeln('TUnused cctor');
end;

class destructor TUnused.Destroy;
begin
  Writeln('TUnused cdtor');
end;

initialization
  Writeln('UCOrdA init');
finalization
  Writeln('UCOrdA final');
end.
