{ Shared unit of system/Sys_InitOrder: uses InitOrderC. }
unit InitOrderD;

interface

uses InitOrderC;

implementation

initialization
  Writeln('init InitOrderD');

finalization
  Writeln('final InitOrderD');
end.
