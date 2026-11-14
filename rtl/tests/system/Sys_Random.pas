// covers: RTL-040
program Sys_Random;
{ Random, Random(Range), Randomize, RandSeed (system.md section 5.2, RTL-040). The generator is the fixed LCG
  RandSeed := RandSeed * 134775813 + 1 (mod 2^32); the expected numbers are computed from that formula and frozen
  in the .out file. A Random value is RandSeed * 2^-32, so it is printed both as RandSeed and scaled back. }
var
  I, N: Integer;
  D: Double;
  Range: Integer;
  Saved: Cardinal;
  Differs, InRange: Boolean;

begin
  Writeln('initial RandSeed ', RandSeed);

  Writeln('Random for RandSeed = 1:');
  RandSeed := 1;
  for I := 1 to 5 do
  begin
    D := Random;
    Writeln(RandSeed, ' ', D:0:15, ' ', Trunc(D * 4294967296.0) = RandSeed);
  end;

  Writeln('Random(Range) for RandSeed = 1:');
  RandSeed := 1;
  Range := 10;
  for I := 1 to 5 do
    Write(' ', Random(Range));
  Writeln;
  RandSeed := 1;
  Range := 1000000;
  for I := 1 to 5 do
    Write(' ', Random(Range));
  Writeln;
  RandSeed := 1;
  Range := MaxInt;
  for I := 1 to 5 do
    Write(' ', Random(Range));
  Writeln;

  Writeln('Range = 1 and Range <= 0 give 0:');
  RandSeed := 1;
  Range := 1;
  Write(Random(Range), ' ', RandSeed);
  Range := 0;
  Write(' ', Random(Range), ' ', RandSeed);
  Range := -5;
  Writeln(' ', Random(Range), ' ', RandSeed);

  Writeln('wrap-around of the seed:');
  RandSeed := $FFFFFFFF;
  D := Random;
  Writeln(RandSeed);

  Writeln('same seed, same sequence:');
  RandSeed := 12345;
  N := Random(1000);
  RandSeed := 12345;
  Writeln(N = Random(1000));

  { 1000 values of Random stay in [0, 1); Random(7) stays in 0..6 }
  RandSeed := 777;
  InRange := True;
  for I := 1 to 1000 do
  begin
    D := Random;
    if (D < 0) or (D >= 1) then
      InRange := False;
    N := Random(7);
    if (N < 0) or (N > 6) then
      InRange := False;
  end;
  Writeln('in range: ', InRange);

  { Randomize sets a seed from the clock; the value is unpredictable, so only check that the generator still
    works from it: two consecutive draws after Randomize differ from each other (the LCG has full period) }
  Randomize;
  Saved := RandSeed;
  D := Random;
  Differs := RandSeed <> Saved;
  Writeln('Randomize: ', Differs);
end.
