// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ UIAutomation - declarations of the UI Automation provider side of the operating system (uiautomationcore.dll) and
  of direct annotation (the annotation object of oleacc.dll, reached through CoCreateInstance): the provider and
  control pattern interfaces a framework implements, the rectangle and point records, the option, direction, state
  and structure-change values, the property, control pattern, control type and event identifiers the framework
  uses, the runtime-id and object-id constants, the result codes, the eight uiautomationcore imports, and the
  annotation interface with its class identifier and four property GUIDs (ui-framework phase8/ui-automation.md
  section 3, rules RTL-1110 to RTL-1114; task R-243). Uses Windows, ActiveX.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the interface pages
  IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, IInvokeProvider,
  IToggleProvider, IValueProvider, IGridProvider, IGridItemProvider, ITableProvider, ITableItemProvider,
  ISelectionProvider and ISelectionItemProvider with their method pages (uiautomationcore.h), the enumeration pages
  ProviderOptions, NavigateDirection, ToggleState, RowOrColumnMajor and StructureChangeType, the structure pages
  UiaRect and UiaPoint, the function pages UiaReturnRawElementProvider, UiaHostProviderFromHwnd,
  UiaRaiseAutomationEvent, UiaRaiseAutomationPropertyChangedEvent, UiaRaiseStructureChangedEvent,
  UiaClientsAreListening, UiaDisconnectProvider and UiaGetReservedNotSupportedValue (uiautomationcoreapi.h), the
  pages "Property IDs", "Control Pattern Identifiers", "Control Type Identifiers", "Event Identifiers" and "Error
  Codes" of UI Automation, the IAccPropServices interface page with its methods SetHwndProp, SetHwndPropStr and
  ClearHwndProps (oleacc.h), "Using Direct Annotation", "Object Identifiers", WM_GETOBJECT and
  IRawElementProviderFragment::GetRuntimeId.
  The pages list the methods of an interface alphabetically and give no number for some constants. The order of
  the methods (the binary contract), the interface identifiers, the class identifier of the annotation object and
  the values of UiaRootObjectId, UiaAppendRuntimeId and CHILDID_SELF are those of the managed interop declarations
  of the .NET desktop runtime 10.0 (MIT licence): the generated provider interfaces with their method tables in
  slot order (assembly System.Windows.Forms.Primitives, namespace Windows.Win32.UI.Accessibility) and the imported
  type library of the annotation component (assembly Accessibility, interface IAccPropServices and class
  CAccPropServices) - facts only, no text; the order was checked against the platform headers uiautomationcore.h
  and oleacc.h. The values of OBJID_CLIENT and of the four property GUIDs are the facts of the platform headers
  winuser.h and uiautomationcoreapi.h; the test Win_UiaSmoke proves the GUIDs against the system (the system's
  lookup of a GUID returns the property identifier of the reference page).
  Every interface derives from IUnknown (as on the reference pages) and is declared with all its methods, except
  IAccPropServices, which stops after ClearHwndProps (the program only calls that object). A parameter whose
  interface type is not declared here is a Pointer, with the reference type named in a comment; enumeration
  parameters and results are Integer; a SAFEARRAY is a PSafeArray of ActiveX. A by-value GUID or VARIANT parameter
  of the reference is a value parameter here (the caller passes a copy, as the platform convention does).
  The system passes the out parameters of a provider method uninitialised: an implementation sets an interface
  result with PPointer(@pRetVal)^ := nil before assigning it (a plain
  assignment would release what the memory holds), and fills a VARIANT result completely. }
unit UIAutomation;

interface

uses Windows, ActiveX;

