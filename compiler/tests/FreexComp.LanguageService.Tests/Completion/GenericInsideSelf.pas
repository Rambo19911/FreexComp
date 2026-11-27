{ context: generic-inside }
program GenericInsideSelf;
type
  TQueue<T> = class
  private
    FHead: Integer;
  public
    procedure Enqueue(const Item: T);
    function Size: Integer;
  end;
procedure TQueue<T>.Enqueue(const Item: T);
begin
  Self.{|}
end;
function TQueue<T>.Size: Integer; begin Result := 0 end;
var
  Q: TQueue<string>;
begin
  Q := TQueue<string>.Create;
end.
{#has FHead, Enqueue, Size, Free}
{#not Item}
