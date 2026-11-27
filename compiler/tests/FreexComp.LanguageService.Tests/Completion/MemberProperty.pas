{ context: member-property }
program MemberProperty;
type
  TList = class
  private
    FCount: Integer;
    function GetItem(Index: Integer): string;
  public
    property Count: Integer read FCount;
    property Items[Index: Integer]: string read GetItem; default;
  end;
function TList.GetItem(Index: Integer): string; begin Result := '' end;
var
  L: TList;
begin
  L.{|}
end.
{#has Count, Items}
{#kind Count=Property, Items=Property}
