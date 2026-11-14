// covers: RTL-910, RTL-912
program TestKit_Self;
{ The TestKit unit itself (testing.md §2): passing checks print "ok <Name>", failing ones
  "FAIL <Name>: expected <e> got <a>" with strings quoted and control characters as #nn; Section prints
  "# <Title>"; Done prints "# passed <p> failed <f>" and halts with exit code 1 because checks failed. }
uses TestKit;

var
  NegZero, One, NextUp: Double;
  Bits: Int64;

begin
  Section('passing');
  Check(True, 'check true');
  CheckEqS('abc', 'abc', 'string equal');
  CheckEqS('', '', 'empty strings');
  CheckEqI(-9223372036854775807 - 1, Low(Int64), 'int64 extreme');
  CheckEqB(False, False, 'boolean equal');
  One := 1.0;
  Bits := $3FF0000000000001;
  Move(Bits, NextUp, SizeOf(NextUp));
  CheckEqF(One, NextUp, 1, 'one ulp apart');
  CheckEqF(One, One, 0, 'identical double');
  NegZero := -0.0;
  CheckEqF(0.0, NegZero, 0, 'signed zeros');

  Section('failing');
  Check(False, 'check false');
  CheckEqS('it''s', 'a'#13#10'b', 'string quoting');
  CheckEqS('', #9, 'control only');
  CheckEqI(42, -7, 'integer');
  CheckEqB(True, False, 'boolean');
  CheckEqF(One, NextUp, 0, 'float ulps');
  Done;
  Writeln('not reached');
end.
