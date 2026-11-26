// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1430, UIF-1431, UIF-1440
// needs: desktop
// phase: 8
program Acc_ToolButtons;
{ U-156, ui-automation.md section 6 (UIF-1430, UIF-1431), in process: the patterns of tool buttons. A plain and a
  drop-down button support Invoke and no Toggle; a check button supports Toggle and no Invoke; a separator supports
  neither. Invoke on a disabled button (or one on a disabled tool bar) is refused with UIA_E_ELEMENTNOTENABLED; on an
  enabled one it returns before OnClick runs, and OnClick runs once when the posted message is handled. Toggle
  reports Down, flips it through the posted click and is refused when disabled. A button freed before the posted
  message is handled is not clicked. Changes of Down and Enabled create no provider (UIF-1440); every provider object
  is released at the end. The form is shown without activation (the posted message needs the tool bar's window). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ComCtrls, ActiveX, UIAutomation,
  Accessibility, AccKit, TestKit;

type
  TCounter = class
  public
    Clicks: Integer;
    procedure Clicked(Sender: TObject);
  end;

var
  F: TForm;
  Bar, Bar2: TToolBar;
  Plain, Drop, CheckBtn, Separator, Off, OffCheck, OnOffBar: TToolButton;
  Counter: TCounter;

procedure TCounter.Clicked(Sender: TObject);
begin
  Inc(Clicks);
end;

function AddButton(ABar: TToolBar; const AName: string; AStyle: TToolButtonStyle): TToolButton;
begin
  Result := TToolButton.Create(F);
  Result.Name := AName;
  Result.Parent := ABar;
  Result.Style := AStyle;
  if AStyle <> tbsSeparator then
    Result.Caption := AName;
  Result.OnClick := Counter.Clicked;
end;

procedure Build;
begin
  Counter := TCounter.Create;
  F := TForm.CreateNew(nil);
  F.Caption := 'Acc_ToolButtons';
  F.SetBounds(40, 40, 500, 200);
  Bar := TToolBar.Create(F);
  Bar.Parent := F;
  Bar.ShowCaptions := True;
  Plain := AddButton(Bar, 'Plain', tbsButton);
  Drop := AddButton(Bar, 'Drop', tbsDropDown);
  Separator := AddButton(Bar, 'Separator', tbsSeparator);
  CheckBtn := AddButton(Bar, 'Check', tbsCheck);
  Off := AddButton(Bar, 'Off', tbsButton);
  Off.Enabled := False;
  OffCheck := AddButton(Bar, 'OffCheck', tbsCheck);
  OffCheck.Enabled := False;
  Bar2 := TToolBar.Create(F);
  Bar2.Parent := F;
  Bar2.Top := 40;
  OnOffBar := AddButton(Bar2, 'OnOffBar', tbsButton);
  Bar2.Enabled := False;
  F.Show;
end;

function Pattern(C: TControl; Id: Integer): IUnknown;
var
  R: HResult;
begin
  Result := nil;
  R := GetAutomationProvider(C).GetPatternProvider(Id, Result);
  CheckEqS('S_OK', ResultWord(R), C.Name + ': GetPatternProvider ' + IntToStr(Id) + ' returns S_OK');
end;

function InvokeOf(C: TControl): IInvokeProvider;
begin
  Result := Pattern(C, UIA_InvokePatternId) as IInvokeProvider;
end;

function ToggleOf(C: TControl): IToggleProvider;
begin
  Result := Pattern(C, UIA_TogglePatternId) as IToggleProvider;
end;

function StateOf(const T: IToggleProvider): Integer;
begin
  Result := -1;
  CheckEqS('S_OK', ResultWord(T.get_ToggleState(Result)), 'get_ToggleState returns S_OK');
end;

procedure TestPatterns;
begin
  Section('which button supports which pattern (UIF-1430, UIF-1431)');
  Check(Pattern(Plain, UIA_InvokePatternId) <> nil, 'a plain button: Invoke');
  Check(Pattern(Plain, UIA_TogglePatternId) = nil, 'a plain button: no Toggle');
  Check(Pattern(Drop, UIA_InvokePatternId) <> nil, 'a drop-down button: Invoke');
  Check(Pattern(Drop, UIA_TogglePatternId) = nil, 'a drop-down button: no Toggle');
  Check(Pattern(CheckBtn, UIA_TogglePatternId) <> nil, 'a check button: Toggle');
  Check(Pattern(CheckBtn, UIA_InvokePatternId) = nil, 'a check button: no Invoke');
  Check(Pattern(Separator, UIA_InvokePatternId) = nil, 'a separator: no Invoke');
  Check(Pattern(Separator, UIA_TogglePatternId) = nil, 'a separator: no Toggle');
  Check(Pattern(Plain, UIA_ValuePatternId) = nil, 'a plain button: no other pattern');
end;

procedure TestInvoke;
var
  Inv: IInvokeProvider;
begin
  Section('Invoke (UIF-1430)');
  Counter.Clicks := 0;
  CheckEqS('UIA_E_ELEMENTNOTENABLED', ResultWord(InvokeOf(Off).Invoke), 'a disabled button: refused');
  CheckEqS('UIA_E_ELEMENTNOTENABLED', ResultWord(InvokeOf(OnOffBar).Invoke),
    'a button on a disabled tool bar: refused');
  Application.ProcessMessages;
  CheckEqI(0, Counter.Clicks, 'a refused Invoke clicks nothing');
  Inv := InvokeOf(Plain);
  CheckEqS('S_OK', ResultWord(Inv.Invoke), 'an enabled button: S_OK');
  CheckEqI(0, Counter.Clicks, 'Invoke returns before OnClick runs (the click is posted)');
  Application.ProcessMessages;
  CheckEqI(1, Counter.Clicks, 'OnClick runs once the posted message is handled');
  Application.ProcessMessages;
  CheckEqI(1, Counter.Clicks, 'and only once');
  CheckEqS('S_OK', ResultWord(InvokeOf(Drop).Invoke), 'a drop-down button: S_OK');
  Application.ProcessMessages;
  CheckEqI(2, Counter.Clicks, 'a drop-down button clicks as well');
end;

procedure TestToggle;
var
  T: IToggleProvider;
begin
  Section('Toggle (UIF-1431)');
  Counter.Clicks := 0;
  T := ToggleOf(CheckBtn);
  CheckEqI(ToggleState_Off, StateOf(T), 'up: Off');
  CheckEqS('S_OK', ResultWord(T.Toggle), 'Toggle: S_OK');
  CheckEqB(False, CheckBtn.Down, 'Toggle returns before the click (posted)');
  Application.ProcessMessages;
  CheckEqB(True, CheckBtn.Down, 'the posted click puts it down');
  CheckEqI(1, Counter.Clicks, 'OnClick ran once');
  CheckEqI(ToggleState_On, StateOf(T), 'down: On');
  T.Toggle;
  Application.ProcessMessages;
  CheckEqB(False, CheckBtn.Down, 'a second Toggle releases it');
  CheckBtn.Down := True;
  CheckEqI(ToggleState_On, StateOf(T), 'Down set by the program: On');
  CheckBtn.Down := False;
  CheckEqI(ToggleState_Off, StateOf(T), 'released by the program: Off');
  CheckEqS('UIA_E_ELEMENTNOTENABLED', ResultWord(ToggleOf(OffCheck).Toggle), 'a disabled check button: refused');
  Application.ProcessMessages;
  CheckEqB(False, OffCheck.Down, 'a refused Toggle changes nothing');
end;

procedure TestFreedBeforeTheClick;
var
  Gone: TToolButton;
begin
  Section('a button freed before its posted click (UIF-1425, UIF-1427)');
  Counter.Clicks := 0;
  Gone := AddButton(Bar, 'Gone', tbsButton);
  CheckEqS('S_OK', ResultWord(InvokeOf(Gone).Invoke), 'Invoke: S_OK');
  Gone.Free;
  Application.ProcessMessages;
  CheckEqI(0, Counter.Clicks, 'no click after the button is gone');
end;

procedure TestNoProviderCreated;
var
  Count: Integer;
  Quiet: TToolButton;
begin
  Section('changes without a provider create none (UIF-1440)');
  Quiet := AddButton(Bar, 'Quiet', tbsCheck);
  Count := AutomationProviderCount;
  Quiet.Down := True;
  Quiet.Down := False;
  Quiet.Enabled := False;
  Quiet.Enabled := True;
  CheckEqI(Count, AutomationProviderCount, 'Down and Enabled changed: no new provider');
  Quiet.Free;
end;

begin
  Application.Initialize;
  Build;
  try
    TestPatterns;
    TestInvoke;
    TestToggle;
    TestFreedBeforeTheClick;
    TestNoProviderCreated;
  finally
    F.Free;
    Counter.Free;
  end;
  CheckEqI(0, AutomationProviderCount, 'every provider object is released (UIF-1425)');
  Done;
end.
