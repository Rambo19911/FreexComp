{ context: type-position }
program TypeGenericSecondArgument;
type
  TPair<K, V> = record
    Key: K;
    Value: V;
  end;
  TColor = (Red, Green);
var
  P: TPair<string, {|}>;
begin
end.
{#has TColor, Integer, TPair}
{#not P, Red}
