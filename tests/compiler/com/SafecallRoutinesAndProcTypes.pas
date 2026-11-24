// covers: LANG-685, LANG-689
program SafecallRoutinesAndProcTypes;

{ safecall on a global routine, on procedural variables (plain and of object), on a virtual method that a descendant
  overrides, on a record method and on a property accessor. }

type
  TCompute = function(A, B: Integer): Integer; safecall;
  TNotify = procedure(const Text: string) of object; safecall;
  TDescribe = function: string of object; safecall;

  TShape = class
    FLabel: string;
    function Area(Scale: Integer): Integer; virtual; safecall;
    function Describe: string; virtual; safecall;
    procedure Notify(const Text: string); safecall;
    function GetLabel: string; safecall;
    procedure SetLabel(const Value: string); safecall;
    property Caption: string read GetLabel write SetLabel;
  end;

  TSquare = class(TShape)
    function Area(Scale: Integer): Integer; override; safecall;
    function Describe: string; override; safecall;
  end;

  TPair = record
    A, B: Integer;
    function Sum: Integer; safecall;
  end;

function Add(A, B: Integer): Integer; safecall;
begin
  Result := A + B;
end;

function Mul(A, B: Integer): Integer; safecall;
begin
  Result := A * B;
end;

procedure Show(const Text: string); safecall; forward;

procedure Show(const Text: string);
begin
  WriteLn('show ', Text);
end;

function TShape.Area(Scale: Integer): Integer;
begin
  Result := Scale;
end;

function TShape.Describe: string;
begin
  Result := 'shape';
end;

procedure TShape.Notify(const Text: string);
begin
  WriteLn('notify ', Text);
end;

function TShape.GetLabel: string;
begin
  Result := '<' + FLabel + '>';
end;

procedure TShape.SetLabel(const Value: string);
begin
  FLabel := Value;
end;

function TSquare.Area(Scale: Integer): Integer;
begin
  Result := Scale * Scale + inherited Area(Scale);
end;

function TSquare.Describe: string;
begin
  Result := 'square of a ' + inherited Describe;
end;

function TPair.Sum: Integer;
begin
  Result := A + B;
end;

var
  F: TCompute;
  N: TNotify;
  D: TDescribe;
  S: TShape;
  P: TPair;
begin
  WriteLn(Add(2, 3));
  Show('direct');
  F := Add;
  WriteLn(F(4, 5));
  F := Mul;
  WriteLn(F(4, 5));
  S := TSquare.Create;
  WriteLn(S.Area(3));
  WriteLn(S.Describe);
  N := S.Notify;
  N('through a method pointer');
  D := S.Describe;
  WriteLn(D);
  S.Caption := 'label';
  WriteLn(S.Caption);
  S.Free;
  S := TShape.Create;
  WriteLn(S.Area(3), ' ', S.Describe);
  S.Free;
  P.A := 20;
  P.B := 22;
  WriteLn(P.Sum);
end.
