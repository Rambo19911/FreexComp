{ context: type-position }
program GenericArgsInsideAngleBrackets;
type
  TBox<T> = class
    Value: T;
  end;
  TItem = record
    Id: Integer;
  end;
var
  B: TBox<{|}>;
  Alpha: Integer;
begin
end.
{#has TItem, Integer, string, TBox}
{#not B, Alpha}
