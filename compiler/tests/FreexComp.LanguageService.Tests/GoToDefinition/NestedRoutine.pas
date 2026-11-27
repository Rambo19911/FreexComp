program NestedRoutine;
procedure Outer;
  procedure {#}Inner;
  begin
    Writeln('inner');
  end;
begin
  {|}Inner;
end;
begin
  Outer;
end.
