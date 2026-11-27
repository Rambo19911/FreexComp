{ context: broken }
program BrokenMemberAfterError;
type
  TPoint = record
    X, Y: Integer;
  end;
procedure Work;
var
  Pt: TPoint;
begin
  Pt.X := 1 +;
  Writeln(Pt.Y;
  Pt.{|}
end;
begin
end.
{#has X, Y}
{#not Pt}
