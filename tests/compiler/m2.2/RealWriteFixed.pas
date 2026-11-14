// covers: LANG-931, LANG-915, CMP-410
program RealWriteFixed;
var
  D: Double;
  S: Single;
  W, N: Integer;
begin
  { the LANG-931 example table }
  Writeln(3.14159:0:2);
  Writeln(3.14159:8:2, '|');
  Writeln(2.5:0:0);
  Writeln(0.5:0:0);
  Writeln(-0.001:0:2);
  Writeln(-0.005:0:2);
  Writeln(1.005:0:2);
  Writeln(0.125:0:2);
  Writeln(1e20:0:0);
  Writeln(123.456:0:1);
  { more rounding edge cases (exact binary values decide; ties go away from zero) }
  Writeln(1.5:0:0);
  Writeln(-0.125:0:2);
  Writeln(9.995:0:2);
  Writeln(99.95:0:1);
  Writeln(-99.96:7:1, '|');
  Writeln(0.0005:0:3);
  Writeln(0.00049:0:3);
  Writeln(0.7:0:0);
  Writeln(0.4:0:0);
  Writeln(-0.4:0:0);
  Writeln(-0.6:0:0);
  Writeln(0.1:0:20);
  Writeln(1234567.891:12:3, '|');
  { zero and -0: no minus sign }
  Writeln(0.0:0:3);
  D := 0;
  D := -D;
  Writeln(D:0:1);
  { more than 40 decimals are 40; negative decimals select the scientific form in width w }
  Writeln(1.5:0:45);
  Writeln(2.5:5:-1, '|');
  { variable width and decimals, Single }
  W := 6;
  N := 1;
  Writeln(3.14159:W:N, '|');
  S := 0.1;
  Writeln(S:0:10);
end.
