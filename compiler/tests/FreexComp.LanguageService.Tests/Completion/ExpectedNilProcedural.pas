{ context: expected-type }
program ExpectedNilProcedural;
type
  TCallback = procedure(Value: Integer);
  TNotify = procedure of object;
var
  Callback: TCallback;
  Notify: TNotify;
  Alpha: Integer;
begin
  Callback := {|}
end.
{#ranked nil, Callback}
{#unranked Alpha, Notify}
