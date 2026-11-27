{ context: member-field }
program MemberField;
type
  TPoint = record
    X, Y: Integer;
  end;
var
  Pt: TPoint;
begin
  Pt.{|}
end.
{#has X, Y}
{#kind X=Field, Y=Field}
{#not Pt, Writeln, begin}
