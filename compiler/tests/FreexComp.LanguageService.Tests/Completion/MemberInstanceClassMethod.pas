{ context: member-instance }
program MemberInstanceClassMethod;
type
  TShape = class
    FWidth: Integer;
    class function Standard: TShape;
    procedure Draw;
  end;
class function TShape.Standard: TShape; begin Result := nil end;
procedure TShape.Draw; begin end;
var
  S: TShape;
begin
  S.{|}
end.
{#has Draw, FWidth, Standard, ClassName, Free}
