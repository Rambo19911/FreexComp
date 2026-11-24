// covers: RTL-1000, RTL-1001, RTL-1002, RTL-1003, RTL-1004, RTL-1005, ABI-200, ABI-201, ABI-202, ABI-203
// phase: 8
program Sys_SafecallHooks;
{ The COM support face of System (com-support.md section 1): the constants, IDispatch, TObject.SafeCallException in
  virtual slot 10, the hooks SafeCallErrorProc / DispCallProc, the helpers _SafecallError, _SafecallException and
  _DispCall, run-time errors 230 / 231 as ESafecallException / EDispatchError, and = / <> on TGUID. }
{$POINTERMATH ON}
uses SysUtils, TestKit;

type
  ECoded = class(Exception);

  TPlain = class
  end;

  { the override answers with a code taken from the object and records what it was given }
  TCoded = class
  public
    Code: HResult;
    SeenClass: string;
    SeenAddr: Pointer;
    RaiseInside: Boolean;
    function SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult; override;
  end;

  IWork = interface
    ['{7B1D2C30-4E5F-4A60-8B71-9C0D1E2F3A4B}']
    function Twice(N: Integer): Integer; safecall;
    procedure Fail; safecall;
  end;

  IWorkRaw = interface
    ['{7B1D2C30-4E5F-4A60-8B71-9C0D1E2F3A4B}']
    function Twice(N: Integer; out Value: Integer): HResult; stdcall;
    function Fail: HResult; stdcall;
  end;

  TWork = class(TInterfacedObject, IWork)
  public
    function Twice(N: Integer): Integer; safecall;
    procedure Fail; safecall;
    function SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult; override;
  end;

var
  HookCode: HResult;
  HookAddr: Pointer;
  HookCalls: Integer;
  HookRaises: Boolean;
  DispCalls: Integer;
  DispDest, DispDesc, DispArgs: Pointer;

function TCoded.SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult;
begin
  if ExceptObject = nil then
    SeenClass := '(nil)'
  else
    SeenClass := ExceptObject.ClassName;
  SeenAddr := ExceptAddr;
  if RaiseInside then
    raise ECoded.Create('inside the override');
  Result := Code;
end;

function TWork.Twice(N: Integer): Integer;
begin
  Result := 2 * N;
end;

procedure TWork.Fail;
begin
  raise ECoded.Create('work failed');
end;

function TWork.SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult;
begin
  Result := HResult($80040123);
end;

procedure SafecallHook(ErrorCode: HResult; ErrorAddr: Pointer);
begin
  HookCalls := HookCalls + 1;
  HookCode := ErrorCode;
  HookAddr := ErrorAddr;
  if HookRaises then
    raise ECoded.Create('from the hook') at ErrorAddr;
end;

procedure DispHook(Dest: Pointer; const Disp: IDispatch; Desc: Pointer; Args: Pointer);
begin
  DispCalls := DispCalls + 1;
  DispDest := Dest;
  DispDesc := Desc;
  DispArgs := Args;
end;

{ _SafecallException inside a handler, as the compiler emits it for a safecall routine }
function Guarded(Instance: TObject; RaiseCoded: Boolean): HResult;
begin
  Result := S_OK;
  try
    if RaiseCoded then
      raise ECoded.Create('guarded')
    else
      raise Exception.Create('guarded');
  except
    Result := _SafecallException(Pointer(Instance));
  end;
end;

procedure TestConstants;
var
  G: TGUID;
  I: Integer;
  S: string;
begin
  Section('constants and IDispatch (RTL-1000, RTL-1001)');
  CheckEqI(1, S_FALSE, 'S_FALSE');
  CheckEqS('8000FFFF', IntToHex(E_UNEXPECTED, 8), 'E_UNEXPECTED');
  G := IDispatch;
  S := '';
  for I := 0 to 15 do
    S := S + IntToHex(PByte(@G)[I], 2);
  CheckEqS('0004020000000000C000000000000046', S, 'IDispatch GUID bytes');
  Check(Pointer(SafeCallErrorProc) = nil, 'SafeCallErrorProc is nil at start-up');
  Check(Pointer(DispCallProc) = nil, 'DispCallProc is nil at start-up');
end;

procedure TestGuidOperators;
var
  A, B: TGUID;
begin
  Section('TGUID = and <>');
  A := IDispatch;
  B := A;
  Check(A = B, 'equal values');
  Check(not (A <> B), 'equal values are not different');
  B.D4[7] := B.D4[7] xor 1;
  Check(A <> B, 'last byte differs');
  Check(not (A = B), 'different values are not equal');
  B := A;
  B.D1 := B.D1 + 1;
  Check(A <> B, 'first field differs');
  Check(A = IDispatch, 'value = interface type');
  Check(A <> IInterface, 'value <> another interface type');
end;

procedure TestSafeCallException;
var
  P: TPlain;
  C: TCoded;
