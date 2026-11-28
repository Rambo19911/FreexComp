{ context: generic-inside }
program AnonymousGenericInside;
type
  TProc = reference to procedure;
  TStack<T> = class
  private
    FItems: array of T;
    FCount: Integer;
  public
    procedure Push(const Item: T);
  end;
procedure TStack<T>.Push(const Item: T);
var
  Later: TProc;
begin
  Later := procedure
    var Kept: T;
    begin
      Kept := Item;
      {|}
    end;
  Later();
end;
begin
end.
{#has Kept, Item, Later, FItems, FCount, Push}
{#kind Item=Parameter, Kept=Variable, FCount=Field}
{#top Item}
