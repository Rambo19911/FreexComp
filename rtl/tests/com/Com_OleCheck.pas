// covers: RTL-1009, RTL-1010, RTL-1011
// phase: 8
program Com_OleCheck;
{ Unit ComObj (com-support.md section 3): the OLE exception classes, OleError and OleCheck. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

procedure TestSilent;
begin
  Section('OleCheck on success (RTL-1011)');
  OleCheck(S_OK);
  Check(True, 'S_OK is silent');
  OleCheck(S_FALSE);
  Check(True, 'S_FALSE is silent');
  OleCheck(High(Integer));
  Check(True, 'every non-negative code is silent');
end;

procedure TestRaises;
var
  Raised: Boolean;
begin
  Section('OleCheck on failure (RTL-1010, RTL-1011)');
  Raised := False;
  try
    OleCheck(E_NOINTERFACE);
  except
    on E: EOleSysError do
    begin
      Raised := True;
      CheckEqS('EOleSysError', E.ClassName, 'the class');
      CheckEqS('80004002', IntToHex(E.ErrorCode, 8), 'ErrorCode');
      Check(E.Message <> '', 'the system message is not empty');
      Check(Copy(E.Message, 1, 9) <> 'OLE error', 'the message is the text of the system');
      CheckEqI(0, E.HelpContext, 'HelpContext');
    end;
  end;
  Check(Raised, 'OleCheck(E_NOINTERFACE) raises');

  Raised := False;
  try
    OleCheck(HResult($8ABC1234));
  except
    on E: EOleSysError do
    begin
      Raised := True;
      CheckEqS('OLE error 8ABC1234', E.Message, 'a code the system has no text for');
      CheckEqS('8ABC1234', IntToHex(E.ErrorCode, 8), 'its ErrorCode');
    end;
  end;
  Check(Raised, 'OleCheck of an unknown code raises');

  Raised := False;
  try
    OleError(S_FALSE);
  except
    on E: EOleError do
    begin
      Raised := True;
      CheckEqS('EOleSysError', E.ClassName, 'OleError raises for any code');
      CheckEqI(1, EOleSysError(E).ErrorCode, 'the code given');
    end;
  end;
  Check(Raised, 'OleError raises');
end;

procedure TestClasses;
var
  S: EOleSysError;
  X: EOleException;
begin
  Section('exception classes (RTL-1009, RTL-1010)');
  Check(EOleError.InheritsFrom(Exception), 'EOleError is an Exception');
  Check(EOleSysError.InheritsFrom(EOleError), 'EOleSysError is an EOleError');
  Check(EOleException.InheritsFrom(EOleSysError), 'EOleException is an EOleSysError');

  S := EOleSysError.Create('own text', E_FAIL, 17);
  try
    CheckEqS('own text', S.Message, 'a message that is not empty is kept');
    CheckEqS('80004005', IntToHex(S.ErrorCode, 8), 'ErrorCode');
    CheckEqI(17, S.HelpContext, 'HelpContext');
    S.ErrorCode := E_ABORT;
    CheckEqS('80004004', IntToHex(S.ErrorCode, 8), 'ErrorCode can be written');
  finally
    S.Free;
  end;

  X := EOleException.Create('description', DISP_E_EXCEPTION, 'the source', 'the help file', 5);
  try
    CheckEqS('description', X.Message, 'Message');
    CheckEqS('80020009', IntToHex(X.ErrorCode, 8), 'ErrorCode');
    CheckEqS('the source', X.Source, 'Source');
    CheckEqS('the help file', X.HelpFile, 'HelpFile');
    CheckEqI(5, X.HelpContext, 'HelpContext');
  finally
    X.Free;
  end;
end;

begin
  TestSilent;
  TestRaises;
  TestClasses;
  Done;
end.
