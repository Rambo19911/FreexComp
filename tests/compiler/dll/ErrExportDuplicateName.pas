// covers: LANG-548
library ErrExportDuplicateName;
// export names are compared case-sensitively; the second item with a name is E2214,
// also across clauses and when the same routine is exported twice without another name

procedure First;
begin
end;

procedure Second;
begin
end;

exports
  First,
  Second name 'First',
  Second name 'first',
  Second;

exports
  First name 'Second',
  FIRST;

begin
end.
