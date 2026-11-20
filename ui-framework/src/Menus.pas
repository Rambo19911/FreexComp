{ Menus - shortcut functions (actions-menus.md section 3), the menu item tree and its native menus
  (controls-standard.md section 12). }
unit Menus;

interface

uses
  Windows, Messages, SysUtils, Classes, Graphics, ImgList, UIConsts;

type
  { the same type as Classes.TShortCut (actions-menus.md section 3) }
  TShortCut = Classes.TShortCut;

const
  scShift = $2000;
  scCtrl = $4000;
  scAlt = $8000;
  scNone = 0;

type
  TMenuBreak = (mbNone, mbBreak, mbBarBreak);
  TFindItemKind = (fkCommand, fkHandle);

  TMenu = class;

  { controls-standard.md section 12 }
  TMenuItem = class(TComponent)
  private
    FItems: TList;
    FParent: TMenuItem;
    FMenu: TMenu;                        { set on the invisible root item of a menu }
    FCommand: Word;
    FHandle: HMENU;
    FBitmaps: TList;                     { item bitmaps inserted into FHandle, owned }
    FAutoCheck: Boolean;
    FBreak: TMenuBreak;
    FCaption: string;
    FChecked: Boolean;
    FDefault: Boolean;
    FEnabled: Boolean;
    FGroupIndex: Byte;
    FHint: string;
    FImageIndex: Integer;
    FRadioItem: Boolean;
    FShortCut: TShortCut;
    FVisible: Boolean;
    FOnClick: TNotifyEvent;
    function GetCount: Integer;
    function GetItem(Index: Integer): TMenuItem;
    function GetHandle: HMENU;
    function GetMenuIndex: Integer;
    procedure SetMenuIndex(Value: Integer);
    procedure SetBreak(Value: TMenuBreak);
    procedure SetCaption(const Value: string);
    procedure SetChecked(Value: Boolean);
    procedure SetDefault(Value: Boolean);
    procedure SetEnabled(Value: Boolean);
    procedure SetGroupIndex(Value: Byte);
    procedure SetImageIndex(Value: Integer);
    procedure SetRadioItem(Value: Boolean);
    procedure SetShortCut(Value: TShortCut);
    procedure SetVisible(Value: Boolean);
    procedure ClearNative;
    procedure InsertNative(Child: TMenuItem; Position: Integer);
    procedure TurnSiblingsOff;
  protected
    procedure MenuChanged;
    procedure RebuildHandle;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); override;
    procedure SetParentComponent(Value: TComponent); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function GetParentComponent: TComponent; override;
    function HasParent: Boolean; override;
    procedure Add(Item: TMenuItem);
    procedure Insert(Index: Integer; Item: TMenuItem);
    procedure Delete(Index: Integer);
    procedure Remove(Item: TMenuItem);
    procedure Clear;
    function IndexOf(Item: TMenuItem): Integer;
    function Find(ACaption: string): TMenuItem;
    procedure Click; virtual;
    function GetParentMenu: TMenu;
    function IsLine: Boolean;
    function HandleAllocated: Boolean;
    property Items[Index: Integer]: TMenuItem read GetItem; default;
    property Count: Integer read GetCount;
    property Parent: TMenuItem read FParent;
    property MenuIndex: Integer read GetMenuIndex write SetMenuIndex;
    property Command: Word read FCommand;
    property Handle: HMENU read GetHandle;
  published
    property AutoCheck: Boolean read FAutoCheck write FAutoCheck default False;
    property Break: TMenuBreak read FBreak write SetBreak default mbNone;
    property Caption: string read FCaption write SetCaption;
    property Checked: Boolean read FChecked write SetChecked default False;
    property Default: Boolean read FDefault write SetDefault default False;
    property Enabled: Boolean read FEnabled write SetEnabled default True;
    property GroupIndex: Byte read FGroupIndex write SetGroupIndex default 0;
    property Hint: string read FHint write FHint;
    property ImageIndex: Integer read FImageIndex write SetImageIndex default -1;
    property RadioItem: Boolean read FRadioItem write SetRadioItem default False;
    property ShortCut: TShortCut read FShortCut write SetShortCut default 0;
    property Visible: Boolean read FVisible write SetVisible default True;
    property OnClick: TNotifyEvent read FOnClick write FOnClick;
  end;

  { abstract base of TMainMenu and TPopupMenu }
  TMenu = class(TComponent)
  private
    FItems: TMenuItem;
    FImages: TCustomImageList;
    FImageChangeLink: TChangeLink;
    function GetHandle: HMENU;
    procedure SetImages(Value: TCustomImageList);
    procedure ImageListChange(Sender: TObject);
  protected
    function CreateMenuHandle: HMENU; virtual;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { the root's native menu was rebuilt }
    procedure MenuChanged; virtual;
    property Images: TCustomImageList read FImages write SetImages;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function HandleAllocated: Boolean;
    { the item of the tree with this Command id (fkCommand) or sub-menu handle (fkHandle); nil if none }
    function FindItem(Value: NativeUInt; Kind: TFindItemKind): TMenuItem;
    { UIF-722: Click on the item with this Command id; False if the menu has none }
    function DispatchCommand(ACommand: Word): Boolean;
    { UIF-723: WM_INITMENUPOPUP for one of this menu's sub-menus; False if the handle is not one of them }
    function DispatchPopup(AHandle: HMENU): Boolean;
    property Items: TMenuItem read FItems;
    property Handle: HMENU read GetHandle;
  end;

  TMainMenu = class(TMenu)
  private
    FWindowHandle: HWND;
  protected
    function CreateMenuHandle: HMENU; override;
    { UIF-720: DrawMenuBar on the form window after the bar was rebuilt }
    procedure MenuChanged; override;
  public
    { the form window showing this menu bar (set by the form), 0 if none }
    property WindowHandle: HWND read FWindowHandle write FWindowHandle;
  published
    property Images;
  end;

  TPopupAlignment = (paLeft, paRight, paCenter);

  { controls-standard.md section 12, UIF-724 }
  TPopupMenu = class(TMenu)
  private
    FAlignment: TPopupAlignment;
    FAutoPopup: Boolean;
    FPopupComponent: TComponent;
    FPopupPoint: TPoint;
    FOnPopup: TNotifyEvent;
    procedure SetPopupComponent(Value: TComponent);
  protected
    procedure DoPopup(Sender: TObject); virtual;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { screen coordinates; returns when the menu closes (a chosen item arrives later as WM_COMMAND) }
    procedure Popup(X, Y: Integer); virtual;
    property PopupComponent: TComponent read FPopupComponent write SetPopupComponent;
    property PopupPoint: TPoint read FPopupPoint;
  published
    property Alignment: TPopupAlignment read FAlignment write FAlignment default paLeft;
    property AutoPopup: Boolean read FAutoPopup write FAutoPopup default True;
    property Images;
    property OnPopup: TNotifyEvent read FOnPopup write FOnPopup;
  end;

function ShortCut(Key: Word; Shift: TShiftState): TShortCut;
procedure ShortCutToKey(ShortCut: TShortCut; var Key: Word; var Shift: TShiftState);
function ShortCutToText(ShortCut: TShortCut): string;
function TextToShortCut(const Text: string): TShortCut;

{ the caption without accelerator marks: '&' removed, '&&' gives '&' }
function StripHotkey(const Text: string): string;

{ UIF-724: the hidden owner window of every pop-up menu (class PN.PopupList), created on first use }
function PopupListWindow: HWND;

{ UIF-722: Click on the item with this Command id of any existing TPopupMenu; False if none has it }
function DispatchPopupCommand(ACommand: Word): Boolean;

implementation

uses
  RTLConsts;

type
  { protected access to the image size (local descendant) }
  TImageListAccess = class(TCustomImageList);

{ UIF-930 }
function ShortCut(Key: Word; Shift: TShiftState): TShortCut;
begin
  Result := 0;
  if Key > 255 then
    Exit;
  Result := Key;
  if ssShift in Shift then
    Result := Result + scShift;
  if ssCtrl in Shift then
    Result := Result + scCtrl;
  if ssAlt in Shift then
    Result := Result + scAlt;
end;

procedure ShortCutToKey(ShortCut: TShortCut; var Key: Word; var Shift: TShiftState);
begin
  Key := ShortCut and $FF;
  Shift := [];
  if ShortCut and scShift <> 0 then
    Include(Shift, ssShift);
  if ShortCut and scCtrl <> 0 then
    Include(Shift, ssCtrl);
  if ShortCut and scAlt <> 0 then
    Include(Shift, ssAlt);
end;

{ UIF-932: the fixed, non-localised key names; '' for a key without one }
function FixedKeyName(Key: Word): string;
begin
  Result := '';
  case Key of
    Ord('A')..Ord('Z'), Ord('0')..Ord('9'): Result := Chr(Key);
    $70..$87: Result := 'F' + IntToStr(Key - $6F);
    8: Result := 'BkSp';
    9: Result := 'Tab';
    13: Result := 'Enter';
    27: Result := 'Esc';
    32: Result := 'Space';
    33: Result := 'PgUp';
    34: Result := 'PgDn';
    35: Result := 'End';
    36: Result := 'Home';
    37: Result := 'Left';
    38: Result := 'Up';
    39: Result := 'Right';
    40: Result := 'Down';
    45: Result := 'Ins';
    46: Result := 'Del';
    $60..$69: Result := 'Num ' + Chr(Ord('0') + Key - $60);
    $6A: Result := 'Num *';
    $6B: Result := 'Num +';
    $6D: Result := 'Num -';
    $6E: Result := 'Num .';
    $6F: Result := 'Num /';
  end;
end;

function KeyName(Key: Word): string;
var
  Buffer: array[0..63] of WideChar;
  N: Integer;
begin
  Result := FixedKeyName(Key);
  if Result <> '' then
    Exit;
  N := GetKeyNameTextW(LONG(MapVirtualKeyW(Key, MAPVK_VK_TO_VSC) shl 16), @Buffer[0], Length(Buffer));
  if N > 0 then
    SetString(Result, PWideChar(@Buffer[0]), N);
end;

{ UIF-931 }
function ShortCutToText(ShortCut: TShortCut): string;
var
  Name: string;
begin
  Result := '';
  if ShortCut = 0 then
    Exit;
  Name := KeyName(ShortCut and $FF);
  if Name = '' then
    Exit;
  if ShortCut and scCtrl <> 0 then
    Result := Result + 'Ctrl+';
  if ShortCut and scAlt <> 0 then
    Result := Result + 'Alt+';
  if ShortCut and scShift <> 0 then
    Result := Result + 'Shift+';
  Result := Result + Name;
end;

{ removes a leading "<Modifier> +" (case-insensitive, optional spaces) from S; True if one was found }
function TakeModifier(var S: string; const Modifier: string): Boolean;
var
  I: Integer;
begin
  Result := False;
  if not SameText(Copy(S, 1, Length(Modifier)), Modifier) then
    Exit;
  I := Length(Modifier) + 1;
  while (I <= Length(S)) and (S[I] = ' ') do
    Inc(I);
  if (I > Length(S)) or (S[I] <> '+') then
    Exit;
  Inc(I);
  while (I <= Length(S)) and (S[I] = ' ') do
    Inc(I);
  S := Copy(S, I, MaxInt);
  Result := True;
end;

{ UIF-933 }
function TextToShortCut(const Text: string): TShortCut;
var
  S: string;
  Modifiers: Word;
  Key: Integer;
  Found: Boolean;
begin
  Result := 0;
  S := Trim(Text);
  Modifiers := 0;
  repeat
    Found := True;
    if TakeModifier(S, 'Ctrl') then
      Modifiers := Modifiers or scCtrl
    else if TakeModifier(S, 'Alt') then
      Modifiers := Modifiers or scAlt
    else if TakeModifier(S, 'Shift') then
      Modifiers := Modifiers or scShift
    else
      Found := False;
  until not Found;
  if S = '' then
    Exit;
  for Key := 1 to 255 do
    if (FixedKeyName(Key) <> '') and SameText(FixedKeyName(Key), S) then
    begin
      Result := Key or Modifiers;
      Exit;
    end;
end;


function StripHotkey(const Text: string): string;
var
  I: Integer;
begin
  Result := '';
  I := 1;
  while I <= Length(Text) do
  begin
    if Text[I] = '&' then
      Inc(I);
    if I <= Length(Text) then
      Result := Result + Text[I];
    Inc(I);
  end;
end;

{ ---------------------------------------------------------------- UIF-720: command ids }

var
  CommandUsed: array[0..65535] of Boolean;
  NextCommand: Integer = 1;

function AllocateCommand: Word;
var
  I, C: Integer;
begin
  for I := 0 to 65534 do
  begin
    C := (NextCommand - 1 + I) mod 65535 + 1;
    if not CommandUsed[C] then
    begin
      CommandUsed[C] := True;
      NextCommand := C mod 65535 + 1;
      Result := C;
      Exit;
    end;
  end;
  raise EOutOfResources.Create(SOutOfResources);
end;

procedure ReleaseCommand(Command: Word);
begin
  if Command <> 0 then
    CommandUsed[Command] := False;
end;

{ ---------------------------------------------------------------- TMenuItem }

constructor TMenuItem.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FEnabled := True;
  FVisible := True;
  FImageIndex := -1;
  FCommand := AllocateCommand;
end;

destructor TMenuItem.Destroy;
var
  Child: TMenuItem;
begin
  if FParent <> nil then
    FParent.Remove(Self);
  ClearNative;
  if FItems <> nil then
  begin
    while FItems.Count > 0 do
    begin
      Child := TMenuItem(FItems.Last);
      FItems.Delete(FItems.Count - 1);
      Child.FParent := nil;
      Child.Free;
    end;
    FItems.Free;
  end;
  if FHandle <> 0 then
    DestroyMenu(FHandle);
  FBitmaps.Free;
  ReleaseCommand(FCommand);
  inherited Destroy;
end;

function TMenuItem.GetCount: Integer;
begin
  if FItems = nil then
    Result := 0
  else
    Result := FItems.Count;
end;

function TMenuItem.GetItem(Index: Integer): TMenuItem;
begin
  if FItems = nil then
    raise EListError.CreateFmt(SListIndexError, [Index]);
  Result := TMenuItem(FItems[Index]);
end;

function TMenuItem.IndexOf(Item: TMenuItem): Integer;
begin
  if FItems = nil then
    Result := -1
  else
    Result := FItems.IndexOf(Pointer(Item));
end;

procedure TMenuItem.Add(Item: TMenuItem);
begin
  Insert(Count, Item);
end;

procedure TMenuItem.Insert(Index: Integer; Item: TMenuItem);
var
  P: TMenuItem;
begin
  if Item = nil then
    Exit;
  { an item cannot contain itself, one of its ancestors, or the root of a menu }
  P := Self;
  while P <> nil do
  begin
    if P = Item then
      raise EInvalidOperation.CreateFmt(SMenuInsert, [Item.Name]);
    P := P.FParent;
  end;
  if Item.FMenu <> nil then
    raise EInvalidOperation.CreateFmt(SMenuInsert, [Item.Name]);
  if Item.FParent <> nil then
    Item.FParent.Remove(Item);
  if (Index < 0) or (Index > Count) then
    raise EListError.CreateFmt(SListIndexError, [Index]);
  if FItems = nil then
    FItems := TList.Create;
  FItems.Insert(Index, Pointer(Item));
  Item.FParent := Self;
  RebuildHandle;
  MenuChanged;                           { the parent's entry may gain or lose its sub-menu }
end;

procedure TMenuItem.Delete(Index: Integer);
var
  Item: TMenuItem;
begin
  Item := GetItem(Index);
  FItems.Delete(Index);
  Item.FParent := nil;
  RebuildHandle;
  MenuChanged;
end;

procedure TMenuItem.Remove(Item: TMenuItem);
var
  I: Integer;
begin
  I := IndexOf(Item);
  if I >= 0 then
    Delete(I);
end;

procedure TMenuItem.Clear;
begin
  while Count > 0 do
    Items[Count - 1].Free;
end;

function TMenuItem.GetMenuIndex: Integer;
begin
  if FParent = nil then
    Result := -1
  else
    Result := FParent.IndexOf(Self);
end;

procedure TMenuItem.SetMenuIndex(Value: Integer);
var
  P: TMenuItem;
  I: Integer;
begin
  P := FParent;
  if P = nil then
    Exit;
  I := P.IndexOf(Self);
  if Value < 0 then
    Value := 0;
  if Value >= P.Count then
    Value := P.Count - 1;
  if Value = I then
    Exit;
  P.FItems.Move(I, Value);
  P.RebuildHandle;
end;

function TMenuItem.Find(ACaption: string): TMenuItem;
var
  I: Integer;
begin
  Result := nil;
  ACaption := StripHotkey(ACaption);
  for I := 0 to Count - 1 do
    if SameText(StripHotkey(Items[I].Caption), ACaption) then
    begin
      Result := Items[I];
      Exit;
    end;
end;

function TMenuItem.GetParentMenu: TMenu;
var
  P: TMenuItem;
begin
  P := Self;
  while P.FParent <> nil do
    P := P.FParent;
  Result := P.FMenu;
end;

function TMenuItem.IsLine: Boolean;
begin
  Result := FCaption = '-';
end;

{ UIF-721 }
function TMenuItem.HasParent: Boolean;
begin
  Result := True;
end;

function TMenuItem.GetParentComponent: TComponent;
begin
  if (FParent <> nil) and (FParent.FMenu <> nil) then
    Result := FParent.FMenu
  else
    Result := FParent;
end;

procedure TMenuItem.SetParentComponent(Value: TComponent);
begin
  if Value is TMenu then
    TMenu(Value).Items.Add(Self)
  else if Value is TMenuItem then
    TMenuItem(Value).Add(Self)
  else if Value <> nil then
    raise EInvalidOperation.CreateFmt(SInvalidParentClass, [Name, Value.ClassName]);
end;

procedure TMenuItem.GetChildren(Proc: TGetChildProc; Root: TComponent);
var
  I: Integer;
begin
  for I := 0 to Count - 1 do
    Proc(Items[I]);
end;

{ UIF-722 (actions: U-077) }
procedure TMenuItem.Click;
begin
  if not FEnabled then
    Exit;
  if FAutoCheck then
  begin
    if FRadioItem then
      Checked := True
    else
      Checked := not FChecked;
  end;
  if Assigned(FOnClick) then
    FOnClick(Self);
end;

{ ---- property setters: a changed entry rebuilds the parent's native menu (UIF-720) }

procedure TMenuItem.SetBreak(Value: TMenuBreak);
begin
  if FBreak <> Value then
  begin
    FBreak := Value;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetCaption(const Value: string);
begin
  if FCaption <> Value then
  begin
    FCaption := Value;
    MenuChanged;
  end;
end;

{ checking a radio item unchecks the other radio items of the same parent with the same GroupIndex }
procedure TMenuItem.TurnSiblingsOff;
var
  I: Integer;
  Sibling: TMenuItem;
begin
  if (FParent = nil) or (csLoading in ComponentState) then
    Exit;
  for I := 0 to FParent.Count - 1 do
  begin
    Sibling := FParent.Items[I];
    if (Sibling <> Self) and Sibling.FRadioItem and (Sibling.FGroupIndex = FGroupIndex) and Sibling.FChecked then
      Sibling.FChecked := False;
  end;
end;

procedure TMenuItem.SetChecked(Value: Boolean);
begin
  if FChecked <> Value then
  begin
    FChecked := Value;
    if Value and FRadioItem then
      TurnSiblingsOff;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetDefault(Value: Boolean);
begin
  if FDefault <> Value then
  begin
    FDefault := Value;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetEnabled(Value: Boolean);
begin
  if FEnabled <> Value then
  begin
    FEnabled := Value;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetGroupIndex(Value: Byte);
begin
  if FGroupIndex <> Value then
  begin
    FGroupIndex := Value;
    if FChecked and FRadioItem then
    begin
      TurnSiblingsOff;
      MenuChanged;
    end;
  end;
end;

procedure TMenuItem.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetRadioItem(Value: Boolean);
begin
  if FRadioItem <> Value then
  begin
    FRadioItem := Value;
    if FChecked and FRadioItem then
      TurnSiblingsOff;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetShortCut(Value: TShortCut);
begin
  if FShortCut <> Value then
  begin
    FShortCut := Value;
    MenuChanged;
  end;
end;

procedure TMenuItem.SetVisible(Value: Boolean);
begin
  if FVisible <> Value then
  begin
    FVisible := Value;
    MenuChanged;
  end;
end;

{ ---- native menu (UIF-720, UIF-725, UIF-934) }

function TMenuItem.HandleAllocated: Boolean;
begin
  Result := FHandle <> 0;
end;

function TMenuItem.GetHandle: HMENU;
begin
  if FHandle = 0 then
  begin
    if FMenu <> nil then
      FHandle := FMenu.CreateMenuHandle
    else
      FHandle := CreatePopupMenu;
    if FHandle = 0 then
      raise EOutOfResources.Create(SOutOfResources);
    RebuildHandle;
  end;
  Result := FHandle;
end;

{ detaches every native entry (RemoveMenu keeps the sub-menus alive) and frees the item bitmaps }
procedure TMenuItem.ClearNative;
var
  I: Integer;
begin
  if FHandle <> 0 then
    while GetMenuItemCount(FHandle) > 0 do
      RemoveMenu(FHandle, 0, MF_BYPOSITION);
  if FBitmaps <> nil then
  begin
    for I := 0 to FBitmaps.Count - 1 do
      DeleteObject(HBITMAP(FBitmaps[I]));
    FBitmaps.Clear;
  end;
end;

procedure TMenuItem.InsertNative(Child: TMenuItem; Position: Integer);
var
  Info: TMenuItemInfoW;
  Text: string;
  Images: TCustomImageList;
  Menu: TMenu;
  Bmp: TBitmap;
  BmpHandle: HBITMAP;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := MIIM_FTYPE or MIIM_STATE or MIIM_ID;
  Info.wID := Child.FCommand;
  if Child.IsLine then
    Info.fType := MFT_SEPARATOR
  else
  begin
    Info.fMask := Info.fMask or MIIM_STRING or MIIM_SUBMENU or MIIM_BITMAP;
    Text := Child.FCaption;
    if Child.FShortCut <> 0 then
      Text := Text + #9 + ShortCutToText(Child.FShortCut);
    Info.dwTypeData := PWideChar(Text);
    Info.cch := Length(Text);
    if Child.FRadioItem then
      Info.fType := Info.fType or MFT_RADIOCHECK;
    if Child.Count > 0 then
      Info.hSubMenu := Child.Handle;
    Menu := GetParentMenu;
    Images := nil;
    if Menu <> nil then
      Images := Menu.FImages;
    if (Images <> nil) and (Child.FImageIndex >= 0) and (Child.FImageIndex < Images.Count) then
    begin
      { a 32-bit bitmap extracted from the image list (controls-standard.md section 12, ImageIndex) }
      Bmp := TBitmap.Create;
      try
        Bmp.PixelFormat := pf32bit;
        Bmp.SetSize(TImageListAccess(Images).Width, TImageListAccess(Images).Height);
        Images.Draw(Bmp.Canvas, 0, 0, Child.FImageIndex, Child.FEnabled);
        BmpHandle := Bmp.ReleaseHandle;
      finally
        Bmp.Free;
      end;
      if FBitmaps = nil then
        FBitmaps := TList.Create;
      FBitmaps.Add(Pointer(BmpHandle));
      Info.hbmpItem := BmpHandle;
    end;
  end;
  case Child.FBreak of
    mbBreak: Info.fType := Info.fType or MFT_MENUBREAK;
    mbBarBreak: Info.fType := Info.fType or MFT_MENUBARBREAK;
  end;
  if Child.FChecked then
    Info.fState := Info.fState or MFS_CHECKED;
  if Child.FDefault then
    Info.fState := Info.fState or MFS_DEFAULT;
  if not Child.FEnabled then
    Info.fState := Info.fState or MFS_GRAYED;
  InsertMenuItemW(FHandle, Position, True, Info);
end;

{ refills the native menu of this item with its visible children, keeping the HMENU (a parent entry refers to it) }
procedure TMenuItem.RebuildHandle;
var
  I, Position: Integer;
begin
  if FHandle = 0 then
    Exit;
  ClearNative;
  Position := 0;
  for I := 0 to Count - 1 do
    if Items[I].FVisible then
    begin
      InsertNative(Items[I], Position);
      Inc(Position);
    end;
  if FMenu <> nil then
    FMenu.MenuChanged;
end;

{ this item's own entry changed: the parent's native menu is rebuilt }
procedure TMenuItem.MenuChanged;
begin
  if FParent <> nil then
    FParent.RebuildHandle;
end;

{ ---------------------------------------------------------------- TMenu }

constructor TMenu.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FItems := TMenuItem.Create(nil);
  FItems.FMenu := Self;
  FImageChangeLink := TChangeLink.Create;
  FImageChangeLink.OnChange := ImageListChange;
end;

destructor TMenu.Destroy;
begin
  SetImages(nil);
  inherited Destroy;           { free notifications first: a form detaches the menu bar before it is destroyed }
  FItems.Free;
  FImageChangeLink.Free;
end;

function TMenu.CreateMenuHandle: HMENU;
begin
  Result := CreatePopupMenu;
end;

function TMenu.GetHandle: HMENU;
begin
  Result := FItems.Handle;
end;

function TMenu.HandleAllocated: Boolean;
begin
  Result := FItems.HandleAllocated;
end;

procedure TMenu.GetChildren(Proc: TGetChildProc; Root: TComponent);
begin
  FItems.GetChildren(Proc, Root);
end;

procedure TMenu.MenuChanged;
begin
end;

function FindIn(Item: TMenuItem; Value: NativeUInt; Kind: TFindItemKind): TMenuItem;
var
  I: Integer;
begin
  if ((Kind = fkCommand) and (Item.FCommand = Value)) or ((Kind = fkHandle) and (Item.FHandle <> 0)
    and (NativeUInt(Item.FHandle) = Value)) then
  begin
    Result := Item;
    Exit;
  end;
  for I := 0 to Item.Count - 1 do
  begin
    Result := FindIn(Item.Items[I], Value, Kind);
    if Result <> nil then
      Exit;
  end;
  Result := nil;
end;

function TMenu.FindItem(Value: NativeUInt; Kind: TFindItemKind): TMenuItem;
begin
  Result := nil;
  if (Kind = fkCommand) and (Value = 0) then
    Exit;
  Result := FindIn(FItems, Value, Kind);
end;

function TMenu.DispatchCommand(ACommand: Word): Boolean;
var
  Item: TMenuItem;
begin
  Item := FindItem(ACommand, fkCommand);
  Result := (Item <> nil) and (Item <> FItems);
  if Result then
    Item.Click;
end;

{ UIF-723: the owning item's OnClick first (update hook), then the native menu is refreshed; linked actions of
  the children are updated with the action links (U-077) }
function TMenu.DispatchPopup(AHandle: HMENU): Boolean;
var
  Item: TMenuItem;
begin
  Item := FindItem(NativeUInt(AHandle), fkHandle);
  Result := Item <> nil;
  if not Result then
    Exit;
  if Assigned(Item.FOnClick) then
    Item.FOnClick(Item);
  Item.RebuildHandle;
end;

{ every existing native menu of the tree is rebuilt (item bitmaps) }
procedure RebuildTree(Item: TMenuItem);
var
  I: Integer;
begin
  Item.RebuildHandle;
  for I := 0 to Item.Count - 1 do
    RebuildTree(Item.Items[I]);
end;

procedure TMenu.ImageListChange(Sender: TObject);
begin
  RebuildTree(FItems);
end;

{ UIF-567 }
procedure TMenu.SetImages(Value: TCustomImageList);
begin
  if FImages = Value then
    Exit;
  if FImages <> nil then
  begin
    FImages.UnRegisterChanges(FImageChangeLink);
    FImages.RemoveFreeNotification(Self);
  end;
  FImages := Value;
  if FImages <> nil then
  begin
    FImages.RegisterChanges(FImageChangeLink);
    FImages.FreeNotification(Self);
  end;
  if not (csDestroying in ComponentState) then
    RebuildTree(FItems);
end;

procedure TMenu.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FImages) then
  begin
    FImages := nil;
    RebuildTree(FItems);
  end;
end;

{ ---------------------------------------------------------------- TMainMenu }

{ UIF-725: the root of a main menu is a menu bar }
function TMainMenu.CreateMenuHandle: HMENU;
begin
  Result := CreateMenu;
end;

procedure TMainMenu.MenuChanged;
begin
  if FWindowHandle <> 0 then
    DrawMenuBar(FWindowHandle);
end;

{ ---------------------------------------------------------------- UIF-724: popup list and owner window }

const
  PopupListClassName = 'PN.PopupList';

var
  PopupList: TList;                      { every existing TPopupMenu }
  PopupWindow: HWND;

function DispatchPopupCommand(ACommand: Word): Boolean;
var
  I: Integer;
begin
  Result := False;
  if ACommand = 0 then
    Exit;
  for I := 0 to PopupList.Count - 1 do
    if TPopupMenu(PopupList[I]).DispatchCommand(ACommand) then
    begin
      Result := True;
      Exit;
    end;
end;

function DispatchPopupInit(AHandle: HMENU): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to PopupList.Count - 1 do
    if TPopupMenu(PopupList[I]).DispatchPopup(AHandle) then
    begin
      Result := True;
      Exit;
    end;
end;

{ WM_COMMAND and WM_INITMENUPOPUP go to the menus; WM_MENUSELECT (hints) comes with U-079 }
function PopupListWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  case Msg of
    WM_COMMAND:
      if (LParam = 0) and DispatchPopupCommand(LoWord(WParam)) then
        Exit;
    WM_INITMENUPOPUP:
      if (HiWord(LParam) = 0) and DispatchPopupInit(HMENU(WParam)) then
        Exit;
  end;
  Result := DefWindowProcW(Wnd, Msg, WParam, LParam);
end;

function PopupListWindow: HWND;
var
  Info: TWndClassExW;
begin
  if PopupWindow = 0 then
  begin
    FillChar(Info, SizeOf(Info), 0);
    Info.cbSize := SizeOf(Info);
    if not GetClassInfoExW(HINST(HInstance), PopupListClassName, Info) then
    begin
      Info.cbSize := SizeOf(Info);
      Info.lpfnWndProc := @PopupListWndProc;
      Info.hInstance := HINST(HInstance);
      Info.lpszClassName := PopupListClassName;
      if RegisterClassExW(Info) = 0 then
        raise EOutOfResources.CreateFmt(SWindowClass, [SysErrorMessage(GetLastError)]);
    end;
    PopupWindow := CreateWindowExW(WS_EX_TOOLWINDOW, PopupListClassName, '', WS_POPUP, 0, 0, 0, 0, 0, 0,
      HINST(HInstance), nil);
    if PopupWindow = 0 then
      raise EOutOfResources.CreateFmt(SWindowCreate, [SysErrorMessage(GetLastError)]);
  end;
  Result := PopupWindow;
end;

{ ---------------------------------------------------------------- TPopupMenu }

constructor TPopupMenu.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAutoPopup := True;
  PopupList.Add(Pointer(Self));
end;

destructor TPopupMenu.Destroy;
begin
  PopupList.Remove(Pointer(Self));
  inherited Destroy;
end;

procedure TPopupMenu.SetPopupComponent(Value: TComponent);
begin
  if FPopupComponent = Value then
    Exit;
  if FPopupComponent <> nil then
    FPopupComponent.RemoveFreeNotification(Self);
  FPopupComponent := Value;
  if FPopupComponent <> nil then
    FPopupComponent.FreeNotification(Self);
end;

procedure TPopupMenu.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FPopupComponent) then
    FPopupComponent := nil;
end;

procedure TPopupMenu.DoPopup(Sender: TObject);
begin
  if Assigned(FOnPopup) then
    FOnPopup(Sender);
end;

{ UIF-724 (action updates of UIF-911 come with the action links, U-077) }
procedure TPopupMenu.Popup(X, Y: Integer);
const
  AlignFlags: array[TPopupAlignment] of UINT = (TPM_LEFTALIGN, TPM_RIGHTALIGN, TPM_CENTERALIGN);
var
  Owner: HWND;
begin
  FPopupPoint := Point(X, Y);
  DoPopup(Self);
  Owner := PopupListWindow;
  SetForegroundWindow(Owner);
  TrackPopupMenuEx(Items.Handle, AlignFlags[FAlignment] or TPM_RIGHTBUTTON, X, Y, Owner, nil);
  PostMessageW(Owner, WM_NULL, 0, 0);
end;

initialization
  PopupList := TList.Create;
  RegisterClasses([TMenuItem, TMainMenu, TPopupMenu]);

finalization
  if PopupWindow <> 0 then
    DestroyWindow(PopupWindow);
  PopupList.Free;
end.
