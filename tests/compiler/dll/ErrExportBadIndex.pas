// covers: LANG-549
library ErrExportBadIndex;
// index N is a constant integer expression in 1..65535

var
  V: Integer;

const
  Top = 65535;

procedure P;
begin
end;

exports
  P name 'a' index 0,
  P name 'b' index 65536,
  P name 'c' index V,
  P name 'd' index 'x',
  P name 'e' index -1,
  P name 'f' index Top + 1,
  P name 'g' index Top,
  P name 'h' index 1;

begin
end.
