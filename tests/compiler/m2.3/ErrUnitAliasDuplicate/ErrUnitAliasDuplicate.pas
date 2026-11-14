// covers: LANG-512, LANG-537
program ErrUnitAliasDuplicate;
// System.Types is an alias of Types (RTL unit-aliases.json): the second entry names the same unit again
uses Types, System.Types;
begin
end.
