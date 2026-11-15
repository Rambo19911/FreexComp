{ Forms - the application object and its message loop (application-forms.md). }
unit Forms;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, CommCtrl, Graphics, Controls, UIConsts;

type
  TIdleEvent = procedure(Sender: TObject; var Done: Boolean) of object;
  TMessageEvent = procedure(var Msg: TMsg; var Handled: Boolean) of object;
  TExceptionEvent = procedure(Sender: TObject; E: Exception) of object;

  { application-forms.md section 2 }
  TApplication = class(TComponent)
  private
    FHandle: HWND;
    FInitialized: Boolean;
    FTerminated: Boolean;
    FMainForm: TWinControl;
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

implementation

const
  AppClassName = 'PN.Application';
  ShellManifestResource = 124;                               { RTL-871 }

function AppWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
begin
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

{ ---------------------------------------------------------------- TApplication }

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

initialization
  Application := TApplication.Create(nil);
  ShowErrorProc := FormsShowError;
  ApplicationHandleException := Application.HandleException;

finalization
  ApplicationHandleException := nil;
  ShowErrorProc := nil;
  Application.Free;
  Application := nil;
end.
