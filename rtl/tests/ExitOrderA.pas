{ Shared unit of system/Sys_ExitOrder: the first unit in initialisation order. Its initialization installs an exit
  procedure the customary way (RTL-050: save the previous value, restore it as the first action). }
unit ExitOrderA;

interface

implementation

var
  SavedExit: Pointer;

procedure ExitA;
begin
  ExitProc := SavedExit;
  Writeln('exit A, ExitCode ', ExitCode);
end;

initialization
  Writeln('init A');
  SavedExit := ExitProc;
  ExitProc := @ExitA;
finalization
  Writeln('final A');
end.
