{ Controls - the control object model (core.md) and the run-time layout engine (layout.md). }
unit Controls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, ActnList, Menus, UIConsts;

{ ---------------------------------------------------------------- core.md section 1: types and constants }

type
  TAlign = (alNone, alTop, alBottom, alLeft, alRight, alClient);
  TAnchorKind = (akLeft, akTop, akRight, akBottom);
  TAnchors = set of TAnchorKind;
  TMouseButton = (mbLeft, mbRight, mbMiddle);
  TCursor = -32768..32767;
  TModalResult = Low(Integer)..High(Integer);
  TCaption = type string;
  { the form border styles live here so that bsNone/bsSingle name one enumeration in units using Controls and Forms;
    TBorderStyle is its subrange }
  TFormBorderStyle = (bsNone, bsSingle, bsSizeable, bsDialog, bsToolWindow, bsSizeToolWin);
  TBorderStyle = bsNone..bsSingle;
  TBevelCut = (bvNone, bvLowered, bvRaised, bvSpace);
  { TScrollBar (StdCtrls) and TControlScrollBar (Forms) }
  TScrollBarKind = (sbHorizontal, sbVertical);
  TControlStyle = set of (csAcceptsControls, csCaptureMouse, csClickEvents, csSetCaption, csOpaque,
    csDoubleClicks, csFixedWidth, csFixedHeight, csNoStdEvents, csReflector, csActionClient, csMenuEvents,
    csParentBackground);
  TControlState = set of (csLButtonDown, csClicked, csReadingState, csAlignmentNeeded, csFocusing, csCreating,
    csCustomPaint, csDestroyingHandle);
  TMouseEvent = procedure(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer) of object;
  TMouseMoveEvent = procedure(Sender: TObject; Shift: TShiftState; X, Y: Integer) of object;
  TMouseWheelEvent = procedure(Sender: TObject; Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint;
    var Handled: Boolean) of object;
  TMouseWheelUpDownEvent = procedure(Sender: TObject; Shift: TShiftState; MousePos: TPoint;
    var Handled: Boolean) of object;
  TKeyEvent = procedure(Sender: TObject; var Key: Word; Shift: TShiftState) of object;
  TKeyPressEvent = procedure(Sender: TObject; var Key: Char) of object;
  TContextPopupEvent = procedure(Sender: TObject; MousePos: TPoint; var Handled: Boolean) of object;
  TConstrainedResizeEvent = procedure(Sender: TObject; var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer)
    of object;
  TWndMethod = procedure(var Message: TMessage) of object;
  TCreateParams = record
    Caption: PChar;
    Style: Cardinal;
    ExStyle: Cardinal;
    X, Y, Width, Height: Integer;
    WndParent: HWND;
    Param: Pointer;
    WindowClass: TWndClassExW;
    WinClassName: array[0..63] of Char;
    SubclassOf: PChar;                                       { nil = our own window class }
  end;

const
  mrNone = 0;
  mrOk = 1;
  mrCancel = 2;
  mrAbort = 3;
  mrRetry = 4;
  mrIgnore = 5;
  mrYes = 6;
  mrNo = 7;
  mrClose = 8;
  mrHelp = 9;
  mrTryAgain = 10;
  mrContinue = 11;
  mrAll = 12;
  mrNoToAll = 13;
  mrYesToAll = 14;

  crDefault = TCursor(0);
  crNone = TCursor(-1);
  crArrow = TCursor(-2);
  crCross = TCursor(-3);
  crIBeam = TCursor(-4);
  crSizeNESW = TCursor(-6);
  crSizeNS = TCursor(-7);
  crSizeNWSE = TCursor(-8);
  crSizeWE = TCursor(-9);
  crUpArrow = TCursor(-10);
  crHourGlass = TCursor(-11);
  crDrag = TCursor(-12);
  crNoDrop = TCursor(-13);
  crHSplit = TCursor(-14);
  crVSplit = TCursor(-15);
  crNo = TCursor(-18);
  crAppStart = TCursor(-19);
  crHelp = TCursor(-20);
  crHandPoint = TCursor(-21);
  crSizeAll = TCursor(-22);

  { UIF-022: component messages (Perform only) and reflected notifications }
  CM_BASE = $B000;
  CN_BASE = $B800;
  CM_ENABLEDCHANGED = CM_BASE + 1;
  CM_VISIBLECHANGED = CM_BASE + 2;
  CM_COLORCHANGED = CM_BASE + 3;
  CM_FONTCHANGED = CM_BASE + 4;
  CM_TEXTCHANGED = CM_BASE + 5;
  CM_PARENTCOLORCHANGED = CM_BASE + 6;
  CM_PARENTFONTCHANGED = CM_BASE + 7;
  CM_PARENTSHOWHINTCHANGED = CM_BASE + 8;
  CM_CURSORCHANGED = CM_BASE + 9;
  CM_MOUSEENTER = CM_BASE + 10;
  CM_MOUSELEAVE = CM_BASE + 11;
  CM_ENTER = CM_BASE + 12;
  CM_EXIT = CM_BASE + 13;
  CM_DIALOGKEY = CM_BASE + 14;
  CM_DIALOGCHAR = CM_BASE + 15;
  CM_WANTSPECIALKEY = CM_BASE + 16;
  CM_SHOWINGCHANGED = CM_BASE + 17;
  CM_RELEASE = CM_BASE + 18;
  CM_CANCELMODE = CM_BASE + 19;
  CM_HINTSHOW = CM_BASE + 20;
  CM_CHANGED = CM_BASE + 21;
  CM_BORDERCHANGED = CM_BASE + 22;
  CM_FOCUSCHANGED = CM_BASE + 23;
  CM_GETDATALINK = CM_BASE + 24;
  CM_ACTIONUPDATE = CM_BASE + 25;
  CM_ACTIONEXECUTE = CM_BASE + 26;
  CM_DPICHANGED = CM_BASE + 27;

  CN_COMMAND = CN_BASE + WM_COMMAND;
  CN_NOTIFY = CN_BASE + WM_NOTIFY;
  CN_HSCROLL = CN_BASE + WM_HSCROLL;
  CN_VSCROLL = CN_BASE + WM_VSCROLL;
  CN_DRAWITEM = CN_BASE + WM_DRAWITEM;
  CN_MEASUREITEM = CN_BASE + WM_MEASUREITEM;
  CN_CTLCOLORBTN = CN_BASE + WM_CTLCOLORBTN;
  CN_CTLCOLOREDIT = CN_BASE + WM_CTLCOLOREDIT;
  CN_CTLCOLORLISTBOX = CN_BASE + WM_CTLCOLORLISTBOX;
  CN_CTLCOLORSTATIC = CN_BASE + WM_CTLCOLORSTATIC;
  CN_CTLCOLORSCROLLBAR = CN_BASE + WM_CTLCOLORSCROLLBAR;

{ ---------------------------------------------------------------- core.md section 12: helper classes }

type
  { 0 = no constraint; each value 0..32767 }
  TSizeConstraints = class(TPersistent)
  private
    FMaxHeight: Integer;
    FMaxWidth: Integer;
    FMinHeight: Integer;
    FMinWidth: Integer;
    FOnChange: TNotifyEvent;
    procedure SetValue(var Field: Integer; Value: Integer);
    procedure SetMaxHeight(Value: Integer);
    procedure SetMaxWidth(Value: Integer);
    procedure SetMinHeight(Value: Integer);
    procedure SetMinWidth(Value: Integer);
  protected
    procedure Change; virtual;
    procedure AssignTo(Dest: TPersistent); override;
  public
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  published
    property MaxHeight: Integer read FMaxHeight write SetMaxHeight default 0;
    property MaxWidth: Integer read FMaxWidth write SetMaxWidth default 0;
    property MinHeight: Integer read FMinHeight write SetMinHeight default 0;
    property MinWidth: Integer read FMinWidth write SetMinWidth default 0;
  end;

  TMargins = class(TPersistent)
  private
    FLeft: Integer;
    FTop: Integer;
    FRight: Integer;
    FBottom: Integer;
    FOnChange: TNotifyEvent;
    procedure SetLeft(Value: Integer);
    procedure SetTop(Value: Integer);
    procedure SetRight(Value: Integer);
    procedure SetBottom(Value: Integer);
  protected
    procedure Change; virtual;
    procedure AssignTo(Dest: TPersistent); override;
    class function DefaultValue: Integer; virtual;
  public
    constructor Create; virtual;
    procedure SetBounds(ALeft, ATop, ARight, ABottom: Integer);
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  published
    property Left: Integer read FLeft write SetLeft default 3;
    property Top: Integer read FTop write SetTop default 3;
    property Right: Integer read FRight write SetRight default 3;
    property Bottom: Integer read FBottom write SetBottom default 3;
  end;

  TPadding = class(TMargins)
  protected
    class function DefaultValue: Integer; override;
  published
    property Left default 0;
    property Top default 0;
    property Right default 0;
    property Bottom default 0;
  end;

{ ---------------------------------------------------------------- core.md section 2: TControl }

  TWinControl = class;
  TControl = class;

  { actions-menus.md section 1.2: Caption, Enabled, Hint, Visible and OnClick of a control }
  TControlActionLink = class(TActionLink)
  protected
    FClient: TControl;
    procedure AssignClient(AClient: TObject); override;
    function IsCaptionLinked: Boolean; override;
    function IsEnabledLinked: Boolean; override;
    function IsHintLinked: Boolean; override;
    function IsVisibleLinked: Boolean; override;
    function IsOnExecuteLinked: Boolean; override;
    procedure SetCaption(const Value: string); override;
    procedure SetEnabled(Value: Boolean); override;
    procedure SetHint(const Value: string); override;
    procedure SetVisible(Value: Boolean); override;
    procedure SetOnExecute(Value: TNotifyEvent); override;
  end;
  TControlActionLinkClass = class of TControlActionLink;

  TControl = class(TComponent)
  private
    FParent: TWinControl;
    FLeft: Integer;
    FTop: Integer;
    FWidth: Integer;
    FHeight: Integer;
    FAlign: TAlign;
    FAnchors: TAnchors;
    FAlignWithMargins: Boolean;
    FMargins: TMargins;
    FConstraints: TSizeConstraints;
    FCursor: TCursor;
    FEnabled: Boolean;
    FVisible: Boolean;
    FHint: string;
    FShowHint: Boolean;
    FParentShowHint: Boolean;
    FText: string;
    FColor: TColor;
    FFont: TFont;
    FParentColor: Boolean;
    FParentFont: Boolean;
    FAutoSize: Boolean;
    FControlStyle: TControlStyle;
    FControlState: TControlState;
    FWindowProc: TWndMethod;
    FLayoutCall: Boolean;                                    { next SetBounds comes from a layout pass or scaling }
    FFontFromParent: Boolean;                                { Font.Assign(Parent.Font) in progress: ParentFont stays }
    FBaseBounds: TRect;                                      { anchor baseline (BL, BT, BW, BH) as Left, Top, Right, Bottom }
    FBaseParentWidth: Integer;                               { (BPW, BPH), layout.md UIF-230 }
    FBaseParentHeight: Integer;
    FOnClick: TNotifyEvent;
    FOnDblClick: TNotifyEvent;
    FOnMouseDown: TMouseEvent;
    FOnMouseUp: TMouseEvent;
    FOnMouseMove: TMouseMoveEvent;
    FOnMouseEnter: TNotifyEvent;
    FOnMouseLeave: TNotifyEvent;
    FOnMouseWheel: TMouseWheelEvent;
    FOnMouseWheelDown: TMouseWheelUpDownEvent;
    FOnMouseWheelUp: TMouseWheelUpDownEvent;
    FOnContextPopup: TContextPopupEvent;
    FPopupMenu: TPopupMenu;
    FActionLink: TControlActionLink;
    FOnResize: TNotifyEvent;
    FOnConstrainedResize: TConstrainedResizeEvent;
    procedure SetLeft(Value: Integer);
    procedure SetTop(Value: Integer);
    procedure SetWidth(Value: Integer);
    procedure SetHeight(Value: Integer);
    function GetBoundsRect: TRect;
    procedure SetBoundsRect(const Value: TRect);
    function GetClientWidth: Integer;
    function GetClientHeight: Integer;
    procedure SetClientWidth(Value: Integer);
    procedure SetClientHeight(Value: Integer);
    procedure SetAlign(Value: TAlign);
    procedure SetAnchors(Value: TAnchors);
    procedure SetAlignWithMargins(Value: Boolean);
    procedure SetMargins(Value: TMargins);
    procedure SetConstraints(Value: TSizeConstraints);
    procedure SetCursor(Value: TCursor);
    procedure SetEnabled(Value: Boolean);
    procedure SetVisible(Value: Boolean);
    procedure SetShowHint(Value: Boolean);
    procedure SetPopupMenu(Value: TPopupMenu);
    procedure SetParentShowHint(Value: Boolean);
    function IsShowHintStored: Boolean;
    function GetCaption: TCaption;
    procedure SetCaption(const Value: TCaption);
    procedure SetColor(Value: TColor);
    function IsColorStored: Boolean;
    procedure SetFont(Value: TFont);
    function IsFontStored: Boolean;
    procedure SetParentColor(Value: Boolean);
    procedure SetParentFont(Value: Boolean);
    procedure SetAutoSize(Value: Boolean);
    procedure FontChanged(Sender: TObject);
    procedure MarginsChanged(Sender: TObject);
    procedure ConstraintsChanged(Sender: TObject);
    procedure CaptureBaseline;
    procedure UpdateBoundsFromOS(const R: TRect);
    procedure ShowHintChanged;
    procedure MouseMessage(var Message: TMessage);
    procedure ClearMouseState;
    procedure TrackMouseControl;
    function GetAction: TBasicAction;
    procedure SetAction(Value: TBasicAction);
  protected
    { actions-menus.md UIF-906: copies the action's values (CheckDefaults: only into properties at their default) }
    procedure ActionChange(Sender: TObject; CheckDefaults: Boolean); virtual;
    function GetActionLinkClass: TControlActionLinkClass; virtual;
    property ActionLink: TControlActionLink read FActionLink;
    { actions-menus.md section 1.2 (published by TButton, TCheckBox, TRadioButton) }
    property Action: TBasicAction read GetAction write SetAction;
    { actions-menus.md UIF-910 (c): the form's idle update reaches this control's action also while it is not
      Showing (tool buttons); base False }
    function UpdatesActionWhenHidden: Boolean; virtual;
    procedure SetParent(AParent: TWinControl); virtual;
    function GetShowing: Boolean; virtual;
    function GetClientRect: TRect; virtual;
    function GetText: TCaption; virtual;
    procedure SetText(const Value: TCaption); virtual;
    { core.md UIF-1250: Name->text copy under csSetCaption }
    procedure SetName(const NewName: TComponentName); override;
    procedure Click; virtual;
    procedure DblClick; virtual;
    procedure MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); virtual;
    procedure MouseMove(Shift: TShiftState; X, Y: Integer); virtual;
    procedure MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); virtual;
    { core.md 7.6: True = handled (OnMouseWheel, then OnMouseWheelUp/OnMouseWheelDown) }
    function DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean; virtual;
    function DoMouseWheelDown(Shift: TShiftState; MousePos: TPoint): Boolean; virtual;
    function DoMouseWheelUp(Shift: TShiftState; MousePos: TPoint): Boolean; virtual;
    { core.md 7.7: fires OnContextPopup at a client position }
    procedure DoContextPopup(MousePos: TPoint; var Handled: Boolean); virtual;
    procedure Resize; virtual;
    procedure AdjustSize; virtual;
    function CanAutoSize(var NewWidth, NewHeight: Integer): Boolean; virtual;
    { layout.md UIF-255, UIF-260, UIF-263: measured auto-size may run (AutoSize on; not loading, destroying,
      scaling or in dump mode) }
    function MeasuringAllowed: Boolean;
    { core.md WM_SETCURSOR: the cursor the control shows while its Cursor is crDefault; crDefault = the parent's
      (base); a splitter answers by its alignment (controls-standard.md UIF-760) }
    function DefaultCursor: TCursor; virtual;
    function CanResize(var NewWidth, NewHeight: Integer): Boolean; virtual;
    procedure ConstrainedResize(var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer); virtual;
    { layout.md UIF-250: intrinsic rules I1-I4 of the class (ConstrainSize step 1); base: none }
    procedure IntrinsicSize(var AWidth, AHeight: Integer); virtual;
    procedure RequestAlign; virtual;
    { UIF-210 step 6: the visual update after the fields changed (graphic controls invalidate; windowed: UIF-211) }
    procedure BoundsChanged(const OldBounds: TRect); virtual;
    procedure WndProc(var Message: TMessage); virtual;
    procedure CMEnabledChanged(var Message: TMessage); virtual;
    procedure CMVisibleChanged(var Message: TMessage); virtual;
    procedure CMColorChanged(var Message: TMessage); virtual;
    procedure CMFontChanged(var Message: TMessage); virtual;
    procedure CMParentColorChanged(var Message: TMessage); virtual;
    procedure CMParentFontChanged(var Message: TMessage); virtual;
    procedure CMParentShowHintChanged(var Message: TMessage); virtual;
    { core.md 1.1: the control under the mouse (or one of its descendants is) shows the new cursor at once }
    procedure CMCursorChanged(var Message: TMessage); virtual;
    procedure CMMouseEnter(var Message: TMessage); virtual;
    procedure CMMouseLeave(var Message: TMessage); virtual;
    procedure WMMouseWheel(var Message: TMessage); virtual;
    procedure WMContextMenu(var Message: TMessage); virtual;
    { core.md UIF-077: PopupMenu (AutoPopup) shown at a screen position for this control; False if none }
    function ShowPopupMenu(const ScreenPos: TPoint): Boolean;
    { core.md UIF-050 }
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure Loaded; override;
    procedure SetParentComponent(Value: TComponent); override;
    { layout.md UIF-280: constraints, margins, own font, then the bounds (ScaleBounds); M = D is a no-op }
    procedure ChangeScale(M, D: Integer); virtual;
    { UIF-280 steps 1 and 5: the bounds by edges, applied flagged "from scaling" (forms: the client size) }
    procedure ScaleBounds(M, D: Integer); virtual;
    property Caption: TCaption read GetCaption write SetCaption;
    property Text: TCaption read GetCaption write SetCaption;
    property Color: TColor read FColor write SetColor stored IsColorStored default clWindow;
    property Font: TFont read FFont write SetFont stored IsFontStored;
    property ParentColor: Boolean read FParentColor write SetParentColor default True;
    property ParentFont: Boolean read FParentFont write SetParentFont default True;
    property AutoSize: Boolean read FAutoSize write SetAutoSize default False;
    property OnClick: TNotifyEvent read FOnClick write FOnClick;
    property OnDblClick: TNotifyEvent read FOnDblClick write FOnDblClick;
    property OnMouseDown: TMouseEvent read FOnMouseDown write FOnMouseDown;
    property OnMouseUp: TMouseEvent read FOnMouseUp write FOnMouseUp;
    property OnMouseMove: TMouseMoveEvent read FOnMouseMove write FOnMouseMove;
    property OnMouseEnter: TNotifyEvent read FOnMouseEnter write FOnMouseEnter;
    property OnMouseLeave: TNotifyEvent read FOnMouseLeave write FOnMouseLeave;
    property OnMouseWheel: TMouseWheelEvent read FOnMouseWheel write FOnMouseWheel;
    property OnMouseWheelDown: TMouseWheelUpDownEvent read FOnMouseWheelDown write FOnMouseWheelDown;
    property OnMouseWheelUp: TMouseWheelUpDownEvent read FOnMouseWheelUp write FOnMouseWheelUp;
    property OnContextPopup: TContextPopupEvent read FOnContextPopup write FOnContextPopup;
    property OnResize: TNotifyEvent read FOnResize write FOnResize;
    property OnConstrainedResize: TConstrainedResizeEvent read FOnConstrainedResize write FOnConstrainedResize;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure SetBounds(ALeft, ATop, AWidth, AHeight: Integer); virtual;
    { layout.md UIF-240 }
    procedure ConstrainSize(var AWidth, AHeight: Integer);
    { layout.md UIF-282: ChangeScale + UIF-283 (baselines re-captured top-down, then the realign) }
    procedure ScaleBy(M, D: Integer);
    procedure BringToFront;
    procedure SendToBack;
    procedure Show;
    procedure Hide;
    procedure Invalidate; virtual;
    procedure Repaint; virtual;
    procedure Update; virtual;
    procedure Refresh;
    procedure Changed;
    function Perform(Msg: Cardinal; WParam: WPARAM; LParam: LPARAM): LRESULT;
    procedure DefaultHandler(var Message); override;
    function ClientToParent(const Point: TPoint; AParent: TWinControl = nil): TPoint;
    function ParentToClient(const Point: TPoint; AParent: TWinControl = nil): TPoint;
    function GetTextLen: Integer;
    function GetTextBuf(Buffer: PChar; BufSize: Integer): Integer;
    procedure SetTextBuf(Buffer: PChar);
    function HasParent: Boolean; override;
    function GetParentComponent: TComponent; override;
    property Parent: TWinControl read FParent write SetParent;
    property BoundsRect: TRect read GetBoundsRect write SetBoundsRect;
    property ClientRect: TRect read GetClientRect;
    property ClientWidth: Integer read GetClientWidth write SetClientWidth stored False;
    property ClientHeight: Integer read GetClientHeight write SetClientHeight stored False;
    property ControlStyle: TControlStyle read FControlStyle write FControlStyle;
    property ControlState: TControlState read FControlState write FControlState;
    property Showing: Boolean read GetShowing;
    property WindowProc: TWndMethod read FWindowProc write FWindowProc;
  published
    property Left: Integer read FLeft write SetLeft;
    property Top: Integer read FTop write SetTop;
    property Width: Integer read FWidth write SetWidth;
    property Height: Integer read FHeight write SetHeight;
    property Align: TAlign read FAlign write SetAlign default alNone;
    property Anchors: TAnchors read FAnchors write SetAnchors default [akLeft, akTop];
    property AlignWithMargins: Boolean read FAlignWithMargins write SetAlignWithMargins default False;
    property Margins: TMargins read FMargins write SetMargins;
    property Constraints: TSizeConstraints read FConstraints write SetConstraints;
    property Cursor: TCursor read FCursor write SetCursor default crDefault;
    property Enabled: Boolean read FEnabled write SetEnabled default True;
    property Hint: string read FHint write FHint;
    property ParentShowHint: Boolean read FParentShowHint write SetParentShowHint default True;
    property ShowHint: Boolean read FShowHint write SetShowHint stored IsShowHintStored default False;
    property Visible: Boolean read FVisible write SetVisible default True;
    property PopupMenu: TPopupMenu read FPopupMenu write SetPopupMenu;
  end;

  TControlClass = class of TControl;

