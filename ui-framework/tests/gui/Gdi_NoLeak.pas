// covers: UIF-570, UIF-571, UIF-573, UIF-575, UIF-576
program Gdi_NoLeak;
{ U-112, graphics.md UIF-576: creates and destroys forms holding one control of every Phase 4 control class (plus a
  main menu, an image list, a popup menu with a sub-menu whose handles are created (U-135) and an enabled TTimer with
  a handler, so its SetTimer runs and the form's destruction kills it), each with its windows created and the whole
  tree painted once (WM_PRINT into a memory bitmap: every control's paint path runs, no window is shown or
  activated). The C# test samples this process's GDI and USER object counts (GetGuiResources) at the two
  synchronisation points: after one warm-up cycle the program writes 'warm' and waits for a line on stdin; after 200
  more cycles it writes 'done' and waits again. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls, Grids, Menus,
  ImgList;

type
  TTicker = class
  public
    procedure Tick(Sender: TObject);
  end;

procedure TTicker.Tick(Sender: TObject);
begin
end;

var
  Ticker: TTicker;

function Add(AClass: TControlClass; AParent: TWinControl; L, T, W, H: Integer): TControl;
begin
  Result := AClass.Create(AParent.Owner);
  Result.Parent := AParent;
  Result.SetBounds(L, T, W, H);
end;

procedure Cycle;
var
  F: TForm;
  Pages: TPageControl;
  Sheet: TTabSheet;
  Bar: TToolBar;
  Btn: TToolButton;
  Menu: TMainMenu;
  Item: TMenuItem;
  Images: TImageList;
  Bmp: TBitmap;
  Tree: TTreeView;
  List: TListView;
  Grid: TStringGrid;
  Popup: TPopupMenu;
  Sub: TMenuItem;
  Timer: TTimer;
begin
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 480;
    Menu := TMainMenu.Create(F);
    Item := TMenuItem.Create(F);
    Item.Caption := '&File';
    Menu.Items.Add(Item);
    F.Menu := Menu;
    Popup := TPopupMenu.Create(F);
    Sub := TMenuItem.Create(F);
    Sub.Caption := 'Sub';
    Popup.Items.Add(Sub);
    Item := TMenuItem.Create(F);
    Item.Caption := 'Child';
    Sub.Add(Item);
    F.PopupMenu := Popup;
    if (Popup.Handle = 0) or (Sub.Handle = 0) then
      raise Exception.Create('popup menu handles not created');
    Timer := TTimer.Create(F);
    Timer.Interval := 60000;
    Timer.OnTimer := Ticker.Tick;
    Images := TImageList.Create(F);
    Bmp := TBitmap.Create;
    try
      Bmp.SetSize(16, 16);
      Bmp.Canvas.Brush.Color := clRed;
      Bmp.Canvas.FillRect(Rect(0, 0, 16, 16));
      Images.Add(Bmp, nil);
    finally
      Bmp.Free;
    end;
    TLabel(Add(TLabel, F, 8, 8, 60, 15)).Caption := 'Label';
    TEdit(Add(TEdit, F, 8, 30, 100, 23)).Text := 'Edit';
    TMemo(Add(TMemo, F, 8, 60, 100, 50)).Lines.Add('Memo');
    TButton(Add(TButton, F, 120, 8, 75, 25)).Caption := 'Button';
    TCheckBox(Add(TCheckBox, F, 120, 40, 90, 17)).Checked := True;
    TRadioButton(Add(TRadioButton, F, 120, 60, 90, 17)).Checked := True;
    TGroupBox(Add(TGroupBox, F, 220, 8, 100, 60)).Caption := 'Group';
    TListBox(Add(TListBox, F, 220, 80, 100, 50)).Items.Add('Item');
    TComboBox(Add(TComboBox, F, 330, 8, 100, 23)).Items.Add('Item');
    Add(TScrollBar, F, 330, 40, 100, 17);
    TStaticText(Add(TStaticText, F, 330, 60, 100, 17)).Caption := 'Static';
    TPanel(Add(TPanel, F, 440, 8, 100, 40)).Caption := 'Panel';
    TImage(Add(TImage, F, 440, 60, 32, 32)).Picture.Bitmap.SetSize(32, 32);
    Add(TShape, F, 480, 60, 32, 32);
    Add(TBevel, F, 520, 60, 32, 32);
    Add(TSplitter, F, 0, 0, 3, 100).Align := alLeft;
    Add(TPanel, TWinControl(Add(TScrollBox, F, 8, 140, 100, 80)), 0, 0, 200, 40);
    Pages := TPageControl(Add(TPageControl, F, 120, 140, 200, 100));
    Sheet := TTabSheet.Create(F);
    Sheet.PageControl := Pages;
    Sheet.Caption := 'Sheet';
    Pages.Images := Images;
    Add(TProgressBar, F, 330, 140, 100, 17);
    Add(TTrackBar, F, 330, 160, 100, 30);
    List := TListView(Add(TListView, F, 8, 250, 150, 100));
    List.ViewStyle := vsReport;
    List.Columns.Add.Caption := 'Column';
    List.Items.Add.Caption := 'Item';
    List.SmallImages := Images;
    Tree := TTreeView(Add(TTreeView, F, 170, 250, 150, 100));
    Tree.Items.AddChild(Tree.Items.Add(nil, 'Root'), 'Child');
    Tree.Images := Images;
    Grid := TStringGrid(Add(TStringGrid, F, 330, 250, 200, 100));
    Grid.Cells[1, 1] := 'Cell';
    Bar := TToolBar(Add(TToolBar, F, 0, 0, 640, 29));
    Bar.Align := alTop;
    Bar.Images := Images;
    Btn := TToolButton.Create(F);
    Btn.Parent := Bar;
    Btn.ImageIndex := 0;
    TStatusBar(Add(TStatusBar, F, 0, 461, 640, 19)).SimpleText := 'Status';
    F.HandleNeeded;
    Bmp := TBitmap.Create;
    try
      Bmp.SetSize(F.ClientWidth, F.ClientHeight);
      SendMessageW(F.Handle, WM_PRINT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_CHILDREN or PRF_ERASEBKGND);
    finally
      Bmp.Free;
    end;
    Application.ProcessMessages;
  finally
    F.Free;
  end;
  Application.ProcessMessages;
end;

var
  I: Integer;
  Line: string;
begin
  Application.Initialize;
  Ticker := TTicker.Create;
  Cycle;
  WriteLn('warm');
  Flush(Output);
  ReadLn(Line);
  for I := 1 to 200 do
    Cycle;
  WriteLn('done');
  Flush(Output);
  ReadLn(Line);
  Ticker.Free;
end.