const
  uiautomationcore = 'uiautomationcore.dll';

  { ProviderOptions }
  ProviderOptions_ClientSideProvider = $1;
  ProviderOptions_ServerSideProvider = $2;
  ProviderOptions_NonClientAreaProvider = $4;
  ProviderOptions_OverrideProvider = $8;
  ProviderOptions_UseComThreading = $20;

  { NavigateDirection }
  NavigateDirection_Parent = 0;
  NavigateDirection_NextSibling = 1;
  NavigateDirection_PreviousSibling = 2;
  NavigateDirection_FirstChild = 3;
  NavigateDirection_LastChild = 4;

  { ToggleState }
  ToggleState_Off = 0;
  ToggleState_On = 1;
  ToggleState_Indeterminate = 2;

  { RowOrColumnMajor }
  RowOrColumnMajor_RowMajor = 0;
  RowOrColumnMajor_ColumnMajor = 1;
  RowOrColumnMajor_Indeterminate = 2;

  { StructureChangeType }
  StructureChangeType_ChildAdded = 0;
  StructureChangeType_ChildRemoved = 1;
  StructureChangeType_ChildrenInvalidated = 2;
  StructureChangeType_ChildrenBulkAdded = 3;
  StructureChangeType_ChildrenBulkRemoved = 4;
  StructureChangeType_ChildrenReordered = 5;

  { property identifiers (PROPERTYID) }
  UIA_ControlTypePropertyId = 30003;
  UIA_LocalizedControlTypePropertyId = 30004;
  UIA_NamePropertyId = 30005;
  UIA_HasKeyboardFocusPropertyId = 30008;
  UIA_IsKeyboardFocusablePropertyId = 30009;
  UIA_IsEnabledPropertyId = 30010;
  UIA_AutomationIdPropertyId = 30011;
  UIA_ClassNamePropertyId = 30012;
  UIA_HelpTextPropertyId = 30013;
  UIA_IsControlElementPropertyId = 30016;
  UIA_IsContentElementPropertyId = 30017;
  UIA_IsOffscreenPropertyId = 30022;
  UIA_FrameworkIdPropertyId = 30024;
  UIA_ValueValuePropertyId = 30045;
  UIA_ToggleToggleStatePropertyId = 30086;

  { control pattern identifiers (PATTERNID) }
  UIA_InvokePatternId = 10000;
  UIA_SelectionPatternId = 10001;
  UIA_ValuePatternId = 10002;
  UIA_RangeValuePatternId = 10003;
  UIA_ExpandCollapsePatternId = 10005;
  UIA_GridPatternId = 10006;
  UIA_GridItemPatternId = 10007;
  UIA_SelectionItemPatternId = 10010;
  UIA_TablePatternId = 10012;
  UIA_TableItemPatternId = 10013;
  UIA_TextPatternId = 10014;
  UIA_TogglePatternId = 10015;

  { control type identifiers (CONTROLTYPEID) }
  UIA_ButtonControlTypeId = 50000;
  UIA_CheckBoxControlTypeId = 50002;
  UIA_ComboBoxControlTypeId = 50003;
  UIA_EditControlTypeId = 50004;
  UIA_ImageControlTypeId = 50006;
  UIA_ListItemControlTypeId = 50007;
  UIA_ListControlTypeId = 50008;
  UIA_MenuItemControlTypeId = 50011;
  UIA_ProgressBarControlTypeId = 50012;
  UIA_RadioButtonControlTypeId = 50013;
  UIA_ScrollBarControlTypeId = 50014;
  UIA_SliderControlTypeId = 50015;
  UIA_StatusBarControlTypeId = 50017;
  UIA_TabControlTypeId = 50018;
  UIA_TabItemControlTypeId = 50019;
  UIA_TextControlTypeId = 50020;
  UIA_ToolBarControlTypeId = 50021;
  UIA_TreeControlTypeId = 50023;
  UIA_TreeItemControlTypeId = 50024;
  UIA_CustomControlTypeId = 50025;
  UIA_GroupControlTypeId = 50026;
  UIA_ThumbControlTypeId = 50027;
  UIA_DataGridControlTypeId = 50028;
  UIA_DataItemControlTypeId = 50029;
  UIA_WindowControlTypeId = 50032;
  UIA_PaneControlTypeId = 50033;
  UIA_HeaderItemControlTypeId = 50035;
  UIA_TableControlTypeId = 50036;
  UIA_SeparatorControlTypeId = 50038;

  { event identifiers (EVENTID) }
  UIA_StructureChangedEventId = 20002;
  UIA_AutomationPropertyChangedEventId = 20004;
  UIA_AutomationFocusChangedEventId = 20005;
  UIA_Invoke_InvokedEventId = 20009;
  UIA_SelectionItem_ElementSelectedEventId = 20012;

  { WM_GETOBJECT: the object id of a request for a UI Automation provider; the first element of the runtime id of
    an element that is not a window (followed by a value unique within its fragment) }
  UiaRootObjectId = -25;
  UiaAppendRuntimeId = 3;

  { result codes of UI Automation }
  UIA_E_ELEMENTNOTENABLED = HResult($80040200);
  UIA_E_ELEMENTNOTAVAILABLE = HResult($80040201);
  UIA_E_INVALIDOPERATION = HResult($80131509);

  { direct annotation: the client object of a window (the header's (LONG) 0xFFFFFFFC; pass DWORD(OBJID_CLIENT) to
    an idObject parameter) and the element itself rather than a child }
  OBJID_CLIENT = -4;
  CHILDID_SELF = 0;

  { the annotation object (CoCreateInstance) and its interface }
  CLSID_AccPropServices: TGUID = '{B5F8350B-0548-48B1-A6EE-88BD00B4A5E7}';
  IID_IAccPropServices: TGUID = '{6E26E776-04F0-495D-80E4-3330352E3169}';

  { UI Automation properties as annotation property GUIDs (MSAAPROPID) }
  Name_Property_GUID: TGUID = '{C3A6921B-4A99-44F1-BCA6-61187052C431}';
  HelpText_Property_GUID: TGUID = '{08555685-0977-45C7-A7A6-ABAF5684121A}';
  AutomationId_Property_GUID: TGUID = '{C82C0500-B60E-4310-A267-303C531F8EE5}';
  ControlType_Property_GUID: TGUID = '{CA774FEA-28AC-4BC2-94CA-ACEC6D6C10A3}';

