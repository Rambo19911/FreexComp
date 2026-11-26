// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
program AccessibilityProbe;
{ U-155, ui-automation.md section 9: the program the UI Automation client tests read
  (FreexComp.UiFramework.Automation.Tests). One form 'Accessibility Probe' (class PN.TProbeForm, component name
  ProbeForm) with one instance of every Phase 4 control and of a frame, a string grid with a fixed row and column
  and goEditing, a tool bar with a plain, a check and a separator button, labels associated by FocusControl, and a
  rich edit (U-156: the system's Text pattern).
  Every content element has a name (its own text, an associated label or AccessibleName). The form is shown
  without activation; the program never initialises COM itself. stdout lines (each flushed):
    'ready <form handle in hex>' once the form is shown;
    'click <component name>' from OnClick of the button and the tool buttons;
    on WM_APP + 1: the list box gets the AccessibleName 'Renamed fruit list' and the label of the edit the caption
      'Full &name:', then 'renamed';
    on WM_APP + 2 (wParam = N): a second form (a label, a panel with a label, an edit, a button) is created, shown
      without activation and freed N times, then created once more and kept: 'held <count> <its handle in hex>';
    on WM_APP + 3: the kept second form is freed: 'freed <count>';
    on WM_APP + 4: 'providers <live provider objects>' (AutomationProviderCount);
    U-156: on WM_APP + 5: the check tool button is released by the program (Down := False), then 'released';
    on WM_APP + 6: the label in the scroll box gets the caption 'Changed label', the image is disabled and a label
      InsertedLabel is inserted into the info panel, then 'changed'; on WM_APP + 7: InsertedLabel is freed, then
      'removed';
    WM_CLOSE ends the program ('probe: close' on stderr, exit code 0). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls, Grids,
  Accessibility;

const
  WM_PROBE_RENAME = WM_APP + 1;
  WM_PROBE_CYCLE = WM_APP + 2;
  WM_PROBE_FREE = WM_APP + 3;
  WM_PROBE_COUNT = WM_APP + 4;
  WM_PROBE_RELEASE = WM_APP + 5;
  WM_PROBE_CHANGE = WM_APP + 6;
  WM_PROBE_REMOVE = WM_APP + 7;

type
  TProbeForm = class(TForm)
  private
    FHeld: TForm;
    FCycles: Integer;
    procedure Clicked(Sender: TObject);
    function NewSecondForm: TForm;
  protected
    procedure WndProc(var Message: TMessage); override;
  end;

var
  Probe: TProbeForm;

procedure Say(const S: string);
begin
  WriteLn(S);
  Flush(Output);
end;

procedure TProbeForm.Clicked(Sender: TObject);
begin
  Say('click ' + TComponent(Sender).Name);
end;

function TProbeForm.NewSecondForm: TForm;
var
  L: TLabel;
  P: TPanel;
  E: TEdit;
  B: TButton;
begin
  Inc(FCycles);
  Result := TForm.CreateNew(nil);
  Result.Name := 'SecondForm';
  Result.Caption := 'Second Form';
  Result.SetBounds(Left + 40, Top + 40, 300, 160);
  L := TLabel.Create(Result);
  L.Name := 'SecondLabel';
  L.Caption := 'Second &label';
  L.Parent := Result;
  L.SetBounds(8, 8, 120, 17);
  P := TPanel.Create(Result);
  P.Name := 'SecondPanel';
  P.Caption := '';
  P.AccessibleName := 'Second panel';
  P.Parent := Result;
  P.SetBounds(8, 30, 200, 60);
  L := TLabel.Create(Result);
  L.Name := 'InnerLabel';
  L.Caption := 'Inner';
  L.Parent := P;
  L.SetBounds(4, 4, 60, 17);
  E := TEdit.Create(Result);
  E.Name := 'SecondEdit';
  E.Parent := Result;
  E.SetBounds(8, 100, 120, 23);
  B := TButton.Create(Result);
  B.Name := 'SecondButton';
  B.Caption := 'Close';
  B.Parent := Result;
  B.SetBounds(140, 100, 75, 25);
  Result.Show;
end;

procedure TProbeForm.WndProc(var Message: TMessage);
var
  I: Integer;
  F: TForm;
  L: TLabel;
begin
  case Message.Msg of
    WM_PROBE_RENAME:
      begin
        TListBox(FindComponent('FruitList')).AccessibleName := 'Renamed fruit list';
        TLabel(FindComponent('NameLabel')).Caption := 'Full &name:';
        Say('renamed');
      end;
    WM_PROBE_CYCLE:
      begin
        for I := 1 to Integer(Message.WParam) do
        begin
          F := NewSecondForm;
          Application.ProcessMessages;
          F.Free;
        end;
        FHeld := NewSecondForm;
        Application.ProcessMessages;
        Say('held ' + IntToStr(FCycles) + ' ' + IntToHex(NativeUInt(FHeld.Handle), 1));
      end;
    WM_PROBE_FREE:
      begin
        FHeld.Free;
        FHeld := nil;
        Application.ProcessMessages;
        Say('freed ' + IntToStr(FCycles));
      end;
    WM_PROBE_COUNT:
      Say('providers ' + IntToStr(AutomationProviderCount));
    WM_PROBE_RELEASE:
      begin
        TToolButton(FindComponent('CheckButton')).Down := False;
        Say('released');
      end;
    WM_PROBE_CHANGE:
      begin
        TLabel(FindComponent('BoxLabel')).Caption := 'Changed label';
        TImage(FindComponent('LogoImage')).Enabled := False;
        L := TLabel.Create(Self);
        L.Name := 'InsertedLabel';
        L.Caption := 'Inserted';
        L.SetBounds(120, 4, 60, 17);
        L.Parent := TPanel(FindComponent('InfoPanel'));
        Say('changed');
      end;
    WM_PROBE_REMOVE:
      begin
        FindComponent('InsertedLabel').Free;
        Say('removed');
      end;
    WM_CLOSE:
      begin
        WriteLn(ErrOutput, 'probe: close');
        Application.Terminate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

function Add(AClass: TControlClass; AParent: TWinControl; const AName: string; L, T, W, H: Integer): TControl;
begin
  Result := AClass.Create(Probe);
  Result.Name := AName;
  Result.Parent := AParent;
  Result.SetBounds(L, T, W, H);
end;

procedure Build;
var
  F: TProbeForm;
  Lbl: TLabel;
  Pages: TPageControl;
  Sheet: TTabSheet;
  Bar: TToolBar;
  Btn: TToolButton;
  Grid: TStringGrid;
  Frame: TFrame;
  Box: TScrollBox;
  List: TListView;
  Tree: TTreeView;
  Stat: TStaticText;
  Combo: TComboBox;
  Ok: TButton;
begin
  F := TProbeForm.CreateNew(nil);
  Probe := F;
  F.Name := 'ProbeForm';
  F.Caption := 'Accessibility Probe';
  F.SetBounds(60, 60, 820, 560);
  Bar := TToolBar(Add(TToolBar, F, 'ProbeToolBar', 0, 0, 800, 29));
  Bar.Align := alTop;
  Bar.ShowCaptions := True;
  Bar.AccessibleName := 'Tools';
  Btn := TToolButton.Create(F);
  Btn.Name := 'PlainButton';
  Btn.Parent := Bar;
  Btn.Caption := '&Plain';
  Btn.OnClick := F.Clicked;
  Btn := TToolButton.Create(F);
  Btn.Name := 'SeparatorButton';
  Btn.Parent := Bar;
  Btn.Style := tbsSeparator;
  Btn := TToolButton.Create(F);
  Btn.Name := 'CheckButton';
  Btn.Parent := Bar;
  Btn.Style := tbsCheck;
  Btn.Caption := '&Check';
  Btn.OnClick := F.Clicked;
  Lbl := TLabel(Add(TLabel, F, 'NameLabel', 8, 40, 60, 17));
  Lbl.Caption := '&Name:';
  Lbl.FocusControl := TEdit(Add(TEdit, F, 'NameEdit', 80, 38, 140, 23));
  TMemo(Add(TMemo, F, 'NotesMemo', 8, 70, 212, 60)).AccessibleName := 'Notes';
  Ok := TButton(Add(TButton, F, 'OkButton', 8, 140, 75, 25));
  Ok.Caption := '&OK';
  Ok.AccessibleDescription := 'Confirms the dialog';
  Ok.OnClick := F.Clicked;
  TCheckBox(Add(TCheckBox, F, 'RememberCheck', 90, 144, 130, 17)).Caption := '&Remember me';
  TRadioButton(Add(TRadioButton, F, 'OptionRadio', 8, 172, 120, 17)).Caption := 'Option &A';
  TGroupBox(Add(TGroupBox, F, 'OptionsGroup', 230, 38, 160, 90)).Caption := '&Options';
  with TListBox(Add(TListBox, F, 'FruitList', 230, 136, 160, 60)) do
  begin
    Items.Add('Apple');
    Items.Add('Pear');
    AccessibleName := 'Fruit list';
  end;
  Stat := TStaticText(Add(TStaticText, F, 'ColourStatic', 400, 40, 60, 17));
  Stat.Caption := '&Colour:';
  Combo := TComboBox(Add(TComboBox, F, 'ColourCombo', 470, 38, 120, 23));
  Combo.Items.Add('Red');
  Stat.FocusControl := Combo;
  TScrollBar(Add(TScrollBar, F, 'PositionScroll', 400, 70, 190, 17)).AccessibleName := 'Scroll position';
  TPanel(Add(TPanel, F, 'InfoPanel', 400, 94, 190, 40)).Caption := 'Info &panel';
  TImage(Add(TImage, F, 'LogoImage', 600, 38, 32, 32)).Hint := 'Logo|The company logo';
  Add(TShape, F, 'DecorShape', 640, 38, 32, 32);
  Add(TBevel, F, 'DecorBevel', 680, 38, 32, 32);
  Box := TScrollBox(Add(TScrollBox, F, 'ScrollArea', 600, 80, 190, 60));
  Box.AccessibleName := 'Scroll area';
  TLabel(Add(TLabel, Box, 'BoxLabel', 4, 4, 80, 17)).Caption := 'In the box';
  Pages := TPageControl(Add(TPageControl, F, 'ProbePages', 8, 200, 250, 120));
  Pages.AccessibleName := 'Pages';
  Sheet := TTabSheet.Create(F);
  Sheet.Name := 'GeneralSheet';
  Sheet.PageControl := Pages;
  Sheet.Caption := '&General';
  TProgressBar(Add(TProgressBar, F, 'ProgressGauge', 270, 210, 150, 17)).AccessibleName := 'Progress';
  TTrackBar(Add(TTrackBar, F, 'VolumeTrack', 270, 235, 150, 30)).AccessibleName := 'Volume';
  List := TListView(Add(TListView, F, 'FileList', 430, 150, 170, 100));
  List.ViewStyle := vsReport;
  List.Columns.Add.Caption := 'File';
  List.Items.Add.Caption := 'readme.txt';
  List.AccessibleName := 'Files';
  TRichEdit(Add(TRichEdit, F, 'NotesRich', 430, 260, 170, 40)).AccessibleName := 'Rich notes';
  Tree := TTreeView(Add(TTreeView, F, 'FolderTree', 610, 150, 180, 100));
  Tree.Items.AddChild(Tree.Items.Add(nil, 'Root'), 'Child');
  Tree.AccessibleName := 'Folders';
  Grid := TStringGrid(Add(TStringGrid, F, 'DataGrid', 8, 330, 300, 120));
  Grid.ColCount := 4;
  Grid.RowCount := 4;
  Grid.FixedRows := 1;
  Grid.FixedCols := 1;
  Grid.Options := Grid.Options + [goEditing];
  Grid.Cells[1, 0] := 'Name';
  Grid.Cells[1, 1] := 'Alpha';
  Grid.AccessibleName := 'Data';
  Frame := TFrame.Create(F);
  Frame.Name := 'AddressFrame';
  Frame.Parent := F;
  Frame.SetBounds(320, 330, 200, 80);
  Frame.AccessibleName := 'Address';
  TLabel(Add(TLabel, Frame, 'StreetLabel', 4, 4, 80, 17)).Caption := '&Street';
  Add(TSplitter, F, 'SideSplitter', 530, 330, 4, 120);
  with TStatusBar(Add(TStatusBar, F, 'ProbeStatus', 0, 500, 800, 20)) do
  begin
    Align := alBottom;
    SimpleText := 'Ready';
    AccessibleName := 'Status';
  end;
  F.Show;
end;

begin
  Application.Initialize;
  Build;
  Say('ready ' + IntToHex(NativeUInt(Probe.Handle), 1));
  while not Application.Terminated do
    Application.HandleMessage;
  Probe.Free;
end.
