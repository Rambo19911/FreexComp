program InlineScope;

procedure Run(N: Integer);
var
  Total: Integer;
begin
  Total := N;
  if N > 0 then
  begin
    var Tmp: Integer := N * 2;
    Total := Total + Tmp;
  end;
  if N > 1 then
  begin
    var Tmp: string := 'two';
    WriteLn(Tmp);
  end;
  try
    Total := Total + 1;
  finally
    var Fin: Integer := Total * 10;
    WriteLn(Fin);
  end;
  WriteLn(Total);
end;

begin
  Run(3);
end.
