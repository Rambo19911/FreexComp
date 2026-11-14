// covers: LANG-730
program RecordMethods;

type
  TPoint = record
  private
    FX, FY: Integer;
    function GetSum: Integer;
    procedure SetX(Value: Integer);
  public
    class var Count: Integer;
    constructor Create(AX, AY: Integer);
    constructor FromX(AX: Integer);
    procedure Move(DX, DY: Integer);
    procedure Reset;
    procedure Print;
    function Scaled(Factor: Integer): TPoint;
    class function Origin: TPoint; static;
    class function Twice(N: Integer): Integer; static;
    property X: Integer read FX write SetX;
    property Y: Integer read FY write FY;
    property Sum: Integer read GetSum;
  end;

  TNamed = record
    Name: string;
    Size: Integer;
    constructor Create(const AName: string);
    function Describe: string;
  end;

constructor TPoint.Create(AX, AY: Integer);
begin
  Inc(Count);
  FX := AX;
  FY := AY;
end;

constructor TPoint.FromX(AX: Integer);
begin
  Inc(Count);
  Self.FX := AX;
end;

function TPoint.GetSum: Integer;
begin
  Result := FX + FY;
end;

procedure TPoint.SetX(Value: Integer);
begin
  FX := Value;
end;

procedure TPoint.Move(DX, DY: Integer);
begin
  FX := FX + DX;
  Inc(Self.FY, DY);
end;

procedure TPoint.Reset;
begin
  Move(-FX, -FY);
end;

procedure TPoint.Print;
begin
  Writeln('(', FX, ', ', FY, ')');
end;

function TPoint.Scaled(Factor: Integer): TPoint;
begin
  Result := TPoint.Create(FX * Factor, FY * Factor);
end;

class function TPoint.Origin: TPoint;
begin
  Result := Create(0, 0);
end;

class function TPoint.Twice(N: Integer): Integer;
begin
  Result := N * 2;
end;

constructor TNamed.Create(const AName: string);
begin
  Name := AName;
  Size := Length(AName);
end;

function TNamed.Describe: string;
begin
  Result := Name + '!';
end;

procedure Bump(var R: TPoint);
begin
  R.Move(1, 1);
end;

var
  P, Q: TPoint;
  N: TNamed;
begin
  P := TPoint.Create(1, 2);
  P.Print;
  P.Move(10, 20);
  P.Print;
  P.X := 5;
  Writeln(P.X, ' ', P.Y, ' ', P.Sum);
  P.Y := 7;
  Writeln(P.Sum);
  Q := P;
  Q := TPoint.FromX(3);
  Q.Print;
  Bump(Q);
  Q.Print;
  Q.Reset;
  Q.Print;
  P.Scaled(2).Print;
  TPoint.Origin.Print;
  Writeln(TPoint.Twice(21), ' ', TPoint.Count);
  N := TNamed.Create('abc');
  Writeln(N.Describe, ' ', N.Size);
  N := TNamed.Create('');
  Writeln(N.Size, ' [', N.Name, ']');
  with P do
  begin
    Move(1, 1);
    Print;
  end;
end.
