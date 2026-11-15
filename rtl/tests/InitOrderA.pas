{ Shared unit of system/Sys_InitOrder: uses InitOrderC (already initialised through InitOrderB). }
unit InitOrderA;

interface

uses InitOrderC;

implementation

initialization
  Writeln('init InitOrderA');

finalization
  Writeln('final InitOrderA');
end.
