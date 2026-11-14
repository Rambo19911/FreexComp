// covers: LANG-402, CMP-508
program ErrDuplicateIdent;
var
  X: Integer;
  X: Char;
const
  X = 5;

procedure P(A: Integer);
var
  A: Integer;
begin
end;

function F: Integer;
var
  Result: Integer;
begin
  F := 1;
end;

begin
end.
