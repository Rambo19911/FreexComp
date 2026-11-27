{ context: expected-type }
program ExpectedBoolean;
var
  Alpha: Integer;
  Done: Boolean;
begin
  Done := {|}
end.
{#ranked True, False, Done}
{#unranked Alpha, nil, not}
