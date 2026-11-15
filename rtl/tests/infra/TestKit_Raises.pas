// covers: RTL-911, RTL-910
program TestKit_Raises;
{ The exception-test pattern of testing.md RTL-911: Expect stores the expected class name and message in TestKit's
  unit variables; inside the handler CheckRaises compares them with the caught exception's. The failing cases show
  the FAIL line (class and quoted message, expected then got); Done therefore halts with exit code 1. }
uses TestKit, SysUtils;

procedure ParseInt(const S: string);
begin
  StrToInt(S);
end;

begin
  Section('passing');
  Expect('EConvertError', '''x'' is not a valid integer value');
  try
    ParseInt('x');
    Check(False, 'StrToInt raises');
  except
    on E: Exception do
      CheckRaises('StrToInt', E.ClassName, E.Message);
  end;
  Expect('EDivByZero', 'Division by zero');
  try
    raise EDivByZero.Create('Division by zero');
  except
    on E: Exception do
      CheckRaises('raise EDivByZero', E.ClassName, E.Message);
  end;

  Section('failing');
  Expect('EConvertError', 'other text');
  try
    ParseInt('y');
    Check(False, 'StrToInt raises');
  except
    on E: Exception do
      CheckRaises('wrong message', E.ClassName, E.Message);
  end;
  Expect('EInOutError', '''z'' is not a valid integer value');
  try
    ParseInt('z');
    Check(False, 'StrToInt raises');
  except
    on E: Exception do
      CheckRaises('wrong class', E.ClassName, E.Message);
  end;
  Done;
end.
