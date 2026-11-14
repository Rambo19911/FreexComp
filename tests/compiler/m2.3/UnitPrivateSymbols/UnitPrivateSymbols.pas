// covers: LANG-311, LANG-515
program UnitPrivateSymbols;
uses Vault;
begin
  Writeln(Open);
  Writeln(Secret);
  Hidden := 1;
  Vault.Helper;
end.
