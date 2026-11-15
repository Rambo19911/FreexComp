{ Forms - the application object and its message loop (application-forms.md). }
unit Forms;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, CommCtrl, Graphics, Controls, UIConsts;

type
  TIdleEvent = procedure(Sender: TObject; var Done: Boolean) of object;
  TMessageEvent = procedure(var Msg: TMsg; var Handled: Boolean) of object;
  TExceptionEvent = procedure(Sender: TObject; E: Exception) of object;

  { layout.md section 11; scrolling itself comes with U-070 }
  TScrollingWinControl = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
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
  protected
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
    FPendingRef: Pointer;                                    { CreateForm: the reference to set before OnCreate }
    FPendingClass: TClass;
    FActive: Boolean;                                        { WM_ACTIVATEAPP }
    FShowMainForm: Boolean;
    FTitle: string;
    FNonInteractive: Boolean;
    FOnException: TExceptionEvent;
    FOnIdle: TIdleEvent;
    FOnMessage: TMessageEvent;
    function GetExeName: string;
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
    property ShowMainForm: Boolean read FShowMainForm write FShowMainForm;
    property Title: string read FTitle write FTitle;
    property ExeName: string read GetExeName;
    { UIF-013: FXC_UI_NONINTERACTIVE = 1 at start-up }
    property NonInteractive: Boolean read FNonInteractive;
    property OnException: TExceptionEvent read FOnException write FOnException;
    property OnIdle: TIdleEvent read FOnIdle write FOnIdle;
    property OnMessage: TMessageEvent read FOnMessage write FOnMessage;
  end;

var
  Application: TApplication;
  Screen: TScreen;

implementation

uses
  RTLConsts;

type
  PComponentRef = ^TComponent;

const
  AppClassName = 'PN.Application';
  ShellManifestResource = 124;                               { RTL-871 }

{ the message-only window: CM_RELEASE of forms without a window (UIF-443); exceptions never unwind into Win32 }
function AppWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  if Msg = CM_RELEASE then
    try
      TObject(Pointer(LParam)).Free;
    except
      Application.HandleException(Application);
    end
  else
    Result := DefWindowProcW(Wnd, Msg, WParam, LParam);
end;

{ UIF-402 step 2: the loaded comctl32 comes from the version 6 side-by-side assembly (its folder names the version;
  both 5.82 and 6.0 live in the side-by-side store). Unknown layout = not active, so the fallback runs. }
function CommonControls6Active: Boolean;
var
  Module: HMODULE;
  Buffer: array[0..MAX_PATH] of Char;
  N: DWORD;
begin
  Result := False;
  Module := GetModuleHandleW('comctl32.dll');
  if Module = 0 then
    Exit;
  N := GetModuleFileNameW(Module, @Buffer[0], MAX_PATH);
  if N = 0 then
    Exit;
  Buffer[N] := #0;
  Result := Pos('.COMMON-CONTROLS_6595B64144CCF1DF_6.', UpperCase(string(PChar(@Buffer[0])))) > 0;
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

constructor TScrollingWinControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
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
  if (Application.FPendingRef <> nil) and (ClassType = Application.FPendingClass) and (Owner = Application) then
  begin
    PComponentRef(Application.FPendingRef)^ := Self;           { UIF-405: the reference is valid during OnCreate }
    Application.FPendingRef := nil;
  end;
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
  if not HandleAllocated or IsIconic(Handle) then
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
  if (Operation = opRemove) and (AComponent = FActiveControl) then
    FActiveControl := nil;
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
    if (FActiveControl <> nil) and FActiveControl.HandleAllocated and IsWindowVisible(FActiveControl.Handle)
      and IsWindowEnabled(FActiveControl.Handle) then
      Windows.SetFocus(FActiveControl.Handle);               { focus restore (full rules: U-043) }
  end
  else
  begin
    C := FindControl(GetFocus);
    if (C <> nil) and (C <> Self) and ContainsControl(C) then
      ActiveControl := C;
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

procedure TCustomForm.SetActiveControl(Value: TWinControl);
begin
  if FActiveControl <> Value then
  begin
    FActiveControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
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

{ ---------------------------------------------------------------- TApplication }

{ UIF-405. The specified sequence (NewInstance, Instance.Create, AfterConstruction) needs a constructor call on an
  instance that dispatches through the VMT; opc binds it statically (reported), so the class reference constructs and
  a form's AfterConstruction stores the pending reference before OnCreate. Same observable result for forms. }
procedure TApplication.CreateForm(InstanceClass: TComponentClass; var Reference);
var
  Instance: TComponent;
  OldRef: Pointer;
  OldClass: TClass;
begin
  Initialize;
  OldRef := FPendingRef;
  OldClass := FPendingClass;
  FPendingRef := @Reference;
  FPendingClass := InstanceClass;
  try
    try
      Instance := InstanceClass.Create(Self);
    except
      TComponent(Reference) := nil;
      raise;
    end;
  finally
    FPendingRef := OldRef;
    FPendingClass := OldClass;
  end;
  TComponent(Reference) := Instance;
  if (FMainForm = nil) and (Instance is TForm) then
    FMainForm := TForm(Instance);
end;

constructor TApplication.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FShowMainForm := True;
  FTitle := ChangeFileExt(ExtractFileName(ParamStr(0)), '');
  FNonInteractive := GetEnvironmentVariable('FXC_UI_NONINTERACTIVE') = '1';
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

procedure TApplication.Run;
begin
  Initialize;
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
  if Done then
    WaitMessage;
end;

{ UIF-400: the remaining forms in reverse creation order, then Screen, then Application }
procedure DestroyGlobals;
var
  I: Integer;
begin
  FindGlobalComponent := nil;
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
  RegisterClasses([TForm]);

finalization
  DestroyGlobals;
end.
