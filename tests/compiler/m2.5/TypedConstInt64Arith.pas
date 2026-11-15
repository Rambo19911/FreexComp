// covers: LANG-221, LANG-222, LANG-341 (C-208)
program TypedConstInt64Arith;

{ A constant declared as a typecast to Int64 or UInt64 keeps its 64-bit type, so an Integer (or Cardinal) operand
  is widened and + - * div are computed in 64 bits; in 32 bits every line below would wrap or overflow. }

const
  MsPerDay = Int64(86400000);
  Two = Int64(2000000000);
  MinusOne = Int64(-1);
  Billion = UInt64(1000000000);
  Five = UInt64(5);
  Typed: Int64 = 86400000;

var
  N: Integer;
  C: Cardinal;
begin
  N := -60;
  Writeln(N * MsPerDay);
  Writeln(MsPerDay * N);
  Writeln(N * Typed);
  N := 2000000000;
  Writeln(N + Two);
  N := -2000000000;
  Writeln(N - Two);
  N := Low(Integer);
  Writeln(N div MinusOne);
  Writeln(N * MinusOne);
  C := 4000000000;
  Writeln(C + Billion);
  Writeln(C * Five);
  Writeln(SizeOf(MsPerDay), ' ', SizeOf(Billion));
end.
