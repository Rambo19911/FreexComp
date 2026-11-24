// covers: LANG-549
library ErrExportDuplicateIndex;
// two items with the same explicit index: E2216 at the second, also across clauses

const
  Seven = 7;

procedure P;
begin
end;

procedure Q;
begin
end;

exports
  P index 7,
  Q index Seven,
  P name 'p2' index 3;

exports
  Q name 'q2' index 2 + 1,
  Q name 'q3';

begin
end.
