// opc: -O1
// covers: RTL-920
program Perf_Strings;
{ testing.md section 4 (task R-122): 1 000 000 appends of one character; the bound (2 s for the run) is checked by
  PerfGuardTests, the output only proves the work was done. }
var
  S: string;
  I: Integer;
begin
  S := '';
  for I := 1 to 1000000 do
    S := S + 'x';
  Writeln(Length(S));
end.