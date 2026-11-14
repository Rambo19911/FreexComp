unit Shapes;

interface

type
  TRect = record
    Width, Height: Integer;
  end;

const
  Sides = 4;

var
  Created: Integer;

function MakeRect(W, H: Integer): TRect;
function Area(const R: TRect): Integer;
procedure Describe(const R: TRect);

implementation

function MakeRect(W, H: Integer): TRect;
begin
  Result.Width := W;
  Result.Height := H;
  Inc(Created);
end;

function Area(const R: TRect): Integer;
begin
  Result := R.Width * R.Height;
end;

procedure Describe(const R: TRect);
begin
  Writeln(R.Width, 'x', R.Height, ' sides=', Sides);
end;

end.
