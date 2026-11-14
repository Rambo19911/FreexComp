// covers: LANG-521, LANG-524, CMP-356, ABI-104
program UnitHaltRunsFinalization;
// Halt inside a unit routine runs the finalization sections in reverse order and ends with its code
uses Logger, Stopper;
begin
  Writeln('main');
  Stop(2);
  Writeln('not reached');
end.