{ ComCtrls - common controls (controls-standard.md sections 20 ... 24: page control, status bar, tool bar,
  progress bar, track bar). }
unit ComCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ImgList, CommCtrl, Menus, ActnList, UIConsts;

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

  TStatusPanelBevel = (pbNone, pbLowered, pbRaised);

  TStatusBar = class;

  { controls-standard.md section 21: one part of a status bar }
  TStatusPanel = class(TCollectionItem)
  private
    FAlignment: TAlignment;
    FBevel: TStatusPanelBevel;
    FText: string;
    FWidth: Integer;
    procedure SetAlignment(Value: TAlignment);
    procedure SetBevel(Value: TStatusPanelBevel);
    procedure SetText(const Value: string);
    procedure SetWidth(Value: Integer);
  public
    constructor Create(Collection: TCollection); override;
    procedure Assign(Source: TPersistent); override;
  published
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property Bevel: TStatusPanelBevel read FBevel write SetBevel default pbLowered;
    property Text: string read FText write SetText;
    property Width: Integer read FWidth write SetWidth default 50;
  end;

  { UIF-781: every change re-sends the parts and texts }
  TStatusPanels = class(TOwnedCollection)
  private
    FStatusBar: TStatusBar;
    function GetItem(Index: Integer): TStatusPanel;
    procedure SetItem(Index: Integer; Value: TStatusPanel);
  protected
    procedure Update(Item: TCollectionItem); override;
  public
    constructor Create(StatusBar: TStatusBar);
    function Add: TStatusPanel;
    property Items[Index: Integer]: TStatusPanel read GetItem write SetItem; default;
  end;

  { controls-standard.md section 21 }
  TStatusBar = class(TWinControl)
  private
    FPanels: TStatusPanels;
    FSimplePanel: Boolean;
    FSimpleText: string;
    FSizeGrip: Boolean;
    procedure SetPanels(Value: TStatusPanels);
    procedure SetSimplePanel(Value: Boolean);
    procedure SetSimpleText(const Value: string);
    procedure SetSizeGrip(Value: Boolean);
  protected
    { SBARS_SIZEGRIP only for SizeGrip, Align alBottom and a sizeable parent form }
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { panel widths are scaled }
    procedure ChangeScale(M, D: Integer); override;
    { UIF-780: parts, texts and the simple mode sent to the window }
    procedure UpdatePanels; virtual;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Align default alBottom;
    property Font;
    property Panels: TStatusPanels read FPanels write SetPanels;
    property ParentFont;
    property SimplePanel: Boolean read FSimplePanel write SetSimplePanel default False;
    property SimpleText: string read FSimpleText write SetSimpleText;
    property SizeGrip: Boolean read FSizeGrip write SetSizeGrip default True;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

  TToolButtonStyle = (tbsButton, tbsCheck, tbsDropDown, tbsSeparator, tbsDivider);

  TToolBar = class;

  { actions-menus.md section 1.2: a tool button also links Checked (as Down) and ImageIndex }
  TToolButtonActionLink = class(TControlActionLink)
  protected
    function IsCheckedLinked: Boolean; override;
    function IsImageIndexLinked: Boolean; override;
    procedure SetChecked(Value: Boolean); override;
    procedure SetImageIndex(Value: Integer); override;
  end;

  { controls-standard.md section 22: a flat button drawn by the framework; its bounds come from the bar (UIF-800) }
  TToolButton = class(TGraphicControl)
  private
    FDown: Boolean;
    FGrouped: Boolean;
    FImageIndex: Integer;
    FStyle: TToolButtonStyle;
    FHot: Boolean;                                           { the mouse is over the button }
    FTracking: Boolean;                                      { the left button went down on it }
    FPressed: Boolean;                                       { tracking and the mouse inside: drawn sunken }
    procedure SetDown(Value: Boolean);
    procedure SetGrouped(Value: Boolean);
    procedure SetImageIndex(Value: Integer);
    procedure SetStyle(Value: TToolButtonStyle);
    procedure SetPressed(Value: Boolean);
    function IsWidthStored: Boolean;
    function IsSpacer: Boolean;
    { UIF-802: the adjacent grouped check buttons (this one excluded) are released }
    procedure ReleaseGroup;
    { the parent bar's layout pass }
    procedure RefreshBar;
  protected
    function GetActionLinkClass: TControlActionLinkClass; override;
    procedure ActionChange(Sender: TObject; CheckDefaults: Boolean); override;
    { UIF-802: check buttons toggle (grouped: go down) before the action / OnClick; spacers never click }
    procedure Click; override;
    procedure MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    procedure MouseMove(Shift: TShiftState; X, Y: Integer); override;
    procedure MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    procedure CMMouseEnter(var Message: TMessage); override;
    procedure CMMouseLeave(var Message: TMessage); override;
    procedure CMEnabledChanged(var Message: TMessage); override;
    { UIF-801 }
    procedure Paint; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    property Left stored False;
    property Top stored False;
    property Width stored IsWidthStored;
    property Height stored False;
    property Align stored False;
    property Anchors stored False;
    property AlignWithMargins stored False;
    property Margins stored False;
    property Constraints stored False;
    property Action;
    property Caption;
    property Down: Boolean read FDown write SetDown default False;
    property Grouped: Boolean read FGrouped write SetGrouped default False;
    property ImageIndex: Integer read FImageIndex write SetImageIndex default -1;
    property Style: TToolButtonStyle read FStyle write SetStyle default tbsButton;
    property OnClick;
  end;

  { controls-standard.md section 22: tool buttons and other controls in one row (UIF-800) }
  TToolBar = class(TCustomControl)
  private
    FButtonHeight: Integer;
    FButtonWidth: Integer;
    FImages: TCustomImageList;
    FImageChangeLink: TChangeLink;
    FIndent: Integer;
    FList: Boolean;
    FShowCaptions: Boolean;
    procedure SetButtonHeight(Value: Integer);
    procedure SetButtonWidth(Value: Integer);
    procedure SetImages(Value: TCustomImageList);
    procedure SetIndent(Value: Integer);
    procedure SetList(Value: Boolean);
    procedure SetShowCaptions(Value: Boolean);
    procedure ImageListChange(Sender: TObject);
  protected
    { UIF-800: the flow layout replaces Align / Anchors of the children }
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
    { ButtonWidth, ButtonHeight and Indent are scaled }
    procedure ChangeScale(M, D: Integer); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure Paint; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Align default alTop;
    property ButtonHeight: Integer read FButtonHeight write SetButtonHeight default 22;
    property ButtonWidth: Integer read FButtonWidth write SetButtonWidth default 23;
    property Color default clBtnFace;
    property Font;
    property Images: TCustomImageList read FImages write SetImages;
    property Indent: Integer read FIndent write SetIndent default 0;
    property List: Boolean read FList write SetList default False;
    property ParentColor default True;
    property ParentFont;
    property ShowCaptions: Boolean read FShowCaptions write SetShowCaptions default False;
    property TabOrder;
    property TabStop default False;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

  TProgressBarOrientation = (pbHorizontal, pbVertical);
  TProgressBarStyle = (pbstNormal, pbstMarquee);

  { controls-standard.md section 23 }
  TProgressBar = class(TWinControl)
  private
    FMax: Integer;
    FMin: Integer;
    FOrientation: TProgressBarOrientation;
    FPosition: Integer;
    FSmooth: Boolean;
    FStep: Integer;
    FStyle: TProgressBarStyle;
    procedure SetMax(Value: Integer);
    procedure SetMin(Value: Integer);
    procedure SetOrientation(Value: TProgressBarOrientation);
    procedure SetPosition(Value: Integer);
    procedure SetSmooth(Value: Boolean);
    procedure SetStep(Value: Integer);
    procedure SetStyle(Value: TProgressBarStyle);
    { the range checked (Max < Min raises), the position clamped, both pushed; while loading only the fields }
    procedure SetParams(APosition, AMin, AMax: Integer);
    procedure UpdateState;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { a stored range is never rejected: Max below Min becomes Min, the position is clamped }
    procedure Loaded; override;
  public
    constructor Create(AOwner: TComponent); override;
    { Position + Step, wrapping to Min when it exceeds Max }
    procedure StepIt;
    { Position + Delta, clamped }
    procedure StepBy(Delta: Integer);
  published
    property Max: Integer read FMax write SetMax default 100;
    property Min: Integer read FMin write SetMin default 0;
    property Orientation: TProgressBarOrientation read FOrientation write SetOrientation default pbHorizontal;
    property Position: Integer read FPosition write SetPosition default 0;
    property Smooth: Boolean read FSmooth write SetSmooth default False;
    property Step: Integer read FStep write SetStep default 10;
    property Style: TProgressBarStyle read FStyle write SetStyle default pbstNormal;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  TTrackBarOrientation = (trHorizontal, trVertical);
  TTickMark = (tmBottomRight, tmTopLeft, tmBoth);
  TTickStyle = (tsNone, tsAuto, tsManual);

  { controls-standard.md section 24 }
  TTrackBar = class(TWinControl)
  private
    FFrequency: Integer;
    FLineSize: Integer;
    FMax: Integer;
    FMin: Integer;
    FOrientation: TTrackBarOrientation;
    FPageSize: Integer;
    FPosition: Integer;
    FTickMarks: TTickMark;
    FTickStyle: TTickStyle;
    FOnChange: TNotifyEvent;
    procedure SetFrequency(Value: Integer);
    procedure SetLineSize(Value: Integer);
    procedure SetMax(Value: Integer);
    procedure SetMin(Value: Integer);
    procedure SetOrientation(Value: TTrackBarOrientation);
    procedure SetPageSize(Value: Integer);
    procedure SetPosition(Value: Integer);
    procedure SetTickMarks(Value: TTickMark);
    procedure SetTickStyle(Value: TTickStyle);
    { the range checked (Max < Min raises), the position clamped, both pushed, OnChange when the position changed;
      while loading only the fields }
    procedure SetParams(APosition, AMin, AMax: Integer);
    procedure UpdateRange;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    procedure Loaded; override;
    { UIF-810: CN_HSCROLL / CN_VSCROLL }
    procedure WndProc(var Message: TMessage); override;
    procedure Changed; virtual;
  public
    constructor Create(AOwner: TComponent); override;
  published
    property Frequency: Integer read FFrequency write SetFrequency default 1;
    property LineSize: Integer read FLineSize write SetLineSize default 1;
    property Max: Integer read FMax write SetMax default 10;
    property Min: Integer read FMin write SetMin default 0;
    property Orientation: TTrackBarOrientation read FOrientation write SetOrientation default trHorizontal;
    property PageSize: Integer read FPageSize write SetPageSize default 2;
    property Position: Integer read FPosition write SetPosition default 0;
    property TabOrder;
    property TabStop default True;
    property TickMarks: TTickMark read FTickMarks write SetTickMarks default tmBottomRight;
    property TickStyle: TTickStyle read FTickStyle write SetTickStyle default tsAuto;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
  end;

implementation

type
  TFormAccess = class(TCustomForm);
  TImageListAccess = class(TCustomImageList);

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

{ ---------------------------------------------------------------- TStatusPanel }

constructor TStatusPanel.Create(Collection: TCollection);
begin
  FAlignment := taLeftJustify;                               { before the insertion re-sends the panels }
  FBevel := pbLowered;
  FWidth := 50;
  inherited Create(Collection);
end;

procedure TStatusPanel.Assign(Source: TPersistent);
begin
  if Source is TStatusPanel then
  begin
    FAlignment := TStatusPanel(Source).FAlignment;
    FBevel := TStatusPanel(Source).FBevel;
    FText := TStatusPanel(Source).FText;
    FWidth := TStatusPanel(Source).FWidth;
    Changed(False);
  end
  else
    inherited Assign(Source);
end;

procedure TStatusPanel.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    Changed(False);
  end;
end;

procedure TStatusPanel.SetBevel(Value: TStatusPanelBevel);
begin
  if FBevel <> Value then
  begin
    FBevel := Value;
    Changed(False);
  end;
end;

procedure TStatusPanel.SetText(const Value: string);
begin
  if FText <> Value then
  begin
    FText := Value;
    Changed(False);
  end;
end;

procedure TStatusPanel.SetWidth(Value: Integer);
begin
  if FWidth <> Value then
  begin
    FWidth := Value;
    Changed(True);
  end;
end;

{ ---------------------------------------------------------------- TStatusPanels }

constructor TStatusPanels.Create(StatusBar: TStatusBar);
begin
  inherited Create(StatusBar, TStatusPanel);
  FStatusBar := StatusBar;
end;

function TStatusPanels.Add: TStatusPanel;
begin
  Result := TStatusPanel(inherited Add);
end;

function TStatusPanels.GetItem(Index: Integer): TStatusPanel;
begin
  Result := TStatusPanel(inherited GetItem(Index));
end;

procedure TStatusPanels.SetItem(Index: Integer; Value: TStatusPanel);
begin
  inherited SetItem(Index, Value);
end;

procedure TStatusPanels.Update(Item: TCollectionItem);
begin
  if FStatusBar <> nil then
    FStatusBar.UpdatePanels;
end;

{ ---------------------------------------------------------------- TStatusBar }

constructor TStatusBar.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FPanels := TStatusPanels.Create(Self);
  FSizeGrip := True;
  SetBounds(Left, Top, 300, 19);
  Align := alBottom;
end;

destructor TStatusBar.Destroy;
begin
  inherited Destroy;
  FPanels.Free;
  FPanels := nil;
end;

procedure TStatusBar.CreateParams(var Params: TCreateParams);
var
  F: TCustomForm;
begin
  inherited CreateParams(Params);
  Params.SubclassOf := STATUSCLASSNAME;
  Params.Style := Params.Style or CCS_NORESIZE or CCS_NOPARENTALIGN or CCS_NODIVIDER;
  F := GetParentForm(Self);
  if FSizeGrip and (Align = alBottom) and (F <> nil)
    and (TFormAccess(F).BorderStyle in [bsSizeable, bsSizeToolWin]) then
    Params.Style := Params.Style or SBARS_SIZEGRIP;
end;

procedure TStatusBar.CreateWnd;
begin
  inherited CreateWnd;
  UpdatePanels;
end;

{ UIF-780 }
procedure TStatusBar.UpdatePanels;
const
  Flags: array[TStatusPanelBevel] of WPARAM = (SBT_NOBORDERS, 0, SBT_POPOUT);
  Prefixes: array[TAlignment] of string = ('', #9#9, #9);
var
  Edges: array of Integer;
  I, N, X: Integer;
  P: TStatusPanel;
  S: string;
begin
  if not HandleAllocated or (FPanels = nil) then
    Exit;
  N := FPanels.Count;
  if N = 0 then
  begin
    SetLength(Edges, 1);
    Edges[0] := -1;
    S := '';
    SendMessageW(Handle, SB_SETPARTS, 1, LPARAM(@Edges[0]));
    SendMessageW(Handle, SB_SETTEXTW, 0, LPARAM(PChar(S)));
  end
  else
  begin
    SetLength(Edges, N);
    X := 0;
    for I := 0 to N - 1 do
    begin
      X := X + FPanels[I].Width;
      Edges[I] := X;
    end;
    Edges[N - 1] := -1;
    SendMessageW(Handle, SB_SETPARTS, WPARAM(N), LPARAM(@Edges[0]));
    for I := 0 to N - 1 do
    begin
      P := FPanels[I];
      S := Prefixes[P.Alignment] + P.Text;
      SendMessageW(Handle, SB_SETTEXTW, WPARAM(I) or Flags[P.Bevel], LPARAM(PChar(S)));
    end;
  end;
  SendMessageW(Handle, SB_SIMPLE, WPARAM(Ord(FSimplePanel)), 0);
  if FSimplePanel then
    SendMessageW(Handle, SB_SETTEXTW, SB_SIMPLEID, LPARAM(PChar(FSimpleText)));
end;

procedure TStatusBar.ChangeScale(M, D: Integer);
var
  I: Integer;
begin
  inherited ChangeScale(M, D);
  if M = D then
    Exit;
  FPanels.BeginUpdate;
  try
    for I := 0 to FPanels.Count - 1 do
      FPanels[I].Width := LayoutMulDiv(FPanels[I].Width, M, D);
  finally
    FPanels.EndUpdate;
  end;
end;

procedure TStatusBar.SetPanels(Value: TStatusPanels);
begin
  FPanels.Assign(Value);
end;

procedure TStatusBar.SetSimplePanel(Value: Boolean);
begin
  if FSimplePanel <> Value then
  begin
    FSimplePanel := Value;
    UpdatePanels;
  end;
end;

procedure TStatusBar.SetSimpleText(const Value: string);
begin
  if FSimpleText <> Value then
  begin
    FSimpleText := Value;
    if FSimplePanel and HandleAllocated then
      SendMessageW(Handle, SB_SETTEXTW, SB_SIMPLEID, LPARAM(PChar(FSimpleText)));
  end;
end;

procedure TStatusBar.SetSizeGrip(Value: Boolean);
begin
  if FSizeGrip <> Value then
  begin
    FSizeGrip := Value;
    RecreateWnd;
  end;
end;

{ ---------------------------------------------------------------- TToolButtonActionLink }

function TToolButtonActionLink.IsCheckedLinked: Boolean;
begin
  Result := (FClient is TToolButton) and (TToolButton(FClient).Down = FChecked);
end;

function TToolButtonActionLink.IsImageIndexLinked: Boolean;
begin
  Result := (FClient is TToolButton) and (TToolButton(FClient).ImageIndex = FImageIndex);
end;

procedure TToolButtonActionLink.SetChecked(Value: Boolean);
begin
  if FClient is TToolButton then
    TToolButton(FClient).Down := Value;
end;

procedure TToolButtonActionLink.SetImageIndex(Value: Integer);
begin
  if FClient is TToolButton then
    TToolButton(FClient).ImageIndex := Value;
end;

{ ---------------------------------------------------------------- TToolButton }

constructor TToolButton.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 23, 22);
  FImageIndex := -1;
  FStyle := tbsButton;
end;

function TToolButton.IsSpacer: Boolean;
begin
  Result := FStyle in [tbsSeparator, tbsDivider];
end;

function TToolButton.IsWidthStored: Boolean;
begin
  Result := IsSpacer;
end;

function TToolButton.GetActionLinkClass: TControlActionLinkClass;
begin
  Result := TToolButtonActionLink;
end;

procedure TToolButton.ActionChange(Sender: TObject; CheckDefaults: Boolean);
begin
  inherited ActionChange(Sender, CheckDefaults);
  if Sender is TCustomAction then
  begin
    if not CheckDefaults or not FDown then
      Down := TCustomAction(Sender).Checked;
    if not CheckDefaults or (FImageIndex = -1) then
      ImageIndex := TCustomAction(Sender).ImageIndex;
  end;
end;

procedure TToolButton.RefreshBar;
begin
  if Parent <> nil then
    Parent.Realign;
end;

procedure TToolButton.ReleaseGroup;
var
  I, Index: Integer;
  C: TControl;
begin
  if Parent = nil then
    Exit;
  Index := -1;
  for I := 0 to Parent.ControlCount - 1 do
    if Parent.Controls[I] = Self then
      Index := I;
  for I := Index - 1 downto 0 do
  begin
    C := Parent.Controls[I];
    if not ((C is TToolButton) and TToolButton(C).FGrouped and (TToolButton(C).FStyle = tbsCheck)) then
      Break;
    if TToolButton(C).FDown then
    begin
      TToolButton(C).FDown := False;
      C.Invalidate;
    end;
  end;
  for I := Index + 1 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    if not ((C is TToolButton) and TToolButton(C).FGrouped and (TToolButton(C).FStyle = tbsCheck)) then
      Break;
    if TToolButton(C).FDown then
    begin
      TToolButton(C).FDown := False;
      C.Invalidate;
    end;
  end;
end;

procedure TToolButton.SetDown(Value: Boolean);
begin
  if FDown = Value then
    Exit;
  FDown := Value;
  if FDown and FGrouped and (FStyle = tbsCheck) and not (csLoading in ComponentState) then
    ReleaseGroup;
  Invalidate;
end;

procedure TToolButton.SetGrouped(Value: Boolean);
begin
  FGrouped := Value;
end;

procedure TToolButton.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    Invalidate;
  end;
end;

{ a button that becomes a separator or divider outside loading gets the spacer width 8 }
procedure TToolButton.SetStyle(Value: TToolButtonStyle);
var
  WasSpacer: Boolean;
begin
  if FStyle = Value then
    Exit;
  WasSpacer := IsSpacer;
  FStyle := Value;
  if IsSpacer and not WasSpacer and not (csLoading in ComponentState) then
    Width := 8;
  Invalidate;
  RefreshBar;
end;

procedure TToolButton.SetPressed(Value: Boolean);
begin
  if FPressed <> Value then
  begin
    FPressed := Value;
    Invalidate;
  end;
end;

procedure TToolButton.Click;
begin
  if IsSpacer then
    Exit;
  if FStyle = tbsCheck then
    if FGrouped then
      Down := True                                           { a down grouped button cannot be released }
    else
      Down := not FDown;
  inherited Click;
end;

procedure TToolButton.MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  inherited MouseDown(Button, Shift, X, Y);
  if (Button = mbLeft) and Enabled and not IsSpacer then
  begin
    FTracking := True;
    SetPressed(True);
  end;
end;

{ while the mouse is captured the button keeps receiving moves: hot and pressed follow the position }
procedure TToolButton.MouseMove(Shift: TShiftState; X, Y: Integer);
var
  Inside: Boolean;
begin
  inherited MouseMove(Shift, X, Y);
  Inside := (X >= 0) and (Y >= 0) and (X < Width) and (Y < Height);
  if FTracking then
    SetPressed(Inside);
  if FHot <> (Inside and Enabled and not IsSpacer) then
  begin
    FHot := not FHot;
    Invalidate;
  end;
end;

procedure TToolButton.MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  inherited MouseUp(Button, Shift, X, Y);
  if Button = mbLeft then
  begin
    FTracking := False;
    SetPressed(False);
  end;
end;

procedure TToolButton.CMMouseEnter(var Message: TMessage);
begin
  inherited CMMouseEnter(Message);
  if Enabled and not IsSpacer and not FHot then
  begin
    FHot := True;
    Invalidate;
  end;
end;

procedure TToolButton.CMMouseLeave(var Message: TMessage);
begin
  inherited CMMouseLeave(Message);
  if FHot then
  begin
    FHot := False;
    Invalidate;
  end;
end;

procedure TToolButton.CMEnabledChanged(var Message: TMessage);
begin
  if not Enabled then
  begin
    FHot := False;
    FTracking := False;
    FPressed := False;
  end;
  inherited CMEnabledChanged(Message);
end;

{ UIF-801 }
procedure TToolButton.Paint;
var
  Bar: TToolBar;
  Images: TCustomImageList;
  R, T: TRect;
  M, X, Y, IW, IH, TH: Integer;
  HasImage, HasText: Boolean;
  S: string;
begin
  R := Rect(0, 0, Width, Height);
  if FStyle = tbsSeparator then
    Exit;
  if FStyle = tbsDivider then
  begin
    X := Width div 2 - 1;
    T := Rect(X, 0, X + 2, Height);
    DrawEdge(Canvas.Handle, T, EDGE_ETCHED, BF_LEFT);
    Exit;
  end;
  Bar := nil;
  if Parent is TToolBar then
    Bar := TToolBar(Parent);
  if FPressed or FDown then
    DrawEdge(Canvas.Handle, R, BDR_SUNKENOUTER, BF_RECT)
  else if FHot then
  begin
    Canvas.Brush.Style := bsSolid;
    Canvas.Brush.Color := clBtnShadow;
    Canvas.FrameRect(R);
    Canvas.Brush.Color := clBtnHighlight;
    Canvas.FillRect(Rect(1, 1, Width - 1, Height - 1));
  end;
  Images := nil;
  if Bar <> nil then
    Images := Bar.Images;
  HasImage := (Images <> nil) and (FImageIndex >= 0) and (FImageIndex < Images.Count);
  if HasImage then
  begin
    IW := TImageListAccess(Images).Width;
    IH := TImageListAccess(Images).Height;
  end
  else
  begin
    IW := 0;
    IH := 0;
  end;
  S := Caption;
  HasText := (Bar <> nil) and Bar.ShowCaptions and (S <> '');
  M := LayoutMulDiv(3, ControlDPI(Self), 96);
  Canvas.Font := Font;
  if not Enabled then
    Canvas.Font.Color := clGrayText;
  if not HasText then
  begin
    if HasImage then
      Images.Draw(Canvas, (Width - IW) div 2, (Height - IH) div 2, FImageIndex, Enabled);
    Exit;
  end;
  Canvas.Brush.Style := bsClear;
  SetBkMode(Canvas.Handle, Windows.TRANSPARENT);
  if Bar.List then
  begin
    X := M;
    if HasImage then
    begin
      Images.Draw(Canvas, M, (Height - IH) div 2, FImageIndex, Enabled);
      X := M + IW + M;
    end;
    T := Rect(X, 0, Width - M, Height);
    Canvas.TextRect(T, S, [tfSingleLine, tfLeft, tfVerticalCenter, tfEndEllipsis]);
  end
  else
  begin
    TH := Canvas.TextHeight('Wg');
    Y := (Height - IH - TH) div 2;
    if HasImage then
      Images.Draw(Canvas, (Width - IW) div 2, Y, FImageIndex, Enabled);
    T := Rect(M, Y + IH, Width - M, Y + IH + TH);
    Canvas.TextRect(T, S, [tfSingleLine, tfCenter, tfTop, tfEndEllipsis]);
  end;
end;

{ ---------------------------------------------------------------- TToolBar }

constructor TToolBar.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  SetBounds(Left, Top, 300, 29);
  Align := alTop;
  Color := clBtnFace;
  ParentColor := True;
  FButtonHeight := 22;
  FButtonWidth := 23;
  FImageChangeLink := TChangeLink.Create;
  FImageChangeLink.OnChange := ImageListChange;
end;

destructor TToolBar.Destroy;
begin
  inherited Destroy;
  FImageChangeLink.Free;
  FImageChangeLink := nil;
end;

{ UIF-800 }
procedure TToolBar.AlignControls(AControl: TControl; var Rect: TRect);
var
  I, X, Y, W: Integer;
  C: TControl;
begin
  X := FIndent;
  Y := LayoutMulDiv(2, ControlDPI(Self), 96);
  for I := 0 to ControlCount - 1 do
  begin
    C := Controls[I];
    if not C.Visible then
      Continue;
    if C is TToolButton then
    begin
      if TToolButton(C).IsSpacer then
        W := C.Width
      else
        W := FButtonWidth;
      C.SetBounds(X, Y, W, FButtonHeight);
    end
    else
    begin
      W := C.Width;
      C.SetBounds(X, Y, W, C.Height);
    end;
    X := X + W;
  end;
end;

procedure TToolBar.ChangeScale(M, D: Integer);
begin
  if M <> D then
  begin
    FButtonHeight := LayoutMulDiv(FButtonHeight, M, D);
    FButtonWidth := LayoutMulDiv(FButtonWidth, M, D);
    FIndent := LayoutMulDiv(FIndent, M, D);
  end;
  inherited ChangeScale(M, D);
end;

procedure TToolBar.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FImages) then
    Images := nil;
