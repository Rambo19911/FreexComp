unit UFExit;

interface

type
  TA = class
    class destructor Destroy;
  end;

  TBox<T> = class
    class var Name: string;
    class destructor Destroy;
  end;

procedure Fill;

implementation

var
  G: string;
  Stop: Boolean;

class destructor TA.Destroy;
begin
  Writeln('TA cdtor');
end;

class destructor TBox<T>.Destroy;
begin
  Writeln('TBox cdtor ', Name);
end;

procedure Fill;
begin
  G := Copy('global!', 1, 6);
  TBox<Integer>.Name := Copy('boxed!', 1, 5);
end;

initialization
  Stop := True;
finalization
  Writeln('final ', G);
  if Stop then
    Exit;
  Writeln('not reached');
end.
