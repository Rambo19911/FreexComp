// covers: LANG-520, LANG-521, CMP-350, CMP-355, ABI-100
program UnitInitFinalOrder;
// the LANG-520 example: P uses A, B; A interface-uses C; B implementation-uses A
uses A, B;
begin
  Writeln('main');
end.