// covers: LANG-533, LANG-535, LANG-536, LANG-538
program UnitAliasScoped;
// System.Types is an alias of Types in the RTL's unit-aliases.json (LANG-533); the candidate Types is found in this
// folder (LANG-510 step 3). AliasUser writes "uses Types", this program "uses System.Types": one unit, initialised once
// (LANG-536); both the written and the canonical name qualify (LANG-538)
uses AliasUser, System.Types;
begin
  Writeln(System.Types.Twice(2));
  Writeln(Types.Twice(3));
  Writeln(Twice(4));
  Writeln(AliasUser.ViaCanonical);
  Writeln(InitCount);
end.
