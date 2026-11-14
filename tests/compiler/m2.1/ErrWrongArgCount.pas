// covers: LANG-420, LANG-900
program ErrWrongArgCount;

procedure Two(A, B: Integer);
begin
end;

function One(A: Integer): Integer;
begin
  Result := A;
end;

begin
  Two(1);
  Two(1, 2, 3);
  Writeln(One);
  Writeln(One(1, 2));
  Two(One(1), One());
end.
