{ context: generic-inside }
program GenericInside;
type
  TStack<T> = class
  private
    FItems: array of T;
    FCount: Integer;
  public
    procedure Push(const Item: T);
    function Peek: T;
  end;
procedure TStack<T>.Push(const Item: T);
begin
  {|}
end;
function TStack<T>.Peek: T; begin Result := FItems[FCount - 1] end;
begin
end.
{#has Item, FItems, FCount, Push, Peek}
{#kind Item=Parameter, FCount=Field}
{#top Item}
