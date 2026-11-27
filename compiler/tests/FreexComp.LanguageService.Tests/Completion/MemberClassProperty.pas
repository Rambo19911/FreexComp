{ context: member-class-var }
program MemberClassProperty;
type
  TSettings = class
  private
    FOwnName: string;
    class var FLevel: Integer;
  public
    class property Level: Integer read FLevel write FLevel;
    property OwnName: string read FOwnName;
  end;
begin
  TSettings.{|}
end.
{#has Level, FLevel}
{#kind Level=Property}
{#not OwnName, FOwnName}
