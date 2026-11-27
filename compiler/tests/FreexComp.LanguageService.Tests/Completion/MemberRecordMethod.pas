{ context: member-method }
program MemberRecordMethod;
type
  TSize = record
    Width, Height: Integer;
    function Area: Integer;
    procedure Grow(By: Integer);
  end;
function TSize.Area: Integer; begin Result := Width * Height end;
procedure TSize.Grow(By: Integer); begin Inc(Width, By) end;
var
  S: TSize;
begin
  S.{|}
end.
{#has Width, Height, Area, Grow}
{#kind Area=Method, Width=Field}
