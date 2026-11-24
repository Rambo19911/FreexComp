// covers: LANG-545
library ErrExportUndeclared;
// an export item that names nothing is E2003 at the item, with its position:
// an unknown name, a routine declared after the clause, and a unit that is not used

function Twice(A: Integer): Integer;
begin
  Result := A * 2;
end;

exports
  Twice,
  Missing,
  Later name 'later',
  NoSuchUnit.Twice;

procedure Later;
begin
end;

begin
end.
