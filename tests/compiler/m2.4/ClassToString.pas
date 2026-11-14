// covers: LANG-622, ABI-073
program ClassToString;

{ Overriding the TObject slots ToString, Equals and GetHashCode (ABI-073 slots 5, 6, 7) and calling them through
  TObject references; TObject.Equals (identity) reached with inherited and through a class that keeps it. The
  default TObject.ToString is not printed (it depends on ClassName). }

type
  TPoint = class
    X, Y: Integer;
    constructor Create(AX, AY: Integer);
    function ToString: string; override;
    function Equals(Obj: TObject): Boolean; override;
    function GetHashCode: Integer; override;
  end;

  TPlain = class
  end;

constructor TPoint.Create(AX, AY: Integer);
begin
  inherited Create;
  X := AX;
  Y := AY;
end;

function TPoint.ToString: string;
begin
  Result := 'point';
end;

function TPoint.Equals(Obj: TObject): Boolean;
begin
  if inherited Equals(Obj) then
    Result := True
  else if Obj = nil then
    Result := False
  else
    Result := (TPoint(Obj).X = X) and (TPoint(Obj).Y = Y);
end;

function TPoint.GetHashCode: Integer;
begin
  Result := X * 31 + Y;
end;

var
  A, B, C: TObject;
  P1, P2: TPlain;
begin
  A := TPoint.Create(1, 2);
  B := TPoint.Create(1, 2);
  C := TPoint.Create(2, 1);
  Writeln(A.ToString, ' ', A.GetHashCode, ' ', C.GetHashCode);
  Writeln(A.Equals(A), ' ', A.Equals(B), ' ', A.Equals(C), ' ', A.Equals(nil));
  P1 := TPlain.Create;
  P2 := TPlain.Create;
  Writeln(P1.Equals(P1), ' ', P1.Equals(P2), ' ', P1.GetHashCode = P1.GetHashCode);
  A.Free;
  B.Free;
  C.Free;
  P1.Free;
  P2.Free;
end.
