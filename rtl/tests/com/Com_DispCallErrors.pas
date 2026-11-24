// covers: RTL-1014
// phase: 8
program Com_DispCallErrors;
{ com-support.md RTL-1014 steps 4 to 6: DISP_E_EXCEPTION with filled exception information (directly and through
  the deferred fill-in callback) becomes EOleException with its fields; any other failing code and a result that
  cannot be converted become EOleSysError; by-reference variables that are copied back (string, Boolean) keep their
  values after a failure, the others are passed by their own address; nothing stays allocated. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

type
  PSmallInt = ^SmallInt;

  IFaulty = dispinterface
    procedure Fail; dispid 1;
    procedure FailDeferred; dispid 2;
    procedure Missing; dispid 3;
    function BadResult: Integer; dispid 4;
    procedure FailRefs(var I: Integer; var S: string; var B: Boolean; const T: string); dispid 5;
    function BadResultRefs(var I: Integer; var S: string; var B: Boolean): Integer; dispid 6;
    procedure FailBare; dispid 7;
    function FailWithResult: string; dispid 8;
    function GoodRefs(var I: Integer; var S: string; var B: Boolean): Integer; dispid 9;
  end;

  TServer = class(TInterfacedObject, IDispatch)
  public
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
      DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
  end;

var
  FillInCalls: Integer;

function NewBStr(const S: string): TBStr;
begin
  Result := SysAllocStringLen(PWideChar(S), Length(S));
end;

{ the deferred fill-in callback of the exception information }
function FillIn(Info: PExcepInfo): HResult; stdcall;
begin
  Inc(FillInCalls);
  Info^.bstrSource := NewBStr('Deferred.Source');
  Info^.bstrDescription := NewBStr('filled in later');
  Info^.bstrHelpFile := NewBStr('later.hlp');
  Info^.dwHelpContext := 77;
  Info^.scode := HResult($80040201);
  Info^.pfnDeferredFillIn := nil;
  Result := S_OK;
end;

function TServer.GetTypeInfoCount(out Count: Integer): HResult;
begin
  Count := 0;
  Result := S_OK;
end;

function TServer.GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult;
begin
  Result := E_NOTIMPL;
end;

function TServer.GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
  DispIDs: Pointer): HResult;
begin
  Result := E_NOTIMPL;
end;

function TServer.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
var
  DP: PDispParams;
  V: PVariantArg;
  R: PVariantArg;
  Info: PExcepInfo;
  I: Integer;
