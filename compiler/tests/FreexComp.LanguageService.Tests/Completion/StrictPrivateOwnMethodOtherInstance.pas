{ context: strict-private }
program StrictPrivateOwnMethodOtherInstance;
type
  TKey = class
  strict private
    FSecret: Integer;
  public
    function Same(Other: TKey): Boolean;
  end;
function TKey.Same(Other: TKey): Boolean;
begin
  Result := Other.{|}
end;
begin
end.
{#has FSecret, Same}