type
  { UiaRect, 32 bytes: screen coordinates in device pixels; an empty rectangle is all zero }
  PUiaRect = ^TUiaRect;
  TUiaRect = record
    left: Double;
    top: Double;
    width: Double;
    height: Double;
  end;
  UiaRect = TUiaRect;

  { UiaPoint, 16 bytes }
  PUiaPoint = ^TUiaPoint;
  TUiaPoint = record
    x: Double;
    y: Double;
  end;
  UiaPoint = TUiaPoint;

  { the basic provider of an element: options, pattern objects, property values (an empty VARIANT = not supplied)
    and the host provider of a window }
  IRawElementProviderSimple = interface(IUnknown)
    ['{D6DD68D1-86FD-4332-8666-9ABEDEA2D24C}']
    function get_ProviderOptions(out pRetVal: Integer): HResult; stdcall;
    function GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult; stdcall;
    function GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult; stdcall;
    function get_HostRawElementProvider(out pRetVal: IRawElementProviderSimple): HResult; stdcall;
  end;

  IRawElementProviderFragmentRoot = interface;

  { an element of a fragment (a tree of elements inside one window) }
  IRawElementProviderFragment = interface(IUnknown)
    ['{F7063DA8-8359-439C-9297-BBC5299A7D87}']
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; stdcall;
    function GetRuntimeId(out pRetVal: PSafeArray): HResult; stdcall;
    function get_BoundingRectangle(out pRetVal: TUiaRect): HResult; stdcall;
    function GetEmbeddedFragmentRoots(out pRetVal: PSafeArray): HResult; stdcall;
    function SetFocus: HResult; stdcall;
    function get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult; stdcall;
  end;

  { the root element of a fragment: hit testing (screen coordinates) and the focused element }
  IRawElementProviderFragmentRoot = interface(IUnknown)
    ['{620CE2A5-AB8F-40A9-86CB-DE3C75599B58}']
    function ElementProviderFromPoint(x, y: Double; out pRetVal: IRawElementProviderFragment): HResult; stdcall;
    function GetFocus(out pRetVal: IRawElementProviderFragment): HResult; stdcall;
  end;

  IInvokeProvider = interface(IUnknown)
    ['{54FCB24B-E18E-47A2-B4D3-ECCBE77599A2}']
    function Invoke: HResult; stdcall;
  end;

  { pRetVal of get_ToggleState is a ToggleState_* value }
  IToggleProvider = interface(IUnknown)
    ['{56D00BD0-C4F4-433C-A836-1A52A57E0892}']
    function Toggle: HResult; stdcall;
    function get_ToggleState(out pRetVal: Integer): HResult; stdcall;
  end;

  { get_Value returns an OLE string the caller frees }
  IValueProvider = interface(IUnknown)
    ['{C7935180-6FB3-4201-B174-7DF73ADBF64A}']
    function SetValue(val: PWideChar): HResult; stdcall;
    function get_Value(out pRetVal: TBStr): HResult; stdcall;
    function get_IsReadOnly(out pRetVal: BOOL): HResult; stdcall;
  end;

  IGridProvider = interface(IUnknown)
    ['{B17D6187-0907-464B-A168-0EF17A1572B1}']
    function GetItem(row, column: Integer; out pRetVal: IRawElementProviderSimple): HResult; stdcall;
    function get_RowCount(out pRetVal: Integer): HResult; stdcall;
    function get_ColumnCount(out pRetVal: Integer): HResult; stdcall;
  end;

  IGridItemProvider = interface(IUnknown)
    ['{D02541F1-FB81-4D64-AE32-F520F8A6DBD1}']
    function get_Row(out pRetVal: Integer): HResult; stdcall;
    function get_Column(out pRetVal: Integer): HResult; stdcall;
    function get_RowSpan(out pRetVal: Integer): HResult; stdcall;
    function get_ColumnSpan(out pRetVal: Integer): HResult; stdcall;
    function get_ContainingGrid(out pRetVal: IRawElementProviderSimple): HResult; stdcall;
  end;

  { the header arrays are safe arrays of IRawElementProviderSimple references (VT_UNKNOWN); pRetVal of
    get_RowOrColumnMajor is a RowOrColumnMajor_* value }
  ITableProvider = interface(IUnknown)
    ['{9C860395-97B3-490A-B52A-858CC22AF166}']
    function GetRowHeaders(out pRetVal: PSafeArray): HResult; stdcall;
    function GetColumnHeaders(out pRetVal: PSafeArray): HResult; stdcall;
    function get_RowOrColumnMajor(out pRetVal: Integer): HResult; stdcall;
  end;

  ITableItemProvider = interface(IUnknown)
    ['{B9734FA6-771F-4D78-9C90-2517999349CD}']
    function GetRowHeaderItems(out pRetVal: PSafeArray): HResult; stdcall;
    function GetColumnHeaderItems(out pRetVal: PSafeArray): HResult; stdcall;
  end;

  ISelectionProvider = interface(IUnknown)
    ['{FB8B03AF-3BDF-48D4-BD36-1A65793BE168}']
    function GetSelection(out pRetVal: PSafeArray): HResult; stdcall;
    function get_CanSelectMultiple(out pRetVal: BOOL): HResult; stdcall;
    function get_IsSelectionRequired(out pRetVal: BOOL): HResult; stdcall;
  end;

  ISelectionItemProvider = interface(IUnknown)
    ['{2ACAD808-B2D4-452D-A407-91FF1AD167B2}']
    function Select: HResult; stdcall;
    function AddToSelection: HResult; stdcall;
    function RemoveFromSelection: HResult; stdcall;
    function get_IsSelected(out pRetVal: BOOL): HResult; stdcall;
    function get_SelectionContainer(out pRetVal: IRawElementProviderSimple): HResult; stdcall;
  end;

  { direct annotation of an accessible element: declared up to ClearHwndProps. idProp is a property GUID
    (MSAAPROPID); paProps points to cProps GUIDs; annoScope is an AnnoScope value }
  IAccPropServices = interface(IUnknown)
    ['{6E26E776-04F0-495D-80E4-3330352E3169}']
    function SetPropValue(pIDString: PByte; dwIDStringLen: DWORD; idProp: TGUID; vValue: TVariantArg): HResult;
      stdcall;
    function SetPropServer(pIDString: PByte; dwIDStringLen: DWORD; paProps: PGUID; cProps: Integer;
      pServer: Pointer { IAccPropServer }; annoScope: Integer): HResult; stdcall;
    function ClearProps(pIDString: PByte; dwIDStringLen: DWORD; paProps: PGUID; cProps: Integer): HResult; stdcall;
    function SetHwndProp(hwnd: HWND; idObject, idChild: DWORD; idProp: TGUID; vValue: TVariantArg): HResult; stdcall;
    function SetHwndPropStr(hwnd: HWND; idObject, idChild: DWORD; idProp: TGUID; str: PWideChar): HResult; stdcall;
    function SetHwndPropServer(hwnd: HWND; idObject, idChild: DWORD; paProps: PGUID; cProps: Integer;
      pServer: Pointer { IAccPropServer }; annoScope: Integer): HResult; stdcall;
    function ClearHwndProps(hwnd: HWND; idObject, idChild: DWORD; paProps: PGUID; cProps: Integer): HResult; stdcall;
  end;

