// covers: RTL-215
program SU_Helpers;
{ sysutils.md section 2.4 and 10: Abort, RaiseLastOSError (with and without a code, code 0), SysErrorMessage (OS text:
  only its shape is asserted), Win32Check, OutOfMemoryError, ExceptionErrorMessage, ShowException (captured through
  ShowErrorProc with IsConsole off), GetLastError, the Win32*Version variables, CheckWin32Version, and AddExitProc
  (LIFO, run at program end: the last lines of the expected output), FreeAndNil (the variable is nil before the
  destructor runs; nil is a no-op). }
uses
  Windows, SysUtils, TestKit;

var
  Shown: string;

procedure Capture(const Text: string);
begin
  Shown := Text;
end;

procedure ExitA;
begin
  Writeln('exit proc A');
end;

procedure ExitB;
begin
  Writeln('exit proc B');
end;

function Trimmed(const S: string): Boolean;
begin
  Result := (S <> '') and (S[Length(S)] <> ' ') and (S[Length(S)] <> #13) and (S[Length(S)] <> #10);
end;

procedure TestAbort;
var
  Msg, Cls: string;
begin
  Section('Abort');
  Msg := '';
  try
    Abort;
  except
    on E: Exception do
    begin
      Msg := E.Message;
      Cls := E.ClassName;
    end;
  end;
  CheckEqS('EAbort', Cls, 'class');
  CheckEqS('Operation aborted', Msg, 'message');
end;

procedure TestOSErrors;
var
  Msg: string;
  Code: Cardinal;
begin
  Section('OS errors');
  Check(Trimmed(SysErrorMessage(2)), 'SysErrorMessage(2): text without trailing white space');
  CheckEqS('', SysErrorMessage($2FFFFFFF), 'unknown code gives empty');
  Code := 99;
  try
    RaiseLastOSError(5);
  except
    on E: EOSError do
    begin
      Msg := E.Message;
      Code := E.ErrorCode;
    end;
  end;
  CheckEqI(5, Code, 'RaiseLastOSError(5) ErrorCode');
  CheckEqS('System Error.  Code: 5.' + sLineBreak + SysErrorMessage(5), Msg, 'RaiseLastOSError(5) message');
  try
    RaiseLastOSError(0);
  except
    on E: EOSError do
    begin
      Msg := E.Message;
      Code := E.ErrorCode;
    end;
  end;
  CheckEqI(0, Code, 'RaiseLastOSError(0) ErrorCode');
  CheckEqS('A call to an OS function failed', Msg, 'RaiseLastOSError(0) message');
  SetLastError(3);
  CheckEqI(3, GetLastError, 'GetLastError');
  try
    SetLastError(3);
    RaiseLastOSError;
  except
    on E: EOSError do
      Code := E.ErrorCode;
  end;
  CheckEqI(3, Code, 'RaiseLastOSError uses GetLastError');
  CheckEqB(True, Win32Check(True), 'Win32Check(True)');
  Code := 0;
  try
    SetLastError(6);
    Win32Check(False);
  except
    on E: EOSError do
      Code := E.ErrorCode;
  end;
  CheckEqI(6, Code, 'Win32Check(False) raises with GetLastError');
end;

procedure TestOutOfMemory;
var
  Cls, Msg: string;
  First, Second: TObject;
begin
  Section('OutOfMemoryError');
  First := nil;
  Second := nil;
  try
    OutOfMemoryError;
  except
    on E: Exception do
    begin
      Cls := E.ClassName;
      Msg := E.Message;
      First := E;
    end;
  end;
  try
    OutOfMemoryError;
  except
    on E: Exception do
      Second := E;
  end;
  CheckEqS('EOutOfMemory', Cls, 'class');
  CheckEqS('Out of memory', Msg, 'message');
  Check((First <> nil) and (First = Second), 'the pre-allocated instance');
end;

procedure TestErrorMessage;
var
  E: Exception;
  O: TObject;
  Buffer: array[0..255] of Char;
  Len: Integer;
  Expected, Module: string;
begin
  Section('ExceptionErrorMessage, ShowException');
  Module := ExtractFileName(ParamStr(0));
  E := EConvertError.Create('bad value');
  Expected := 'Exception EConvertError in module ' + Module + ' at 0000000000001234.' + sLineBreak + 'bad value';
  Len := ExceptionErrorMessage(E, Pointer(NativeInt($1234)), @Buffer[0], Length(Buffer));
  CheckEqI(Length(Expected), Len, 'length');
  CheckEqS(Expected, StrPas(@Buffer[0]), 'text for an Exception');
  Len := ExceptionErrorMessage(E, Pointer(NativeInt($1234)), @Buffer[0], 10);
  CheckEqI(9, Len, 'truncated to the buffer');
  CheckEqS(Copy(Expected, 1, 9), StrPas(@Buffer[0]), 'truncated text');
  O := TObject.Create;
  Len := ExceptionErrorMessage(O, nil, @Buffer[0], Length(Buffer));
  CheckEqS('Exception TObject in module ' + Module + ' at 0000000000000000.', StrPas(@Buffer[0]),
    'no message line for a non-Exception');
  CheckEqI(Length(StrPas(@Buffer[0])), Len, 'length for a non-Exception');
  IsConsole := False;
  ShowErrorProc := Capture;
  ShowException(E, Pointer(NativeInt($1234)));
  IsConsole := True;
  ShowErrorProc := nil;
  CheckEqS(Expected, Shown, 'ShowException text (GUI path)');
  O.Free;
  E.Free;
end;

type
  { its destructor looks at the variable FreeAndNil is clearing }
  TWatched = class
    destructor Destroy; override;
  end;

var
  Watched: TWatched;
  SeenInDestroy: string;

destructor TWatched.Destroy;
begin
  if Watched = nil then
    SeenInDestroy := 'nil'
  else
    SeenInDestroy := 'set';
  inherited Destroy;
end;

procedure TestFreeAndNil;
var
  O: TObject;
  Before: NativeInt;
begin
  Section('FreeAndNil');
  Before := _LiveAllocCount;
  Watched := TWatched.Create;
  FreeAndNil(Watched);
  Check(Watched = nil, 'variable is nil');
  CheckEqS('nil', SeenInDestroy, 'nil before the destructor runs');
  O := nil;
  FreeAndNil(O);
  Check(O = nil, 'nil stays nil');
  O := TObject.Create;
  FreeAndNil(O);
  Check(O = nil, 'TObject variable');
  CheckEqI(0, _LiveAllocCount - Before, 'freed');
end;

procedure TestVersion;
begin
  Section('version');
  Check(Win32MajorVersion >= 6, 'Win32MajorVersion');
  Check(Win32MinorVersion >= 0, 'Win32MinorVersion');
  Check(Win32BuildNumber > 0, 'Win32BuildNumber');
  CheckEqB(True, CheckWin32Version(6, 1), 'CheckWin32Version(6, 1)');
  CheckEqB(True, CheckWin32Version(Win32MajorVersion, Win32MinorVersion), 'CheckWin32Version(own)');
  CheckEqB(False, CheckWin32Version(Win32MajorVersion + 1), 'CheckWin32Version(next major)');
  CheckEqB(False, CheckWin32Version(Win32MajorVersion, Win32MinorVersion + 1), 'CheckWin32Version(next minor)');
end;

begin
  AddExitProc(ExitA);
  AddExitProc(ExitB);
  TestAbort;
  TestOSErrors;
  TestOutOfMemory;
  TestErrorMessage;
  TestVersion;
  TestFreeAndNil;
  Done;
end.
