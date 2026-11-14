// covers: RTL-212, ABI-101
program SU_RuntimeErrorMapping;
{ sysutils.md RTL-212: every runtime error number raised through System.ErrorProc becomes the listed exception with
  the exact message; EInOutError carries the number; the address is the one passed to ErrorProc. }
{$R+}
{$Q+}
uses
  SysUtils, TestKit;

procedure Expect(Code: Integer; const ClassName, Msg: string);
var
  Got: string;
begin
  Got := '(no exception)';
  try
    RunError(Code);
  except
    on E: EInOutError do
      Got := E.ClassName + ': ' + E.Message + ' [' + IntToStr(E.ErrorCode) + ']';
    on E: Exception do
      Got := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS(ClassName + ': ' + Msg, Got, 'RunError(' + IntToStr(Code) + ')');
end;

function Divide(A, B: Integer): Integer;
begin
  Result := A div B;
end;

var
  A: array[1..3] of Integer;
  I: Integer;
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Section('RTL-212 table');
  Expect(200, 'EDivByZero', 'Division by zero');
  Expect(201, 'ERangeError', 'Range check error');
  Expect(202, 'EStackOverflow', 'Stack overflow');
  Expect(203, 'EOutOfMemory', 'Out of memory');
  Expect(204, 'EInvalidPointer', 'Invalid pointer operation');
  Expect(205, 'EOverflow', 'Floating point overflow');
  Expect(206, 'EUnderflow', 'Floating point underflow');
  Expect(207, 'EInvalidOp', 'Invalid floating point operation');
  Expect(208, 'EZeroDivide', 'Floating point division by zero');
  Expect(211, 'EAbstractError', 'Abstract method called');
  Expect(215, 'EIntOverflow', 'Integer overflow');
  Expect(216, 'EAccessViolation', 'Access violation');
  Expect(218, 'EPrivilege', 'Privileged instruction');
  Expect(219, 'EInvalidCast', 'Invalid class typecast');
  Expect(228, 'EIntfCastError', 'Interface not supported');
  Expect(2, 'EInOutError', 'File not found [2]');
  Expect(3, 'EInOutError', 'Path not found [3]');
  Expect(4, 'EInOutError', 'Too many open files [4]');
  Expect(5, 'EInOutError', 'File access denied [5]');
  Expect(6, 'EInOutError', 'Invalid file handle [6]');
  Expect(100, 'EInOutError', 'Read beyond end of file [100]');
  Expect(101, 'EInOutError', 'Disk write error [101]');
  Expect(102, 'EInOutError', 'File not assigned [102]');
  Expect(103, 'EInOutError', 'File not open [103]');
  Expect(104, 'EInOutError', 'File not open for input [104]');
  Expect(105, 'EInOutError', 'File not open for output [105]');
  Expect(106, 'EInOutError', 'Invalid numeric input [106]');
  Expect(150, 'EInOutError', 'I/O error 150 [150]');
  Expect(1, 'EInOutError', 'I/O error 1 [1]');
  Expect(230, 'Exception', 'Runtime error 230');

  Section('errors raised by compiled code');
  try
    Writeln(Divide(1, 0));
  except
    on E: EDivByZero do
      CheckEqS('Division by zero', E.Message, 'div by zero in a callee');
  end;
  I := 4;
  try
    A[I] := 1;
  except
    on E: ERangeError do
      CheckEqS('Range check error', E.Message, 'range check');
  end;
  I := MaxInt;
  try
    I := I + 1;
  except
    on E: EIntOverflow do
      CheckEqS('Integer overflow', E.Message, 'overflow check');
  end;
  try
    Reset(Input);
    Rewrite(Input);
    Flush(Input);
  except
    on E: EInOutError do
      CheckEqI(105, E.ErrorCode, 'I/O check raises EInOutError');
  end;
  CheckEqI(0, _LiveAllocCount - Before, 'no leaks');
  Done;
end.
