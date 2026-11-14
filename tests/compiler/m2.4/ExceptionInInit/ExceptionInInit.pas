// covers: LANG-832, LANG-833, ABI-104, CMP-355
program ExceptionInInit;

{ an unhandled exception in a unit initialization ends the program with exit code 217; the main block never runs and
  the units whose initialization completed are finalised (ABI-104 step 2) }

uses
  InitOk, InitFails;

begin
  Writeln('main');
end.
