// covers: LANG-801, LANG-830, LANG-831, ABI-101, ABI-125
program ExceptionSysUtils;

{ SysUtils' Exception with its message; runtime errors become SysUtils exceptions through ErrorProc: EDivByZero from
  'div' in a callee, ERangeError from a range check in the routine of the try statement }

{$R+}

uses
  SysUtils;

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
  try
    raise Exception.Create('msg');
  except
    on E: Exception do Writeln(E.ClassName, ': ', E.Message);
  end;
  try
    Writeln(Divide(1, 0));
  except
    on E: EDivByZero do Writeln(E.ClassName, ': ', E.Message);
  end;
  I := 4;
  try
    A[I] := 1;
  except
    on E: ERangeError do Writeln(E.ClassName, ': ', E.Message, ', is EIntError: ', E is EIntError);
  end;
  try
    raise EConvertError.Create('bad number');
  except
    on E: EIntError do Writeln('not reached');
    on E: Exception do Writeln(E.ClassName, ': ', E.Message);
  end;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.