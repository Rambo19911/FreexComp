{ context: none-disabled }
program NoneDisabledElse;
var
  X: Integer;
begin
{$IFNDEF NEVER_DEFINED}
  X := 1;
{$ELSE}
  X := {|}
{$ENDIF}
end.
{#none}
