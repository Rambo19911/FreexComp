// covers: LANG-942, CMP-411
program ReadBadNumber;
// A sign without digits is a malformed integer: runtime error 106 (the output before it is kept).
var
  I: Integer;
begin
  Read(I);
  Writeln(I);
  Read(I);
  Writeln('not reached ', I);
end.
