{ context: strict-protected }
program StrictProtectedMainProgram;
type
  TBase = class
  strict protected
    FState: Integer;
  public
    procedure Run;
  end;
procedure TBase.Run; begin end;
var
  B: TBase;
begin
  B.{|}
end.
{#has Run}
{#not FState}
