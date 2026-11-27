{ context: strict-private }
program StrictPrivateMainProgram;
type
  TKey = class
  strict private
    FSecret: Integer;
  private
    FPlain: Integer;
  public
    procedure Use;
  end;
procedure TKey.Use; begin end;
var
  K: TKey;
begin
  K.{|}
end.
{#has FPlain, Use}
{#not FSecret}
