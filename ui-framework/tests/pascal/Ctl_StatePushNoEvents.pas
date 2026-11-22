// covers: UIF-600
// needs: desktop
program Ctl_StatePushNoEvents;
{ U-133, controls-standard.md UIF-600 (core.md UIF-033): every property set before the window exists has a field copy
  and is pushed to the HWND in CreateWnd (the native state equals the property afterwards, also after RecreateWnd);
  no event fires during that push, and none while csLoading (a .form with every event bound). Each case then changes
  the state by code once so that the bound handler is proved to fire (the test is not vacuous; a combo box, list box
  and page control fire nothing for a change by code, UIF-702, UIF-774). Controls: TEdit,
  TMemo, TCheckBox, TRadioButton, TComboBox, TListBox, TScrollBar, TTrackBar, TPageControl. }
uses
  Windows, Messages, CommCtrl, Types, SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    procedure Event(Sender: TObject);
  end;

  TPushForm = class(TForm)
  published
    Edit: TEdit;
    Memo: TMemo;
    Check: TCheckBox;
    Radio: TRadioButton;
    Combo: TComboBox;
    List: TListBox;
    Scroll: TScrollBar;
    Track: TTrackBar;
    Pages: TPageControl;
    Sheet1: TTabSheet;
    Sheet2: TTabSheet;
    procedure AnyEvent(Sender: TObject);
    procedure PagesChanging(Sender: TObject; var AllowChange: Boolean);
  end;

{$FORM Ctl_StatePushNoEvents.form}

var
  Log: TLog;

