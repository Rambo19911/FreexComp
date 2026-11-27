{ context: member-field }
program MemberPointerDeref;
type
  PItem = ^TItem;
  TItem = record
    Key: Integer;
    Next: PItem;
  end;
var
  P: PItem;
begin
  P^.{|}
end.
{#has Key, Next}
{#kind Key=Field}
