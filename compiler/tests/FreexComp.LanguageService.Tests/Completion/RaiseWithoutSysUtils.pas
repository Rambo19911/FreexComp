{ context: raise }
program RaiseWithoutSysUtils;
type
  TFailure = class end;
begin
  raise {|}
end.
{#has TFailure, TObject}
{#not Integer, Exception}
