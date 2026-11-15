// covers: LANG-700, LANG-704, LANG-710
program GenericRecord;

type
  TPair<K, V> = record
    Key: K;
    Value: V;
    constructor Create(const AKey: K; const AValue: V);
    function Swapped: TPair<V, K>;
    procedure Show;
  end;

constructor TPair<K, V>.Create(const AKey: K; const AValue: V);
begin
  Key := AKey;
  Value := AValue;
end;

function TPair<K, V>.Swapped: TPair<V, K>;
begin
  Result.Key := Value;
  Result.Value := Key;
end;

procedure TPair<K, V>.Show;
begin
  Writeln(Key, ' = ', Value);
end;

var
  P: TPair<string, Integer>;
  Q: TPair<Integer, string>;
  R: TPair<string, Integer>;
begin
  P := TPair<string, Integer>.Create('one', 1);
  P.Show;
  Q := P.Swapped;
  Q.Show;
  R := Q.Swapped;
  R.Show;
  Writeln(SizeOf(TPair<Byte, Int64>));
  P.Value := P.Value + 41;
  Writeln(P.Key, ' ', P.Value);
end.