{ ---------------------------------------------------------------- core.md section 3: TWinControl }

  TTabOrder = -1..32767;

  TWinControl = class(TControl)
  private
    FHandle: HWND;
    FDefWndProc: Pointer;                                    { default window procedure (own class or system class) }
    FControls: TList;                                        { all children, z-order back (0) to front (UIF-145) }
    FTabList: TList;                                         { windowed children in tab order (UIF-100) }
    FTabOrder: Integer;
    FTabStop: Boolean;
    FAlignLevel: Integer;
    FAligning: Boolean;
    FSettingBounds: Boolean;                                 { UIF-211 call in progress: no UIF-213 feedback }
    FBrush: TBrush;
    FOwnClass: Boolean;                                      { the window is of our own class, not a system class }
    FDoubleBuffered: Boolean;
    FSkipErase: Boolean;                                     { UIF-560: BeginPaint's erase of a buffered window }
    FPadding: TPadding;
    FCtlID: Cardinal;                                        { control id of the child window (0 = not yet assigned) }
    FSwallowChar: Boolean;                                   { UIF-085 step 2: drop the next WM_CHAR }
    FOnEnter: TNotifyEvent;
    FOnExit: TNotifyEvent;
    FOnKeyDown: TKeyEvent;
    FOnKeyUp: TKeyEvent;
    FOnKeyPress: TKeyPressEvent;
    function GetControl(Index: Integer): TControl;
    function GetControlCount: Integer;
    function GetTabOrder: TTabOrder;
    procedure SetTabOrder(Value: TTabOrder);
    procedure SetTabStop(Value: Boolean);
    procedure MoveChild(AControl: TControl; NewIndex: Integer);
    procedure SetPadding(Value: TPadding);
    procedure PaddingChanged(Sender: TObject);
    procedure ApplyAnchors(C: TControl);
    procedure RealignTree;
    procedure SortTabList;
    function FocusRoot: TWinControl;
    function GetHandle: HWND;
    function WindowAbove: HWND;
    function RouteMouse(var Message: TMessage): Boolean;
    function Reflect(C: TWinControl; var Message: TMessage): Boolean;
    function ChildByCtlID(ID: Cardinal): TWinControl;
  protected
    { core.md section 4 }
    procedure CreateParams(var Params: TCreateParams); virtual;
    procedure CreateWnd; virtual;
    procedure CreateWindowHandle(const Params: TCreateParams); virtual;
    { the menu bar handed to CreateWindowExW for a top-level window (forms: application-forms.md Menu); 0 }
    function WindowMenu: HMENU; virtual;
    procedure CreateHandle; virtual;
    procedure DestroyWnd; virtual;
    procedure DestroyWindowHandle; virtual;
    { UIF-039 }
    procedure RecreateWnd;
    procedure WndProc(var Message: TMessage); override;
    procedure WMNCDestroy(var Message: TWMNoParams); virtual;
    procedure WMWindowPosChanged(var Message: TWMWindowPosMsg); virtual;
    procedure WMEnable(var Message: TMessage); virtual;
    procedure WMMouseLeave(var Message: TMessage); virtual;
    { core.md 7.10: client area: the effective cursor of the control under the mouse }
    procedure WMSetCursor(var Message: TMessage); virtual;
    procedure WMContextMenu(var Message: TMessage); override;
    { core.md 7.10: scroll messages of the window's own scroll bars (LParam = 0) }
    procedure WMHScroll(var Message: TWMScroll); virtual;
    procedure WMVScroll(var Message: TWMScroll); virtual;
    { core.md 7.9 }
    procedure WMKeyDown(var Message: TWMKey); virtual;
    procedure WMKeyUp(var Message: TWMKey); virtual;
    procedure WMChar(var Message: TWMKey); virtual;
    { UIF-085 step 1: True when this control, as the top of a parent chain, sees its descendants' keys first
      (forms: KeyPreview); base False }
    function PreviewKeys: Boolean; virtual;
    procedure CMEnabledChanged(var Message: TMessage); override;
    procedure CMVisibleChanged(var Message: TMessage); override;
    procedure CMColorChanged(var Message: TMessage); override;
    procedure CMFontChanged(var Message: TMessage); override;
    { UIF-071 }
    procedure CNCtlColor(var Message: TMessage); virtual;
    function GetShowing: Boolean; override;
    { core.md 7.8 }
    procedure WMEraseBkgnd(var Message: TWMEraseBkgnd); virtual;
    procedure WMPaint(var Message: TWMPaint); virtual;
    procedure WMPrintClient(var Message: TMessage); virtual;
    procedure PaintHandler(var Message: TWMPaint);
    procedure PaintBuffered;
    procedure PaintWindow(DC: HDC); virtual;
    procedure PaintControls(DC: HDC; First: TControl);
    property DoubleBuffered: Boolean read FDoubleBuffered write FDoubleBuffered default False;
    { layout.md UIF-211 }
    procedure BoundsChanged(const OldBounds: TRect); override;
    { layout.md UIF-213: the window rectangle the OS gave, in the units of the bounds fields }
    function WindowBounds: TRect; virtual;
    property DefWndProc: Pointer read FDefWndProc write FDefWndProc;
    procedure SetChildOrder(Child: TComponent; Order: Integer); virtual;
    { layout.md UIF-222: one layout pass over Rect (the align rect) }
    procedure AlignControls(AControl: TControl; var Rect: TRect); virtual;
    { layout.md UIF-234: the scroll position of a scrolling container (anchor baselines are unscrolled); base (0, 0) }
    function ScrollOffset: TPoint; virtual;
    procedure Loaded; override;
    { UIF-280: Padding, then the inherited steps, then the children in Controls[] order }
    procedure ChangeScale(M, D: Integer); override;
    { UIF-283 first half: this control and every descendant re-capture their anchor baselines, top-down }
    procedure CaptureBaselines;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); override;
    procedure DoEnter; virtual;
    procedure DoExit; virtual;
    { core.md 8.3: CM_ENTER / CM_EXIT fire OnEnter / OnExit }
    procedure CMEnter(var Message: TMessage); virtual;
    procedure CMExit(var Message: TMessage); virtual;
    procedure KeyDown(var Key: Word; Shift: TShiftState); virtual;
    procedure KeyUp(var Key: Word; Shift: TShiftState); virtual;
    procedure KeyPress(var Key: Char); virtual;
    property Padding: TPadding read FPadding write SetPadding;
    property TabOrder: TTabOrder read GetTabOrder write SetTabOrder default -1;
    property TabStop: Boolean read FTabStop write SetTabStop default False;
    property OnEnter: TNotifyEvent read FOnEnter write FOnEnter;
    property OnExit: TNotifyEvent read FOnExit write FOnExit;
    property OnKeyDown: TKeyEvent read FOnKeyDown write FOnKeyDown;
    property OnKeyUp: TKeyEvent read FOnKeyUp write FOnKeyUp;
    property OnKeyPress: TKeyPressEvent read FOnKeyPress write FOnKeyPress;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function HandleAllocated: Boolean;
    procedure HandleNeeded;
    procedure DestroyHandle;
    { UIF-060: entry from StdWndProc; exceptions never unwind into the system }
    procedure MainWndProc(var Message: TMessage);
    procedure DefaultHandler(var Message); override;
    property Handle: HWND read GetHandle;
    { background brush for WM_ERASEBKGND and WM_CTLCOLOR*; its colour follows Color }
    property Brush: TBrush read FBrush;
    procedure InsertControl(AControl: TControl);
    procedure RemoveControl(AControl: TControl);
    procedure AlignControl(AControl: TControl);
    procedure DisableAlign;
    procedure EnableAlign;
    procedure Realign;
    function AlignDisabled: Boolean;
    { layout.md UIF-218: shrinks the client rectangle to the area of aligned children; base: no change }
    procedure AdjustClientRect(var Rect: TRect); virtual;
    { layout.md UIF-293: moves every child by (DeltaX, DeltaY) as a layout pass would (no baseline capture, no
      realign request) and scrolls the window contents }
    procedure ScrollBy(DeltaX, DeltaY: Integer);
    function ControlAtPos(const Pos: TPoint; AllowDisabled: Boolean; AllowWinControls: Boolean = False): TControl;
    function FindChildControl(const ControlName: string): TControl;
    function ContainsControl(Control: TControl): Boolean;
    procedure Broadcast(var Message);
    procedure NotifyControls(Msg: Word);
    property Controls[Index: Integer]: TControl read GetControl;
    property ControlCount: Integer read GetControlCount;
    procedure Invalidate; override;
    procedure Update; override;
    { core.md 8.1 }
    function CanFocus: Boolean; virtual;
    function Focused: Boolean;
    procedure SetFocus; virtual;
    { core.md 8.2 }
    procedure GetTabOrderList(List: TList); virtual;
    function FindNextControl(CurControl: TWinControl; GoForward, CheckTabStop, CheckParent: Boolean): TWinControl;
    procedure SelectNext(CurControl: TWinControl; GoForward, CheckTabStop: Boolean);
    procedure SelectFirst;
    { core.md 8.1/8.3 entry points of the control at the end of a focus chain (forms override them; Controls cannot
      use Forms): UIF-110 and UIF-093; the base does nothing }
    function SetFocusedControl(Control: TWinControl): Boolean; virtual;
    procedure DefocusControl(Control: TWinControl; Removing: Boolean); virtual;
  end;

{ ---------------------------------------------------------------- core.md section 6, section 12 }

  { a canvas bound to a control: a window DC on demand (graphic controls: the parent's, moved to the control and
    clipped to it), given back in FreeHandle }
  TControlCanvas = class(TCanvas)
  private
    FControl: TControl;
    FDeviceContext: HDC;
    FWindowHandle: HWND;
    procedure SetControl(AControl: TControl);
  protected
    procedure CreateHandle; override;
  public
    destructor Destroy; override;
    procedure FreeHandle;
    property Control: TControl read FControl write SetControl;
  end;

  TGraphicControl = class(TControl)
  private
    FCanvas: TCanvas;
  protected
    procedure Paint; virtual;
    procedure WndProc(var Message: TMessage); override;
    procedure WMPaint(var Message: TWMPaint); virtual;
    { valid only inside Paint }
    property Canvas: TCanvas read FCanvas;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  end;

  TCustomControl = class(TWinControl)
  private
    FCanvas: TCanvas;
  protected
    procedure Paint; virtual;
    procedure PaintWindow(DC: HDC); override;
    procedure DestroyWnd; override;
    property Canvas: TCanvas read FCanvas;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  end;

{ ---------------------------------------------------------------- core.md section 13: global helpers }

{ core.md section 13: the control of a window of this process, nil otherwise }
function FindControl(Handle: HWND): TWinControl;
{ core.md 13: the control that holds the mouse capture (graphic controls capture through their parent's window) }
function GetCaptureControl: TControl;
procedure SetCaptureControl(Control: TControl);
{ core.md WM_SETCURSOR: a non-default Screen.Cursor wins; else the first non-default Cursor (or DefaultCursor) up
  the parent chain }
function EffectiveCursor(C: TControl): TCursor;

{ layout.md section 1: (a*b + c div 2) div c, rounding half away from zero, 64-bit intermediate; c > 0 }
function LayoutMulDiv(A, B, C: Integer): Integer;
function IsPositiveResult(const AModalResult: TModalResult): Boolean;
function CursorToIdent(Cursor: Longint; var Ident: string): Boolean;
function IdentToCursor(const Ident: string; var Cursor: Longint): Boolean;
function CursorToString(Cursor: TCursor): string;
function StringToCursor(const S: string): TCursor;
{ UIF-021: the stock cursor of a value (0 for crNone; unknown values: the arrow) }
function CursorHandle(Cursor: TCursor): HCURSOR;

{ actions-menus.md UIF-940: the text before the first '|' (the whole string without one) }
function GetShortHint(const Hint: string): string;
{ UIF-940: the text after the first '|' (the whole string without one) }
function GetLongHint(const Hint: string): string;
{ core.md 7.9: ssShift, ssCtrl from GetKeyState; ssAlt from bit 29 of the key data }
function KeyDataToShiftState(KeyData: LPARAM): TShiftState;

type
  TMouseControlHook = procedure(Control: TControl);

var
  { core.md UIF-068: called after the control under the mouse changed (nil: none); set by Forms (hints) }
  MouseControlHook: TMouseControlHook;
  { application-forms.md 3: the Screen.Cursor override, written by Forms (Controls cannot use Forms); crDefault = none }
  ScreenCursor: TCursor;
  { core.md 8.1: the class that ends a focus chain (TCustomForm, registered by Forms); nil = nothing can focus }
  FocusRootClass: TClass;
  { layout.md UIF-310: dump mode (set by Forms); window bounds never follow or feed back the fields (UIF-211, UIF-213) }
  LayoutDumpMode: Boolean;

implementation

{ ---------------------------------------------------------------- helpers }

var
  CaptureControl: TControl;                                  { set by SetCaptureControl (core.md 13) }
  MouseControl: TControl;                                    { the control under the mouse (UIF-068) }

const
  WM_MOUSEBUTTONLAST = WM_MBUTTONDBLCLK;                    { client-coordinate mouse messages: WM_MOUSEFIRST..this }

function KeysToShift(Keys: WPARAM): TShiftState;
begin
  Result := [];
  if (Keys and MK_SHIFT) <> 0 then
    Include(Result, ssShift);
  if (Keys and MK_CONTROL) <> 0 then
    Include(Result, ssCtrl);
  if (Keys and MK_LBUTTON) <> 0 then
    Include(Result, ssLeft);
  if (Keys and MK_RBUTTON) <> 0 then
    Include(Result, ssRight);
  if (Keys and MK_MBUTTON) <> 0 then
    Include(Result, ssMiddle);
  if GetKeyState(VK_MENU) < 0 then
    Include(Result, ssAlt);
end;

procedure CheckRange(Value: Integer);
begin
  if (Value < 0) or (Value > 32767) then
    raise EInvalidOperation.CreateFmt(SValueOutOfRange, [Value]);
end;

function LayoutMulDiv(A, B, C: Integer): Integer;
var
  P: Int64;
begin
  P := Int64(A) * B;
  if P >= 0 then
    Result := Integer((P + C div 2) div C)
  else
    Result := Integer((P - C div 2) div C);
end;

function IsPositiveResult(const AModalResult: TModalResult): Boolean;
begin
  Result := (AModalResult = mrOk) or (AModalResult = mrYes) or (AModalResult = mrAll)
    or (AModalResult = mrYesToAll) or (AModalResult = mrContinue);
end;

{ ---------------------------------------------------------------- cursor idents (UIF-021) }

const
  CursorCount = 20;
  Cursors: array[0..CursorCount - 1] of TIdentMapEntry = (
    (Value: crDefault; Name: 'crDefault'),
    (Value: crNone; Name: 'crNone'),
    (Value: crArrow; Name: 'crArrow'),
    (Value: crCross; Name: 'crCross'),
    (Value: crIBeam; Name: 'crIBeam'),
    (Value: crSizeNESW; Name: 'crSizeNESW'),
    (Value: crSizeNS; Name: 'crSizeNS'),
    (Value: crSizeNWSE; Name: 'crSizeNWSE'),
    (Value: crSizeWE; Name: 'crSizeWE'),
    (Value: crUpArrow; Name: 'crUpArrow'),
    (Value: crHourGlass; Name: 'crHourGlass'),
    (Value: crDrag; Name: 'crDrag'),
    (Value: crNoDrop; Name: 'crNoDrop'),
    (Value: crHSplit; Name: 'crHSplit'),
    (Value: crVSplit; Name: 'crVSplit'),
    (Value: crNo; Name: 'crNo'),
    (Value: crAppStart; Name: 'crAppStart'),
    (Value: crHelp; Name: 'crHelp'),
    (Value: crHandPoint; Name: 'crHandPoint'),
    (Value: crSizeAll; Name: 'crSizeAll'));

function CursorToIdent(Cursor: Longint; var Ident: string): Boolean;
begin
  Result := IntToIdent(Cursor, Ident, Cursors);
end;

function IdentToCursor(const Ident: string; var Cursor: Longint): Boolean;
begin
  Result := IdentToInt(Ident, Cursor, Cursors);
end;

function CursorToString(Cursor: TCursor): string;
begin
  if not CursorToIdent(Cursor, Result) then
    Result := IntToStr(Cursor);
end;

function StringToCursor(const S: string): TCursor;
var
  V: Longint;
begin
  if IdentToCursor(S, V) then
    Result := V
  else
    Result := StrToInt(S);
end;

function CursorHandle(Cursor: TCursor): HCURSOR;
var
  Id: PWideChar;
begin
  case Cursor of
    crNone:
      begin
        Result := 0;
        Exit;
      end;
    crCross: Id := IDC_CROSS;
    crIBeam: Id := IDC_IBEAM;
    crSizeNESW: Id := IDC_SIZENESW;
    crSizeNS, crHSplit: Id := IDC_SIZENS;
    crSizeNWSE: Id := IDC_SIZENWSE;
    crSizeWE, crVSplit: Id := IDC_SIZEWE;
    crSizeAll: Id := IDC_SIZEALL;
    crUpArrow: Id := IDC_UPARROW;
    crHourGlass: Id := IDC_WAIT;
    crAppStart: Id := IDC_APPSTARTING;
    crHelp: Id := IDC_HELP;
    crHandPoint: Id := IDC_HAND;
    crNo, crNoDrop: Id := IDC_NO;
  else
    Id := IDC_ARROW;
  end;
  Result := LoadCursorW(0, Id);
end;

{ core.md WM_SETCURSOR: a non-default Screen.Cursor wins; else up the parent chain while crDefault }
function EffectiveCursor(C: TControl): TCursor;
begin
  Result := ScreenCursor;
  if Result <> crDefault then
    Exit;
  while C <> nil do
  begin
    if C.Cursor <> crDefault then
    begin
      Result := C.Cursor;
      Exit;
    end;
    if C.DefaultCursor <> crDefault then
    begin
      Result := C.DefaultCursor;
      Exit;
    end;
    C := C.Parent;
  end;
end;

{ ---------------------------------------------------------------- TSizeConstraints }

procedure TSizeConstraints.SetValue(var Field: Integer; Value: Integer);
begin
  CheckRange(Value);
  if Field <> Value then
  begin
    Field := Value;
    Change;
  end;
end;

procedure TSizeConstraints.SetMaxHeight(Value: Integer);
begin
  SetValue(FMaxHeight, Value);
end;

procedure TSizeConstraints.SetMaxWidth(Value: Integer);
begin
  SetValue(FMaxWidth, Value);
end;

procedure TSizeConstraints.SetMinHeight(Value: Integer);
begin
  SetValue(FMinHeight, Value);
end;

procedure TSizeConstraints.SetMinWidth(Value: Integer);
begin
  SetValue(FMinWidth, Value);
end;

procedure TSizeConstraints.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TSizeConstraints.AssignTo(Dest: TPersistent);
var
  D: TSizeConstraints;
begin
  if Dest is TSizeConstraints then
  begin
    D := TSizeConstraints(Dest);
    D.FMaxHeight := FMaxHeight;
    D.FMaxWidth := FMaxWidth;
    D.FMinHeight := FMinHeight;
    D.FMinWidth := FMinWidth;
    D.Change;
  end
  else
    inherited AssignTo(Dest);
end;

{ ---------------------------------------------------------------- TMargins, TPadding }

constructor TMargins.Create;
begin
  inherited Create;
  FLeft := DefaultValue;
  FTop := DefaultValue;
  FRight := DefaultValue;
  FBottom := DefaultValue;
end;

class function TMargins.DefaultValue: Integer;
begin
  Result := 3;
end;

class function TPadding.DefaultValue: Integer;
begin
  Result := 0;
end;

procedure TMargins.SetBounds(ALeft, ATop, ARight, ABottom: Integer);
begin
  CheckRange(ALeft);
  CheckRange(ATop);
  CheckRange(ARight);
  CheckRange(ABottom);
  if (ALeft <> FLeft) or (ATop <> FTop) or (ARight <> FRight) or (ABottom <> FBottom) then
  begin
    FLeft := ALeft;
    FTop := ATop;
    FRight := ARight;
    FBottom := ABottom;
    Change;
  end;
end;

procedure TMargins.SetLeft(Value: Integer);
begin
  SetBounds(Value, FTop, FRight, FBottom);
end;

procedure TMargins.SetTop(Value: Integer);
begin
  SetBounds(FLeft, Value, FRight, FBottom);
end;

procedure TMargins.SetRight(Value: Integer);
begin
  SetBounds(FLeft, FTop, Value, FBottom);
end;

procedure TMargins.SetBottom(Value: Integer);
begin
  SetBounds(FLeft, FTop, FRight, Value);
end;

procedure TMargins.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TMargins.AssignTo(Dest: TPersistent);
begin
  if Dest is TMargins then
    TMargins(Dest).SetBounds(FLeft, FTop, FRight, FBottom)
  else
    inherited AssignTo(Dest);
end;

{ ---------------------------------------------------------------- TControlActionLink }

procedure TControlActionLink.AssignClient(AClient: TObject);
begin
  FClient := TControl(AClient);
end;

function TControlActionLink.IsCaptionLinked: Boolean;
begin
  Result := FClient.Caption = FCaption;
end;

function TControlActionLink.IsEnabledLinked: Boolean;
begin
  Result := FClient.FEnabled = FEnabled;
end;

function TControlActionLink.IsHintLinked: Boolean;
begin
  Result := FClient.FHint = FHint;
end;

function TControlActionLink.IsVisibleLinked: Boolean;
begin
  Result := FClient.FVisible = FVisible;
end;

function TControlActionLink.IsOnExecuteLinked: Boolean;
begin
  Result := SameEvent(FClient.FOnClick, FOnExecute);
end;

procedure TControlActionLink.SetCaption(const Value: string);
begin
  FClient.Caption := Value;
end;

procedure TControlActionLink.SetEnabled(Value: Boolean);
begin
  FClient.Enabled := Value;
end;

procedure TControlActionLink.SetHint(const Value: string);
begin
  FClient.FHint := Value;
end;

procedure TControlActionLink.SetVisible(Value: Boolean);
begin
  FClient.Visible := Value;
end;

procedure TControlActionLink.SetOnExecute(Value: TNotifyEvent);
begin
  FClient.FOnClick := Value;
end;

{ ---------------------------------------------------------------- hints (actions-menus.md UIF-940) }

function GetShortHint(const Hint: string): string;
var
  I: Integer;
begin
  I := Pos('|', Hint);
  if I = 0 then
    Result := Hint
  else
    Result := Copy(Hint, 1, I - 1);
end;

function GetLongHint(const Hint: string): string;
var
  I: Integer;
begin
  I := Pos('|', Hint);
  if I = 0 then
    Result := Hint
  else
    Result := Copy(Hint, I + 1, MaxInt);
end;

{ ---------------------------------------------------------------- TControl }

constructor TControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FWindowProc := WndProc;
  FAnchors := [akLeft, akTop];
  FCursor := crDefault;
  FEnabled := True;
  FVisible := True;
  FParentShowHint := True;
  FColor := clWindow;
  FParentColor := True;
  FParentFont := True;
  FControlStyle := [csCaptureMouse, csClickEvents, csSetCaption, csDoubleClicks];
  FFont := TFont.Create;
  FFont.OnChange := FontChanged;
  FMargins := TMargins.Create;
  FMargins.OnChange := MarginsChanged;
  FConstraints := TSizeConstraints.Create;
  FConstraints.OnChange := ConstraintsChanged;
end;

destructor TControl.Destroy;
begin
  if CaptureControl = Self then
    SetCaptureControl(nil);
  if MouseControl = Self then
  begin
    MouseControl := nil;
    if Assigned(MouseControlHook) then
      MouseControlHook(nil);
  end;
  if FActionLink <> nil then
  begin
    FActionLink.Free;
    FActionLink := nil;
  end;
  SetParent(nil);
  FConstraints.Free;
  FMargins.Free;
  FFont.Free;
  inherited Destroy;
end;

var
  LoadLayoutLevel: Integer;                                  { > 0: the load-time layout pass runs (UIF-300 step 6) }
  ScalingLevel: Integer;                                     { > 0: ChangeScale runs; layout passes are deferred }

procedure TControl.ChangeScale(M, D: Integer);
begin
  if (M = D) or (M <= 0) or (D <= 0) then
    Exit;
  Inc(ScalingLevel);
  try
    { step 2 }
    FConstraints.MinWidth := LayoutMulDiv(FConstraints.MinWidth, M, D);
    FConstraints.MinHeight := LayoutMulDiv(FConstraints.MinHeight, M, D);
    FConstraints.MaxWidth := LayoutMulDiv(FConstraints.MaxWidth, M, D);
    FConstraints.MaxHeight := LayoutMulDiv(FConstraints.MaxHeight, M, D);
    FMargins.SetBounds(LayoutMulDiv(FMargins.Left, M, D), LayoutMulDiv(FMargins.Top, M, D),
      LayoutMulDiv(FMargins.Right, M, D), LayoutMulDiv(FMargins.Bottom, M, D));
    { step 3 }
    if not FParentFont then
      FFont.Height := LayoutMulDiv(FFont.Height, M, D);
    { steps 1, 5 }
    ScaleBounds(M, D);
  finally
    Dec(ScalingLevel);
  end;
end;

procedure TControl.ScaleBounds(M, D: Integer);
var
  NL, NT: Integer;
begin
  NL := LayoutMulDiv(FLeft, M, D);
  NT := LayoutMulDiv(FTop, M, D);
  FLayoutCall := True;
  SetBounds(NL, NT, LayoutMulDiv(FLeft + FWidth, M, D) - NL, LayoutMulDiv(FTop + FHeight, M, D) - NT);
  FLayoutCall := False;
end;

procedure TControl.ScaleBy(M, D: Integer);
begin
  if (M = D) or (M <= 0) or (D <= 0) then
    Exit;
  ChangeScale(M, D);
  if Self is TWinControl then
    TWinControl(Self).CaptureBaselines
  else
    CaptureBaseline;
  if FParent <> nil then
    FParent.Realign;
  if Self is TWinControl then
    TWinControl(Self).RealignTree;
end;

procedure TControl.SetBounds(ALeft, ATop, AWidth, AHeight: Integer);
var
  Explicit, SizeChanged, Loading: Boolean;
  Old: TRect;
begin
  Explicit := not FLayoutCall;
  FLayoutCall := False;
  { step 1 }
  if AWidth < 0 then
    AWidth := 0;
  if AHeight < 0 then
    AHeight := 0;
  { step 2 }
  ConstrainSize(AWidth, AHeight);
  { step 3 }
  if (ALeft = FLeft) and (ATop = FTop) and (AWidth = FWidth) and (AHeight = FHeight) then
    Exit;
  { step 4 }
  Old := BoundsRect;
  SizeChanged := (AWidth <> FWidth) or (AHeight <> FHeight);
  FLeft := ALeft;
  FTop := ATop;
  FWidth := AWidth;
  FHeight := AHeight;
  Loading := csLoading in ComponentState;
  { step 5 }
  if Explicit and not Loading then
    CaptureBaseline;
  { step 6 }
  BoundsChanged(Old);
  { step 7 }
  if SizeChanged and (Self is TWinControl) then
    TWinControl(Self).Realign;
  { step 8 (scaling itself fires no OnResize; the UIF-283 realign does for what it resizes) }
  if SizeChanged and not Loading and (LoadLayoutLevel = 0) and (ScalingLevel = 0) then
    Resize;
  { step 9 }
  if Explicit and not Loading then
    RequestAlign;
end;

procedure TControl.BoundsChanged(const OldBounds: TRect);
begin
end;

{ layout.md UIF-213: the fields take the rectangle the OS gave (R = left, top, width, height), then steps 5, 7, 8, 9
  of UIF-210 run as for an explicit change }
procedure TControl.UpdateBoundsFromOS(const R: TRect);
var
  SizeChanged, Loading: Boolean;
begin
  if (R.Left = FLeft) and (R.Top = FTop) and (R.Right = FWidth) and (R.Bottom = FHeight) then
    Exit;
  SizeChanged := (R.Right <> FWidth) or (R.Bottom <> FHeight);
  FLeft := R.Left;
  FTop := R.Top;
  FWidth := R.Right;
  FHeight := R.Bottom;
  Loading := csLoading in ComponentState;
  if not Loading then
    CaptureBaseline;
  if SizeChanged and (Self is TWinControl) then
    TWinControl(Self).Realign;
  if SizeChanged and not Loading and (LoadLayoutLevel = 0) then
    Resize;
  if not Loading then
    RequestAlign;
end;

procedure TControl.SetLeft(Value: Integer);
begin
  SetBounds(Value, FTop, FWidth, FHeight);
end;

procedure TControl.SetTop(Value: Integer);
begin
  SetBounds(FLeft, Value, FWidth, FHeight);
end;

procedure TControl.SetWidth(Value: Integer);
begin
  SetBounds(FLeft, FTop, Value, FHeight);
end;

procedure TControl.SetHeight(Value: Integer);
begin
  SetBounds(FLeft, FTop, FWidth, Value);
end;

function TControl.GetBoundsRect: TRect;
begin
  Result := Rect(FLeft, FTop, FLeft + FWidth, FTop + FHeight);
end;

procedure TControl.SetBoundsRect(const Value: TRect);
begin
  SetBounds(Value.Left, Value.Top, Value.Right - Value.Left, Value.Bottom - Value.Top);
end;

function TControl.GetClientRect: TRect;
begin
  Result := Rect(0, 0, FWidth, FHeight);
end;

function TControl.GetClientWidth: Integer;
var
  R: TRect;
begin
  R := GetClientRect;
  Result := R.Right - R.Left;
end;

function TControl.GetClientHeight: Integer;
var
  R: TRect;
begin
  R := GetClientRect;
  Result := R.Bottom - R.Top;
end;

procedure TControl.SetClientWidth(Value: Integer);
begin
  SetWidth(FWidth + (Value - GetClientWidth));
end;

procedure TControl.SetClientHeight(Value: Integer);
begin
  SetHeight(FHeight + (Value - GetClientHeight));
end;

procedure TControl.SetAlign(Value: TAlign);
begin
  if FAlign <> Value then
  begin
    FAlign := Value;
    if not (csLoading in ComponentState) then
      CaptureBaseline;
    RequestAlign;
  end;
end;

procedure TControl.SetAnchors(Value: TAnchors);
begin
  if FAnchors <> Value then
  begin
    FAnchors := Value;
    if not (csLoading in ComponentState) then
      CaptureBaseline;
  end;
end;

{ UIF-232: (BL, BT, BW, BH) := bounds, (BPW, BPH) := parent's client size, (0, 0) without a parent (UIF-233) }
procedure TControl.CaptureBaseline;
var
  Offset: TPoint;
begin
  FBaseBounds := Rect(FLeft, FTop, FWidth, FHeight);
  if FParent <> nil then
  begin
    Offset := FParent.ScrollOffset;                          { UIF-234: unscrolled coordinates }
    FBaseBounds.Left := FLeft + Offset.X;
    FBaseBounds.Top := FTop + Offset.Y;
    FBaseParentWidth := FParent.ClientWidth;
    FBaseParentHeight := FParent.ClientHeight;
  end
  else
  begin
    FBaseParentWidth := 0;
    FBaseParentHeight := 0;
  end;
end;

procedure TControl.SetAlignWithMargins(Value: Boolean);
begin
  if FAlignWithMargins <> Value then
  begin
    FAlignWithMargins := Value;
    RequestAlign;
  end;
end;

procedure TControl.SetMargins(Value: TMargins);
begin
  FMargins.Assign(Value);
end;

procedure TControl.SetConstraints(Value: TSizeConstraints);
begin
  FConstraints.Assign(Value);
end;

procedure TControl.MarginsChanged(Sender: TObject);
begin
  RequestAlign;
end;

{ UIF-241: re-applied as an explicit change }
procedure TControl.ConstraintsChanged(Sender: TObject);
begin
  if ScalingLevel = 0 then                                   { scaling applies the new bounds itself (UIF-280) }
    SetBounds(FLeft, FTop, FWidth, FHeight);
end;

procedure TControl.ConstrainSize(var AWidth, AHeight: Integer);
var
  MinW, MinH, MaxW, MaxH: Integer;
begin
  IntrinsicSize(AWidth, AHeight);
  MinW := FConstraints.MinWidth;
  MinH := FConstraints.MinHeight;
  MaxW := FConstraints.MaxWidth;
  MaxH := FConstraints.MaxHeight;
  ConstrainedResize(MinW, MinH, MaxW, MaxH);
  if (MaxW > 0) and (AWidth > MaxW) then
    AWidth := MaxW;
  if (MinW > 0) and (AWidth < MinW) then
    AWidth := MinW;
  if (MaxH > 0) and (AHeight > MaxH) then
    AHeight := MaxH;
  if (MinH > 0) and (AHeight < MinH) then
    AHeight := MinH;
end;

procedure TControl.IntrinsicSize(var AWidth, AHeight: Integer);
begin
end;

procedure TControl.SetCursor(Value: TCursor);
begin
  if FCursor <> Value then
  begin
    FCursor := Value;
    Perform(CM_CURSORCHANGED, 0, 0);
  end;
end;

procedure TControl.SetEnabled(Value: Boolean);
begin
  if FEnabled <> Value then
  begin
    FEnabled := Value;
    Perform(CM_ENABLEDCHANGED, 0, 0);
  end;
end;

procedure TControl.SetVisible(Value: Boolean);
begin
  if FVisible <> Value then
  begin
    FVisible := Value;
    if not (csLoading in ComponentState) then
    begin
      Perform(CM_VISIBLECHANGED, WPARAM(Ord(Value)), 0);
      RequestAlign;
    end;
  end;
end;

procedure TControl.SetShowHint(Value: Boolean);
begin
  FParentShowHint := False;
  if FShowHint <> Value then
  begin
    FShowHint := Value;
    ShowHintChanged;
  end;
end;

procedure TControl.SetParentShowHint(Value: Boolean);
begin
  if FParentShowHint <> Value then
  begin
    FParentShowHint := Value;
    if Value then
      Perform(CM_PARENTSHOWHINTCHANGED, 0, 0);
  end;
end;

function TControl.IsShowHintStored: Boolean;
begin
  Result := not FParentShowHint;
end;

function TControl.GetCaption: TCaption;
begin
  Result := GetText;
end;

{ UIF-1250: the text follows the name while it equals the old name exactly; never while loading or reading }
procedure TControl.SetName(const NewName: TComponentName);
var
  Follow: Boolean;
begin
  Follow := (csSetCaption in ControlStyle) and not (csLoading in ComponentState) and
    not (csReading in ComponentState) and (GetText = Name);
  inherited SetName(NewName);
  if Follow then
    SetText(Name);
end;

procedure TControl.SetCaption(const Value: TCaption);
begin
  SetText(Value);
end;

function TControl.GetText: TCaption;
var
  Len: Integer;
  S: string;
begin
  Len := GetTextLen;
  SetLength(S, Len);
  if Len > 0 then
    GetTextBuf(PChar(S), Len + 1);
  Result := S;
end;

procedure TControl.SetText(const Value: TCaption);
begin
  if GetText <> Value then
  begin
    SetTextBuf(PChar(string(Value)));
    Perform(CM_TEXTCHANGED, 0, 0);
  end;
end;

function TControl.GetTextLen: Integer;
begin
  Result := Integer(Perform(WM_GETTEXTLENGTH, 0, 0));
end;

function TControl.GetTextBuf(Buffer: PChar; BufSize: Integer): Integer;
begin
  Result := Integer(Perform(WM_GETTEXT, WPARAM(BufSize), LPARAM(Buffer)));
end;

procedure TControl.SetTextBuf(Buffer: PChar);
begin
  Perform(WM_SETTEXT, 0, LPARAM(Buffer));
end;

procedure TControl.SetColor(Value: TColor);
begin
  FParentColor := False;
  if FColor <> Value then
  begin
    FColor := Value;
    Perform(CM_COLORCHANGED, 0, 0);
  end;
end;

function TControl.IsColorStored: Boolean;
begin
  Result := not FParentColor;
end;

procedure TControl.SetFont(Value: TFont);
begin
  FFont.Assign(Value);
end;

function TControl.IsFontStored: Boolean;
begin
  Result := not FParentFont;
end;

procedure TControl.FontChanged(Sender: TObject);
begin
  if not FFontFromParent then
    FParentFont := False;
  Perform(CM_FONTCHANGED, 0, 0);
end;

procedure TControl.SetParentColor(Value: Boolean);
begin
  if FParentColor <> Value then
  begin
    FParentColor := Value;
    if Value then
      Perform(CM_PARENTCOLORCHANGED, 0, 0);
  end;
end;

procedure TControl.SetParentFont(Value: Boolean);
begin
  if FParentFont <> Value then
  begin
    FParentFont := Value;
    if Value then
      Perform(CM_PARENTFONTCHANGED, 0, 0);
  end;
end;

procedure TControl.SetAutoSize(Value: Boolean);
begin
  if FAutoSize <> Value then
  begin
    FAutoSize := Value;
    if Value then
      AdjustSize;
  end;
end;

{ UIF-046, UIF-047 }
procedure TControl.SetParent(AParent: TWinControl);
begin
  if FParent = AParent then
    Exit;
  if (AParent <> nil) and (Self is TWinControl) and TWinControl(Self).ContainsControl(AParent) then
    raise EInvalidOperation.Create(SControlParentSelf);
  if FParent <> nil then
    FParent.RemoveControl(Self);
  if AParent <> nil then
    AParent.InsertControl(Self);
end;

{ UIF-146, UIF-148 (list part) }
procedure TControl.BringToFront;
begin
  if FParent <> nil then
    FParent.MoveChild(Self, FParent.FControls.Count - 1);
end;

procedure TControl.SendToBack;
begin
  if FParent <> nil then
    FParent.MoveChild(Self, 0);
end;

function TControl.GetShowing: Boolean;
begin
  Result := FVisible and (FParent <> nil) and FParent.Showing;
end;

{ actions-menus.md UIF-909: the linked action runs unless OnClick is the client's own handler }
procedure TControl.Click;
begin
  if (FActionLink <> nil) and (FActionLink.Action <> nil) and (not Assigned(FOnClick)
    or SameEvent(FOnClick, FActionLink.Action.OnExecute)) then
    FActionLink.Execute(Self)
  else if Assigned(FOnClick) then
    FOnClick(Self);
end;

function TControl.GetAction: TBasicAction;
begin
  if FActionLink = nil then
    Result := nil
  else
    Result := FActionLink.Action;
end;

function TControl.UpdatesActionWhenHidden: Boolean;
begin
  Result := False;
end;

function TControl.GetActionLinkClass: TControlActionLinkClass;
begin
  Result := TControlActionLink;
end;

{ UIF-906; UIF-908: while loading only the link is made (Loaded copies) }
procedure TControl.SetAction(Value: TBasicAction);
begin
  if GetAction = Value then
    Exit;
  if FActionLink <> nil then
  begin
    if FActionLink.Action <> nil then
      FActionLink.Action.RemoveFreeNotification(Self);
    FActionLink.Free;
    FActionLink := nil;
  end;
  if Value = nil then
  begin
    Exclude(FControlStyle, csActionClient);
    Exit;
  end;
  FActionLink := GetActionLinkClass.Create(Self);
  FActionLink.Action := Value;
  Value.FreeNotification(Self);
  Include(FControlStyle, csActionClient);
  if not (csLoading in ComponentState) then
    ActionChange(Value, True);
end;

procedure TControl.ActionChange(Sender: TObject; CheckDefaults: Boolean);
var
  A: TCustomAction;
begin
  if Sender is TCustomAction then
  begin
    A := TCustomAction(Sender);
    if not CheckDefaults or (Caption = '') or (Caption = Name) then
      Caption := A.Caption;
    if not CheckDefaults or FEnabled then
      Enabled := A.Enabled;
    if not CheckDefaults or (FHint = '') then
      FHint := A.Hint;
    if not CheckDefaults or FVisible then
      Visible := A.Visible;
  end;
  if (Sender is TBasicAction) and (not CheckDefaults or not Assigned(FOnClick)) then
    FOnClick := TBasicAction(Sender).OnExecute;
end;

procedure TControl.DblClick;
begin
  if Assigned(FOnDblClick) then
    FOnDblClick(Self);
end;

procedure TControl.MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  if Assigned(FOnMouseDown) then
    FOnMouseDown(Self, Button, Shift, X, Y);
end;

procedure TControl.MouseMove(Shift: TShiftState; X, Y: Integer);
begin
  if Assigned(FOnMouseMove) then
    FOnMouseMove(Self, Shift, X, Y);
end;

procedure TControl.MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  if Assigned(FOnMouseUp) then
    FOnMouseUp(Self, Button, Shift, X, Y);
end;

procedure TControl.Resize;
begin
  if Assigned(FOnResize) then
    FOnResize(Self);
end;

procedure TControl.AdjustSize;
var
  W, H: Integer;
begin
  if FAutoSize and not (csLoading in ComponentState) then
  begin
    W := FWidth;
    H := FHeight;
    if CanAutoSize(W, H) then
      SetBounds(FLeft, FTop, W, H);
  end;
end;

function TControl.CanAutoSize(var NewWidth, NewHeight: Integer): Boolean;
begin
  Result := True;
end;

function TControl.DefaultCursor: TCursor;
begin
  Result := crDefault;
end;

function TControl.MeasuringAllowed: Boolean;
begin
  Result := FAutoSize and ([csLoading, csDestroying] * ComponentState = []) and not LayoutDumpMode
    and (ScalingLevel = 0);
end;

function TControl.CanResize(var NewWidth, NewHeight: Integer): Boolean;
begin
  Result := True;
end;

procedure TControl.ConstrainedResize(var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer);
begin
  if Assigned(FOnConstrainedResize) then
    FOnConstrainedResize(Self, MinWidth, MinHeight, MaxWidth, MaxHeight);
end;

procedure TControl.RequestAlign;
begin
  if FParent <> nil then
    FParent.AlignControl(Self);
end;

{ core.md UIF-060: framework handlers by case; everything else through Dispatch }
procedure TControl.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_MOUSEMOVE then
    TrackMouseControl;
  if (Message.Msg >= WM_MOUSEFIRST) and (Message.Msg <= WM_MOUSEBUTTONLAST)
    and not (csNoStdEvents in FControlStyle) then
  begin
    MouseMessage(Message);
    Exit;
  end;
  case Message.Msg of
    WM_MOUSEWHEEL:
      WMMouseWheel(Message);
    WM_CONTEXTMENU:
      WMContextMenu(Message);
    CM_MOUSEENTER:
      CMMouseEnter(Message);
    CM_MOUSELEAVE:
      CMMouseLeave(Message);
    WM_CANCELMODE, WM_CAPTURECHANGED:
      begin
        ClearMouseState;
        Dispatch(Message);
      end;
    CM_ENABLEDCHANGED:
      CMEnabledChanged(Message);
    CM_VISIBLECHANGED:
      CMVisibleChanged(Message);
    CM_COLORCHANGED:
      CMColorChanged(Message);
    CM_FONTCHANGED:
      CMFontChanged(Message);
    CM_PARENTCOLORCHANGED:
      CMParentColorChanged(Message);
    CM_PARENTFONTCHANGED:
      CMParentFontChanged(Message);
    CM_PARENTSHOWHINTCHANGED:
      CMParentShowHintChanged(Message);
    CM_CURSORCHANGED:
      CMCursorChanged(Message);
  else
    Dispatch(Message);
  end;
end;

procedure TControl.CMCursorChanged(var Message: TMessage);
var
  C: TControl;
begin
  C := MouseControl;
  while (C <> nil) and (C <> Self) do
    C := C.FParent;
  if C <> nil then
    Windows.SetCursor(CursorHandle(EffectiveCursor(MouseControl)));
end;

procedure TControl.CMColorChanged(var Message: TMessage);
begin
  Invalidate;
end;

procedure TControl.CMFontChanged(var Message: TMessage);
begin
  Invalidate;
end;

{ core.md 1.1: copy the parent's colour; the flag stays True }
procedure TControl.CMParentColorChanged(var Message: TMessage);
begin
  if FParentColor and (FParent <> nil) and (FColor <> FParent.FColor) then
  begin
    FColor := FParent.FColor;
    Perform(CM_COLORCHANGED, 0, 0);
  end;
end;

{ core.md 1.1: Font.Assign(Parent.Font); the flag stays True }
procedure TControl.CMParentFontChanged(var Message: TMessage);
begin
  if FParentFont and (FParent <> nil) then
  begin
    FFontFromParent := True;
    try
      FFont.Assign(FParent.FFont);
    finally
      FFontFromParent := False;
    end;
  end;
end;

procedure TControl.CMParentShowHintChanged(var Message: TMessage);
begin
  if FParentShowHint and (FParent <> nil) and (FShowHint <> FParent.FShowHint) then
  begin
    FShowHint := FParent.FShowHint;
    ShowHintChanged;
  end;
end;

{ the children of a windowed control copy the new value when their ParentShowHint is True }
procedure TControl.ShowHintChanged;
begin
  if Self is TWinControl then
    TWinControl(Self).NotifyControls(CM_PARENTSHOWHINTCHANGED);
end;

{ UIF-065: mouse messages to events, in the order of the table; default processing through Dispatch }
procedure TControl.MouseMessage(var Message: TMessage);
var
  X, Y: Integer;
  Shift: TShiftState;
  Clicked: Boolean;
begin
  X := SmallInt(LoWord(Message.LParam));
  Y := SmallInt(HiWord(Message.LParam));
  Shift := KeysToShift(Message.WParam);
  case Message.Msg of
    WM_LBUTTONDOWN, WM_LBUTTONDBLCLK:
      begin
        if (Message.Msg = WM_LBUTTONDBLCLK) and (csDoubleClicks in FControlStyle) then
        begin
          DblClick;
          MouseDown(mbLeft, Shift + [ssDouble], X, Y);
        end
        else
        begin
          if csCaptureMouse in FControlStyle then
            SetCaptureControl(Self);
          Include(FControlState, csLButtonDown);
          if csClickEvents in FControlStyle then
            Include(FControlState, csClicked);
          MouseDown(mbLeft, Shift, X, Y);
        end;
        Dispatch(Message);
      end;
    WM_LBUTTONUP:
      begin
        Dispatch(Message);
        { the flag is read first: releasing the capture sends WM_CAPTURECHANGED, which clears it }
        Clicked := csClicked in FControlState;
        if CaptureControl = Self then
          SetCaptureControl(nil);
        Exclude(FControlState, csClicked);
        if Clicked and (X >= 0) and (Y >= 0) and (X < ClientWidth) and (Y < ClientHeight) then
          Click;
        Exclude(FControlState, csLButtonDown);
        MouseUp(mbLeft, Shift, X, Y);
      end;
    WM_RBUTTONDOWN:
      begin
        MouseDown(mbRight, Shift, X, Y);
        Dispatch(Message);
      end;
    WM_MBUTTONDOWN:
      begin
        MouseDown(mbMiddle, Shift, X, Y);
        Dispatch(Message);
      end;
    WM_RBUTTONDBLCLK:
      begin
        MouseDown(mbRight, Shift + [ssDouble], X, Y);
        Dispatch(Message);
      end;
    WM_MBUTTONDBLCLK:
      begin
        MouseDown(mbMiddle, Shift + [ssDouble], X, Y);
        Dispatch(Message);
      end;
    WM_RBUTTONUP:
      begin
        MouseUp(mbRight, Shift, X, Y);
        Dispatch(Message);
      end;
    WM_MBUTTONUP:
      begin
        MouseUp(mbMiddle, Shift, X, Y);
        Dispatch(Message);
      end;
    WM_MOUSEMOVE:
      begin
        MouseMove(Shift, X, Y);
        Dispatch(Message);
      end;
  else
    Dispatch(Message);
  end;
end;

{ UIF-065 last row; a graphic child capturing through this window loses its state too }
procedure TControl.ClearMouseState;
begin
  FControlState := FControlState - [csLButtonDown, csClicked];
  if (CaptureControl <> nil) and (CaptureControl <> Self) and not (CaptureControl is TWinControl)
    and (CaptureControl.FParent = Self) then
    CaptureControl.FControlState := CaptureControl.FControlState - [csLButtonDown, csClicked];
  if (CaptureControl = Self) or ((CaptureControl <> nil) and (CaptureControl.FParent = Self)
    and not (CaptureControl is TWinControl)) then
    CaptureControl := nil;
end;

{ UIF-068: a WM_MOUSEMOVE reached Self (after graphic routing) }
procedure TControl.TrackMouseControl;
var
  Old: TControl;
  Track: TTrackMouseEvent;
  H: HWND;
begin
  if MouseControl = Self then
    Exit;
  Old := MouseControl;
  if Old <> nil then
    Old.Perform(CM_MOUSELEAVE, 0, 0);
  MouseControl := Self;
  Perform(CM_MOUSEENTER, 0, 0);
  if Assigned(MouseControlHook) and (MouseControl = Self) then
    MouseControlHook(Self);
  H := 0;
  if Self is TWinControl then
    H := TWinControl(Self).FHandle
  else if FParent <> nil then
    H := FParent.FHandle;
  if H <> 0 then
  begin
    Track.cbSize := SizeOf(Track);
    Track.dwFlags := TME_LEAVE;
    Track.hwndTrack := H;
    Track.dwHoverTime := 0;
    TrackMouseEvent(Track);
  end;
end;

procedure TControl.CMMouseEnter(var Message: TMessage);
begin
  if Assigned(FOnMouseEnter) then
    FOnMouseEnter(Self);
end;

procedure TControl.CMMouseLeave(var Message: TMessage);
begin
  if Assigned(FOnMouseLeave) then
    FOnMouseLeave(Self);
end;

function TControl.DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean;
begin
  Result := False;
  if Assigned(FOnMouseWheel) then
    FOnMouseWheel(Self, Shift, WheelDelta, MousePos, Result);
  if not Result then
    if WheelDelta > 0 then
      Result := DoMouseWheelUp(Shift, MousePos)
    else if WheelDelta < 0 then
      Result := DoMouseWheelDown(Shift, MousePos);
end;

function TControl.DoMouseWheelDown(Shift: TShiftState; MousePos: TPoint): Boolean;
begin
  Result := False;
  if Assigned(FOnMouseWheelDown) then
    FOnMouseWheelDown(Self, Shift, MousePos, Result);
end;

function TControl.DoMouseWheelUp(Shift: TShiftState; MousePos: TPoint): Boolean;
begin
  Result := False;
  if Assigned(FOnMouseWheelUp) then
    FOnMouseWheelUp(Self, Shift, MousePos, Result);
end;

{ UIF-075: unhandled, default processing (DefWindowProc passes it to the parent window) }
procedure TControl.WMMouseWheel(var Message: TMessage);
var
  P: TPoint;
begin
  P := Point(SmallInt(LoWord(Message.LParam)), SmallInt(HiWord(Message.LParam)));
  if DoMouseWheel(KeysToShift(LoWord(Message.WParam)), SmallInt(HiWord(Message.WParam)), P) then
    Message.Result := 0
  else
    Dispatch(Message);
end;

procedure TControl.DoContextPopup(MousePos: TPoint; var Handled: Boolean);
begin
  if Assigned(FOnContextPopup) then
    FOnContextPopup(Self, MousePos, Handled);
end;

{ UIF-077: screen position (-1, -1 = keyboard: the client centre) to client; handled = result 1, else default
  processing (DefWindowProc passes it to the parent window) }
procedure TControl.WMContextMenu(var Message: TMessage);
var
  P, S: TPoint;
  H: HWND;
  Handled: Boolean;
begin
  P := Point(SmallInt(LoWord(Message.LParam)), SmallInt(HiWord(Message.LParam)));
  if (P.X = -1) and (P.Y = -1) then
    P := Point(ClientWidth div 2, ClientHeight div 2)
  else
  begin
    H := 0;
    if Self is TWinControl then
      H := TWinControl(Self).FHandle
    else if FParent <> nil then
    begin
      H := FParent.FHandle;
      P := Point(P.X - FLeft, P.Y - FTop);                  { screen offset of the parent's client origin below }
    end;
    if H <> 0 then
      ScreenToClient(H, P);
  end;
  Handled := False;
  DoContextPopup(P, Handled);
  if Handled then
  begin
    Message.Result := 1;
    Exit;
  end;
  { the screen position of the popup: the message's, or the client centre for the keyboard origin }
  S := Point(SmallInt(LoWord(Message.LParam)), SmallInt(HiWord(Message.LParam)));
  if (S.X = -1) and (S.Y = -1) then
  begin
    S := P;
    H := 0;
    if Self is TWinControl then
      H := TWinControl(Self).FHandle
    else if FParent <> nil then
    begin
      H := FParent.FHandle;
      S := Point(P.X + FLeft, P.Y + FTop);
    end;
    if H <> 0 then
      Windows.ClientToScreen(H, S);
  end;
  if ShowPopupMenu(S) then
    Message.Result := 1
  else
    Dispatch(Message);
end;

function TControl.ShowPopupMenu(const ScreenPos: TPoint): Boolean;
begin
  Result := (FPopupMenu <> nil) and FPopupMenu.AutoPopup;
  if Result then
  begin
    FPopupMenu.PopupComponent := Self;
    FPopupMenu.Popup(ScreenPos.X, ScreenPos.Y);
  end;
end;

procedure TControl.SetPopupMenu(Value: TPopupMenu);
begin
  if FPopupMenu = Value then
    Exit;
  if FPopupMenu <> nil then
    FPopupMenu.RemoveFreeNotification(Self);
  FPopupMenu := Value;
  if FPopupMenu <> nil then
    FPopupMenu.FreeNotification(Self);
end;

procedure TControl.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FPopupMenu) then
    FPopupMenu := nil;
  { UIF-912: a freed action (it unregistered the link first) leaves the copied values alone }
  if (Operation = opRemove) and (FActionLink <> nil) and ((FActionLink.Action = AComponent)
    or (FActionLink.Action = nil)) then
  begin
    FActionLink.Free;
    FActionLink := nil;
    Exclude(FControlStyle, csActionClient);
  end;
end;

{ UIF-130 (graphic part) }
procedure TControl.CMEnabledChanged(var Message: TMessage);
begin
  Invalidate;
end;

{ UIF-136 (graphic part) }
procedure TControl.CMVisibleChanged(var Message: TMessage);
begin
  Invalidate;
end;

procedure TControl.DefaultHandler(var Message);
var
  M: PMessage;
  N: Integer;
  Buffer: PChar;
begin
  M := @Message;
  case M^.Msg of
    WM_GETTEXTLENGTH:
      M^.Result := Length(FText);
    WM_GETTEXT:
      begin
        N := 0;
        if M^.WParam > 0 then
        begin
          N := Length(FText);
          if N > Integer(M^.WParam) - 1 then
            N := Integer(M^.WParam) - 1;
          Buffer := PChar(M^.LParam);
          if N > 0 then
            Move(PChar(FText)^, Buffer^, N * SizeOf(Char));
          Buffer[N] := #0;
        end;
        M^.Result := N;
      end;
    WM_SETTEXT:
      begin
        FText := string(PChar(M^.LParam));
        M^.Result := 1;
      end;
  end;
end;

function TControl.Perform(Msg: Cardinal; WParam: WPARAM; LParam: LPARAM): LRESULT;
var
  Message: TMessage;
begin
  Message.Msg := Msg;
  Message.WParam := WParam;
  Message.LParam := LParam;
  Message.Result := 0;
  FWindowProc(Message);
  Result := Message.Result;
end;

procedure TControl.Loaded;
begin
  inherited Loaded;
  CaptureBaseline;                                           { UIF-232 case 4 }
  if (FActionLink <> nil) and (FActionLink.Action <> nil) then
  begin
    FActionLink.RecordValues;                                { UIF-908 }
    ActionChange(FActionLink.Action, True);
  end;
end;

procedure TControl.Changed;
begin
  Perform(CM_CHANGED, 0, LPARAM(Self));
end;

procedure TControl.Show;
begin
  SetVisible(True);
end;

procedure TControl.Hide;
begin
  SetVisible(False);
end;

{ core.md 2.2: the control's rectangle in the parent window; never creates a handle }
procedure TControl.Invalidate;
var
  R: TRect;
begin
  if (FParent <> nil) and (FParent.FHandle <> 0) then
  begin
    R := BoundsRect;
    InvalidateRect(FParent.FHandle, @R, not (csOpaque in FControlStyle));
  end;
end;

procedure TControl.Repaint;
begin
  Invalidate;
  Update;
end;

{ core.md 2.2: UpdateWindow of the nearest windowed control with a handle }
procedure TControl.Update;
begin
  if FParent <> nil then
    FParent.Update;
end;

procedure TControl.Refresh;
begin
  Repaint;
end;

function TControl.ClientToParent(const Point: TPoint; AParent: TWinControl): TPoint;
var
  C: TControl;
begin
  if AParent = nil then
    AParent := FParent;
  if AParent = nil then
    raise EInvalidOperation.CreateFmt(SParentRequired, [Name]);
  Result := Point;
  C := Self;
  while C <> AParent do
  begin
    if C.FParent = nil then
      raise EInvalidOperation.CreateFmt(SParentRequired, [Name]);
    Result.X := Result.X + C.FLeft;
    Result.Y := Result.Y + C.FTop;
    C := C.FParent;
  end;
end;

function TControl.ParentToClient(const Point: TPoint; AParent: TWinControl): TPoint;
var
  Origin: TPoint;
begin
  Origin := ClientToParent(Types.Point(0, 0), AParent);
  Result.X := Point.X - Origin.X;
  Result.Y := Point.Y - Origin.Y;
end;

function TControl.HasParent: Boolean;
begin
  Result := FParent <> nil;
end;

function TControl.GetParentComponent: TComponent;
begin
  Result := FParent;
end;

procedure TControl.SetParentComponent(Value: TComponent);
begin
  if Value is TWinControl then
    SetParent(TWinControl(Value))
  else if Value <> nil then
    raise EInvalidOperation.CreateFmt(SInvalidParentClass, [Name, Value.ClassName]);
end;

{ ---------------------------------------------------------------- TWinControl }

constructor TWinControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FControls := TList.Create;
  FTabList := TList.Create;
  FTabOrder := -1;
  FPadding := TPadding.Create;
  FPadding.OnChange := PaddingChanged;
  FBrush := TBrush.Create;
  FBrush.Color := FColor;
end;

{ UIF-037 (handle-less part) }
destructor TWinControl.Destroy;
var
  I: Integer;
  R: TWinControl;
begin
  Destroying;
  R := FocusRoot;                                            { step 2: the focus goes to the form, silently }
  if (R <> nil) and (R <> Self) then
    R.DefocusControl(Self, True);
  SetParent(nil);
  DestroyHandle;
  for I := FControls.Count - 1 downto 0 do
    TControl(FControls[I]).Free;
  inherited Destroy;
  FBrush.Free;
  FPadding.Free;
  FTabList.Free;
  FControls.Free;
end;

{ UIF-102 (layout.md UIF-300 step 4): stable sort by the TabOrder read while loading; unassigned (-1) last }
procedure TWinControl.SortTabList;
var
  I, J: Integer;
  C: Pointer;

  function Key(P: Pointer): Integer;
  begin
    Result := TWinControl(P).FTabOrder;
    if Result < 0 then
      Result := MaxInt;
  end;

begin
  for I := 1 to FTabList.Count - 1 do
  begin
    C := FTabList[I];
    J := I - 1;
    while (J >= 0) and (Key(FTabList[J]) > Key(C)) do
    begin
      FTabList[J + 1] := FTabList[J];
      Dec(J);
    end;
    FTabList[J + 1] := C;
  end;
end;

{ layout.md UIF-300 step 5: one pass on every windowed control of the tree, parents before children }
procedure TWinControl.RealignTree;
var
  I: Integer;
begin
  Realign;
  for I := 0 to FControls.Count - 1 do
    if TObject(FControls[I]) is TWinControl then
      TWinControl(FControls[I]).RealignTree;
end;

procedure TWinControl.ChangeScale(M, D: Integer);
var
  I: Integer;
begin
  if (M = D) or (M <= 0) or (D <= 0) then
    Exit;
  Inc(ScalingLevel);
  try
    FPadding.SetBounds(LayoutMulDiv(FPadding.Left, M, D), LayoutMulDiv(FPadding.Top, M, D),
      LayoutMulDiv(FPadding.Right, M, D), LayoutMulDiv(FPadding.Bottom, M, D));
    inherited ChangeScale(M, D);
    for I := 0 to FControls.Count - 1 do
      TControl(FControls[I]).ChangeScale(M, D);
  finally
    Dec(ScalingLevel);
  end;
end;

procedure TWinControl.CaptureBaselines;
var
  I: Integer;
begin
  CaptureBaseline;
  for I := 0 to FControls.Count - 1 do
    if TObject(FControls[I]) is TWinControl then
      TWinControl(FControls[I]).CaptureBaselines
    else
      TControl(FControls[I]).CaptureBaseline;
end;

{ UIF-227 last row, UIF-300: Loaded runs children first, so the root of the loaded tree (no parent still loading)
  runs the layout of the whole tree after every baseline is captured; OnResize stays silent meanwhile (step 6) }
procedure TWinControl.Loaded;
begin
  inherited Loaded;
  SortTabList;
  if (FParent = nil) or not (csLoading in FParent.ComponentState) then
  begin
    Inc(LoadLayoutLevel);
    try
      RealignTree;
    finally
      Dec(LoadLayoutLevel);
    end;
  end;
  if (FParent <> nil) and (FParent.FHandle <> 0) and (FHandle = 0) then
    HandleNeeded;                                            { UIF-030: loading children get their handle here }
end;

{ ---------------------------------------------------------------- window handles (core.md section 4) }

var
  ControlAtom: string;                                       { window property naming the control (UIF-035) }
  NextCtlID: Cardinal;                                       { next child-window control id, 1..$FFFF cyclic }

function FindControl(Handle: HWND): TWinControl;
var
  Pid: DWORD;
begin
  Result := nil;
  if Handle = 0 then
    Exit;
  Pid := 0;
  GetWindowThreadProcessId(Handle, @Pid);
  if Pid = GetCurrentProcessId then
    Result := TWinControl(Pointer(GetPropW(Handle, PChar(ControlAtom))));
end;

function GetCaptureControl: TControl;
begin
  Result := CaptureControl;
  if (Result = nil) or (GetCapture = 0) then
    Result := FindControl(GetCapture);
end;

procedure SetCaptureControl(Control: TControl);
begin
  CaptureControl := nil;
  if GetCapture <> 0 then
    ReleaseCapture;
  if Control = nil then
    Exit;
  if Control is TWinControl then
    SetCapture(TWinControl(Control).Handle)
  else if Control.Parent <> nil then
    SetCapture(Control.Parent.Handle)
  else
    Exit;
  CaptureControl := Control;
end;

{ UIF-035 }
function StdWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
var
  C: TWinControl;
  M: TMessage;
begin
  C := TWinControl(Pointer(GetPropW(Wnd, PChar(ControlAtom))));
  if (C = nil) and (Msg = WM_NCCREATE) then
  begin
    C := TWinControl(PCreateStructW(LParam)^.lpCreateParams);
    if C <> nil then
    begin
      SetPropW(Wnd, PChar(ControlAtom), THandle(Pointer(C)));
      C.FHandle := Wnd;
    end;
  end;
  if C = nil then
  begin
    Result := DefWindowProcW(Wnd, Msg, WParam, LParam);
    Exit;
  end;
  M.Msg := Msg;
  M.WParam := WParam;
  M.LParam := LParam;
  M.Result := 0;
  C.MainWndProc(M);
  Result := M.Result;
end;

function TWinControl.GetHandle: HWND;
begin
  HandleNeeded;
  Result := FHandle;
end;

{ UIF-031 }
procedure TWinControl.HandleNeeded;
begin
  if FHandle = 0 then
  begin
    if FParent <> nil then
      FParent.HandleNeeded;
    if FHandle = 0 then
      CreateHandle;
  end;
end;

{ core.md section 4.2 }
procedure TWinControl.CreateParams(var Params: TCreateParams);
var
  S: string;
  I, N: Integer;
begin
  FillChar(Params, SizeOf(Params), 0);
  Params.Caption := PChar(FText);
  Params.Style := WS_CHILD or WS_CLIPSIBLINGS;
  Params.ExStyle := 0;
  if csAcceptsControls in FControlStyle then
  begin
    Params.Style := Params.Style or WS_CLIPCHILDREN;
    Params.ExStyle := WS_EX_CONTROLPARENT;
  end;
  if FVisible then
    Params.Style := Params.Style or WS_VISIBLE;
  if not FEnabled then
    Params.Style := Params.Style or WS_DISABLED;
  if FTabStop then
    Params.Style := Params.Style or WS_TABSTOP;
  Params.X := FLeft;
  Params.Y := FTop;
  Params.Width := FWidth;
  Params.Height := FHeight;
  if FParent <> nil then
    Params.WndParent := FParent.FHandle
  else
    Params.WndParent := 0;
  Params.WindowClass.style := CS_DBLCLKS;
  Params.WindowClass.hCursor := 0;
  Params.WindowClass.hbrBackground := 0;
  Params.WindowClass.hInstance := HINST(HInstance);
  S := 'PN.' + ClassName;
  N := Length(S);
  if N > 63 then
    N := 63;
  for I := 0 to N - 1 do
    Params.WinClassName[I] := S[I + 1];
  Params.WinClassName[N] := #0;
  Params.SubclassOf := nil;
end;

{ UIF-032 step 2 }
procedure TWinControl.CreateWnd;
var
  Params: TCreateParams;
  Info: TWndClassExW;
begin
  CreateParams(Params);
  if ((Params.Style and WS_CHILD) <> 0) and (Params.WndParent = 0) then
    raise EInvalidOperation.CreateFmt(SParentRequired, [Name]);
  if Params.SubclassOf = nil then
  begin
    FillChar(Info, SizeOf(Info), 0);
    Info.cbSize := SizeOf(Info);
    if not GetClassInfoExW(HINST(HInstance), @Params.WinClassName[0], Info) then
    begin
      Params.WindowClass.cbSize := SizeOf(TWndClassExW);
      Params.WindowClass.lpfnWndProc := @StdWndProc;
      Params.WindowClass.lpszClassName := @Params.WinClassName[0];
      if RegisterClassExW(Params.WindowClass) = 0 then
        raise EOutOfResources.CreateFmt(SWindowClass, [SysErrorMessage(GetLastError)]);
    end;
    FDefWndProc := @DefWindowProcW;
  end;
  FOwnClass := Params.SubclassOf = nil;
  CreateWindowHandle(Params);
  if FHandle = 0 then
    raise EOutOfResources.CreateFmt(SWindowCreate, [SysErrorMessage(GetLastError)]);
  { UIF-147: Win32 links a new child window at the bottom of its siblings; ours goes directly below the nearest
    later windowed sibling that has a handle, on top when there is none (list order = Z order) }
  if (Params.Style and WS_CHILD) <> 0 then
    SetWindowPos(FHandle, WindowAbove, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE);
  SendMessageW(FHandle, WM_SETFONT, WPARAM(FFont.Handle), 0);
end;

{ UIF-032 step 2c-2e }
procedure TWinControl.CreateWindowHandle(const Params: TCreateParams);
var
  ClassName: PChar;
  H: HWND;
  Menu: HMENU;
begin
  if Params.SubclassOf <> nil then
    ClassName := Params.SubclassOf
  else
    ClassName := @Params.WinClassName[0];
  { UIF-070: child windows carry a non-zero control id (WM_MEASUREITEM names the control by it) }
  if (Params.Style and WS_CHILD) <> 0 then
  begin
    if FCtlID = 0 then
    begin
      FCtlID := NextCtlID;
      if NextCtlID >= $FFFF then
        NextCtlID := 1
      else
        Inc(NextCtlID);
    end;
    Menu := HMENU(FCtlID);
  end
  else
    Menu := WindowMenu;
  H := CreateWindowExW(Params.ExStyle, ClassName, Params.Caption, Params.Style, Params.X, Params.Y, Params.Width,
    Params.Height, Params.WndParent, Menu, HINST(HInstance), Pointer(Self));
  if H = 0 then
    Exit;
  if Params.SubclassOf <> nil then
  begin
    SetPropW(H, PChar(ControlAtom), THandle(Pointer(Self)));
    FHandle := H;
    FDefWndProc := Pointer(SetWindowLongPtrW(H, GWLP_WNDPROC, LONG_PTR(@StdWndProc)));
  end
  else
    FHandle := H;
end;

function TWinControl.WindowMenu: HMENU;
begin
  Result := 0;
end;

{ UIF-032 }
procedure TWinControl.CreateHandle;
var
  I: Integer;
  C: TControl;
begin
  if FHandle <> 0 then
    Exit;
  Include(FControlState, csCreating);
  try
    CreateWnd;
    for I := 0 to FControls.Count - 1 do
    begin
      C := TControl(FControls[I]);
      if (C is TWinControl) and (TWinControl(C).FHandle = 0) then
        TWinControl(C).CreateHandle;
    end;
  finally
    Exclude(FControlState, csCreating);
  end;
end;

{ UIF-036 }
procedure TWinControl.DestroyHandle;
var
  I: Integer;
  C: TControl;
begin
  if FHandle = 0 then
    Exit;
  Include(FControlState, csDestroyingHandle);
  try
    { children first, so that each DestroyWnd can save its native state (UIF-034) }
    for I := FControls.Count - 1 downto 0 do
    begin
      C := TControl(FControls[I]);
      if C is TWinControl then
        TWinControl(C).DestroyHandle;
    end;
    DestroyWnd;
  finally
    Exclude(FControlState, csDestroyingHandle);
  end;
end;

{ UIF-034: native state is saved into the fields before the window goes }
procedure TWinControl.DestroyWnd;
var
  S: string;
begin
  S := GetText;
  FText := S;
  DestroyWindowHandle;
end;

procedure TWinControl.DestroyWindowHandle;
begin
  if FHandle <> 0 then
    DestroyWindow(FHandle);
  FHandle := 0;
end;

{ UIF-147: the window of the nearest later windowed sibling that has a handle; HWND_TOP when none }
function TWinControl.WindowAbove: HWND;
var
  I: Integer;
  C: TControl;
begin
  Result := HWND_TOP;
  if FParent = nil then
    Exit;
  for I := FParent.FControls.IndexOf(Pointer(Self)) + 1 to FParent.FControls.Count - 1 do
  begin
    C := TControl(FParent.FControls[I]);
    if (C is TWinControl) and (TWinControl(C).FHandle <> 0) then
    begin
      Result := TWinControl(C).FHandle;
      Exit;
    end;
  end;
end;

{ UIF-039 }
procedure TWinControl.RecreateWnd;
var
  F, Above: HWND;
  Focused: TWinControl;
  IsChildWindow: Boolean;
begin
  if FHandle = 0 then
    Exit;
  { the control owning the focus window (a native control may focus an inner window of its own) }
  F := GetFocus;
  Focused := nil;
  while (F <> 0) and (Focused = nil) do
  begin
    Focused := FindControl(F);
    if Focused = nil then
      F := GetParent(F);
  end;
  if (Focused <> nil) and not ContainsControl(Focused) then
    Focused := nil;
  IsChildWindow := (GetWindowLongPtrW(FHandle, GWL_STYLE) and WS_CHILD) <> 0;
  Above := GetWindow(FHandle, GW_HWNDPREV);
  DestroyHandle;
  CreateHandle;
  if IsChildWindow then
    SetWindowPos(FHandle, Above, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE);
  if Focused <> nil then
    Windows.SetFocus(Focused.Handle);
end;

{ UIF-060: the exception never unwinds into the system; without an application hook (no Forms unit) it is
  reported through SysUtils.ShowException, EAbort excepted }
procedure TWinControl.MainWndProc(var Message: TMessage);
begin
  try
    FWindowProc(Message);
  except
    if Assigned(ApplicationHandleException) then
      ApplicationHandleException(Self)
    else if not (ExceptObject is EAbort) then
      ShowException(ExceptObject, ExceptAddr);
  end;
end;

{ UIF-060: framework handlers by case; everything else goes to Dispatch (message methods, DefaultHandler) }
procedure TWinControl.WndProc(var Message: TMessage);
var
  C: TWinControl;
begin
  if (Message.Msg >= WM_MOUSEFIRST) and (Message.Msg <= WM_MOUSEBUTTONLAST) and RouteMouse(Message) then
    Exit;
  case Message.Msg of
    { core.md 7.2 steps 1-2 }
    WM_SETFOCUS:
      begin
        C := FocusRoot;
        if (C = nil) or (C = Self) or C.SetFocusedControl(Self) then
          inherited WndProc(Message);
      end;
    WM_KILLFOCUS:
      if not (csFocusing in FControlState) then
        inherited WndProc(Message);
    CM_ENTER:
      CMEnter(Message);
    CM_EXIT:
      CMExit(Message);
    WM_NCDESTROY:
      WMNCDestroy(TWMNoParams(Message));
    WM_WINDOWPOSCHANGED:
      WMWindowPosChanged(TWMWindowPosMsg(Message));
    WM_ENABLE:
      WMEnable(Message);
    WM_MOUSELEAVE:
      WMMouseLeave(Message);
    WM_SETCURSOR:
      WMSetCursor(Message);
    WM_KEYDOWN, WM_SYSKEYDOWN:
      WMKeyDown(TWMKey(Message));
    WM_KEYUP, WM_SYSKEYUP:
      WMKeyUp(TWMKey(Message));
    WM_CHAR:
      WMChar(TWMKey(Message));
    { UIF-070: reflection to the child object; without a target, default processing }
    WM_COMMAND:
      if (Message.LParam = 0) or not Reflect(FindControl(HWND(Message.LParam)), Message) then
        inherited WndProc(Message);
    WM_HSCROLL:
      if Message.LParam = 0 then
        WMHScroll(TWMScroll(Message))
      else if not Reflect(FindControl(HWND(Message.LParam)), Message) then
        inherited WndProc(Message);
    WM_VSCROLL:
      if Message.LParam = 0 then
        WMVScroll(TWMScroll(Message))
      else if not Reflect(FindControl(HWND(Message.LParam)), Message) then
        inherited WndProc(Message);
    WM_NOTIFY:
      if (Message.LParam = 0) or not Reflect(FindControl(PNMHdr(Message.LParam)^.hwndFrom), Message) then
        inherited WndProc(Message);
    WM_DRAWITEM:
      if (Message.WParam = 0)
        or not Reflect(FindControl(PDrawItemStruct(Message.LParam)^.hwndItem), Message) then
        inherited WndProc(Message);
    WM_MEASUREITEM:
      if (Message.WParam = 0) or not Reflect(ChildByCtlID(Cardinal(Message.WParam)), Message) then
        inherited WndProc(Message);
    WM_CTLCOLORMSGBOX..WM_CTLCOLORSTATIC:
      begin
        { UIF-070: answered by the child control of HWND LParam; default processing when it has none or declines }
        C := FindControl(HWND(Message.LParam));
        if (C <> nil) and (C <> Self) then
          Message.Result := C.Perform(CN_BASE + Message.Msg, Message.WParam, Message.LParam);
        if Message.Result = 0 then
          inherited WndProc(Message);
      end;
    CN_BASE + WM_CTLCOLORMSGBOX..CN_BASE + WM_CTLCOLORSTATIC:
      CNCtlColor(Message);
    WM_ERASEBKGND:
      WMEraseBkgnd(TWMEraseBkgnd(Message));
    WM_PAINT:
      WMPaint(TWMPaint(Message));
    WM_PRINTCLIENT:
      WMPrintClient(Message);
  else
    inherited WndProc(Message);
  end;
end;

{ UIF-064: to the graphic child holding the capture, else to the enabled visible graphic child under the point,
  in its own coordinates }
function TWinControl.RouteMouse(var Message: TMessage): Boolean;
var
  X, Y: Integer;
  C: TControl;
begin
  X := SmallInt(LoWord(Message.LParam));
  Y := SmallInt(HiWord(Message.LParam));
  if (CaptureControl <> nil) and not (CaptureControl is TWinControl) and (CaptureControl.FParent = Self) then
    C := CaptureControl
  else
    C := ControlAtPos(Point(X, Y), False);
  Result := C <> nil;
  if Result then
    Message.Result := C.Perform(Message.Msg, Message.WParam, MakeLParam(Word(X - C.FLeft), Word(Y - C.FTop)));
end;

{ UIF-070: CN_BASE + Msg to the target object, whose result is the answer; False = no target }
function TWinControl.Reflect(C: TWinControl; var Message: TMessage): Boolean;
begin
  Result := (C <> nil) and (C <> Self);
  if Result then
    Message.Result := C.Perform(CN_BASE + Message.Msg, Message.WParam, Message.LParam);
end;

{ the windowed child with this control id (assigned before its window exists, so it works during creation) }
function TWinControl.ChildByCtlID(ID: Cardinal): TWinControl;
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to FControls.Count - 1 do
  begin
    C := TControl(FControls[I]);
    if (C is TWinControl) and (TWinControl(C).FCtlID = ID) then
    begin
      Result := TWinControl(C);
      Exit;
    end;
  end;
  Result := nil;
end;

procedure TWinControl.WMHScroll(var Message: TWMScroll);
begin
  Dispatch(Message);
end;

procedure TWinControl.WMVScroll(var Message: TWMScroll);
begin
  Dispatch(Message);
end;

{ UIF-068: leaves only when the tracked control is Self or one of its graphic children }
procedure TWinControl.WMMouseLeave(var Message: TMessage);
var
  Old: TControl;
begin
  Old := MouseControl;
  if (Old <> nil) and ((Old = Self) or (not (Old is TWinControl) and (Old.FParent = Self))) then
  begin
    MouseControl := nil;
    Old.Perform(CM_MOUSELEAVE, 0, 0);
    if Assigned(MouseControlHook) and (MouseControl = nil) then
      MouseControlHook(nil);
  end;
  Dispatch(Message);
end;

procedure TWinControl.WMSetCursor(var Message: TMessage);
var
  P: TPoint;
  C: TControl;
begin
  if (LoWord(Message.LParam) <> HTCLIENT) or (FHandle = 0) then
  begin
    Dispatch(Message);
    Exit;
  end;
  GetCursorPos(P);
  ScreenToClient(FHandle, P);
  C := ControlAtPos(P, False);
  if C = nil then
    C := Self;
  Windows.SetCursor(CursorHandle(EffectiveCursor(C)));
  Message.Result := 1;
end;

{ UIF-077: the graphic child under the point gets the first chance }
procedure TWinControl.WMContextMenu(var Message: TMessage);
var
  P: TPoint;
  C: TControl;
  Handled: Boolean;
begin
  P := Point(SmallInt(LoWord(Message.LParam)), SmallInt(HiWord(Message.LParam)));
  if ((P.X <> -1) or (P.Y <> -1)) and (FHandle <> 0) then
  begin
    ScreenToClient(FHandle, P);
    C := ControlAtPos(P, False);
    if C <> nil then
    begin
      Handled := False;
      C.DoContextPopup(Point(P.X - C.FLeft, P.Y - C.FTop), Handled);
      if Handled or C.ShowPopupMenu(Point(SmallInt(LoWord(Message.LParam)), SmallInt(HiWord(Message.LParam)))) then
      begin
        Message.Result := 1;
        Exit;
      end;
    end;
  end;
  inherited WMContextMenu(Message);
end;

{ UIF-071 }
procedure TWinControl.CNCtlColor(var Message: TMessage);
begin
  SetTextColor(HDC(Message.WParam), COLORREF(ColorToRGB(FFont.Color)));
  SetBkColor(HDC(Message.WParam), COLORREF(ColorToRGB(FBrush.Color)));
  Message.Result := LRESULT(FBrush.Handle);
end;

procedure TWinControl.CMColorChanged(var Message: TMessage);
begin
  FBrush.Color := FColor;
  inherited CMColorChanged(Message);
  NotifyControls(CM_PARENTCOLORCHANGED);
end;

procedure TWinControl.CMFontChanged(var Message: TMessage);
begin
  if FHandle <> 0 then
    SendMessageW(FHandle, WM_SETFONT, WPARAM(FFont.Handle), 1);
  inherited CMFontChanged(Message);
  NotifyControls(CM_PARENTFONTCHANGED);
end;

{ layout.md UIF-211: the window follows the fields; the feedback of this very call is suppressed }
procedure TWinControl.BoundsChanged(const OldBounds: TRect);
begin
  if (FHandle = 0) or LayoutDumpMode then
    Exit;
  FSettingBounds := True;
  try
    SetWindowPos(FHandle, 0, FLeft, FTop, FWidth, FHeight, SWP_NOZORDER or SWP_NOACTIVATE);
  finally
    FSettingBounds := False;
  end;
end;

{ (left, top, width, height) of the window; a child window in its parent's client coordinates }
function TWinControl.WindowBounds: TRect;
var
  R: TRect;
  P: HWND;
begin
  GetWindowRect(FHandle, R);
  if (GetWindowLongPtrW(FHandle, GWL_STYLE) and WS_CHILD) <> 0 then
  begin
    P := GetParent(FHandle);
    if P <> 0 then
      MapWindowPoints(0, P, @R, 2);
  end;
  Result := Rect(R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top);
end;

{ layout.md UIF-213 }
procedure TWinControl.WMWindowPosChanged(var Message: TWMWindowPosMsg);
begin
  DefaultHandler(Message);
  if (FHandle <> 0) and not FSettingBounds and not LayoutDumpMode then
    UpdateBoundsFromOS(WindowBounds);
end;

{ core.md 7.10: an external EnableWindow syncs the field (no CM_ENABLEDCHANGED) }
procedure TWinControl.WMEnable(var Message: TMessage);
begin
  DefaultHandler(Message);
  FEnabled := Message.WParam <> 0;
end;

{ UIF-130; UIF-132: the focus leaves first }
procedure TWinControl.CMEnabledChanged(var Message: TMessage);
var
  R: TWinControl;
begin
  R := FocusRoot;
  if not FEnabled and (R <> nil) and (R <> Self) then
    R.DefocusControl(Self, False);
  if FHandle <> 0 then
    EnableWindow(FHandle, FEnabled);
  inherited CMEnabledChanged(Message);
end;

{ UIF-136: shown or hidden without activation; UIF-137: the focus leaves first }
procedure TWinControl.CMVisibleChanged(var Message: TMessage);
const
  Flags: array[Boolean] of Cardinal = (SWP_HIDEWINDOW, SWP_SHOWWINDOW);
var
  R: TWinControl;
begin
  R := FocusRoot;
  if not FVisible and (R <> nil) and (R <> Self) then
    R.DefocusControl(Self, False);
  if FHandle <> 0 then
    SetWindowPos(FHandle, 0, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_NOACTIVATE
      or Flags[FVisible]);
end;

{ core.md 2.1: a top-level window shows while its window is visible (forms refine this, application-forms.md) }
function TWinControl.GetShowing: Boolean;
begin
  if FParent = nil then
    Result := FVisible and (FHandle <> 0) and IsWindowVisible(FHandle)
  else
    Result := inherited GetShowing;
end;

{ UIF-036: the window is gone; detach the object }
procedure TWinControl.WMNCDestroy(var Message: TWMNoParams);
begin
  DefaultHandler(Message);
  if FHandle <> 0 then
    RemovePropW(FHandle, PChar(ControlAtom));
  FHandle := 0;
end;

procedure TWinControl.DefaultHandler(var Message);
var
  M: PMessage;
begin
  M := @Message;
  if FHandle <> 0 then
    M^.Result := CallWindowProcW(TFNWndProc(FDefWndProc), FHandle, M^.Msg, M^.WParam, M^.LParam)
  else
    inherited DefaultHandler(Message);
end;

procedure TWinControl.SetPadding(Value: TPadding);
begin
  FPadding.Assign(Value);
end;

procedure TWinControl.PaddingChanged(Sender: TObject);
begin
  Realign;
end;

procedure TWinControl.AdjustClientRect(var Rect: TRect);
begin
end;

function TWinControl.HandleAllocated: Boolean;
begin
  Result := FHandle <> 0;
end;

function TWinControl.GetControl(Index: Integer): TControl;
begin
  Result := TControl(FControls[Index]);
end;

function TWinControl.GetControlCount: Integer;
begin
  Result := FControls.Count;
end;

{ UIF-047 step 3 }
procedure TWinControl.InsertControl(AControl: TControl);
begin
  FControls.Add(Pointer(AControl));
  if AControl is TWinControl then
    FTabList.Add(Pointer(AControl));
  AControl.FParent := Self;
  AControl.Perform(CM_PARENTFONTCHANGED, 0, 0);
  AControl.Perform(CM_PARENTCOLORCHANGED, 0, 0);
  AControl.Perform(CM_PARENTSHOWHINTCHANGED, 0, 0);
  AControl.CaptureBaseline;                                  { UIF-232 case 2 }
  if (AControl is TWinControl) and (FHandle <> 0) and not (csLoading in AControl.ComponentState) then
    TWinControl(AControl).HandleNeeded;                      { UIF-030 third bullet }
  AlignControl(AControl);
end;

{ UIF-047 step 1 }
procedure TWinControl.RemoveControl(AControl: TControl);
begin
  if AControl is TWinControl then
    TWinControl(AControl).DestroyHandle;
  FTabList.Remove(Pointer(AControl));
  FControls.Remove(Pointer(AControl));
  AControl.FParent := nil;
  AlignControl(nil);
end;

{ UIF-225, UIF-226: a pass requested while loading, destroying or align-disabled only sets the flag; a request
  during the running pass of the same parent is ignored }
procedure TWinControl.AlignControl(AControl: TControl);
var
  R: TRect;
begin
  if (FAlignLevel > 0) or (ScalingLevel > 0) or (csLoading in ComponentState) or (csDestroying in ComponentState) then
  begin
    Include(FControlState, csAlignmentNeeded);
    Exit;
  end;
  if FAligning then
    Exit;
  FAligning := True;
  try
    Exclude(FControlState, csAlignmentNeeded);
    { UIF-220: the align rect }
    R := GetClientRect;
    AdjustClientRect(R);
    R.Left := R.Left + FPadding.Left;
    R.Top := R.Top + FPadding.Top;
    R.Right := R.Right - FPadding.Right;
    R.Bottom := R.Bottom - FPadding.Bottom;
    AlignControls(AControl, R);
  finally
    FAligning := False;
  end;
end;

procedure TWinControl.DisableAlign;
begin
  Inc(FAlignLevel);
end;

procedure TWinControl.EnableAlign;
begin
  Dec(FAlignLevel);
  if (FAlignLevel = 0) and (csAlignmentNeeded in FControlState) then
    Realign;
end;

procedure TWinControl.Realign;
begin
  AlignControl(nil);
end;

function TWinControl.AlignDisabled: Boolean;
begin
  Result := FAlignLevel > 0;
end;

{ UIF-222: Place(C, Phase, R) }
procedure PlaceControl(C: TControl; Phase: TAlign; var R: TRect);
var
  ml, mt, mr, mb, AW, AH, W, H: Integer;
begin
  if C.FAlignWithMargins then
  begin
    ml := C.FMargins.Left;
    mt := C.FMargins.Top;
    mr := C.FMargins.Right;
    mb := C.FMargins.Bottom;
  end
  else
  begin
    ml := 0;
    mt := 0;
    mr := 0;
    mb := 0;
  end;
  AW := R.Right - R.Left - ml - mr;
  if AW < 0 then
    AW := 0;
  AH := R.Bottom - R.Top - mt - mb;
  if AH < 0 then
    AH := 0;
  case Phase of
    alTop, alBottom:
      begin
        W := AW;
        H := C.FHeight;
      end;
    alLeft, alRight:
      begin
        W := C.FWidth;
        H := AH;
      end;
  else
    W := AW;
    H := AH;
  end;
  C.ConstrainSize(W, H);
  C.FLayoutCall := True;
  case Phase of
    alTop:
      begin
        C.SetBounds(R.Left + ml, R.Top + mt, W, H);
        R.Top := R.Top + H + mt + mb;
      end;
    alBottom:
      begin
        C.SetBounds(R.Left + ml, R.Bottom - mb - H, W, H);
        R.Bottom := R.Bottom - (H + mt + mb);
      end;
    alLeft:
      begin
        C.SetBounds(R.Left + ml, R.Top + mt, W, H);
        R.Left := R.Left + W + ml + mr;
      end;
    alRight:
      begin
        C.SetBounds(R.Right - mr - W, R.Top + mt, W, H);
        R.Right := R.Right - (W + ml + mr);
      end;
  else
    C.SetBounds(R.Left + ml, R.Top + mt, W, H);
  end;
  C.FLayoutCall := False;
end;

function AlignKey(C: TControl; Phase: TAlign): Integer;
begin
  case Phase of
    alTop: Result := C.FTop;
    alBottom: Result := -(C.FTop + C.FHeight);
    alLeft: Result := C.FLeft;
    alRight: Result := -(C.FLeft + C.FWidth);
  else
    Result := 0;
  end;
end;

procedure TWinControl.AlignControls(AControl: TControl; var Rect: TRect);
var
  Phase: TAlign;
  Count, I, J, K: Integer;
  Keys: array of Integer;
  List: array of TControl;
  C: TControl;
begin
  { keys of every phase are evaluated on the bounds the children have when the pass starts }
  Count := FControls.Count;
  SetLength(Keys, Count);
  SetLength(List, Count);
  for Phase := alTop to alClient do
  begin
    K := 0;
    for I := 0 to Count - 1 do
    begin
      C := TControl(FControls[I]);
      if (C.FAlign = Phase) and C.FVisible then
      begin
        { stable insertion by key; equal keys keep Controls[] order }
        J := K;
        while (J > 0) and (Keys[J - 1] > AlignKey(C, Phase)) do
        begin
          Keys[J] := Keys[J - 1];
          List[J] := List[J - 1];
          Dec(J);
        end;
        Keys[J] := AlignKey(C, Phase);
        List[J] := C;
        Inc(K);
      end;
    end;
    for I := 0 to K - 1 do
      PlaceControl(List[I], Phase, Rect);
  end;
  for I := 0 to FControls.Count - 1 do
  begin
    C := TControl(FControls[I]);
    if C.FAlign = alNone then
      ApplyAnchors(C);
  end;
end;

{ UIF-231 }
procedure TWinControl.ApplyAnchors(C: TControl);
var
  PW, PH, BL, BT, BW, BH, L, T, W, H: Integer;
  Offset: TPoint;
begin
  PW := ClientWidth;
  PH := ClientHeight;
  BL := C.FBaseBounds.Left;
  BT := C.FBaseBounds.Top;
  BW := C.FBaseBounds.Right;
  BH := C.FBaseBounds.Bottom;
  W := BW;
  H := BH;
  if akLeft in C.FAnchors then
  begin
    L := BL;
    if akRight in C.FAnchors then
    begin
      W := BW + PW - C.FBaseParentWidth;
      if W < 0 then
        W := 0;
    end;
  end
  else if akRight in C.FAnchors then
    L := BL + PW - C.FBaseParentWidth
  else if C.FBaseParentWidth > 0 then
    L := LayoutMulDiv(BL + BW div 2, PW, C.FBaseParentWidth) - BW div 2
  else
    L := BL;
  if akTop in C.FAnchors then
  begin
    T := BT;
    if akBottom in C.FAnchors then
    begin
      H := BH + PH - C.FBaseParentHeight;
      if H < 0 then
        H := 0;
    end;
  end
  else if akBottom in C.FAnchors then
    T := BT + PH - C.FBaseParentHeight
  else if C.FBaseParentHeight > 0 then
    T := LayoutMulDiv(BT + BH div 2, PH, C.FBaseParentHeight) - BH div 2
  else
    T := BT;
  C.ConstrainSize(W, H);
  Offset := ScrollOffset;                                    { UIF-234 }
  C.FLayoutCall := True;
  C.SetBounds(L - Offset.X, T - Offset.Y, W, H);
  C.FLayoutCall := False;
end;

function TWinControl.ScrollOffset: TPoint;
begin
  Result := Point(0, 0);
end;

procedure TWinControl.ScrollBy(DeltaX, DeltaY: Integer);
var
  I: Integer;
  C: TControl;
begin
  if (DeltaX = 0) and (DeltaY = 0) then
    Exit;
  for I := 0 to FControls.Count - 1 do
  begin
    C := TControl(FControls[I]);
    C.FLayoutCall := True;
    C.SetBounds(C.FLeft + DeltaX, C.FTop + DeltaY, C.FWidth, C.FHeight);
    C.FLayoutCall := False;
  end;
  if (FHandle <> 0) and not LayoutDumpMode then
    ScrollWindowEx(FHandle, DeltaX, DeltaY, nil, nil, 0, nil, SW_INVALIDATE or SW_ERASE);
end;

procedure TWinControl.MoveChild(AControl: TControl; NewIndex: Integer);
var
  I: Integer;
begin
  I := FControls.IndexOf(Pointer(AControl));
  if (I >= 0) and (I <> NewIndex) then
  begin
    FControls.Move(I, NewIndex);
    { UIF-146, UIF-147: the window goes directly below the next windowed sibling with a handle (top if none) }
    if (AControl is TWinControl) and (TWinControl(AControl).FHandle <> 0) then
      SetWindowPos(TWinControl(AControl).FHandle, TWinControl(AControl).WindowAbove, 0, 0, 0, 0,
        SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE)
    else
      AControl.Invalidate;
    AlignControl(nil);                                       { UIF-148 }
  end;
end;

{ UIF-149 }
procedure TWinControl.SetChildOrder(Child: TComponent; Order: Integer);
begin
  if (Child is TControl) and (TControl(Child).FParent = Self) then
  begin
    if Order < 0 then
      Order := 0;
    if Order > FControls.Count - 1 then
      Order := FControls.Count - 1;
    MoveChild(TControl(Child), Order);
  end;
end;

{ UIF-051 }
procedure TWinControl.GetChildren(Proc: TGetChildProc; Root: TComponent);
var
  I: Integer;
  C: TControl;
begin
  for I := 0 to FControls.Count - 1 do
  begin
    C := TControl(FControls[I]);
    if C.Owner = Root then
      Proc(C);
  end;
end;

{ UIF-101 (outside loading) }
function TWinControl.GetTabOrder: TTabOrder;
begin
  if FParent <> nil then
    Result := FParent.FTabList.IndexOf(Pointer(Self))
  else
    Result := -1;
end;

procedure TWinControl.SetTabOrder(Value: TTabOrder);
var
  Cur: Integer;
begin
  if csLoading in ComponentState then
  begin
    FTabOrder := Value;
    Exit;
  end;
  if (FParent = nil) or (Value < 0) then
    Exit;
  Cur := FParent.FTabList.IndexOf(Pointer(Self));
  if Value > FParent.FTabList.Count - 1 then
    Value := FParent.FTabList.Count - 1;
  if (Cur >= 0) and (Cur <> Value) then
    FParent.FTabList.Move(Cur, Value);
end;

procedure TWinControl.SetTabStop(Value: Boolean);
begin
  FTabStop := Value;
end;

function TWinControl.ControlAtPos(const Pos: TPoint; AllowDisabled: Boolean; AllowWinControls: Boolean): TControl;
var
  I: Integer;
  C: TControl;
begin
  for I := FControls.Count - 1 downto 0 do
  begin
    C := TControl(FControls[I]);
    if C.FVisible and (C.FEnabled or AllowDisabled) and (AllowWinControls or not (C is TWinControl))
      and (Pos.X >= C.FLeft) and (Pos.X < C.FLeft + C.FWidth) and (Pos.Y >= C.FTop) and (Pos.Y < C.FTop + C.FHeight) then
    begin
      Result := C;
      Exit;
    end;
  end;
  Result := nil;
end;

function TWinControl.FindChildControl(const ControlName: string): TControl;
var
  I: Integer;
begin
  for I := 0 to FControls.Count - 1 do
    if SameText(TControl(FControls[I]).Name, ControlName) then
    begin
      Result := TControl(FControls[I]);
      Exit;
    end;
  Result := nil;
end;

function TWinControl.ContainsControl(Control: TControl): Boolean;
begin
  while (Control <> nil) and (Control <> Self) do
    Control := Control.FParent;
  Result := Control <> nil;
end;

procedure TWinControl.Broadcast(var Message);
var
  I: Integer;
begin
  for I := 0 to FControls.Count - 1 do
  begin
    TControl(FControls[I]).WindowProc(TMessage(Message));
    if TMessage(Message).Result <> 0 then
      Exit;
  end;
end;

procedure TWinControl.NotifyControls(Msg: Word);
var
  Message: TMessage;
begin
  Message.Msg := Msg;
  Message.WParam := 0;
  Message.LParam := 0;
  Message.Result := 0;
  Broadcast(Message);
end;

procedure TWinControl.DoEnter;
begin
  if Assigned(FOnEnter) then
    FOnEnter(Self);
end;

procedure TWinControl.DoExit;
begin
  if Assigned(FOnExit) then
    FOnExit(Self);
end;

procedure TWinControl.CMEnter(var Message: TMessage);
begin
  DoEnter;
end;

procedure TWinControl.CMExit(var Message: TMessage);
begin
  DoExit;
end;

{ ---------------------------------------------------------------- focus and tab order (core.md section 8) }

{ the top of the parent chain when it is a form (FocusRootClass), else nil }
function TWinControl.FocusRoot: TWinControl;
begin
  Result := Self;
  while Result.FParent <> nil do
    Result := Result.FParent;
  if (FocusRootClass = nil) or not Result.InheritsFrom(FocusRootClass) then
    Result := nil;
end;

{ UIF-090 }
function TWinControl.CanFocus: Boolean;
var
  C: TWinControl;
begin
  Result := False;
  C := Self;
  while C <> nil do
  begin
    if not (C.FVisible and C.FEnabled) then
      Exit;
    if C.FParent = nil then
      Break;
    C := C.FParent;
  end;
  Result := (FocusRootClass <> nil) and C.InheritsFrom(FocusRootClass);
end;

function TWinControl.Focused: Boolean;
begin
  Result := (FHandle <> 0) and (GetFocus = FHandle);
end;

{ UIF-091 }
procedure TWinControl.SetFocus;
var
  R: TWinControl;
begin
  if not CanFocus then
    raise EInvalidOperation.Create(SCannotFocus);
  R := FocusRoot;
  if R.SetFocusedControl(Self) and R.HandleAllocated and (GetActiveWindow = R.Handle) then
    Windows.SetFocus(Handle);
end;

{ UIF-103 }
procedure TWinControl.GetTabOrderList(List: TList);
var
  I: Integer;
  C: TWinControl;
begin
  for I := 0 to FTabList.Count - 1 do
  begin
    C := TWinControl(FTabList[I]);
    List.Add(Pointer(C));
    C.GetTabOrderList(List);
  end;
end;

{ UIF-104 }
function TWinControl.FindNextControl(CurControl: TWinControl; GoForward, CheckTabStop,
  CheckParent: Boolean): TWinControl;
var
  List: TList;
  N, Start, K, I: Integer;
  C: TWinControl;
begin
  Result := nil;
  List := TList.Create;
  try
    GetTabOrderList(List);
    N := List.Count;
    Start := List.IndexOf(Pointer(CurControl));
    for K := 1 to N do
    begin
      if GoForward then
      begin
        if Start < 0 then
          I := K - 1
        else
          I := (Start + K) mod N;
      end
      else if Start < 0 then
        I := N - K
      else
        I := (Start - K + N) mod N;
      C := TWinControl(List[I]);
      if C.CanFocus and (not CheckTabStop or C.FTabStop)
        and (not CheckParent or ((CurControl <> nil) and (C.FParent = CurControl.FParent))) then
      begin
        Result := C;
        Exit;
      end;
    end;
  finally
    List.Free;
  end;
end;

{ UIF-105 }
procedure TWinControl.SelectNext(CurControl: TWinControl; GoForward, CheckTabStop: Boolean);
var
  C: TWinControl;
begin
  C := FindNextControl(CurControl, GoForward, CheckTabStop, False);
  if C <> nil then
    C.SetFocus;
end;

procedure TWinControl.SelectFirst;
begin
  SelectNext(nil, True, True);
end;

function TWinControl.SetFocusedControl(Control: TWinControl): Boolean;
begin
  Result := True;
end;

procedure TWinControl.DefocusControl(Control: TWinControl; Removing: Boolean);
begin
end;

procedure TWinControl.KeyDown(var Key: Word; Shift: TShiftState);
begin
  if Assigned(FOnKeyDown) then
    FOnKeyDown(Self, Key, Shift);
end;

procedure TWinControl.KeyUp(var Key: Word; Shift: TShiftState);
begin
  if Assigned(FOnKeyUp) then
    FOnKeyUp(Self, Key, Shift);
end;

procedure TWinControl.KeyPress(var Key: Char);
begin
  if Assigned(FOnKeyPress) then
    FOnKeyPress(Self, Key);
end;

function TWinControl.PreviewKeys: Boolean;
begin
  Result := False;
end;

{ core.md 7.9: ssShift, ssCtrl from GetKeyState; ssAlt from bit 29 of the key data }
function KeyDataToShiftState(KeyData: LPARAM): TShiftState;
begin
  Result := [];
  if GetKeyState(VK_SHIFT) < 0 then
    Include(Result, ssShift);
  if GetKeyState(VK_CONTROL) < 0 then
    Include(Result, ssCtrl);
  if (KeyData and $20000000) <> 0 then
    Include(Result, ssAlt);
end;

{ UIF-085 step 1: the top of the parent chain when it previews keys and is not Self }
function KeyPreviewer(C: TWinControl): TWinControl;
begin
  Result := C;
  while Result.FParent <> nil do
    Result := Result.FParent;
  if (Result = C) or not Result.PreviewKeys then
    Result := nil;
end;

{ UIF-085 steps 1, 2, 4: Key = 0 consumes the key and the WM_CHAR translated from it (the mark is cleared by the
  next key-down or key-up, U-123) }
procedure TWinControl.WMKeyDown(var Message: TWMKey);
var
  Key: Word;
  Shift: TShiftState;
  P: TWinControl;
begin
  Key := Message.CharCode;
  Shift := KeyDataToShiftState(Message.KeyData);
  P := KeyPreviewer(Self);
  if P <> nil then
    P.KeyDown(Key, Shift);
  if Key <> 0 then
    KeyDown(Key, Shift);
  FSwallowChar := Key = 0;
  if Key = 0 then
    Message.Result := 0
  else
    Dispatch(Message);
end;

procedure TWinControl.WMKeyUp(var Message: TWMKey);
var
  Key: Word;
  Shift: TShiftState;
  P: TWinControl;
begin
  { U-123: the drop mark covers only the character translated from the key, which arrives before its key-up }
  FSwallowChar := False;
  Key := Message.CharCode;
  Shift := KeyDataToShiftState(Message.KeyData);
  P := KeyPreviewer(Self);
  if P <> nil then
    P.KeyUp(Key, Shift);
  if Key <> 0 then
    KeyUp(Key, Shift);
  if Key = 0 then
    Message.Result := 0
  else
    Dispatch(Message);
end;

{ UIF-085 step 3: #0 consumes; a changed character replaces WParam before default processing }
procedure TWinControl.WMChar(var Message: TWMKey);
var
  Key: Char;
  P: TWinControl;
begin
  if FSwallowChar then
  begin
    FSwallowChar := False;
    Message.Result := 0;
    Exit;
  end;
  Key := Char(Message.CharCode);
  P := KeyPreviewer(Self);
  if P <> nil then
    P.KeyPress(Key);
  if Key <> #0 then
    KeyPress(Key);
  if Key = #0 then
    Message.Result := 0
  else
  begin
    Message.CharCode := Word(Key);
    Dispatch(Message);
  end;
end;

{ ---------------------------------------------------------------- painting (core.md 7.8) }

procedure TWinControl.Invalidate;
begin
  if FHandle <> 0 then
    InvalidateRect(FHandle, nil, True);
end;

procedure TWinControl.Update;
begin
  if FHandle <> 0 then
    UpdateWindow(FHandle)
  else
    inherited Update;
end;

{ UIF-080 }
procedure TWinControl.WMEraseBkgnd(var Message: TWMEraseBkgnd);
begin
  if not FOwnClass then
  begin
    DefaultHandler(Message);
    Exit;
  end;
  { UIF-560: a buffered paint erases into its memory bitmap; the window itself is covered by the copy }
  if FSkipErase then
  begin
    Message.Result := 1;
    Exit;
  end;
  if not ((csOpaque in FControlStyle) and FDoubleBuffered and (Self is TCustomControl)) then
    Windows.FillRect(Message.DC, GetClientRect, FBrush.Handle);
  Message.Result := 1;
end;

{ UIF-081: a system class without graphic children paints alone }
procedure TWinControl.WMPaint(var Message: TWMPaint);
var
  I: Integer;
  Graphic: Boolean;
begin
  Graphic := False;
  for I := 0 to FControls.Count - 1 do
    if not (TObject(FControls[I]) is TWinControl) then
      Graphic := True;
  if FOwnClass or Graphic then
    PaintHandler(Message)
  else
    DefaultHandler(Message);
end;

{ core.md 7.10: own classes paint the client area into the given DC (erasing first when asked) }
procedure TWinControl.WMPrintClient(var Message: TMessage);
begin
  if not FOwnClass then
  begin
    DefaultHandler(Message);
    Exit;
  end;
  if (Message.LParam and PRF_ERASEBKGND) <> 0 then
    Perform(WM_ERASEBKGND, Message.WParam, 0);
  PaintWindow(HDC(Message.WParam));
  PaintControls(HDC(Message.WParam), nil);
end;

procedure TWinControl.PaintHandler(var Message: TWMPaint);
var
  DC: HDC;
  PS: TPaintStruct;
begin
  if (Message.DC = 0) and FOwnClass and FDoubleBuffered then
  begin
    PaintBuffered;
    Exit;
  end;
  DC := Message.DC;
  if DC = 0 then
    DC := BeginPaint(FHandle, PS);
  try
    PaintWindow(DC);
    PaintControls(DC, nil);
  finally
    if Message.DC = 0 then
      EndPaint(FHandle, PS);
  end;
end;

{ graphics.md UIF-560: the update rectangle is painted (erase logic, PaintWindow, graphic children) into a
  compatible bitmap whose origin is offset to the rectangle, then copied to the window (UIF-571, UIF-573) }
procedure TWinControl.PaintBuffered;
var
  DC, MemDC: HDC;
  PS: TPaintStruct;
  Bmp, OldBmp: HBITMAP;
  R: TRect;
  W, H: Integer;
begin
  FSkipErase := True;
  try
    DC := BeginPaint(FHandle, PS);
  finally
    FSkipErase := False;
  end;
  try
    R := PS.rcPaint;
    W := R.Right - R.Left;
    H := R.Bottom - R.Top;
    if (W <= 0) or (H <= 0) then
      Exit;
    MemDC := CreateCompatibleDC(DC);
    if MemDC = 0 then
      raise EOutOfResources.Create(SOutOfResources);
    try
      Bmp := CreateCompatibleBitmap(DC, W, H);
      if Bmp = 0 then
        raise EOutOfResources.Create(SOutOfResources);
      OldBmp := SelectObject(MemDC, Bmp);
      try
        SetViewportOrgEx(MemDC, -R.Left, -R.Top, nil);
        Perform(WM_ERASEBKGND, WPARAM(MemDC), 0);
        PaintWindow(MemDC);
        PaintControls(MemDC, nil);
        SetViewportOrgEx(MemDC, 0, 0, nil);
        BitBlt(DC, R.Left, R.Top, W, H, MemDC, 0, 0, SRCCOPY);
      finally
        SelectObject(MemDC, OldBmp);
        DeleteObject(Bmp);
      end;
    finally
      DeleteDC(MemDC);
    end;
  finally
    EndPaint(FHandle, PS);
  end;
end;

{ own classes: nothing (TCustomControl paints); system classes paint into the DC }
procedure TWinControl.PaintWindow(DC: HDC);
begin
  if not FOwnClass then
    CallWindowProcW(TFNWndProc(FDefWndProc), FHandle, WM_PAINT, WPARAM(DC), 0);
end;

{ UIF-081: graphic children from First (all when nil) back to front, origin at their top-left, clipped to them }
procedure TWinControl.PaintControls(DC: HDC; First: TControl);
var
  I, Saved: Integer;
  C: TControl;
begin
  I := 0;
  if First <> nil then
  begin
    I := FControls.IndexOf(Pointer(First));
    if I < 0 then
      I := 0;
  end;
  while I < FControls.Count do
  begin
    C := TControl(FControls[I]);
    if not (C is TWinControl) and C.FVisible then
    begin
      Saved := SaveDC(DC);
      try
        OffsetViewportOrgEx(DC, C.FLeft, C.FTop, nil);
        IntersectClipRect(DC, 0, 0, C.FWidth, C.FHeight);
        C.Perform(WM_PAINT, WPARAM(DC), 0);
      finally
        RestoreDC(DC, Saved);
      end;
    end;
    Inc(I);
  end;
end;

{ ---------------------------------------------------------------- TControlCanvas }

destructor TControlCanvas.Destroy;
begin
  FreeHandle;
  inherited Destroy;
end;

procedure TControlCanvas.SetControl(AControl: TControl);
begin
  if FControl <> AControl then
  begin
    FreeHandle;
    FControl := AControl;
  end;
end;

procedure TControlCanvas.CreateHandle;
var
  W: HWND;
  DC: HDC;
begin
  if FControl = nil then
  begin
    inherited CreateHandle;
    Exit;
  end;
  if FDeviceContext = 0 then
  begin
    if FControl is TWinControl then
      W := TWinControl(FControl).Handle
    else if (FControl.FParent <> nil) and (FControl.FParent.FHandle <> 0) then
      W := FControl.FParent.FHandle
    else
      Exit;
    DC := GetDC(W);
    if not (FControl is TWinControl) then
    begin
      OffsetViewportOrgEx(DC, FControl.FLeft, FControl.FTop, nil);
      IntersectClipRect(DC, 0, 0, FControl.FWidth, FControl.FHeight);
    end;
    FWindowHandle := W;
    FDeviceContext := DC;
  end;
  Handle := FDeviceContext;
end;

{ graphics.md UIF-570 }
procedure TControlCanvas.FreeHandle;
begin
  if FDeviceContext <> 0 then
  begin
    Handle := 0;
    ReleaseDC(FWindowHandle, FDeviceContext);
    FDeviceContext := 0;
    FWindowHandle := 0;
  end;
end;

{ ---------------------------------------------------------------- TGraphicControl }

constructor TGraphicControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FCanvas := TControlCanvas.Create;
  TControlCanvas(FCanvas).Control := Self;
end;

destructor TGraphicControl.Destroy;
begin
  inherited Destroy;
  FCanvas.Free;
end;

procedure TGraphicControl.Paint;
begin
end;

procedure TGraphicControl.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_PAINT then
    WMPaint(TWMPaint(Message))
  else
    inherited WndProc(Message);
end;

{ UIF-055: the parent's DC (origin and clip already moved to the control); Font and Color set before Paint }
procedure TGraphicControl.WMPaint(var Message: TWMPaint);
begin
  if Message.DC = 0 then
    Exit;
  TControlCanvas(FCanvas).FreeHandle;
  FCanvas.Handle := Message.DC;
  try
    FCanvas.Font := FFont;
    FCanvas.Brush.Color := FColor;
    Paint;
  finally
    FCanvas.Handle := 0;
  end;
end;

{ ---------------------------------------------------------------- TCustomControl }

constructor TCustomControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FCanvas := TControlCanvas.Create;
  TControlCanvas(FCanvas).Control := Self;
end;

destructor TCustomControl.Destroy;
begin
  inherited Destroy;
  FCanvas.Free;
end;

procedure TCustomControl.Paint;
begin
end;

procedure TCustomControl.PaintWindow(DC: HDC);
begin
  TControlCanvas(FCanvas).FreeHandle;
  FCanvas.Handle := DC;
  try
    Paint;
  finally
    FCanvas.Handle := 0;
  end;
end;

{ the cached window DC goes before the window (graphics.md UIF-570) }
procedure TCustomControl.DestroyWnd;
begin
  TControlCanvas(FCanvas).FreeHandle;
  inherited DestroyWnd;
end;

initialization
  ControlAtom :='PN.Control.' + IntToHex(GetCurrentProcessId, 1);
  NextCtlID := 1;
  RegisterIntegerConsts(TypeInfo(TCursor), IdentToCursor, CursorToIdent);
end.
