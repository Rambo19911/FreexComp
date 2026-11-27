program Label;
procedure Work(N: Integer);
label {#}Again;
begin
Again:
  N := N + 1;
  if N < 3 then goto {|}Again;
  Writeln(N);
end;
begin
  Work(0);
end.
