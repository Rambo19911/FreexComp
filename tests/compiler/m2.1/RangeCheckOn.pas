// covers: LANG-351, LANG-280
program RangeCheckOn;
var
  I: Integer;
  B: Byte;

procedure TakeByte(V: Byte);
begin
  Writeln('got ', V);
end;

begin
{$R-}
  I := 300;
  B := I;
  Writeln('unchecked ', B);
{$R+}
  I := 255;
  B := I;
  Writeln('in range ', B);
  I := 0;
  B := I;
  Writeln('in range ', B);
  I := 200;
  TakeByte(I);
  I := 256;
  B := I;
  Writeln('never');
end.
