// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Accessibility - UI Automation for framework controls (phase8/ui-automation.md sections 4, 5 and 8, UIF-1410 to
  UIF-1427, UIF-1440, UIF-1444 to UIF-1446): the names, help texts, automation ids and control types of controls;
  server-side providers for framework-drawn windows (fragment roots over the system's host provider) and for graphic
  controls (fragments of their windowed parent), one per control, created when a client first asks and disconnected
  when the control or its window goes; the provider registry (the provider class of a control class); direct
  annotation of native controls (windows of system classes keep the system's providers and get only what the system
  cannot know); the events of framework-drawn controls (name, IsEnabled and integer property changes, automation
  events, children invalidated), raised only when a client listens. Controls does not use this unit: it reaches it
  through AccessibilityMessageHook and AccessibilityChangeHook, assigned here in the initialization and cleared in the
  finalization. Forms uses this unit, so every program with forms has providers; StdCtrls, ExtCtrls, ComCtrls, Grids
  and Forms register the provider classes of their controls. Nothing is created and no COM call is made until a client
  sends WM_GETOBJECT. }
unit Accessibility;

interface

uses
  Windows, Messages, Types, ActiveX, UIAutomation, Classes, SysUtils, Controls;

type
  { UIF-1419: not an element of the tree; a control element only; a control and content element }
  TAccessibleElementKind = (ekNone, ekControl, ekContent);

  TAutomationProvider = class;
  TAutomationProviderClass = class of TAutomationProvider;

  { the provider of one control (UIF-1418, UIF-1425): the class methods are the rules of the control's class and are
    asked without a provider (also for native controls, which never get one) }
  TAutomationProvider = class(TInterfacedObject, IRawElementProviderSimple, IRawElementProviderFragment)
  private
    FControl: TControl;
    FKeep: IRawElementProviderSimple;                        { the control's reference (UIF-1425) }
    FReturned: Boolean;                                      { returned through UiaReturnRawElementProvider }
    FRaised: Boolean;                                        { an event was raised on it (the system keeps it) }
    FNumber: Integer;                                        { unique in the process (runtime ids) }
    FLastName: string;                                       { the name last reported (UIF-1445) }
  protected
    { False (and Res = UIA_E_ELEMENTNOTAVAILABLE) when the control is gone (UIF-1425, UIF-1427) }
    function Alive(out Res: HResult): Boolean;
    function IsOffscreen: Boolean; virtual;
    { UIF-1426: disconnects the providers of the elements of the control that are no controls (grid cells, U-157);
      called while this provider still answers for its control, before the control lets it go }
    procedure DisconnectFragments; virtual;
    { the element is gone: every later call answers UIA_E_ELEMENTNOTAVAILABLE (U-157: a grid cell whose grid let
      its provider go) }
    procedure ForgetControl;
  public
    constructor Create(AControl: TControl); virtual;
    destructor Destroy; override;
    { UIF-1411 rule (2): the control's own text before the mnemonic rule; '' = none }
    class function OwnText(Control: TControl): string; virtual;
    { UIF-1417: the control type of the class (AccessibleRole = arDefault) }
    class function DefaultControlType(Control: TControl): Integer; virtual;
    { UIF-1411 rule (3): the control a label of this class names; nil = not a label }
    class function LabelTarget(Control: TControl): TControl; virtual;
    { UIF-1419, before the Visible rule }
    class function ElementKind(Control: TControl): TAccessibleElementKind; virtual;
    property Control: TControl read FControl;
    property Number: Integer read FNumber;
    { IRawElementProviderSimple }
    function get_ProviderOptions(out pRetVal: Integer): HResult; stdcall;
    function GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult; virtual; stdcall;
    function GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult; virtual; stdcall;
    function get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult; virtual; stdcall;
    { IRawElementProviderFragment }
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; virtual; stdcall;
    function GetRuntimeId(out pRetVal: PSafeArray): HResult; virtual; stdcall;
    function get_BoundingRectangle(out pRetVal: TUiaRect): HResult; virtual; stdcall;
    function GetEmbeddedFragmentRoots(out pRetVal: PSafeArray): HResult; stdcall;
    function SetFocus: HResult; virtual; stdcall;
    function get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult; virtual; stdcall;
  end;

  { UIF-1423: a framework-drawn window, the root of the fragment of its graphic children }
  TWindowAutomationProvider = class(TAutomationProvider, IRawElementProviderFragmentRoot)
  public
    function get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult; override; stdcall;
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; override; stdcall;
    function GetRuntimeId(out pRetVal: PSafeArray): HResult; override; stdcall;
    function get_BoundingRectangle(out pRetVal: TUiaRect): HResult; override; stdcall;
    function SetFocus: HResult; override; stdcall;
    function get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult; override; stdcall;
    { IRawElementProviderFragmentRoot }
    function ElementProviderFromPoint(x, y: Double; out pRetVal: IRawElementProviderFragment): HResult; virtual;
      stdcall;
    function GetFocus(out pRetVal: IRawElementProviderFragment): HResult; virtual; stdcall;
  end;

  { UIF-1424: a graphic control, a fragment of its windowed parent }
  TGraphicAutomationProvider = class(TAutomationProvider)
  protected
    function IsOffscreen: Boolean; override;
  public
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; override; stdcall;
    function GetRuntimeId(out pRetVal: PSafeArray): HResult; override; stdcall;
    function get_BoundingRectangle(out pRetVal: TUiaRect): HResult; override; stdcall;
    function SetFocus: HResult; override; stdcall;
    function get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult; override; stdcall;
  end;

{ UIF-1421: the provider class of ControlClass and of its descendants without a registration of their own }
procedure RegisterAutomationProvider(ControlClass: TControlClass; ProviderClass: TAutomationProviderClass);
{ UIF-1421: the class registered for the nearest registered ancestor of the control's class }
function AutomationProviderClassOf(Control: TControl): TAutomationProviderClass;
{ UIF-1420: the provider of a control, created on first request (for tests); nil for a native control }
function GetAutomationProvider(Control: TControl): IRawElementProviderSimple;
{ UIF-1425: the provider objects alive in the process (tests) }
function AutomationProviderCount: Integer;

{ UIF-1411, UIF-1412: the name of a control }
function AccessibleNameOf(Control: TControl): string;
{ UIF-1413 }
function AccessibleHelpTextOf(Control: TControl): string;
{ UIF-1417: the control-type id of a control (an AccessibleRole override or the class default) }
function AccessibleControlTypeOf(Control: TControl): Integer;
{ UIF-1419 }
function AccessibleElementKindOf(Control: TControl): TAccessibleElementKind;
{ UIF-1412: a single '&' is removed, '&&' becomes '&' }
function StripMnemonic(const S: string): string;
{ the Caption (window text) of a control, for the provider classes of other units }
function ControlCaption(Control: TControl): string;
{ UIF-1418: IsEnabled (the control and every parent enabled) }
function AccessibleEnabledOf(Control: TControl): Boolean;
{ UIF-1440: the control has a provider and a UI Automation client listens (UiaClientsAreListening) }
function AutomationEventsWanted(Control: TControl): Boolean;
{ UIF-1444: raises an automation event (an EVENTID) on the control's provider when AutomationEventsWanted }
procedure RaiseAutomationEventOf(Control: TControl; EventId: Integer);
{ UIF-1445: raises the property-changed event of an integer property (a PROPERTYID; old and new value) on the
  control's provider when AutomationEventsWanted }
procedure RaiseIntPropertyChanged(Control: TControl; PropertyId, OldValue, NewValue: Integer);
{ UIF-1444 (U-157): raises an automation event on Fragment, an element of the control that is not a control (a grid
  cell), when AutomationEventsWanted(Control) }
procedure RaiseFragmentEvent(Control: TControl; const Fragment: IRawElementProviderSimple; EventId: Integer);
{ UIF-1445 (U-157): raises the property-changed event of a string property on such an element (old and new value)
  when AutomationEventsWanted(Control) }
procedure RaiseFragmentStrPropertyChanged(Control: TControl; const Fragment: IRawElementProviderSimple;
  PropertyId: Integer; const OldValue, NewValue: string);

implementation

type
  TControlAccess = class(TControl);

  TRegistration = record
    ControlClass: TControlClass;
    ProviderClass: TAutomationProviderClass;
  end;

const
  FrameworkId = 'FreexComp';
  { the first element of the runtime id of a window element (the second is the window handle): the value the
    system's host provider reports for a window, used for the structure-changed event of UIF-1446 }
  HwndRuntimeIdPrefix = 42;
  { UIF-1417: the control-type id of each role (the identifier of the same name on the reference page) }
  RoleControlType: array[TAccessibleRole] of Integer = (0, UIA_ButtonControlTypeId, UIA_CheckBoxControlTypeId,
    UIA_ComboBoxControlTypeId, UIA_CustomControlTypeId, UIA_DataGridControlTypeId, UIA_DataItemControlTypeId,
    UIA_EditControlTypeId, UIA_GroupControlTypeId, UIA_HeaderItemControlTypeId, UIA_ImageControlTypeId,
    UIA_ListControlTypeId, UIA_ListItemControlTypeId, UIA_MenuItemControlTypeId, UIA_PaneControlTypeId,
    UIA_ProgressBarControlTypeId, UIA_RadioButtonControlTypeId, UIA_ScrollBarControlTypeId,
    UIA_SeparatorControlTypeId, UIA_SliderControlTypeId, UIA_StatusBarControlTypeId, UIA_TabControlTypeId,
    UIA_TabItemControlTypeId, UIA_TableControlTypeId, UIA_TextControlTypeId, UIA_ThumbControlTypeId,
    UIA_ToolBarControlTypeId, UIA_TreeControlTypeId, UIA_TreeItemControlTypeId, UIA_WindowControlTypeId);

var
  Registrations: array of TRegistration;
  LiveProviders: Integer;
  NextNumber: Integer;
  AnnotatedWindows: Integer;

{ ---------------------------------------------------------------- registry (UIF-1421) }

procedure RegisterAutomationProvider(ControlClass: TControlClass; ProviderClass: TAutomationProviderClass);
var
  I, N: Integer;
begin
  N := Length(Registrations);
  for I := 0 to N - 1 do
    if Registrations[I].ControlClass = ControlClass then
    begin
      Registrations[I].ProviderClass := ProviderClass;
      Exit;
    end;
  SetLength(Registrations, N + 1);
  Registrations[N].ControlClass := ControlClass;
  Registrations[N].ProviderClass := ProviderClass;
end;

function AutomationProviderClassOf(Control: TControl): TAutomationProviderClass;
var
  C: TClass;
  I: Integer;
begin
  C := Control.ClassType;
  while C <> nil do
  begin
    for I := 0 to Length(Registrations) - 1 do
      if Registrations[I].ControlClass = C then
      begin
        Result := Registrations[I].ProviderClass;
        Exit;
      end;
    C := C.ClassParent;
  end;
  if Control is TWinControl then
    Result := TWindowAutomationProvider
  else
    Result := TGraphicAutomationProvider;
end;

{ ---------------------------------------------------------------- names and kinds (UIF-1411...1419) }

function StripMnemonic(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  I := 1;
  while I <= Length(S) do
  begin
    if S[I] = '&' then
    begin
      if (I < Length(S)) and (S[I + 1] = '&') then
      begin
        Result := Result + '&';
        Inc(I);
      end;
    end
    else
      Result := Result + S[I];
    Inc(I);
  end;
end;

function ControlCaption(Control: TControl): string;
begin
  Result := TControlAccess(Control).Caption;
end;

{ the top of the parent chain (the form) }
function RootOf(Control: TControl): TControl;
begin
  Result := Control;
  while Result.Parent <> nil do
    Result := Result.Parent;
end;

{ rule (3): the first label of the form in creation order (owned components, depth first) that names Control }
function FindLabel(Owner: TComponent; Control: TControl; out Text: string): Boolean;
var
  I: Integer;
  C: TComponent;
begin
  Result := False;
  for I := 0 to Owner.ComponentCount - 1 do
  begin
    C := Owner.Components[I];
    if (C is TControl) and (C <> Control)
      and (AutomationProviderClassOf(TControl(C)).LabelTarget(TControl(C)) = Control) then
    begin
      Text := StripMnemonic(AutomationProviderClassOf(TControl(C)).OwnText(TControl(C)));
      Result := True;
      Exit;
    end;
    if (C.ComponentCount > 0) and FindLabel(C, Control, Text) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function LabelNameOf(Control: TControl): string;
var
  Root: TControl;
begin
  Result := '';
  Root := RootOf(Control);
  if not FindLabel(Root, Control, Result) and (Root.Owner <> nil) and (Root.Owner <> Root) then
    FindLabel(Root.Owner, Control, Result);
end;

{ UIF-1411 with the rule that gave the name: 1, 2, 3 or 0 (none) }
function NameAndRule(Control: TControl; out Rule: Integer): string;
begin
  Rule := 1;
  Result := Control.AccessibleName;
  if Result <> '' then
    Exit;
  Rule := 2;
  Result := StripMnemonic(AutomationProviderClassOf(Control).OwnText(Control));
  if Result <> '' then
    Exit;
  Rule := 3;
  Result := LabelNameOf(Control);
  if Result = '' then
    Rule := 0;
end;

function AccessibleNameOf(Control: TControl): string;
var
  Rule: Integer;
begin
  Result := NameAndRule(Control, Rule);
end;

function AccessibleHelpTextOf(Control: TControl): string;
begin
  Result := Control.AccessibleDescription;
  if Result = '' then
    Result := GetShortHint(Control.Hint);
end;

function AccessibleControlTypeOf(Control: TControl): Integer;
begin
  if Control.AccessibleRole <> arDefault then
    Result := RoleControlType[Control.AccessibleRole]
  else
    Result := AutomationProviderClassOf(Control).DefaultControlType(Control);
end;

function AccessibleElementKindOf(Control: TControl): TAccessibleElementKind;
begin
  if not Control.Visible then
    Result := ekNone
  else
    Result := AutomationProviderClassOf(Control).ElementKind(Control);
end;

function EnabledChain(Control: TControl): Boolean;
begin
  Result := True;
  while Control <> nil do
  begin
    if not Control.Enabled then
    begin
      Result := False;
      Exit;
    end;
    Control := Control.Parent;
  end;
end;

function AccessibleEnabledOf(Control: TControl): Boolean;
begin
  Result := EnabledChain(Control);
end;

function IsGraphicElement(C: TControl): Boolean;
begin
  Result := not (C is TWinControl) and (AccessibleElementKindOf(C) <> ekNone);
end;

{ the next graphic element of Parent after (Step 1) or before (Step -1) index From }
function GraphicElement(Parent: TWinControl; From, Step: Integer): TControl;
var
  I: Integer;
begin
  Result := nil;
  I := From + Step;
  while (I >= 0) and (I < Parent.ControlCount) do
  begin
    if IsGraphicElement(Parent.Controls[I]) then
    begin
      Result := Parent.Controls[I];
      Exit;
    end;
    Inc(I, Step);
  end;
end;

{ ---------------------------------------------------------------- providers of controls (UIF-1425) }

function IsNative(Control: TControl): Boolean;
begin
  Result := (Control is TWinControl) and TWinControl(Control).HasSystemWindowClass;
end;

function ProviderOf(Control: TControl): TAutomationProvider;
begin
  Result := TAutomationProvider(Control.AutomationObject);
  if (Result <> nil) or IsNative(Control) or (csDestroying in Control.ComponentState) then
    Exit;
  Result := AutomationProviderClassOf(Control).Create(Control);
  Result.FKeep := Result;
  Control.AutomationObject := Result;
end;

function GetAutomationProvider(Control: TControl): IRawElementProviderSimple;
var
  P: TAutomationProvider;
begin
  P := ProviderOf(Control);
  if P = nil then
    Result := nil
  else
    Result := P.FKeep;
end;

function AutomationProviderCount: Integer;
begin
  Result := LiveProviders;
end;

{ the provider of C as a fragment (nil when C has none) }
function FragmentOf(C: TControl): IRawElementProviderFragment;
var
  P: TAutomationProvider;
begin
  Result := nil;
  if C = nil then
    Exit;
  P := ProviderOf(C);
  if P <> nil then
    Result := P;
end;

{ UIF-1425, UIF-1426: the control lets its provider go. Disconnect: UiaDisconnectProvider first, while the provider
  still answers for its control (the system asks it for its identity; a provider that already answers
  UIA_E_ELEMENTNOTAVAILABLE is not disconnected and stays referenced by the clients' elements) }
procedure DetachProvider(Control: TControl; Disconnect: Boolean);
var
  P: TAutomationProvider;
  Keep: IRawElementProviderSimple;
begin
  P := TAutomationProvider(Control.AutomationObject);
  if P = nil then
    Exit;
  Keep := P.FKeep;
  P.DisconnectFragments;
  if Disconnect then
    UiaDisconnectProvider(Keep);
  Control.AutomationObject := nil;
  P.FControl := nil;
  P.FKeep := nil;
  Keep := nil;
end;

{ ---------------------------------------------------------------- string, integer, Boolean VARIANTs }

procedure PutStr(var V: TVariantArg; const S: string);
begin
  V.vt := VT_BSTR;
  V.bstrVal := SysAllocStringLen(PWideChar(S), Length(S));
end;

procedure PutInt(var V: TVariantArg; I: Integer);
begin
  V.vt := VT_I4;
  V.lVal := I;
end;

procedure PutBool(var V: TVariantArg; B: Boolean);
begin
  V.vt := VT_BOOL;
  if B then
    V.boolVal := -1
  else
    V.boolVal := 0;
end;

{ ---------------------------------------------------------------- TAutomationProvider }

constructor TAutomationProvider.Create(AControl: TControl);
begin
  inherited Create;
  FControl := AControl;
  Inc(NextNumber);
  FNumber := NextNumber;
  Inc(LiveProviders);
  FLastName := AccessibleNameOf(AControl);
end;

destructor TAutomationProvider.Destroy;
begin
  Dec(LiveProviders);
  inherited Destroy;
end;

function TAutomationProvider.Alive(out Res: HResult): Boolean;
begin
  Result := FControl <> nil;
  if Result then
    Res := S_OK
  else
    Res := UIA_E_ELEMENTNOTAVAILABLE;
end;

function TAutomationProvider.IsOffscreen: Boolean;
begin
  Result := not FControl.Showing;
end;

procedure TAutomationProvider.DisconnectFragments;
begin
end;

procedure TAutomationProvider.ForgetControl;
begin
  FControl := nil;
end;

class function TAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := '';
end;

class function TAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_CustomControlTypeId;
end;

class function TAutomationProvider.LabelTarget(Control: TControl): TControl;
begin
  Result := nil;
end;

class function TAutomationProvider.ElementKind(Control: TControl): TAccessibleElementKind;
begin
  Result := ekContent;
end;

function TAutomationProvider.get_ProviderOptions(out pRetVal: Integer): HResult;
begin
  pRetVal := ProviderOptions_ServerSideProvider;
  Result := S_OK;
end;

function TAutomationProvider.GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Alive(Result);
end;

function TAutomationProvider.GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult;
var
  C: TControl;
  S: string;
  Kind: TAccessibleElementKind;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  if not Alive(Result) then
    Exit;
  C := FControl;
  try
    case propertyId of
      UIA_NamePropertyId:
        begin
          S := AccessibleNameOf(C);
          FLastName := S;
          PutStr(pRetVal, S);
        end;
      UIA_HelpTextPropertyId:
        begin
          S := AccessibleHelpTextOf(C);
          if S <> '' then
            PutStr(pRetVal, S);
        end;
      UIA_AutomationIdPropertyId:
        PutStr(pRetVal, C.Name);
      UIA_ClassNamePropertyId:
        PutStr(pRetVal, C.ClassName);
      UIA_FrameworkIdPropertyId:
        PutStr(pRetVal, FrameworkId);
      UIA_ControlTypePropertyId:
        PutInt(pRetVal, AccessibleControlTypeOf(C));
      UIA_LocalizedControlTypePropertyId:
        if (C.AccessibleRole = arCustom) and (C.AccessibleDescription <> '') then
          PutStr(pRetVal, C.AccessibleDescription);
      UIA_IsEnabledPropertyId:
        PutBool(pRetVal, EnabledChain(C));
      UIA_IsOffscreenPropertyId:
        PutBool(pRetVal, IsOffscreen);
      UIA_IsKeyboardFocusablePropertyId, UIA_HasKeyboardFocusPropertyId:
        if not (C is TWinControl) then
          PutBool(pRetVal, False);
      UIA_IsControlElementPropertyId:
        begin
          Kind := AccessibleElementKindOf(C);
          PutBool(pRetVal, Kind <> ekNone);
        end;
      UIA_IsContentElementPropertyId:
        begin
          Kind := AccessibleElementKindOf(C);
          PutBool(pRetVal, Kind = ekContent);
        end;
    end;
  except
    FillChar(pRetVal, SizeOf(pRetVal), 0);
    Result := E_FAIL;
  end;
end;

function TAutomationProvider.get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Alive(Result);
end;

function TAutomationProvider.Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Alive(Result);
end;

function TAutomationProvider.GetRuntimeId(out pRetVal: PSafeArray): HResult;
begin
  pRetVal := nil;
  Alive(Result);
end;

function TAutomationProvider.get_BoundingRectangle(out pRetVal: TUiaRect): HResult;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  Alive(Result);
end;

function TAutomationProvider.GetEmbeddedFragmentRoots(out pRetVal: PSafeArray): HResult;
begin
  pRetVal := nil;
  Alive(Result);
end;

function TAutomationProvider.SetFocus: HResult;
begin
  if Alive(Result) then
    Result := UIA_E_INVALIDOPERATION;
end;

function TAutomationProvider.get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Alive(Result);
end;

{ ---------------------------------------------------------------- TWindowAutomationProvider (UIF-1423) }

function TWindowAutomationProvider.get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  if TWinControl(FControl).HandleAllocated then
    Result := UiaHostProviderFromHwnd(TWinControl(FControl).Handle, pRetVal);
end;

function TWindowAutomationProvider.Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult;
var
  W: TWinControl;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  W := TWinControl(FControl);
  case direction of
    NavigateDirection_FirstChild:
      pRetVal := FragmentOf(GraphicElement(W, -1, 1));
    NavigateDirection_LastChild:
      pRetVal := FragmentOf(GraphicElement(W, W.ControlCount, -1));
  end;
end;

function TWindowAutomationProvider.GetRuntimeId(out pRetVal: PSafeArray): HResult;
begin
  pRetVal := nil;                                           { the host supplies the window's runtime id }
  Alive(Result);
end;

function TWindowAutomationProvider.get_BoundingRectangle(out pRetVal: TUiaRect): HResult;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);                   { the host supplies the window's rectangle }
  Alive(Result);
end;

function TWindowAutomationProvider.SetFocus: HResult;
begin
  if not Alive(Result) then
    Exit;
  if TWinControl(FControl).CanFocus then
    TWinControl(FControl).SetFocus
  else
    Result := UIA_E_INVALIDOPERATION;
end;

function TWindowAutomationProvider.get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  if Alive(Result) then
    pRetVal := Self;
end;

function TWindowAutomationProvider.ElementProviderFromPoint(x, y: Double;
  out pRetVal: IRawElementProviderFragment): HResult;
var
  W: TWinControl;
  P: TPoint;
  I: Integer;
  C: TControl;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  W := TWinControl(FControl);
  if W.HandleAllocated then
  begin
    P := Point(Round(x), Round(y));
    ScreenToClient(W.Handle, P);
    for I := W.ControlCount - 1 downto 0 do
    begin
      C := W.Controls[I];
      if IsGraphicElement(C) and PtInRect(C.BoundsRect, P) then
      begin
        pRetVal := FragmentOf(C);
        Exit;
      end;
    end;
  end;
  pRetVal := Self;
end;

function TWindowAutomationProvider.GetFocus(out pRetVal: IRawElementProviderFragment): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  Alive(Result);
end;

{ ---------------------------------------------------------------- TGraphicAutomationProvider (UIF-1424) }

function TGraphicAutomationProvider.IsOffscreen: Boolean;
var
  R, Client, X: TRect;
begin
  Result := not FControl.Showing;
  if not Result and (FControl.Parent <> nil) then
  begin
    R := FControl.BoundsRect;
    Client := FControl.Parent.ClientRect;
    Result := not IntersectRect(X, R, Client);
  end;
end;

function TGraphicAutomationProvider.Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult;
var
  Parent: TWinControl;
  Index: Integer;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  Parent := FControl.Parent;
  if Parent = nil then
    Exit;
  Index := 0;
  while (Index < Parent.ControlCount) and (Parent.Controls[Index] <> FControl) do
    Inc(Index);
  case direction of
    NavigateDirection_Parent:
      pRetVal := FragmentOf(Parent);
    NavigateDirection_NextSibling:
      pRetVal := FragmentOf(GraphicElement(Parent, Index, 1));
    NavigateDirection_PreviousSibling:
      pRetVal := FragmentOf(GraphicElement(Parent, Index, -1));
  end;
end;

function TGraphicAutomationProvider.GetRuntimeId(out pRetVal: PSafeArray): HResult;
var
  I, V: Integer;
begin
  pRetVal := nil;
  if not Alive(Result) then
    Exit;
  pRetVal := SafeArrayCreateVector(VT_I4, 0, 2);
  if pRetVal = nil then
  begin
    Result := E_OUTOFMEMORY;
    Exit;
  end;
  I := 0;
  V := UiaAppendRuntimeId;
  SafeArrayPutElement(pRetVal, @I, @V);
  I := 1;
  V := FNumber;
  SafeArrayPutElement(pRetVal, @I, @V);
end;

function TGraphicAutomationProvider.get_BoundingRectangle(out pRetVal: TUiaRect): HResult;
var
  P: TPoint;
  Parent: TWinControl;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  if not Alive(Result) then
    Exit;
  Parent := FControl.Parent;
  if (Parent = nil) or not Parent.HandleAllocated or not FControl.Showing then
    Exit;
  P := Point(FControl.Left, FControl.Top);
  ClientToScreen(Parent.Handle, P);
  pRetVal.left := P.X;
  pRetVal.top := P.Y;
  pRetVal.width := FControl.Width;
  pRetVal.height := FControl.Height;
end;

function TGraphicAutomationProvider.SetFocus: HResult;
begin
  if Alive(Result) then
    Result := UIA_E_INVALIDOPERATION;
end;

function TGraphicAutomationProvider.get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult;
var
  P: TAutomationProvider;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) or (FControl.Parent = nil) then
    Exit;
  P := ProviderOf(FControl.Parent);
  if P is TWindowAutomationProvider then
    pRetVal := TWindowAutomationProvider(P);
end;

{ ---------------------------------------------------------------- direct annotation (UIF-1414...1416) }

{ UIF-1414: the name annotated on a native control: only a name of rule (1) or (3) }
function AnnotationNameOf(Control: TControl): string;
var
  Rule: Integer;
begin
  Result := NameAndRule(Control, Rule);
  if Rule = 2 then
    Result := '';
end;

{ UIF-1416: one annotation call inside its own COM bracket; Clear: remove every annotation of the window }
procedure Annotate(Control: TWinControl; Clear: Boolean);
var
  Co: HResult;
  Services: IAccPropServices;
  H: HWND;
  V: TVariantArg;
  Props: array[0..3] of TGUID;

  procedure PutProp(const Prop: TGUID; const S: string);
  var
    One: TGUID;
  begin
    if S <> '' then
      Services.SetHwndPropStr(H, DWORD(OBJID_CLIENT), CHILDID_SELF, Prop, PWideChar(S))
    else
    begin
      One := Prop;
      Services.ClearHwndProps(H, DWORD(OBJID_CLIENT), CHILDID_SELF, @One, 1);
    end;
  end;

begin
  H := Control.Handle;
  Co := CoInitializeEx(nil, COINIT_APARTMENTTHREADED);
  try
    if Succeeded(CoCreateInstance(CLSID_AccPropServices, nil, CLSCTX_INPROC_SERVER, IID_IAccPropServices,
      Services)) then
    begin
      if Clear then
      begin
        Props[0] := Name_Property_GUID;
        Props[1] := HelpText_Property_GUID;
        Props[2] := AutomationId_Property_GUID;
        Props[3] := ControlType_Property_GUID;
        Services.ClearHwndProps(H, DWORD(OBJID_CLIENT), CHILDID_SELF, @Props[0], 4);
      end
      else
      begin
        PutProp(Name_Property_GUID, AnnotationNameOf(Control));
        PutProp(HelpText_Property_GUID, AccessibleHelpTextOf(Control));
        PutProp(AutomationId_Property_GUID, Control.Name);
        if Control.AccessibleRole <> arDefault then
        begin
          FillChar(V, SizeOf(V), 0);
          PutInt(V, RoleControlType[Control.AccessibleRole]);
          Services.SetHwndProp(H, DWORD(OBJID_CLIENT), CHILDID_SELF, ControlType_Property_GUID, V);
        end
        else
          PutProp(ControlType_Property_GUID, '');
      end;
      Services := nil;
    end;
  finally
    if (Co = S_OK) or (Co = S_FALSE) then
      CoUninitialize;
  end;
  if Clear then
  begin
    if Control.AccessibleAnnotated then
      Dec(AnnotatedWindows);
    Control.AccessibleAnnotated := False;
  end
  else if not Control.AccessibleAnnotated then
  begin
    Inc(AnnotatedWindows);
    Control.AccessibleAnnotated := True;
  end;
end;

{ ---------------------------------------------------------------- the hooks of Controls }

{ UIF-1414, UIF-1422 }
function MessageHook(Control: TWinControl; var Message: TMessage): Boolean;
var
  P: TAutomationProvider;
begin
  Result := False;
  if (csDestroying in Control.ComponentState) or (csDestroyingHandle in Control.ControlState)
    or (csCreating in Control.ControlState) or not Control.HandleAllocated then
    Exit;
  if Control.HasSystemWindowClass then
  begin
    if not Control.AccessibleAnnotated then
      Annotate(Control, False);
    Exit;
  end;
  if Integer(DWORD(Message.LParam)) <> UiaRootObjectId then
    Exit;
  P := ProviderOf(Control);
  if P = nil then
    Exit;
  P.FReturned := True;
  Message.Result := UiaReturnRawElementProvider(Control.Handle, Message.WParam, Message.LParam, P.FKeep);
  Result := True;
end;

{ UIF-1410, UIF-1445: the name property-changed event of a framework-drawn control (UIF-1440) }
procedure NameMayHaveChanged(Control: TControl);
var
  P: TAutomationProvider;
  OldName, NewName: string;
  OldValue, NewValue: TVariantArg;
begin
  P := TAutomationProvider(Control.AutomationObject);
  if P = nil then
    Exit;
  NewName := AccessibleNameOf(Control);
  if NewName = P.FLastName then
    Exit;
  OldName := P.FLastName;
  P.FLastName := NewName;
  if not UiaClientsAreListening then
    Exit;
  FillChar(OldValue, SizeOf(OldValue), 0);
  FillChar(NewValue, SizeOf(NewValue), 0);
  PutStr(OldValue, OldName);
  PutStr(NewValue, NewName);
  P.FRaised := True;
  UiaRaiseAutomationPropertyChangedEvent(P.FKeep, UIA_NamePropertyId, OldValue, NewValue);
  VariantClear(OldValue);
  VariantClear(NewValue);
end;

{ ---------------------------------------------------------------- events (UIF-1440, UIF-1444...1446) }

function AutomationEventsWanted(Control: TControl): Boolean;
begin
  Result := (Control <> nil) and (Control.AutomationObject <> nil) and UiaClientsAreListening;
end;

procedure RaiseAutomationEventOf(Control: TControl; EventId: Integer);
begin
  if AutomationEventsWanted(Control) then
  begin
    TAutomationProvider(Control.AutomationObject).FRaised := True;
    UiaRaiseAutomationEvent(TAutomationProvider(Control.AutomationObject).FKeep, EventId);
  end;
end;

procedure RaiseIntPropertyChanged(Control: TControl; PropertyId, OldValue, NewValue: Integer);
var
  OldV, NewV: TVariantArg;
begin
  if not AutomationEventsWanted(Control) then
    Exit;
  FillChar(OldV, SizeOf(OldV), 0);
  FillChar(NewV, SizeOf(NewV), 0);
  PutInt(OldV, OldValue);
  PutInt(NewV, NewValue);
  TAutomationProvider(Control.AutomationObject).FRaised := True;
  UiaRaiseAutomationPropertyChangedEvent(TAutomationProvider(Control.AutomationObject).FKeep, PropertyId, OldV, NewV);
end;

procedure RaiseFragmentEvent(Control: TControl; const Fragment: IRawElementProviderSimple; EventId: Integer);
begin
  if (Fragment = nil) or not AutomationEventsWanted(Control) then
    Exit;
  TAutomationProvider(Control.AutomationObject).FRaised := True;
  UiaRaiseAutomationEvent(Fragment, EventId);
end;

procedure RaiseFragmentStrPropertyChanged(Control: TControl; const Fragment: IRawElementProviderSimple;
  PropertyId: Integer; const OldValue, NewValue: string);
var
  OldV, NewV: TVariantArg;
begin
  if (Fragment = nil) or not AutomationEventsWanted(Control) then
    Exit;
  FillChar(OldV, SizeOf(OldV), 0);
  FillChar(NewV, SizeOf(NewV), 0);
  PutStr(OldV, OldValue);
  PutStr(NewV, NewValue);
  TAutomationProvider(Control.AutomationObject).FRaised := True;
  UiaRaiseAutomationPropertyChangedEvent(Fragment, PropertyId, OldV, NewV);
  VariantClear(OldV);
  VariantClear(NewV);
end;

{ UIF-1445: Enabled of a control with a provider changed; IsEnabled changes only when every parent is enabled }
procedure EnabledChanged(Control: TControl);
var
  OldV, NewV: TVariantArg;
begin
  if not AutomationEventsWanted(Control) or not EnabledChain(Control.Parent) then
    Exit;
  FillChar(OldV, SizeOf(OldV), 0);
  FillChar(NewV, SizeOf(NewV), 0);
  PutBool(OldV, not Control.Enabled);
  PutBool(NewV, Control.Enabled);
  TAutomationProvider(Control.AutomationObject).FRaised := True;
  UiaRaiseAutomationPropertyChangedEvent(TAutomationProvider(Control.AutomationObject).FKeep, UIA_IsEnabledPropertyId,
    OldV, NewV);
end;

{ UIF-1446: a graphic control was inserted into or removed from Parent: its children are invalidated (sender and
  runtime id: the parent's window element) }
procedure ChildrenChanged(Parent: TWinControl);
var
  Ids: array[0..1] of Integer;
begin
  if (Parent = nil) or (csDestroying in Parent.ComponentState) or not Parent.HandleAllocated
    or not AutomationEventsWanted(Parent) then
    Exit;
  Ids[0] := HwndRuntimeIdPrefix;
  Ids[1] := Integer(NativeUInt(Parent.Handle));
  TAutomationProvider(Parent.AutomationObject).FRaised := True;
  UiaRaiseStructureChangedEvent(TAutomationProvider(Parent.AutomationObject).FKeep,
    StructureChangeType_ChildrenInvalidated, @Ids[0], 2);
end;

{ UIF-1415: re-annotation of an annotated window; UIF-1445: name events }
procedure PropertiesChanged(Control: TControl);
var
  Target: TControl;
begin
  if (AnnotatedWindows = 0) and (LiveProviders = 0) then
    Exit;                                                    { no client has asked: nothing to do (UIF-1426) }
  Target := AutomationProviderClassOf(Control).LabelTarget(Control);
  if (Control is TWinControl) and TWinControl(Control).AccessibleAnnotated
    and TWinControl(Control).HandleAllocated then
    Annotate(TWinControl(Control), False);
  if (Target is TWinControl) and TWinControl(Target).AccessibleAnnotated and TWinControl(Target).HandleAllocated then
    Annotate(TWinControl(Target), False);
  NameMayHaveChanged(Control);
  if Target <> nil then
    NameMayHaveChanged(Target);
end;

{ UIF-1415, UIF-1426: the window is being destroyed (its handle is still valid). The fragments of its graphic
  controls, then those of its other elements (grid cells, U-157), are disconnected first, while their fragment root
  still answers: the system finds a fragment's window
  through its root, and a disconnect after the window is gone fails (E_FAIL) and leaves the provider held by the
  client's elements. A graphic control that outlives the window gets a new provider on the next request. The
  window's provider is disconnected when it was returned to a client or an event was raised on it (the system keeps
  a provider it raised an event for until it is disconnected; U-156). }
procedure WindowDestroying(Control: TWinControl);
var
  P: TAutomationProvider;
  I: Integer;
begin
  if Control.AccessibleAnnotated then
    Annotate(Control, True);
  P := TAutomationProvider(Control.AutomationObject);
  if P = nil then
    Exit;
  for I := 0 to Control.ControlCount - 1 do
    if not (Control.Controls[I] is TWinControl) then
      DetachProvider(Control.Controls[I], True);
  P.DisconnectFragments;
  if P.FReturned then
    UiaReturnRawElementProvider(Control.Handle, 0, 0, nil);
  DetachProvider(Control, P.FReturned or P.FRaised);
end;

procedure ChangeHook(Control: TControl; Change: TAccessibilityChange);
begin
  case Change of
    acProperties:
      PropertiesChanged(Control);
    acRemoved:
      begin
        DetachProvider(Control, True);
        ChildrenChanged(Control.Parent);
      end;
    acWindowDestroying:
      WindowDestroying(TWinControl(Control));
    acDestroying:
      DetachProvider(Control, not (Control is TWinControl)
        or TAutomationProvider(Control.AutomationObject).FRaised);
    acEnabled:
      EnabledChanged(Control);
    acInserted:
      ChildrenChanged(Control.Parent);
  end;
end;

initialization
  RegisterAutomationProvider(TWinControl, TWindowAutomationProvider);
  RegisterAutomationProvider(TGraphicControl, TGraphicAutomationProvider);
  AccessibilityMessageHook := MessageHook;
  AccessibilityChangeHook := ChangeHook;

finalization
  AccessibilityMessageHook := nil;
  AccessibilityChangeHook := nil;

end.
