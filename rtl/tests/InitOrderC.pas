{ Shared unit of system/Sys_InitOrder: uses only System. }
unit InitOrderC;

interface

implementation

initialization
  Writeln('init InitOrderC');

finalization
  Writeln('final InitOrderC');
end.
