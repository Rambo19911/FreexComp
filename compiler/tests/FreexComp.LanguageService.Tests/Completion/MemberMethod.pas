{ context: member-method }
program MemberMethod;
type
  TCounter = class
    procedure Increment;
    function Current: Integer;
  end;
procedure TCounter.Increment; begin end;
function TCounter.Current: Integer; begin Result := 0 end;
var
  C: TCounter;
begin
  C.{|}
end.
{#has Increment, Current, Free, ClassName}
{#kind Increment=Method, Current=Method}
{#not C, TCounter}
