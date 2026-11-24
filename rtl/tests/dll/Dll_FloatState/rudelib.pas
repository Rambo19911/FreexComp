// covers: RTL-953, RTL-966
library rudelib;
// a library that changes the control state of the thread that loads it, and notes the error mode of the process

uses Windows;

var
  ModeAtLoad: Cardinal;

function ErrorModeAtLoad: Cardinal; stdcall;
begin
  Result := ModeAtLoad;
end;

exports
  ErrorModeAtLoad;

begin
  ModeAtLoad := SetErrorMode(0);
  SetErrorMode(ModeAtLoad);
  _SetMXCSR($1F80);
end.
