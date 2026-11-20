{ Forms - the application object and its message loop (application-forms.md). }
unit Forms;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, CommCtrl, Graphics, Controls, UIConsts;

type
  TIdleEvent = procedure(Sender: TObject; var Done: Boolean) of object;
  TMessageEvent = procedure(var Msg: TMsg; var Handled: Boolean) of object;
  TExceptionEvent = procedure(Sender: TObject; E: Exception) of object;

  TScrollingWinControl = class;

  { controls-standard.md section 17: one scroll bar of a scrolling container; lengths are device pixels }
  TControlScrollBar = class(TPersistent)
  private
    FControl: TScrollingWinControl;
    FKind: TScrollBarKind;
    FIncrement: Integer;
    FRange: Integer;
    FTracking: Boolean;
    FVisible: Boolean;
    FPosition: Integer;
    FShown: Boolean;                                         { layout.md UIF-291: the bar is visible }
    FCalcRange: Integer;                                     { the range in effect after the last pass }
    procedure SetPosition(Value: Integer);
    procedure SetRange(Value: Integer);
    procedure SetVisible(Value: Boolean);
    function ClientExtent: Integer;
    function MaxPosition: Integer;
    procedure UpdateNative;
  public
    constructor Create(AControl: TScrollingWinControl; AKind: TScrollBarKind);
    procedure Assign(Source: TPersistent); override;
    { the bar is shown (UIF-291) }
    function IsScrollBarVisible: Boolean;
    property Kind: TScrollBarKind read FKind;
    { 0 ... range - client extent; the setter scrolls the children (UIF-293); always 0 in dump mode }
    property Position: Integer read FPosition write SetPosition;
    property ScrollPos: Integer read FPosition;
  published
    property Increment: Integer read FIncrement write FIncrement default 8;
    property Range: Integer read FRange write SetRange default 0;
    property Tracking: Boolean read FTracking write FTracking default False;
    property Visible: Boolean read FVisible write SetVisible default True;
  end;

  { layout.md section 11 }
  TScrollingWinControl = class(TWinControl)
  private
    FAutoScroll: Boolean;
    FHorzScrollBar: TControlScrollBar;
    FVertScrollBar: TControlScrollBar;
    procedure SetAutoScroll(Value: Boolean);
    procedure SetHorzScrollBar(Value: TControlScrollBar);
    procedure SetVertScrollBar(Value: TControlScrollBar);
    procedure ScrollMessage(Bar: TControlScrollBar; var Message: TWMScroll);
  protected
    { layout.md UIF-215: the device-pixel border on each side (TScrollBox bsSingle: 2) }
    function BorderInset: Integer; virtual;
    { the DPI of the form (96 without one or in dump mode) }
    function ScrollDPI: Integer;
    { layout.md UIF-215: B = MulDiv(17, DPI, 96) }
    function BarSize: Integer;
    { layout.md UIF-290, UIF-291, UIF-292: ranges, bar visibility, position clamp, native bars }
    procedure UpdateScrollBars; virtual;
    { the bars are decided before the pass, so the pass uses the resulting client size }
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
    function ScrollOffset: TPoint; override;
    function GetClientRect: TRect; override;
    procedure CreateWnd; override;
    { controls-standard.md UIF-750 }
    procedure WMHScroll(var Message: TWMScroll); override;
    procedure WMVScroll(var Message: TWMScroll); override;
    { core.md UIF-075: 3 x 16 logical px per 120 delta units, vertically }
    function DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean; override;
    { UIF-751: CM_FOCUSCHANGED for a descendant scrolls it into view }
    procedure WndProc(var Message: TMessage); override;
    { Increment, Range and the positions are scaled }
    procedure ChangeScale(M, D: Integer); override;
    property AutoScroll: Boolean read FAutoScroll write SetAutoScroll default False;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { layout.md UIF-218: offset by the scroll position }
    procedure AdjustClientRect(var Rect: TRect); override;
    { UIF-751 }
    procedure ScrollInView(AControl: TControl);
    property HorzScrollBar: TControlScrollBar read FHorzScrollBar write SetHorzScrollBar;
    property VertScrollBar: TControlScrollBar read FVertScrollBar write SetVertScrollBar;
  end;

  { controls-standard.md section 17 }
  TScrollBox = class(TScrollingWinControl)
  private
    FBorderStyle: TBorderStyle;
    procedure SetBorderStyle(Value: TBorderStyle);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    function BorderInset: Integer; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    property AutoScroll default True;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property Color default clBtnFace;
    property Font;
    property HorzScrollBar;
    property Padding;
    property ParentColor default True;
    property ParentFont;
    property TabOrder;
    property TabStop;
    property VertScrollBar;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

  { application-forms.md section 4 }
  TBorderIcon = (biSystemMenu, biMinimize, biMaximize, biHelp);
  TBorderIcons = set of TBorderIcon;
  TFormStyle = (fsNormal, fsMDIChild, fsMDIForm, fsStayOnTop);
  TPosition = (poDesigned, poDefault, poDefaultPosOnly, poDefaultSizeOnly, poScreenCenter, poDesktopCenter,
    poMainFormCenter, poOwnerFormCenter);
  TWindowState = (wsNormal, wsMinimized, wsMaximized);
  TCloseAction = (caNone, caHide, caFree, caMinimize);
  TFormStateItem = (fsCreating, fsVisible, fsShowing, fsModal, fsCreatedMDIChild, fsActivated);
  TFormState = set of TFormStateItem;
  TCloseEvent = procedure(Sender: TObject; var Action: TCloseAction) of object;
  TCloseQueryEvent = procedure(Sender: TObject; var CanClose: Boolean) of object;
  TMonitorDpiChangedEvent = procedure(Sender: TObject; OldDPI, NewDPI: Integer) of object;

  TMonitor = class;

  TCustomForm = class(TScrollingWinControl)
  private
    FActiveControl: TWinControl;
    FEnteredControl: TWinControl;                            { the control whose OnEnter chain fired (UIF-110 Old) }
    FFocusSerial: Integer;                                   { counts SetFocusedControl calls: OnExit moved focus }
    FFocusExiting: Boolean;
    FBorderIcons: TBorderIcons;
    FBorderStyle: TFormBorderStyle;
    FFormStyle: TFormStyle;
    FKeyPreview: Boolean;
    FPixelsPerInch: Integer;
    FDesignPixelsPerInch: Integer;
    FPosition: TPosition;
    FScaled: Boolean;
    FWindowState: TWindowState;
    FModalResult: TModalResult;
    FFormState: TFormState;
    FCanvas: TCanvas;
    FCreateReached: Boolean;                                 { UIF-442: OnDestroy only after OnCreate was reached }
    FClientWidth: Integer;                                   { layout.md UIF-271, device px }
    FClientHeight: Integer;
    FShownOnce: Boolean;                                     { UIF-431: OnResize is suppressed until the first show }
    FResizeLock: Integer;                                    { Resize calls collected while bounds settle }
    FResizePending: Boolean;
    FResizedWidth: Integer;                                  { the client size of the last OnResize }
    FResizedHeight: Integer;
    FOnActivate: TNotifyEvent;
    FOnAfterMonitorDpiChanged: TMonitorDpiChangedEvent;
    FOnClose: TCloseEvent;
    FOnCloseQuery: TCloseQueryEvent;
    FOnCreate: TNotifyEvent;
    FOnDeactivate: TNotifyEvent;
    FOnDestroy: TNotifyEvent;
    FOnHide: TNotifyEvent;
    FOnPaint: TNotifyEvent;
    FOnShow: TNotifyEvent;
    procedure SetActiveControl(Value: TWinControl);
    procedure SetBorderIcons(Value: TBorderIcons);
    procedure SetBorderStyle(Value: TFormBorderStyle);
    procedure SetFormStyle(Value: TFormStyle);
    procedure SetPixelsPerInch(Value: Integer);
    procedure SetWindowState(Value: TWindowState);
    function GetActive: Boolean;
    function GetMonitor: TMonitor;
    procedure ApplyPosition;
    procedure FormStyles(var Style, ExStyle: Cardinal);
    procedure GetFrame(out FrameWidth, FrameHeight: Integer);
    procedure FrameChanged;
    procedure AdoptClientSize;
    procedure ShowCommand(var Cmd: Integer);
    procedure WMActivate(var Message: TMessage);
    procedure WMSize(var Message: TMessage);
    procedure EndResizeLock;
    { UIF-445 step 5: CloseQuery/OnClose; a veto resets ModalResult to mrNone }
    procedure CloseModal(var FreeAfter: Boolean);
    { layout.md UIF-285 }
    procedure WMDpiChanged(var Message: TMessage);
  protected
    { layout.md UIF-280 step 1 for forms: Left/Top unscaled, the client size scaled }
    procedure ScaleBounds(M, D: Integer); override;
    { layout.md UIF-300 step 2: the stored design DPI scaled to the form's DPI (Scaled = True) }
    procedure Loaded; override;
    { layout.md UIF-245: a form's own constraints are ignored in dump mode }
    procedure ConstrainedResize(var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer); override;
    { core.md UIF-115 step 3: the form's navigation keys }
    procedure CMDialogKey(var Message: TMessage); virtual;
    { UIF-431: once per client size change, never before the first show }
    procedure Resize; override;
    procedure DoShow; virtual;
    procedure DoHide; virtual;
    procedure Activate; virtual;
    procedure Deactivate; virtual;
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { layout.md UIF-271: the stored client size }
    function GetClientRect: TRect; override;
    { forms do not scroll in Phase 4 (AutoScroll False, not published): no bars }
    procedure UpdateScrollBars; override;
    procedure WMWindowPosChanged(var Message: TWMWindowPosMsg); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure PaintWindow(DC: HDC); override;
    procedure DestroyWnd; override;
    procedure CMVisibleChanged(var Message: TMessage); override;
    procedure WndProc(var Message: TMessage); override;
    function PreviewKeys: Boolean; override;
    property ActiveControl: TWinControl read FActiveControl write SetActiveControl;
    property BorderIcons: TBorderIcons read FBorderIcons write SetBorderIcons
      default [biSystemMenu, biMinimize, biMaximize];
    property BorderStyle: TFormBorderStyle read FBorderStyle write SetBorderStyle default bsSizeable;
    property FormStyle: TFormStyle read FFormStyle write SetFormStyle default fsNormal;
    property KeyPreview: Boolean read FKeyPreview write FKeyPreview default False;
    { layout.md UIF-270: the design DPI while loading, the current DPI otherwise; always stored }
    property PixelsPerInch: Integer read FPixelsPerInch write SetPixelsPerInch stored True;
    property Position: TPosition read FPosition write FPosition default poDesigned;
    property Scaled: Boolean read FScaled write FScaled default True;
    property WindowState: TWindowState read FWindowState write SetWindowState default wsNormal;
    property OnActivate: TNotifyEvent read FOnActivate write FOnActivate;
    property OnAfterMonitorDpiChanged: TMonitorDpiChangedEvent read FOnAfterMonitorDpiChanged
      write FOnAfterMonitorDpiChanged;
    property OnClose: TCloseEvent read FOnClose write FOnClose;
    property OnCloseQuery: TCloseQueryEvent read FOnCloseQuery write FOnCloseQuery;
    property OnCreate: TNotifyEvent read FOnCreate write FOnCreate;
    property OnDeactivate: TNotifyEvent read FOnDeactivate write FOnDeactivate;
    property OnDestroy: TNotifyEvent read FOnDestroy write FOnDestroy;
    property OnHide: TNotifyEvent read FOnHide write FOnHide;
    property OnPaint: TNotifyEvent read FOnPaint write FOnPaint;
    property OnShow: TNotifyEvent read FOnShow write FOnShow;
  public
    { UIF-430 }
    constructor Create(AOwner: TComponent); override;
    constructor CreateNew(AOwner: TComponent; Dummy: Integer = 0); virtual;
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure BeforeDestruction; override;
    { layout.md UIF-273: outer size = client size + frame }
    procedure SetBounds(ALeft, ATop, AWidth, AHeight: Integer); override;
    procedure Show;
    procedure Hide;
    { moves (never resizes) the window into the work area of AMonitor (nil: the form's monitor) }
    procedure MakeFullyVisible(AMonitor: TMonitor = nil);
    { UIF-440 }
    procedure Close;
    function CloseQuery: Boolean; virtual;
    { posts CM_RELEASE; the receiver frees the form (safe inside the form's own event handlers) }
    procedure Release;
    { UIF-445 ... 447 }
    function ShowModal: Integer; virtual;
    { layout.md UIF-282: a form built in code (logical pixels) scaled from 96 DPI to PixelsPerInch }
    procedure ScaleForCurrentDpi;
    { core.md section 8 }
    procedure SetFocus; override;
    function SetFocusedControl(Control: TWinControl): Boolean; override;
    procedure FocusControl(Control: TWinControl);
    procedure DefocusControl(Control: TWinControl; Removing: Boolean); override;
    { actions-menus.md UIF-921 (menus and actions come with their units); base: no shortcut }
    function IsShortCut(var Message: TWMKey): Boolean; virtual;
    property Active: Boolean read GetActive;
    property Monitor: TMonitor read GetMonitor;
    property Canvas: TCanvas read FCanvas;
    property FormState: TFormState read FFormState;
    property ModalResult: TModalResult read FModalResult write FModalResult;
  published
    property Width stored False;
    property Height stored False;
  end;

  TCustomFormClass = class of TCustomForm;

  TForm = class(TCustomForm)
  published
    property ActiveControl;
    property BorderIcons;
    property BorderStyle;
    property Caption;
    property ClientWidth stored True;
    property ClientHeight stored True;
    property Color default clBtnFace;
    property Font;
    property FormStyle;
    property KeyPreview;
    property PixelsPerInch;
    property Position;
    property Scaled;
    property WindowState;
    property OnActivate;
    property OnAfterMonitorDpiChanged;
    property OnClick;
    property OnClose;
    property OnCloseQuery;
    property OnContextPopup;
    property OnCreate;
    property OnDblClick;
    property OnDeactivate;
    property OnDestroy;
    property OnHide;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnMouseWheel;
    property OnPaint;
    property OnResize;
    property OnShow;
  end;

  TFormClass = class of TForm;

  { application-forms.md section 3 }
  TMonitor = class(TObject)
  private
    FHandle: HMONITOR;
    function Info: TMonitorInfo;
    function GetBoundsRect: TRect;
    function GetWorkareaRect: TRect;
    function GetPrimary: Boolean;
    function GetPixelsPerInch: Integer;
  public
    property Handle: HMONITOR read FHandle;
    property BoundsRect: TRect read GetBoundsRect;
    property WorkareaRect: TRect read GetWorkareaRect;
    property Primary: Boolean read GetPrimary;
    property PixelsPerInch: Integer read GetPixelsPerInch;
  end;

  TScreen = class(TComponent)
  private
    FCustomForms: TList;
    FForms: TList;
    FMonitors: TList;
    FFonts: TStringList;
    FCursor: TCursor;
    FActiveForm: TForm;
    FActiveCustomForm: TCustomForm;
    FFocusedForm: TCustomForm;
    FIconFont: TFont;
    FMenuFont: TFont;
    FHintFont: TFont;
    FMessageFont: TFont;
    FOnActiveFormChange: TNotifyEvent;
    FOnActiveControlChange: TNotifyEvent;
    function GetCustomForm(Index: Integer): TCustomForm;
    function GetCustomFormCount: Integer;
    function GetForm(Index: Integer): TForm;
    function GetFormCount: Integer;
    function GetActiveControl: TWinControl;
    function GetWidth: Integer;
    function GetHeight: Integer;
    function GetDesktopRect: TRect;
    function GetWorkAreaRect: TRect;
    function GetPixelsPerInch: Integer;
    procedure SetCursor(Value: TCursor);
    function GetFonts: TStrings;
    function GetMonitorCount: Integer;
    function GetMonitor(Index: Integer): TMonitor;
    procedure EnumMonitors;
    procedure ClearMonitors;
    procedure AddForm(AForm: TCustomForm);
    procedure RemoveForm(AForm: TCustomForm);
    { UIF-435: the active form; OnActiveFormChange after a change }
    procedure SetActiveForm(AForm: TCustomForm);
    { the TMonitor of a monitor handle (the list is re-read once when it is unknown) }
    function MonitorOf(H: HMONITOR): TMonitor;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure ResetFonts;
    { re-reads IconFont, MenuFont, HintFont, MessageFont (WM_SETTINGCHANGE) }
    procedure UpdateSystemFonts;
    property CustomForms[Index: Integer]: TCustomForm read GetCustomForm;
    property CustomFormCount: Integer read GetCustomFormCount;
    property Forms[Index: Integer]: TForm read GetForm;
    property FormCount: Integer read GetFormCount;
    property ActiveForm: TForm read FActiveForm;
    property ActiveCustomForm: TCustomForm read FActiveCustomForm;
    property ActiveControl: TWinControl read GetActiveControl;
    property FocusedForm: TCustomForm read FFocusedForm write FFocusedForm;
    property Width: Integer read GetWidth;
    property Height: Integer read GetHeight;
    property DesktopRect: TRect read GetDesktopRect;
    property WorkAreaRect: TRect read GetWorkAreaRect;
    property PixelsPerInch: Integer read GetPixelsPerInch;
    property Cursor: TCursor read FCursor write SetCursor;
    property Fonts: TStrings read GetFonts;
    property MonitorCount: Integer read GetMonitorCount;
    property Monitors[Index: Integer]: TMonitor read GetMonitor;
    property IconFont: TFont read FIconFont;
    property MenuFont: TFont read FMenuFont;
    property HintFont: TFont read FHintFont;
    property MessageFont: TFont read FMessageFont;
    property OnActiveFormChange: TNotifyEvent read FOnActiveFormChange write FOnActiveFormChange;
    property OnActiveControlChange: TNotifyEvent read FOnActiveControlChange write FOnActiveControlChange;
  end;

  { application-forms.md section 2 }
  TApplication = class(TComponent)
  private
    FHandle: HWND;
    FInitialized: Boolean;
    FTerminated: Boolean;
    FMainForm: TForm;
    FActive: Boolean;                                        { WM_ACTIVATEAPP }
    FShowMainForm: Boolean;
    FTitle: string;
    FNonInteractive: Boolean;
    FModalLevel: Integer;
    FDumpLayoutFile: string;
    { layout.md UIF-312: writes the dump of every form; False (and the reason on stderr) when writing fails }
    function WriteLayoutDump: Boolean;
    FOnException: TExceptionEvent;
    FOnIdle: TIdleEvent;
    FOnMessage: TMessageEvent;
    function GetExeName: string;
    { UIF-412: the Classes.WakeMainThread hook; the loop wakes and runs CheckSynchronize }
    procedure WakeMainThread(Sender: TObject);
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-402; idempotent }
    procedure Initialize;
    { UIF-405 }
    procedure CreateForm(InstanceClass: TComponentClass; var Reference);
    { UIF-410 }
    procedure Run;
    procedure Terminate;
    { UIF-411: False when the queue is empty }
    function ProcessMessage(var Msg: TMsg): Boolean;
    { UIF-411 IsKeyMsg: core.md UIF-115 (shortcuts, navigation keys); True = consumed }
    function IsKeyMsg(var Msg: TMsg): Boolean;
    procedure ProcessMessages;
    procedure HandleMessage;
    { UIF-412 }
    procedure Idle(const Msg: TMsg);
    { UIF-460: reports ExceptObject; the caller continues }
    procedure HandleException(Sender: TObject);
    { UIF-461, UIF-462 }
    procedure ShowException(E: Exception);
    property Handle: HWND read FHandle;
    property MainForm: TForm read FMainForm;
    { a window of this application is the foreground window }
    property Active: Boolean read FActive;
    property Terminated: Boolean read FTerminated;
    { UIF-445: nesting depth of ShowModal }
    property ModalLevel: Integer read FModalLevel;
    property ShowMainForm: Boolean read FShowMainForm write FShowMainForm;
    property Title: string read FTitle write FTitle;
    property ExeName: string read GetExeName;
    { UIF-013: FXC_UI_NONINTERACTIVE = 1 at start-up }
    property NonInteractive: Boolean read FNonInteractive;
    { layout.md UIF-310: the path after --dump-layout; '' = not in dump mode }
    property DumpLayoutFile: string read FDumpLayoutFile;
    property OnException: TExceptionEvent read FOnException write FOnException;
    property OnIdle: TIdleEvent read FOnIdle write FOnIdle;
    property OnMessage: TMessageEvent read FOnMessage write FOnMessage;
  end;

type
  TTimerHook = procedure(TimerID: UINT_PTR);

var
  Application: TApplication;
  Screen: TScreen;
  { controls-standard.md UIF-730: WM_TIMER at the application window goes here (set by ExtCtrls) }
  TimerHook: TTimerHook;

{ application-forms.md 4.2: the form at the top of the parent chain (TopForm), or the nearest one; nil if none }
function GetParentForm(Control: TControl; TopForm: Boolean = True): TCustomForm;
{ as GetParentForm, raising EInvalidOperation SParentRequired when there is no form }
function ValidParentForm(Control: TControl; TopForm: Boolean = True): TCustomForm;
{ core.md UIF-120: Str has the accelerator '&<VK>' (AnsiUpperCase comparison, '&&' is a literal ampersand) }
function IsAccel(VK: Word; const Str: string): Boolean;
{ layout.md UIF-320: the canonical dump text of AForm, or of every form of Screen (creation order) for nil }
function LayoutDumpText(AForm: TCustomForm = nil): string;
{ UIF-320: S as a JSON string: quotes, '\', control characters as \u00XX (upper-case hex), all else literal }
function LayoutDumpString(const S: string): string;

implementation

uses
  RTLConsts;

function GetParentForm(Control: TControl; TopForm: Boolean): TCustomForm;
begin
  Result := nil;
  while Control <> nil do
  begin
    if Control is TCustomForm then
    begin
      Result := TCustomForm(Control);
      if not TopForm then
        Exit;
    end;
    Control := Control.Parent;
  end;
end;

function ValidParentForm(Control: TControl; TopForm: Boolean): TCustomForm;
begin
  Result := GetParentForm(Control, TopForm);
  if Result = nil then
    raise EInvalidOperation.CreateFmt(SParentRequired, [Control.Name]);
end;

function IsAccel(VK: Word; const Str: string): Boolean;
var
  I: Integer;
begin
  Result := False;
  I := 1;
  while I < Length(Str) do
  begin
    if Str[I] = '&' then
    begin
      if Str[I + 1] <> '&' then
      begin
        Result := AnsiUpperCase(Str[I + 1]) = AnsiUpperCase(Char(VK));
        if Result then
          Exit;
      end;
      Inc(I, 2);
    end
    else
      Inc(I);
  end;
end;

{ UIF-120: CM_DIALOGCHAR to the visible, enabled controls of the tree, depth-first in z-order, until one answers }
function OfferDialogChar(Parent: TWinControl; var Message: TMessage): Boolean;
var
  I: Integer;
  C: TControl;
begin
  Result := False;
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    if not (C.Visible and C.Enabled) then
      Continue;
    Message.Result := 0;
    C.WindowProc(Message);
    if (Message.Result <> 0) or ((C is TWinControl) and OfferDialogChar(TWinControl(C), Message)) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ UIF-110 helper: CM_FOCUSCHANGED to every control of the tree, depth-first in z-order }
procedure SendFocusChanged(Parent: TWinControl; Control: TWinControl);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    C.Perform(CM_FOCUSCHANGED, 0, LPARAM(Pointer(Control)));
    if C is TWinControl then
      SendFocusChanged(TWinControl(C), Control);
  end;
end;

{ C is a proper ancestor of Control }
function IsAncestor(C, Control: TWinControl): Boolean;
begin
  Result := (C <> nil) and (Control <> nil) and (C <> Control) and C.ContainsControl(Control);
end;

const
  AppClassName = 'PN.Application';
  ShellManifestResource = 124;                               { RTL-871 }

{ the message-only window: CM_RELEASE of forms without a window (UIF-443); exceptions never unwind into Win32 }
function AppWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  if Msg = WM_NULL then
    CheckSynchronize(0)                                      { UIF-412: WakeMainThread }
  else if Msg = WM_TIMER then
  begin
    if Assigned(TimerHook) then
      TimerHook(UINT_PTR(WParam));                           { UIF-730 }
  end
  else if Msg = CM_RELEASE then
    try
      TObject(Pointer(LParam)).Free;
    except
      Application.HandleException(Application);
    end
  else
    Result := DefWindowProcW(Wnd, Msg, WParam, LParam);
end;

{ UIF-402 step 2: the comctl32 that loading resolves to under the current activation context exports the
  version-6-only LoadIconMetric }
function CommonControls6Active: Boolean;
var
  Module: HMODULE;
begin
  Result := False;
  Module := LoadLibraryW('comctl32.dll');
  if Module = 0 then
    Exit;
  Result := GetProcAddress(Module, 'LoadIconMetric') <> nil;
  FreeLibrary(Module);
end;

{ RTL-871: the common controls 6 manifest of the system shell library, active for the life of the process }
procedure ActivateCommonControls6;
var
  Buffer: array[0..MAX_PATH] of Char;
  N: UINT;
  Dir, Source: string;
  Context: TActCtxW;
  H: THandle;
  Cookie: ULONG_PTR;
begin
  N := GetSystemDirectoryW(@Buffer[0], MAX_PATH);
  if N = 0 then
    Exit;
  Buffer[N] := #0;
  Dir := string(PChar(@Buffer[0]));
  Source := Dir + '\shell32.dll';
  FillChar(Context, SizeOf(Context), 0);
  Context.cbSize := SizeOf(Context);
  Context.dwFlags := ACTCTX_FLAG_RESOURCE_NAME_VALID or ACTCTX_FLAG_ASSEMBLY_DIRECTORY_VALID;
  Context.lpSource := PChar(Source);
  Context.lpAssemblyDirectory := PChar(Dir);
  Context.lpResourceName := MakeIntResource(ShellManifestResource);
  H := CreateActCtxW(Context);
  if H <> INVALID_HANDLE_VALUE then
  begin
    Cookie := 0;
    ActivateActCtx(H, Cookie);
  end;
end;

{ ---------------------------------------------------------------- TScrollingWinControl }

constructor TControlScrollBar.Create(AControl: TScrollingWinControl; AKind: TScrollBarKind);
begin
  inherited Create;
  FControl := AControl;
  FKind := AKind;
  FIncrement := 8;
  FVisible := True;
end;

procedure TControlScrollBar.Assign(Source: TPersistent);
begin
  if Source is TControlScrollBar then
  begin
    FIncrement := TControlScrollBar(Source).FIncrement;
    FTracking := TControlScrollBar(Source).FTracking;
    FVisible := TControlScrollBar(Source).FVisible;
    Range := TControlScrollBar(Source).FRange;
  end
  else
    inherited Assign(Source);
end;

function TControlScrollBar.IsScrollBarVisible: Boolean;
begin
  Result := FShown;
end;

function TControlScrollBar.ClientExtent: Integer;
begin
  if FKind = sbHorizontal then
    Result := FControl.ClientWidth
  else
    Result := FControl.ClientHeight;
end;

function TControlScrollBar.MaxPosition: Integer;
begin
  Result := FCalcRange - ClientExtent;
  if Result < 0 then
    Result := 0;
end;

{ UIF-293: the children move by minus the change; dump mode keeps every position at 0 }
procedure TControlScrollBar.SetPosition(Value: Integer);
var
  D: Integer;
begin
  if LayoutDumpMode then
    Exit;
  if Value > MaxPosition then
    Value := MaxPosition;
  if Value < 0 then
    Value := 0;
  if Value = FPosition then
    Exit;
  D := Value - FPosition;
  FPosition := Value;
  if FKind = sbHorizontal then
    FControl.ScrollBy(-D, 0)
  else
    FControl.ScrollBy(0, -D);
  UpdateNative;
end;

procedure TControlScrollBar.SetRange(Value: Integer);
begin
  if Value < 0 then
    Value := 0;
  if FRange <> Value then
  begin
    FRange := Value;
    if not FControl.FAutoScroll then
      FControl.Realign;
  end;
end;

procedure TControlScrollBar.SetVisible(Value: Boolean);
begin
  if FVisible <> Value then
  begin
    FVisible := Value;
    FControl.Realign;
  end;
end;

{ SetScrollInfo without SIF_DISABLENOSCROLL: a page as large as the range hides the bar }
procedure TControlScrollBar.UpdateNative;
const
  Bars: array[TScrollBarKind] of Integer = (SB_HORZ, SB_VERT);
var
  Info: TScrollInfo;
begin
  if not FControl.HandleAllocated or LayoutDumpMode then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  if FShown then
  begin
    Info.nMax := FCalcRange - 1;
    Info.nPage := UINT(ClientExtent);
    Info.nPos := FPosition;
  end;
  SetScrollInfo(FControl.Handle, Bars[FKind], Info, True);
end;

{ ---------------------------------------------------------------- TScrollingWinControl }

constructor TScrollingWinControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  FHorzScrollBar := TControlScrollBar.Create(Self, sbHorizontal);
  FVertScrollBar := TControlScrollBar.Create(Self, sbVertical);
end;

destructor TScrollingWinControl.Destroy;
begin
  inherited Destroy;
  FHorzScrollBar.Free;
  FVertScrollBar.Free;
end;

procedure TScrollingWinControl.SetAutoScroll(Value: Boolean);
begin
  if FAutoScroll <> Value then
  begin
    FAutoScroll := Value;
    Realign;
  end;
end;

procedure TScrollingWinControl.SetHorzScrollBar(Value: TControlScrollBar);
begin
  FHorzScrollBar.Assign(Value);
end;

procedure TScrollingWinControl.SetVertScrollBar(Value: TControlScrollBar);
begin
  FVertScrollBar.Assign(Value);
end;

function TScrollingWinControl.BorderInset: Integer;
begin
  Result := 0;
end;

function TScrollingWinControl.ScrollDPI: Integer;
var
  F: TCustomForm;
begin
  Result := 96;
  F := GetParentForm(Self, False);
  if (F <> nil) and not LayoutDumpMode and (F.FPixelsPerInch > 0) then
    Result := F.FPixelsPerInch;
end;

function TScrollingWinControl.BarSize: Integer;
begin
  Result := LayoutMulDiv(17, ScrollDPI, 96);
end;

{ UIF-215 }
function TScrollingWinControl.GetClientRect: TRect;
var
  W, H: Integer;
begin
  W := Width - 2 * BorderInset;
  H := Height - 2 * BorderInset;
  if FVertScrollBar.FShown then
    W := W - BarSize;
  if FHorzScrollBar.FShown then
    H := H - BarSize;
  if W < 0 then
    W := 0;
  if H < 0 then
    H := 0;
  Result := Rect(0, 0, W, H);
end;

procedure TScrollingWinControl.UpdateScrollBars;
var
  W0, H0, B, RangeH, RangeV, I, V: Integer;
  HBar, VBar: Boolean;
  C: TControl;
begin
  { UIF-290, UIF-292 }
  if FAutoScroll then
  begin
    RangeH := 0;
    RangeV := 0;
    for I := 0 to ControlCount - 1 do
    begin
      C := Controls[I];
      if (C.Align <> alNone) or not C.Visible then
        Continue;
      if akRight in C.Anchors then
        V := C.Left + C.Constraints.MinWidth
      else
        V := C.Left + C.Width;
      V := V + FHorzScrollBar.FPosition;
      if V > RangeH then
        RangeH := V;
      if akBottom in C.Anchors then
        V := C.Top + C.Constraints.MinHeight
      else
        V := C.Top + C.Height;
      V := V + FVertScrollBar.FPosition;
      if V > RangeV then
        RangeV := V;
    end;
  end
  else
  begin
    RangeH := FHorzScrollBar.FRange;
    RangeV := FVertScrollBar.FRange;
  end;
  { UIF-291; a bar with Visible = False is never shown and takes no space }
  W0 := Width - 2 * BorderInset;
  if W0 < 0 then
    W0 := 0;
  H0 := Height - 2 * BorderInset;
  if H0 < 0 then
    H0 := 0;
  B := BarSize;
  HBar := FHorzScrollBar.FVisible and (RangeH > W0);
  VBar := FVertScrollBar.FVisible and (RangeV > H0);
  if HBar and not VBar then
    VBar := FVertScrollBar.FVisible and (RangeV > H0 - B);
  if VBar and not HBar then
    HBar := FHorzScrollBar.FVisible and (RangeH > W0 - B);
  FHorzScrollBar.FShown := HBar;
  FVertScrollBar.FShown := VBar;
  FHorzScrollBar.FCalcRange := RangeH;
  FVertScrollBar.FCalcRange := RangeV;
  { a smaller range pulls the position back (children move with it) }
  if FHorzScrollBar.FPosition > FHorzScrollBar.MaxPosition then
    FHorzScrollBar.Position := FHorzScrollBar.MaxPosition;
  if FVertScrollBar.FPosition > FVertScrollBar.MaxPosition then
    FVertScrollBar.Position := FVertScrollBar.MaxPosition;
  FHorzScrollBar.UpdateNative;
  FVertScrollBar.UpdateNative;
end;

procedure TScrollingWinControl.AlignControls(AControl: TControl; var Rect: TRect);
begin
  UpdateScrollBars;
  Rect := GetClientRect;
  AdjustClientRect(Rect);
  Rect.Left := Rect.Left + Padding.Left;
  Rect.Top := Rect.Top + Padding.Top;
  Rect.Right := Rect.Right - Padding.Right;
  Rect.Bottom := Rect.Bottom - Padding.Bottom;
  inherited AlignControls(AControl, Rect);
end;

procedure TScrollingWinControl.AdjustClientRect(var Rect: TRect);
begin
  inherited AdjustClientRect(Rect);
  OffsetRect(Rect, -FHorzScrollBar.FPosition, -FVertScrollBar.FPosition);
end;

function TScrollingWinControl.ScrollOffset: TPoint;
begin
  Result := Point(FHorzScrollBar.FPosition, FVertScrollBar.FPosition);
end;

procedure TScrollingWinControl.CreateWnd;
begin
  inherited CreateWnd;
  FHorzScrollBar.UpdateNative;
  FVertScrollBar.UpdateNative;
end;

{ UIF-750: line = Increment, page = client extent - Increment, the thumb only with Tracking while dragging }
procedure TScrollingWinControl.ScrollMessage(Bar: TControlScrollBar; var Message: TWMScroll);
var
  P: Integer;
begin
  P := Bar.FPosition;
  case Message.ScrollCode of
    SB_LINEUP: P := P - Bar.FIncrement;
    SB_LINEDOWN: P := P + Bar.FIncrement;
    SB_PAGEUP: P := P - (Bar.ClientExtent - Bar.FIncrement);
    SB_PAGEDOWN: P := P + (Bar.ClientExtent - Bar.FIncrement);
    SB_THUMBPOSITION: P := Message.Pos;
    SB_THUMBTRACK:
      if Bar.FTracking then
        P := Message.Pos;
    SB_TOP: P := 0;
    SB_BOTTOM: P := Bar.MaxPosition;
  end;
  Bar.Position := P;
  Message.Result := 0;
end;

procedure TScrollingWinControl.WMHScroll(var Message: TWMScroll);
begin
  ScrollMessage(FHorzScrollBar, Message);
end;

procedure TScrollingWinControl.WMVScroll(var Message: TWMScroll);
begin
  ScrollMessage(FVertScrollBar, Message);
end;

function TScrollingWinControl.DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean;
var
  Step: Integer;
begin
  Result := inherited DoMouseWheel(Shift, WheelDelta, MousePos);
  if Result or (FVertScrollBar.MaxPosition = 0) then
    Exit;
  Step := LayoutMulDiv(48, ScrollDPI, 96);                   { 3 x 16 logical px }
  FVertScrollBar.Position := FVertScrollBar.FPosition - MulDiv(WheelDelta, Step, 120);
  Result := True;
end;

procedure TScrollingWinControl.WndProc(var Message: TMessage);
var
  C: TControl;
begin
  if (Message.Msg = CM_FOCUSCHANGED) and (Message.LParam <> 0) then
  begin
    C := TControl(Pointer(Message.LParam));
    if (C <> Self) and ContainsControl(C) then
      ScrollInView(C);
  end;
  inherited WndProc(Message);
end;

{ UIF-751: the smallest position change that brings the control's bounds into the client area }
procedure TScrollingWinControl.ScrollInView(AControl: TControl);
var
  P: TPoint;
  R: TRect;
  D: Integer;
begin
  if (AControl = nil) or (AControl = Self) or not ContainsControl(AControl) then
    Exit;
  P := AControl.ClientToParent(Point(0, 0), Self);
  R := Bounds(P.X, P.Y, AControl.Width, AControl.Height);
  if R.Left < 0 then
    FHorzScrollBar.Position := FHorzScrollBar.FPosition + R.Left
  else if R.Right > ClientWidth then
  begin
    D := R.Right - ClientWidth;
    if D > R.Left then
      D := R.Left;
    FHorzScrollBar.Position := FHorzScrollBar.FPosition + D;
  end;
  if R.Top < 0 then
    FVertScrollBar.Position := FVertScrollBar.FPosition + R.Top
  else if R.Bottom > ClientHeight then
  begin
    D := R.Bottom - ClientHeight;
    if D > R.Top then
      D := R.Top;
    FVertScrollBar.Position := FVertScrollBar.FPosition + D;
  end;
end;

procedure TScrollingWinControl.ChangeScale(M, D: Integer);
begin
  if (M <> D) and (M > 0) and (D > 0) then
  begin
    FHorzScrollBar.FIncrement := LayoutMulDiv(FHorzScrollBar.FIncrement, M, D);
    FVertScrollBar.FIncrement := LayoutMulDiv(FVertScrollBar.FIncrement, M, D);
    FHorzScrollBar.FRange := LayoutMulDiv(FHorzScrollBar.FRange, M, D);
    FVertScrollBar.FRange := LayoutMulDiv(FVertScrollBar.FRange, M, D);
    FHorzScrollBar.FPosition := LayoutMulDiv(FHorzScrollBar.FPosition, M, D);
    FVertScrollBar.FPosition := LayoutMulDiv(FVertScrollBar.FPosition, M, D);
  end;
  inherited ChangeScale(M, D);
end;

{ ---------------------------------------------------------------- TScrollBox }

constructor TScrollBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FBorderStyle := bsSingle;
  AutoScroll := True;
  SetBounds(Left, Top, 185, 41);
  Color := clBtnFace;
  ParentColor := True;
end;

procedure TScrollBox.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
end;

function TScrollBox.BorderInset: Integer;
begin
  if FBorderStyle = bsSingle then
    Result := 2
  else
    Result := 0;
end;

procedure TScrollBox.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
    Perform(CM_BORDERCHANGED, 0, 0);
    Realign;
  end;
end;

{ ---------------------------------------------------------------- TCustomForm }

{ UIF-430 steps 1-2: a class other than TForm/TCustomForm reads its embedded .form }
constructor TCustomForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  if (ClassType <> TForm) and (ClassType <> TCustomForm) then
    if not InitInheritedComponent(Self, TForm) then
      raise EResNotFound.CreateFmt(SResNotFound, [ClassName]);
end;

constructor TCustomForm.CreateNew(AOwner: TComponent; Dummy: Integer);
begin
  Application.Initialize;                                    { UIF-402: a form constructor initializes }
  FFormState := [fsCreating];
  inherited Create(AOwner);
  Visible := False;
  ParentColor := False;
  ParentFont := False;
  Color := clBtnFace;
  FBorderIcons := [biSystemMenu, biMinimize, biMaximize];
  FBorderStyle := bsSizeable;
  FScaled := True;
  if LayoutDumpMode then
    FPixelsPerInch := 96                                     { UIF-311 }
  else
    FPixelsPerInch := GetDpiForSystem;
  FDesignPixelsPerInch := 96;
  FClientWidth := 320;
  FClientHeight := 240;
  FrameChanged;
  FCanvas := TControlCanvas.Create;
  TControlCanvas(FCanvas).Control := Self;
  Screen.AddForm(Self);
end;

{ UIF-442 steps 4-6 }
destructor TCustomForm.Destroy;
begin
  if Screen <> nil then
    Screen.RemoveForm(Self);
  if (Application <> nil) and (Application.FMainForm = Self) then
  begin
    Application.FMainForm := nil;
    Application.Terminate;
  end;
  inherited Destroy;
  FCanvas.Free;
end;

{ UIF-430 step 5: OnCreate, reported and survived }
procedure TCustomForm.AfterConstruction;
begin
  inherited AfterConstruction;
  Exclude(FFormState, fsCreating);
  FCreateReached := True;
  if Assigned(FOnCreate) then
    try
      FOnCreate(Self);
    except
      Application.HandleException(Self);
    end;
end;

{ UIF-442 steps 1-2 }
procedure TCustomForm.BeforeDestruction;
begin
  if Visible then
    Hide;
  if FCreateReached and Assigned(FOnDestroy) then
    try
      FOnDestroy(Self);
    except
      Application.HandleException(Self);
    end;
  inherited BeforeDestruction;
end;

procedure TCustomForm.Show;
begin
  Visible := True;
  BringToFront;
end;

procedure TCustomForm.Hide;
begin
  Visible := False;
end;

function IntMin(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function IntMax(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

procedure TCustomForm.Close;
var
  Action: TCloseAction;
begin
  if fsModal in FFormState then
    ModalResult := mrCancel
  else if CloseQuery then
  begin
    Action := caHide;
    if Self = Application.FMainForm then
      Action := caFree;
    if Assigned(FOnClose) then
      FOnClose(Self, Action);
    case Action of
      caHide: Hide;
      caMinimize: WindowState := wsMinimized;
      caFree: Release;
    end;
    if (Self = Application.FMainForm) and (Action in [caHide, caFree]) then
      Application.Terminate;
  end;
end;

function TCustomForm.CloseQuery: Boolean;
begin
  Result := True;
  if Assigned(FOnCloseQuery) then
    FOnCloseQuery(Self, Result);
end;

procedure TCustomForm.Release;
begin
  if HandleAllocated then
    PostMessageW(Handle, CM_RELEASE, 0, LPARAM(Pointer(Self)))
  else
    PostMessageW(Application.Handle, CM_RELEASE, 0, LPARAM(Pointer(Self)));
end;

type
  TTaskWindows = record
    Skip: HWND;
    List: TList;
  end;
  PTaskWindows = ^TTaskWindows;

{ UIF-445 step 3: every visible and enabled top-level window of the thread except the modal form's own }
function DisableTaskWindow(Wnd: HWND; Data: LPARAM): BOOL; stdcall;
var
  P: PTaskWindows;
begin
  P := PTaskWindows(Pointer(Data));
  if (Wnd <> P^.Skip) and IsWindowVisible(Wnd) and IsWindowEnabled(Wnd) then
  begin
    P^.List.Add(Pointer(Wnd));
    EnableWindow(Wnd, False);
  end;
  Result := True;
end;

procedure TCustomForm.CloseModal(var FreeAfter: Boolean);
var
  Action: TCloseAction;
begin
  Action := caHide;
  if CloseQuery then
  begin
    if Assigned(FOnClose) then
      FOnClose(Self, Action);
  end
  else
    Action := caNone;
  if Action = caNone then
    FModalResult := mrNone
  else if Action = caFree then
    FreeAfter := True;
end;

function TCustomForm.ShowModal: Integer;
var
  Task: TTaskWindows;
  ActiveWindow: HWND;
  FreeAfter: Boolean;
  I: Integer;
begin
  if Visible or (fsModal in FFormState) then
    raise EInvalidOperation.Create(SVisibleModal);
  if not Enabled then
    raise EInvalidOperation.Create(SDisabledModal);
  ReleaseCapture;
  Inc(Application.FModalLevel);
  ActiveWindow := GetActiveWindow;
  Include(FFormState, fsModal);
  FModalResult := mrNone;
  FreeAfter := False;
  Task.List := TList.Create;
  try
    HandleNeeded;
    Task.Skip := Handle;
    EnumThreadWindows(MainThreadID, DisableTaskWindow, LPARAM(@Task));
    Show;
    repeat
      Application.HandleMessage;
      if Application.Terminated then
        FModalResult := mrCancel
      else if FModalResult <> mrNone then
        CloseModal(FreeAfter);
    until FModalResult <> mrNone;
    Result := FModalResult;
  finally
    for I := 0 to Task.List.Count - 1 do
      if IsWindow(HWND(Task.List[I])) then
        EnableWindow(HWND(Task.List[I]), True);
    Task.List.Free;
    Hide;
    if (ActiveWindow <> 0) and IsWindow(ActiveWindow) then
      SetActiveWindow(ActiveWindow);
    Exclude(FFormState, fsModal);
    Dec(Application.FModalLevel);
  end;
  if FreeAfter then
    Release;
end;

procedure TCustomForm.MakeFullyVisible(AMonitor: TMonitor);
var
  WA: TRect;
  L, T: Integer;
begin
  if AMonitor = nil then
    AMonitor := Monitor;
  WA := AMonitor.WorkareaRect;
  L := Left;
  T := Top;
  if L + Width > WA.Right then
    L := WA.Right - Width;
  if L < WA.Left then
    L := WA.Left;
  if T + Height > WA.Bottom then
    T := WA.Bottom - Height;
  if T < WA.Top then
    T := WA.Top;
  if (L <> Left) or (T <> Top) then
    SetBounds(L, T, Width, Height);
end;

{ the monitor of the window (nearest), or of the bounds without a handle }
function TCustomForm.GetMonitor: TMonitor;
begin
  if HandleAllocated then
    Result := Screen.MonitorOf(MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST))
  else
    Result := Screen.MonitorOf(MonitorFromRect(BoundsRect, MONITOR_DEFAULTTONEAREST));
end;

{ UIF-436 }
procedure TCustomForm.ApplyPosition;
var
  R, WA: TRect;
  P: TPoint;
  M: TCustomForm;
  Pos: TPosition;
  Info: TMonitorInfo;
  I: Integer;
  Visible32: Boolean;
begin
  Pos := FPosition;
  if Pos = poOwnerFormCenter then
  begin
    if (Owner is TCustomForm) and (Owner <> Self) and TCustomForm(Owner).HandleAllocated then
    begin
      M := TCustomForm(Owner);
      SetBounds(M.Left + (M.Width - Width) div 2, M.Top + (M.Height - Height) div 2, Width, Height);
      MakeFullyVisible;
      Exit;
    end;
    Pos := poMainFormCenter;
  end;
  if Pos = poMainFormCenter then
  begin
    M := Application.FMainForm;
    if (M <> nil) and (M <> Self) and M.HandleAllocated then
    begin
      SetBounds(M.Left + (M.Width - Width) div 2, M.Top + (M.Height - Height) div 2, Width, Height);
      MakeFullyVisible;
      Exit;
    end;
    Pos := poScreenCenter;
  end;
  case Pos of
    poScreenCenter:
      begin
        GetCursorPos(P);
        FillChar(Info, SizeOf(Info), 0);
        Info.cbSize := SizeOf(Info);
        GetMonitorInfoW(MonitorFromPoint(P, MONITOR_DEFAULTTONEAREST), Info);
        WA := Info.rcWork;
        SetBounds(WA.Left + (WA.Right - WA.Left - Width) div 2, WA.Top + (WA.Bottom - WA.Top - Height) div 2,
          Width, Height);
      end;
    poDesktopCenter:
      begin
        R := Screen.DesktopRect;
        SetBounds(R.Left + (R.Right - R.Left - Width) div 2, R.Top + (R.Bottom - R.Top - Height) div 2, Width,
          Height);
      end;
    poDesigned:
      begin
        Visible32 := False;
        for I := 0 to Screen.MonitorCount - 1 do
        begin
          WA := Screen.Monitors[I].WorkareaRect;
          if (IntMin(Left + Width, WA.Right) - IntMax(Left, WA.Left) >= 32)
            and (IntMin(Top + Height, WA.Bottom) - IntMax(Top, WA.Top) >= 32) then
            Visible32 := True;
        end;
        if not Visible32 then
          for I := 0 to Screen.MonitorCount - 1 do
            if Screen.Monitors[I].Primary then
              MakeFullyVisible(Screen.Monitors[I]);
      end;
  end;
end;

{ UIF-425 }
procedure TCustomForm.CreateParams(var Params: TCreateParams);
const
  FormClass = 'PN.TForm';
var
  I, FW, FH: Integer;
begin
  if FFormStyle in [fsMDIChild, fsMDIForm] then
    raise ENotSupportedException.Create(SMDINotSupported);
  inherited CreateParams(Params);
  FormStyles(Params.Style, Params.ExStyle);
  if Application.FMainForm = Self then
    Params.ExStyle := Params.ExStyle or WS_EX_APPWINDOW;
  if FFormStyle = fsStayOnTop then
    Params.ExStyle := Params.ExStyle or WS_EX_TOPMOST;
  GetFrame(FW, FH);
  Params.Width := FClientWidth + FW;
  Params.Height := FClientHeight + FH;
  { UIF-436: Windows chooses position and/or size of a window created before the first show }
  if not FShownOnce and ((Params.Style and (WS_POPUP or WS_CHILD)) = 0) then
  begin
    if FPosition in [poDefault, poDefaultPosOnly] then
    begin
      Params.X := Integer(CW_USEDEFAULT);
      Params.Y := Integer(CW_USEDEFAULT);
    end;
    if FPosition in [poDefault, poDefaultSizeOnly] then
    begin
      Params.Width := Integer(CW_USEDEFAULT);
      Params.Height := Integer(CW_USEDEFAULT);
    end;
  end;
  Params.WndParent := 0;
  if (Application.FMainForm <> nil) and (Application.FMainForm <> Self) and Application.FMainForm.HandleAllocated then
    Params.WndParent := Application.FMainForm.Handle;
  for I := 0 to Length(FormClass) - 1 do
    Params.WinClassName[I] := FormClass[I + 1];
  Params.WinClassName[Length(FormClass)] := #0;
end;

{ UIF-425: the style bits of BorderStyle and BorderIcons (no main-form or z-order bits) }
procedure TCustomForm.FormStyles(var Style, ExStyle: Cardinal);
begin
  Style := WS_CLIPCHILDREN or WS_CLIPSIBLINGS;
  ExStyle := WS_EX_CONTROLPARENT;
  case FBorderStyle of
    bsNone:
      Style := Style or WS_POPUP;
    bsSingle:
      Style := Style or WS_OVERLAPPED or WS_CAPTION;
    bsSizeable:
      Style := Style or WS_OVERLAPPED or WS_CAPTION or WS_THICKFRAME;
    bsDialog:
      begin
        Style := Style or WS_POPUP or WS_CAPTION;
        ExStyle := ExStyle or WS_EX_DLGMODALFRAME;
      end;
    bsToolWindow:
      begin
        Style := Style or WS_OVERLAPPED or WS_CAPTION;
        ExStyle := ExStyle or WS_EX_TOOLWINDOW;
      end;
    bsSizeToolWin:
      begin
        Style := Style or WS_OVERLAPPED or WS_CAPTION or WS_THICKFRAME;
        ExStyle := ExStyle or WS_EX_TOOLWINDOW;
      end;
  end;
  if FBorderStyle <> bsNone then
  begin
    if biSystemMenu in FBorderIcons then
    begin
      Style := Style or WS_SYSMENU;
      if FBorderStyle in [bsSingle, bsSizeable] then
      begin
        if biMinimize in FBorderIcons then
          Style := Style or WS_MINIMIZEBOX;
        if biMaximize in FBorderIcons then
          Style := Style or WS_MAXIMIZEBOX;
      end;
    end;
    if (biHelp in FBorderIcons) and ((Style and (WS_MINIMIZEBOX or WS_MAXIMIZEBOX)) = 0) then
      ExStyle := ExStyle or WS_EX_CONTEXTHELP;
  end;
end;

{ layout.md UIF-272: the frame of AdjustWindowRectExForDpi for the current style properties and DPI }
procedure TCustomForm.GetFrame(out FrameWidth, FrameHeight: Integer);
var
  Style, ExStyle: Cardinal;
  R: TRect;
begin
  FormStyles(Style, ExStyle);
  R := Rect(0, 0, 0, 0);
  if FPixelsPerInch > 0 then
    AdjustWindowRectExForDpi(R, Style, False, ExStyle, FPixelsPerInch);
  FrameWidth := R.Right - R.Left;
  FrameHeight := R.Bottom - R.Top;
end;

{ a style or DPI change keeps the client size: the window is re-created, or the outer size recomputed }
procedure TCustomForm.FrameChanged;
var
  FW, FH: Integer;
begin
  if HandleAllocated then
    RecreateWnd
  else
  begin
    GetFrame(FW, FH);
    SetBounds(Left, Top, FClientWidth + FW, FClientHeight + FH);
  end;
end;

{ layout.md UIF-272: the real client size wins; a change runs a layout pass }
procedure TCustomForm.AdoptClientSize;
var
  R: TRect;
begin
  if not HandleAllocated or IsIconic(Handle) or LayoutDumpMode then
    Exit;
  Windows.GetClientRect(Handle, R);
  if (R.Right <> FClientWidth) or (R.Bottom <> FClientHeight) then
  begin
    FClientWidth := R.Right;
    FClientHeight := R.Bottom;
    Realign;
    Resize;
  end;
end;

procedure TCustomForm.SetBounds(ALeft, ATop, AWidth, AHeight: Integer);
var
  FW, FH: Integer;
begin
  GetFrame(FW, FH);
  Inc(FResizeLock);
  try
    FClientWidth := AWidth - FW;                             { the layout pass inside sees the new client size }
    FClientHeight := AHeight - FH;
    inherited SetBounds(ALeft, ATop, AWidth, AHeight);
    FClientWidth := Width - FW;
    FClientHeight := Height - FH;
    AdoptClientSize;
  finally
    EndResizeLock;
  end;
end;

procedure TCustomForm.EndResizeLock;
begin
  Dec(FResizeLock);
  if (FResizeLock = 0) and FResizePending then
  begin
    FResizePending := False;
    Resize;
  end;
end;

procedure TCustomForm.Resize;
begin
  if FResizeLock > 0 then
  begin
    FResizePending := True;
    Exit;
  end;
  if not FShownOnce or ((FClientWidth = FResizedWidth) and (FClientHeight = FResizedHeight)) then
    Exit;
  FResizedWidth := FClientWidth;
  FResizedHeight := FClientHeight;
  inherited Resize;
end;

procedure TCustomForm.UpdateScrollBars;
begin
end;

function TCustomForm.GetClientRect: TRect;
begin
  Result := Rect(0, 0, FClientWidth, FClientHeight);
end;

{ the window has the outer size of the stored client size (CreateParams); then UIF-270 DPI and UIF-272 }
procedure TCustomForm.CreateWnd;
var
  R: TRect;
  FW, FH, DPI: Integer;
begin
  inherited CreateWnd;
  if LayoutDumpMode then
    Exit;                                                    { UIF-313: bounds stay formula-defined }
  DPI := GetDpiForWindow(Handle);
  if (DPI > 0) and (DPI <> FPixelsPerInch) then
  begin
    FPixelsPerInch := DPI;
    GetFrame(FW, FH);
    SetBounds(Left, Top, FClientWidth + FW, FClientHeight + FH);
  end;
  R := WindowBounds;
  if (R.Left <> Left) or (R.Top <> Top) or (R.Right <> Width) or (R.Bottom <> Height) then
    SetBounds(R.Left, R.Top, R.Right, R.Bottom);
  AdoptClientSize;
end;

procedure TCustomForm.WMWindowPosChanged(var Message: TWMWindowPosMsg);
begin
  Inc(FResizeLock);
  try
    AdoptClientSize;
    inherited WMWindowPosChanged(Message);
  finally
    EndResizeLock;
  end;
end;

procedure TCustomForm.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if Operation = opRemove then
  begin
    if AComponent = FActiveControl then
      FActiveControl := nil;
    if AComponent = FEnteredControl then
      FEnteredControl := nil;
  end;
end;

{ UIF-426: OnPaint with Canvas bound to the paint DC }
procedure TCustomForm.PaintWindow(DC: HDC);
begin
  TControlCanvas(FCanvas).FreeHandle;
  FCanvas.Handle := DC;
  try
    if Assigned(FOnPaint) then
      FOnPaint(Self);
  finally
    FCanvas.Handle := 0;
  end;
end;

procedure TCustomForm.DestroyWnd;
begin
  TControlCanvas(FCanvas).FreeHandle;
  inherited DestroyWnd;
end;

{ UIF-432 step 4: the ShowWindow command of WindowState; a non-main form of an inactive application does not
  take the activation }
procedure TCustomForm.ShowCommand(var Cmd: Integer);
begin
  case FWindowState of
    wsMaximized: Cmd := SW_SHOWMAXIMIZED;
    wsMinimized: Cmd := SW_SHOWMINIMIZED;
  else
    Cmd := SW_SHOWNORMAL;
  end;
  if (Cmd = SW_SHOWNORMAL) and (Application.FMainForm <> Self) and not Application.Active then
    Cmd := SW_SHOWNOACTIVATE;
end;

{ UIF-432 (first show), UIF-433 (later shows), UIF-434 (hide) }
procedure TCustomForm.CMVisibleChanged(var Message: TMessage);
var
  Cmd: Integer;
  First: Boolean;
begin
  if Visible then
  begin
    First := not FShownOnce;
    HandleNeeded;                                            { step 1 }
    if First then
      ApplyPosition;                                         { step 2 }
    Include(FFormState, fsShowing);                          { step 3 }
    try
      DoShow;
    finally
      Exclude(FFormState, fsShowing);
    end;
    if not Visible then
      Exit;                                                  { OnShow hid the form again }
    Include(FFormState, fsVisible);
    ShowCommand(Cmd);
    Inc(FResizeLock);                                        { the WM_SIZE of the show are coalesced }
    try
      ShowWindow(Handle, Cmd);                               { steps 4, 5 (WM_ACTIVATE) }
      AdoptClientSize;
    finally
      Dec(FResizeLock);
      FResizePending := False;
    end;
    if First then
    begin
      FShownOnce := True;                                    { step 6: OnResize exactly once }
      FResizedWidth := -1;
      Resize;
    end
    else
      Resize;                                                { only if the size changed meanwhile }
  end
  else
  begin
    if HandleAllocated then
      ShowWindow(Handle, SW_HIDE);                           { the active form gets its OnDeactivate first }
    if fsActivated in FFormState then
    begin
      Exclude(FFormState, fsActivated);                      { Win32 kept the hidden window active (no foreground) }
      Deactivate;
    end;
    if fsVisible in FFormState then
    begin
      Exclude(FFormState, fsVisible);
      DoHide;
    end;
  end;
end;

procedure TCustomForm.DoShow;
begin
  if Assigned(FOnShow) then
    FOnShow(Self);
end;

procedure TCustomForm.DoHide;
begin
  if Assigned(FOnHide) then
    FOnHide(Self);
end;

procedure TCustomForm.Activate;
begin
  if Assigned(FOnActivate) then
    FOnActivate(Self);
end;

procedure TCustomForm.Deactivate;
begin
  if Assigned(FOnDeactivate) then
    FOnDeactivate(Self);
end;

{ UIF-435 }
procedure TCustomForm.WMActivate(var Message: TMessage);
var
  C: TWinControl;
begin
  inherited WndProc(Message);                                { default processing focuses the window }
  if LoWord(Message.WParam) <> WA_INACTIVE then
  begin
    Include(FFormState, fsActivated);
    Screen.SetActiveForm(Self);
    Activate;
    { UIF-092: ActiveControl, else the first tab stop, else the form keeps the focus }
    C := FActiveControl;
    if (C = nil) or not C.CanFocus then
      C := FindNextControl(nil, True, True, False);
    if C <> nil then
    begin
      SetFocusedControl(C);
      Windows.SetFocus(C.Handle);
    end;
  end
  else
  begin
    C := FindControl(GetFocus);
    if (C <> nil) and (C <> Self) and ContainsControl(C) then
      FActiveControl := C;                                   { remembered without events (UIF-111) }
    if fsActivated in FFormState then
    begin
      Exclude(FFormState, fsActivated);
      Deactivate;
    end;
  end;
end;

procedure TCustomForm.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    WM_SETTINGCHANGE:
      begin
        Screen.UpdateSystemFonts;
        inherited WndProc(Message);
      end;
    WM_ACTIVATE:
      WMActivate(Message);
    WM_SIZE:
      WMSize(Message);
    WM_DPICHANGED:
      WMDpiChanged(Message);
    WM_CLOSE:
      Close;                                                 { UIF-440: default processing never destroys }
    WM_QUERYENDSESSION:
      Message.Result := LRESULT(Ord(CloseQuery));            { UIF-441 }
    WM_ENDSESSION:
      if Message.WParam <> 0 then
        Halt(0)
      else
        inherited WndProc(Message);
    CM_RELEASE:
      Free;
    CM_DIALOGKEY:
      CMDialogKey(Message);
    CM_DIALOGCHAR:
      Message.Result := Ord(OfferDialogChar(Self, Message));   { the main-menu fallback comes with menus }
    WM_ACTIVATEAPP:
      begin
        Application.FActive := Message.WParam <> 0;
        inherited WndProc(Message);
      end;
  else
    inherited WndProc(Message);
  end;
end;

function TCustomForm.PreviewKeys: Boolean;
begin
  Result := FKeyPreview;
end;

{ UIF-092: an active form focuses the control; otherwise (and while loading) the value is only recorded }
procedure TCustomForm.SetActiveControl(Value: TWinControl);
begin
  if FActiveControl = Value then
    Exit;
  if (Value <> nil) and not (csLoading in ComponentState) and Active and Value.CanFocus then
    Value.SetFocus
  else
  begin
    FActiveControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

{ UIF-091 for the form itself: activates the window }
procedure TCustomForm.SetFocus;
begin
  if not (Visible and Enabled) then
    raise EInvalidOperation.Create(SCannotFocus);
  if Application.Active then
    SetForegroundWindow(Handle)
  else
    SetActiveWindow(Handle);
end;

{ UIF-110 }
function TCustomForm.SetFocusedControl(Control: TWinControl): Boolean;
var
  Old, C: TWinControl;
  Serial, I: Integer;
  Enter: TList;
begin
  Result := True;
  if csDestroying in ComponentState then
    Exit;
  Old := FEnteredControl;
  if Old = Control then
  begin
    FActiveControl := Control;
    Exit;
  end;
  Inc(FFocusSerial);
  Serial := FFocusSerial;
  if not FFocusExiting then
  begin
    { exit: Old, then its ancestors that are not ancestors of Control (innermost first); a handler that moves the
      focus elsewhere abandons this change; the nested change skips this phase }
    FFocusExiting := True;
    try
      C := Old;
      while (C <> nil) and (C <> Self) do
      begin
        if (C = Old) or not IsAncestor(C, Control) then
        begin
          C.Perform(CM_EXIT, 0, 0);
          if FFocusSerial <> Serial then
          begin
            Result := False;
            Exit;
          end;
        end;
        C := C.Parent;
      end;
    finally
      FFocusExiting := False;
    end;
  end;
  FActiveControl := Control;
  FEnteredControl := Control;
  if Control <> nil then
  begin
    Control.FreeNotification(Self);
    { enter: Control's ancestors that are not ancestors of Old (outermost first), then Control }
    Enter := TList.Create;
    try
      C := Control.Parent;
      while (C <> nil) and (C <> Self) do
      begin
        if not IsAncestor(C, Old) then
          Enter.Add(Pointer(C));
        C := C.Parent;
      end;
      for I := Enter.Count - 1 downto 0 do
        TWinControl(Enter[I]).Perform(CM_ENTER, 0, 0);
    finally
      Enter.Free;
    end;
    Control.Perform(CM_ENTER, 0, 0);
  end;
  SendFocusChanged(Self, Control);
  if (Screen.FActiveCustomForm = Self) and Assigned(Screen.FOnActiveControlChange) then
    Screen.FOnActiveControlChange(Screen);
end;

function TCustomForm.IsShortCut(var Message: TWMKey): Boolean;
begin
  Result := False;
end;

{ UIF-115 step 3, Return/Escape: offered to every control of the tree, depth-first in z-order, until one answers }
function OfferDialogKey(Parent: TWinControl; var Message: TMessage): Boolean;
var
  I: Integer;
  C: TControl;
begin
  Result := False;
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    Message.Result := 0;
    C.WindowProc(Message);
    if Message.Result <> 0 then
    begin
      Result := True;
      Exit;
    end;
    if (C is TWinControl) and OfferDialogKey(TWinControl(C), Message) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ core.md UIF-115 step 3 table; Result <> 0 = consumed }
procedure TCustomForm.CMDialogKey(var Message: TMessage);
var
  C: TWinControl;
begin
  Message.Result := 0;
  case Message.WParam of
    VK_TAB:
      if GetKeyState(VK_CONTROL) >= 0 then
      begin
        SelectNext(FActiveControl, GetKeyState(VK_SHIFT) >= 0, True);
        Message.Result := 1;
      end;
    VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN:
      if (FActiveControl <> nil) and (FActiveControl.Parent <> nil) then
      begin
        C := FActiveControl.Parent.FindNextControl(FActiveControl,
          (Message.WParam = VK_RIGHT) or (Message.WParam = VK_DOWN), False, True);
        if C <> nil then
        begin
          C.SetFocus;
          { a radio button (its window answers DLGC_RADIOBUTTON) that receives focus this way becomes checked }
          if C.HandleAllocated and (FActiveControl = C)
            and ((SendMessageW(C.Handle, WM_GETDLGCODE, 0, 0) and DLGC_RADIOBUTTON) <> 0) then
            SendMessageW(C.Handle, BM_CLICK, 0, 0);
        end;
        Message.Result := 1;
      end;
    VK_RETURN, VK_ESCAPE, VK_CANCEL:
      if OfferDialogKey(Self, Message) then
        Message.Result := 1
      else
        Message.Result := 0;
  end;
end;

{ UIF-094 }
procedure TCustomForm.FocusControl(Control: TWinControl);
begin
  ActiveControl := Control;
end;

{ UIF-093 (disabled, hidden) and UIF-037 step 2 (Removing: destroyed, silently to the form) }
procedure TCustomForm.DefocusControl(Control: TWinControl; Removing: Boolean);
var
  Next: TWinControl;
  F: HWND;
begin
  if (FActiveControl = nil) or ((Control <> FActiveControl) and not Control.ContainsControl(FActiveControl)) then
    Exit;
  if Removing or (csDestroying in ComponentState) then
  begin
    F := GetFocus;
    FActiveControl := nil;
    if (FEnteredControl <> nil) and ((FEnteredControl = Control) or Control.ContainsControl(FEnteredControl)) then
      FEnteredControl := nil;
    if (F <> 0) and Control.HandleAllocated and ((F = Control.Handle) or IsChild(Control.Handle, F))
      and HandleAllocated and not (csDestroying in ComponentState) then
      Windows.SetFocus(Handle);
    Exit;
  end;
  Next := FindNextControl(Control, True, True, False);
  if Next <> nil then
    Next.SetFocus
  else
  begin
    SetFocusedControl(nil);
    if Active then
      Windows.SetFocus(Handle);
  end;
end;

procedure TCustomForm.SetBorderIcons(Value: TBorderIcons);
begin
  if FBorderIcons <> Value then
  begin
    FBorderIcons := Value;
    FrameChanged;
  end;
end;

procedure TCustomForm.SetBorderStyle(Value: TFormBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    FrameChanged;
  end;
end;

procedure TCustomForm.SetFormStyle(Value: TFormStyle);
begin
  if FFormStyle <> Value then
  begin
    FFormStyle := Value;
    FrameChanged;
  end;
end;

{ layout.md UIF-270: a stored value is the design DPI; at run time the property is read-only }
procedure TCustomForm.SetPixelsPerInch(Value: Integer);
begin
  if csLoading in ComponentState then
    FDesignPixelsPerInch := Value;
end;

procedure TCustomForm.ScaleBounds(M, D: Integer);
var
  FW, FH: Integer;
begin
  GetFrame(FW, FH);
  SetBounds(Left, Top, LayoutMulDiv(FClientWidth, M, D) + FW, LayoutMulDiv(FClientHeight, M, D) + FH);
end;

{ the children's Loaded (children first) captured their baselines before this scaling: they re-capture }
procedure TCustomForm.Loaded;
begin
  if FScaled and not LayoutDumpMode and (FPixelsPerInch <> FDesignPixelsPerInch) then
  begin
    ChangeScale(FPixelsPerInch, FDesignPixelsPerInch);
    CaptureBaselines;
  end;
  inherited Loaded;
end;

procedure TCustomForm.ConstrainedResize(var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer);
begin
  if LayoutDumpMode then
  begin
    MinWidth := 0;
    MinHeight := 0;
    MaxWidth := 0;
    MaxHeight := 0;
  end
  else
    inherited ConstrainedResize(MinWidth, MinHeight, MaxWidth, MaxHeight);
end;

procedure TCustomForm.ScaleForCurrentDpi;
begin
  ScaleBy(FPixelsPerInch, 96);
end;

{ CM_DPICHANGED (WParam new DPI, LParam old DPI) to every control of the tree, depth-first in z-order }
procedure SendDpiChanged(Parent: TWinControl; OldDPI, NewDPI: Integer);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    C.Perform(CM_DPICHANGED, WPARAM(NewDPI), LPARAM(OldDPI));
    if C is TWinControl then
      SendDpiChanged(TWinControl(C), OldDPI, NewDPI);
  end;
end;

{ UIF-285: the scaling and its UIF-283 run before the window takes the suggested rectangle, so the real client size
  is adopted like any later resize (anchors follow it) }
procedure TCustomForm.WMDpiChanged(var Message: TMessage);
var
  OldDPI, NewDPI: Integer;
  R: TRect;
begin
  Message.Result := 0;
  if LayoutDumpMode then
    Exit;                                                    { UIF-311 }
  OldDPI := FPixelsPerInch;
  NewDPI := HiWord(Message.WParam);
  R := PRect(Pointer(Message.LParam))^;
  if FScaled then
    ScaleBy(NewDPI, OldDPI);
  FPixelsPerInch := NewDPI;
  SetBounds(R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top);
  if FScaled then
    SendDpiChanged(Self, OldDPI, NewDPI);
  if Assigned(FOnAfterMonitorDpiChanged) then
    FOnAfterMonitorDpiChanged(Self, OldDPI, NewDPI);
  Message.Result := 0;
end;

{ layout.md UIF-275: a visible form takes the new state at once; a hidden one at its next show }
procedure TCustomForm.SetWindowState(Value: TWindowState);
const
  Commands: array[TWindowState] of Integer = (SW_SHOWNORMAL, SW_MINIMIZE, SW_MAXIMIZE);
begin
  if FWindowState = Value then
    Exit;
  FWindowState := Value;
  if HandleAllocated and (fsVisible in FFormState) then
    ShowWindow(Handle, Commands[Value]);
end;

{ a state change made by the system is read back }
procedure TCustomForm.WMSize(var Message: TMessage);
begin
  inherited WndProc(Message);
  if fsVisible in FFormState then
    case Message.WParam of
      SIZE_MAXIMIZED: FWindowState := wsMaximized;
      SIZE_MINIMIZED: FWindowState := wsMinimized;
      SIZE_RESTORED: FWindowState := wsNormal;
    end;
end;

function TCustomForm.GetActive: Boolean;
begin
  Result := fsActivated in FFormState;
end;

{ ---------------------------------------------------------------- TMonitor }

function TMonitor.Info: TMonitorInfo;
begin
  FillChar(Result, SizeOf(Result), 0);
  Result.cbSize := SizeOf(Result);
  GetMonitorInfoW(FHandle, Result);
end;

function TMonitor.GetBoundsRect: TRect;
begin
  Result := Info.rcMonitor;
end;

function TMonitor.GetWorkareaRect: TRect;
begin
  Result := Info.rcWork;
end;

function TMonitor.GetPrimary: Boolean;
begin
  Result := (Info.dwFlags and MONITORINFOF_PRIMARY) <> 0;
end;

{ application-forms.md 3: the DPI of a form window on this monitor, else the system DPI }
function TMonitor.GetPixelsPerInch: Integer;
var
  I: Integer;
  F: TCustomForm;
begin
  for I := 0 to Screen.CustomFormCount - 1 do
  begin
    F := Screen.CustomForms[I];
    if F.HandleAllocated and (MonitorFromWindow(F.Handle, MONITOR_DEFAULTTONULL) = FHandle) then
    begin
      Result := GetDpiForWindow(F.Handle);
      Exit;
    end;
  end;
  Result := GetDpiForSystem;
end;

{ ---------------------------------------------------------------- TScreen }

constructor TScreen.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FCustomForms := TList.Create;
  FForms := TList.Create;
  FMonitors := TList.Create;
  FIconFont := TFont.Create;
  FMenuFont := TFont.Create;
  FHintFont := TFont.Create;
  FMessageFont := TFont.Create;
  UpdateSystemFonts;
end;

destructor TScreen.Destroy;
begin
  ClearMonitors;
  FMonitors.Free;
  FFonts.Free;
  FIconFont.Free;
  FMenuFont.Free;
  FHintFont.Free;
  FMessageFont.Free;
  FForms.Free;
  FCustomForms.Free;
  inherited Destroy;
end;

procedure TScreen.AddForm(AForm: TCustomForm);
begin
  FCustomForms.Add(AForm);
  if AForm is TForm then
    FForms.Add(AForm);
end;

procedure TScreen.RemoveForm(AForm: TCustomForm);
begin
  FCustomForms.Remove(AForm);
  FForms.Remove(AForm);
  if FActiveCustomForm = AForm then
    FActiveCustomForm := nil;
  if FActiveForm = AForm then
    FActiveForm := nil;
  if FFocusedForm = AForm then
    FFocusedForm := nil;
end;

procedure TScreen.SetActiveForm(AForm: TCustomForm);
begin
  if FActiveCustomForm = AForm then
    Exit;
  FActiveCustomForm := AForm;
  if AForm is TForm then
    FActiveForm := TForm(AForm)
  else
    FActiveForm := nil;
  FFocusedForm := AForm;
  if Assigned(FOnActiveFormChange) then
    FOnActiveFormChange(Self);
end;

function TScreen.MonitorOf(H: HMONITOR): TMonitor;
var
  Pass, I: Integer;
begin
  for Pass := 0 to 1 do
  begin
    for I := 0 to MonitorCount - 1 do
      if Monitors[I].Handle = H then
      begin
        Result := Monitors[I];
        Exit;
      end;
    EnumMonitors;
  end;
  Result := Monitors[0];
end;

function TScreen.GetCustomForm(Index: Integer): TCustomForm;
begin
  Result := TCustomForm(FCustomForms[Index]);
end;

function TScreen.GetCustomFormCount: Integer;
begin
  Result := FCustomForms.Count;
end;

function TScreen.GetForm(Index: Integer): TForm;
begin
  Result := TForm(FForms[Index]);
end;

function TScreen.GetFormCount: Integer;
begin
  Result := FForms.Count;
end;

function TScreen.GetActiveControl: TWinControl;
begin
  Result := nil;
  if FActiveCustomForm <> nil then
    Result := FActiveCustomForm.FActiveControl;
end;

function TScreen.GetWidth: Integer;
begin
  Result := GetSystemMetrics(SM_CXSCREEN);
end;

function TScreen.GetHeight: Integer;
begin
  Result := GetSystemMetrics(SM_CYSCREEN);
end;

function TScreen.GetDesktopRect: TRect;
begin
  Result.Left := GetSystemMetrics(SM_XVIRTUALSCREEN);
  Result.Top := GetSystemMetrics(SM_YVIRTUALSCREEN);
  Result.Right := Result.Left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
  Result.Bottom := Result.Top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
end;

function TScreen.GetWorkAreaRect: TRect;
begin
  Result := Rect(0, 0, 0, 0);
  SystemParametersInfoW(SPI_GETWORKAREA, 0, @Result, 0);
end;

function TScreen.GetPixelsPerInch: Integer;
begin
  Result := GetDpiForSystem;
end;

{ applied at once; back to crDefault: the window under the mouse chooses again }
procedure TScreen.SetCursor(Value: TCursor);
var
  P: TPoint;
  H: HWND;
  Hit: LRESULT;
begin
  if FCursor = Value then
    Exit;
  FCursor := Value;
  Controls.ScreenCursor := Value;
  if Value <> crDefault then
    Windows.SetCursor(CursorHandle(Value))
  else
  begin
    GetCursorPos(P);
    H := WindowFromPoint(P);
    if (H <> 0) and (FindControl(H) <> nil) then
    begin
      Hit := SendMessageW(H, WM_NCHITTEST, 0, MakeLParam(Word(P.X), Word(P.Y)));
      SendMessageW(H, WM_SETCURSOR, WPARAM(H), MakeLParam(Word(Hit), WM_MOUSEMOVE));
    end;
  end;
end;

function EnumFontsProc(lpelfe: PLogFontW; lpntme: PTextMetricW; FontType: DWORD; lParam: LPARAM): Integer; stdcall;
begin
  TStrings(Pointer(lParam)).Add(string(PChar(@lpelfe^.lfFaceName[0])));
  Result := 1;
end;

function TScreen.GetFonts: TStrings;
var
  DC: HDC;
  LF: TLogFontW;
begin
  if FFonts = nil then
  begin
    FFonts := TStringList.Create;
    FFonts.Sorted := True;
    FFonts.Duplicates := dupIgnore;
    FillChar(LF, SizeOf(LF), 0);
    LF.lfCharSet := DEFAULT_CHARSET;
    DC := GetDC(0);
    try
      EnumFontFamiliesExW(DC, LF, EnumFontsProc, LPARAM(Pointer(FFonts)), 0);
    finally
      ReleaseDC(0, DC);
    end;
  end;
  Result := FFonts;
end;

procedure TScreen.ResetFonts;
begin
  FFonts.Free;
  FFonts := nil;
end;

function EnumMonitorsProc(hMonitor: HMONITOR; hdcMonitor: HDC; lprcMonitor: PRect; dwData: LPARAM): BOOL; stdcall;
var
  M: TMonitor;
begin
  M := TMonitor.Create;
  M.FHandle := hMonitor;
  TList(Pointer(dwData)).Add(M);
  Result := True;
end;

procedure TScreen.EnumMonitors;
begin
  ClearMonitors;
  EnumDisplayMonitors(0, nil, EnumMonitorsProc, LPARAM(Pointer(FMonitors)));
end;

procedure TScreen.ClearMonitors;
var
  I: Integer;
begin
  for I := 0 to FMonitors.Count - 1 do
    TMonitor(FMonitors[I]).Free;
  FMonitors.Clear;
end;

function TScreen.GetMonitorCount: Integer;
begin
  if FMonitors.Count = 0 then
    EnumMonitors;
  Result := FMonitors.Count;
end;

function TScreen.GetMonitor(Index: Integer): TMonitor;
begin
  if FMonitors.Count = 0 then
    EnumMonitors;
  Result := TMonitor(FMonitors[Index]);
end;

procedure SetSystemFont(Font: TFont; const LF: TLogFontW; DPI: Integer);
begin
  Font.PixelsPerInch := DPI;
  Font.Handle := CreateFontIndirectW(LF);
end;

procedure TScreen.UpdateSystemFonts;
var
  DPI: UINT;
  Metrics: TNonClientMetricsW;
  Icon: TLogFontW;
begin
  DPI := GetDpiForSystem;
  FillChar(Metrics, SizeOf(Metrics), 0);
  Metrics.cbSize := SizeOf(Metrics);
  if SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, SizeOf(Metrics), @Metrics, 0, DPI) then
  begin
    SetSystemFont(FMenuFont, Metrics.lfMenuFont, DPI);
    SetSystemFont(FHintFont, Metrics.lfStatusFont, DPI);
    SetSystemFont(FMessageFont, Metrics.lfMessageFont, DPI);
  end;
  FillChar(Icon, SizeOf(Icon), 0);
  if SystemParametersInfoForDpi(SPI_GETICONTITLELOGFONT, SizeOf(Icon), @Icon, 0, DPI) then
    SetSystemFont(FIconFont, Icon, DPI);
end;

{ UIF-400: Classes.FindGlobalComponent searches the forms by Name }
function FormsFindGlobalComponent(const Name: string): TComponent;
var
  I: Integer;
begin
  for I := 0 to Screen.CustomFormCount - 1 do
  begin
    Result := Screen.CustomForms[I];
    if SameText(Result.Name, Name) then
      Exit;
  end;
  Result := nil;
end;

{ ---------------------------------------------------------------- layout dump (layout.md section 13) }

function LayoutDumpString(const S: string): string;
const
  Hex = '0123456789ABCDEF';
var
  I: Integer;
  C: Char;
begin
  Result := '"';
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if C = '"' then
      Result := Result + '\"'
    else if C = '\' then
      Result := Result + '\\'
    else if Ord(C) < 32 then
      Result := Result + '\u00' + Hex[Ord(C) div 16 + 1] + Hex[Ord(C) mod 16 + 1]
    else
      Result := Result + C;
  end;
  Result := Result + '"';
end;

function DumpBool(B: Boolean): string;
begin
  if B then
    Result := 'true'
  else
    Result := 'false';
end;

{ UIF-322: the named controls of Parent, recursively; Indent = the indentation of the "controls" key }
procedure DumpControls(var S: string; Parent: TWinControl; const Indent: string);
var
  I, N: Integer;
  C: TControl;
  Pad: string;
begin
  N := 0;
  for I := 0 to Parent.ControlCount - 1 do
    if Parent.Controls[I].Name <> '' then
      Inc(N);
  if N = 0 then
  begin
    S := S + Indent + '"controls": []' + #10;
    Exit;
  end;
  S := S + Indent + '"controls": [' + #10;
  Pad := Indent + '    ';
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    if C.Name = '' then
      Continue;
    S := S + Indent + '  {' + #10
      + Pad + '"class": ' + LayoutDumpString(C.ClassName) + ',' + #10
      + Pad + '"name": ' + LayoutDumpString(C.Name) + ',' + #10
      + Pad + '"left": ' + IntToStr(C.Left) + ',' + #10
      + Pad + '"top": ' + IntToStr(C.Top) + ',' + #10
      + Pad + '"width": ' + IntToStr(C.Width) + ',' + #10
      + Pad + '"height": ' + IntToStr(C.Height) + ',' + #10
      + Pad + '"visible": ' + DumpBool(C.Visible) + ',' + #10;
    if C is TWinControl then
      DumpControls(S, TWinControl(C), Pad)
    else
      S := S + Pad + '"controls": []' + #10;
    Dec(N);
    if N > 0 then
      S := S + Indent + '  },' + #10
    else
      S := S + Indent + '  }' + #10;
  end;
  S := S + Indent + ']' + #10;
end;

procedure DumpForm(var S: string; F: TCustomForm; Last: Boolean);
begin
  S := S + '    {' + #10
    + '      "class": ' + LayoutDumpString(F.ClassName) + ',' + #10
    + '      "name": ' + LayoutDumpString(F.Name) + ',' + #10
    + '      "clientWidth": ' + IntToStr(F.ClientWidth) + ',' + #10
    + '      "clientHeight": ' + IntToStr(F.ClientHeight) + ',' + #10;
  DumpControls(S, F, '      ');
  if Last then
    S := S + '    }' + #10
  else
    S := S + '    },' + #10;
end;

function LayoutDumpText(AForm: TCustomForm): string;
var
  I: Integer;
begin
  Result := '{' + #10 + '  "formatVersion": 1,' + #10 + '  "dpi": 96,' + #10;
  if (AForm = nil) and (Screen.CustomFormCount = 0) then
    Result := Result + '  "forms": []' + #10
  else
  begin
    Result := Result + '  "forms": [' + #10;
    if AForm <> nil then
      DumpForm(Result, AForm, True)
    else
      for I := 0 to Screen.CustomFormCount - 1 do
        DumpForm(Result, Screen.CustomForms[I], I = Screen.CustomFormCount - 1);
    Result := Result + '  ]' + #10;
  end;
  Result := Result + '}' + #10;
end;

{ ---------------------------------------------------------------- TApplication }

{ UIF-405 }
procedure TApplication.CreateForm(InstanceClass: TComponentClass; var Reference);
var
  Instance: TComponent;
begin
  Initialize;
  Instance := TComponent(InstanceClass.NewInstance);
  TComponent(Reference) := Instance;
  try
    Instance.Create(Self);
    Instance.AfterConstruction;
  except
    TComponent(Reference) := nil;
    Instance.Free;
    raise;
  end;
  if (FMainForm = nil) and (Instance is TForm) then
    FMainForm := TForm(Instance);
end;

constructor TApplication.Create(AOwner: TComponent);
var
  I: Integer;
begin
  inherited Create(AOwner);
  FShowMainForm := True;
  FTitle := ChangeFileExt(ExtractFileName(ParamStr(0)), '');
  FNonInteractive := GetEnvironmentVariable('FXC_UI_NONINTERACTIVE') = '1';
  { layout.md UIF-310 }
  for I := 1 to ParamCount - 1 do
    if ParamStr(I) = '--dump-layout' then
    begin
      FDumpLayoutFile := ParamStr(I + 1);
      Break;
    end;
  if FDumpLayoutFile <> '' then
  begin
    LayoutDumpMode := True;
    FNonInteractive := True;                                 { UIF-314 }
  end;
end;

function TApplication.GetExeName: string;
begin
  Result := ParamStr(0);
end;

{ UIF-465: an exception while reporting goes to stderr in every mode }
procedure WriteErrorLine(const S: string);
begin
  WriteLn(ErrOutput, S);
end;

procedure TApplication.HandleException(Sender: TObject);
var
  E: TObject;
  Wrapper: Exception;
begin
  E := ExceptObject;
  if E is EAbort then
    Exit;
  Wrapper := nil;
  try
    try
      if not (E is Exception) then
      begin
        Wrapper := Exception.Create(E.ClassName);
        E := Wrapper;
      end;
      if Assigned(FOnException) then
        FOnException(Sender, Exception(E))
      else
        ShowException(Exception(E));
    except
      if ExceptObject is Exception then
        WriteErrorLine(FTitle + ': ' + ExceptObject.ClassName + ': ' + Exception(ExceptObject).Message)
      else
        WriteErrorLine(FTitle + ': ' + ExceptObject.ClassName);
    end;
  finally
    Wrapper.Free;
  end;
end;

procedure TApplication.ShowException(E: Exception);
var
  S: string;
begin
  if FNonInteractive then
  begin
    WriteErrorLine(FTitle + ': ' + E.ClassName + ': ' + E.Message);
    if ExitCode = 0 then
      ExitCode := 1;
    Exit;
  end;
  S := E.Message;
  if (S = '') or ((S[Length(S)] <> '.') and (S[Length(S)] <> '!') and (S[Length(S)] <> '?')) then
    S := S + '.';
  MessageBoxW(0, PChar(S), PChar(FTitle), MB_OK or MB_ICONSTOP or MB_TASKMODAL);
end;

{ UIF-471 }
procedure FormsShowError(const Text: string);
begin
  if Application.FNonInteractive then
    WriteErrorLine(Text)
  else
    MessageBoxW(0, PChar(Text), PChar(Application.FTitle), MB_OK or MB_ICONERROR or MB_TASKMODAL);
end;

destructor TApplication.Destroy;
begin
  if FHandle <> 0 then
    DestroyWindow(FHandle);
  FHandle := 0;
  inherited Destroy;
end;

procedure TApplication.Initialize;
var
  Controls: TInitCommonControlsEx;
  Info: TWndClassExW;
begin
  if FInitialized then
    Exit;
  FInitialized := True;
  { step 1 }
  if not AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) then
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
  { step 2 }
  if not CommonControls6Active then
    ActivateCommonControls6;
  Controls.dwSize := SizeOf(Controls);
  Controls.dwICC := ICC_WIN95_CLASSES or ICC_STANDARD_CLASSES or ICC_BAR_CLASSES or ICC_TAB_CLASSES
    or ICC_LISTVIEW_CLASSES or ICC_TREEVIEW_CLASSES or ICC_PROGRESS_CLASS or ICC_UPDOWN_CLASS;
  InitCommonControlsEx(Controls);
  { step 3 }
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  if not GetClassInfoExW(HINST(HInstance), AppClassName, Info) then
  begin
    Info.cbSize := SizeOf(Info);
    Info.lpfnWndProc := @AppWndProc;
    Info.hInstance := HINST(HInstance);
    Info.lpszClassName := AppClassName;
    if RegisterClassExW(Info) = 0 then
      raise EOutOfResources.CreateFmt(SWindowClass, [SysErrorMessage(GetLastError)]);
  end;
  FHandle := CreateWindowExW(0, AppClassName, '', 0, 0, 0, 0, 0, HWND_MESSAGE, 0, HINST(HInstance), nil);
  if FHandle = 0 then
    raise EOutOfResources.CreateFmt(SWindowCreate, [SysErrorMessage(GetLastError)]);
end;

function TApplication.WriteLayoutDump: Boolean;
var
  Stream: TFileStream;
  Bytes: TBytes;
begin
  Result := True;
  try
    Bytes := BytesOf(LayoutDumpText(nil));
    Stream := TFileStream.Create(FDumpLayoutFile, fmCreate);
    try
      if Length(Bytes) > 0 then
        Stream.WriteBuffer(Bytes[0], Length(Bytes));
    finally
      Stream.Free;
    end;
  except
    on E: Exception do
    begin
      WriteErrorLine(Format(SLayoutDumpError, [E.Message]));
      Result := False;
    end;
  end;
end;

procedure TApplication.Run;
begin
  Initialize;
  { UIF-312: no form is shown, no loop runs }
  if FDumpLayoutFile <> '' then
  begin
    if WriteLayoutDump then
      Halt(0)
    else
      Halt(2);
  end;
  if FMainForm = nil then
    Exit;
  if FShowMainForm then
    FMainForm.Visible := True;
  repeat
    HandleMessage;
  until FTerminated;
end;

procedure TApplication.Terminate;
begin
  PostQuitMessage(0);
end;

{ core.md UIF-115: shortcuts and navigation keys of a form's windows; True = consumed }
function TApplication.IsKeyMsg(var Msg: TMsg): Boolean;
var
  W: HWND;
  C: TWinControl;
  F: TCustomForm;
  Key: Word;
  Code: LRESULT;
  Message: TWMKey;
begin
  Result := False;
  if (Msg.message <> WM_KEYDOWN) and (Msg.message <> WM_SYSKEYDOWN) and (Msg.message <> WM_CHAR)
    and (Msg.message <> WM_SYSCHAR) then
    Exit;
  W := Msg.hwnd;
  C := nil;
  while (W <> 0) and (C = nil) do
  begin
    C := FindControl(W);
    if C = nil then
      W := GetParent(W);
  end;
  if C = nil then
    Exit;
  F := GetParentForm(C);
  if F = nil then
    Exit;
  Key := Word(Msg.wParam);
  { UIF-120: accelerator characters }
  if (Msg.message = WM_CHAR) or (Msg.message = WM_SYSCHAR) then
  begin
    if (Msg.message = WM_SYSCHAR) or ((SendMessageW(Msg.hwnd, WM_GETDLGCODE, Key, LPARAM(@Msg))
      and (DLGC_WANTCHARS or DLGC_WANTALLKEYS)) = 0) then
      Result := F.Perform(CM_DIALOGCHAR, Key, Msg.lParam) <> 0;
    Exit;
  end;
  { step 1 }
  Message.Msg := Msg.message;
  Message.CharCode := Key;
  Message.KeyData := Msg.lParam;
  Message.Result := 0;
  if F.IsShortCut(Message) then
  begin
    Result := True;
    Exit;
  end;
  { step 2 }
  if ((Key = VK_TAB) or (Key = VK_LEFT) or (Key = VK_RIGHT) or (Key = VK_UP) or (Key = VK_DOWN)
    or (Key = VK_RETURN) or (Key = VK_ESCAPE) or (Key = VK_CANCEL)) and ((Msg.lParam and $20000000) = 0) then
  begin
    if C.Perform(CM_WANTSPECIALKEY, Key, 0) <> 0 then
      Exit;
    Code := SendMessageW(Msg.hwnd, WM_GETDLGCODE, Key, LPARAM(@Msg));
    if (Code and DLGC_WANTALLKEYS) <> 0 then
      Exit;
    if (Key = VK_TAB) and ((Code and DLGC_WANTTAB) <> 0) and (GetKeyState(VK_CONTROL) >= 0) then
      Exit;
    if ((Key = VK_LEFT) or (Key = VK_RIGHT) or (Key = VK_UP) or (Key = VK_DOWN))
      and ((Code and DLGC_WANTARROWS) <> 0) then
      Exit;
  end;
  { step 3 }
  Result := F.Perform(CM_DIALOGKEY, Key, Msg.lParam) <> 0;
end;

function TApplication.ProcessMessage(var Msg: TMsg): Boolean;
var
  Handled: Boolean;
begin
  Result := PeekMessageW(Msg, 0, 0, 0, PM_REMOVE);
  if not Result then
    Exit;
  if Msg.message = WM_QUIT then
  begin
    FTerminated := True;
    Exit;
  end;
  Handled := False;
  if Assigned(FOnMessage) then
    FOnMessage(Msg, Handled);
  if Handled or (Msg.hwnd = 0) then
    Exit;
  { Escape while a control holds the mouse capture cancels that mode (a splitter drag, UIF-763) }
  if (Msg.message = WM_KEYDOWN) and (Msg.wParam = VK_ESCAPE) and (GetCaptureControl <> nil) then
  begin
    GetCaptureControl.Perform(WM_CANCELMODE, 0, 0);
    Exit;
  end;
  if IsKeyMsg(Msg) then
    Exit;
  TranslateMessage(Msg);
  DispatchMessageW(Msg);
end;

procedure TApplication.ProcessMessages;
var
  Msg: TMsg;
begin
  while ProcessMessage(Msg) do
    ;
end;

procedure TApplication.HandleMessage;
var
  Msg: TMsg;
begin
  if not ProcessMessage(Msg) then
    Idle(Msg);
end;

procedure TApplication.Idle(const Msg: TMsg);
var
  Done: Boolean;
begin
  Done := True;
  if Assigned(FOnIdle) then
    try
      FOnIdle(Self, Done);
    except
      HandleException(Self);                                 { UIF-463 }
    end;
  { step 4 }
  if CheckSynchronize(0) then
    Done := False;
  if Done then
    WaitMessage;
end;

procedure TApplication.WakeMainThread(Sender: TObject);
begin
  if FHandle <> 0 then
    PostMessageW(FHandle, WM_NULL, 0, 0);
end;

{ UIF-400: the remaining forms in reverse creation order, then Screen, then Application }
procedure DestroyGlobals;
var
  I: Integer;
begin
  FindGlobalComponent := nil;
  Classes.WakeMainThread := nil;
  for I := Screen.CustomFormCount - 1 downto 0 do
    if I < Screen.CustomFormCount then
      Screen.CustomForms[I].Free;
  Screen.Free;
  Screen := nil;
  ApplicationHandleException := nil;
  ShowErrorProc := nil;
  Application.Free;
  Application := nil;
end;

initialization
  Application := TApplication.Create(nil);
  Screen := TScreen.Create(nil);
  ShowErrorProc := FormsShowError;
  ApplicationHandleException := Application.HandleException;
  FindGlobalComponent := FormsFindGlobalComponent;
  Classes.WakeMainThread := Application.WakeMainThread;
  FocusRootClass := TCustomForm;
  RegisterClasses([TForm, TScrollBox]);

finalization
  DestroyGlobals;
end.
