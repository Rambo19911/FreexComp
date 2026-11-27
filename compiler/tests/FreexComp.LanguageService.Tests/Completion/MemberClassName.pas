{ context: member-class }
program MemberClassName;
type
  TShape = class
    FWidth: Integer;
    constructor Create(AWidth: Integer);
    class function Standard: TShape;
    procedure Draw;
  end;
constructor TShape.Create(AWidth: Integer); begin FWidth := AWidth end;
class function TShape.Standard: TShape; begin Result := TShape.Create(1) end;
procedure TShape.Draw; begin end;
var
  S: TShape;
begin
  S := TShape.{|}
end.
{#has Create, Standard}
{#kind Standard=Method}
{#not Draw, FWidth, Free}
