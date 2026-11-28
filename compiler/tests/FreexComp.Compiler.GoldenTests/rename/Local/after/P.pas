program P;

{ CMP-1511: a local variable; the global of the same name and the other routine's local keep their names. }

var
  Count: Integer;

procedure Run;
var
  Total, Step: Integer;
begin
  Step := 2;
  Total := 3;
  Total := Total * Step;
  Writeln('run ', Total);
end;

procedure Other;
var
  Count: Integer;
begin
  Count := 5;
  Writeln('other ', Count);
end;

begin
  Count := 1;
  Run;
  Other;
  Writeln('global ', Count);
end.
