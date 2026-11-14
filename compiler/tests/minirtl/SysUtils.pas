{ Mini SysUtils for the M2.4 golden tests (docs/spec/compiler/toolchain.md CMP-630, 631). Test-only, never shipped; replaced
  by the RTL's SysUtils. The root exception class keeps its message as the first field (ABI-125); the classes are a subset of
  the hierarchy of docs/spec/rtl/sysutils.md section 2.2; the initialization installs the hooks that turn runtime errors
  (ABI-101, RTL-212) and OS exceptions (ABI-121 step 2, RTL-213) into exception objects and reports unhandled exceptions
  with their message (ABI-124, RTL-215). }
unit SysUtils;

interface

type
  Exception = class(TObject)
  private
    FMessage: string;
  public
    constructor Create(const Msg: string);
    property Message: string read FMessage write FMessage;
  end;

  ExceptClass = class of Exception;

  EAbort = class(Exception)
  end;

  EHeapException = class(Exception)
  end;

  EOutOfMemory = class(EHeapException)
  end;

  EInvalidPointer = class(EHeapException)
  end;

  EInOutError = class(Exception)
  public
    ErrorCode: Integer;
  end;

  EExternal = class(Exception)
  public
    ExceptionCode: Cardinal;
    ExceptionAddress: Pointer;
  end;

  EIntError = class(EExternal)
  end;

  EDivByZero = class(EIntError)
  end;

  ERangeError = class(EIntError)
  end;

  EIntOverflow = class(EIntError)
  end;

  EMathError = class(EExternal)
  end;

  EInvalidOp = class(EMathError)
  end;

  EZeroDivide = class(EMathError)
  end;

  EOverflow = class(EMathError)
  end;

  EUnderflow = class(EMathError)
  end;

  EAccessViolation = class(EExternal)
  end;

  EPrivilege = class(EExternal)
  end;

  EStackOverflow = class(EExternal)
  end;

  EInvalidCast = class(Exception)
  end;

  EConvertError = class(Exception)
  end;

  EAssertionFailed = class(Exception)
  end;

  EAbstractError = class(Exception)
  end;

  EIntfCastError = class(Exception)
  end;

implementation

{$POINTERMATH ON}

type
  PByte = ^Byte;
  PPointer = ^Pointer;
  PCardinal = ^Cardinal;
  PNativeUInt = ^NativeUInt;

constructor Exception.Create(const Msg: string);
begin
  FMessage := Msg;
end;

function InOutError(Code: Integer; const Msg: string): Exception;
var
  E: EInOutError;
begin
  E := EInOutError.Create(Msg);
  E.ErrorCode := Code;
  Result := E;
end;

{ RTL-212: the class and message of a runtime error number }
function ErrorObject(Code: Integer): Exception;
var
  Text: string;
begin
  case Code of
    200: Result := EDivByZero.Create('Division by zero');
    201: Result := ERangeError.Create('Range check error');
    202: Result := EStackOverflow.Create('Stack overflow');
    203: Result := EOutOfMemory.Create('Out of memory');
    204: Result := EInvalidPointer.Create('Invalid pointer operation');
    205: Result := EOverflow.Create('Floating point overflow');
    206: Result := EUnderflow.Create('Floating point underflow');
    207: Result := EInvalidOp.Create('Invalid floating point operation');
    208: Result := EZeroDivide.Create('Floating point division by zero');
    211: Result := EAbstractError.Create('Abstract method called');
    215: Result := EIntOverflow.Create('Integer overflow');
    216: Result := EAccessViolation.Create('Access violation');
    218: Result := EPrivilege.Create('Privileged instruction');
    219: Result := EInvalidCast.Create('Invalid class typecast');
    227: Result := EAssertionFailed.Create('Assertion failed');
    228: Result := EIntfCastError.Create('Interface not supported');
    2: Result := InOutError(Code, 'File not found');
    3: Result := InOutError(Code, 'Path not found');
    4: Result := InOutError(Code, 'Too many open files');
    5: Result := InOutError(Code, 'File access denied');
    6: Result := InOutError(Code, 'Invalid file handle');
    100: Result := InOutError(Code, 'Read beyond end of file');
    101: Result := InOutError(Code, 'Disk write error');
    102: Result := InOutError(Code, 'File not assigned');
    103: Result := InOutError(Code, 'File not open');
    104: Result := InOutError(Code, 'File not open for input');
    105: Result := InOutError(Code, 'File not open for output');
    106: Result := InOutError(Code, 'Invalid numeric input');
  else
    Str(Code, Text);
    if Code < 200 then
      Result := InOutError(Code, 'I/O error ' + Text)
    else
      Result := Exception.Create('Runtime error ' + Text);
  end;
end;

{ ABI-101: ErrorProc raises the error's exception at the error address }
procedure RaiseRuntimeError(Code: Integer; Addr: Pointer);
begin
  raise ErrorObject(Code) at Addr;
end;

function Hex16(Value: NativeUInt): string;
var
  I, D: Integer;
begin
  Result := '$';
  for I := 15 downto 0 do
  begin
    D := Integer((Value shr (I * 4)) and 15);
    if D < 10 then
      Result := Result + Chr(48 + D)
    else
      Result := Result + Chr(55 + D);
  end;
end;

{ ABI-121 step 2 / RTL-213: the object for an OS exception (EXCEPTION_RECORD: code at 0, address at 16, information at 32) }
function OsExceptionObject(ErrorCode: Integer; ExceptionRecord: Pointer): Pointer;
var
  Rec: PByte;
  E: Exception;
  Access: string;
begin
  Rec := ExceptionRecord;
  if ErrorCode = 216 then
  begin
    case PNativeUInt(Rec + 32)^ of
      1: Access := 'write';
      8: Access := 'execute';
    else
      Access := 'read';
    end;
    E := EAccessViolation.Create('Access violation at address ' + Hex16(NativeUInt(PPointer(Rec + 16)^)) + ': ' + Access
      + ' of address ' + Hex16(PNativeUInt(Rec + 40)^));
  end
  else
    E := ErrorObject(ErrorCode);
  if E is EExternal then
  begin
    EExternal(E).ExceptionCode := PCardinal(Rec)^;
    EExternal(E).ExceptionAddress := PPointer(Rec + 16)^;
  end;
  Result := Pointer(E);
end;

{ ABI-124 / RTL-215: the class name and, for an Exception, its message; _UnhandledException then ends with exit code 217 }
procedure ShowUnhandled(Obj: TObject; Addr: Pointer);
begin
  if Obj is Exception then
    Writeln(ErrOutput, 'Unhandled exception ', Obj.ClassName, ': ', Exception(Obj).Message)
  else
    Writeln(ErrOutput, 'Unhandled exception ', Obj.ClassName);
end;

initialization
  ErrorProc := RaiseRuntimeError;
  ExceptObjProc := OsExceptionObject;
  ExceptProc := ShowUnhandled;
end.