end;

procedure TToolBar.Paint;
begin
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := Color;
  Canvas.FillRect(Rect(0, 0, Width, Height));
end;

procedure TToolBar.ImageListChange(Sender: TObject);
begin
  Invalidate;
end;

procedure TToolBar.SetImages(Value: TCustomImageList);
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
  Invalidate;
end;

procedure TToolBar.SetButtonHeight(Value: Integer);
begin
  if FButtonHeight <> Value then
  begin
    FButtonHeight := Value;
    Realign;
    Invalidate;
  end;
end;

procedure TToolBar.SetButtonWidth(Value: Integer);
begin
  if FButtonWidth <> Value then
  begin
    FButtonWidth := Value;
    Realign;
    Invalidate;
  end;
end;

procedure TToolBar.SetIndent(Value: Integer);
begin
  if FIndent <> Value then
  begin
    FIndent := Value;
    Realign;
    Invalidate;
  end;
end;

procedure TToolBar.SetList(Value: Boolean);
begin
  if FList <> Value then
  begin
    FList := Value;
    Invalidate;
  end;
end;

procedure TToolBar.SetShowCaptions(Value: Boolean);
begin
  if FShowCaptions <> Value then
  begin
    FShowCaptions := Value;
    Invalidate;
  end;
end;

{ ---------------------------------------------------------------- TProgressBar }

