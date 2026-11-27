{ context: generic-instantiated }
program GenericInstantiatedRecord;
type
  TPair<K, V> = record
    Key: K;
    Value: V;
    function Swapped: TPair<V, K>;
  end;
function TPair<K, V>.Swapped: TPair<V, K>; begin Result.Key := Value; Result.Value := Key end;
var
  Entry: TPair<string, Integer>;
begin
  Entry.{|}
end.
{#has Key, Value, Swapped}
