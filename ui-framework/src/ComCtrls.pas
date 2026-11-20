{ ComCtrls - common controls (controls-standard.md section 20: TPageControl, TTabSheet). }
unit ComCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ImgList, CommCtrl, Menus, UIConsts;

type
  TTabPosition = (tpTop, tpBottom, tpLeft, tpRight);
  TTabChangingEvent = procedure(Sender: TObject; var AllowChange: Boolean) of object;

  TPageControl = class;

  { controls-standard.md section 20: one page of a TPageControl; bounds and Visible are managed by the page control }
  TTabSheet = class(TWinControl)
  private
    FPageControl: TPageControl;
    FImageIndex: Integer;
    FTabVisible: Boolean;
    FOnShow: TNotifyEvent;
    FOnHide: TNotifyEvent;
    function GetPageIndex: Integer;
    function GetTabIndex: Integer;
    procedure SetImageIndex(Value: Integer);
    procedure SetPageControl(Value: TPageControl);
    procedure SetPageIndex(Value: Integer);
    procedure SetTabVisible(Value: Boolean);
  protected
    procedure DoShow; virtual;
    procedure DoHide; virtual;
    { UIF-770: only a TPageControl may be the parent }
    procedure SetParent(AParent: TWinControl); override;
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property PageControl: TPageControl read FPageControl write SetPageControl;
    property PageIndex: Integer read GetPageIndex write SetPageIndex;
    property TabIndex: Integer read GetTabIndex;
  published
    property Left stored False;
    property Top stored False;
    property Width stored False;
    property Height stored False;
    property Align stored False;
    property Anchors stored False;
    property AlignWithMargins stored False;
    property Margins stored False;
    property Constraints stored False;
    property Visible stored False;
    property Caption;
    property Color default clBtnFace;
    property Font;
    property ImageIndex: Integer read FImageIndex write SetImageIndex default -1;
    property Padding;
    property ParentColor default True;
    property ParentFont;
    property TabVisible: Boolean read FTabVisible write SetTabVisible default True;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnHide: TNotifyEvent read FOnHide write FOnHide;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
    property OnShow: TNotifyEvent read FOnShow write FOnShow;
  end;

  { the native tab strip (SysTabControl32) with the properties shared by tab controls }
  TCustomTabControl = class(TWinControl)
  private
    FHotTrack: Boolean;
    FImages: TCustomImageList;
    FMultiLine: Boolean;
    FTabHeight: SmallInt;
    FTabPosition: TTabPosition;
    FTabWidth: SmallInt;
    FOnChange: TNotifyEvent;
    FOnChanging: TTabChangingEvent;
    procedure SetHotTrack(Value: Boolean);
    procedure SetImages(Value: TCustomImageList);
    procedure SetMultiLine(Value: Boolean);
    procedure SetTabHeight(Value: SmallInt);
    procedure SetTabPosition(Value: TTabPosition);
    procedure SetTabWidth(Value: SmallInt);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    procedure ChangeScale(M, D: Integer); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { the tab height of layout.md UIF-219 }
    function TabH: Integer;
    { TCM_SETITEMSIZE (TabWidth, TabH) and the realign of a changed tab height }
    procedure TabSizeChanged; virtual;
    function CanChange: Boolean; virtual;
    procedure Change; virtual;
    property HotTrack: Boolean read FHotTrack write SetHotTrack default False;
    property Images: TCustomImageList read FImages write SetImages;
    property MultiLine: Boolean read FMultiLine write SetMultiLine default False;
    property TabHeight: SmallInt read FTabHeight write SetTabHeight default 0;
    property TabPosition: TTabPosition read FTabPosition write SetTabPosition default tpTop;
    property TabWidth: SmallInt read FTabWidth write SetTabWidth default 0;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnChanging: TTabChangingEvent read FOnChanging write FOnChanging;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  { controls-standard.md section 20 }
  TPageControl = class(TCustomTabControl)
  private
    FPages: TList;
    FActivePage: TTabSheet;
    function GetActivePageIndex: Integer;
    function GetPage(Index: Integer): TTabSheet;
    function GetPageCount: Integer;
    procedure SetActivePage(Value: TTabSheet);
    procedure SetActivePageIndex(Value: Integer);
    function IsActivePageStored: Boolean;
    procedure InsertPage(Page: TTabSheet);
    procedure RemovePage(Page: TTabSheet);
    procedure MovePage(Page: TTabSheet; Index: Integer);
    procedure PageTabVisibleChanged(Page: TTabSheet);
    { UIF-777: the next page with a tab after Page, else the previous one }
    function SuccessorOf(Page: TTabSheet): TTabSheet;
    { the page of native tab Index }
    function PageOfTab(Index: Integer): TTabSheet;
    { UIF-773 / UIF-774: the switch itself (no OnChanging / OnChange) }
    procedure ChangeActivePage(Page: TTabSheet);
    { UIF-773: a user selection: OnChanging (veto), the switch, OnChange }
    procedure SelectPage(Page: TTabSheet);
    { UIF-776: the native tabs rebuilt from the pages }
    procedure UpdateTabs;
  protected
    procedure CreateWnd; override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
    procedure Loaded; override;
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure SelectNextPage(GoForward: Boolean; CheckTabVisible: Boolean = True);
    function IndexOfTabAt(X, Y: Integer): Integer;
    { UIF-219: the page rectangle in client coordinates }
    function PageRect: TRect;
    property ActivePageIndex: Integer read GetActivePageIndex write SetActivePageIndex;
    property PageCount: Integer read GetPageCount;
    property Pages[Index: Integer]: TTabSheet read GetPage;
  published
    property ActivePage: TTabSheet read FActivePage write SetActivePage stored IsActivePageStored;
    property Font;
    property HotTrack;
    property Images;
    property MultiLine;
    property ParentFont;
    property TabHeight;
    property TabOrder;
    property TabPosition;
    property TabStop default True;
    property TabWidth;
    property OnChange;
    property OnChanging;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