constructor TProgressBar.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 150, 17);
  FMax := 100;
  FStep := 10;
end;

procedure TProgressBar.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := PROGRESS_CLASS;
  if FSmooth then
    Params.Style := Params.Style or PBS_SMOOTH;
  if FOrientation = pbVertical then
    Params.Style := Params.Style or PBS_VERTICAL;
  if FStyle = pbstMarquee then
    Params.Style := Params.Style or PBS_MARQUEE;
end;

procedure TProgressBar.CreateWnd;
begin
  inherited CreateWnd;
  UpdateState;
  SendMessageW(Handle, PBM_SETSTEP, WPARAM(FStep), 0);
  if FStyle = pbstMarquee then
    SendMessageW(Handle, PBM_SETMARQUEE, 1, 30);
end;

procedure TProgressBar.UpdateState;
begin
  if not HandleAllocated then
    Exit;
  SendMessageW(Handle, PBM_SETRANGE32, WPARAM(FMin), LPARAM(FMax));
  SendMessageW(Handle, PBM_SETPOS, WPARAM(FPosition), 0);
end;

procedure TProgressBar.SetParams(APosition, AMin, AMax: Integer);
begin
  if csLoading in ComponentState then
  begin
    FMin := AMin;
    FMax := AMax;
    FPosition := APosition;
    Exit;
  end;
  if AMax < AMin then
    raise EInvalidOperation.Create(SProgressRange);
  FMin := AMin;
  FMax := AMax;
  if APosition > FMax then
    APosition := FMax;
  if APosition < FMin then
    APosition := FMin;
  FPosition := APosition;
  UpdateState;