{ uiautomationcore: WM_GETOBJECT answers, host providers, events, disconnection }
function UiaReturnRawElementProvider(hwnd: HWND; wParam: WPARAM; lParam: LPARAM;
  const el: IRawElementProviderSimple): LRESULT; stdcall; external uiautomationcore name 'UiaReturnRawElementProvider';
function UiaHostProviderFromHwnd(hwnd: HWND; out ppProvider: IRawElementProviderSimple): HResult; stdcall;
  external uiautomationcore name 'UiaHostProviderFromHwnd';
function UiaRaiseAutomationEvent(const pProvider: IRawElementProviderSimple; id: Integer): HResult; stdcall;
  external uiautomationcore name 'UiaRaiseAutomationEvent';
function UiaRaiseAutomationPropertyChangedEvent(const pProvider: IRawElementProviderSimple; id: Integer;
  oldValue, newValue: TVariantArg): HResult; stdcall; external uiautomationcore name 'UiaRaiseAutomationPropertyChangedEvent';
function UiaRaiseStructureChangedEvent(const pProvider: IRawElementProviderSimple; structureChangeType: Integer;
  pRuntimeId: PInteger; cRuntimeIdLen: Integer): HResult; stdcall;
  external uiautomationcore name 'UiaRaiseStructureChangedEvent';
function UiaClientsAreListening: BOOL; stdcall; external uiautomationcore name 'UiaClientsAreListening';
function UiaDisconnectProvider(const pProvider: IRawElementProviderSimple): HResult; stdcall;
  external uiautomationcore name 'UiaDisconnectProvider';
function UiaGetReservedNotSupportedValue(out punkNotSupportedValue: IUnknown): HResult; stdcall;
  external uiautomationcore name 'UiaGetReservedNotSupportedValue';

implementation

end.
