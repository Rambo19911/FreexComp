// covers: RTL-1016
// phase: 8
program Com_DispCallThreads;
{ com-support.md RTL-1016: the two hooks ComObj installs are process-wide and set once, and a dispatch call keeps
  all its state on the stack. Two threads released together by one event each make many calls (property put, get
  and a method with an argument) through a dispatch interface on a server of their own; every result must be the
  thread's own value. The hooks read inside the threads and after the calls are the ones read at start-up. No COM
  initialisation is needed: the servers are written here. }
uses SysUtils, Windows, ActiveX, ComObj, Classes, SyncObjs, TestKit;

const
  Rounds = 2000;

type
  ICounter = dispinterface
    property Value: Integer dispid 1;
    function Scaled(Factor: Integer): Integer; dispid 2;
  end;

  TServer = class(TInterfacedObject, IDispatch)
  public
    Value: Integer;
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
      DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
  end;

  TCaller = class(TThread)
  public
    Base: Integer;
    Wrong: Integer;
    Calls: Integer;
    SawDispCall, SawSafeCall: Pointer;
  protected
    procedure Execute; override;
  end;

var
  Go: TEvent;

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

{ 1: Value (get / put); 2: Scaled(Factor) = Value * Factor }
function TServer.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
var
  DP: PDispParams;
  R: PVariantArg;
begin
  DP := @Params;
  R := VarResult;
  Result := S_OK;
  if (DispID = 1) and (Flags and DISPATCH_PROPERTYPUT <> 0) then
    Value := DP^.rgvarg^.lVal
  else if DispID = 1 then
  begin
    R^.vt := VT_I4;
    R^.lVal := Value;
  end
  else if DispID = 2 then
  begin
    R^.vt := VT_I4;
    R^.lVal := Value * DP^.rgvarg^.lVal;
  end
  else
    Result := DISP_E_MEMBERNOTFOUND;
end;

function HookOfDispCall: Pointer;
begin
  Move(DispCallProc, Result, SizeOf(Pointer));
end;

function HookOfSafeCall: Pointer;
begin
  Move(SafeCallErrorProc, Result, SizeOf(Pointer));
end;

procedure TCaller.Execute;
var
  D: ICounter;
  I, V: Integer;
begin
  D := TServer.Create as IDispatch;
  SawDispCall := HookOfDispCall;
  SawSafeCall := HookOfSafeCall;
  Go.WaitFor(INFINITE);
  for I := 1 to Rounds do
  begin
    V := Base + I;
    D.Value := V;
    if D.Value <> V then
      Inc(Wrong);
    if D.Scaled(3) <> 3 * V then
      Inc(Wrong);
    Inc(Calls, 3);
  end;
  D := nil;
end;

var
  A, B: TCaller;
  DispHook, SafeHook: Pointer;
begin
  DispHook := HookOfDispCall;
  SafeHook := HookOfSafeCall;
  Section('the hooks (RTL-1013, RTL-1016)');
  Check(DispHook <> nil, 'DispCallProc is set by ComObj');
  Check(SafeHook <> nil, 'SafeCallErrorProc is set by ComObj');

  Section('two threads calling at the same time');
  Go := TEvent.Create(nil, True, False, '');
  A := TCaller.Create(True);
  B := TCaller.Create(True);
  A.Base := 1000000;
  B.Base := 2000000;
  A.Start;
  B.Start;
  Go.SetEvent;
  A.WaitFor;
  B.WaitFor;
  CheckEqI(3 * Rounds, A.Calls, 'first thread: every call made');
  CheckEqI(3 * Rounds, B.Calls, 'second thread: every call made');
  CheckEqI(0, A.Wrong, 'first thread: every result its own');
  CheckEqI(0, B.Wrong, 'second thread: every result its own');
  Check((A.SawDispCall = DispHook) and (B.SawDispCall = DispHook), 'the threads see the same DispCallProc');
  Check((A.SawSafeCall = SafeHook) and (B.SawSafeCall = SafeHook), 'the threads see the same SafeCallErrorProc');
  Check(HookOfDispCall = DispHook, 'DispCallProc unchanged after the calls');
  Check(HookOfSafeCall = SafeHook, 'SafeCallErrorProc unchanged after the calls');
  A.Free;
  B.Free;
  Go.Free;
  Done;
end.
