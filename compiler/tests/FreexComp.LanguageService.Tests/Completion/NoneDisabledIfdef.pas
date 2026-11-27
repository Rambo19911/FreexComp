{ context: none-disabled }
program NoneDisabledIfdef;
var
  X: Integer;
begin
{$IFDEF NEVER_DEFINED}
  X := {|}
{$ENDIF}
end.
{#none}
