{ Shared unit of system/Exc_Escapes: initialised before ExcEscapeUnit, so it is finalised after it. Uses only System. }
unit ExcEscapeFirst;

interface

implementation

initialization
  Writeln('init ExcEscapeFirst');

finalization
  Writeln('final ExcEscapeFirst');
end.
