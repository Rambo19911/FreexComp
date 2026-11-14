// covers: LANG-310, LANG-311, LANG-516, LANG-538
program UnitQualifiedNames;
// two units export the same names: unqualified references denote the unit listed later, qualification reaches
// either one (also the implicitly used System)
uses First, Second;

begin
  Writeln(Greeting);
  Writeln(First.Greeting);
  Writeln(Second.Greeting);
  Counter := 5;
  First.Counter := 7;
  Writeln(First.Counter, ' ', Second.Counter, ' ', Counter);
  Writeln(Twice(3), ' ', First.Twice(3), ' ', Second.Twice(3));
  Writeln(System.Length(First.Greeting));
end.