end;

procedure TProgressBar.Loaded;
begin
  inherited Loaded;
  if FMax < FMin then
    FMax := FMin;
  SetParams(FPosition, FMin, FMax);
end;

procedure TProgressBar.SetMax(Value: Integer);
begin
  SetParams(FPosition, FMin, Value);
end;

procedure TProgressBar.SetMin(Value: Integer);
begin
  SetParams(FPosition, Value, FMax);
end;

procedure TProgressBar.SetPosition(Value: Integer);
begin
  SetParams(Value, FMin, FMax);
end;

procedure TProgressBar.SetStep(Value: Integer);
begin
  FStep := Value;
  if HandleAllocated then
    SendMessageW(Handle, PBM_SETSTEP, WPARAM(FStep), 0);
end;

procedure TProgressBar.StepIt;
var
  P: Integer;
begin
  P := FPosition + FStep;
  if P > FMax then
    P := FMin;
  SetPosition(P);
end;

procedure TProgressBar.StepBy(Delta: Integer);
begin
  SetPosition(FPosition + Delta);
end;

procedure TProgressBar.SetOrientation(Value: TProgressBarOrientation);
begin
  if FOrientation <> Value then
  begin
    FOrientation := Value;
    RecreateWnd;
  end;
end;

procedure TProgressBar.SetSmooth(Value: Boolean);
begin
  if FSmooth <> Value then
  begin
    FSmooth := Value;
    RecreateWnd;
  end;
