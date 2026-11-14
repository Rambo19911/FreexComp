// covers: LANG-350, LANG-361
program ErrAssignToConst;
const
  Limit = 10;
var
  I: Integer;

procedure P(const N: Integer);
begin
  N := 5;
end;

begin
  for I := 1 to Limit do
    I := I + 1;
  Limit := 3;
end.
