// covers: LANG-641, LANG-646, CMP-367
program ClassProperties;

{ Field and method accessors (read and write), a write-only-by-field / read-by-virtual-method property, a field path
  into a record field, a read-only record property read further (C.Pos.X), properties used inside a method without
  qualifier, and a descendant redeclaring a property with a new read accessor (LANG-646). }

type
  TPoint = record
    X, Y: Integer;
  end;

  TCounter = class
  private
    FCount: Integer;
    FName: string;
    FPos: TPoint;
    FLog: string;
    function GetTwice: Integer;
    procedure SetCount(Value: Integer);
    function GetName: string; virtual;
  public
    procedure Bump;
    property Count: Integer read FCount write SetCount;
    property Twice: Integer read GetTwice;
    property Name: string read GetName write FName;
    property X: Integer read FPos.X write FPos.X;
    property Pos: TPoint read FPos;
    property Log: string read FLog;
  end;

  TLoud = class(TCounter)
  private
    function GetName: string; override;
    function GetThrice: Integer;
  public
    property Twice read GetThrice;
  end;

function TCounter.GetTwice: Integer;
begin
  Result := FCount * 2;
end;

procedure TCounter.SetCount(Value: Integer);
begin
  FLog := FLog + '+';
  FCount := Value;
end;

function TCounter.GetName: string;
begin
  Result := FName;
end;

procedure TCounter.Bump;
begin
  Count := Count + 1;
end;

function TLoud.GetName: string;
begin
  Result := FName + '!';
end;

function TLoud.GetThrice: Integer;
begin
  Result := FCount * 3;
end;

var
  C: TCounter;
  L: TLoud;
begin
  C := TCounter.Create;
  C.Count := 5;
  Writeln(C.Count, ' ', C.Twice);
  C.Name := 'abc';
  Writeln(C.Name);
  C.X := 7;
  Writeln(C.X, ' ', C.Pos.X, ' ', C.Pos.Y);
  C.Bump;
  Writeln(C.Count, ' ', C.Log);
  C.Free;

  L := TLoud.Create;
  L.Count := 2;
  L.Name := 'xyz';
  Writeln(L.Name, ' ', L.Twice);
  C := L;
  Writeln(C.Name, ' ', C.Twice);
  L.Free;
end.
