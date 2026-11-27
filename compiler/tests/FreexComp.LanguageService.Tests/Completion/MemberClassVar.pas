{ context: member-class-var }
program MemberClassVar;
type
  TRegistry = class
  public
    Name: string;
    class var Instances: Integer;
  end;
begin
  TRegistry.{|}
end.
{#has Instances}
{#not Name}