implementation

type
  TFormAccess = class(TCustomForm);

{ the DPI of a control: its form's current PixelsPerInch, 96 (logical) without a form }
function ControlDPI(Control: TControl): Integer;
var
  F: TCustomForm;
begin
  F := GetParentForm(Control);
  if F <> nil then
    Result := TFormAccess(F).PixelsPerInch
  else
    Result := 96;
end;

{ ---------------------------------------------------------------- TTabSheet }

constructor TTabSheet.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  Color := clBtnFace;
  ParentColor := True;
  FImageIndex := -1;
  FTabVisible := True;
end;

destructor TTabSheet.Destroy;
begin
  inherited Destroy;
end;

procedure TTabSheet.DoShow;
begin
  if Assigned(FOnShow) and not (csLoading in ComponentState) then
    FOnShow(Self);
end;

procedure TTabSheet.DoHide;
begin
  if Assigned(FOnHide) and not (csLoading in ComponentState) then
    FOnHide(Self);
end;

procedure TTabSheet.SetParent(AParent: TWinControl);
begin
  if AParent = Parent then
    Exit;
  if (AParent <> nil) and not (AParent is TPageControl) then
    raise EInvalidOperation.CreateFmt(SInvalidParentClass, [Name, AParent.ClassName]);
  if FPageControl <> nil then
    FPageControl.RemovePage(Self);
  inherited SetParent(AParent);
  if AParent <> nil then
    TPageControl(AParent).InsertPage(Self);
end;

procedure TTabSheet.SetPageControl(Value: TPageControl);
begin
  SetParent(Value);
end;

function TTabSheet.GetPageIndex: Integer;
begin
  if FPageControl = nil then
    Result := -1
  else
    Result := FPageControl.FPages.IndexOf(Pointer(Self));
end;

procedure TTabSheet.SetPageIndex(Value: Integer);
begin
  if FPageControl <> nil then
    FPageControl.MovePage(Self, Value);
end;

function TTabSheet.GetTabIndex: Integer;
var
  I: Integer;
begin
  Result := -1;
  if (FPageControl = nil) or not FTabVisible then
    Exit;
  Result := 0;
  for I := 0 to FPageControl.FPages.Count - 1 do
  begin
    if FPageControl.FPages[I] = Pointer(Self) then
      Exit;
    if TTabSheet(FPageControl.FPages[I]).FTabVisible then
      Inc(Result);
  end;
end;

procedure TTabSheet.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    if FPageControl <> nil then
      FPageControl.UpdateTabs;
  end;
end;

procedure TTabSheet.SetTabVisible(Value: Boolean);
begin
  if FTabVisible <> Value then
  begin
    FTabVisible := Value;
    if FPageControl <> nil then
      FPageControl.PageTabVisibleChanged(Self);
  end;
end;

procedure TTabSheet.WndProc(var Message: TMessage);
begin
  inherited WndProc(Message);
  if (Message.Msg = CM_TEXTCHANGED) and (FPageControl <> nil) then
    FPageControl.UpdateTabs;
end;

{ ---------------------------------------------------------------- TCustomTabControl }

constructor TCustomTabControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  SetBounds(Left, Top, 289, 193);
  TabStop := True;
end;

procedure TCustomTabControl.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := WC_TABCONTROL;
  Params.Style := Params.Style or TCS_TABS or TCS_SINGLELINE or WS_CLIPCHILDREN;
  if FHotTrack then
    Params.Style := Params.Style or TCS_HOTTRACK;
  if FTabWidth > 0 then
    Params.Style := Params.Style or TCS_FIXEDWIDTH;
end;

procedure TCustomTabControl.CreateWnd;
begin
  inherited CreateWnd;
  if FImages <> nil then
    SendMessageW(Handle, TCM_SETIMAGELIST, 0, LPARAM(FImages.Handle));
  SendMessageW(Handle, TCM_SETITEMSIZE, 0, MakeLParam(Word(FTabWidth), Word(TabH)));
end;

function TCustomTabControl.TabH: Integer;
begin
  if FTabHeight > 0 then
    Result := FTabHeight
  else
    Result := Abs(Font.Height) + LayoutMulDiv(9, ControlDPI(Self), 96);
end;

procedure TCustomTabControl.TabSizeChanged;
begin
  if HandleAllocated then
    SendMessageW(Handle, TCM_SETITEMSIZE, 0, MakeLParam(Word(FTabWidth), Word(TabH)));
  Realign;
end;

procedure TCustomTabControl.ChangeScale(M, D: Integer);
begin
  inherited ChangeScale(M, D);
  if M <> D then
  begin
    FTabHeight := LayoutMulDiv(FTabHeight, M, D);
    FTabWidth := LayoutMulDiv(FTabWidth, M, D);
    if HandleAllocated then
      SendMessageW(Handle, TCM_SETITEMSIZE, 0, MakeLParam(Word(FTabWidth), Word(TabH)));
  end;
end;

procedure TCustomTabControl.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FImages) then
    Images := nil;
