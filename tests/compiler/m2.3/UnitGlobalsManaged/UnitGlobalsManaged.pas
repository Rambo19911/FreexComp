// covers: LANG-523, CMP-325, CMP-350, CMP-355, CMP-912
program UnitGlobalsManaged;
// Probe is initialised first, so it is finalised last: after Data's managed globals and the program's own
uses Probe, Data;

var
  Own: string;
begin
  Own := Greeting + '!';
  Writeln(Own, ' ', Length(Numbers), ' ', Pair.Name);
end.