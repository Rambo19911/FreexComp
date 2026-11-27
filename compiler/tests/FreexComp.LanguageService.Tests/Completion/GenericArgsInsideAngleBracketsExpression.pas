{ context: type-position }
program GenericArgsInsideAngleBracketsExpression;
type
  TBox<T> = class
    Value: T;
  end;
  TItem = record
    Id: Integer;
  end;
var
  Alpha: TObject;
begin
  Alpha := TBox<{|}>.Create;
end.
{#has TItem, Integer, string, TBox}
{#not Alpha}
