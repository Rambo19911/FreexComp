// covers: UIF-440, UIF-441, UIF-442, UIF-443
// needs: desktop
program Form_CloseDestroy;
{ U-041, application-forms.md UIF-440 ... 443: Close asks CloseQuery (OnCloseQuery), then OnClose with the default
  action caHide (caFree for the main form) and performs the action: caNone nothing, caHide Hide, caMinimize
  WindowState := wsMinimized, caFree Release; WM_CLOSE calls Close and never destroys the window by default
  processing; Release posts CM_RELEASE (to the form's window, else to Application.Handle) and the receiver frees the
  form; WM_QUERYENDSESSION answers CloseQuery; closing the main form with caHide or caFree terminates the
  application, observable order OnCloseQuery, OnClose, OnHide, OnDestroy. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TLog = class
  public
    Text: string;
    CanClose: Boolean;
    Action: TCloseAction;
    SetAction: Boolean;
    procedure Add(Sender: TObject; const S: string);
    procedure Query(Sender: TObject; var ACanClose: Boolean);
    procedure Closing(Sender: TObject; var AAction: TCloseAction);
    procedure Hidden(Sender: TObject);
    procedure Destroyed(Sender: TObject);
    procedure Hook(F: TCustomForm);
  end;

  THookedForm = class(TForm);

  TMainForm = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  Log: TLog;
  MainForm: TMainForm;

procedure TLog.Add(Sender: TObject; const S: string);
begin
  Text := Text + TComponent(Sender).Name + '.' + S + ';';
end;

procedure TLog.Query(Sender: TObject; var ACanClose: Boolean);
begin
  Add(Sender, 'CloseQuery');
  ACanClose := CanClose;
end;

procedure TLog.Closing(Sender: TObject; var AAction: TCloseAction);
begin
  case AAction of
    caNone: Add(Sender, 'Close(caNone)');
    caHide: Add(Sender, 'Close(caHide)');
    caFree: Add(Sender, 'Close(caFree)');
    caMinimize: Add(Sender, 'Close(caMinimize)');
  end;
  if SetAction then
    AAction := Action;
end;

procedure TLog.Hidden(Sender: TObject);
begin
  Add(Sender, 'Hide');
end;

procedure TLog.Destroyed(Sender: TObject);
begin
  Add(Sender, 'Destroy');
end;

procedure TLog.Hook(F: TCustomForm);
begin
  THookedForm(F).OnCloseQuery := Query;
  THookedForm(F).OnClose := Closing;
  THookedForm(F).OnHide := Hidden;
  THookedForm(F).OnDestroy := Destroyed;
end;

constructor TMainForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  Name := 'Main';
  Log.Hook(Self);
end;

procedure Reset(ACanClose: Boolean);
begin
  Log.Text := '';
  Log.CanClose := ACanClose;
  Log.SetAction := False;
end;

procedure Run;
var
  F, G: TForm;
  H: HWND;
  N: Integer;
begin
  Log := TLog.Create;
  try
    Application.CreateForm(TMainForm, MainForm);
    MainForm.Show;
    F := TForm.CreateNew(nil);
    F.Name := 'F';
    Log.Hook(F);
    F.Show;

    Section('Close of a form (UIF-440)');
    Reset(False);
    F.Close;
    CheckEqS('F.CloseQuery;', Log.Text, 'CloseQuery False: nothing else');
    CheckEqB(True, F.Visible, 'still visible');
    CheckEqB(False, F.CloseQuery, 'CloseQuery function');
    Reset(True);
    F.Close;
    CheckEqS('F.CloseQuery;F.Close(caHide);F.Hide;', Log.Text, 'default action caHide');
    CheckEqB(False, F.Visible, 'hidden');
    CheckEqB(True, Screen.FormCount = 2, 'not freed');
    F.Show;
    Reset(True);
    Log.SetAction := True;
    Log.Action := caNone;
    F.Close;
    CheckEqS('F.CloseQuery;F.Close(caHide);', Log.Text, 'caNone: nothing');
    CheckEqB(True, F.Visible, 'caNone: still visible');
    Reset(True);
    Log.SetAction := True;
    Log.Action := caMinimize;
    F.Close;
    CheckEqB(True, F.WindowState = wsMinimized, 'caMinimize: WindowState');
    CheckEqB(True, IsIconic(F.Handle), 'caMinimize: minimised');
    F.WindowState := wsNormal;

    Section('WM_CLOSE');
    Reset(True);
    H := F.Handle;
    SendMessageW(H, WM_CLOSE, 0, 0);
    CheckEqS('F.CloseQuery;F.Close(caHide);F.Hide;', Log.Text, 'WM_CLOSE calls Close');
    CheckEqB(True, IsWindow(H), 'the window is not destroyed');
    F.Show;
    Reset(True);
    SendMessageW(H, WM_SYSCOMMAND, SC_CLOSE, 0);
    CheckEqS('F.CloseQuery;F.Close(caHide);F.Hide;', Log.Text, 'SC_CLOSE calls Close');
    F.Show;

    Section('WM_QUERYENDSESSION (UIF-441)');
    Reset(False);
    CheckEqI(0, SendMessageW(H, WM_QUERYENDSESSION, 0, 0), 'CloseQuery False: 0');
    Reset(True);
    Check(SendMessageW(H, WM_QUERYENDSESSION, 0, 0) <> 0, 'CloseQuery True: not 0');
    CheckEqB(True, F.Visible, 'the form stays');

    Section('caFree and Release');
    Reset(True);
    Log.SetAction := True;
    Log.Action := caFree;
    N := Screen.FormCount;
    F.Close;
    CheckEqS('F.CloseQuery;F.Close(caHide);', Log.Text, 'Release: not freed yet');
    CheckEqI(N, Screen.FormCount, 'still listed');
    Application.ProcessMessages;
    CheckEqS('F.CloseQuery;F.Close(caHide);F.Hide;F.Destroy;', Log.Text, 'CM_RELEASE frees: OnHide, OnDestroy');
    CheckEqI(N - 1, Screen.FormCount, 'removed from Screen');
    CheckEqB(False, IsWindow(H), 'window destroyed');
    G := TForm.CreateNew(nil);
    G.Name := 'G';
    Log.Hook(G);
    Reset(True);
    G.Release;
    CheckEqB(False, G.HandleAllocated, 'Release without a handle creates none');
    Application.ProcessMessages;
    CheckEqS('G.Destroy;', Log.Text, 'released through Application.Handle');
    CheckEqI(N - 1, Screen.FormCount, 'G removed');

    Section('closing the main form');
    Reset(True);
    MainForm.Close;
    CheckEqS('Main.CloseQuery;Main.Close(caFree);', Log.Text, 'main form: default caFree');
    CheckEqB(True, Application.MainForm = MainForm, 'alive until the release');
    Application.ProcessMessages;
    CheckEqS('Main.CloseQuery;Main.Close(caFree);Main.Hide;Main.Destroy;', Log.Text,
      'OnCloseQuery, OnClose, OnHide, OnDestroy');
    CheckEqB(True, Application.MainForm = nil, 'MainForm nil');
    CheckEqB(True, Application.Terminated, 'the application terminates');
  finally
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
