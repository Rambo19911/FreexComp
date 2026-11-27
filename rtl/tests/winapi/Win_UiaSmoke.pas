// covers: RTL-1110, RTL-1111, RTL-1112, RTL-1113, RTL-1114, RTL-1115
// phase: 8
program Win_UiaSmoke;
{ The UI Automation bindings against the system: every function the unit UIAutomation declares for
  uiautomationcore.dll and the three safe-array functions of ActiveX are called once, with arguments whose result a
  wrong declaration cannot produce, on a message-only window (no desktop is needed and nothing is shown). The
  annotation object is created by CoCreateInstance (which proves its class and interface ids) and the two slots
  the framework uses are called on it. The four property GUIDs are proven by the system's own lookup of the
  identifier that belongs to a GUID (UiaLookupId, reached through GetProcAddress here only, so that the binding
  imports nothing more). The results that depend on the system's state are printed as words. }
uses Windows, ActiveX, UIAutomation, TestKit;

type
  { a server-side provider of the test window: the system may call it while an event is raised }
  TTestProvider = class(TInterfacedObject, IRawElementProviderSimple)
  private
    FWnd: HWND;
  public
    constructor Create(Wnd: HWND);
    function get_ProviderOptions(out pRetVal: Integer): HResult; stdcall;
    function GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult; stdcall;
    function GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult; stdcall;
    function get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult; stdcall;
  end;

  TUiaLookupId = function(IdType: Integer; const Guid: TGUID): Integer; stdcall;

const
  AutomationIdentifierType_Property = 0;
  DISP_E_BADINDEX = HResult($8002000B);

constructor TTestProvider.Create(Wnd: HWND);
begin
  inherited Create;
  FWnd := Wnd;
end;

function TTestProvider.get_ProviderOptions(out pRetVal: Integer): HResult;
begin
  pRetVal := ProviderOptions_ServerSideProvider;
  Result := S_OK;
end;

{ the system passes the out parameters uninitialised: they are cleared without releasing what they hold }
function TTestProvider.GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Result := S_OK;
end;

function TTestProvider.GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  pRetVal.vt := VT_EMPTY;
  Result := S_OK;
end;

function TTestProvider.get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Result := UiaHostProviderFromHwnd(FWnd, pRetVal);
end;

{ the provider kind of a ProviderOptions value }
function OptionsWord(Options: Integer): string;
begin
  if Options and (ProviderOptions_ClientSideProvider or ProviderOptions_ServerSideProvider) =
    ProviderOptions_ServerSideProvider then
    Result := 'server-side'
  else if Options and (ProviderOptions_ClientSideProvider or ProviderOptions_ServerSideProvider) =
    ProviderOptions_ClientSideProvider then
    Result := 'client-side'
  else
    Result := 'other ' + IntStr(Options);
end;

function ResultWord(R: HResult): string;
begin
  if R = S_OK then
    Result := 'S_OK'
  else if R = S_FALSE then
    Result := 'S_FALSE'
  else if R = E_INVALIDARG then
    Result := 'E_INVALIDARG'
  else if R = UIA_E_ELEMENTNOTAVAILABLE then
    Result := 'UIA_E_ELEMENTNOTAVAILABLE'
  else
    Result := 'other ' + HexStr64(R);
end;

procedure TestWithoutComInitialisation(Wnd: HWND);
var
  Listening: BOOL;
  Host: IRawElementProviderSimple;
  NotSupported: IUnknown;
  Options: Integer;
  Provider: IRawElementProviderSimple;
  OldValue, NewValue: TVariantArg;
  RuntimeId: array[0..1] of Integer;
begin
  Section('uiautomationcore.dll (RTL-1113, RTL-1114)');
  Listening := UiaClientsAreListening;     // True when some client on this desktop listens: not printed
  Check(Listening or not Listening, 'UiaClientsAreListening returns without fault');

  CheckEqI(S_OK, UiaHostProviderFromHwnd(Wnd, Host), 'UiaHostProviderFromHwnd');
  Check(Host <> nil, 'a host provider');
  CheckEqI(S_OK, Host.get_ProviderOptions(Options), 'get_ProviderOptions of the host provider');
  CheckEqS('server-side', OptionsWord(Options), 'the options of the host provider');
  Host := nil;

  CheckEqI(S_OK, UiaGetReservedNotSupportedValue(NotSupported), 'UiaGetReservedNotSupportedValue');
  Check(NotSupported <> nil, 'the not-supported value');
  NotSupported := nil;

  CheckEqI(0, UiaReturnRawElementProvider(Wnd, 0, 0, nil), 'UiaReturnRawElementProvider(h, 0, 0, nil)');

  Provider := TTestProvider.Create(Wnd);
  Check(Succeeded(UiaRaiseAutomationEvent(Provider, UIA_Invoke_InvokedEventId)), 'UiaRaiseAutomationEvent');
  VariantInit(OldValue);
  OldValue.vt := VT_I4;
  OldValue.lVal := ToggleState_Off;
  VariantInit(NewValue);
  NewValue.vt := VT_I4;
  NewValue.lVal := ToggleState_On;
  Check(Succeeded(UiaRaiseAutomationPropertyChangedEvent(Provider, UIA_ToggleToggleStatePropertyId, OldValue, NewValue)),
    'UiaRaiseAutomationPropertyChangedEvent');
  CheckEqI(VT_I4, OldValue.vt, 'the old value is the caller''s');
  CheckEqI(ToggleState_On, NewValue.lVal, 'the new value is the caller''s');
  RuntimeId[0] := UiaAppendRuntimeId;
  RuntimeId[1] := 1;
  Check(Succeeded(UiaRaiseStructureChangedEvent(Provider, StructureChangeType_ChildrenInvalidated, @RuntimeId[0], 2)),
    'UiaRaiseStructureChangedEvent');
  CheckEqS('S_OK', ResultWord(UiaDisconnectProvider(Provider)), 'UiaDisconnectProvider');
  Provider := nil;
end;

procedure TestPropertyGuids;
var
  Lookup: TUiaLookupId;
begin
  Section('property GUIDs (RTL-1112)');
  Lookup := TUiaLookupId(GetProcAddress(GetModuleHandleW(uiautomationcore), 'UiaLookupId'));
  Check(Assigned(Lookup), 'UiaLookupId found');
  CheckEqI(UIA_NamePropertyId, Lookup(AutomationIdentifierType_Property, Name_Property_GUID), 'Name_Property_GUID');
  CheckEqI(UIA_HelpTextPropertyId, Lookup(AutomationIdentifierType_Property, HelpText_Property_GUID),
    'HelpText_Property_GUID');
  CheckEqI(UIA_AutomationIdPropertyId, Lookup(AutomationIdentifierType_Property, AutomationId_Property_GUID),
    'AutomationId_Property_GUID');
  CheckEqI(UIA_ControlTypePropertyId, Lookup(AutomationIdentifierType_Property, ControlType_Property_GUID),
    'ControlType_Property_GUID');
end;

procedure TestAnnotation(Wnd: HWND);
var
  Services: IAccPropServices;
  Props: array[0..0] of TGUID;
begin
  Section('direct annotation (RTL-1112, RTL-1114)');
  CheckEqI(S_OK, CoCreateInstance(CLSID_AccPropServices, nil, CLSCTX_INPROC_SERVER, IID_IAccPropServices, Services),
    'the annotation object');
  Check(Services <> nil, 'IAccPropServices');
  CheckEqI(S_OK, Services.SetHwndPropStr(Wnd, DWORD(OBJID_CLIENT), CHILDID_SELF, Name_Property_GUID, 'Smoke name'),
    'SetHwndPropStr');
  Props[0] := Name_Property_GUID;
  CheckEqI(S_OK, Services.ClearHwndProps(Wnd, DWORD(OBJID_CLIENT), CHILDID_SELF, @Props[0], 1), 'ClearHwndProps');
  Services := nil;
end;

procedure TestSafeArray;
var
  A: PSafeArray;
  I, Idx, Value: Integer;
  Data: PInteger;
begin
  Section('safe arrays (RTL-1113)');
  A := SafeArrayCreateVector(VT_I4, 0, 4);
  Check(A <> nil, 'SafeArrayCreateVector');
  CheckEqI(1, PWord(A)^, 'one dimension');
  CheckEqI(4, PCardinal(NativeUInt(A) + 4)^, 'elements of four bytes');
  for I := 0 to 3 do
  begin
    Idx := I;                                     // @ of a for control variable is E2053 (LANG-361)
    Value := (I + 1) * 10;
    CheckEqI(S_OK, SafeArrayPutElement(A, @Idx, @Value), 'SafeArrayPutElement ' + IntStr(I));
  end;
  I := 4;
  Value := 50;
  CheckEqI(DISP_E_BADINDEX, SafeArrayPutElement(A, @I, @Value), 'an index past the end');
  Data := PPointer(NativeUInt(A) + 16)^;          // pvData of SAFEARRAY
  CheckEqI(10, Data^, 'element 0');
  CheckEqI(40, PInteger(NativeUInt(Data) + 12)^, 'element 3');
  CheckEqI(S_OK, SafeArrayDestroy(A), 'SafeArrayDestroy');
end;

var
  Wnd: HWND;
  Co: HResult;
begin
  Wnd := CreateWindowExW(0, 'STATIC', 'Win_UiaSmoke', 0, 0, 0, 0, 0, HWND_MESSAGE, 0, HInstance, nil);
  Check(Wnd <> 0, 'a message-only window');
  TestWithoutComInitialisation(Wnd);
  TestPropertyGuids;
  Co := CoInitializeEx(nil, COINIT_APARTMENTTHREADED);
  Check(Succeeded(Co), 'CoInitializeEx');
  TestAnnotation(Wnd);
  TestSafeArray;
  CoUninitialize;
  Check(DestroyWindow(Wnd), 'DestroyWindow');
  Done;
end.