end;

function TCustomTabControl.CanChange: Boolean;
begin
  Result := True;
  if Assigned(FOnChanging) then
    FOnChanging(Self, Result);
end;

procedure TCustomTabControl.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TCustomTabControl.SetHotTrack(Value: Boolean);
begin
  if FHotTrack <> Value then
  begin
    FHotTrack := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTabControl.SetImages(Value: TCustomImageList);
begin
  if FImages = Value then
    Exit;
  if FImages <> nil then
    FImages.RemoveFreeNotification(Self);
  FImages := Value;
  if FImages <> nil then
    FImages.FreeNotification(Self);
  if HandleAllocated then
    if FImages <> nil then
      SendMessageW(Handle, TCM_SETIMAGELIST, 0, LPARAM(FImages.Handle))
    else
      SendMessageW(Handle, TCM_SETIMAGELIST, 0, 0);
end;

procedure TCustomTabControl.SetMultiLine(Value: Boolean);
begin
  if Value then
    raise ENotSupportedException.Create(SMultiLineTabs);
  FMultiLine := False;
end;

procedure TCustomTabControl.SetTabPosition(Value: TTabPosition);
begin
  if Value <> tpTop then
    raise ENotSupportedException.Create(SMultiLineTabs);
  FTabPosition := tpTop;