begin
  Section('TObject.SafeCallException and _SafecallException (RTL-1002, RTL-1005)');
  P := TPlain.Create;
  C := TCoded.Create;
  try
    CheckEqS('8000FFFF', IntToHex(P.SafeCallException(nil, nil), 8), 'base method');
    CheckEqS('8000FFFF', IntToHex(Guarded(P, False), 8), 'base method through the helper');
    CheckEqS('8000FFFF', IntToHex(Guarded(nil, False), 8), 'no instance');

    C.Code := HResult($80040200);
    CheckEqS('80040200', IntToHex(Guarded(C, True), 8), 'the code of an override is returned');
    CheckEqS('ECoded', C.SeenClass, 'the override receives the exception object');
    Check(C.SeenAddr <> nil, 'the override receives the exception address');

    C.Code := S_OK;
    CheckEqS('8000FFFF', IntToHex(Guarded(C, False), 8), 'an override returning 0 gives E_UNEXPECTED');
    CheckEqS('Exception', C.SeenClass, 'the override was called');
    C.Code := S_FALSE;
    CheckEqS('8000FFFF', IntToHex(Guarded(C, False), 8), 'an override returning 1 gives E_UNEXPECTED');

    C.Code := HResult($80040200);
    C.RaiseInside := True;
    CheckEqS('8000FFFF', IntToHex(Guarded(C, True), 8), 'an exception of the override is swallowed');
    Check(ExceptObject = nil, 'no handler is left active');
  finally
    C.Free;
    P.Free;
  end;
end;

procedure TestSafecallError;
var
  Reached: Boolean;
begin
  Section('_SafecallError and SafeCallErrorProc (RTL-1003, RTL-1004, RTL-1005)');
  Expect('ESafecallException', 'Exception in safecall method');
  Reached := False;
  try
    _SafecallError(HResult($80004005));
    Reached := True;
  except
    on E: Exception do
      CheckRaises('no hook: run-time error 230', E.ClassName, E.Message);
  end;
  Check(not Reached, 'the helper does not return');

  SafeCallErrorProc := @SafecallHook;
  try
    HookRaises := True;
    Expect('ECoded', 'from the hook');
    try
      _SafecallError(HResult($80004005));
    except
      on E: Exception do
        CheckRaises('the hook raises', E.ClassName, E.Message);
    end;
    CheckEqI(1, HookCalls, 'the hook was called once');
    CheckEqS('80004005', IntToHex(HookCode, 8), 'the hook receives the code');
    Check(HookAddr <> nil, 'the hook receives a non-nil address');

    HookRaises := False;
    Expect('ESafecallException', 'Exception in safecall method');
    try
      _SafecallError(HResult($8007000E));
    except
      on E: Exception do
        CheckRaises('a hook that returns: run-time error 230', E.ClassName, E.Message);
    end;
    CheckEqI(2, HookCalls, 'the hook was called before the error');
    CheckEqS('8007000E', IntToHex(HookCode, 8), 'the second code');
  finally
    SafeCallErrorProc := nil;
  end;
end;

procedure TestSafecallMethods;
var
  W: IWork;
  R: IWorkRaw;
  N: Integer;
begin
  Section('safecall methods use the helpers');
  W := TWork.Create;
  R := IWorkRaw(Pointer(W));
  CheckEqI(42, W.Twice(21), 'result of a safecall function');
  CheckEqI(0, R.Twice(4, N), 'the raw call returns S_OK');
  CheckEqI(8, N, 'the raw call stores the result');
  CheckEqS('80040123', IntToHex(R.Fail, 8), 'the raw call returns the code of SafeCallException');

  Expect('ESafecallException', 'Exception in safecall method');
  try
    W.Fail;
    Check(False, 'not reached');
  except
    on E: Exception do
      CheckRaises('a failing safecall call without a hook', E.ClassName, E.Message);
  end;

  HookCalls := 0;
  HookRaises := True;
  SafeCallErrorProc := @SafecallHook;
  try
    Expect('ECoded', 'from the hook');
    try
      W.Fail;
    except
      on E: Exception do
        CheckRaises('a failing safecall call with a hook', E.ClassName, E.Message);
    end;
    CheckEqI(1, HookCalls, 'the hook was called once');
    CheckEqS('80040123', IntToHex(HookCode, 8), 'the hook receives the code of the method');
  finally
    SafeCallErrorProc := nil;
  end;
end;

procedure TestDispCall;
var
  Dest, Desc, Args: Integer;
begin
  Section('_DispCall and DispCallProc (RTL-1003, RTL-1004, RTL-1005)');
  Expect('EDispatchError', 'Dispatch calls need unit ComObj');
  try
    _DispCall(@Dest, nil, @Desc, @Args);
    Check(False, 'not reached');
  except
    on E: Exception do
      CheckRaises('no hook: run-time error 231', E.ClassName, E.Message);
  end;

  DispCallProc := @DispHook;
  try
    _DispCall(@Dest, nil, @Desc, @Args);
    CheckEqI(1, DispCalls, 'the hook was called');
    Check((DispDest = @Dest) and (DispDesc = @Desc) and (DispArgs = @Args), 'the hook receives the arguments');
  finally
    DispCallProc := nil;
  end;
end;

begin
  TestConstants;
  TestGuidOperators;
  TestSafeCallException;
  TestSafecallError;
  TestSafecallMethods;
  TestDispCall;
  Done;
end.