procedure TLog.Event(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + ';';
end;

procedure TPushForm.AnyEvent(Sender: TObject);
begin
  Log.Event(Sender);
end;

procedure TPushForm.PagesChanging(Sender: TObject; var AllowChange: Boolean);
begin
  Log.Text := Log.Text + 'changing;';
end;

function WinText(C: TWinControl): string;
var
  N: Integer;
begin
  N := GetWindowTextLengthW(C.Handle);
  SetLength(Result, N);
  if N > 0 then
    GetWindowTextW(C.Handle, PChar(Result), N + 1);
end;

function Native(C: TWinControl; Msg: UINT): Integer;
begin
  Result := Integer(SendMessageW(C.Handle, Msg, 0, 0));
end;

{ the native state of every control, compared with what the properties say }
procedure CheckNative(F: TPushForm; const Stage: string);
begin
  CheckEqS(F.Edit.Text, WinText(F.Edit), Stage + ': edit text pushed');
  CheckEqS(F.Memo.Text, WinText(F.Memo), Stage + ': memo text pushed');
  CheckEqI(BST_CHECKED, Native(F.Check, BM_GETCHECK), Stage + ': check box state pushed');
  CheckEqI(BST_CHECKED, Native(F.Radio, BM_GETCHECK), Stage + ': radio button state pushed');
  CheckEqI(3, Native(F.Combo, CB_GETCOUNT), Stage + ': combo items pushed');
  CheckEqI(1, Native(F.Combo, CB_GETCURSEL), Stage + ': combo index pushed');
  CheckEqI(3, Native(F.List, LB_GETCOUNT), Stage + ': list items pushed');
  CheckEqI(2, Native(F.List, LB_GETCURSEL), Stage + ': list index pushed');
  CheckEqI(40, Native(F.Scroll, SBM_GETPOS), Stage + ': scroll bar position pushed');
  CheckEqI(7, Native(F.Track, TBM_GETPOS), Stage + ': track bar position pushed');
  CheckEqI(1, Native(F.Pages, TCM_GETCURSEL), Stage + ': active page pushed');
end;

{ one change by code per control: each bound handler fires (the handlers really are bound) }
procedure CheckHandlersFire(F: TPushForm; const Stage: string);
begin
  Log.Text := '';
  F.Edit.Text := 'changed';
  F.Memo.Text := 'changed';
  F.Check.Checked := False;
  F.Scroll.Position := 41;
  F.Track.Position := 8;
  CheckEqS('Edit;Memo;Check;Scroll;Track;', Log.Text, Stage + ': handlers fire on a change by code');
  Check(Assigned(F.Combo.OnChange) and Assigned(F.List.OnClick) and Assigned(F.Pages.OnChange), Stage
    + ': handlers bound where a change by code fires nothing (UIF-702, UIF-774)');
end;

procedure SetState(F: TPushForm);
begin
  F.Edit.Text := 'pushed';
  F.Memo.Text := 'line 1' + #13#10 + 'line 2';
  F.Check.Checked := True;
  F.Radio.Checked := True;
  F.Combo.Items.Text := 'a' + #13#10 + 'b' + #13#10 + 'c';
  F.Combo.ItemIndex := 1;
  F.List.Items.Text := 'x' + #13#10 + 'y' + #13#10 + 'z';
  F.List.ItemIndex := 2;
  F.Scroll.Position := 40;
  F.Track.Max := 10;
  F.Track.Position := 7;
  F.Pages.ActivePage := F.Sheet2;
end;

procedure BindAll(F: TPushForm);
begin
  F.Edit.OnChange := Log.Event;
  F.Edit.OnClick := Log.Event;
  F.Memo.OnChange := Log.Event;
  F.Memo.OnClick := Log.Event;
  F.Check.OnClick := Log.Event;
  F.Radio.OnClick := Log.Event;
  F.Combo.OnChange := Log.Event;
  F.Combo.OnClick := Log.Event;
  F.Combo.OnSelect := Log.Event;
  F.List.OnClick := Log.Event;
  F.Scroll.OnChange := Log.Event;
  F.Track.OnChange := Log.Event;
  F.Pages.OnChange := Log.Event;
  F.Pages.OnClick := Log.Event;
end;

procedure RecreateAll(F: TPushForm);
var
  I: Integer;
begin
  for I := 0 to F.ControlCount - 1 do
    if F.Controls[I] is TWinControl then
    begin
      TWinAccess(F.Controls[I]).RecreateWnd;
      TWinControl(F.Controls[I]).HandleNeeded;
    end;
end;

procedure Run;
var
  F: TPushForm;
begin
  Application.Initialize;
  Log := TLog.Create;

  Section('loading a .form with every event bound (csLoading)');
  Log.Text := '';
  F := TPushForm.Create(nil);
  try
    CheckEqS('', Log.Text, 'no event while loading');
    Check(Assigned(F.Edit.OnChange) and Assigned(F.Check.OnClick) and Assigned(F.Track.OnChange), 'events bound');
    Check(not F.HandleAllocated, 'no window yet');
    F.HandleNeeded;
    F.Edit.HandleNeeded;
    CheckEqS('', Log.Text, 'no event while the streamed state is pushed in CreateWnd');
    CheckEqS('streamed', WinText(F.Edit), 'streamed edit text pushed');
    CheckEqI(BST_CHECKED, Native(F.Check, BM_GETCHECK), 'streamed check state pushed');
    CheckEqI(5, Native(F.Track, TBM_GETPOS), 'streamed track position pushed');
    CheckEqI(1, Native(F.Pages, TCM_GETCURSEL), 'streamed active page pushed');
  finally
    F.Free;
  end;

  Section('state set in code before the window exists (CreateWnd push)');
  F := TPushForm.Create(nil);
  try
    SetState(F);
    BindAll(F);
    Check(not F.Edit.HandleAllocated, 'no edit window before the push');
    Log.Text := '';
    F.HandleNeeded;
    F.Edit.HandleNeeded;
    CheckEqS('', Log.Text, 'no event during the push');
    CheckNative(F, 'first window');

    Section('RecreateWnd pushes the state again (no events)');
    Log.Text := '';
    RecreateAll(F);
    CheckEqS('', Log.Text, 'no event during the push of a re-created window');
    CheckNative(F, 're-created window');
    CheckHandlersFire(F, 'after the push');
  finally
    F.Free;
  end;
  Log.Free;
end;

begin
  Run;
  Done;
end.
