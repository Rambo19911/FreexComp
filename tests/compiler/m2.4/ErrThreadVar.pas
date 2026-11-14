// covers: LANG-345
program ErrThreadVar;

threadvar
  Name: string;
  Count: Integer = 3;
  Ok: Integer;

begin
  Ok := 1;
  Writeln(Ok);
end.
