// covers: UIF-050, UIF-077, UIF-722, UIF-723, UIF-724
// needs: desktop
program Menu_Popup;
{ U-075, controls-standard.md section 12 (UIF-724), core.md UIF-077: TPopupMenu defaults; Popup fires OnPopup,
  records PopupPoint and shows the native menu owned by the hidden PN.PopupList window, which forwards
  WM_INITMENUPOPUP (the root item's OnClick hook proves the menu opened) and WM_COMMAND to the menus; the form
  also routes WM_COMMAND to the items of every existing popup menu; the context-menu path by messages: a control's
  PopupMenu (AutoPopup) shown at the message's screen position or, for the keyboard origin (-1, -1), at the client
  centre, PopupComponent set, result 1; OnContextPopup Handled suppresses it; AutoPopup False or no PopupMenu lets
  the message bubble to the parent window; a graphic control's PopupMenu is shown through its parent; freeing a
  popup menu clears PopupMenu (UIF-050); freeing the PopupComponent clears it.
  No test blocks in the modal menu loop: before every Popup a WM_CANCELMODE is posted to the owner window, which the
  menu loop retrieves and passes to default processing, closing the menu (cancelled, so no WM_COMMAND follows). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, Menus, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Popped(Sender: TObject);
    procedure Opened(Sender: TObject);
    procedure Clicked(Sender: TObject);
    procedure Handle(Sender: TObject; MousePos: TPoint; var Handled: Boolean);
  end;

var
  Log: TLog;

procedure TLog.Popped(Sender: TObject);
begin
  Text := Text + 'popup:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Opened(Sender: TObject);
begin
  Text := Text + 'opened;';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TMenuItem(Sender).Caption + ';';
end;

procedure TLog.Handle(Sender: TObject; MousePos: TPoint; var Handled: Boolean);
begin
  Text := Text + 'context;';
  Handled := True;
end;

function NewPopup(AOwner: TComponent; const AName: string; var Item: TMenuItem): TPopupMenu;
begin
  Result := TPopupMenu.Create(AOwner);
  Result.Name := AName;
  Result.OnPopup := Log.Popped;
  Result.Items.OnClick := Log.Opened;
  Item := TMenuItem.Create(AOwner);
  Item.Caption := AName + 'Item';
  Item.OnClick := Log.Clicked;
  Result.Items.Add(Item);
end;

procedure CancelNextMenu;
begin
  PostMessageW(PopupListWindow, WM_CANCELMODE, 0, 0);
end;

function PointStr(const P: TPoint): string;
begin
  Result := Format('(%d, %d)', [P.X, P.Y]);
end;

function ClassOf(Wnd: HWND): string;
var
  Buffer: array[0..63] of WideChar;
begin
  SetString(Result, PWideChar(@Buffer[0]), GetClassNameW(Wnd, @Buffer[0], Length(Buffer)));
end;

procedure Run;
var
  F: TForm;
  PM, PF, PL: TPopupMenu;
  Item, FormItem, LabelItem: TMenuItem;
  P, Q: TPanel;
  L: TLabel;
  R: LRESULT;
  C: TPoint;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(80, 80, 400, 300);
    PM := NewPopup(F, 'PM', Item);

    Section('defaults');
    CheckEqB(True, PM.Alignment = paLeft, 'Alignment paLeft');
    CheckEqB(True, PM.AutoPopup, 'AutoPopup True');
    CheckEqB(True, PM.Images = nil, 'Images nil');
    CheckEqB(True, PM.PopupComponent = nil, 'PopupComponent nil');
    Check(GetClass('TPopupMenu') <> nil, 'TPopupMenu registered');
    CheckEqS('PN.PopupList', ClassOf(PopupListWindow), 'owner window class');
    CheckEqB(False, IsWindowVisible(PopupListWindow), 'owner window hidden');
    CheckEqB(True, PopupListWindow = PopupListWindow, 'one owner window');

    Section('Popup (UIF-724)');
    Log.Text := '';
    CancelNextMenu;
    PM.Popup(120, 130);
    CheckEqS('popup:PM;opened;', Log.Text, 'OnPopup, then the native menu opened (WM_INITMENUPOPUP forwarded)');
    CheckEqS('(120, 130)', PointStr(PM.PopupPoint), 'PopupPoint');
    Application.ProcessMessages;
    CheckEqS('popup:PM;opened;', Log.Text, 'cancelled: no command');
    PM.Alignment := paCenter;
    Log.Text := '';
    CancelNextMenu;
    PM.Popup(200, 200);
    CheckEqS('popup:PM;opened;', Log.Text, 'paCenter: shown as well');
    PM.Alignment := paLeft;

    Section('commands (UIF-722)');
    Log.Text := '';
    SendMessageW(PopupListWindow, WM_COMMAND, MakeWParam(Item.Command, 0), 0);
    CheckEqS('click:PMItem;', Log.Text, 'owner window: item clicked');
    Log.Text := '';
    PostMessageW(PopupListWindow, WM_COMMAND, MakeWParam(Item.Command, 0), 0);
    Application.ProcessMessages;
    CheckEqS('click:PMItem;', Log.Text, 'posted command (as after a selection): clicked in the loop');
    Log.Text := '';
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(Item.Command, 0), 0);
    CheckEqS('click:PMItem;', Log.Text, 'the form routes ids of popup items too');
    CheckEqB(False, DispatchPopupCommand(0), 'id 0: none');
    Log.Text := '';
    SendMessageW(PopupListWindow, WM_INITMENUPOPUP, WPARAM(PM.Handle), 0);
    CheckEqS('opened;', Log.Text, 'owner window: WM_INITMENUPOPUP to the root''s hook');

    Section('context menu (UIF-077)');
    P := TPanel.Create(F);
    P.SetBounds(10, 10, 200, 150);
    P.Parent := F;
    P.Name := 'P';
    P.PopupMenu := PM;
    Q := TPanel.Create(F);
    Q.SetBounds(10, 10, 80, 60);
    Q.Parent := P;
    Q.Name := 'Q';
    F.HandleNeeded;
    Log.Text := '';
    CancelNextMenu;
    R := SendMessageW(P.Handle, WM_CONTEXTMENU, WPARAM(P.Handle), MakeLParam(150, 160));
    CheckEqS('popup:PM;opened;', Log.Text, 'PopupMenu shown');
    CheckEqI(1, R, 'result 1');
    CheckEqB(True, PM.PopupComponent = P, 'PopupComponent = the control');
    CheckEqS('(150, 160)', PointStr(PM.PopupPoint), 'at the message position');
    Log.Text := '';
    CancelNextMenu;
    SendMessageW(P.Handle, WM_CONTEXTMENU, WPARAM(P.Handle), LPARAM(-1));
    C := Point(P.ClientWidth div 2, P.ClientHeight div 2);
    Windows.ClientToScreen(P.Handle, C);
    CheckEqS('popup:PM;opened;', Log.Text, 'keyboard origin: shown');
    CheckEqS(PointStr(C), PointStr(PM.PopupPoint), 'keyboard origin: at the client centre');
    Log.Text := '';
    CancelNextMenu;
    SendMessageW(Q.Handle, WM_CONTEXTMENU, WPARAM(Q.Handle), MakeLParam(150, 160));
    CheckEqS('popup:PM;opened;', Log.Text, 'child without PopupMenu: bubbles to the parent''s');
    CheckEqB(True, PM.PopupComponent = P, 'PopupComponent = the parent');
    PF := NewPopup(F, 'PF', FormItem);
    F.PopupMenu := PF;
    P.PopupMenu.AutoPopup := False;
    Log.Text := '';
    CancelNextMenu;
    SendMessageW(P.Handle, WM_CONTEXTMENU, WPARAM(P.Handle), MakeLParam(150, 160));
    CheckEqS('popup:PF;opened;', Log.Text, 'AutoPopup False: bubbles on to the form''s menu');
    CheckEqB(True, PF.PopupComponent = F, 'PopupComponent = the form');
    PM.AutoPopup := True;
    P.OnContextPopup := Log.Handle;
    Log.Text := '';
    R := SendMessageW(P.Handle, WM_CONTEXTMENU, WPARAM(P.Handle), MakeLParam(150, 160));
    CheckEqS('context;', Log.Text, 'OnContextPopup Handled: no popup');
    CheckEqI(1, R, 'handled: result 1');
    P.OnContextPopup := nil;

    Section('graphic control');
    L := TLabel.Create(F);
    L.SetBounds(250, 20, 60, 20);
    L.Parent := F;
    L.Name := 'L';
    PL := NewPopup(F, 'PL', LabelItem);
    L.PopupMenu := PL;
    C := Point(260, 25);
    Windows.ClientToScreen(F.Handle, C);
    Log.Text := '';
    CancelNextMenu;
    R := SendMessageW(F.Handle, WM_CONTEXTMENU, WPARAM(F.Handle), MakeLParam(Word(C.X), Word(C.Y)));
    CheckEqS('popup:PL;opened;', Log.Text, 'the label''s PopupMenu');
    CheckEqB(True, PL.PopupComponent = L, 'PopupComponent = the label');
    CheckEqI(1, R, 'result 1');

    Section('free notifications (UIF-050)');
    PL.Free;
    CheckEqB(True, L.PopupMenu = nil, 'freed popup menu: PopupMenu cleared');
    CheckEqB(True, PF.PopupComponent = F, 'PopupComponent still set');
    Q.PopupMenu := PF;
    Log.Text := '';
    CancelNextMenu;
    SendMessageW(Q.Handle, WM_CONTEXTMENU, WPARAM(Q.Handle), MakeLParam(150, 160));
    CheckEqB(True, PF.PopupComponent = Q, 'PopupComponent = Q');
    Q.Free;
    CheckEqB(True, PF.PopupComponent = nil, 'freed PopupComponent cleared');
    Log.Text := '';
    PF.Free;
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(FormItem.Command, 0), 0);
    CheckEqS('', Log.Text, 'a freed popup menu''s items are not routed');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
