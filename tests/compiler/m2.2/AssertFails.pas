// covers: LANG-900, ABI-103
program AssertFails;
var
  I: Integer;

function Next: Integer;
begin
  Inc(I);
  Result := I;
end;

begin
  I := 0;
  Assert(I = 0);
  Writeln('passed');
  {$C-}
  Assert(Next = 100, 'not evaluated');
  Assert(False);
  {$C+}
  Writeln('disabled ', I);
  Assert(Next = 1, 'one');
  Writeln('still ', I);
  Assert(I > 5, 'too small');
  Writeln('not reached');
end.