end;

procedure TProgressBar.SetStyle(Value: TProgressBarStyle);
begin
  if FStyle <> Value then
  begin
    FStyle := Value;
    RecreateWnd;
  end;
end;

{ ---------------------------------------------------------------- TTrackBar }

constructor TTrackBar.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 150, 45);
  FFrequency := 1;
  FLineSize := 1;
  FMax := 10;
  FPageSize := 2;
  FTickStyle := tsAuto;
  TabStop := True;
end;

procedure TTrackBar.CreateParams(var Params: TCreateParams);
const
  Marks: array[TTickMark] of DWORD = (TBS_BOTTOM, TBS_TOP, TBS_BOTH);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := TRACKBAR_CLASS;
  if FOrientation = trVertical then
    Params.Style := Params.Style or TBS_VERT
  else
    Params.Style := Params.Style or TBS_HORZ;
  case FTickStyle of
    tsNone: Params.Style := Params.Style or TBS_NOTICKS;
    tsAuto: Params.Style := Params.Style or TBS_AUTOTICKS;
  end;
  Params.Style := Params.Style or Marks[FTickMarks];
end;

procedure TTrackBar.CreateWnd;
begin
  inherited CreateWnd;
  SendMessageW(Handle, TBM_SETLINESIZE, 0, LPARAM(FLineSize));
  SendMessageW(Handle, TBM_SETPAGESIZE, 0, LPARAM(FPageSize));
  UpdateRange;
  SendMessageW(Handle, TBM_SETTICFREQ, WPARAM(FFrequency), 0);
