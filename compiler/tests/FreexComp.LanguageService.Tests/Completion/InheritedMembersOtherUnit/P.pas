{ context: inherited-members }
program P;
uses Shapes;
type
  TSquare = class(TShape)
    procedure Resize(Size: Integer);
  end;
procedure TSquare.Resize(Size: Integer); begin end;
var
  Sq: TSquare;
begin
  Sq.{|}
end.
{#has Resize, Draw, Name, Free, Guarded}
{#not FName, Hidden}
