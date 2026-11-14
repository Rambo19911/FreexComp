// covers: LANG-505
program UnitNameMismatch;
// Helpers.pas declares "unit Helper": E2201 at its heading
uses Helpers;
begin
end.
