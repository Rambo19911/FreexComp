// covers: CMP-135, CMP-504
program ErrMultiple;
var
  I: Integer;
  B: Boolean;

procedure P;
begin
  Unknown1 := 1;
end;

begin
  B := 5;
  I := Unknown2;
end.
