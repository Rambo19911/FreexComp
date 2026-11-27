program CaseKeywordsUpper;

procedure Show(const Value: string); overload;
begin
  if Value <> '' then
    Writeln(Value) // begin end
  else
    Writeln('empty');
end;

var
  I: Integer;
begin
  for I := 1 to 2 do
    Show('begin');
  repeat
    I := I div 2;
  until I = 0;
end.
