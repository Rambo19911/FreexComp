// covers: LANG-832, CMP-355, ABI-124
program ExceptionUnhandled;

{ exceptions.md section 7, Ex2: an exception that no handler takes ends the program with exit code 217 through the
  top-level handler of _start (stderr is not compared) }

begin
  Writeln('before');
  raise TObject.Create;
end.
