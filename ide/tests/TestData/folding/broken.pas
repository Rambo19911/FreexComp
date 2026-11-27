unit Broken;

interface

implementation

procedure A;
begin
  if True then
  begin
    Writeln;
  end;
  Writeln;
{ the end of A is missing }

procedure B;
begin
  Writeln;
end;

procedure C;
begin
  Writeln;
end;

end.
