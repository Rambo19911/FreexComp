{ context: member-constant }
program MemberConstant;
type
  TBuffer = class
  public
    Data: Integer;
    const Capacity = 64;
    const Name = 'buffer';
  end;
var
  N: Integer;
begin
  N := TBuffer.{|}
end.
{#has Capacity, Name}
{#kind Capacity=Constant, Name=Constant}
{#not Data}
