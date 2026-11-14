// covers: LANG-662, LANG-663
program IntfRefCount;

{ Reference counting of interface variables: a new TInterfacedObject has count 0, every interface reference adds one,
  the object frees itself when the last reference goes (assignment, nil, end of scope, program finalization). }

type
  ICounter = interface
    function RefCountNow: Integer;
  end;

  TTracked = class(TInterfacedObject, ICounter)
    FName: string;
    constructor Create(const AName: string);
    destructor Destroy; override;
    function RefCountNow: Integer;
  end;

constructor TTracked.Create(const AName: string);
begin
  FName := AName;
  Writeln('create ', FName);
end;

destructor TTracked.Destroy;
begin
  Writeln('destroy ', FName);
  inherited Destroy;
end;

function TTracked.RefCountNow: Integer;
begin
  Result := RefCount;
end;

procedure Scope;
var
  A: ICounter;
begin
  A := TTracked.Create('local');
  Writeln('in scope ', A.RefCountNow);
end;

var
  O: TTracked;
  X, Y: ICounter;
begin
  O := TTracked.Create('one');
  Writeln(O.RefCount);
  X := O;
  Writeln(O.RefCount);
  Y := X;
  Writeln(X.RefCountNow);
  X := nil;
  Writeln(Y.RefCountNow);
  Writeln('release last');
  Y := nil;
  Scope;
  X := TTracked.Create('two');
  Y := TTracked.Create('three');
  X := Y;
  Writeln('end');
end.
