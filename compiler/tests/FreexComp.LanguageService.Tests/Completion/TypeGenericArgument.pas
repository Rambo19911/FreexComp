{ context: type-position }
program TypeGenericArgument;
type
  TBox<T> = class
    Value: T;
  end;
  TItem = record
    Id: Integer;
  end;
var
  B: TBox<{|}
begin
end.
{#has TItem, Integer, string}
{#not B}
