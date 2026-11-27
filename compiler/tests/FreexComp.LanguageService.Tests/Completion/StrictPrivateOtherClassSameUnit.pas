{ context: strict-private }
program StrictPrivateOtherClassSameUnit;
type
  TKey = class
  strict private
    FSecret: Integer;
    procedure Rotate;
  private
    FPlain: Integer;
  public
    procedure Use;
  end;
  TKeyRing = class
    procedure Inspect(K: TKey);
  end;
procedure TKey.Rotate; begin end;
procedure TKey.Use; begin end;
procedure TKeyRing.Inspect(K: TKey);
begin
  K.{|}
end;
begin
end.
{#has FPlain, Use}
{#not FSecret, Rotate}
