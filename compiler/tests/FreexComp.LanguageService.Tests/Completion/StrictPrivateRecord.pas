{ context: strict-private }
program StrictPrivateRecord;
type
  TToken = record
  strict private
    FRaw: string;
  private
    FKind: Integer;
  public
    function Text: string;
  end;
function TToken.Text: string; begin Result := FRaw end;
var
  T: TToken;
begin
  T.{|}
end.
{#has FKind, Text}
{#not FRaw}
