program ForwardRoutine;
procedure Later; forward;
procedure First;
begin
  {|}Later;
end;
procedure {#}Later;
begin
  Writeln('later');
end;
begin
  First;
end.
