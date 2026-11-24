// covers: RTL-966
library quietlib;
// does nothing with floating point: whatever the control state is after the load, the start-up of the library's
// run-time library left it so

var
  AtLoad: Cardinal;

function StateAtLoad: Cardinal; stdcall;
begin
  Result := AtLoad;
end;

exports
  StateAtLoad;

begin
  AtLoad := _GetMXCSR;
end.
