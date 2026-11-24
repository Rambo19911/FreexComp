// covers: RTL-1013, RTL-1015
// phase: 8
program Com_SafecallNoErrorInfo;
{ com-support.md RTL-1013: a failing result code from a safecall method that publishes no error information
  becomes EOleSysError with the code preserved, and error information that an earlier failure published and its
  caller consumed is not used again. No COM initialisation is needed for any of it (RTL-1015). }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

type
  IPlain = interface
    ['{6D8F0A12-3C4E-4B5A-9D7F-1A2B3C4D5E6F}']
    procedure Fail; safecall;
    procedure FailWithInfo; safecall;
  end;

  { SafeCallException gives a code of its own; error information only when asked }
  TPlain = class(TInterfacedObject, IPlain)
  public
    WithInfo: Boolean;
    procedure Fail; safecall;
    procedure FailWithInfo; safecall;
    function SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult; override;
  end;

  { the base SafeCallException: E_UNEXPECTED, no error information }
  TBase = class(TInterfacedObject, IPlain)
  public
    procedure Fail; safecall;
    procedure FailWithInfo; safecall;
  end;

procedure TPlain.Fail;
begin
  WithInfo := False;
  raise Exception.Create('not published');
end;

procedure TPlain.FailWithInfo;
begin
  WithInfo := True;
  raise Exception.Create('published');
end;

function TPlain.SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult;
begin
  if WithInfo then
    HandleSafeCallException(ExceptObject, ExceptAddr, IPlain, 'Test.Plain', '');
  Result := HResult($80040155);
end;

procedure TBase.Fail;
begin
  raise Exception.Create('base');
end;

procedure TBase.FailWithInfo;
begin
  raise Exception.Create('base');
end;

{ the class, code and message a failing call of Fail (or FailWithInfo) raises }
function Outcome(const P: IPlain; WithInfo: Boolean): string;
begin
  Result := 'no exception';
  try
    if WithInfo then
      P.FailWithInfo
    else
      P.Fail;
  except
    on E: EOleException do
      Result := E.ClassName + ' ' + IntToHex(E.ErrorCode, 8) + ' [' + E.Message + '] [' + E.Source + ']';
    on E: EOleSysError do
      Result := E.ClassName + ' ' + IntToHex(E.ErrorCode, 8);
    on E: Exception do
      Result := E.ClassName;
  end;
end;

var
  P, B: IPlain;
  Info: IErrorInfo;
  Raised: Boolean;

begin
  P := TPlain.Create;
  B := TBase.Create;

  Section('no error information (RTL-1013)');
  CheckEqI(S_FALSE, GetErrorInfo(0, Info), 'the thread starts without error information');
  CheckEqS('EOleSysError 80040155', Outcome(P, False), 'the code of the method is preserved');
  CheckEqS('EOleSysError 8000FFFF', Outcome(B, False), 'the base SafeCallException');
  Raised := False;
  try
    P.Fail;
  except
    on E: EOleSysError do
    begin
      Raised := True;
      Check(E.Message <> '', 'the message is never empty');
    end;
  end;
  Check(Raised, 'the failure raises');

  Section('consumed error information is not used again (RTL-1013)');
  CheckEqS('EOleException 80040155 [published] [Test.Plain]', Outcome(P, True), 'a failure with error information');
  CheckEqI(S_FALSE, GetErrorInfo(0, Info), 'the handler consumed it');
  CheckEqS('EOleSysError 80040155', Outcome(P, False), 'the next failure without information');
  CheckEqS('EOleSysError 8000FFFF', Outcome(B, True), 'and one of another object');
  CheckEqS('EOleException 80040155 [published] [Test.Plain]', Outcome(P, True), 'information again');
  CheckEqS('EOleSysError 80040155', Outcome(P, False), 'and none again');

  P := nil;
  B := nil;
  Done;
end.
