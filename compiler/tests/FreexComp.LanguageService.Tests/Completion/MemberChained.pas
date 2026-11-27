{ context: member-field }
program MemberChained;
type
  TEngine = class
    Power: Integer;
    procedure Start;
  end;
  TCar = class
    Engine: TEngine;
  end;
procedure TEngine.Start; begin end;
var
  Car: TCar;
begin
  Car.Engine.{|}
end.
{#has Power, Start}
{#not Engine}
