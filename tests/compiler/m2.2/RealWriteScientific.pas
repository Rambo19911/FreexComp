// covers: LANG-930, LANG-915, CMP-410
program RealWriteScientific;
var
  D: Double;
  S: Single;
  W: Integer;
begin
  { the LANG-930 example table: default width 23 gives 15 significant digits }
  Writeln(1.5);
  Writeln(-1234.5678);
  Writeln(0.0);
  Writeln(1e-10);
  Writeln(0.1);
  D := 2;
  Writeln(D / 3);
  { widths: D = min(max(W - 9, 1), 16) fraction digits, then padding }
  Writeln(1.5:10, '|');
  Writeln(1.5:12, '|');
  Writeln(1.5:0, '|');
  Writeln(1.5:30, '|');
  Writeln(1.5:-5, '|');
  W := 14;
  Writeln(2.5:W, '|');
  Writeln(-2.5e-300:12, '|');
  { -0 prints with a space }
  D := 0;
  D := -D;
  Writeln(D);
  { rounding half away from zero on the exact binary value, with carries into the exponent }
  Writeln(1e23);
  Writeln(1e23:30);
  Writeln(0.95:10);
  Writeln(9.96:10);
  Writeln(123456789012345678.0);
  { extremes }
  Writeln(1.7976931348623157e308);
  Writeln(5e-324);
  { a Single is converted to Double exactly }
  S := 0.1;
  Writeln(S);
  Write(1.5, 2.5:10);
  Writeln;
end.
