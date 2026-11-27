{ context: strict-protected }
program StrictProtectedOwnMethod;
type
  TBase = class
  strict protected
    FState: Integer;
  public
    procedure Run;
  end;
procedure TBase.Run;
begin
  Self.{|}
end;
begin
end.
{#has FState, Run}
