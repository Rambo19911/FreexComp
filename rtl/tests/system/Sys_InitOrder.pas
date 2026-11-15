// covers: RTL-006, ABI-100
program Sys_InitOrder;
{ Unit initialisation in dependency order, depth-first in "uses" order (system.md RTL-006, units-programs.md):
  the program uses InitOrderB, InitOrderA; InitOrderB uses InitOrderC, InitOrderD; InitOrderD and InitOrderA use
  InitOrderC. So C, D, B, A are initialised, each once and after everything it uses, and finalised in reverse order
  after the main block. }
uses InitOrderB, InitOrderA;

begin
  Writeln('main');
end.
