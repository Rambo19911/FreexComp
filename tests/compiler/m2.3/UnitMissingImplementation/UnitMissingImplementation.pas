// covers: LANG-506
program UnitMissingImplementation;
// Tools declares Missing in its interface but never implements it: E2100 in Tools.pas
uses Tools;
begin
  Present;
end.