begin
  DP := @Params;
  R := VarResult;
  Info := ExcepInfo;
  { every by-reference argument is changed before the outcome is decided }
  V := DP^.rgvarg;
  for I := 0 to Integer(DP^.cArgs) - 1 do
  begin
    if V^.vt = VT_I4 or VT_BYREF then
      PInteger(V^.byRef)^ := 999
    else if V^.vt = VT_BOOL or VT_BYREF then
      PSmallInt(V^.byRef)^ := -1
    else if V^.vt = VT_BSTR or VT_BYREF then
    begin
      SysFreeString(PPointer(V^.byRef)^);
      PPointer(V^.byRef)^ := NewBStr('changed by the server');
    end;
    Inc(V);
  end;
  Result := DISP_E_EXCEPTION;
  case DispID of
    1:
      begin
        Info^.wCode := 0;
        Info^.bstrSource := NewBStr('Test.Faulty');
        Info^.bstrDescription := NewBStr('it br'#0'oke');
        Info^.bstrHelpFile := NewBStr('faulty.hlp');
        Info^.dwHelpContext := 42;
        Info^.scode := HResult($80040200);
      end;
    2: Info^.pfnDeferredFillIn := @FillIn;
    3: Result := DISP_E_MEMBERNOTFOUND;
    4, 6:
      begin
        R^.vt := VT_BSTR;
        R^.bstrVal := NewBStr('not a number');
        Result := S_OK;
      end;
    5:
      begin
        { no code of its own: the caller reports DISP_E_EXCEPTION }
        Info^.wCode := 1001;
        Info^.bstrDescription := NewBStr('by reference');
      end;
    7: ;
    8:
      begin
        { a result filled in although the call fails must still be released by the caller }
        R^.vt := VT_BSTR;
        R^.bstrVal := NewBStr('ignored');
        Result := E_FAIL;
      end;
    9:
      begin
        R^.vt := VT_I4;
        R^.lVal := 5;
        Result := S_OK;
      end;
  else
    Result := DISP_E_MEMBERNOTFOUND;
  end;
end;

var
  D: IFaulty;
  Outcome: string;
  HelpCtx: Integer;

{ runs member Which and writes down what it raised }
procedure Call(Which: Integer; var I: Integer; var S: string; var B: Boolean);
var
  T: string;
begin
  Outcome := 'no exception';
  HelpCtx := -1;
  try
    case Which of
      1: D.Fail;
      2: D.FailDeferred;
      3: D.Missing;
      4: D.BadResult;
      5: D.FailRefs(I, S, B, 'by value');
      6: I := D.BadResultRefs(I, S, B);
      7: D.FailBare;
      8: T := D.FailWithResult;
      9: D.GoodRefs(I, S, B);
    end;
  except
    on E: EOleException do
    begin
      Outcome := E.ClassName + ' ' + IntToHex(E.ErrorCode, 8) + ' [' + E.Message + '] [' + E.Source + '] [' +
        E.HelpFile + ']';
      HelpCtx := E.HelpContext;
    end;
    on E: EOleSysError do
    begin
      Outcome := E.ClassName + ' ' + IntToHex(E.ErrorCode, 8);
      if E.Message = '' then
        Outcome := Outcome + ' EMPTY MESSAGE';
      HelpCtx := E.HelpContext;
    end;
    on E: Exception do
      Outcome := E.ClassName;
  end;
end;

procedure Run;
var
  I: Integer;
  S: string;
  B: Boolean;
begin
  I := 1;
  S := 'kept';
  B := False;

  Section('exception information (RTL-1014 step 4)');
  Call(1, I, S, B);
  CheckEqS('EOleException 80040200 [it br'#0'oke] [Test.Faulty] [faulty.hlp]', Outcome, 'the filled record');
  CheckEqI(42, HelpCtx, 'its help context');
  Call(2, I, S, B);
  CheckEqS('EOleException 80040201 [filled in later] [Deferred.Source] [later.hlp]', Outcome,
    'through the deferred fill-in callback');
  CheckEqI(77, HelpCtx, 'its help context');
  CheckEqI(1, FillInCalls, 'the callback ran once');
  Call(7, I, S, B);
  CheckEqS('EOleException 80020009 [' + SysErrorMessage($80020009) + '] [] []', Outcome,
    'an empty record: the code is DISP_E_EXCEPTION and the message is the text of the system (RTL-1010)');
  CheckEqI(0, HelpCtx, 'no help context');

  Section('other failures (RTL-1014 steps 4 and 5)');
  Call(3, I, S, B);
  CheckEqS('EOleSysError 80020003', Outcome, 'DISP_E_MEMBERNOTFOUND');
  CheckEqI(0, HelpCtx, 'no help context');
  Call(8, I, S, B);
  CheckEqS('EOleSysError 80004005', Outcome, 'a failing code with a result record');
  Call(4, I, S, B);
  CheckEqS('EOleSysError 80020005', Outcome, 'a result that cannot be converted: the code of the conversion');

  Section('by-reference variables after a failure (RTL-1014 step 6)');
  Call(5, I, S, B);
  CheckEqS('EOleException 80020009 [by reference] [] []', Outcome, 'the call failed');
  CheckEqI(999, I, 'the Integer was passed by its own address: the server wrote to it');
  I := 1;
  CheckEqS('kept', S, 'the string is unchanged');
  CheckEqB(False, B, 'the Boolean is unchanged');
  Call(6, I, S, B);
  CheckEqS('EOleSysError 80020005', Outcome, 'the result could not be converted');
  CheckEqI(999, I, 'the Integer was passed by its own address; the result was not stored');
  I := 1;
  CheckEqS('kept', S, 'the string is unchanged');
  CheckEqB(False, B, 'the Boolean is unchanged');
  Call(9, I, S, B);
  CheckEqS('no exception', Outcome, 'the same arguments on success');
  CheckEqI(999, I, 'the Integer is copied back');
  CheckEqS('changed by the server', S, 'the string is copied back');
  CheckEqB(True, B, 'the Boolean is copied back');
end;

procedure Quiet;
var
  I, N: Integer;
  S: string;
  B: Boolean;
begin
  I := 1;
  S := 'kept';
  B := False;
  for N := 1 to 9 do
    Call(N, I, S, B);
  Outcome := '';
end;

var
  Keep: IDispatch;
  Before: NativeInt;

begin
  Keep := TServer.Create;
  D := Keep;
  Run;

  Section('nothing stays allocated (RTL-1014 step 6)');
  Outcome := '';
  Before := _LiveAllocCount;
  Quiet;
  CheckEqI(0, _LiveAllocCount - Before, 'live allocations after every outcome');

  D := nil;
  Keep := nil;
  Done;
end.
