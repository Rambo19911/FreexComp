{ context: type-position }
program TypeAfterAs;
type
  TAnimal = class end;
  TCat = class(TAnimal) end;
var
  A: TAnimal;
begin
  (A as {|}).Free;
end.
{#has TCat, TAnimal}
{#not A}
