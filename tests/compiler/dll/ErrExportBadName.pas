// covers: LANG-547
library ErrExportBadName;
// an export name is [A-Za-z_][A-Za-z0-9_$]{0,254} and none of the reserved link names:
// empty, with '@', with a dot, a reserved name - also when it is the routine's own identifier

procedure P;
begin
end;

procedure memcpy;
begin
end;

const
  Suffix = '@8';

exports
  P name '',
  P name 'P' + Suffix,
  P name 'lib.P',
  P name '_start',
  memcpy,
  P name 'P$ok_1';

begin
end.
