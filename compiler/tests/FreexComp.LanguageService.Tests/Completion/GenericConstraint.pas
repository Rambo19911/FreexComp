{ context: generic-constraint }
program GenericConstraint;
type
  TShape = class
    function Area: Double; virtual;
    procedure Paint;
  end;
  TCanvas<T: TShape> = class
    procedure Render(Item: T);
  end;
function TShape.Area: Double; begin Result := 0 end;
procedure TShape.Paint; begin end;
procedure TCanvas<T>.Render(Item: T);
begin
  Item.{|}
end;
begin
end.
{#has Area, Paint, Free}
{#not Render}
