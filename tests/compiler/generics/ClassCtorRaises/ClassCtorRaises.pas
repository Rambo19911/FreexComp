// covers: LANG-791, CMP-1396, ABI-104
program ClassCtorRaises;

{ an exception escaping a class constructor is an exception escaping the unit's initialization: the program ends with
  exit code 217, the main block never runs and only the units whose initialization completed are finalised }

uses
  UCRFirst, UCRaise;

begin
  Writeln('main');
end.
