{ context: strict-private }
program StrictPrivateRecordOwnMethod;
type
  TToken = record
  strict private
    FRaw: string;
  public
    function Text: string;
  end;
function TToken.Text: string;
begin
  Result := {|}
end;
begin
end.
{#has FRaw, Text, Result}
