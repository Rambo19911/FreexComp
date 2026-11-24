{ DataTestUtils - assertion and printing helpers shared by the data-access test programs.
  Every check prints one line, "ok <Name>" or "FAIL <Name>...", so a program's expected output lists every
  check and a regression shows as a differing line. }
unit DataTestUtils;

interface

uses
  SysUtils;

type
  TTestMethod = procedure of object;

{ Prints "ok <Name>" or "FAIL <Name>". }
procedure Check(Cond: Boolean; const Name: string);
{ Prints "ok <Name>" or "FAIL <Name>: got '<Actual>' expected '<Expected>'". }
procedure CheckEq(const Actual, Expected, Name: string);
procedure CheckEqInt(Actual, Expected: Int64; const Name: string);
procedure CheckEqBool(Actual, Expected: Boolean; const Name: string);
{ Calls Proc; passes when it raises an exception whose class name is ExceptClassName and whose message
  contains MessagePart (an empty MessagePart matches any message). }
procedure CheckRaises(Proc: TTestMethod; const ExceptClassName, MessagePart, Name: string);
procedure CheckRaisesProc(Proc: TProcedure; const ExceptClassName, MessagePart, Name: string);
{ Prints "== <Title>". }
procedure Section(const Title: string);
{ Number of checks that printed FAIL so far. }
function FailedChecks: Integer;

implementation

var
  Failed: Integer;

procedure Fail(const Name, Detail: string);
begin
  Inc(Failed);
  if Detail = '' then
    WriteLn('FAIL ', Name)
  else
    WriteLn('FAIL ', Name, ': ', Detail);
end;

procedure Check(Cond: Boolean; const Name: string);
begin
  if Cond then
    WriteLn('ok ', Name)
  else
    Fail(Name, '');
end;

procedure CheckEq(const Actual, Expected, Name: string);
begin
  if Actual = Expected then
    WriteLn('ok ', Name)
  else
    Fail(Name, 'got ''' + Actual + ''' expected ''' + Expected + '''');
end;

procedure CheckEqInt(Actual, Expected: Int64; const Name: string);
begin
  CheckEq(IntToStr(Actual), IntToStr(Expected), Name);
end;

function BoolName(Value: Boolean): string;
begin
  if Value then
    Result := 'True'
  else
    Result := 'False';
end;

procedure CheckEqBool(Actual, Expected: Boolean; const Name: string);
begin
  CheckEq(BoolName(Actual), BoolName(Expected), Name);
end;

procedure ReportRaised(Raised: Boolean; const GotClass, GotMessage, ExceptClassName, MessagePart, Name: string);
begin
  if not Raised then
    Fail(Name, 'no exception, expected ' + ExceptClassName)
  else if (GotClass = ExceptClassName) and ((MessagePart = '') or (Pos(MessagePart, GotMessage) > 0)) then
    WriteLn('ok ', Name)
  else
    Fail(Name, 'got ' + GotClass + ' ''' + GotMessage + ''' expected ' + ExceptClassName + ' containing ''' +
      MessagePart + '''');
end;

procedure CheckRaises(Proc: TTestMethod; const ExceptClassName, MessagePart, Name: string);
var
  Raised: Boolean;
  GotClass, GotMessage: string;
begin
  Raised := False;
  try
    Proc;
  except
    on E: Exception do
    begin
      Raised := True;
      GotClass := E.ClassName;
      GotMessage := E.Message;
    end;
  end;
  ReportRaised(Raised, GotClass, GotMessage, ExceptClassName, MessagePart, Name);
end;

procedure CheckRaisesProc(Proc: TProcedure; const ExceptClassName, MessagePart, Name: string);
var
  Raised: Boolean;
  GotClass, GotMessage: string;
begin
  Raised := False;
  try
    Proc;
  except
    on E: Exception do
    begin
      Raised := True;
      GotClass := E.ClassName;
      GotMessage := E.Message;
    end;
  end;
  ReportRaised(Raised, GotClass, GotMessage, ExceptClassName, MessagePart, Name);
end;

procedure Section(const Title: string);
begin
  WriteLn('== ', Title);
end;

function FailedChecks: Integer;
begin
  Result := Failed;
end;

end.
