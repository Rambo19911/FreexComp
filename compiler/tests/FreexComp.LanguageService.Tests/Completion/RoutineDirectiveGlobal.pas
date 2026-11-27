{ context: routine-directive }
program RoutineDirectiveGlobal;
procedure Helper(A: Integer); {|}
begin
end;
begin
end.
{#has overload, inline, stdcall, forward}
{#not Writeln}
