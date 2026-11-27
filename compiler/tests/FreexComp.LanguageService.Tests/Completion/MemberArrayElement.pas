{ context: member-field }
program MemberArrayElement;
type
  TPoint = record
    X, Y: Integer;
  end;
var
  Points: array[0..3] of TPoint;
begin
  Points[1].{|}
end.
{#has X, Y}
{#not Points}
