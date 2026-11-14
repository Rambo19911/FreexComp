{ Shared unit of system/Sys_ExitOrder: the third unit. With variant 5 its finalization halts; the shutdown goes on
  with the remaining units and this finalization does not run again. }
unit ExitOrderC;

interface

implementation

initialization
  Writeln('init C');
finalization
  Writeln('final C');
  if ParamStr(1) = '5' then
  begin
    Writeln('halt 8 in final C');
    Halt(8);
  end;
  Writeln('final C done');
end.
