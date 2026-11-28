program P;

{ CMP-1511: a local variable; the global of the same name and the other routine's local keep their names. }

var
  Count: Integer;

procedure Run;
var
  Count, Step: Integer;
begin
  Step := 2;
  Count := 3;
  Count := Count * Step;
  Writeln('run ', Count);
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
