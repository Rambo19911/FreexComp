{ context: interface }
program InterfaceMembers;
type
  IGreeter = interface
    procedure Greet(const Who: string);
    function GetCount: Integer;
    property Count: Integer read GetCount;
  end;
var
  G: IGreeter;
begin
  G.{|}
end.
{#has Greet, GetCount, Count, QueryInterface}
{#kind Greet=Method, Count=Property}
{#not Free, ClassName}
