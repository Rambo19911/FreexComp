unit MainUnit;

interface

uses
  SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls, Menus;

type
  TMainForm = class(TForm)
    MainMenu1: TMainMenu;
    FileMenu: TMenuItem;
    FileExit: TMenuItem;
    NameEdit: TEdit;
    GreetButton: TButton;
    ClearButton: TButton;
    ResultText: TStaticText;
    UpperCheck: TCheckBox;
    HistoryList: TListBox;
    StatusBar: TStatusBar;
    procedure FormActivate(Sender: TObject);
    procedure FormClose(Sender: TObject; var Action: TCloseAction);
    procedure FormCloseQuery(Sender: TObject; var CanClose: Boolean);
    procedure FormCreate(Sender: TObject);
    procedure FormDestroy(Sender: TObject);
    procedure FormHide(Sender: TObject);
    procedure FormPaint(Sender: TObject);
    procedure FormResize(Sender: TObject);
    procedure FormShow(Sender: TObject);
    procedure FileExitClick(Sender: TObject);
    procedure GreetButtonClick(Sender: TObject);
    procedure ClearButtonClick(Sender: TObject);
    procedure ControlEnter(Sender: TObject);
  private
    FLogName: string;
    FPaintLogged: Boolean;
    procedure Log(const Line: string);
  end;

var
  MainForm: TMainForm;

implementation

{$FORM MainUnit.form}

procedure TMainForm.Log(const Line: string);
var
  F: TextFile;
begin
  if FLogName = '' then Exit;
  AssignFile(F, FLogName);
  if FileExists(FLogName) then Append(F) else Rewrite(F);
  Writeln(F, Line);
  CloseFile(F);
end;

procedure TMainForm.FormCreate(Sender: TObject);
begin
  FLogName := GetEnvironmentVariable('HELLO_EVENT_LOG');
  Log('OnCreate');
end;

procedure TMainForm.FormShow(Sender: TObject);      begin Log('OnShow'); end;
procedure TMainForm.FormActivate(Sender: TObject);  begin Log('OnActivate'); end;
procedure TMainForm.FormResize(Sender: TObject);
begin
  Log(Format('OnResize %dx%d', [ClientWidth, ClientHeight]));
end;
procedure TMainForm.FormPaint(Sender: TObject);
begin
  if not FPaintLogged then begin FPaintLogged := True; Log('OnPaint'); end;
end;
procedure TMainForm.FormCloseQuery(Sender: TObject; var CanClose: Boolean); begin Log('OnCloseQuery'); end;
procedure TMainForm.FormClose(Sender: TObject; var Action: TCloseAction);   begin Log('OnClose'); end;
procedure TMainForm.FormHide(Sender: TObject);      begin Log('OnHide'); end;
procedure TMainForm.FormDestroy(Sender: TObject);   begin Log('OnDestroy'); end;

procedure TMainForm.ControlEnter(Sender: TObject);
begin
  Log('enter: ' + TComponent(Sender).Name);
end;

procedure TMainForm.FileExitClick(Sender: TObject);
begin
  Close;
end;

procedure TMainForm.GreetButtonClick(Sender: TObject);
var
  S, Msg: string;
begin
  S := Trim(NameEdit.Text);
  if S = '!raise' then
    raise Exception.Create('Test exception');
  if S = '' then
  begin
    ResultText.Caption := 'Please enter a name.';
    Exit;
  end;
  Msg := 'Hello, ' + S + '!';
  if UpperCheck.Checked then
    Msg := UpperCase(Msg);
  ResultText.Caption := Msg;
  HistoryList.Items.Add(Msg);
  StatusBar.SimpleText := Format('%d greeting(s)', [HistoryList.Items.Count]);
end;

procedure TMainForm.ClearButtonClick(Sender: TObject);
begin
  NameEdit.Clear;
  ResultText.Caption := '';
  HistoryList.Clear;
  StatusBar.SimpleText := 'Ready';
end;

end.
