// covers: LANG-622, LANG-623, LANG-641, LANG-650, CMP-366, CMP-367, ABI-073, ABI-074
program ClassVirtual;

{ classes-interfaces.md section 11: an abstract Area
  overridden in two descendants, dispatched through a TShape reference; a static method calling a virtual one;
  a three-level chain where the middle class does not override; Destroy overridden and reached through Free. }

type
  TShape = class
  private
    FName: string;
  public
    constructor Create(const AName: string);
    function Area: Double; virtual; abstract;
    procedure Describe;
    function Kind: string; virtual;
    property Name: string read FName;
  end;

  TRect = class(TShape)
  private
    FW, FH: Double;
  public
    constructor Create(W, H: Double);
    function Area: Double; override;
  end;

  TSquare = class(TRect)
    constructor Create(S: Double);
  end;

  TCircle = class(TShape)
    FR: Double;
    constructor Create(R: Double);
    function Area: Double; override;
    function Kind: string; override;
    destructor Destroy; override;
  end;

constructor TShape.Create(const AName: string);
begin
  inherited Create;
  FName := AName;
end;

procedure TShape.Describe;
begin
  Writeln(Name, ' ', Area:0:2, ' ', Kind);
end;

function TShape.Kind: string;
begin
  Result := 'shape';
end;

constructor TRect.Create(W, H: Double);
begin
  inherited Create('rect');
  FW := W;
  FH := H;
end;

function TRect.Area: Double;
begin
  Result := FW * FH;
end;

constructor TSquare.Create(S: Double);
begin
  inherited Create(S, S);
  FName := 'square';
end;

constructor TCircle.Create(R: Double);
begin
  inherited Create('circle');
  FR := R;
end;

function TCircle.Area: Double;
begin
  Result := Pi * FR * FR;
end;

function TCircle.Kind: string;
begin
  Result := 'round ' + inherited Kind;
end;

destructor TCircle.Destroy;
begin
  Writeln('circle gone');
  inherited;
end;

var
  S: TShape;
  Shapes: array[0..2] of TShape;
  I: Integer;
  Total: Double;
begin
  S := TRect.Create(2, 3);
  S.Describe;
  Writeln(S is TRect, ' ', S is TCircle);
  S.Free;
  S := TCircle.Create(1);
  S.Describe;
  S.Free;

  Shapes[0] := TSquare.Create(4);
  Shapes[1] := TCircle.Create(2);
  Shapes[2] := TRect.Create(1, 5);
  Total := 0;
  for I := 0 to 2 do
    Total := Total + Shapes[I].Area;
  Writeln('total ', Total:0:3);
  for I := 2 downto 0 do
  begin
    Write(Shapes[I].Kind, ': ');
    Shapes[I].Describe;
    Shapes[I].Free;
  end;
end.
