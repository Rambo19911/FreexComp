{ Shared unit of system/Sys_ExitOrder: the second unit. With variant 4 its initialization halts, so it is not
  finalised (its Init did not complete, ABI-104 step 2) and ExitOrderC is never initialised. }
unit ExitOrderB;

interface

implementation

initialization
  Writeln('init B');
  if ParamStr(1) = '4' then
  begin
    Writeln('halt 4 in init B');
    Halt(4);
  end;
  Writeln('init B done');
finalization
  Writeln('final B');
end.
