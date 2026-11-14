// covers: LANG-510, LANG-535
program UnitNotFound;
// no NoSuchUnit.pas anywhere on the search path: E2202 at the entry, with the name as written
uses NoSuchUnit;
begin
end.
