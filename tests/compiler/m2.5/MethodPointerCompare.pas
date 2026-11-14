// covers: LANG-481, CMP-378, LANG-664
program MethodPointerCompare;

{ = and <> between method pointers compare Code and Data: the same method of the same object is equal, the same
  method of another object or another method of the same object is not; a method pointer from a function result is
  evaluated once. Interface method pointers (Intf.M): Data is the interface reference, the call reaches the
  implementing method (also an override behind the interface), and two pointers to the same interface method compare
  equal. }

type
  TNotify = procedure(N: Integer) of object;

  IGreeter = interface
    ['{6A1E2C3D-0B4F-4A5E-9C8D-7F6E5D4C3B2A}']
    procedure Greet(N: Integer);
  end;

  TCounter = class
    Name: string;
    Calls: Integer;
    constructor Create(const AName: string);
    procedure Hit(N: Integer);
    procedure Miss(N: Integer);
  end;

  TGreeter = class(TInterfacedObject, IGreeter)
    procedure Greet(N: Integer); virtual;
  end;

  TLoudGreeter = class(TGreeter)
    procedure Greet(N: Integer); override;
  end;

constructor TCounter.Create(const AName: string);
begin
  Name := AName;
end;

procedure TCounter.Hit(N: Integer);
begin
  Inc(Calls, N);
  Writeln(Name, '.Hit ', N);
end;

procedure TCounter.Miss(N: Integer);
begin
  Writeln(Name, '.Miss ', N);
end;

procedure TGreeter.Greet(N: Integer);
begin
  Writeln('hello ', N);
end;

procedure TLoudGreeter.Greet(N: Integer);
begin
  Writeln('HELLO ', N);
end;

var
  Made: Integer;

function Make(C: TCounter): TNotify;
begin
  Inc(Made);
  Result := C.Hit;
end;

var
  A, B: TCounter;
  E, F: TNotify;
  G, L: IGreeter;
begin
  A := TCounter.Create('a');
  B := TCounter.Create('b');
  E := A.Hit;
  F := A.Hit;
  Writeln('same ', E = F, ' ', E <> F);
  F := B.Hit;
  Writeln('other object ', E = F, ' ', E <> F);
  F := A.Miss;
  Writeln('other method ', E = F, ' ', E <> F);
  F := nil;
  Writeln('nil ', E = F, ' ', F = nil);
  Made := 0;
  Writeln('result ', Make(A) = E, ' ', E = Make(B), ' ', Made);
  E(2);
  Writeln('calls ', A.Calls);

  G := TGreeter.Create;
  L := TLoudGreeter.Create;
  E := G.Greet;
  E(1);
  F := L.Greet;
  F(2);
  Writeln('intf ', E = F, ' ', TMethod(E).Data = Pointer(G));
  F := G.Greet;
  Writeln('intf same ', E = F);
  A.Free;
  B.Free;
end.
