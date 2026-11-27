program ConstructorCall;
type
  TItem = class
    N: Integer;
    constructor Create(AN: Integer);
  end;
constructor TItem.{#}Create(AN: Integer);
begin
  N := AN;
end;
begin
  Writeln(TItem.{|}Create(7).N);
end.
