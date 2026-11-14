// opc: -O1
// covers: RTL-920
program Perf_Format;
{ testing.md section 4 (task R-122): 100 000 Format calls with an integer, a string and a fixed-point argument; the
  bound (2 s for the run) is checked by PerfGuardTests. }
uses
  SysUtils;

var
  S: string;
  I: Integer;
begin
  for I := 1 to 100000 do
    S := Format('%d %s %.2f', [I, 'abc', I / 3]);
  Writeln(S);
end.