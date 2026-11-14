// covers: LANG-513, LANG-539
program UnitCircularInterface;
// Alpha -> Beta -> Gamma -> Alpha through interface uses clauses: E2205 in Alpha (the cycle's unit first reached
// from the program), at its entry that opens the cycle
uses Alpha;
begin
end.