end;

procedure TCustomTabControl.SetTabHeight(Value: SmallInt);
begin
  if Value < 0 then
    Value := 0;
  if FTabHeight <> Value then
  begin
    FTabHeight := Value;
    TabSizeChanged;
  end;
end;

procedure TCustomTabControl.SetTabWidth(Value: SmallInt);
begin
  if Value < 0 then
    Value := 0;
  if FTabWidth <> Value then
  begin
    FTabWidth := Value;
    RecreateWnd;                                             { TCS_FIXEDWIDTH }
  end;
end;

{ ---------------------------------------------------------------- TPageControl }

constructor TPageControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FPages := TList.Create;
end;

destructor TPageControl.Destroy;
begin
  inherited Destroy;
  FPages.Free;
  FPages := nil;
end;

function TPageControl.GetPage(Index: Integer): TTabSheet;
begin
  Result := TTabSheet(FPages[Index]);
end;

function TPageControl.GetPageCount: Integer;
begin
  Result := FPages.Count;
end;

function TPageControl.GetActivePageIndex: Integer;
begin
  if FActivePage = nil then
    Result := -1
  else
    Result := FPages.IndexOf(Pointer(FActivePage));
end;

procedure TPageControl.SetActivePageIndex(Value: Integer);
begin
  if (Value >= 0) and (Value < FPages.Count) then
    SetActivePage(TTabSheet(FPages[Value]))
  else
    SetActivePage(nil);
end;

procedure TPageControl.SetActivePage(Value: TTabSheet);
begin
  if csLoading in ComponentState then
  begin
    FActivePage := Value;                                    { applied in Loaded (UIF-771) }
    Exit;
  end;
  if (Value <> nil) and ((Value.FPageControl <> Self) or not Value.FTabVisible) then
    Exit;
  if (Value = nil) and (SuccessorOf(nil) <> nil) then
    Exit;                                                    { UIF-771: a page with a tab stays active }
  ChangeActivePage(Value);
end;

function TPageControl.IsActivePageStored: Boolean;
begin
  Result := (FActivePage <> nil) and (FPages.Count > 0) and (FPages[0] <> Pointer(FActivePage));
end;

function TPageControl.SuccessorOf(Page: TTabSheet): TTabSheet;
var
  I, Index: Integer;
begin
  Result := nil;
  Index := FPages.IndexOf(Pointer(Page));
  for I := Index + 1 to FPages.Count - 1 do
    if TTabSheet(FPages[I]).FTabVisible and (FPages[I] <> Pointer(Page)) then
    begin
      Result := TTabSheet(FPages[I]);
      Exit;
    end;
  for I := Index - 1 downto 0 do
    if TTabSheet(FPages[I]).FTabVisible then
    begin
      Result := TTabSheet(FPages[I]);
      Exit;
    end;
end;

function TPageControl.PageOfTab(Index: Integer): TTabSheet;
var
  I: Integer;
begin
  Result := nil;
  if Index < 0 then
    Exit;
  for I := 0 to FPages.Count - 1 do
    if TTabSheet(FPages[I]).FTabVisible then
    begin
      if Index = 0 then
      begin
        Result := TTabSheet(FPages[I]);
        Exit;
      end;
      Dec(Index);
    end;
end;

