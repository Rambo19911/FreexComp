// covers: LANG-535, LANG-538, LANG-539
// opc: --unit-scope None --unit-scope Lib
program UnitScopeNames;
// Tools: no Tools.pas, no None.Tools.pas, so the scope names give Lib.Tools (LANG-535 step 4, in order); Helpers.pas
// exists, so it wins over Lib.Helpers.pas (step 2 before step 4); the scope name None needs no file (LANG-539)
uses Tools, Helpers;
begin
  Writeln(Tools.From);
  Writeln(Lib.Tools.From);
  Writeln(From);
  Writeln(Helpers.HelpersFrom);
end.
