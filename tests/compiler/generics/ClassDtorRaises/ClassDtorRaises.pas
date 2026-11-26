// covers: LANG-791, CMP-1396, ABI-104
program ClassDtorRaises;

{ an exception escaping a class destructor behaves as one raised by the last statement of the unit's finalization
  section: reported like an unhandled exception, exit code 217, the units initialised before are still finalised }

uses
  UCDFirst, UCDRaise;

begin
  Writeln('main');
end.
