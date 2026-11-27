program DirectivesDisabled;
{$APPTYPE CONSOLE}
var
  I: Integer;
begin
I := 0;
      {$IFDEF NEVER_DEFINED_SYMBOL}
   this   text is not   Pascal at all;
        and keeps its spacing
            {$ELSE}
I := 1;
  {$ENDIF}
  if I = 1 then
    {$IFNDEF NEVER_DEFINED_SYMBOL}
Writeln('on');
    {$ENDIF}
end.
