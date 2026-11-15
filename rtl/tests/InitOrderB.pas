{ Shared unit of system/Sys_InitOrder: uses InitOrderC, then InitOrderD. }
unit InitOrderB;

interface

uses InitOrderC, InitOrderD;

implementation

initialization
  Writeln('init InitOrderB');

finalization
  Writeln('final InitOrderB');
end.
