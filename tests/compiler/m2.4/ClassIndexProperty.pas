// covers: LANG-643
program ClassIndexProperty;

{ Four properties sharing one getter and one setter through 'index N' (the accessors receive N first); an array
  property with 'index' (the accessor receives the array index, then N). }

type
  TRect = class
  private
    FCoords: array[0..3] of Integer;
    function GetCoord(Index: Integer): Integer;
    procedure SetCoord(Index: Integer; Value: Integer);
    function GetPlus(I: Integer; Index: Integer): Integer;
  public
    property Left: Integer index 0 read GetCoord write SetCoord;
    property Top: Integer index 1 read GetCoord write SetCoord;
    property Right: Integer index 2 read GetCoord write SetCoord;
    property Bottom: Integer index 3 read GetCoord write SetCoord;
    property Plus[I: Integer]: Integer index 100 read GetPlus;
  end;

function TRect.GetCoord(Index: Integer): Integer;
begin
  Result := FCoords[Index];
end;

procedure TRect.SetCoord(Index: Integer; Value: Integer);
begin
  Writeln('set ', Index, ' = ', Value);
  FCoords[Index] := Value;
end;

function TRect.GetPlus(I: Integer; Index: Integer): Integer;
begin
  Result := I + Index;
end;

var
  R: TRect;
begin
  R := TRect.Create;
  R.Left := 1;
  R.Top := 2;
  R.Right := 30;
  R.Bottom := 40;
  Writeln(R.Left, ' ', R.Top, ' ', R.Right, ' ', R.Bottom);
  Writeln(R.Right - R.Left, 'x', R.Bottom - R.Top);
  Writeln(R.Plus[5]);
  R.Free;
end.
