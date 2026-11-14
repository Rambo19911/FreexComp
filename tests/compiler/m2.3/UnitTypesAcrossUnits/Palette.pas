unit Palette;

interface

type
  TColor = (Red, Green, Blue, Yellow);
  TColors = set of TColor;
  TPixel = record
    X, Y: Integer;
    Color: TColor;
  end;
  TRow = array[1..3] of TPixel;

const
  Warm = [Red, Yellow];

function ColorName(C: TColor): string;
function Pixel(X, Y: Integer; C: TColor): TPixel;

implementation

function ColorName(C: TColor): string;
begin
  case C of
    Red: Result := 'red';
    Green: Result := 'green';
    Blue: Result := 'blue';
  else
    Result := 'yellow';
  end;
end;

function Pixel(X, Y: Integer; C: TColor): TPixel;
begin
  Result.X := X;
  Result.Y := Y;
  Result.Color := C;
end;

end.
