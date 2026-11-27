{ context: strict-private }
program StrictPrivateOwnMethod;
type
  TKey = class
  strict private
    FSecret: Integer;
    procedure Rotate;
  public
    procedure Use;
  end;
procedure TKey.Rotate; begin end;
procedure TKey.Use;
begin
  {|}
end;
begin
end.
{#has FSecret, Rotate, Use}
{#kind FSecret=Field}