procedure TPageControl.InsertPage(Page: TTabSheet);
begin
  FPages.Add(Pointer(Page));
  Page.FPageControl := Self;
  if (FActivePage = nil) and Page.FTabVisible and not (csLoading in ComponentState) then
    ChangeActivePage(Page)
  else if Page <> FActivePage then
    Page.Visible := False;
  UpdateTabs;
  Realign;
end;

procedure TPageControl.RemovePage(Page: TTabSheet);
var
  Index: Integer;
begin
  Index := FPages.IndexOf(Pointer(Page));
  if Index < 0 then
    Exit;
  if Page = FActivePage then
    if csDestroying in ComponentState then
      FActivePage := nil
    else
      ChangeActivePage(SuccessorOf(Page));
  FPages.Delete(Index);
  Page.FPageControl := nil;
  if not (csDestroying in ComponentState) then
    UpdateTabs;
end;

procedure TPageControl.MovePage(Page: TTabSheet; Index: Integer);
var
  Old: Integer;
begin
  Old := FPages.IndexOf(Pointer(Page));
  if Index < 0 then
    Index := 0;
  if Index >= FPages.Count then
    Index := FPages.Count - 1;
  if (Old < 0) or (Old = Index) then
    Exit;
  FPages.Move(Old, Index);
  UpdateTabs;
end;

procedure TPageControl.PageTabVisibleChanged(Page: TTabSheet);
begin
  if not (csLoading in ComponentState) then
    if not Page.FTabVisible and (Page = FActivePage) then
      ChangeActivePage(SuccessorOf(Page))
    else if Page.FTabVisible and (FActivePage = nil) then
      ChangeActivePage(Page);
  UpdateTabs;
end;

procedure TPageControl.ChangeActivePage(Page: TTabSheet);
var
  Old: TTabSheet;
  F: TCustomForm;
begin
  if FActivePage = Page then
    Exit;
  Old := FActivePage;
  if Old <> nil then
  begin
    { UIF-773: the focus inside the old page moves to the page control }
    F := GetParentForm(Self);
    if (F <> nil) and (TFormAccess(F).ActiveControl <> nil) and Old.ContainsControl(TFormAccess(F).ActiveControl)
      and CanFocus then
      SetFocus;
  end;
  FActivePage := Page;
  if (Old <> nil) and not (csDestroying in Old.ComponentState) then
  begin
    Old.DoHide;
    Old.Visible := False;
  end;
  if Page <> nil then
  begin
    Page.Visible := True;
    if Page.HandleAllocated then
      SetWindowPos(Page.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE);
    Page.DoShow;
  end;
  if HandleAllocated then
    if Page <> nil then
      SendMessageW(Handle, TCM_SETCURSEL, WPARAM(Page.TabIndex), 0)
    else
      SendMessageW(Handle, TCM_SETCURSEL, WPARAM(-1), 0);
end;

procedure TPageControl.SelectPage(Page: TTabSheet);
begin
  if (Page = nil) or (Page = FActivePage) or not Page.FTabVisible then
    Exit;
  if not CanChange then
    Exit;
  ChangeActivePage(Page);
  Change;
end;

procedure TPageControl.SelectNextPage(GoForward: Boolean; CheckTabVisible: Boolean);
var
  I, Start, N: Integer;
  P: TTabSheet;
begin
  N := FPages.Count;
  if N = 0 then
    Exit;
  Start := FPages.IndexOf(Pointer(FActivePage));
  if Start < 0 then
    if GoForward then
      Start := N - 1
    else
      Start := 0;
  I := Start;
  repeat
    if GoForward then
      I := (I + 1) mod N
    else
      I := (I + N - 1) mod N;
    P := TTabSheet(FPages[I]);
    if P.FTabVisible then                                    { a page without a tab cannot be active (UIF-771) }
    begin
      SelectPage(P);
      Exit;
    end;
  until I = Start;
end;

function TPageControl.IndexOfTabAt(X, Y: Integer): Integer;
var
  Info: TTCHitTestInfo;
