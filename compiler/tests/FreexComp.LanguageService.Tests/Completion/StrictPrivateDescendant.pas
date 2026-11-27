{ context: strict-private }
program StrictPrivateDescendant;
type
  TKey = class
  strict private
    FSecret: Integer;
  strict protected
    FShared: Integer;
  end;
  TMasterKey = class(TKey)
    procedure Copy;
  end;
procedure TMasterKey.Copy;
begin
  {|}
end;
begin
end.
{#has FShared, Copy}
{#not FSecret}
