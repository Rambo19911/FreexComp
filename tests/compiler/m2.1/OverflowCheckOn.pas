// covers: LANG-223
program OverflowCheckOn;
var
  I: Integer;
  X: Int64;
begin
{$Q-}
  I := MaxInt;
  I := I + 1;
  Writeln('wrapped ', I);
{$Q+}
  I := MaxInt - 1;
  I := I + 1;
  Writeln('no overflow ', I);
  X := High(Int64);
  X := X - 1;
  Writeln('no overflow ', X);
  I := I * 2;
  Writeln('never');
end.