begin
  Result := -1;
  if not HandleAllocated then
    Exit;
  Info.pt := Point(X, Y);
  Info.flags := 0;
  Result := Integer(SendMessageW(Handle, TCM_HITTEST, 0, LPARAM(@Info)));
end;

procedure TPageControl.UpdateTabs;
var
  I, N: Integer;
  Item: TTCItemW;
  P: TTabSheet;
  S: string;
begin
  if not HandleAllocated or (FPages = nil) then
    Exit;
  SendMessageW(Handle, TCM_DELETEALLITEMS, 0, 0);
  N := 0;
  for I := 0 to FPages.Count - 1 do
  begin
    P := TTabSheet(FPages[I]);
    if not P.FTabVisible then
      Continue;
    S := P.Caption;
    FillChar(Item, SizeOf(Item), 0);
    Item.mask := TCIF_TEXT or TCIF_IMAGE;
    Item.pszText := PChar(S);
    Item.iImage := P.FImageIndex;
    SendMessageW(Handle, TCM_INSERTITEMW, WPARAM(N), LPARAM(@Item));
    Inc(N);
  end;
  if FActivePage <> nil then
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(FActivePage.TabIndex), 0)
  else
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(-1), 0);
end;

procedure TPageControl.CreateWnd;
begin
  inherited CreateWnd;
  UpdateTabs;
end;

function TPageControl.PageRect: TRect;
var
  P, G: Integer;
begin
  P := LayoutMulDiv(4, ControlDPI(Self), 96);
  G := LayoutMulDiv(2, ControlDPI(Self), 96);
  Result := Rect(P, TabH + P + G, Width - P, Height - P);
  if Result.Right < Result.Left then
    Result.Right := Result.Left;
  if Result.Bottom < Result.Top then
    Result.Bottom := Result.Top;
end;

procedure TPageControl.AlignControls(AControl: TControl; var Rect: TRect);
var
  I: Integer;
  R: TRect;
begin
  inherited AlignControls(AControl, Rect);
  if FPages = nil then
    Exit;
  R := PageRect;
  for I := 0 to FPages.Count - 1 do
    TTabSheet(FPages[I]).SetBounds(R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top);
end;

procedure TPageControl.Loaded;
var
  P: TTabSheet;
begin
  P := FActivePage;
  FActivePage := nil;
  if (P = nil) or (P.FPageControl <> Self) or not P.FTabVisible then
    P := SuccessorOf(nil);
  ChangeActivePage(P);
  inherited Loaded;
  UpdateTabs;
  Realign;
end;

procedure TPageControl.WndProc(var Message: TMessage);
var
  I: Integer;
  P: TTabSheet;
begin
  case Message.Msg of
    CN_NOTIFY:
      case PNMHdr(Message.LParam)^.code of
        TCN_SELCHANGING:
          if CanChange then
            Message.Result := 0
          else
            Message.Result := 1;
        TCN_SELCHANGE:
          begin
            P := PageOfTab(Integer(SendMessageW(Handle, TCM_GETCURSEL, 0, 0)));
            if (P <> nil) and (P <> FActivePage) then
            begin
              ChangeActivePage(P);
              Change;
            end;
          end;
      else
        inherited WndProc(Message);
      end;
    CM_DIALOGKEY:
      if (Message.WParam = VK_TAB) and (GetKeyState(VK_CONTROL) < 0) then
      begin
        SelectNextPage(GetKeyState(VK_SHIFT) >= 0);
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_DIALOGCHAR:
      begin
        for I := 0 to FPages.Count - 1 do
        begin
          P := TTabSheet(FPages[I]);
          if P.FTabVisible and P.Enabled and IsAccel(Word(Message.WParam), P.Caption) then
          begin
            SelectPage(P);
            Message.Result := 1;
            Exit;
          end;
        end;
        inherited WndProc(Message);
      end;
    CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        TabSizeChanged;
      end;
  else
    inherited WndProc(Message);
  end;
end;

initialization
  RegisterClasses([TPageControl, TTabSheet]);

end.
