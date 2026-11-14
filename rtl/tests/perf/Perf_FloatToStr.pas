// opc: -O1
// covers: RTL-920
program Perf_FloatToStr;
{ testing.md section 4 (task R-122): 100 000 FloatToStr conversions of values needing all 15 digits; the bound (2 s
  for the run) is checked by PerfGuardTests. }
uses
  SysUtils;

var
  S: string;
  I: Integer;
begin
  for I := 1 to 100000 do
    S := FloatToStr(I / 7);
  Writeln(S);
end.