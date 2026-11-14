unit Canvas;

interface

uses Palette;

function MakeRow: TRow;
function Colors(const Row: TRow): TColors;

implementation

function MakeRow: TRow;
var
  I: Integer;
begin
  for I := 1 to 3 do
    Result[I] := Pixel(I, I * 10, TColor(I));
end;

function Colors(const Row: TRow): TColors;
var
  I: Integer;
begin
  Result := [];
  for I := Low(Row) to High(Row) do
    Include(Result, Row[I].Color);
end;

end.
