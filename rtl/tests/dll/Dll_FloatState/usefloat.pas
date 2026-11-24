// covers: RTL-953, RTL-966
program usefloat;
// the host rounds upwards (a control state that is not the default); the status flags (low six bits) are no part of
// the control state

uses Windows, SysUtils;

const
  Mine = $5F80;                        { all exceptions masked, rounding towards plus infinity }
  ControlBits = $FFC0;

type
  TValue = function: Cardinal; stdcall;

function Control: string;
begin
  Result := IntToHex(Integer(_GetMXCSR and ControlBits), 4);
end;

function CurrentErrorMode: Cardinal;
begin
  Result := SetErrorMode(0);
  SetErrorMode(Result);
end;

var
  Dir: string;
  H: HMODULE;
  Value: TValue;
  Mode, Before, Error: Cardinal;
begin
  Dir := ExtractFilePath(ParamStr(0));
  SetErrorMode(SEM_FAILCRITICALERRORS);
  Mode := CurrentErrorMode;
  _SetMXCSR(Mine);
  Writeln('control state of the host: ', Control);

  H := LoadLibraryW(PChar(Dir + 'quietlib.dll'));
  Writeln('LoadLibraryW loaded: ', H <> 0);
  Writeln('control state after LoadLibraryW: ', Control);
  Value := TValue(GetProcAddress(H, 'StateAtLoad'));
  Writeln('control state the library block saw: ', IntToHex(Integer(Value() and ControlBits), 4));
  Writeln('freed: ', FreeLibrary(H));
  Writeln('control state after FreeLibrary: ', Control);

  Before := _GetMXCSR;
  H := SafeLoadLibrary(Dir + 'quietlib.dll');
  Writeln('SafeLoadLibrary loaded: ', H <> 0);
  Writeln('state after SafeLoadLibrary is the state before: ', _GetMXCSR = Before);
  Writeln('error mode restored: ', CurrentErrorMode = Mode);
  Writeln('freed: ', FreeLibrary(H));

  // a library that changes the state: plain loading lets the change through, SafeLoadLibrary undoes it
  H := LoadLibraryW(PChar(Dir + 'rudelib.dll'));
  Writeln('control state after LoadLibraryW of the changing library: ', Control);
  Writeln('freed: ', FreeLibrary(H));
  _SetMXCSR(Mine);
  Before := _GetMXCSR;
  H := SafeLoadLibrary(Dir + 'rudelib.dll');
  Writeln('SafeLoadLibrary loaded the changing library: ', H <> 0);
  Writeln('state after SafeLoadLibrary is the state before: ', _GetMXCSR = Before);
  Value := TValue(GetProcAddress(H, 'ErrorModeAtLoad'));
  Writeln('error mode during the load is the default argument: ', Value() = SEM_NOOPENFILEERRORBOX);
  Writeln('error mode restored: ', CurrentErrorMode = Mode);
  Writeln('freed: ', FreeLibrary(H));
  H := SafeLoadLibrary(Dir + 'rudelib.dll', SEM_FAILCRITICALERRORS or SEM_NOGPFAULTERRORBOX);
  Value := TValue(GetProcAddress(H, 'ErrorModeAtLoad'));
  Writeln('error mode during the load is the argument: ', Value() = SEM_FAILCRITICALERRORS or SEM_NOGPFAULTERRORBOX);
  Writeln('error mode restored: ', CurrentErrorMode = Mode);
  Writeln('freed: ', FreeLibrary(H));

  // a missing file: 0 and the error of LoadLibraryW, no message box, everything as before
  Before := _GetMXCSR;
  H := SafeLoadLibrary(Dir + 'no-such-library.dll');
  Error := GetLastError;
  Writeln('SafeLoadLibrary of a missing file returned 0: ', H = 0);
  Writeln('last error: ', Error);
  Writeln('state after SafeLoadLibrary is the state before: ', _GetMXCSR = Before);
  Writeln('error mode restored: ', CurrentErrorMode = Mode);
  H := SafeLoadLibrary('');
  Writeln('SafeLoadLibrary of an empty name returned 0: ', H = 0);
  Writeln('control state at the end: ', Control);
end.
