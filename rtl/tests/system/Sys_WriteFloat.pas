// covers: RTL-100, RTL-101, RTL-102, RTL-103, RTL-104
program Sys_WriteFloat;
{ Write of reals (_WriteFloat), as the language defines it (LANG-930...932): scientific form with W - 9 fraction digits
  (W = 23 by default, 1..16), fixed form with d decimals (at most 40), rounding half away from zero on the exact binary
  value, right-aligned in the width; every row ends in '|' to show the padding. }
var
  Zero, D, NaN, Inf: Double;
  S: Single;
  W, N: Integer;
begin
  Writeln('# scientific');
  Writeln(1.0, '|');
  Writeln(-1234.5, '|');
  Writeln(0.1, '|');
  Writeln(1.0:10, '|');
  Writeln(123456789.0:12, '|');
  Writeln(2.5:0, '|');
  Writeln(0.0, '|');
  Writeln(1.5:30, '|');
  Writeln(1.5:-5, '|');
  Writeln(1.5:9, '|');
  Writeln(9.96:10, '|');
  Writeln(0.95:10, '|');
  Writeln(9.5:10, '|');
  Writeln(0.125:10, '|');
  Writeln(0.375:10, '|');
  Writeln(-0.125:10, '|');
  Writeln(0.125:11, '|');
  Writeln(99999.5:13, '|');
  Writeln(1e23, '|');
  Writeln(1e23:30, '|');
  Writeln(2e-10:25, '|');
  Writeln(5e-324, '|');
  Writeln(5e-324:26, '|');
  Writeln(2.2250738585072014e-308, '|');
  Writeln(1.7976931348623157e308, '|');
  Writeln(1e100, '|');
  Writeln(-1e-100:12, '|');

  Writeln('# fixed');
  Writeln(3.14159:0:2, '|');
  Writeln(2.5:0:0, '|');
  Writeln(3.5:0:0, '|');
  Writeln(0.125:0:2, '|');
  Writeln(1.005:0:2, '|');
  Writeln(-7.25:8:1, '|');
  Writeln(1e20:0:1, '|');
  Writeln(-0.001:0:2, '|');
  Writeln(99.95:0:1, '|');
  Writeln(9.995:0:2, '|');
  Writeln(999.9999:0:3, '|');
  Writeln(0.0005:0:3, '|');
  Writeln(0.00049:0:3, '|');
  Writeln(0.4:0:0, '|');
  Writeln(0.5:0:0, '|');
  Writeln(0.6:0:0, '|');
  Writeln(-0.6:0:0, '|');
  Writeln(-0.4:0:0, '|');
  Writeln(0.05:0:1, '|');
  Writeln(0.04:0:1, '|');
  Writeln(0.004:0:1, '|');
  Writeln(0.95:0:0, '|');
  Writeln(1.5:0:45, '|');
  Writeln(0.1:0:40, '|');
  Writeln(2.5:5:-1, '|');
  Writeln(123.456:12:3, '|');
  Writeln(1.5:-3:1, '|');
  Writeln(0.0:0:3, '|');
  Writeln(5e-324:0:2, '|');
  Writeln(1e300:0:0, '|');
  Writeln(1.7976931348623157e308:0:1, '|');
  Writeln(2251799813685248.5:0:0, '|');
  Writeln(-2251799813685249.5:0:0, '|');

  Writeln('# variables, Single, -0');
  W := 6;
  N := 1;
  D := 3.14159;
  Write(D:W:N, '|');                     { expect:    3.1| 3.14159000000000E+0000| }
  Writeln(D, '|');
  S := 0.1;
  Write(S, '|');                         { expect:  1.00000001490116E-0001|0.1000000015| }
  Writeln(S:0:10, '|');
  Zero := 0;
  D := -Zero;
  Write(D, '|');                         { expect:  0.00000000000000E+0000|0.0| }
  Writeln(D:0:1, '|');

  Writeln('# specials');
  Inf := 1 / Zero;
  NaN := Zero / Zero;
  Write(NaN, '|', Inf, '|');             { expect:                     NAN|                    INF|                   -INF| }
  Writeln(-Inf, '|');
  Write(NaN:0, '|', Inf:4, '|');         { expect: NAN| INF|-INF|   NAN|  INF|NAN| }
  Writeln(-Inf:0:2, '|', NaN:6:1, '|', Inf:5:-1, '|', -NaN:0:0, '|');
  S := -Inf;
  Writeln(S:5, '|', S:0:0, '|');         { expect:  -INF|-INF| }
end.