end;

procedure TTrackBar.UpdateRange;
begin
  if not HandleAllocated then
    Exit;
  SendMessageW(Handle, TBM_SETRANGEMIN, 0, LPARAM(FMin));
  SendMessageW(Handle, TBM_SETRANGEMAX, 0, LPARAM(FMax));
  SendMessageW(Handle, TBM_SETPOS, 1, LPARAM(FPosition));
end;

procedure TTrackBar.SetParams(APosition, AMin, AMax: Integer);
var
  Old: Integer;
begin
  if csLoading in ComponentState then
  begin
    FMin := AMin;
    FMax := AMax;
    FPosition := APosition;
    Exit;
  end;
  if AMax < AMin then
    raise EInvalidOperation.Create(STrackBarRange);
  Old := FPosition;
  FMin := AMin;
  FMax := AMax;
  if APosition > FMax then
    APosition := FMax;
  if APosition < FMin then
    APosition := FMin;
  FPosition := APosition;
  UpdateRange;
  if FPosition <> Old then
    Changed;
end;

{ a stored range is never rejected: Max below Min becomes Min, the position is clamped, no event }
procedure TTrackBar.Loaded;
begin
  inherited Loaded;
  if FMax < FMin then
    FMax := FMin;
  if FPosition > FMax then
    FPosition := FMax;
  if FPosition < FMin then
    FPosition := FMin;
  UpdateRange;
end;

procedure TTrackBar.Changed;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TTrackBar.WndProc(var Message: TMessage);
var
  P: Integer;
begin
  case Message.Msg of
    CN_HSCROLL, CN_VSCROLL:
      begin
        P := Integer(SendMessageW(Handle, TBM_GETPOS, 0, 0));
        if P <> FPosition then
        begin
          FPosition := P;
          Changed;
        end;
        Message.Result := 0;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TTrackBar.SetMax(Value: Integer);
begin
  SetParams(FPosition, FMin, Value);
end;

procedure TTrackBar.SetMin(Value: Integer);
begin
  SetParams(FPosition, Value, FMax);
end;

procedure TTrackBar.SetPosition(Value: Integer);
begin
  SetParams(Value, FMin, FMax);
end;

procedure TTrackBar.SetFrequency(Value: Integer);
begin
  if FFrequency <> Value then
  begin
    FFrequency := Value;
    if HandleAllocated then
      SendMessageW(Handle, TBM_SETTICFREQ, WPARAM(FFrequency), 0);
  end;
end;

procedure TTrackBar.SetLineSize(Value: Integer);
begin
  if FLineSize <> Value then
  begin
    FLineSize := Value;
    if HandleAllocated then
      SendMessageW(Handle, TBM_SETLINESIZE, 0, LPARAM(FLineSize));
  end;
end;

procedure TTrackBar.SetPageSize(Value: Integer);
begin
  if FPageSize <> Value then
  begin
    FPageSize := Value;
    if HandleAllocated then
      SendMessageW(Handle, TBM_SETPAGESIZE, 0, LPARAM(FPageSize));
  end;
end;

procedure TTrackBar.SetOrientation(Value: TTrackBarOrientation);
begin
  if FOrientation <> Value then
  begin
    FOrientation := Value;
    RecreateWnd;
  end;
end;

procedure TTrackBar.SetTickMarks(Value: TTickMark);
begin
  if FTickMarks <> Value then
  begin
    FTickMarks := Value;
    RecreateWnd;
  end;
end;

procedure TTrackBar.SetTickStyle(Value: TTickStyle);
begin
  if FTickStyle <> Value then
  begin
    FTickStyle := Value;
    RecreateWnd;
  end;
end;

initialization
  RegisterClasses([TPageControl, TTabSheet, TStatusBar, TToolBar, TToolButton, TProgressBar, TTrackBar]);

end.
