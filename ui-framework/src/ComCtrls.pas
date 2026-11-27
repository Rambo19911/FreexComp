// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ComCtrls - common controls (controls-standard.md sections 20 ... 24: page control, status bar, tool bar,
  progress bar, track bar) and the rich text editor (rich-edit.md). }
unit ComCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ImgList, CommCtrl, Menus, ActnList,
  UIConsts;

type
  TTabPosition = (tpTop, tpBottom, tpLeft, tpRight);
  { UIF-1490 }
  TTabStyle = (tsTabs, tsButtons, tsFlatButtons);
  TTabChangingEvent = procedure(Sender: TObject; var AllowChange: Boolean) of object;

  TCustomTabControl = class;
  TDrawTabEvent = procedure(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean) of object;
  TTabGetImageEvent = procedure(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer) of object;

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

  { the native tab strip (SysTabControl32) with the properties shared by tab controls; the geometry of
    phase8/tab-control.md section 4 (tab lengths, rows, display rectangle) is computed, never queried (UIF-1503) }
  TCustomTabControl = class(TWinControl)
  private
    FHotTrack: Boolean;
    FImages: TCustomImageList;
    FImageChangeLink: TChangeLink;
    FMultiLine: Boolean;
    FOwnerDraw: Boolean;
    FRaggedRight: Boolean;
    FScrollOpposite: Boolean;
    FStyle: TTabStyle;
    FTabHeight: SmallInt;
    FTabIndex: Integer;
    FTabPosition: TTabPosition;
    FTabWidth: SmallInt;
    FCanvas: TCanvas;
    FOnChange: TNotifyEvent;
    FOnChanging: TTabChangingEvent;
    FOnDrawTab: TDrawTabEvent;
    FOnGetImageIndex: TTabGetImageEvent;
    function GetDisplayRect: TRect;
    procedure ImageListChange(Sender: TObject);
    { UIF-1493: a property that changes the window styles: recreate and realign }
    procedure StyleChanged;
    procedure SetHotTrack(Value: Boolean);
    procedure SetImages(Value: TCustomImageList);
    procedure SetMultiLine(Value: Boolean);
    procedure SetOwnerDraw(Value: Boolean);
    procedure SetRaggedRight(Value: Boolean);
    procedure SetScrollOpposite(Value: Boolean);
    procedure SetStyle(Value: TTabStyle);
    procedure SetTabHeight(Value: SmallInt);
    procedure SetTabPosition(Value: TTabPosition);
    procedure SetTabWidth(Value: SmallInt);
    { UIF-1513: the parent's WM_DRAWITEM (ODT_TAB) reflected }
    procedure CNDrawItem(var Message: TWMDrawItem);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { a window DC Canvas obtained on demand goes before the window (graphics.md UIF-570) }
    procedure DestroyWnd; override;
    procedure WndProc(var Message: TMessage); override;
    { UIF-1514: OnDrawTab, else the default content; Canvas is bound to the item DC }
    procedure DrawTab(TabIndex: Integer; const Rect: TRect; Active: Boolean); virtual;
    procedure ChangeScale(M, D: Integer); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { the tab thickness TabH of UIF-1497 }
    function TabH: Integer;
    { TCM_SETITEMSIZE (TabWidth, TabH) and the realign of a changed tab height }
    procedure TabSizeChanged; virtual;
    function CanChange: Boolean; virtual;
    procedure Change; virtual;
    { the visible tabs (section 2): their number and the raw caption of visible tab Index }
    function VisibleTabCount: Integer; virtual;
    function TabCaption(Index: Integer): string; virtual;
    { the native tab items rebuilt (texts, images, selection) }
    procedure UpdateTabItems; virtual;
    function GetTabIndex: Integer; virtual;
    procedure SetTabIndex(Value: Integer); virtual;
    { UIF-1512 }
    function GetImageIndex(TabIndex: Integer): Integer; virtual;
    { UIF-1510 }
    procedure SelectNextTab(GoForward: Boolean);
    { section 2: MultiLine, or tabs on the left or right }
    function EffectiveMultiLine: Boolean;
    { UIF-1492: TCS_SCROLLOPPOSITE is sent }
    function ScrollOppositeInEffect: Boolean;
    { UIF-1498: the length of visible tab Index }
    function TabLength(Index: Integer): Integer;
    { UIF-1499: the row of visible tab Index (-1 for none) and the number of rows }
    function RowOfTab(Index: Integer; out Rows: Integer): Integer;
    { UIF-1500: K, the number of rows on the side opposite the strip }
    function OppositeRows: Integer;
    property HotTrack: Boolean read FHotTrack write SetHotTrack default False;
    property Images: TCustomImageList read FImages write SetImages;
    property MultiLine: Boolean read FMultiLine write SetMultiLine default False;
    property OwnerDraw: Boolean read FOwnerDraw write SetOwnerDraw default False;
    property RaggedRight: Boolean read FRaggedRight write SetRaggedRight default False;
    property ScrollOpposite: Boolean read FScrollOpposite write SetScrollOpposite default False;
    property Style: TTabStyle read FStyle write SetStyle default tsTabs;
    property TabHeight: SmallInt read FTabHeight write SetTabHeight default 0;
    property TabIndex: Integer read GetTabIndex write SetTabIndex default -1;
    property TabPosition: TTabPosition read FTabPosition write SetTabPosition default tpTop;
    property TabWidth: SmallInt read FTabWidth write SetTabWidth default 0;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnChanging: TTabChangingEvent read FOnChanging write FOnChanging;
    property OnDrawTab: TDrawTabEvent read FOnDrawTab write FOnDrawTab;
    property OnGetImageIndex: TTabGetImageEvent read FOnGetImageIndex write FOnGetImageIndex;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-1499: R }
    function RowCount: Integer;
    { TCM_GETITEMRECT of visible tab Index; Rect(0, 0, 0, 0) without a handle or for a bad index }
    function TabRect(Index: Integer): TRect;
    { TCM_HITTEST; -1 without a handle }
    function IndexOfTabAt(X, Y: Integer): Integer;
    { UIF-1501: the display rectangle in client coordinates }
    property DisplayRect: TRect read GetDisplayRect;
    { UIF-1513, UIF-1515: bound to the item DC only inside DrawTab / OnDrawTab }
    property Canvas: TCanvas read FCanvas;
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
    { the visible tabs changed: native tabs rebuilt and the pages realigned (UIF-1502) }
    procedure TabsChanged;
  protected
    function VisibleTabCount: Integer; override;
    function TabCaption(Index: Integer): string; override;
    procedure UpdateTabItems; override;
    function GetTabIndex: Integer; override;
    procedure SetTabIndex(Value: Integer); override;
    function GetImageIndex(TabIndex: Integer): Integer; override;
    procedure CreateWnd; override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
    procedure Loaded; override;
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure SelectNextPage(GoForward: Boolean; CheckTabVisible: Boolean = True);
    { UIF-1502: the page rectangle = DisplayRect }
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
    property OwnerDraw;
    property ParentFont;
    property RaggedRight;
    property ScrollOpposite;
    property Style;
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
    property OnDrawTab;
    property OnEnter;
    property OnExit;
    property OnGetImageIndex;
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

  { phase8/tab-control.md section 5: the plain tab strip; Tabs held by the control (UIF-1506), the selection is
    TabIndex (UIF-1507), children are not switched by it (UIF-1511) }
  TTabControl = class(TCustomTabControl)
  private
    FTabs: TStrings;
    FTabsUpdating: Boolean;
    procedure SetTabs(Value: TStrings);
    { the native item Index (text and image, UIF-1512) inserted or re-set }
    procedure SendItem(Index: Integer; Insert: Boolean);
    { UIF-1506: after a Tabs change: the selection restored and the control realigned }
    procedure TabsEdited;
    { UIF-1509, UIF-1510: a selection by the user: OnChanging (veto), TabIndex, OnChange }
    procedure SelectTab(Index: Integer);
  protected
    function VisibleTabCount: Integer; override;
    function TabCaption(Index: Integer): string; override;
    procedure UpdateTabItems; override;
    procedure SetTabIndex(Value: Integer); override;
    procedure CreateWnd; override;
    procedure Loaded; override;
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-1511: the align rect is DisplayRect }
    procedure AdjustClientRect(var Rect: TRect); override;
  published
    property Font;
    property HotTrack;
    property Images;
    property MultiLine;
    property OwnerDraw;
    property ParentFont;
    property RaggedRight;
    property ScrollOpposite;
    property Style;
    property TabHeight;
    property TabIndex;
    property TabOrder;
    property TabPosition;
    property Tabs: TStrings read FTabs write SetTabs;
    property TabStop default True;
    property TabWidth;
    property OnChange;
    property OnChanging;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnDrawTab;
    property OnEnter;
    property OnExit;
    property OnGetImageIndex;
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
    { UIF-910 (c): an action can show a hidden tool button again }
    function UpdatesActionWhenHidden: Boolean; override;
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
    { accessibility.md UIF-1430, UIF-1431: the click of a tool button posted by its Invoke or Toggle pattern }
    procedure WndProc(var Message: TMessage); override;
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


  TCustomListView = class;

  TListItems = class;
  TDisplayCode = (drBounds, drIcon, drLabel, drSelectBounds);

  { controls-standard.md section 25: an item of a list view (run time only, not streamed); the single source of
    the native item's text and image (UIF-820) }
  TListItem = class(TPersistent)
  private
    FOwner: TListItems;
    FCaption: string;
    FSubItems: TStrings;
    FImageIndex: Integer;
    FChecked: Boolean;
    FSelected: Boolean;
    FFocused: Boolean;
    FData: Pointer;
    function GetIndex: Integer;
    function GetListView: TCustomListView;
    procedure SetCaption(const Value: string);
    procedure SetChecked(Value: Boolean);
    procedure SetFocused(Value: Boolean);
    procedure SetImageIndex(Value: Integer);
    procedure SetSelected(Value: Boolean);
    procedure SetSubItems(Value: TStrings);
    procedure SubItemsChanged(Sender: TObject);
    { the native state bit(s) set, the field kept (the notification fires the events) }
    procedure SetState(Mask, State: UINT);
  public
    constructor Create(AOwner: TListItems);
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    procedure Delete;
    procedure MakeVisible(PartialOK: Boolean);
    function DisplayRect(Code: TDisplayCode): TRect;
    property Caption: string read FCaption write SetCaption;
    property Checked: Boolean read FChecked write SetChecked;
    property Data: Pointer read FData write FData;
    property Focused: Boolean read FFocused write SetFocused;
    property ImageIndex: Integer read FImageIndex write SetImageIndex;
    property Index: Integer read GetIndex;
    property ListView: TCustomListView read GetListView;
    property Owner: TListItems read FOwner;
    property Selected: Boolean read FSelected write SetSelected;
    property SubItems: TStrings read FSubItems write SetSubItems;
  end;

  { ListView.Items }
  TListItems = class(TPersistent)
  private
    FOwner: TCustomListView;
    FList: TList;
    FUpdateCount: Integer;
    function GetCount: Integer;
    function GetItem(Index: Integer): TListItem;
    { the native item for Item at Index (callback text and image, lParam = the item, its state) }
    procedure InsertNative(Item: TListItem; Index: Integer);
  public
    constructor Create(AOwner: TCustomListView);
    destructor Destroy; override;
    function Add: TListItem;
    function Insert(Index: Integer): TListItem;
    procedure Delete(Index: Integer);
    procedure Clear;
    function IndexOf(Value: TListItem): Integer;
    { WM_SETREDRAW, nested }
    procedure BeginUpdate;
    procedure EndUpdate;
    property Count: Integer read GetCount;
    property Item[Index: Integer]: TListItem read GetItem; default;
    property Owner: TCustomListView read FOwner;
  end;

  TSearchDirection = (sdLeft, sdRight, sdAbove, sdBelow, sdAll);
  TItemState = (isNone, isCut, isDropHilited, isFocused, isSelected, isActivating);
  TItemStates = set of TItemState;

  TItemChange = (ctText, ctImage, ctState);
  TViewStyle = (vsIcon, vsSmallIcon, vsList, vsReport);

  TListColumn = class(TCollectionItem)
  private
    FAlignment: TAlignment;
    FCaption: string;
    FWidth: Integer;
    procedure SetAlignment(Value: TAlignment);
    procedure SetCaption(const Value: string);
    procedure SetWidth(Value: Integer);
  protected
    function GetDisplayName: string; override;
  public
    constructor Create(Collection: TCollection); override;
    procedure Assign(Source: TPersistent); override;
  published
    { column 0 is always shown left-aligned (native rule); the property keeps its value }
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property Caption: string read FCaption write SetCaption;
    { follows a width change made in the window (the user dragging a divider) }
    property Width: Integer read FWidth write SetWidth default 50;
  end;

  { every change rebuilds the native columns }
  TListColumns = class(TOwnedCollection)
  private
    FListView: TCustomListView;
    function GetItem(Index: Integer): TListColumn;
    procedure SetItem(Index: Integer; Value: TListColumn);
  protected
    procedure Update(Item: TCollectionItem); override;
  public
    constructor Create(ListView: TCustomListView);
    function Add: TListColumn;
    property Items[Index: Integer]: TListColumn read GetItem write SetItem; default;
  end;

  TLVChangeEvent = procedure(Sender: TObject; Item: TListItem; Change: TItemChange) of object;
  TLVColumnClickEvent = procedure(Sender: TObject; Column: TListColumn) of object;
  TLVEditedEvent = procedure(Sender: TObject; Item: TListItem; var S: string) of object;
  TLVCheckedItemEvent = procedure(Sender: TObject; Item: TListItem) of object;
  TLVSelectItemEvent = procedure(Sender: TObject; Item: TListItem; Selected: Boolean) of object;

  { controls-standard.md section 25 }
  TCustomListView = class(TWinControl)
  private
    FBorderStyle: TBorderStyle;
    FCheckboxes: Boolean;
    FColumnClick: Boolean;
    FColumns: TListColumns;
    FGridLines: Boolean;
    FHideSelection: Boolean;
    FLargeImages: TCustomImageList;
    FMultiSelect: Boolean;
    FReadOnly: Boolean;
    FRowSelect: Boolean;
    FShowColumnHeaders: Boolean;
    FSmallImages: TCustomImageList;
    FViewStyle: TViewStyle;
    FLargeChangeLink: TChangeLink;
    FSmallChangeLink: TChangeLink;
    FUpdatingColumns: Boolean;
    FItems: TListItems;
    { set while the window is created, recreated or destroyed and while items are inserted: no events }
    FNoEvents: Boolean;
    FOnChange: TLVChangeEvent;
    FOnColumnClick: TLVColumnClickEvent;
    FOnEdited: TLVEditedEvent;
    FOnItemChecked: TLVCheckedItemEvent;
    FOnSelectItem: TLVSelectItemEvent;
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetCheckboxes(Value: Boolean);
    procedure SetColumnClick(Value: Boolean);
    procedure SetColumns(Value: TListColumns);
    procedure SetGridLines(Value: Boolean);
    procedure SetHideSelection(Value: Boolean);
    procedure SetLargeImages(Value: TCustomImageList);
    procedure SetMultiSelect(Value: Boolean);
    procedure SetReadOnly(Value: Boolean);
    procedure SetRowSelect(Value: Boolean);
    procedure SetShowColumnHeaders(Value: Boolean);
    procedure SetSmallImages(Value: TCustomImageList);
    procedure SetViewStyle(Value: TViewStyle);
    procedure ImageListChange(Sender: TObject);
    procedure SetImageList(var Field: TCustomImageList; Value: TCustomImageList; Link: TChangeLink);
    procedure UpdateExtendedStyles;
    procedure UpdateImageLists;
    procedure UpdateColors;
    procedure ItemChanged(Item: TListItem; Change: TItemChange);
    procedure SyncItemState(Index: Integer);
    procedure GetDispInfo(var Info: TLVDispInfoW);
    function GetSelected: TListItem;
    procedure SetSelected(Value: TListItem);
    function GetItemIndex: Integer;
    procedure SetItemIndex(Value: Integer);
    function GetItemFocused: TListItem;
    procedure SetItemFocused(Value: TListItem);
    function GetSelCount: Integer;
    function GetTopItem: TListItem;
    function GetVisibleRowCount: Integer;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-821: every item inserted with its state, no events }
    procedure CreateWnd; override;
    procedure DestroyWnd; override;
    procedure ChangeScale(M, D: Integer); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure CMColorChanged(var Message: TMessage); override;
    procedure CMFontChanged(var Message: TMessage); override;
    { CN_NOTIFY (LVN_COLUMNCLICK, LVN_GETDISPINFOW, LVN_ITEMCHANGED, LVN_ENDLABELEDITW); WM_NOTIFY of the header
      (a native width change) }
    procedure WndProc(var Message: TMessage); override;
    { the native columns rebuilt from Columns }
    procedure UpdateColumns; virtual;
    procedure DoColumnClick(Column: TListColumn); virtual;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property Checkboxes: Boolean read FCheckboxes write SetCheckboxes default False;
    property ColumnClick: Boolean read FColumnClick write SetColumnClick default True;
    property Columns: TListColumns read FColumns write SetColumns;
    property GridLines: Boolean read FGridLines write SetGridLines default False;
    property HideSelection: Boolean read FHideSelection write SetHideSelection default True;
    property LargeImages: TCustomImageList read FLargeImages write SetLargeImages;
    property MultiSelect: Boolean read FMultiSelect write SetMultiSelect default False;
    property ReadOnly: Boolean read FReadOnly write SetReadOnly default False;
    property RowSelect: Boolean read FRowSelect write SetRowSelect default False;
    property ShowColumnHeaders: Boolean read FShowColumnHeaders write SetShowColumnHeaders default True;
    property SmallImages: TCustomImageList read FSmallImages write SetSmallImages;
    property ViewStyle: TViewStyle read FViewStyle write SetViewStyle default vsIcon;
    property OnChange: TLVChangeEvent read FOnChange write FOnChange;
    property OnColumnClick: TLVColumnClickEvent read FOnColumnClick write FOnColumnClick;
    property OnEdited: TLVEditedEvent read FOnEdited write FOnEdited;
    property OnItemChecked: TLVCheckedItemEvent read FOnItemChecked write FOnItemChecked;
    property OnSelectItem: TLVSelectItemEvent read FOnSelectItem write FOnSelectItem;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function GetItemAt(X, Y: Integer): TListItem;
    function GetNextItem(StartItem: TListItem; Direction: TSearchDirection; States: TItemStates): TListItem;
    procedure ClearSelection;
    procedure SelectAll;
    procedure Clear;
    property ItemFocused: TListItem read GetItemFocused write SetItemFocused;
    property ItemIndex: Integer read GetItemIndex write SetItemIndex;
    property Items: TListItems read FItems;
    property SelCount: Integer read GetSelCount;
    { the first selected item or nil }
    property Selected: TListItem read GetSelected write SetSelected;
    property TopItem: TListItem read GetTopItem;
    property VisibleRowCount: Integer read GetVisibleRowCount;
  end;

  TListView = class(TCustomListView)
  published
    property BorderStyle;
    property Checkboxes;
    property Color default clWindow;
    property ColumnClick;
    property Columns;
    property Font;
    property GridLines;
    property HideSelection;
    property LargeImages;
    property MultiSelect;
    property ParentColor default False;
    property ParentFont;
    property ReadOnly;
    property RowSelect;
    property ShowColumnHeaders;
    property SmallImages;
    property TabOrder;
    property TabStop default True;
    property ViewStyle;
    property OnChange;
    property OnClick;
    property OnColumnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEdited;
    property OnEnter;
    property OnExit;
    property OnItemChecked;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnSelectItem;
  end;


  TCustomTreeView = class;
  TTreeNodes = class;

  { controls-standard.md section 26: a node of a tree view (run time only, not streamed); the objects form their
    own tree (UIF-831) and are the single source of the native item's text and images (UIF-830) }
  TTreeNode = class(TPersistent)
  private
    FOwner: TTreeNodes;
    FParent: TTreeNode;
    FChildren: TList;
    FText: string;
    FImageIndex: Integer;
    FSelectedIndex: Integer;
    FData: Pointer;
    FItemId: HTREEITEM;
    FExpanded: Boolean;
    function GetCount: Integer;
    function GetItem(Index: Integer): TTreeNode;
    function GetLevel: Integer;
    function GetIndex: Integer;
    function GetAbsoluteIndex: Integer;
    function GetHasChildren: Boolean;
    function GetExpanded: Boolean;
    procedure SetExpanded(Value: Boolean);
    function GetSelected: Boolean;
    procedure SetSelected(Value: Boolean);
    function GetTreeView: TCustomTreeView;
    procedure SetText(const Value: string);
    procedure SetImageIndex(Value: Integer);
    procedure SetSelectedIndex(Value: Integer);
    { the siblings list this node is in (the parent's children or the roots) }
    function Siblings: TList;
    { the native item re-reads text and images }
    procedure Refresh;
    { cChildren of the native item from the object }
    procedure UpdateChildren;
  public
    constructor Create(AOwner: TTreeNodes);
    { UIF-833: the children first (last first, each deepest first), then the native item }
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    procedure Delete;
    procedure DeleteChildren;
    procedure Expand(Recurse: Boolean);
    procedure Collapse(Recurse: Boolean);
    procedure MakeVisible;
    { starts a label edit in the window (OnEditing may refuse it) }
    procedure EditText;
    function GetFirstChild: TTreeNode;
    function GetLastChild: TTreeNode;
    function GetNextSibling: TTreeNode;
    function GetPrevSibling: TTreeNode;
    { pre-order }
    function GetNext: TTreeNode;
    function GetPrev: TTreeNode;
    function DisplayRect(TextOnly: Boolean): TRect;
    property AbsoluteIndex: Integer read GetAbsoluteIndex;
    property Count: Integer read GetCount;
    property Data: Pointer read FData write FData;
    property Expanded: Boolean read GetExpanded write SetExpanded;
    { the caret item of the tree (the same as Selected) }
    property Focused: Boolean read GetSelected write SetSelected;
    property HasChildren: Boolean read GetHasChildren;
    property ImageIndex: Integer read FImageIndex write SetImageIndex;
    property Index: Integer read GetIndex;
    property Item[Index: Integer]: TTreeNode read GetItem; default;
    property ItemId: HTREEITEM read FItemId;
    property Level: Integer read GetLevel;
    property Owner: TTreeNodes read FOwner;
    property Parent: TTreeNode read FParent;
    property Selected: Boolean read GetSelected write SetSelected;
    { -1: the ImageIndex is shown for the selected node }
    property SelectedIndex: Integer read FSelectedIndex write SetSelectedIndex;
    property Text: string read FText write SetText;
    property TreeView: TCustomTreeView read GetTreeView;
  end;

  { TreeView.Items }
  TTreeNodes = class(TPersistent)
  private
    FOwner: TCustomTreeView;
    FRoots: TList;
    FCount: Integer;
    FUpdateCount: Integer;
    function GetItem(Index: Integer): TTreeNode;
    { a new node at Index of Parent's children (nil: the roots) }
    function InternalAdd(Parent: TTreeNode; Index: Integer; const S: string): TTreeNode;
    { the native item of Node (after its previous sibling) and of its descendants }
    procedure InsertNative(Node: TTreeNode);
  public
    constructor Create(AOwner: TCustomTreeView);
    destructor Destroy; override;
    { after the last sibling of Sibling; nil: the last root }
    function Add(Sibling: TTreeNode; const S: string): TTreeNode;
    function AddFirst(Sibling: TTreeNode; const S: string): TTreeNode;
    { the last child of Parent; nil: the last root }
    function AddChild(Parent: TTreeNode; const S: string): TTreeNode;
    function AddChildFirst(Parent: TTreeNode; const S: string): TTreeNode;
    { before Sibling; nil: the last root }
    function Insert(Sibling: TTreeNode; const S: string): TTreeNode;
    procedure Delete(Node: TTreeNode);
    procedure Clear;
    function GetFirstNode: TTreeNode;
    { WM_SETREDRAW, nested }
    procedure BeginUpdate;
    procedure EndUpdate;
    { all nodes }
    property Count: Integer read FCount;
    { pre-order }
    property Item[Index: Integer]: TTreeNode read GetItem; default;
    property Owner: TCustomTreeView read FOwner;
  end;

  TTVChangedEvent = procedure(Sender: TObject; Node: TTreeNode) of object;
  TTVChangingEvent = procedure(Sender: TObject; Node: TTreeNode; var AllowChange: Boolean) of object;
  TTVExpandedEvent = procedure(Sender: TObject; Node: TTreeNode) of object;
  TTVExpandingEvent = procedure(Sender: TObject; Node: TTreeNode; var AllowExpansion: Boolean) of object;
  TTVCollapsingEvent = procedure(Sender: TObject; Node: TTreeNode; var AllowCollapse: Boolean) of object;
  TTVEditedEvent = procedure(Sender: TObject; Node: TTreeNode; var S: string) of object;
  TTVEditingEvent = procedure(Sender: TObject; Node: TTreeNode; var AllowEdit: Boolean) of object;

  { controls-standard.md section 26 }
  TCustomTreeView = class(TWinControl)
  private
    FBorderStyle: TBorderStyle;
    FHideSelection: Boolean;
    FImages: TCustomImageList;
    FImageChangeLink: TChangeLink;
    FIndent: Integer;
    FItems: TTreeNodes;
    FReadOnly: Boolean;
    FRowSelect: Boolean;
    FShowButtons: Boolean;
    FShowLines: Boolean;
    FShowRoot: Boolean;
    { the selected node while there is no window (saved by DestroyWnd) }
    FSelectedNode: TTreeNode;
    { set while the window is created, recreated or destroyed and while the control is destroyed: no events }
    FNoEvents: Boolean;
    { set during the node's own TVM_EXPAND: a first expansion is also notified by the window, it is not reported
      twice }
    FExpanding: Boolean;
    FOnChange: TTVChangedEvent;
    FOnChanging: TTVChangingEvent;
    FOnCollapsed: TTVExpandedEvent;
    FOnCollapsing: TTVCollapsingEvent;
    FOnEdited: TTVEditedEvent;
    FOnEditing: TTVEditingEvent;
    FOnExpanded: TTVExpandedEvent;
    FOnExpanding: TTVExpandingEvent;
    { OnExpanding / OnCollapsing: whether the change may happen }
    function CanExpandChange(Node: TTreeNode; Expand: Boolean): Boolean;
    { OnExpanded / OnCollapsed }
    procedure ExpandChanged(Node: TTreeNode; Expand: Boolean);
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetHideSelection(Value: Boolean);
    procedure SetImages(Value: TCustomImageList);
    procedure SetIndent(Value: Integer);
    procedure SetReadOnly(Value: Boolean);
    procedure SetRowSelect(Value: Boolean);
    procedure SetShowButtons(Value: Boolean);
    procedure SetShowLines(Value: Boolean);
    procedure SetShowRoot(Value: Boolean);
    function GetSelected: TTreeNode;
    procedure SetSelected(Value: TTreeNode);
    function GetTopItem: TTreeNode;
    procedure ImageListChange(Sender: TObject);
    procedure UpdateColors;
    procedure UpdateImages;
    function NodeOf(Item: HTREEITEM): TTreeNode;
    procedure GetDispInfo(var Info: TTVDispInfoW);
    procedure SaveState(Node: TTreeNode);
    procedure ClearIds(Node: TTreeNode);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-831: the nodes inserted in pre-order, then the expanded state and the selection restored }
    procedure CreateWnd; override;
    { UIF-831: the expanded state and the selection saved into the objects }
    procedure DestroyWnd; override;
    procedure ChangeScale(M, D: Integer); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure CMColorChanged(var Message: TMessage); override;
    procedure CMFontChanged(var Message: TMessage); override;
    { CN_NOTIFY: TVN_GETDISPINFOW; UIF-832: the selection, expansion and label edit notifications }
    procedure WndProc(var Message: TMessage); override;
    property OnChange: TTVChangedEvent read FOnChange write FOnChange;
    property OnChanging: TTVChangingEvent read FOnChanging write FOnChanging;
    property OnCollapsed: TTVExpandedEvent read FOnCollapsed write FOnCollapsed;
    property OnCollapsing: TTVCollapsingEvent read FOnCollapsing write FOnCollapsing;
    property OnEdited: TTVEditedEvent read FOnEdited write FOnEdited;
    property OnEditing: TTVEditingEvent read FOnEditing write FOnEditing;
    property OnExpanded: TTVExpandedEvent read FOnExpanded write FOnExpanded;
    property OnExpanding: TTVExpandingEvent read FOnExpanding write FOnExpanding;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property HideSelection: Boolean read FHideSelection write SetHideSelection default True;
    property Images: TCustomImageList read FImages write SetImages;
    property Indent: Integer read FIndent write SetIndent default 19;
    property ReadOnly: Boolean read FReadOnly write SetReadOnly default False;
    property RowSelect: Boolean read FRowSelect write SetRowSelect default False;
    property ShowButtons: Boolean read FShowButtons write SetShowButtons default True;
    property ShowLines: Boolean read FShowLines write SetShowLines default True;
    property ShowRoot: Boolean read FShowRoot write SetShowRoot default True;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function GetNodeAt(X, Y: Integer): TTreeNode;
    procedure FullExpand;
    procedure FullCollapse;
    { a label edit is in progress }
    function IsEditing: Boolean;
    property Items: TTreeNodes read FItems;
    { settable; nil clears the selection }
    property Selected: TTreeNode read GetSelected write SetSelected;
    property TopItem: TTreeNode read GetTopItem;
  end;

  TTreeView = class(TCustomTreeView)
  published
    property BorderStyle;
    property Color default clWindow;
    property Font;
    property HideSelection;
    property Images;
    property Indent;
    property ParentColor default False;
    property ParentFont;
    property ReadOnly;
    property RowSelect;
    property ShowButtons;
    property ShowLines;
    property ShowRoot;
    property TabOrder;
    property TabStop default True;
    property OnChange;
    property OnChanging;
    property OnClick;
    property OnCollapsed;
    property OnCollapsing;
    property OnContextPopup;
    property OnDblClick;
    property OnEdited;
    property OnEditing;
    property OnEnter;
    property OnExit;
    property OnExpanded;
    property OnExpanding;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  { ---------------------------------------------------------------- rich text editor (rich-edit.md) }

  TCustomRichEdit = class;

  TAttributeType = (atSelected, atDefaultText);
  TConsistentAttribute = (caBold, caColor, caFace, caItalic, caSize, caStrikeOut, caUnderline, caProtected);
  TConsistentAttributes = set of TConsistentAttribute;

  { rich-edit.md section 3 (UIF-1320 ... 1323): the character format of the selection or the default one }
  TTextAttributes = class(TPersistent)
  private
    FRichEdit: TCustomRichEdit;
    FType: TAttributeType;
    function GetColor: TColor;
    procedure SetColor(Value: TColor);
    function GetConsistentAttributes: TConsistentAttributes;
    function GetName: string;
    procedure SetName(const Value: string);
    function GetProtected: Boolean;
    procedure SetProtected(Value: Boolean);
    function GetSize: Integer;
    procedure SetSize(Value: Integer);
    function GetStyle: TFontStyles;
    procedure SetStyle(Value: TFontStyles);
  protected
    procedure AssignTo(Dest: TPersistent); override;         { a TFont }
  public
    constructor Create(AOwner: TCustomRichEdit; AttributeType: TAttributeType);
    procedure Assign(Source: TPersistent); override;         { TTextAttributes or TFont }
    property Color: TColor read GetColor write SetColor;
    property ConsistentAttributes: TConsistentAttributes read GetConsistentAttributes;
    property Name: string read GetName write SetName;
    property Protected: Boolean read GetProtected write SetProtected;
    property Size: Integer read GetSize write SetSize;       { points }
    property Style: TFontStyles read GetStyle write SetStyle;
  end;

  TNumberingStyle = (nsNone, nsBullet);

  { rich-edit.md section 4 (UIF-1324): the format of the paragraphs touched by the selection; lengths in points }
  TParaAttributes = class(TPersistent)
  private
    FRichEdit: TCustomRichEdit;
    function GetAlignment: TAlignment;
    procedure SetAlignment(Value: TAlignment);
    function GetFirstIndent: Integer;
    procedure SetFirstIndent(Value: Integer);
    function GetLeftIndent: Integer;
    procedure SetLeftIndent(Value: Integer);
    function GetNumbering: TNumberingStyle;
    procedure SetNumbering(Value: TNumberingStyle);
    function GetRightIndent: Integer;
    procedure SetRightIndent(Value: Integer);
    function GetTab(Index: Byte): Integer;
    procedure SetTab(Index: Byte; Value: Integer);
    function GetTabCount: Integer;
    procedure SetTabCount(Value: Integer);
  public
    constructor Create(AOwner: TCustomRichEdit);
    procedure Assign(Source: TPersistent); override;         { TParaAttributes }
    property Alignment: TAlignment read GetAlignment write SetAlignment;
    property FirstIndent: Integer read GetFirstIndent write SetFirstIndent;
    property LeftIndent: Integer read GetLeftIndent write SetLeftIndent;
    property Numbering: TNumberingStyle read GetNumbering write SetNumbering;
    property RightIndent: Integer read GetRightIndent write SetRightIndent;
    property Tab[Index: Byte]: Integer read GetTab write SetTab;
    property TabCount: Integer read GetTabCount write SetTabCount;
  end;

  TSearchType = (stWholeWord, stMatchCase);
  TSearchTypes = set of TSearchType;
  TRichEditLinkClickEvent = procedure(Sender: TObject; const URL: string) of object;
  TRichEditProtectChangeEvent = procedure(Sender: TObject; StartPos, EndPos: Integer;
    var AllowChange: Boolean) of object;

  { UIF-1306: the paragraphs of a rich edit as TStrings; UIF-1325 ... 1330: loading and saving }
  TRichEditStrings = class(TStrings)
  private
    FRichEdit: TCustomRichEdit;
    FList: TStringList;                                      { the lines while the control has no handle }
    FSyncing: Boolean;
    procedure Synchronize;
    procedure Replace(AStart, ALength: Integer; const S: string);
  protected
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    procedure Put(Index: Integer; const S: string); override;
    function GetTextStr: string; override;
    procedure SetTextStr(const Value: string); override;
  public
    destructor Destroy; override;
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    procedure Insert(Index: Integer; const S: string); override;
    { PlainText decides: RTF through the engine's stream messages, or the inherited text path; the TEncoding
      overloads are inherited and always text }
    procedure LoadFromFile(const FileName: string); overload; override;
    procedure LoadFromStream(Stream: TStream); overload; override;
    procedure SaveToFile(const FileName: string); overload; override;
    procedure SaveToStream(Stream: TStream); overload; override;
  end;

  { rich-edit.md section 2: a multi-line editor on the rich edit engine of the system (window class RICHEDIT50W) }
  TCustomRichEdit = class(TCustomMemo)
  private
    FAutoURLDetect: Boolean;
    FPlainText: Boolean;
    FZoom: Integer;
    FSavedContent: TMemoryStream;                            { UIF-1304: the RTF of a window that went }
    FUpdating: Integer;                                      { > 0: the control changes its own content }
    FInTextChanged: Boolean;
    FSelAttributes: TTextAttributes;
    FDefAttributes: TTextAttributes;
    FParagraph: TParaAttributes;
    FOnLinkClick: TRichEditLinkClickEvent;
    FOnProtectChange: TRichEditProtectChangeEvent;
    FOnSelectionChange: TNotifyEvent;
    procedure SetAutoURLDetect(Value: Boolean);
    function GetZoom: Integer;
    procedure SetZoom(Value: Integer);
    procedure SetSelAttributes(Value: TTextAttributes);
    procedure SetDefAttributes(Value: TTextAttributes);
    procedure SetParagraph(Value: TParaAttributes);
    function GetCanRedo: Boolean;
    function EngineText(UseCRLF: Boolean): string;
    procedure SetEngineText(const S: string);
    procedure ApplyFont;
    procedure ApplyZoom;
    procedure TransferContent(Msg: Cardinal; Format: NativeUInt; Stream: TStream; const FailMessage: string);
    function LoadRichText(Stream: TStream): Boolean;
    procedure SaveRichText(Stream: TStream);
    function ProtectChange(Msg: Cardinal; StartPos, EndPos: Integer): Boolean;
    procedure LinkClick(Msg: Cardinal; StartPos, EndPos: Integer);
  protected
    { UIF-1301: the name the engine library is loaded by (empty = Msftedit.dll); a seam for the load failure test }
    class var FLibraryName: string;
  protected
    function CreateLines: TStrings; override;
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-1300, UIF-1303, UIF-1304: library, then limit, event mask, colour, default format, URL detection, zoom,
      content, selection and Modified }
    procedure CreateWnd; override;
    { UIF-1304: content (RTF unless PlainText) and zoom saved }
    procedure DestroyWnd; override;
    { CN_NOTIFY: EN_SELCHANGE, EN_PROTECTED, EN_LINK; colour and font changes; WM_SETFONT never reaches the engine }
    procedure WndProc(var Message: TMessage); override;
    procedure Change; override;
    { UIF-1333: called exactly when OnSelectionChange fires; the base fires it }
    procedure SelectionChange; virtual;
    { UIF-1305: engine character positions }
    function GetSelStart: Integer; override;
    function GetSelLength: Integer; override;
    function GetSelText: string; override;
    procedure SetSelection(AStart, ALength: Integer); override;
    procedure ApplyHideSelection; override;
    procedure ApplyMaxLength; override;
    function GetCaretPos: TPoint; override;
    procedure SetCaretPos(const Value: TPoint); override;
    property AutoURLDetect: Boolean read FAutoURLDetect write SetAutoURLDetect default True;
    property PlainText: Boolean read FPlainText write FPlainText default False;
    property Zoom: Integer read GetZoom write SetZoom default 100;
    property OnLinkClick: TRichEditLinkClickEvent read FOnLinkClick write FOnLinkClick;
    property OnProtectChange: TRichEditProtectChangeEvent read FOnProtectChange write FOnProtectChange;
    property OnSelectionChange: TNotifyEvent read FOnSelectionChange write FOnSelectionChange;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-1335: the position of the first match in StartPos .. StartPos + Length, -1 when there is none }
    function FindText(const SearchStr: string; StartPos, Length: Integer; Options: TSearchTypes): Integer;
    procedure Redo;
    procedure ClearUndo;
    property CanRedo: Boolean read GetCanRedo;
    property DefAttributes: TTextAttributes read FDefAttributes write SetDefAttributes;
    property Paragraph: TParaAttributes read FParagraph write SetParagraph;
    property SelAttributes: TTextAttributes read FSelAttributes write SetSelAttributes;
  end;

  TRichEdit = class(TCustomRichEdit)
  published
    property Alignment;
    property AutoURLDetect;
    property BorderStyle;
    property Color default clWindow;
    property Font;
    property HideSelection;
    property Lines;
    property MaxLength;
    property ParentColor default False;
    property ParentFont;
    property PlainText;
    property ReadOnly;
    property ScrollBars;
    property TabOrder;
    property TabStop default True;
    property WantReturns;
    property WantTabs;
    property WordWrap;
    property Zoom;
    property OnChange;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnLinkClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnProtectChange;
    property OnSelectionChange;
  end;

implementation

uses
  RTLConsts, RichEdit, UIAutomation, Accessibility, UxTheme;

type
  TFormAccess = class(TCustomForm);
  TImageListAccess = class(TCustomImageList);

  { accessibility.md UIF-1411, UIF-1417, UIF-1419, UIF-1421: the rules of the classes of this unit }
  TTabSheetAutomationProvider = class(TWindowAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
    class function DefaultControlType(Control: TControl): Integer; override;
  end;

  TToolBarAutomationProvider = class(TWindowAutomationProvider)
  public
    class function DefaultControlType(Control: TControl): Integer; override;
  end;

  { UIF-1430, UIF-1431: Invoke (tbsButton, tbsDropDown) and Toggle (tbsCheck) }
  TToolButtonAutomationProvider = class(TGraphicAutomationProvider, IInvokeProvider, IToggleProvider)
  private
    { posts the click to the tool bar (Kind: ClickByInvoke or ClickByToggle) }
    function PostClick(Kind: Integer): HResult;
  public
    class function OwnText(Control: TControl): string; override;
    class function DefaultControlType(Control: TControl): Integer; override;
    class function ElementKind(Control: TControl): TAccessibleElementKind; override;
    function GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult; override; stdcall;
    { IInvokeProvider }
    function Invoke: HResult; stdcall;
    { IToggleProvider }
    function Toggle: HResult; stdcall;
    function get_ToggleState(out pRetVal: Integer): HResult; stdcall;
  end;

const
  { UIF-1430: posted to a tool bar by a tool button's provider: WParam = the provider's Number, LParam = the kind }
  CM_TOOLBUTTONCLICK = CM_BASE + 91;
  ClickByInvoke = 0;
  ClickByToggle = 1;

class function TTabSheetAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
end;

class function TTabSheetAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_PaneControlTypeId;
end;

class function TToolBarAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_ToolBarControlTypeId;
end;

{ the caption; a button without one is named by its hint }
class function TToolButtonAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
  if Result = '' then
    Result := GetShortHint(Control.Hint);
end;

class function TToolButtonAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  if TToolButton(Control).Style in [tbsSeparator, tbsDivider] then
    Result := UIA_SeparatorControlTypeId
  else
    Result := UIA_ButtonControlTypeId;
end;

class function TToolButtonAutomationProvider.ElementKind(Control: TControl): TAccessibleElementKind;
begin
  if TToolButton(Control).Style in [tbsSeparator, tbsDivider] then
    Result := ekNone
  else
    Result := ekContent;
end;

function TToolButtonAutomationProvider.GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult;
var
  Inv: IInvokeProvider;
  Tog: IToggleProvider;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  case TToolButton(Control).Style of
    tbsButton, tbsDropDown:
      if patternId = UIA_InvokePatternId then
      begin
        Inv := Self;
        pRetVal := Inv;
      end;
    tbsCheck:
      if patternId = UIA_TogglePatternId then
      begin
        Tog := Self;
        pRetVal := Tog;
      end;
  end;
end;

{ the click is posted, so that a click that opens a modal dialog does not block the client (UIF-1430) }
function TToolButtonAutomationProvider.PostClick(Kind: Integer): HResult;
var
  Bar: TWinControl;
begin
  if not Alive(Result) then
    Exit;
  if not AccessibleEnabledOf(Control) then
  begin
    Result := UIA_E_ELEMENTNOTENABLED;
    Exit;
  end;
  Bar := Control.Parent;
  if not (Bar is TToolBar) or not Bar.HandleAllocated
    or not PostMessageW(Bar.Handle, CM_TOOLBUTTONCLICK, WPARAM(Number), LPARAM(Kind)) then
    Result := UIA_E_INVALIDOPERATION;
end;

function TToolButtonAutomationProvider.Invoke: HResult;
begin
  Result := PostClick(ClickByInvoke);
end;

function TToolButtonAutomationProvider.Toggle: HResult;
begin
  Result := PostClick(ClickByToggle);
end;

function TToolButtonAutomationProvider.get_ToggleState(out pRetVal: Integer): HResult;
begin
  pRetVal := ToggleState_Off;
  if Alive(Result) and TToolButton(Control).Down then
    pRetVal := ToggleState_On;
end;

{ UIF-1431: ToggleToggleState property-changed of a check button whose Down changed (UIF-1440 inside) }
procedure ToolButtonDownChanged(Button: TToolButton);
begin
  if (Button.Style = tbsCheck) and (Button.AutomationObject <> nil) then
    if Button.Down then
      RaiseIntPropertyChanged(Button, UIA_ToggleToggleStatePropertyId, ToggleState_Off, ToggleState_On)
    else
      RaiseIntPropertyChanged(Button, UIA_ToggleToggleStatePropertyId, ToggleState_On, ToggleState_Off);
end;

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
      FPageControl.TabsChanged;
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
    FPageControl.TabsChanged;
end;

{ ---------------------------------------------------------------- TCustomTabControl }

constructor TCustomTabControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  SetBounds(Left, Top, 289, 193);
  TabStop := True;
  FTabIndex := -1;
  FImageChangeLink := TChangeLink.Create;
  FImageChangeLink.OnChange := ImageListChange;
  FCanvas := TControlCanvas.Create;
  TControlCanvas(FCanvas).Control := Self;
end;

destructor TCustomTabControl.Destroy;
begin
  inherited Destroy;
  FImageChangeLink.Free;
  FImageChangeLink := nil;
  FCanvas.Free;
  FCanvas := nil;
end;

{ UIF-1492 }
procedure TCustomTabControl.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := WC_TABCONTROL;
  Params.Style := Params.Style or WS_CLIPCHILDREN;
  case FStyle of
    tsButtons:
      Params.Style := Params.Style or TCS_BUTTONS;
    tsFlatButtons:
      Params.Style := Params.Style or TCS_BUTTONS or TCS_FLATBUTTONS;
  else
    Params.Style := Params.Style or TCS_TABS;
  end;
  if EffectiveMultiLine then
    Params.Style := Params.Style or TCS_MULTILINE
  else
    Params.Style := Params.Style or TCS_SINGLELINE;
  case FTabPosition of
    tpBottom:
      Params.Style := Params.Style or TCS_BOTTOM;
    tpLeft:
      Params.Style := Params.Style or TCS_VERTICAL;
    tpRight:
      Params.Style := Params.Style or TCS_VERTICAL or TCS_RIGHT;
  end;
  if ScrollOppositeInEffect then
    Params.Style := Params.Style or TCS_SCROLLOPPOSITE;
  if FRaggedRight then
    Params.Style := Params.Style or TCS_RAGGEDRIGHT;
  if FHotTrack then
    Params.Style := Params.Style or TCS_HOTTRACK;
  if FTabWidth > 0 then
    Params.Style := Params.Style or TCS_FIXEDWIDTH;
  if FOwnerDraw then
    Params.Style := Params.Style or TCS_OWNERDRAWFIXED;
end;

procedure TCustomTabControl.DestroyWnd;
begin
  TControlCanvas(FCanvas).FreeHandle;
  inherited DestroyWnd;
end;

procedure TCustomTabControl.WndProc(var Message: TMessage);
begin
  if Message.Msg = CN_DRAWITEM then
    CNDrawItem(TWMDrawItem(Message))
  else
    inherited WndProc(Message);
end;

{ UIF-1513: the item DC's state saved, Canvas bound to it for DrawTab, both undone in a finally; answered 1 }
procedure TCustomTabControl.CNDrawItem(var Message: TWMDrawItem);
var
  D: PDrawItemStruct;
  Saved: Integer;
begin
  D := Message.DrawItemStruct;
  if (D = nil) or (D^.CtlType <> ODT_TAB) then
  begin
    inherited WndProc(TMessage(Message));
    Exit;
  end;
  Saved := SaveDC(D^.hDC);
  TControlCanvas(FCanvas).FreeHandle;
  FCanvas.Handle := D^.hDC;
  try
    FCanvas.Font := Font;
    FCanvas.Brush.Color := Color;
    DrawTab(Integer(D^.itemID), D^.rcItem, (D^.itemState and ODS_SELECTED) <> 0);
  finally
    FCanvas.Handle := 0;
    RestoreDC(D^.hDC, Saved);
  end;
  Message.Result := 1;
end;

{ UIF-1514: OnDrawTab when assigned; otherwise Rect filled with the brush, the tab's image (UIF-1512) at
  Rect.Left + Px vertically centred and the caption centred in the rest of Rect (accelerators shown) }
procedure TCustomTabControl.DrawTab(TabIndex: Integer; const Rect: TRect; Active: Boolean);
var
  R: TRect;
  Px, ImageIndex: Integer;
  S: string;
begin
  if Assigned(FOnDrawTab) then
  begin
    FOnDrawTab(Self, TabIndex, Rect, Active);
    Exit;
  end;
  FCanvas.FillRect(Rect);
  R := Rect;
  if FImages <> nil then
  begin
    ImageIndex := GetImageIndex(TabIndex);
    if (ImageIndex >= 0) and (ImageIndex < FImages.Count) then
    begin
      Px := LayoutMulDiv(6, ControlDPI(Self), 96);
      FImages.Draw(FCanvas, Rect.Left + Px, (Rect.Top + Rect.Bottom - TImageListAccess(FImages).Height) div 2,
        ImageIndex);
      R.Left := Rect.Left + Px + TImageListAccess(FImages).Width;
    end;
  end;
  S := TabCaption(TabIndex);
  FCanvas.Brush.Style := bsClear;
  FCanvas.TextRect(R, S, [tfSingleLine, tfVerticalCenter, tfCenter]);
end;

{ UIF-1494: bottom, left and right tabs use the unthemed renderer; UIF-1495: padding and minimum tab length sent
  before any tab item exists }
procedure TCustomTabControl.CreateWnd;
var
  NoTheme: array[0..0] of WideChar;
  DPI: Integer;
begin
  inherited CreateWnd;
  if FTabPosition <> tpTop then
  begin
    NoTheme[0] := #0;
    SetWindowTheme(Handle, @NoTheme[0], @NoTheme[0]);
  end;
  DPI := ControlDPI(Self);
  SendMessageW(Handle, TCM_SETPADDING, 0, MakeLParam(Word(LayoutMulDiv(6, DPI, 96)), Word(LayoutMulDiv(3, DPI, 96))));
  SendMessageW(Handle, TCM_SETMINTABWIDTH, 0, LPARAM(LayoutMulDiv(48, DPI, 96)));
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

function TCustomTabControl.VisibleTabCount: Integer;
begin
  Result := 0;
end;

function TCustomTabControl.TabCaption(Index: Integer): string;
begin
  Result := '';
end;

procedure TCustomTabControl.UpdateTabItems;
begin
end;

function TCustomTabControl.GetTabIndex: Integer;
begin
  Result := FTabIndex;
end;

procedure TCustomTabControl.SetTabIndex(Value: Integer);
begin
  FTabIndex := Value;
  if HandleAllocated then
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(Value), 0);
end;

{ UIF-1512: the default image index of visible tab TabIndex is TabIndex; OnGetImageIndex may change it }
function TCustomTabControl.GetImageIndex(TabIndex: Integer): Integer;
begin
  Result := TabIndex;
  if Assigned(FOnGetImageIndex) then
    FOnGetImageIndex(Self, TabIndex, Result);
end;

{ UIF-1510: the next / previous visible tab, cyclically, with OnChanging (veto) and OnChange }
procedure TCustomTabControl.SelectNextTab(GoForward: Boolean);
var
  N, I: Integer;
begin
  N := VisibleTabCount;
  if N = 0 then
    Exit;
  I := TabIndex;
  if GoForward then
    I := (I + 1) mod N
  else if I <= 0 then
    I := N - 1
  else
    I := I - 1;
  if I = TabIndex then
    Exit;
  if not CanChange then
    Exit;
  TabIndex := I;
  Change;
end;

function TCustomTabControl.EffectiveMultiLine: Boolean;
begin
  Result := FMultiLine or (FTabPosition = tpLeft) or (FTabPosition = tpRight);
end;

function TCustomTabControl.ScrollOppositeInEffect: Boolean;
begin
  Result := FScrollOpposite and (FStyle = tsTabs) and EffectiveMultiLine;
end;

{ UIF-1498: TabWidth when > 0, else Max(MinW, Tx + 2 Px + Iw); Tx is the GDI extent of the raw caption (with its '&'
  characters) in the control's font, measured on a memory DC compatible with the screen }
function TCustomTabControl.TabLength(Index: Integer): Integer;
var
  DPI, MinW: Integer;
  DC: HDC;
  Old: HGDIOBJ;
  S: string;
  Size: TSize;
begin
  if FTabWidth > 0 then
  begin
    Result := FTabWidth;
    Exit;
  end;
  DPI := ControlDPI(Self);
  S := TabCaption(Index);
  Size.cx := 0;
  Size.cy := 0;
  if S <> '' then
  begin
    DC := CreateCompatibleDC(0);
    try
      Old := SelectObject(DC, Font.Handle);
      GetTextExtentPoint32W(DC, PWideChar(S), Length(S), Size);
      SelectObject(DC, Old);
    finally
      DeleteDC(DC);
    end;
  end;
  Result := Size.cx + 2 * LayoutMulDiv(6, DPI, 96);
  if (FImages <> nil) and (GetImageIndex(Index) >= 0) then
    Result := Result + TImageListAccess(FImages).Width + LayoutMulDiv(6, DPI, 96);
  MinW := LayoutMulDiv(48, DPI, 96);
  if Result < MinW then
    Result := MinW;
end;

{ UIF-1499: one row unless effective multi-line; else the visible tabs packed greedily in order, a row of k tabs
  fitting when k = 1 or w_1 + ... + w_k + (k - 1) Sa + E <= L }
function TCustomTabControl.RowOfTab(Index: Integer; out Rows: Integer): Integer;
var
  N, I, K, Sum, W, Sa, E, L, DPI: Integer;
begin
  Result := -1;
  N := VisibleTabCount;
  if N = 0 then
  begin
    Rows := 0;
    Exit;
  end;
  if not EffectiveMultiLine then
  begin
    Rows := 1;
    if (Index >= 0) and (Index < N) then
      Result := 0;
    Exit;
  end;
  DPI := ControlDPI(Self);
  if FStyle = tsTabs then
  begin
    Sa := 0;
    E := 5;
  end
  else
  begin
    if (FStyle = tsFlatButtons) and (FTabPosition = tpTop) then
      Sa := 10
    else
      Sa := 3;
    E := 3;
  end;
  Sa := LayoutMulDiv(Sa, DPI, 96);
  E := LayoutMulDiv(E, DPI, 96);
  if (FTabPosition = tpTop) or (FTabPosition = tpBottom) then
    L := Width
  else
    L := Height;
  Rows := 1;
  K := 0;
  Sum := 0;
  for I := 0 to N - 1 do
  begin
    W := TabLength(I);
    if (K > 0) and (Sum + W + K * Sa + E > L) then
    begin
      Inc(Rows);
      K := 0;
      Sum := 0;
    end;
    Sum := Sum + W;
    Inc(K);
    if I = Index then
      Result := Rows - 1;
  end;
end;

function TCustomTabControl.RowCount: Integer;
begin
  RowOfTab(-1, Result);
end;

{ UIF-1500: K = the row of the selected tab when TCS_SCROLLOPPOSITE is sent and a tab is selected, else 0 }
function TCustomTabControl.OppositeRows: Integer;
var
  Rows: Integer;
begin
  Result := 0;
  if not ScrollOppositeInEffect or (TabIndex < 0) then
    Exit;
  Result := RowOfTab(TabIndex, Rows);
  if Result < 0 then
    Result := 0;
end;

{ UIF-1501: depth(r) = r TabH + Max(r - 1, 0) Sr + P on the strip side (Rn = R - K rows) and on the opposite side
  (K rows); extents clamped at 0 }
function TCustomTabControl.GetDisplayRect: TRect;
var
  DPI, P, Sr, Th, R, K, DK, DN: Integer;
begin
  DPI := ControlDPI(Self);
  P := LayoutMulDiv(4, DPI, 96);
  if FStyle = tsTabs then
    Sr := 0
  else
    Sr := LayoutMulDiv(3, DPI, 96);
  Th := TabH;
  R := RowCount;
  K := OppositeRows;
  DK := K * Th + P;
  if K > 1 then
    DK := DK + (K - 1) * Sr;
  DN := (R - K) * Th + P;
  if R - K > 1 then
    DN := DN + (R - K - 1) * Sr;
  case FTabPosition of
    tpBottom:
      Result := Rect(P, DK, Width - P, Height - DN);
    tpLeft:
      Result := Rect(DN, P, Width - DK, Height - P);
    tpRight:
      Result := Rect(DK, P, Width - DN, Height - P);
  else
    Result := Rect(P, DN, Width - P, Height - DK);
  end;
  if Result.Right < Result.Left then
    Result.Right := Result.Left;
  if Result.Bottom < Result.Top then
    Result.Bottom := Result.Top;
end;

function TCustomTabControl.TabRect(Index: Integer): TRect;
begin
  Result := Rect(0, 0, 0, 0);
  if not HandleAllocated or (Index < 0) or (Index >= Integer(SendMessageW(Handle, TCM_GETITEMCOUNT, 0, 0))) then
    Exit;
  if SendMessageW(Handle, TCM_GETITEMRECT, WPARAM(Index), LPARAM(@Result)) = 0 then
    Result := Rect(0, 0, 0, 0);
end;

function TCustomTabControl.IndexOfTabAt(X, Y: Integer): Integer;
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

procedure TCustomTabControl.StyleChanged;
begin
  RecreateWnd;
  Realign;
end;

procedure TCustomTabControl.SetHotTrack(Value: Boolean);
begin
  if FHotTrack <> Value then
  begin
    FHotTrack := Value;
    RecreateWnd;
  end;
end;

{ UIF-1496: a change link with the image list; the tab items' images re-queried and the control realigned }
procedure TCustomTabControl.SetImages(Value: TCustomImageList);
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
  if HandleAllocated then
    if FImages <> nil then
      SendMessageW(Handle, TCM_SETIMAGELIST, 0, LPARAM(FImages.Handle))
    else
      SendMessageW(Handle, TCM_SETIMAGELIST, 0, 0);
  if not (csDestroying in ComponentState) then
  begin
    UpdateTabItems;
    Realign;
  end;
end;

procedure TCustomTabControl.ImageListChange(Sender: TObject);
begin
  if HandleAllocated and (FImages <> nil) then
    SendMessageW(Handle, TCM_SETIMAGELIST, 0, LPARAM(FImages.Handle));
  UpdateTabItems;
  Realign;
end;

procedure TCustomTabControl.SetMultiLine(Value: Boolean);
begin
  if FMultiLine <> Value then
  begin
    FMultiLine := Value;
    StyleChanged;
  end;
end;

procedure TCustomTabControl.SetOwnerDraw(Value: Boolean);
begin
  if FOwnerDraw <> Value then
  begin
    FOwnerDraw := Value;
    StyleChanged;
  end;
end;

procedure TCustomTabControl.SetRaggedRight(Value: Boolean);
begin
  if FRaggedRight <> Value then
  begin
    FRaggedRight := Value;
    StyleChanged;
  end;
end;

procedure TCustomTabControl.SetScrollOpposite(Value: Boolean);
begin
  if FScrollOpposite <> Value then
  begin
    FScrollOpposite := Value;
    StyleChanged;
  end;
end;

procedure TCustomTabControl.SetStyle(Value: TTabStyle);
begin
  if FStyle <> Value then
  begin
    FStyle := Value;
    StyleChanged;
  end;
end;

{ UIF-1493: the stored MultiLine value is kept; left and right tabs are multi-line through the effective rule }
procedure TCustomTabControl.SetTabPosition(Value: TTabPosition);
begin
  if FTabPosition <> Value then
  begin
    FTabPosition := Value;
    StyleChanged;
  end;
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
    Realign;                                                 { the tab lengths of UIF-1498 }
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
    TabsChanged;
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
  TabsChanged;
end;

procedure TPageControl.PageTabVisibleChanged(Page: TTabSheet);
begin
  if not (csLoading in ComponentState) then
    if not Page.FTabVisible and (Page = FActivePage) then
      ChangeActivePage(SuccessorOf(Page))
    else if Page.FTabVisible and (FActivePage = nil) then
      ChangeActivePage(Page);
  TabsChanged;
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
  if ScrollOppositeInEffect then
    Realign;                                                 { UIF-1502: K follows the selected tab }
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

function TPageControl.VisibleTabCount: Integer;
var
  I: Integer;
begin
  Result := 0;
  if FPages <> nil then
    for I := 0 to FPages.Count - 1 do
      if TTabSheet(FPages[I]).FTabVisible then
        Inc(Result);
end;

function TPageControl.TabCaption(Index: Integer): string;
var
  P: TTabSheet;
begin
  P := PageOfTab(Index);
  if P = nil then
    Result := ''
  else
    Result := P.Caption;
end;

procedure TPageControl.UpdateTabItems;
begin
  UpdateTabs;
end;

{ section 3: the tab index of the active page }
function TPageControl.GetTabIndex: Integer;
begin
  if FActivePage = nil then
    Result := -1
  else
    Result := FActivePage.TabIndex;
end;

procedure TPageControl.SetTabIndex(Value: Integer);
begin
  SetActivePage(PageOfTab(Value));
end;

{ UIF-1512: the page's ImageIndex; OnGetImageIndex may change it }
function TPageControl.GetImageIndex(TabIndex: Integer): Integer;
var
  P: TTabSheet;
begin
  P := PageOfTab(TabIndex);
  if P = nil then
    Result := -1
  else
    Result := P.FImageIndex;
  if Assigned(OnGetImageIndex) then
    OnGetImageIndex(Self, TabIndex, Result);
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
    if Images <> nil then
      Item.iImage := GetImageIndex(N)
    else
      Item.iImage := -1;
    SendMessageW(Handle, TCM_INSERTITEMW, WPARAM(N), LPARAM(@Item));
    Inc(N);
  end;
  if FActivePage <> nil then
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(FActivePage.TabIndex), 0)
  else
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(-1), 0);
end;

procedure TPageControl.TabsChanged;
begin
  UpdateTabs;
  if not (csDestroying in ComponentState) then
    Realign;
end;

procedure TPageControl.CreateWnd;
begin
  inherited CreateWnd;
  UpdateTabs;
end;

function TPageControl.PageRect: TRect;
begin
  Result := DisplayRect;
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

{ UIF-771: the old page of the switch is the page an earlier Loaded made visible (a page control reached by a frame
  override block is loaded twice, classes.md RTL-1097): a second Loaded hides it or leaves it, never shows two }
procedure TPageControl.Loaded;
var
  P: TTabSheet;
  I: Integer;
begin
  P := FActivePage;
  FActivePage := nil;
  for I := 0 to FPages.Count - 1 do
    if TTabSheet(FPages[I]).Visible then
      FActivePage := TTabSheet(FPages[I]);
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

{ ---------------------------------------------------------------- TTabStrings }

type
  { UIF-1506: the strings and objects of TTabControl.Tabs, held here (nothing is read back from the window) and
    mirrored to the native items; the TabIndex rules of UIF-1507 (not while loading, UIF-1508) }
  TTabStrings = class(TStrings)
  private
    FControl: TTabControl;
    FList: TStringList;
    { UIF-1507: the selected tab after an edit (not while loading) }
    procedure Follow(Value: Integer);
  protected
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    function GetObject(Index: Integer): TObject; override;
    procedure Put(Index: Integer; const S: string); override;
    procedure PutObject(Index: Integer; AObject: TObject); override;
    { BeginUpdate / EndUpdate: one rebuild at the outermost EndUpdate }
    procedure SetUpdateState(Updating: Boolean); override;
  public
    constructor Create(AControl: TTabControl);
    destructor Destroy; override;
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    procedure Exchange(Index1, Index2: Integer); override;
    procedure Insert(Index: Integer; const S: string); override;
    procedure Move(CurIndex, NewIndex: Integer); override;
  end;

constructor TTabStrings.Create(AControl: TTabControl);
begin
  inherited Create;
  FControl := AControl;
  FList := TStringList.Create;
end;

destructor TTabStrings.Destroy;
begin
  FList.Free;
  FList := nil;
  inherited Destroy;
end;

procedure TTabStrings.Follow(Value: Integer);
begin
  if not (csLoading in FControl.ComponentState) then
    FControl.FTabIndex := Value;
end;

function TTabStrings.Get(Index: Integer): string;
begin
  Result := FList[Index];
end;

function TTabStrings.GetCount: Integer;
begin
  Result := FList.Count;
end;

function TTabStrings.GetObject(Index: Integer): TObject;
begin
  Result := FList.Objects[Index];
end;

procedure TTabStrings.Put(Index: Integer; const S: string);
begin
  FList[Index] := S;
  if not FControl.FTabsUpdating then
    FControl.SendItem(Index, False);
  FControl.TabsEdited;
end;

procedure TTabStrings.PutObject(Index: Integer; AObject: TObject);
begin
  FList.Objects[Index] := AObject;
end;

procedure TTabStrings.SetUpdateState(Updating: Boolean);
begin
  FControl.FTabsUpdating := Updating;
  if not Updating then
  begin
    FControl.UpdateTabItems;
    FControl.TabsEdited;
  end;
end;

procedure TTabStrings.Clear;
begin
  FList.Clear;
  Follow(-1);
  if not FControl.FTabsUpdating and FControl.HandleAllocated then
    SendMessageW(FControl.Handle, TCM_DELETEALLITEMS, 0, 0);
  FControl.TabsEdited;
end;

procedure TTabStrings.Delete(Index: Integer);
var
  T: Integer;
begin
  FList.Delete(Index);
  T := FControl.FTabIndex;
  if Index < T then
    Follow(T - 1)
  else if (Index = T) and (T >= FList.Count) then
    Follow(FList.Count - 1);                                 { the new last one, -1 when none is left }
  if not FControl.FTabsUpdating and FControl.HandleAllocated then
    SendMessageW(FControl.Handle, TCM_DELETEITEM, WPARAM(Index), 0);
  FControl.TabsEdited;
end;

procedure TTabStrings.Exchange(Index1, Index2: Integer);
var
  T: Integer;
begin
  FList.Exchange(Index1, Index2);
  T := FControl.FTabIndex;
  if T = Index1 then
    Follow(Index2)
  else if T = Index2 then
    Follow(Index1);
  if not FControl.FTabsUpdating then
  begin
    FControl.SendItem(Index1, False);
    FControl.SendItem(Index2, False);
  end;
  FControl.TabsEdited;
end;

procedure TTabStrings.Insert(Index: Integer; const S: string);
var
  T: Integer;
begin
  FList.Insert(Index, S);
  T := FControl.FTabIndex;
  if (FList.Count = 1) and (T = -1) then
    Follow(0)                                                { UIF-1507: the first tab of an empty control }
  else if Index <= T then
    Follow(T + 1);
  if not FControl.FTabsUpdating then
    FControl.SendItem(Index, True);
  FControl.TabsEdited;
end;

procedure TTabStrings.Move(CurIndex, NewIndex: Integer);
var
  T, I, Lo, Hi: Integer;
begin
  if CurIndex = NewIndex then
    Exit;
  FList.Move(CurIndex, NewIndex);
  T := FControl.FTabIndex;
  if T = CurIndex then
    Follow(NewIndex)
  else if (CurIndex < T) and (T <= NewIndex) then
    Follow(T - 1)
  else if (NewIndex <= T) and (T < CurIndex) then
    Follow(T + 1);
  if not FControl.FTabsUpdating then
  begin
    if CurIndex < NewIndex then
    begin
      Lo := CurIndex;
      Hi := NewIndex;
    end
    else
    begin
      Lo := NewIndex;
      Hi := CurIndex;
    end;
    for I := Lo to Hi do
      FControl.SendItem(I, False);
  end;
  FControl.TabsEdited;
end;

{ ---------------------------------------------------------------- TTabControl }

constructor TTabControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FTabs := TTabStrings.Create(Self);
end;

destructor TTabControl.Destroy;
begin
  inherited Destroy;
  FTabs.Free;
  FTabs := nil;
end;

procedure TTabControl.SetTabs(Value: TStrings);
begin
  FTabs.Assign(Value);
end;

function TTabControl.VisibleTabCount: Integer;
begin
  if FTabs = nil then
    Result := 0
  else
    Result := FTabs.Count;
end;

function TTabControl.TabCaption(Index: Integer): string;
begin
  Result := FTabs[Index];
end;

procedure TTabControl.SendItem(Index: Integer; Insert: Boolean);
var
  Item: TTCItemW;
  S: string;
begin
  if not HandleAllocated then
    Exit;
  S := FTabs[Index];
  FillChar(Item, SizeOf(Item), 0);
  Item.mask := TCIF_TEXT or TCIF_IMAGE;
  Item.pszText := PChar(S);
  if Images <> nil then
    Item.iImage := GetImageIndex(Index)
  else
    Item.iImage := -1;
  if Insert then
    SendMessageW(Handle, TCM_INSERTITEMW, WPARAM(Index), LPARAM(@Item))
  else
    SendMessageW(Handle, TCM_SETITEMW, WPARAM(Index), LPARAM(@Item));
end;

procedure TTabControl.TabsEdited;
begin
  if FTabsUpdating or (csDestroying in ComponentState) then
    Exit;
  if HandleAllocated then
    SendMessageW(Handle, TCM_SETCURSEL, WPARAM(FTabIndex), 0);
  Realign;
end;

{ UIF-1506: TCM_DELETEALLITEMS, every tab inserted, TabIndex selected }
procedure TTabControl.UpdateTabItems;
var
  I: Integer;
begin
  if not HandleAllocated or (FTabs = nil) then
    Exit;
  SendMessageW(Handle, TCM_DELETEALLITEMS, 0, 0);
  for I := 0 to FTabs.Count - 1 do
    SendItem(I, True);
  SendMessageW(Handle, TCM_SETCURSEL, WPARAM(FTabIndex), 0);
end;

{ UIF-1507: a value outside -1 ... Count - 1 is ignored; UIF-1508: stored unchecked while loading }
procedure TTabControl.SetTabIndex(Value: Integer);
begin
  if csLoading in ComponentState then
  begin
    FTabIndex := Value;
    Exit;
  end;
  if (Value < -1) or (Value >= FTabs.Count) then
    Exit;
  inherited SetTabIndex(Value);
  if ScrollOppositeInEffect then
    Realign;                                                 { UIF-1502: K follows the selected tab }
end;

procedure TTabControl.CreateWnd;
begin
  inherited CreateWnd;
  UpdateTabItems;
end;

{ UIF-1508: the streamed TabIndex applied; out of range -> -1 }
procedure TTabControl.Loaded;
begin
  if (FTabIndex < -1) or (FTabIndex >= FTabs.Count) then
    FTabIndex := -1;
  inherited Loaded;
  UpdateTabItems;
  Realign;
end;

procedure TTabControl.AdjustClientRect(var Rect: TRect);
begin
  Rect := DisplayRect;
end;

procedure TTabControl.SelectTab(Index: Integer);
begin
  if (Index < 0) or (Index >= FTabs.Count) or (Index = FTabIndex) then
    Exit;
  if not CanChange then
    Exit;
  TabIndex := Index;
  Change;
end;

procedure TTabControl.WndProc(var Message: TMessage);
var
  I: Integer;
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
            TabIndex := Integer(SendMessageW(Handle, TCM_GETCURSEL, 0, 0));
            Change;
          end;
      else
        inherited WndProc(Message);
      end;
    CM_DIALOGKEY:
      if (Message.WParam = VK_TAB) and (GetKeyState(VK_CONTROL) < 0) then
      begin
        SelectNextTab(GetKeyState(VK_SHIFT) >= 0);
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_DIALOGCHAR:
      begin
        for I := 0 to FTabs.Count - 1 do
          if IsAccel(Word(Message.WParam), FTabs[I]) then
          begin
            SelectTab(I);
            Message.Result := 1;
            Exit;
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

function TToolButton.UpdatesActionWhenHidden: Boolean;
begin
  Result := True;
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
      ToolButtonDownChanged(TToolButton(C));
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
      ToolButtonDownChanged(TToolButton(C));
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
  ToolButtonDownChanged(Self);
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

{ UIF-1430, UIF-1431: the button whose provider posted the click is clicked as by the mouse (when it still exists
  and is enabled); after an Invoke, Invoke_Invoked is raised when a client listens }
procedure TToolBar.WndProc(var Message: TMessage);
var
  I: Integer;
  C: TControl;
  P: TAutomationProvider;
  Keep: IRawElementProviderSimple;
begin
  if Message.Msg <> CM_TOOLBUTTONCLICK then
  begin
    inherited WndProc(Message);
    Exit;
  end;
  for I := 0 to ControlCount - 1 do
  begin
    C := Controls[I];
    if (C is TToolButton) and (C.AutomationObject <> nil)
      and (TAutomationProvider(C.AutomationObject).Number = Integer(Message.WParam)) then
    begin
      Message.Result := 0;
      if AccessibleEnabledOf(C) then
      begin
        P := TAutomationProvider(C.AutomationObject);
        Keep := GetAutomationProvider(C);                    { P lives through the click, also when C goes }
        TToolButton(C).Click;
        if (Message.LParam = ClickByInvoke) and (P.Control <> nil) then
          RaiseAutomationEventOf(P.Control, UIA_Invoke_InvokedEventId);
      end;
      Exit;
    end;
  end;
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


{ ---------------------------------------------------------------- TListColumn }

constructor TListColumn.Create(Collection: TCollection);
begin
  FWidth := 50;
  inherited Create(Collection);
end;

procedure TListColumn.Assign(Source: TPersistent);
begin
  if Source is TListColumn then
  begin
    FAlignment := TListColumn(Source).FAlignment;
    FCaption := TListColumn(Source).FCaption;
    FWidth := TListColumn(Source).FWidth;
    Changed(False);
  end
  else
    inherited Assign(Source);
end;

function TListColumn.GetDisplayName: string;
begin
  Result := FCaption;
end;

procedure TListColumn.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    Changed(False);
  end;
end;

procedure TListColumn.SetCaption(const Value: string);
begin
  if FCaption <> Value then
  begin
    FCaption := Value;
    Changed(False);
  end;
end;

procedure TListColumn.SetWidth(Value: Integer);
begin
  if FWidth <> Value then
  begin
    FWidth := Value;
    Changed(False);
  end;
end;

{ ---------------------------------------------------------------- TListColumns }

constructor TListColumns.Create(ListView: TCustomListView);
begin
  inherited Create(ListView, TListColumn);
  FListView := ListView;
end;

function TListColumns.Add: TListColumn;
begin
  Result := TListColumn(inherited Add);
end;

function TListColumns.GetItem(Index: Integer): TListColumn;
begin
  Result := TListColumn(inherited GetItem(Index));
end;

procedure TListColumns.SetItem(Index: Integer; Value: TListColumn);
begin
  inherited SetItem(Index, Value);
end;

procedure TListColumns.Update(Item: TCollectionItem);
begin
  if FListView <> nil then
    FListView.UpdateColumns;
end;

{ ---------------------------------------------------------------- TListItem }

constructor TListItem.Create(AOwner: TListItems);
begin
  inherited Create;
  FOwner := AOwner;
  FImageIndex := -1;
  FSubItems := TStringList.Create;
  TStringList(FSubItems).OnChange := SubItemsChanged;
end;

destructor TListItem.Destroy;
var
  I: Integer;
  LV: TCustomListView;
begin
  if FOwner <> nil then
  begin
    I := FOwner.FList.IndexOf(Self);
    if I >= 0 then
    begin
      LV := FOwner.FOwner;
      if LV.HandleAllocated then
        SendMessageW(LV.Handle, LVM_DELETEITEM, WPARAM(I), 0);
      FOwner.FList.Delete(I);
    end;
  end;
  FSubItems.Free;
  inherited Destroy;
end;

procedure TListItem.Assign(Source: TPersistent);
begin
  if Source is TListItem then
  begin
    Caption := TListItem(Source).FCaption;
    SubItems := TListItem(Source).FSubItems;
    ImageIndex := TListItem(Source).FImageIndex;
    Data := TListItem(Source).FData;
  end
  else
    inherited Assign(Source);
end;

function TListItem.GetIndex: Integer;
begin
  Result := FOwner.IndexOf(Self);
end;

function TListItem.GetListView: TCustomListView;
begin
  Result := FOwner.FOwner;
end;

procedure TListItem.Delete;
begin
  Free;
end;

procedure TListItem.SetCaption(const Value: string);
begin
  if FCaption <> Value then
  begin
    FCaption := Value;
    ListView.ItemChanged(Self, ctText);
  end;
end;

procedure TListItem.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    ListView.ItemChanged(Self, ctImage);
  end;
end;

procedure TListItem.SetSubItems(Value: TStrings);
begin
  FSubItems.Assign(Value);
end;

procedure TListItem.SubItemsChanged(Sender: TObject);
begin
  ListView.ItemChanged(Self, ctText);
end;

procedure TListItem.SetState(Mask, State: UINT);
var
  It: TLVItemW;
  LV: TCustomListView;
begin
  LV := ListView;
  if not LV.HandleAllocated then
    Exit;
  FillChar(It, SizeOf(It), 0);
  It.mask := LVIF_STATE;
  It.stateMask := Mask;
  It.state := State;
  SendMessageW(LV.Handle, LVM_SETITEMSTATE, WPARAM(Index), LPARAM(@It));
end;

procedure TListItem.SetChecked(Value: Boolean);
begin
  SetState(LVIS_STATEIMAGEMASK, UINT(Ord(Value) + 1) shl 12);
  FChecked := Value;
end;

procedure TListItem.SetSelected(Value: Boolean);
begin
  SetState(LVIS_SELECTED, UINT(Ord(Value)) * LVIS_SELECTED);
  FSelected := Value;
end;

procedure TListItem.SetFocused(Value: Boolean);
var
  I: Integer;
begin
  SetState(LVIS_FOCUSED, UINT(Ord(Value)) * LVIS_FOCUSED);
  if Value and not ListView.HandleAllocated then
    for I := 0 to FOwner.Count - 1 do
      FOwner[I].FFocused := False;
  FFocused := Value;
end;

procedure TListItem.MakeVisible(PartialOK: Boolean);
begin
  if ListView.HandleAllocated then
    SendMessageW(ListView.Handle, LVM_ENSUREVISIBLE, WPARAM(Index), LPARAM(Ord(PartialOK)));
end;

function TListItem.DisplayRect(Code: TDisplayCode): TRect;
const
  Codes: array[TDisplayCode] of Integer = (LVIR_BOUNDS, LVIR_ICON, LVIR_LABEL, LVIR_SELECTBOUNDS);
begin
  Result := Rect(0, 0, 0, 0);
  if ListView.HandleAllocated then
  begin
    Result.Left := Codes[Code];
    if SendMessageW(ListView.Handle, LVM_GETITEMRECT, WPARAM(Index), LPARAM(@Result)) = 0 then
      Result := Rect(0, 0, 0, 0);
  end;
end;

{ ---------------------------------------------------------------- TListItems }

constructor TListItems.Create(AOwner: TCustomListView);
begin
  inherited Create;
  FOwner := AOwner;
  FList := TList.Create;
end;

destructor TListItems.Destroy;
begin
  Clear;
  FList.Free;
  inherited Destroy;
end;

function TListItems.GetCount: Integer;
begin
  Result := FList.Count;
end;

function TListItems.GetItem(Index: Integer): TListItem;
begin
  Result := TListItem(FList[Index]);
end;

function TListItems.IndexOf(Value: TListItem): Integer;
begin
  Result := FList.IndexOf(Value);
end;

function TListItems.Add: TListItem;
begin
  Result := Insert(FList.Count);
end;

function TListItems.Insert(Index: Integer): TListItem;
begin
  Result := TListItem.Create(Self);
  FList.Insert(Index, Result);
  if FOwner.HandleAllocated then
    InsertNative(Result, Index);
end;

procedure TListItems.InsertNative(Item: TListItem; Index: Integer);
var
  It: TLVItemW;
  Old: Boolean;
begin
  FillChar(It, SizeOf(It), 0);
  It.mask := LVIF_TEXT or LVIF_IMAGE or LVIF_PARAM or LVIF_STATE;
  It.iItem := Index;
  It.pszText := LPSTR_TEXTCALLBACKW;
  It.iImage := I_IMAGECALLBACK;
  It.lParam := LPARAM(Item);
  It.stateMask := LVIS_SELECTED or LVIS_FOCUSED or LVIS_STATEIMAGEMASK;
  It.state := UINT(Ord(Item.FChecked) + 1) shl 12;
  if Item.FSelected then
    It.state := It.state or LVIS_SELECTED;
  if Item.FFocused then
    It.state := It.state or LVIS_FOCUSED;
  Old := FOwner.FNoEvents;
  FOwner.FNoEvents := True;
  try
    SendMessageW(FOwner.Handle, LVM_INSERTITEMW, 0, LPARAM(@It));
    { the state is not always taken on insertion (LVS_EX_CHECKBOXES resets the state image) }
    SendMessageW(FOwner.Handle, LVM_SETITEMSTATE, WPARAM(Index), LPARAM(@It));
  finally
    FOwner.FNoEvents := Old;
  end;
end;

procedure TListItems.Delete(Index: Integer);
begin
  Item[Index].Free;
end;

procedure TListItems.Clear;
var
  I: Integer;
  It: TListItem;
begin
  if FList.Count = 0 then
    Exit;
  if FOwner.HandleAllocated then
    SendMessageW(FOwner.Handle, LVM_DELETEALLITEMS, 0, 0);
  for I := FList.Count - 1 downto 0 do
  begin
    It := TListItem(FList[I]);
    It.FOwner := nil;
    It.Free;
  end;
  FList.Clear;
end;

procedure TListItems.BeginUpdate;
begin
  if (FUpdateCount = 0) and FOwner.HandleAllocated then
    SendMessageW(FOwner.Handle, WM_SETREDRAW, 0, 0);
  Inc(FUpdateCount);
end;

procedure TListItems.EndUpdate;
begin
  Dec(FUpdateCount);
  if (FUpdateCount = 0) and FOwner.HandleAllocated then
  begin
    SendMessageW(FOwner.Handle, WM_SETREDRAW, 1, 0);
    FOwner.Invalidate;
  end;
end;

{ ---------------------------------------------------------------- TCustomListView }

constructor TCustomListView.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle - [csAcceptsControls, csSetCaption];
  FColumns := TListColumns.Create(Self);
  FItems := TListItems.Create(Self);
  FBorderStyle := bsSingle;
  FColumnClick := True;
  FHideSelection := True;
  FShowColumnHeaders := True;
  FLargeChangeLink := TChangeLink.Create;
  FLargeChangeLink.OnChange := ImageListChange;
  FSmallChangeLink := TChangeLink.Create;
  FSmallChangeLink.OnChange := ImageListChange;
  TabStop := True;
  Color := clWindow;
  SetBounds(Left, Top, 250, 150);
end;

destructor TCustomListView.Destroy;
begin
  { UIF-822: no events while the list is cleared }
  FNoEvents := True;
  FItems.Clear;
  FColumns.FListView := nil;
  LargeImages := nil;
  SmallImages := nil;
  inherited Destroy;
  FItems.Free;
  FItems := nil;
  FColumns.Free;
  FColumns := nil;
  FLargeChangeLink.Free;
  FLargeChangeLink := nil;
  FSmallChangeLink.Free;
  FSmallChangeLink := nil;
end;

procedure TCustomListView.CreateParams(var Params: TCreateParams);
const
  Views: array[TViewStyle] of DWORD = (LVS_ICON, LVS_SMALLICON, LVS_LIST, LVS_REPORT);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := WC_LISTVIEW;
  Params.Style := Params.Style or LVS_SHAREIMAGELISTS or Views[FViewStyle];
  if not FMultiSelect then
    Params.Style := Params.Style or LVS_SINGLESEL;
  if not FHideSelection then
    Params.Style := Params.Style or LVS_SHOWSELALWAYS;
  if not FReadOnly then
    Params.Style := Params.Style or LVS_EDITLABELS;
  if not FShowColumnHeaders then
    Params.Style := Params.Style or LVS_NOCOLUMNHEADER;
  if not FColumnClick then
    Params.Style := Params.Style or LVS_NOSORTHEADER;
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
end;

procedure TCustomListView.CreateWnd;
var
  I: Integer;
begin
  inherited CreateWnd;
  UpdateExtendedStyles;
  UpdateColors;
  UpdateImageLists;
  UpdateColumns;
  for I := 0 to FItems.Count - 1 do
    FItems.InsertNative(FItems[I], I);
end;

procedure TCustomListView.UpdateExtendedStyles;
const
  Mask = LVS_EX_GRIDLINES or LVS_EX_FULLROWSELECT or LVS_EX_CHECKBOXES or LVS_EX_DOUBLEBUFFER;
var
  Bits: DWORD;
begin
  if not HandleAllocated then
    Exit;
  Bits := LVS_EX_DOUBLEBUFFER;
  if FGridLines then
    Bits := Bits or LVS_EX_GRIDLINES;
  if FRowSelect then
    Bits := Bits or LVS_EX_FULLROWSELECT;
  if FCheckboxes then
    Bits := Bits or LVS_EX_CHECKBOXES;
  SendMessageW(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, Mask, LPARAM(Bits));
end;

procedure TCustomListView.UpdateColors;
begin
  if not HandleAllocated then
    Exit;
  SendMessageW(Handle, LVM_SETBKCOLOR, 0, LPARAM(ColorToRGB(Color)));
  SendMessageW(Handle, LVM_SETTEXTBKCOLOR, 0, LPARAM(ColorToRGB(Color)));
  SendMessageW(Handle, LVM_SETTEXTCOLOR, 0, LPARAM(ColorToRGB(Font.Color)));
  Invalidate;
end;

procedure TCustomListView.UpdateImageLists;
var
  H: HIMAGELIST;
begin
  if not HandleAllocated then
    Exit;
  H := 0;
  if FLargeImages <> nil then
    H := FLargeImages.Handle;
  SendMessageW(Handle, LVM_SETIMAGELIST, LVSIL_NORMAL, LPARAM(H));
  H := 0;
  if FSmallImages <> nil then
    H := FSmallImages.Handle;
  SendMessageW(Handle, LVM_SETIMAGELIST, LVSIL_SMALL, LPARAM(H));
end;

procedure TCustomListView.UpdateColumns;
const
  Formats: array[TAlignment] of Integer = (LVCFMT_LEFT, LVCFMT_RIGHT, LVCFMT_CENTER);
var
  I: Integer;
  C: TLVColumnW;
  Col: TListColumn;
begin
  if not HandleAllocated or (FColumns = nil) then
    Exit;
  FUpdatingColumns := True;
  try
    while SendMessageW(Handle, LVM_DELETECOLUMN, 0, 0) <> 0 do
      ;
    for I := 0 to FColumns.Count - 1 do
    begin
      Col := FColumns[I];
      FillChar(C, SizeOf(C), 0);
      C.mask := LVCF_FMT or LVCF_WIDTH or LVCF_TEXT or LVCF_SUBITEM;
      if I = 0 then
        C.fmt := LVCFMT_LEFT
      else
        C.fmt := Formats[Col.Alignment];
      C.cx := Col.Width;
      C.pszText := PChar(Col.Caption);
      C.iSubItem := I;
      SendMessageW(Handle, LVM_INSERTCOLUMNW, WPARAM(I), LPARAM(@C));
    end;
  finally
    FUpdatingColumns := False;
  end;
end;

procedure TCustomListView.ChangeScale(M, D: Integer);
var
  I: Integer;
begin
  inherited ChangeScale(M, D);
  if M = D then
    Exit;
  FColumns.BeginUpdate;
  try
    for I := 0 to FColumns.Count - 1 do
      FColumns[I].Width := LayoutMulDiv(FColumns[I].Width, M, D);
  finally
    FColumns.EndUpdate;
  end;
end;

procedure TCustomListView.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if Operation = opRemove then
  begin
    if AComponent = FLargeImages then
      LargeImages := nil;
    if AComponent = FSmallImages then
      SmallImages := nil;
  end;
end;

procedure TCustomListView.CMColorChanged(var Message: TMessage);
begin
  inherited CMColorChanged(Message);
  UpdateColors;
end;

procedure TCustomListView.CMFontChanged(var Message: TMessage);
begin
  inherited CMFontChanged(Message);
  UpdateColors;
end;

procedure TCustomListView.WndProc(var Message: TMessage);
var
  N: PNMHdr;
  I: Integer;
  Item: TListItem;
  S: string;
begin
  case Message.Msg of
    CN_NOTIFY:
      begin
        N := PNMHdr(Message.LParam);
        case N^.code of
          LVN_COLUMNCLICK:
            begin
              I := PNMListView(Message.LParam)^.iSubItem;
              if (I >= 0) and (I < FColumns.Count) then
                DoColumnClick(FColumns[I]);
              Message.Result := 0;
            end;
          LVN_GETDISPINFOW:
            begin
              GetDispInfo(PLVDispInfoW(Message.LParam)^);
              Message.Result := 0;
            end;
          LVN_ITEMCHANGED:
            begin
              if not FNoEvents and not (csDestroying in ComponentState)
                and ((PNMListView(Message.LParam)^.uChanged and LVIF_STATE) <> 0) then
              begin
                I := PNMListView(Message.LParam)^.iItem;
                if I = -1 then
                  for I := 0 to FItems.Count - 1 do
                    SyncItemState(I)
                else
                  SyncItemState(I);
              end;
              Message.Result := 0;
            end;
          LVN_ENDLABELEDITW:
            { UIF-824 }
            if (PLVDispInfoW(Message.LParam)^.item.pszText <> nil)
              and (PLVDispInfoW(Message.LParam)^.item.iItem >= 0)
              and (PLVDispInfoW(Message.LParam)^.item.iItem < FItems.Count) then
            begin
              Item := FItems[PLVDispInfoW(Message.LParam)^.item.iItem];
              S := PChar(PLVDispInfoW(Message.LParam)^.item.pszText);
              if Assigned(FOnEdited) then
                FOnEdited(Self, Item, S);
              Item.Caption := S;
              Message.Result := 1;
            end
            else
              Message.Result := 0;
        else
          inherited WndProc(Message);
        end;
      end;
    WM_LBUTTONDOWN, WM_LBUTTONDBLCLK:
      begin
        inherited WndProc(Message);
        { UIF-825: the native control tracks the button itself and consumes WM_LBUTTONUP; when it returns with the
          button released, the release is replayed through the normal mouse path (OnClick, OnMouseUp) }
        if HandleAllocated and (GetKeyState(VK_LBUTTON) >= 0) then
        begin
          if (Message.Msg = WM_LBUTTONDOWN) and (csClickEvents in ControlStyle) then
            ControlState := ControlState + [csClicked];
          Perform(WM_LBUTTONUP, 0, Message.LParam);
        end;
      end;
    WM_NOTIFY:
      begin
        inherited WndProc(Message);
        N := PNMHdr(Message.LParam);
        if (N <> nil) and (N^.code = HDN_ITEMCHANGEDW) and not FUpdatingColumns
          and (N^.hwndFrom = HWND(SendMessageW(Handle, LVM_GETHEADER, 0, 0))) then
        begin
          I := PNMHeaderW(Message.LParam)^.iItem;
          if (I >= 0) and (I < FColumns.Count) then
            FColumns[I].FWidth := Integer(SendMessageW(Handle, LVM_GETCOLUMNWIDTH, WPARAM(I), 0));
        end;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomListView.DoColumnClick(Column: TListColumn);
begin
  if Assigned(FOnColumnClick) then
    FOnColumnClick(Self, Column);
end;

procedure TCustomListView.ImageListChange(Sender: TObject);
begin
  UpdateImageLists;
end;

procedure TCustomListView.SetImageList(var Field: TCustomImageList; Value: TCustomImageList; Link: TChangeLink);
begin
  if Field = Value then
    Exit;
  if Field <> nil then
  begin
    Field.UnRegisterChanges(Link);
    Field.RemoveFreeNotification(Self);
  end;
  Field := Value;
  if Field <> nil then
  begin
    Field.RegisterChanges(Link);
    Field.FreeNotification(Self);
  end;
  UpdateImageLists;
end;

procedure TCustomListView.SetLargeImages(Value: TCustomImageList);
begin
  SetImageList(FLargeImages, Value, FLargeChangeLink);
end;

procedure TCustomListView.SetSmallImages(Value: TCustomImageList);
begin
  SetImageList(FSmallImages, Value, FSmallChangeLink);
end;

procedure TCustomListView.SetColumns(Value: TListColumns);
begin
  FColumns.Assign(Value);
end;

procedure TCustomListView.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetCheckboxes(Value: Boolean);
begin
  if FCheckboxes <> Value then
  begin
    FCheckboxes := Value;
    UpdateExtendedStyles;
  end;
end;

procedure TCustomListView.SetColumnClick(Value: Boolean);
begin
  if FColumnClick <> Value then
  begin
    FColumnClick := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetGridLines(Value: Boolean);
begin
  if FGridLines <> Value then
  begin
    FGridLines := Value;
    UpdateExtendedStyles;
  end;
end;

procedure TCustomListView.SetHideSelection(Value: Boolean);
begin
  if FHideSelection <> Value then
  begin
    FHideSelection := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetMultiSelect(Value: Boolean);
begin
  if FMultiSelect <> Value then
  begin
    FMultiSelect := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetReadOnly(Value: Boolean);
begin
  if FReadOnly <> Value then
  begin
    FReadOnly := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetRowSelect(Value: Boolean);
begin
  if FRowSelect <> Value then
  begin
    FRowSelect := Value;
    UpdateExtendedStyles;
  end;
end;

procedure TCustomListView.SetShowColumnHeaders(Value: Boolean);
begin
  if FShowColumnHeaders <> Value then
  begin
    FShowColumnHeaders := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListView.SetViewStyle(Value: TViewStyle);
const
  Views: array[TViewStyle] of Integer = (LV_VIEW_ICON, LV_VIEW_SMALLICON, LV_VIEW_LIST, LV_VIEW_DETAILS);
begin
  if FViewStyle <> Value then
  begin
    FViewStyle := Value;
    if HandleAllocated then
      SendMessageW(Handle, LVM_SETVIEW, WPARAM(Views[FViewStyle]), 0);
  end;
end;

procedure TCustomListView.DestroyWnd;
var
  Old: Boolean;
begin
  { the item objects mirror the native state at all times: nothing to read back, nothing to report }
  Old := FNoEvents;
  FNoEvents := True;
  try
    inherited DestroyWnd;
  finally
    FNoEvents := Old;
  end;
end;

{ UIF-820: a text or image change of an item with a window }
procedure TCustomListView.ItemChanged(Item: TListItem; Change: TItemChange);
begin
  if not HandleAllocated or FNoEvents or (csDestroying in ComponentState) then
    Exit;
  SendMessageW(Handle, LVM_UPDATE, WPARAM(Item.Index), 0);
  if Assigned(FOnChange) then
    FOnChange(Self, Item, Change);
end;

{ UIF-822: the item objects follow the native state; the events for what changed }
procedure TCustomListView.SyncItemState(Index: Integer);
var
  Item: TListItem;
  S: UINT;
  Sel, Foc, Any: Boolean;
  Img: Integer;
begin
  if (Index < 0) or (Index >= FItems.Count) then
    Exit;
  Item := FItems[Index];
  S := UINT(SendMessageW(Handle, LVM_GETITEMSTATE, WPARAM(Index),
    LVIS_SELECTED or LVIS_FOCUSED or LVIS_STATEIMAGEMASK));
  Sel := (S and LVIS_SELECTED) <> 0;
  Foc := (S and LVIS_FOCUSED) <> 0;
  Img := Integer((S and LVIS_STATEIMAGEMASK) shr 12);
  Any := False;
  if Sel <> Item.FSelected then
  begin
    Item.FSelected := Sel;
    Any := True;
    if Assigned(FOnSelectItem) then
      FOnSelectItem(Self, Item, Sel);
  end;
  if (Img <> 0) and ((Img = 2) <> Item.FChecked) then
  begin
    Item.FChecked := Img = 2;
    Any := True;
    if Assigned(FOnItemChecked) then
      FOnItemChecked(Self, Item);
  end;
  if Foc <> Item.FFocused then
  begin
    Item.FFocused := Foc;
    Any := True;
  end;
  if Any and Assigned(FOnChange) then
    FOnChange(Self, Item, ctState);
end;

{ LVN_GETDISPINFOW: the caption, the sub-items and the image from the item objects }
procedure TCustomListView.GetDispInfo(var Info: TLVDispInfoW);
var
  Item: TListItem;
  S: string;
  N: Integer;
begin
  if (Info.item.iItem < 0) or (Info.item.iItem >= FItems.Count) then
    Exit;
  Item := FItems[Info.item.iItem];
  if ((Info.item.mask and LVIF_TEXT) <> 0) and (Info.item.pszText <> nil) and (Info.item.cchTextMax > 0) then
  begin
    if Info.item.iSubItem = 0 then
      S := Item.FCaption
    else if Info.item.iSubItem <= Item.FSubItems.Count then
      S := Item.FSubItems[Info.item.iSubItem - 1]
    else
      S := '';
    N := Length(S);
    if N > Info.item.cchTextMax - 1 then
      N := Info.item.cchTextMax - 1;
    if N > 0 then
      Move(PChar(S)^, Info.item.pszText^, N * SizeOf(Char));
    PChar(Info.item.pszText)[N] := #0;
  end;
  if ((Info.item.mask and LVIF_IMAGE) <> 0) and (Info.item.iSubItem = 0) then
    Info.item.iImage := Item.FImageIndex;
end;

function TCustomListView.GetSelected: TListItem;
var
  I: Integer;
begin
  Result := nil;
  for I := 0 to FItems.Count - 1 do
    if FItems[I].FSelected then
    begin
      Result := FItems[I];
      Exit;
    end;
end;

procedure TCustomListView.SetSelected(Value: TListItem);
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
    if (FItems[I] <> Value) and FItems[I].FSelected then
      FItems[I].Selected := False;
  if Value <> nil then
    Value.Selected := True;
end;

function TCustomListView.GetItemIndex: Integer;
var
  S: TListItem;
begin
  S := GetSelected;
  if S = nil then
    Result := -1
  else
    Result := S.Index;
end;

procedure TCustomListView.SetItemIndex(Value: Integer);
begin
  if (Value < 0) or (Value >= FItems.Count) then
    SetSelected(nil)
  else
    SetSelected(FItems[Value]);
end;

function TCustomListView.GetItemFocused: TListItem;
var
  I: Integer;
begin
  Result := nil;
  for I := 0 to FItems.Count - 1 do
    if FItems[I].FFocused then
    begin
      Result := FItems[I];
      Exit;
    end;
end;

procedure TCustomListView.SetItemFocused(Value: TListItem);
var
  Old: TListItem;
begin
  if Value <> nil then
    Value.Focused := True
  else
  begin
    Old := GetItemFocused;
    if Old <> nil then
      Old.Focused := False;
  end;
end;

function TCustomListView.GetSelCount: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to FItems.Count - 1 do
    if FItems[I].FSelected then
      Inc(Result);
end;

function TCustomListView.GetTopItem: TListItem;
var
  I: Integer;
begin
  Result := nil;
  if not HandleAllocated then
    Exit;
  I := Integer(SendMessageW(Handle, LVM_GETTOPINDEX, 0, 0));
  if (I >= 0) and (I < FItems.Count) then
    Result := FItems[I];
end;

function TCustomListView.GetVisibleRowCount: Integer;
begin
  if HandleAllocated then
    Result := Integer(SendMessageW(Handle, LVM_GETCOUNTPERPAGE, 0, 0))
  else
    Result := 0;
end;

function TCustomListView.GetItemAt(X, Y: Integer): TListItem;
var
  Info: TLVHitTestInfo;
  I: Integer;
begin
  Result := nil;
  if not HandleAllocated then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.pt := Point(X, Y);
  I := Integer(SendMessageW(Handle, LVM_HITTEST, 0, LPARAM(@Info)));
  if (I >= 0) and (I < FItems.Count) and ((Info.flags and LVHT_ONITEM) <> 0) then
    Result := FItems[I];
end;

function TCustomListView.GetNextItem(StartItem: TListItem; Direction: TSearchDirection;
  States: TItemStates): TListItem;
const
  Directions: array[TSearchDirection] of UINT = (LVNI_TOLEFT, LVNI_TORIGHT, LVNI_ABOVE, LVNI_BELOW, LVNI_ALL);
var
  Flags: UINT;
  Start, I: Integer;
begin
  Result := nil;
  if not HandleAllocated then
    Exit;
  Flags := Directions[Direction];
  if isCut in States then
    Flags := Flags or LVNI_CUT;
  if isDropHilited in States then
    Flags := Flags or LVNI_DROPHILITED;
  if isFocused in States then
    Flags := Flags or LVNI_FOCUSED;
  if isSelected in States then
    Flags := Flags or LVNI_SELECTED;
  if StartItem = nil then
    Start := -1
  else
    Start := StartItem.Index;
  I := Integer(SendMessageW(Handle, LVM_GETNEXTITEM, WPARAM(Start), LPARAM(Flags)));
  if (I >= 0) and (I < FItems.Count) then
    Result := FItems[I];
end;

procedure TCustomListView.ClearSelection;
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
    if FItems[I].FSelected then
      FItems[I].Selected := False;
end;

procedure TCustomListView.SelectAll;
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
    if not FItems[I].FSelected then
      FItems[I].Selected := True;
end;

procedure TCustomListView.Clear;
begin
  FItems.Clear;
end;


{ ---------------------------------------------------------------- TTreeNode }

constructor TTreeNode.Create(AOwner: TTreeNodes);
begin
  inherited Create;
  FOwner := AOwner;
  FImageIndex := -1;
  FSelectedIndex := -1;
end;

destructor TTreeNode.Destroy;
var
  TV: TCustomTreeView;
begin
  while (FChildren <> nil) and (FChildren.Count > 0) do
    TTreeNode(FChildren[0]).Free;
  if FOwner <> nil then
  begin
    TV := FOwner.FOwner;
    if TV.FSelectedNode = Self then
      TV.FSelectedNode := nil;
    if (FItemId <> 0) and TV.HandleAllocated then
      SendMessageW(TV.Handle, TVM_DELETEITEM, 0, LPARAM(FItemId));
    FItemId := 0;
    Siblings.Remove(Self);
    Dec(FOwner.FCount);
    if (FParent <> nil) and not FParent.HasChildren then
      FParent.UpdateChildren;
  end;
  FChildren.Free;
  inherited Destroy;
end;

procedure TTreeNode.Assign(Source: TPersistent);
begin
  if Source is TTreeNode then
  begin
    Text := TTreeNode(Source).FText;
    ImageIndex := TTreeNode(Source).FImageIndex;
    SelectedIndex := TTreeNode(Source).FSelectedIndex;
    Data := TTreeNode(Source).FData;
  end
  else
    inherited Assign(Source);
end;

function TTreeNode.Siblings: TList;
begin
  if FParent <> nil then
  begin
    if FParent.FChildren = nil then
      FParent.FChildren := TList.Create;
    Result := FParent.FChildren;
  end
  else
    Result := FOwner.FRoots;
end;

function TTreeNode.GetTreeView: TCustomTreeView;
begin
  Result := FOwner.FOwner;
end;

function TTreeNode.GetCount: Integer;
begin
  if FChildren = nil then
    Result := 0
  else
    Result := FChildren.Count;
end;

function TTreeNode.GetItem(Index: Integer): TTreeNode;
begin
  Result := TTreeNode(FChildren[Index]);
end;

function TTreeNode.GetHasChildren: Boolean;
begin
  Result := GetCount > 0;
end;

function TTreeNode.GetLevel: Integer;
var
  P: TTreeNode;
begin
  Result := 0;
  P := FParent;
  while P <> nil do
  begin
    Inc(Result);
    P := P.FParent;
  end;
end;

function TTreeNode.GetIndex: Integer;
begin
  Result := Siblings.IndexOf(Self);
end;

function TTreeNode.GetAbsoluteIndex: Integer;
var
  N: TTreeNode;
begin
  Result := 0;
  N := FOwner.GetFirstNode;
  while (N <> nil) and (N <> Self) do
  begin
    Inc(Result);
    N := N.GetNext;
  end;
end;

function TTreeNode.GetFirstChild: TTreeNode;
begin
  if HasChildren then
    Result := TTreeNode(FChildren[0])
  else
    Result := nil;
end;

function TTreeNode.GetLastChild: TTreeNode;
begin
  if HasChildren then
    Result := TTreeNode(FChildren[FChildren.Count - 1])
  else
    Result := nil;
end;

function TTreeNode.GetNextSibling: TTreeNode;
var
  L: TList;
  I: Integer;
begin
  L := Siblings;
  I := L.IndexOf(Self);
  if I < L.Count - 1 then
    Result := TTreeNode(L[I + 1])
  else
    Result := nil;
end;

function TTreeNode.GetPrevSibling: TTreeNode;
var
  L: TList;
  I: Integer;
begin
  L := Siblings;
  I := L.IndexOf(Self);
  if I > 0 then
    Result := TTreeNode(L[I - 1])
  else
    Result := nil;
end;

function TTreeNode.GetNext: TTreeNode;
var
  N: TTreeNode;
begin
  Result := GetFirstChild;
  if Result <> nil then
    Exit;
  N := Self;
  while N <> nil do
  begin
    Result := N.GetNextSibling;
    if Result <> nil then
      Exit;
    N := N.FParent;
  end;
end;

function TTreeNode.GetPrev: TTreeNode;
begin
  Result := GetPrevSibling;
  if Result = nil then
    Result := FParent
  else
    while Result.HasChildren do
      Result := Result.GetLastChild;
end;

function TTreeNode.GetExpanded: Boolean;
var
  TV: TCustomTreeView;
begin
  TV := TreeView;
  if (FItemId <> 0) and TV.HandleAllocated then
    Result := (SendMessageW(TV.Handle, TVM_GETITEMSTATE, WPARAM(FItemId), TVIS_EXPANDED) and TVIS_EXPANDED) <> 0
  else
    Result := FExpanded;
end;

procedure TTreeNode.SetExpanded(Value: Boolean);
const
  Codes: array[Boolean] of WPARAM = (TVE_COLLAPSE, TVE_EXPAND);
var
  TV: TCustomTreeView;
begin
  TV := TreeView;
  if (FItemId = 0) or not TV.HandleAllocated or not HasChildren then
  begin
    FExpanded := Value;
    Exit;
  end;
  { UIF-834: TVM_EXPAND sends no notifications; the events are fired here, only for a real change }
  if GetExpanded = Value then
    Exit;
  if not TV.CanExpandChange(Self, Value) then
    Exit;
  TV.FExpanding := True;
  try
    SendMessageW(TV.Handle, TVM_EXPAND, Codes[Value], LPARAM(FItemId));
  finally
    TV.FExpanding := False;
  end;
  FExpanded := Value;
  TV.ExpandChanged(Self, Value);
end;

function TTreeNode.GetSelected: Boolean;
begin
  Result := TreeView.Selected = Self;
end;

procedure TTreeNode.SetSelected(Value: Boolean);
begin
  if Value then
    TreeView.Selected := Self
  else if GetSelected then
    TreeView.Selected := nil;
end;

procedure TTreeNode.Refresh;
var
  It: TTVItemW;
  TV: TCustomTreeView;
begin
  TV := TreeView;
  if (FItemId = 0) or not TV.HandleAllocated then
    Exit;
  FillChar(It, SizeOf(It), 0);
  It.mask := TVIF_HANDLE or TVIF_TEXT or TVIF_IMAGE or TVIF_SELECTEDIMAGE;
  It.hItem := FItemId;
  It.pszText := LPSTR_TEXTCALLBACKW;
  It.iImage := I_IMAGECALLBACK;
  It.iSelectedImage := I_IMAGECALLBACK;
  SendMessageW(TV.Handle, TVM_SETITEMW, 0, LPARAM(@It));
end;

procedure TTreeNode.UpdateChildren;
var
  It: TTVItemW;
  TV: TCustomTreeView;
begin
  TV := TreeView;
  if (FItemId = 0) or not TV.HandleAllocated then
    Exit;
  FillChar(It, SizeOf(It), 0);
  It.mask := TVIF_HANDLE or TVIF_CHILDREN;
  It.hItem := FItemId;
  It.cChildren := Ord(HasChildren);
  SendMessageW(TV.Handle, TVM_SETITEMW, 0, LPARAM(@It));
end;

procedure TTreeNode.SetText(const Value: string);
begin
  if FText <> Value then
  begin
    FText := Value;
    Refresh;
  end;
end;

procedure TTreeNode.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    Refresh;
  end;
end;

procedure TTreeNode.SetSelectedIndex(Value: Integer);
begin
  if FSelectedIndex <> Value then
  begin
    FSelectedIndex := Value;
    Refresh;
  end;
end;

procedure TTreeNode.Delete;
begin
  Free;
end;

procedure TTreeNode.DeleteChildren;
begin
  while HasChildren do
    TTreeNode(FChildren[0]).Free;
end;

procedure TTreeNode.Expand(Recurse: Boolean);
var
  I: Integer;
begin
  Expanded := True;
  if Recurse then
    for I := 0 to Count - 1 do
      Item[I].Expand(True);
end;

procedure TTreeNode.Collapse(Recurse: Boolean);
var
  I: Integer;
begin
  if Recurse then
    for I := 0 to Count - 1 do
      Item[I].Collapse(True);
  Expanded := False;
end;

procedure TTreeNode.MakeVisible;
begin
  if (FItemId <> 0) and TreeView.HandleAllocated then
    SendMessageW(TreeView.Handle, TVM_ENSUREVISIBLE, 0, LPARAM(FItemId));
end;

procedure TTreeNode.EditText;
begin
  if (FItemId <> 0) and TreeView.HandleAllocated then
    SendMessageW(TreeView.Handle, TVM_EDITLABELW, 0, LPARAM(FItemId));
end;

function TTreeNode.DisplayRect(TextOnly: Boolean): TRect;
begin
  Result := Rect(0, 0, 0, 0);
  if (FItemId = 0) or not TreeView.HandleAllocated then
    Exit;
  { the item handle goes in the first bytes of the rectangle }
  PNativeUInt(@Result)^ := NativeUInt(FItemId);
  if SendMessageW(TreeView.Handle, TVM_GETITEMRECT, WPARAM(Ord(TextOnly)), LPARAM(@Result)) = 0 then
    Result := Rect(0, 0, 0, 0);
end;

{ ---------------------------------------------------------------- TTreeNodes }

constructor TTreeNodes.Create(AOwner: TCustomTreeView);
begin
  inherited Create;
  FOwner := AOwner;
  FRoots := TList.Create;
end;

destructor TTreeNodes.Destroy;
begin
  Clear;
  FRoots.Free;
  inherited Destroy;
end;

function TTreeNodes.GetFirstNode: TTreeNode;
begin
  if FRoots.Count > 0 then
    Result := TTreeNode(FRoots[0])
  else
    Result := nil;
end;

function TTreeNodes.GetItem(Index: Integer): TTreeNode;
begin
  if (Index < 0) or (Index >= FCount) then
    raise EListError.CreateFmt(SListIndexError, [Index]);
  Result := GetFirstNode;
  while Index > 0 do
  begin
    Result := Result.GetNext;
    Dec(Index);
  end;
end;

function TTreeNodes.InternalAdd(Parent: TTreeNode; Index: Integer; const S: string): TTreeNode;
begin
  Result := TTreeNode.Create(Self);
  Result.FParent := Parent;
  Result.FText := S;
  Result.Siblings.Insert(Index, Result);
  Inc(FCount);
  if FOwner.HandleAllocated and ((Parent = nil) or (Parent.FItemId <> 0)) then
  begin
    InsertNative(Result);
    if Parent <> nil then
      Parent.UpdateChildren;
  end;
end;

procedure TTreeNodes.InsertNative(Node: TTreeNode);
var
  S: TTVInsertStructW;
  Prev: TTreeNode;
  I: Integer;
begin
  FillChar(S, SizeOf(S), 0);
  if Node.FParent <> nil then
    S.hParent := Node.FParent.FItemId
  else
    S.hParent := TVI_ROOT;
  Prev := Node.GetPrevSibling;
  if Prev <> nil then
    S.hInsertAfter := Prev.FItemId
  else
    S.hInsertAfter := TVI_FIRST;
  S.item.mask := TVIF_TEXT or TVIF_IMAGE or TVIF_SELECTEDIMAGE or TVIF_PARAM or TVIF_CHILDREN;
  S.item.pszText := LPSTR_TEXTCALLBACKW;
  S.item.iImage := I_IMAGECALLBACK;
  S.item.iSelectedImage := I_IMAGECALLBACK;
  S.item.cChildren := Ord(Node.HasChildren);
  S.item.lParam := LPARAM(Pointer(Node));
  Node.FItemId := HTREEITEM(SendMessageW(FOwner.Handle, TVM_INSERTITEMW, 0, LPARAM(@S)));
  for I := 0 to Node.Count - 1 do
    InsertNative(Node.Item[I]);
end;

function TTreeNodes.Add(Sibling: TTreeNode; const S: string): TTreeNode;
begin
  if Sibling = nil then
    Result := InternalAdd(nil, FRoots.Count, S)
  else
    Result := InternalAdd(Sibling.FParent, Sibling.Siblings.Count, S);
end;

function TTreeNodes.AddFirst(Sibling: TTreeNode; const S: string): TTreeNode;
begin
  if Sibling = nil then
    Result := InternalAdd(nil, 0, S)
  else
    Result := InternalAdd(Sibling.FParent, 0, S);
end;

function TTreeNodes.AddChild(Parent: TTreeNode; const S: string): TTreeNode;
begin
  if Parent = nil then
    Result := InternalAdd(nil, FRoots.Count, S)
  else
    Result := InternalAdd(Parent, Parent.Count, S);
end;

function TTreeNodes.AddChildFirst(Parent: TTreeNode; const S: string): TTreeNode;
begin
  Result := InternalAdd(Parent, 0, S);
end;

function TTreeNodes.Insert(Sibling: TTreeNode; const S: string): TTreeNode;
begin
  if Sibling = nil then
    Result := InternalAdd(nil, FRoots.Count, S)
  else
    Result := InternalAdd(Sibling.FParent, Sibling.Index, S);
end;

procedure TTreeNodes.Delete(Node: TTreeNode);
begin
  Node.Free;
end;

procedure TTreeNodes.Clear;
begin
  while FRoots.Count > 0 do
    TTreeNode(FRoots[0]).Free;
end;

procedure TTreeNodes.BeginUpdate;
begin
  if (FUpdateCount = 0) and FOwner.HandleAllocated then
    SendMessageW(FOwner.Handle, WM_SETREDRAW, 0, 0);
  Inc(FUpdateCount);
end;

procedure TTreeNodes.EndUpdate;
begin
  Dec(FUpdateCount);
  if (FUpdateCount = 0) and FOwner.HandleAllocated then
  begin
    SendMessageW(FOwner.Handle, WM_SETREDRAW, 1, 0);
    FOwner.Invalidate;
  end;
end;

{ ---------------------------------------------------------------- TCustomTreeView }

constructor TCustomTreeView.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle - [csAcceptsControls, csSetCaption];
  FItems := TTreeNodes.Create(Self);
  FBorderStyle := bsSingle;
  FHideSelection := True;
  FIndent := 19;
  FShowButtons := True;
  FShowLines := True;
  FShowRoot := True;
  FImageChangeLink := TChangeLink.Create;
  FImageChangeLink.OnChange := ImageListChange;
  TabStop := True;
  Color := clWindow;
  SetBounds(Left, Top, 121, 97);
end;

destructor TCustomTreeView.Destroy;
begin
  { UIF-833 }
  FNoEvents := True;
  Images := nil;
  FItems.Clear;
  inherited Destroy;
  FItems.Free;
  FItems := nil;
  FImageChangeLink.Free;
  FImageChangeLink := nil;
end;

procedure TCustomTreeView.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := WC_TREEVIEW;
  if FShowButtons then
    Params.Style := Params.Style or TVS_HASBUTTONS;
  if FShowLines then
    Params.Style := Params.Style or TVS_HASLINES;
  if FShowRoot then
    Params.Style := Params.Style or TVS_LINESATROOT;
  if not FHideSelection then
    Params.Style := Params.Style or TVS_SHOWSELALWAYS;
  if not FReadOnly then
    Params.Style := Params.Style or TVS_EDITLABELS;
  if FRowSelect then
    Params.Style := Params.Style or TVS_FULLROWSELECT;
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
end;

procedure TCustomTreeView.CreateWnd;
var
  I: Integer;
  N: TTreeNode;
  Old: Boolean;
begin
  inherited CreateWnd;
  UpdateColors;
  UpdateImages;
  SendMessageW(Handle, TVM_SETINDENT, WPARAM(FIndent), 0);
  Old := FNoEvents;
  FNoEvents := True;
  try
    for I := 0 to FItems.FRoots.Count - 1 do
      FItems.InsertNative(TTreeNode(FItems.FRoots[I]));
    N := FItems.GetFirstNode;
    while N <> nil do
    begin
      if N.FExpanded and N.HasChildren then
        SendMessageW(Handle, TVM_EXPAND, TVE_EXPAND, LPARAM(N.FItemId));
      N := N.GetNext;
    end;
    if FSelectedNode <> nil then
      SendMessageW(Handle, TVM_SELECTITEM, TVGN_CARET, LPARAM(FSelectedNode.FItemId));
  finally
    FNoEvents := Old;
  end;
end;

procedure TCustomTreeView.SaveState(Node: TTreeNode);
begin
  while Node <> nil do
  begin
    Node.FExpanded := Node.GetExpanded;
    Node := Node.GetNext;
  end;
end;

procedure TCustomTreeView.ClearIds(Node: TTreeNode);
begin
  while Node <> nil do
  begin
    Node.FItemId := 0;
    Node := Node.GetNext;
  end;
end;

procedure TCustomTreeView.DestroyWnd;
var
  Old: Boolean;
begin
  SaveState(FItems.GetFirstNode);
  FSelectedNode := GetSelected;
  Old := FNoEvents;
  FNoEvents := True;
  try
    inherited DestroyWnd;
  finally
    FNoEvents := Old;
  end;
  ClearIds(FItems.GetFirstNode);
end;

procedure TCustomTreeView.UpdateColors;
begin
  if not HandleAllocated then
    Exit;
  SendMessageW(Handle, TVM_SETBKCOLOR, 0, LPARAM(ColorToRGB(Color)));
  SendMessageW(Handle, TVM_SETTEXTCOLOR, 0, LPARAM(ColorToRGB(Font.Color)));
end;

procedure TCustomTreeView.UpdateImages;
var
  H: HIMAGELIST;
begin
  if not HandleAllocated then
    Exit;
  H := 0;
  if FImages <> nil then
    H := FImages.Handle;
  SendMessageW(Handle, TVM_SETIMAGELIST, TVSIL_NORMAL, LPARAM(H));
end;

procedure TCustomTreeView.CMColorChanged(var Message: TMessage);
begin
  inherited CMColorChanged(Message);
  UpdateColors;
end;

procedure TCustomTreeView.CMFontChanged(var Message: TMessage);
begin
  inherited CMFontChanged(Message);
  UpdateColors;
end;

procedure TCustomTreeView.ChangeScale(M, D: Integer);
begin
  inherited ChangeScale(M, D);
  if M <> D then
    Indent := LayoutMulDiv(FIndent, M, D);
end;

procedure TCustomTreeView.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FImages) then
    Images := nil;
end;

procedure TCustomTreeView.ImageListChange(Sender: TObject);
begin
  UpdateImages;
end;

procedure TCustomTreeView.SetImages(Value: TCustomImageList);
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
  UpdateImages;
end;

function TCustomTreeView.NodeOf(Item: HTREEITEM): TTreeNode;
var
  It: TTVItemW;
begin
  Result := nil;
  if (Item = 0) or not HandleAllocated then
    Exit;
  FillChar(It, SizeOf(It), 0);
  It.mask := TVIF_HANDLE or TVIF_PARAM;
  It.hItem := Item;
  if SendMessageW(Handle, TVM_GETITEMW, 0, LPARAM(@It)) <> 0 then
    Result := TTreeNode(Pointer(It.lParam));
end;

{ TVN_GETDISPINFOW: the text and the images from the node }
procedure TCustomTreeView.GetDispInfo(var Info: TTVDispInfoW);
var
  Node: TTreeNode;
  N: Integer;
begin
  Node := TTreeNode(Pointer(Info.item.lParam));
  if Node = nil then
    Node := NodeOf(Info.item.hItem);
  if Node = nil then
    Exit;
  if ((Info.item.mask and TVIF_TEXT) <> 0) and (Info.item.pszText <> nil) and (Info.item.cchTextMax > 0) then
  begin
    N := Length(Node.FText);
    if N > Info.item.cchTextMax - 1 then
      N := Info.item.cchTextMax - 1;
    if N > 0 then
      Move(PChar(Node.FText)^, Info.item.pszText^, N * SizeOf(Char));
    PChar(Info.item.pszText)[N] := #0;
  end;
  if (Info.item.mask and TVIF_IMAGE) <> 0 then
    Info.item.iImage := Node.FImageIndex;
  if (Info.item.mask and TVIF_SELECTEDIMAGE) <> 0 then
    if Node.FSelectedIndex = -1 then
      Info.item.iSelectedImage := Node.FImageIndex
    else
      Info.item.iSelectedImage := Node.FSelectedIndex;
end;

procedure TCustomTreeView.WndProc(var Message: TMessage);
var
  NM: PNMTreeViewW;
  D: PTVDispInfoW;
  Node: TTreeNode;
  Allow: Boolean;
  S: string;
begin
  if Message.Msg <> CN_NOTIFY then
  begin
    inherited WndProc(Message);
    Exit;
  end;
  NM := PNMTreeViewW(Message.LParam);
  D := PTVDispInfoW(Message.LParam);
  Message.Result := 0;
  case NM^.hdr.code of
    TVN_GETDISPINFOW:
      GetDispInfo(D^);
    TVN_SELCHANGINGW:
      if not FNoEvents and not (csDestroying in ComponentState) and Assigned(FOnChanging) then
      begin
        Allow := True;
        FOnChanging(Self, TTreeNode(Pointer(NM^.itemNew.lParam)), Allow);
        Message.Result := Ord(not Allow);
      end;
    TVN_SELCHANGEDW:
      if not FNoEvents and not (csDestroying in ComponentState) and Assigned(FOnChange) then
        FOnChange(Self, TTreeNode(Pointer(NM^.itemNew.lParam)));
    TVN_ITEMEXPANDINGW:
      begin
        Node := TTreeNode(Pointer(NM^.itemNew.lParam));
        if (Node <> nil) and not FExpanding and ((NM^.action and TVE_TOGGLE) in [TVE_COLLAPSE, TVE_EXPAND])
          and not CanExpandChange(Node, (NM^.action and TVE_TOGGLE) = TVE_EXPAND) then
          Message.Result := 1;
      end;
    TVN_ITEMEXPANDEDW:
      begin
        Node := TTreeNode(Pointer(NM^.itemNew.lParam));
        if (Node <> nil) and ((NM^.action and TVE_TOGGLE) in [TVE_COLLAPSE, TVE_EXPAND]) then
        begin
          Node.FExpanded := (NM^.action and TVE_TOGGLE) = TVE_EXPAND;
          if not FExpanding then
            ExpandChanged(Node, Node.FExpanded);
        end;
      end;
    TVN_BEGINLABELEDITW:
      begin
        Node := TTreeNode(Pointer(D^.item.lParam));
        if (Node <> nil) and not FNoEvents and Assigned(FOnEditing) then
        begin
          Allow := True;
          FOnEditing(Self, Node, Allow);
          Message.Result := Ord(not Allow);
        end;
      end;
    TVN_ENDLABELEDITW:
      begin
        { the text stays the callback: the node takes the edited text, result 0 }
        Node := TTreeNode(Pointer(D^.item.lParam));
        if (Node <> nil) and (D^.item.pszText <> nil) then
        begin
          S := PChar(D^.item.pszText);
          if Assigned(FOnEdited) then
            FOnEdited(Self, Node, S);
          Node.Text := S;
        end;
      end;
  else
    inherited WndProc(Message);
  end;
end;

function TCustomTreeView.CanExpandChange(Node: TTreeNode; Expand: Boolean): Boolean;
begin
  Result := True;
  if FNoEvents or (csDestroying in ComponentState) then
    Exit;
  if Expand then
  begin
    if Assigned(FOnExpanding) then
      FOnExpanding(Self, Node, Result);
  end
  else if Assigned(FOnCollapsing) then
    FOnCollapsing(Self, Node, Result);
end;

procedure TCustomTreeView.ExpandChanged(Node: TTreeNode; Expand: Boolean);
begin
  if FNoEvents or (csDestroying in ComponentState) then
    Exit;
  if Expand then
  begin
    if Assigned(FOnExpanded) then
      FOnExpanded(Self, Node);
  end
  else if Assigned(FOnCollapsed) then
    FOnCollapsed(Self, Node);
end;

function TCustomTreeView.IsEditing: Boolean;
begin
  Result := HandleAllocated and (SendMessageW(Handle, TVM_GETEDITCONTROL, 0, 0) <> 0);
end;

function TCustomTreeView.GetSelected: TTreeNode;
begin
  if HandleAllocated then
    Result := NodeOf(HTREEITEM(SendMessageW(Handle, TVM_GETNEXTITEM, TVGN_CARET, 0)))
  else
    Result := FSelectedNode;
end;

procedure TCustomTreeView.SetSelected(Value: TTreeNode);
var
  H: HTREEITEM;
begin
  if not HandleAllocated then
    FSelectedNode := Value
  else
  begin
    H := 0;
    if Value <> nil then
      H := Value.FItemId;
    SendMessageW(Handle, TVM_SELECTITEM, TVGN_CARET, LPARAM(H));
  end;
end;

function TCustomTreeView.GetTopItem: TTreeNode;
begin
  if HandleAllocated then
    Result := NodeOf(HTREEITEM(SendMessageW(Handle, TVM_GETNEXTITEM, TVGN_FIRSTVISIBLE, 0)))
  else
    Result := nil;
end;

function TCustomTreeView.GetNodeAt(X, Y: Integer): TTreeNode;
var
  Info: TTVHitTestInfo;
begin
  Result := nil;
  if not HandleAllocated then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.pt := Point(X, Y);
  SendMessageW(Handle, TVM_HITTEST, 0, LPARAM(@Info));
  if (Info.flags and TVHT_ONITEM) <> 0 then
    Result := NodeOf(Info.hItem);
end;

procedure TCustomTreeView.FullExpand;
var
  I: Integer;
begin
  for I := 0 to FItems.FRoots.Count - 1 do
    TTreeNode(FItems.FRoots[I]).Expand(True);
end;

procedure TCustomTreeView.FullCollapse;
var
  I: Integer;
begin
  for I := 0 to FItems.FRoots.Count - 1 do
    TTreeNode(FItems.FRoots[I]).Collapse(True);
end;

procedure TCustomTreeView.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetHideSelection(Value: Boolean);
begin
  if FHideSelection <> Value then
  begin
    FHideSelection := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetReadOnly(Value: Boolean);
begin
  if FReadOnly <> Value then
  begin
    FReadOnly := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetRowSelect(Value: Boolean);
begin
  if FRowSelect <> Value then
  begin
    FRowSelect := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetShowButtons(Value: Boolean);
begin
  if FShowButtons <> Value then
  begin
    FShowButtons := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetShowLines(Value: Boolean);
begin
  if FShowLines <> Value then
  begin
    FShowLines := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetShowRoot(Value: Boolean);
begin
  if FShowRoot <> Value then
  begin
    FShowRoot := Value;
    RecreateWnd;
  end;
end;

procedure TCustomTreeView.SetIndent(Value: Integer);
begin
  if FIndent <> Value then
  begin
    FIndent := Value;
    if HandleAllocated then
      SendMessageW(Handle, TVM_SETINDENT, WPARAM(FIndent), 0);
  end;
end;

{ ---------------------------------------------------------------- rich text editor (rich-edit.md) }

const
  CodePageUtf16 = 1200;
  NoTextLimit = $7FFFFFF0;                                   { UIF-1310 }
  RichEditEvents = ENM_CHANGE or ENM_SELCHANGE or ENM_LINK or ENM_PROTECTED;
  CharStyleMasks: array[TFontStyle] of DWORD = (CFM_BOLD, CFM_ITALIC, CFM_UNDERLINE, CFM_STRIKEOUT);

type
  { what the stream callbacks of one EM_STREAMIN / EM_STREAMOUT share with their caller (UIF-1329) }
  PStreamCookie = ^TStreamCookie;
  TStreamCookie = record
    Stream: TStream;
    Failure: TObject;                                        { the exception a callback caught }
  end;

var
  RichEditLibrary: HMODULE;                                  { UIF-1300: loaded once, released in the finalization }
  RichEditWindows: Integer;                                  { windows of the engine that exist }

{ UIF-1300, UIF-1301 }
procedure LoadRichEditLibrary(const Name: string);
var
  Code: DWORD;
  E: EOSError;
begin
  if RichEditLibrary <> 0 then
    Exit;
  RichEditLibrary := LoadLibraryExW(PChar(Name), 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
  if RichEditLibrary = 0 then
  begin
    Code := GetLastError;
    E := EOSError.Create(SRichEditLibrary + ': ' + SysErrorMessage(Code));
    E.ErrorCode := Code;
    raise E;
  end;
end;

{ UIF-1329: an exception never crosses the system call }
function StreamReadCallback(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;
var
  C: PStreamCookie;
begin
  C := PStreamCookie(dwCookie);
  Result := 0;
  pcb := 0;
  try
    pcb := C^.Stream.Read(pbBuff^, cb);
  except
    C^.Failure := TObject(AcquireExceptionObject);
    Result := 1;
  end;
end;

function StreamWriteCallback(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;
var
  C: PStreamCookie;
begin
  C := PStreamCookie(dwCookie);
  Result := 0;
  pcb := 0;
  try
    pcb := C^.Stream.Write(pbBuff^, cb);
  except
    C^.Failure := TObject(AcquireExceptionObject);
    Result := 1;
  end;
end;

{ the text of TStrings for a control text: a trailing line break added (RTL-420) }
function RichLinesText(const S: string): string;
begin
  if S = '' then
    Result := ''
  else
    Result := S + #13#10;
end;

{ paragraphs of an engine text (separated by one CR): the empty text has none }
function ParagraphCount(const S: string): Integer;
var
  I: Integer;
begin
  if S = '' then
  begin
    Result := 0;
    Exit;
  end;
  Result := 1;
  for I := 1 to Length(S) do
    if S[I] = #13 then
      Inc(Result);
end;

{ engine position of the first character of paragraph Index (0 <= Index < ParagraphCount(S)) }
function ParagraphStart(const S: string; Index: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  I := 1;
  while (Index > 0) and (I <= Length(S)) do
  begin
    if S[I] = #13 then
    begin
      Dec(Index);
      Result := I;
    end;
    Inc(I);
  end;
end;

{ engine position just after the last character of paragraph Index (before its CR) }
function ParagraphEnd(const S: string; Index: Integer): Integer;
var
  I: Integer;
begin
  I := ParagraphStart(S, Index) + 1;
  while (I <= Length(S)) and (S[I] <> #13) do
    Inc(I);
  Result := I - 1;
end;

{ ---------------------------------------------------------------- TTextAttributes }

{ the character format of the attribute's scope; False without a window (Format is then empty). UIF-1322: for a
  mixed selection the engine reports the values at the end of the selection, so the values are read again from
  the first character; dwMask stays the one of the whole selection }
function ReadCharFormat(A: TTextAttributes; var Format: TCharFormat2W): Boolean;
const
  Members = CFM_BOLD or CFM_ITALIC or CFM_UNDERLINE or CFM_STRIKEOUT or CFM_PROTECTED or CFM_SIZE or CFM_COLOR
    or CFM_FACE;
var
  H: HWND;
  Sel, First: TCharRange;
  Mask: DWORD;
begin
  FillChar(Format, SizeOf(Format), 0);
  Format.cbSize := SizeOf(Format);
  Result := A.FRichEdit.HandleAllocated;
  if not Result then
    Exit;
  H := A.FRichEdit.Handle;
  if A.FType = atDefaultText then
  begin
    SendMessageW(H, EM_GETCHARFORMAT, SCF_DEFAULT, LPARAM(@Format));
    Exit;
  end;
  SendMessageW(H, EM_GETCHARFORMAT, SCF_SELECTION, LPARAM(@Format));
  if (Format.dwMask and Members) = Members then
    Exit;
  Mask := Format.dwMask;
  SendMessageW(H, EM_EXGETSEL, 0, LPARAM(@Sel));
  First.cpMin := Sel.cpMin;
  First.cpMax := Sel.cpMin + 1;
  Inc(A.FRichEdit.FUpdating);                                { no OnSelectionChange for the two moves }
  try
    SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@First));
    SendMessageW(H, EM_GETCHARFORMAT, SCF_SELECTION, LPARAM(@Format));
    SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@Sel));
  finally
    Dec(A.FRichEdit.FUpdating);
  end;
  Format.dwMask := Mask;
end;

{ sends the members named by Mask; never called without a window }
procedure WriteCharFormat(A: TTextAttributes; var Format: TCharFormat2W; Mask: DWORD);
begin
  Format.cbSize := SizeOf(Format);
  Format.dwMask := Mask;
  if A.FType = atSelected then
    SendMessageW(A.FRichEdit.Handle, EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@Format))
  else
    SendMessageW(A.FRichEdit.Handle, EM_SETCHARFORMAT, SCF_DEFAULT, LPARAM(@Format));
end;

{ UIF-1321: the members of a character format that a TFont carries; returns their mask }
function FontToCharFormat(Font: TFont; var Format: TCharFormat2W): DWORD;
var
  S: string;
  I: Integer;
  St: TFontStyle;
begin
  FillChar(Format, SizeOf(Format), 0);
  Format.cbSize := SizeOf(Format);
  S := Font.Name;
  if Length(S) > LF_FACESIZE - 1 then
    SetLength(S, LF_FACESIZE - 1);
  for I := 1 to Length(S) do
    Format.szFaceName[I - 1] := S[I];
  Format.yHeight := Font.Size * 20;
  for St := Low(TFontStyle) to High(TFontStyle) do
    if St in Font.Style then
      Format.dwEffects := Format.dwEffects or CharStyleMasks[St];
  if (Font.Color = clWindowText) or (Font.Color = clDefault) then
    Format.dwEffects := Format.dwEffects or CFE_AUTOCOLOR
  else
    Format.crTextColor := COLORREF(ColorToRGB(Font.Color));
  Result := CFM_FACE or CFM_SIZE or CFM_BOLD or CFM_ITALIC or CFM_UNDERLINE or CFM_STRIKEOUT or CFM_COLOR;
end;

constructor TTextAttributes.Create(AOwner: TCustomRichEdit; AttributeType: TAttributeType);
begin
  inherited Create;
  FRichEdit := AOwner;
  FType := AttributeType;
end;

function TTextAttributes.GetColor: TColor;
var
  F: TCharFormat2W;
begin
  if not ReadCharFormat(Self, F) then
    Result := FRichEdit.Font.Color
  else if (F.dwEffects and CFE_AUTOCOLOR) <> 0 then
    Result := clWindowText
  else
    Result := TColor(F.crTextColor);
end;

procedure TTextAttributes.SetColor(Value: TColor);
var
  F: TCharFormat2W;
begin
  if not FRichEdit.HandleAllocated then
  begin
    if FType = atDefaultText then
      FRichEdit.Font.Color := Value;
    Exit;
  end;
  FillChar(F, SizeOf(F), 0);
  if (Value = clWindowText) or (Value = clDefault) then
    F.dwEffects := CFE_AUTOCOLOR
  else
    F.crTextColor := COLORREF(ColorToRGB(Value));
  WriteCharFormat(Self, F, CFM_COLOR);
end;

{ UIF-1322 }
function TTextAttributes.GetConsistentAttributes: TConsistentAttributes;
var
  F: TCharFormat2W;
begin
  Result := [caBold, caColor, caFace, caItalic, caSize, caStrikeOut, caUnderline, caProtected];
  if (FType = atDefaultText) or not ReadCharFormat(Self, F) then
    Exit;
  Result := [];
  if (F.dwMask and CFM_BOLD) <> 0 then
    Include(Result, caBold);
  if (F.dwMask and CFM_COLOR) <> 0 then
    Include(Result, caColor);
  if (F.dwMask and CFM_FACE) <> 0 then
    Include(Result, caFace);
  if (F.dwMask and CFM_ITALIC) <> 0 then
    Include(Result, caItalic);
  if (F.dwMask and CFM_SIZE) <> 0 then
    Include(Result, caSize);
  if (F.dwMask and CFM_STRIKEOUT) <> 0 then
    Include(Result, caStrikeOut);
  if (F.dwMask and CFM_UNDERLINE) <> 0 then
    Include(Result, caUnderline);
  if (F.dwMask and CFM_PROTECTED) <> 0 then
    Include(Result, caProtected);
end;

function TTextAttributes.GetName: string;
var
  F: TCharFormat2W;
begin
  if ReadCharFormat(Self, F) then
    Result := PChar(@F.szFaceName[0])
  else
    Result := FRichEdit.Font.Name;
end;

procedure TTextAttributes.SetName(const Value: string);
var
  F: TCharFormat2W;
  I, N: Integer;
begin
  if not FRichEdit.HandleAllocated then
  begin
    if FType = atDefaultText then
      FRichEdit.Font.Name := Value;
    Exit;
  end;
  FillChar(F, SizeOf(F), 0);
  N := Length(Value);
  if N > LF_FACESIZE - 1 then
    N := LF_FACESIZE - 1;
  for I := 1 to N do
    F.szFaceName[I - 1] := Value[I];
  WriteCharFormat(Self, F, CFM_FACE);
end;

function TTextAttributes.GetProtected: Boolean;
var
  F: TCharFormat2W;
begin
  Result := ReadCharFormat(Self, F) and ((F.dwEffects and CFE_PROTECTED) <> 0);
end;

procedure TTextAttributes.SetProtected(Value: Boolean);
var
  F: TCharFormat2W;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  if Value then
    F.dwEffects := CFE_PROTECTED;
  WriteCharFormat(Self, F, CFM_PROTECTED);
end;

function TTextAttributes.GetSize: Integer;
var
  F: TCharFormat2W;
begin
  if ReadCharFormat(Self, F) then
    Result := (F.yHeight + 10) div 20
  else
    Result := FRichEdit.Font.Size;
end;

procedure TTextAttributes.SetSize(Value: Integer);
var
  F: TCharFormat2W;
begin
  if not FRichEdit.HandleAllocated then
  begin
    if FType = atDefaultText then
      FRichEdit.Font.Size := Value;
    Exit;
  end;
  FillChar(F, SizeOf(F), 0);
  F.yHeight := Value * 20;
  WriteCharFormat(Self, F, CFM_SIZE);
end;

function TTextAttributes.GetStyle: TFontStyles;
var
  F: TCharFormat2W;
  St: TFontStyle;
begin
  if not ReadCharFormat(Self, F) then
  begin
    Result := FRichEdit.Font.Style;
    Exit;
  end;
  Result := [];
  for St := Low(TFontStyle) to High(TFontStyle) do
    if (F.dwEffects and CharStyleMasks[St]) <> 0 then
      Include(Result, St);
end;

procedure TTextAttributes.SetStyle(Value: TFontStyles);
var
  F: TCharFormat2W;
  St: TFontStyle;
begin
  if not FRichEdit.HandleAllocated then
  begin
    if FType = atDefaultText then
      FRichEdit.Font.Style := Value;
    Exit;
  end;
  FillChar(F, SizeOf(F), 0);
  for St := Low(TFontStyle) to High(TFontStyle) do
    if St in Value then
      F.dwEffects := F.dwEffects or CharStyleMasks[St];
  WriteCharFormat(Self, F, CFM_BOLD or CFM_ITALIC or CFM_UNDERLINE or CFM_STRIKEOUT);
end;

procedure TTextAttributes.Assign(Source: TPersistent);
var
  F: TCharFormat2W;
  Mask: DWORD;
begin
  if Source is TTextAttributes then
  begin
    Color := TTextAttributes(Source).Color;
    Name := TTextAttributes(Source).Name;
    Size := TTextAttributes(Source).Size;
    Style := TTextAttributes(Source).Style;
    Protected := TTextAttributes(Source).Protected;
  end
  else if Source is TFont then
  begin
    if FRichEdit.HandleAllocated then
    begin
      Mask := FontToCharFormat(TFont(Source), F);
      WriteCharFormat(Self, F, Mask);
    end
    else if (FType = atDefaultText) and (Source <> FRichEdit.Font) then
      FRichEdit.Font.Assign(Source);
  end
  else
    inherited Assign(Source);
end;

procedure TTextAttributes.AssignTo(Dest: TPersistent);
begin
  if Dest is TFont then
  begin
    TFont(Dest).Color := Color;
    TFont(Dest).Name := Name;
    TFont(Dest).Size := Size;
    TFont(Dest).Style := Style;
  end
  else
    inherited AssignTo(Dest);
end;

{ ---------------------------------------------------------------- TParaAttributes }

{ the paragraph format of the selection; False without a window (Format is then empty) }
function ReadParaFormat(A: TParaAttributes; var Format: TParaFormat2): Boolean;
begin
  FillChar(Format, SizeOf(Format), 0);
  Format.cbSize := SizeOf(Format);
  Result := A.FRichEdit.HandleAllocated;
  if Result then
    SendMessageW(A.FRichEdit.Handle, EM_GETPARAFORMAT, 0, LPARAM(@Format));
end;

{ sends the members named by Mask; never called without a window }
procedure WriteParaFormat(A: TParaAttributes; var Format: TParaFormat2; Mask: DWORD);
begin
  Format.cbSize := SizeOf(Format);
  Format.dwMask := Mask;
  SendMessageW(A.FRichEdit.Handle, EM_SETPARAFORMAT, 0, LPARAM(@Format));
end;

constructor TParaAttributes.Create(AOwner: TCustomRichEdit);
begin
  inherited Create;
  FRichEdit := AOwner;
end;

function TParaAttributes.GetAlignment: TAlignment;
var
  F: TParaFormat2;
begin
  Result := taLeftJustify;
  if ReadParaFormat(Self, F) then
    case F.wAlignment of
      PFA_RIGHT: Result := taRightJustify;
      PFA_CENTER: Result := taCenter;
    end;
end;

procedure TParaAttributes.SetAlignment(Value: TAlignment);
const
  Alignments: array[TAlignment] of Word = (PFA_LEFT, PFA_RIGHT, PFA_CENTER);
var
  F: TParaFormat2;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  F.wAlignment := Alignments[Value];
  WriteParaFormat(Self, F, PFM_ALIGNMENT);
end;

function TParaAttributes.GetFirstIndent: Integer;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  Result := F.dxStartIndent div 20;
end;

procedure TParaAttributes.SetFirstIndent(Value: Integer);
var
  F: TParaFormat2;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  F.dxStartIndent := Value * 20;
  WriteParaFormat(Self, F, PFM_STARTINDENT);
end;

function TParaAttributes.GetLeftIndent: Integer;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  Result := F.dxOffset div 20;
end;

procedure TParaAttributes.SetLeftIndent(Value: Integer);
var
  F: TParaFormat2;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  F.dxOffset := Value * 20;
  WriteParaFormat(Self, F, PFM_OFFSET);
end;

function TParaAttributes.GetNumbering: TNumberingStyle;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  if F.wNumbering = 0 then
    Result := nsNone
  else
    Result := nsBullet;
end;

procedure TParaAttributes.SetNumbering(Value: TNumberingStyle);
var
  F: TParaFormat2;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  if Value = nsBullet then
    F.wNumbering := PFN_BULLET;
  WriteParaFormat(Self, F, PFM_NUMBERING);
end;

function TParaAttributes.GetRightIndent: Integer;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  Result := F.dxRightIndent div 20;
end;

procedure TParaAttributes.SetRightIndent(Value: Integer);
var
  F: TParaFormat2;
begin
  if not FRichEdit.HandleAllocated then
    Exit;
  FillChar(F, SizeOf(F), 0);
  F.dxRightIndent := Value * 20;
  WriteParaFormat(Self, F, PFM_RIGHTINDENT);
end;

{ the low 24 bits of a tab entry are its position; the rest is alignment and leader }
function TParaAttributes.GetTab(Index: Byte): Integer;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  if Index < F.cTabCount then
    Result := (F.rgxTabs[Index] and $FFFFFF) div 20
  else
    Result := 0;
end;

procedure TParaAttributes.SetTab(Index: Byte; Value: Integer);
var
  F: TParaFormat2;
begin
  if (Index >= MAX_TAB_STOPS) or not ReadParaFormat(Self, F) then
    Exit;
  F.rgxTabs[Index] := Value * 20;
  if F.cTabCount < Index + 1 then
    F.cTabCount := Index + 1;
  WriteParaFormat(Self, F, PFM_TABSTOPS);
end;

function TParaAttributes.GetTabCount: Integer;
var
  F: TParaFormat2;
begin
  ReadParaFormat(Self, F);
  Result := F.cTabCount;
end;

procedure TParaAttributes.SetTabCount(Value: Integer);
var
  F: TParaFormat2;
begin
  if not ReadParaFormat(Self, F) then
    Exit;
  if Value < 0 then
    Value := 0;
  if Value > MAX_TAB_STOPS then
    Value := MAX_TAB_STOPS;
  F.cTabCount := SmallInt(Value);
  WriteParaFormat(Self, F, PFM_TABSTOPS);
end;

procedure TParaAttributes.Assign(Source: TPersistent);
var
  S: TParaAttributes;
  I: Integer;
begin
  if Source is TParaAttributes then
  begin
    S := TParaAttributes(Source);
    Alignment := S.Alignment;
    FirstIndent := S.FirstIndent;
    LeftIndent := S.LeftIndent;
    RightIndent := S.RightIndent;
    Numbering := S.Numbering;
    TabCount := 0;
    for I := 0 to S.TabCount - 1 do
      Tab[Byte(I)] := S.Tab[Byte(I)];
  end
  else
    inherited Assign(Source);
end;

{ ---------------------------------------------------------------- TRichEditStrings }

destructor TRichEditStrings.Destroy;
begin
  FList.Free;
  inherited Destroy;
end;

{ the handle-less list became the control's text }
procedure TRichEditStrings.Synchronize;
var
  S: string;
begin
  S := FList.Text;
  if (Length(S) >= 2) and (Copy(S, Length(S) - 1, 2) = #13#10) then
    SetLength(S, Length(S) - 2);
  FSyncing := True;
  try
    FRichEdit.Text := S;
  finally
    FSyncing := False;
  end;
end;

{ handle: EM_EXSETSEL + EM_REPLACESEL (engine positions); never called without one }
procedure TRichEditStrings.Replace(AStart, ALength: Integer; const S: string);
begin
  FRichEdit.SetSelection(AStart, ALength);
  SendMessageW(FRichEdit.Handle, EM_REPLACESEL, 0, LPARAM(PChar(S)));
end;

function TRichEditStrings.Get(Index: Integer): string;
var
  S: string;
begin
  if not FRichEdit.HandleAllocated then
  begin
    Result := FList[Index];
    Exit;
  end;
  S := FRichEdit.EngineText(False);
  if (Index < 0) or (Index >= ParagraphCount(S)) then
    Error(SListIndexError, Index);
  Result := Copy(S, ParagraphStart(S, Index) + 1, ParagraphEnd(S, Index) - ParagraphStart(S, Index));
end;

function TRichEditStrings.GetCount: Integer;
begin
  if FRichEdit.HandleAllocated then
    Result := ParagraphCount(FRichEdit.EngineText(False))
  else
    Result := FList.Count;
end;

procedure TRichEditStrings.Put(Index: Integer; const S: string);
var
  T: string;
begin
  if not FRichEdit.HandleAllocated then
  begin
    FList[Index] := S;
    Synchronize;
    Exit;
  end;
  T := FRichEdit.EngineText(False);
  if (Index < 0) or (Index >= ParagraphCount(T)) then
    Error(SListIndexError, Index);
  Replace(ParagraphStart(T, Index), ParagraphEnd(T, Index) - ParagraphStart(T, Index), S);
end;

procedure TRichEditStrings.Insert(Index: Integer; const S: string);
var
  T: string;
  N: Integer;
begin
  if not FRichEdit.HandleAllocated then
  begin
    FList.Insert(Index, S);
    Synchronize;
    Exit;
  end;
  T := FRichEdit.EngineText(False);
  N := ParagraphCount(T);
  if (Index < 0) or (Index > N) then
    Error(SListIndexError, Index);
  if N = 0 then
    Replace(0, 0, S)
  else if Index < N then
    Replace(ParagraphStart(T, Index), 0, S + #13)
  else
    Replace(Length(T), 0, #13 + S);
end;

procedure TRichEditStrings.Delete(Index: Integer);
var
  T: string;
  N, A: Integer;
begin
  if not FRichEdit.HandleAllocated then
  begin
    FList.Delete(Index);
    Synchronize;
    Exit;
  end;
  T := FRichEdit.EngineText(False);
  N := ParagraphCount(T);
  if (Index < 0) or (Index >= N) then
    Error(SListIndexError, Index);
  if N = 1 then
    Replace(0, Length(T), '')
  else if Index < N - 1 then
  begin
    A := ParagraphStart(T, Index);
    Replace(A, ParagraphStart(T, Index + 1) - A, '');
  end
  else
  begin
    A := ParagraphEnd(T, Index - 1);
    Replace(A, Length(T) - A, '');
  end;
end;

procedure TRichEditStrings.Clear;
begin
  if not FRichEdit.HandleAllocated then
  begin
    FList.Clear;
    Synchronize;
  end
  else
    FRichEdit.SetEngineText('');
end;

{ one EM_GETTEXTEX with GT_USECRLF }
function TRichEditStrings.GetTextStr: string;
begin
  if FRichEdit.HandleAllocated then
    Result := RichLinesText(FRichEdit.EngineText(True))
  else
    Result := FList.Text;
end;

{ one EM_SETTEXTEX; a trailing line break is removed (RTL-420) }
procedure TRichEditStrings.SetTextStr(const Value: string);
var
  S: string;
begin
  if not FRichEdit.HandleAllocated then
  begin
    FList.Text := Value;
    Synchronize;
    Exit;
  end;
  S := Value;
  if (Length(S) >= 2) and (S[Length(S) - 1] = #13) and (S[Length(S)] = #10) then
    SetLength(S, Length(S) - 2);
  FRichEdit.SetEngineText(S);
end;

procedure TRichEditStrings.LoadFromFile(const FileName: string);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    LoadFromStream(S);
  finally
    S.Free;
  end;
end;

{ UIF-1325, UIF-1326, UIF-1328 }
procedure TRichEditStrings.LoadFromStream(Stream: TStream);
begin
  if FRichEdit.FPlainText or not FRichEdit.LoadRichText(Stream) then
    inherited LoadFromStream(Stream);
end;

procedure TRichEditStrings.SaveToFile(const FileName: string);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmCreate);
  try
    SaveToStream(S);
  finally
    S.Free;
  end;
end;

{ UIF-1325, UIF-1327, UIF-1328 }
procedure TRichEditStrings.SaveToStream(Stream: TStream);
begin
  if FRichEdit.FPlainText then
    inherited SaveToStream(Stream)
  else
    FRichEdit.SaveRichText(Stream);
end;

{ ---------------------------------------------------------------- TCustomRichEdit }

constructor TCustomRichEdit.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FZoom := 100;
  FAutoURLDetect := True;
  AutoSelect := False;
  FSelAttributes := TTextAttributes.Create(Self, atSelected);
  FDefAttributes := TTextAttributes.Create(Self, atDefaultText);
  FParagraph := TParaAttributes.Create(Self);
end;

destructor TCustomRichEdit.Destroy;
begin
  inherited Destroy;
  FSavedContent.Free;
  FParagraph.Free;
  FDefAttributes.Free;
  FSelAttributes.Free;
end;

function TCustomRichEdit.CreateLines: TStrings;
var
  L: TRichEditStrings;
begin
  L := TRichEditStrings.Create;
  L.FRichEdit := Self;
  L.FList := TStringList.Create;
  Result := L;
end;

{ the styles of the multi-line edit apply unchanged; the content is pushed by CreateWnd, not as the window text }
procedure TCustomRichEdit.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := MSFTEDIT_CLASS;
  Params.Caption := nil;
end;

procedure TCustomRichEdit.CreateWnd;
var
  Plain: string;
  S, L, Z: Integer;
  M: Boolean;
begin
  if FLibraryName = '' then
    LoadRichEditLibrary(MSFTEDIT_DLL)
  else
    LoadRichEditLibrary(FLibraryName);
  Plain := Text;
  S := SelStart;
  L := SelLength;
  M := Modified;
  Z := FZoom;
  inherited CreateWnd;
  Inc(RichEditWindows);
  ApplyMaxLength;
  SendMessageW(Handle, EM_SETEVENTMASK, 0, RichEditEvents);
  SendMessageW(Handle, EM_SETBKGNDCOLOR, 0, LPARAM(ColorToRGB(Color)));
  ApplyFont;
  SendMessageW(Handle, EM_AUTOURLDETECT, WPARAM(Ord(FAutoURLDetect)), 0);
  if FSavedContent <> nil then
    try
      FSavedContent.Position := 0;
      TransferContent(EM_STREAMIN, SF_RTF, FSavedContent, SRichEditLoadFail);
    finally
      FreeAndNil(FSavedContent);
    end
  else if Plain <> '' then
    SetEngineText(Plain);
  TRichEditStrings(Lines).FList.Clear;
  { after the content: the engine drops its zoom when the whole text is replaced }
  FZoom := Z;
  ApplyZoom;
  SetSelection(S, L);
  SendMessageW(Handle, EM_SETMODIFY, WPARAM(Ord(M)), 0);
  SendMessageW(Handle, EM_EMPTYUNDOBUFFER, 0, 0);
end;

procedure TCustomRichEdit.DestroyWnd;
begin
  FZoom := GetZoom;
  FreeAndNil(FSavedContent);
  if not FPlainText and not (csDestroying in ComponentState) then
  begin
    FSavedContent := TMemoryStream.Create;
    TransferContent(EM_STREAMOUT, SF_RTF, FSavedContent, SRichEditSaveFail);
  end;
  inherited DestroyWnd;
  Dec(RichEditWindows);
  TRichEditStrings(Lines).FList.Text := RichLinesText(Text);
end;

procedure TCustomRichEdit.WndProc(var Message: TMessage);
var
  Protect: PENProtected;
  Link: PENLink;
begin
  case Message.Msg of
    CN_NOTIFY:
      case PNMHdr(Message.LParam)^.code of
        EN_SELCHANGE:
          begin
            SelectionChange;
            Message.Result := 0;
          end;
        EN_PROTECTED:
          begin
            Protect := PENProtected(Message.LParam);
            Message.Result := Ord(not ProtectChange(Protect^.msg, Protect^.chrg.cpMin, Protect^.chrg.cpMax));
          end;
        EN_LINK:
          begin
            Link := PENLink(Message.LParam);
            LinkClick(Link^.msg, Link^.chrg.cpMin, Link^.chrg.cpMax);
            Message.Result := 0;
          end;
      else
        inherited WndProc(Message);
      end;
    { UIF-1321: the font reaches the engine as its default character format only }
    WM_SETFONT:
      Message.Result := 0;
    { UIF-1334: the engine drops its zoom when the whole content is replaced; the control keeps it }
    WM_SETTEXT, EM_SETTEXTEX, EM_STREAMIN:
      if HandleAllocated and not (csCreating in ControlState) then
      begin
        GetZoom;
        inherited WndProc(Message);
        if FZoom <> 100 then
          ApplyZoom;
      end
      else
        inherited WndProc(Message);
    CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        if HandleAllocated then
          ApplyFont;
      end;
    { UIF-1311: the engine does not ask for its colours }
    CM_COLORCHANGED:
      begin
        inherited WndProc(Message);
        if HandleAllocated then
          SendMessageW(Handle, EM_SETBKGNDCOLOR, 0, LPARAM(ColorToRGB(Color)));
      end;
    CM_TEXTCHANGED:
      begin
        if not HandleAllocated then
        begin
          { UIF-1304: text assigned without a window replaces what the last window left }
          FreeAndNil(FSavedContent);
          if not TRichEditStrings(Lines).FSyncing then
            TRichEditStrings(Lines).FList.Text := RichLinesText(Text);
        end;
        { with a window the engine has reported the change (EN_CHANGE) }
        FInTextChanged := True;
        try
          inherited WndProc(Message);
        finally
          FInTextChanged := False;
        end;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomRichEdit.Change;
begin
  if (FUpdating > 0) or (FInTextChanged and HandleAllocated) then
    Exit;
  inherited Change;
end;

{ UIF-1333 }
procedure TCustomRichEdit.SelectionChange;
begin
  if (FUpdating > 0) or (csLoading in ComponentState) or (csCreating in ControlState) then
    Exit;
  if Assigned(FOnSelectionChange) then
    FOnSelectionChange(Self);
end;

{ UIF-1333: True = the change is carried out. The control's own whole-content operations and format changes
  (they change no text) are never refused }
function TCustomRichEdit.ProtectChange(Msg: Cardinal; StartPos, EndPos: Integer): Boolean;
begin
  Result := (FUpdating > 0) or (csLoading in ComponentState) or (csCreating in ControlState)
    or (Msg = EM_SETCHARFORMAT) or (Msg = EM_SETPARAFORMAT) or (Msg = WM_COPY);
  if not Result and Assigned(FOnProtectChange) then
    FOnProtectChange(Self, StartPos, EndPos, Result);
end;

{ UIF-1332 }
procedure TCustomRichEdit.LinkClick(Msg: Cardinal; StartPos, EndPos: Integer);
var
  Range: TTextRangeW;
  URL: string;
  N: Integer;
begin
  if (Msg <> WM_LBUTTONUP) or (EndPos <= StartPos) or not Assigned(FOnLinkClick)
    or (csLoading in ComponentState) or (csCreating in ControlState) then
    Exit;
  SetLength(URL, EndPos - StartPos + 1);
  Range.chrg.cpMin := StartPos;
  Range.chrg.cpMax := EndPos;
  Range.lpstrText := PChar(URL);
  N := Integer(SendMessageW(Handle, EM_GETTEXTRANGE, 0, LPARAM(@Range)));
  SetLength(URL, N);
  FOnLinkClick(Self, URL);
end;

{ the whole text: paragraphs separated by CR LF, or by the single CR of the engine's positions }
function TCustomRichEdit.EngineText(UseCRLF: Boolean): string;
var
  LenEx: TGetTextLengthEx;
  GetEx: TGetTextEx;
  N: Integer;
begin
  LenEx.flags := GTL_NUMCHARS;
  if UseCRLF then
    LenEx.flags := LenEx.flags or GTL_USECRLF;
  LenEx.codepage := CodePageUtf16;
  N := Integer(SendMessageW(Handle, EM_GETTEXTLENGTHEX, WPARAM(@LenEx), 0));
  if N <= 0 then
  begin
    Result := '';
    Exit;
  end;
  SetLength(Result, N);
  FillChar(GetEx, SizeOf(GetEx), 0);
  GetEx.cb := DWORD((N + 1) * SizeOf(Char));
  if UseCRLF then
    GetEx.flags := GT_USECRLF
  else
    GetEx.flags := GT_DEFAULT;
  GetEx.codepage := CodePageUtf16;
  N := Integer(SendMessageW(Handle, EM_GETTEXTEX, WPARAM(@GetEx), LPARAM(PChar(Result))));
  SetLength(Result, N);
end;

{ UIF-1306: one EM_SETTEXTEX replaces the whole content (UTF-16). The engine would read a text that starts like
  an RTF document as RTF, also in UTF-16; such a text goes through WM_SETTEXT, which never does (UIF-1309) }
procedure TCustomRichEdit.SetEngineText(const S: string);
var
  SetEx: TSetTextEx;
begin
  if (Length(S) >= 2) and (S[1] = '{') and (S[2] = '\') then
    SendMessageW(Handle, WM_SETTEXT, 0, LPARAM(PChar(S)))
  else
  begin
    SetEx.flags := ST_DEFAULT;
    SetEx.codepage := CodePageUtf16;
    SendMessageW(Handle, EM_SETTEXTEX, WPARAM(@SetEx), LPARAM(PChar(S)));
  end;
end;

{ UIF-1321: the control's font is the default character format; with PlainText it is the format of all text }
procedure TCustomRichEdit.ApplyFont;
var
  F: TCharFormat2W;
begin
  F.dwMask := FontToCharFormat(Font, F);
  SendMessageW(Handle, EM_SETCHARFORMAT, SCF_DEFAULT, LPARAM(@F));
  if FPlainText then
    SendMessageW(Handle, EM_SETCHARFORMAT, SCF_ALL, LPARAM(@F));
end;

{ UIF-1334 }
procedure TCustomRichEdit.ApplyZoom;
begin
  if FZoom = 100 then
    SendMessageW(Handle, EM_SETZOOM, 0, 0)
  else
    SendMessageW(Handle, EM_SETZOOM, WPARAM(FZoom), 100);
end;

{ UIF-1329: EM_STREAMIN / EM_STREAMOUT with the callbacks; a caught exception is raised again here }
procedure TCustomRichEdit.TransferContent(Msg: Cardinal; Format: NativeUInt; Stream: TStream;
  const FailMessage: string);
var
  ES: TEditStream;
  Cookie: TStreamCookie;
begin
  Cookie.Stream := Stream;
  Cookie.Failure := nil;
  ES.dwCookie := DWORD_PTR(@Cookie);
  ES.dwError := 0;
  if Msg = EM_STREAMIN then
    ES.pfnCallback := @StreamReadCallback
  else
    ES.pfnCallback := @StreamWriteCallback;
  Inc(FUpdating);
  try
    SendMessageW(Handle, Msg, WPARAM(Format), LPARAM(@ES));
  finally
    Dec(FUpdating);
  end;
  if Cookie.Failure <> nil then
    raise Exception(Cookie.Failure);
  if ES.dwError <> 0 then
    raise EStreamError.Create(FailMessage);
end;

{ UIF-1326: False = the stream does not start with the RTF signature and nothing was done }
function TCustomRichEdit.LoadRichText(Stream: TStream): Boolean;
const
  Signature: array[0..4] of Byte = (Ord('{'), Ord('\'), Ord('r'), Ord('t'), Ord('f'));
var
  Head: array[0..4] of Byte;
  Start: Int64;
  I, N: Integer;
begin
  HandleNeeded;
  Inc(FUpdating);
  try
    try
      Start := Stream.Position;
      N := Stream.Read(Head, 5);
      Stream.Position := Start;
      Result := N = 5;
      for I := 0 to 4 do
        Result := Result and (Head[I] = Signature[I]);
      if Result then
        TransferContent(EM_STREAMIN, SF_RTF, Stream, SRichEditLoadFail);
    except
      SetEngineText('');
      raise;
    end;
    if Result then
    begin
      SetSelection(0, 0);
      SendMessageW(Handle, EM_SETMODIFY, 0, 0);
      SendMessageW(Handle, EM_EMPTYUNDOBUFFER, 0, 0);
    end;
  finally
    Dec(FUpdating);
  end;
  if Result then
    Change;
end;

{ UIF-1327 }
procedure TCustomRichEdit.SaveRichText(Stream: TStream);
begin
  HandleNeeded;
  TransferContent(EM_STREAMOUT, SF_RTF, Stream, SRichEditSaveFail);
end;

function TCustomRichEdit.GetSelStart: Integer;
var
  R: TCharRange;
begin
  if not HandleAllocated then
  begin
    Result := inherited GetSelStart;
    Exit;
  end;
  R.cpMin := 0;
  R.cpMax := 0;
  SendMessageW(Handle, EM_EXGETSEL, 0, LPARAM(@R));
  Result := R.cpMin;
end;

function TCustomRichEdit.GetSelLength: Integer;
var
  R: TCharRange;
begin
  if not HandleAllocated then
  begin
    Result := inherited GetSelLength;
    Exit;
  end;
  R.cpMin := 0;
  R.cpMax := 0;
  SendMessageW(Handle, EM_EXGETSEL, 0, LPARAM(@R));
  Result := R.cpMax - R.cpMin;
end;

{ EM_GETSELTEXT; paragraph breaks as CR LF, as in Text }
function TCustomRichEdit.GetSelText: string;
var
  S: string;
  N: Integer;
begin
  if not HandleAllocated then
  begin
    Result := inherited GetSelText;
    Exit;
  end;
  SetLength(S, GetSelLength + 1);
  N := Integer(SendMessageW(Handle, EM_GETSELTEXT, 0, LPARAM(PChar(S))));
  SetLength(S, N);
  Result := StringReplace(S, #13, #13#10, [rfReplaceAll]);
end;

procedure TCustomRichEdit.SetSelection(AStart, ALength: Integer);
var
  R: TCharRange;
begin
  if not HandleAllocated then
  begin
    inherited SetSelection(AStart, ALength);
    Exit;
  end;
  if AStart < 0 then
    AStart := 0;
  if ALength < 0 then
    ALength := 0;
  R.cpMin := AStart;
  R.cpMax := AStart + ALength;
  SendMessageW(Handle, EM_EXSETSEL, 0, LPARAM(@R));
end;

procedure TCustomRichEdit.ApplyHideSelection;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_HIDESELECTION, WPARAM(Ord(HideSelection)), 0);
end;

{ UIF-1310 }
procedure TCustomRichEdit.ApplyMaxLength;
begin
  if not HandleAllocated then
    Exit;
  if MaxLength <= 0 then
    SendMessageW(Handle, EM_EXLIMITTEXT, 0, NoTextLimit)
  else
    SendMessageW(Handle, EM_EXLIMITTEXT, 0, LPARAM(MaxLength));
end;

{ (column, paragraph) of the start of the selection, in engine positions }
function TCustomRichEdit.GetCaretPos: TPoint;
var
  S: string;
  P, I: Integer;
begin
  if not HandleAllocated then
  begin
    Result := inherited GetCaretPos;
    Exit;
  end;
  S := EngineText(False);
  P := GetSelStart;
  Result.X := 0;
  Result.Y := 0;
  for I := 1 to P do
    if (I <= Length(S)) and (S[I] = #13) then
    begin
      Inc(Result.Y);
      Result.X := 0;
    end
    else
      Inc(Result.X);
end;

procedure TCustomRichEdit.SetCaretPos(const Value: TPoint);
var
  S: string;
  N, A, E: Integer;
begin
  if not HandleAllocated then
  begin
    inherited SetCaretPos(Value);
    Exit;
  end;
  S := EngineText(False);
  N := ParagraphCount(S);
  if N = 0 then
    SetSelection(0, 0)
  else if Value.Y >= N then
    SetSelection(Length(S), 0)
  else
  begin
    A := ParagraphStart(S, Value.Y);
    E := ParagraphEnd(S, Value.Y);
    if A + Value.X < E then
      E := A + Value.X;
    SetSelection(E, 0);
  end;
end;

{ UIF-1331: the event mask is set again so that the engine scans the existing text }
procedure TCustomRichEdit.SetAutoURLDetect(Value: Boolean);
begin
  if FAutoURLDetect <> Value then
  begin
    FAutoURLDetect := Value;
    if HandleAllocated then
    begin
      SendMessageW(Handle, EM_SETEVENTMASK, 0, RichEditEvents);
      SendMessageW(Handle, EM_AUTOURLDETECT, WPARAM(Ord(Value)), 0);
    end;
  end;
end;

{ UIF-1334: a zoom changed by the user is read back }
function TCustomRichEdit.GetZoom: Integer;
var
  Num, Den: Integer;
begin
  if HandleAllocated then
  begin
    Num := 0;
    Den := 0;
    SendMessageW(Handle, EM_GETZOOM, WPARAM(@Num), LPARAM(@Den));
    if (Num > 0) and (Den > 0) then
      FZoom := MulDiv(Num, 100, Den)
    else
      FZoom := 100;
  end;
  Result := FZoom;
end;

procedure TCustomRichEdit.SetZoom(Value: Integer);
begin
  if Value < 10 then
    Value := 10;
  if Value > 500 then
    Value := 500;
  FZoom := Value;
  if HandleAllocated then
    ApplyZoom;
end;

procedure TCustomRichEdit.SetSelAttributes(Value: TTextAttributes);
begin
  FSelAttributes.Assign(Value);
end;

procedure TCustomRichEdit.SetDefAttributes(Value: TTextAttributes);
begin
  FDefAttributes.Assign(Value);
end;

procedure TCustomRichEdit.SetParagraph(Value: TParaAttributes);
begin
  FParagraph.Assign(Value);
end;

{ UIF-1335 }
function TCustomRichEdit.FindText(const SearchStr: string; StartPos, Length: Integer; Options: TSearchTypes): Integer;
var
  Find: TFindTextExW;
  Flags: WPARAM;
begin
  Result := -1;
  if (SearchStr = '') or not HandleAllocated then
    Exit;
  if StartPos < 0 then
    StartPos := 0;
  Find.chrg.cpMin := StartPos;
  if (Length < 0) or (Length > MaxInt - StartPos) then
    Find.chrg.cpMax := -1
  else
    Find.chrg.cpMax := StartPos + Length;
  Find.lpstrText := PChar(SearchStr);
  Find.chrgText.cpMin := -1;
  Find.chrgText.cpMax := -1;
  Flags := FR_DOWN;
  if stWholeWord in Options then
    Flags := Flags or FR_WHOLEWORD;
  if stMatchCase in Options then
    Flags := Flags or FR_MATCHCASE;
  Result := Integer(SendMessageW(Handle, EM_FINDTEXTEXW, Flags, LPARAM(@Find)));
end;

{ UIF-1336 }
procedure TCustomRichEdit.Redo;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_REDO, 0, 0);
end;

function TCustomRichEdit.GetCanRedo: Boolean;
begin
  Result := HandleAllocated and (SendMessageW(Handle, EM_CANREDO, 0, 0) <> 0);
end;

procedure TCustomRichEdit.ClearUndo;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_EMPTYUNDOBUFFER, 0, 0);
end;

initialization
  RegisterClasses([TTabControl, TPageControl, TTabSheet, TStatusBar, TToolBar, TToolButton, TProgressBar, TTrackBar,
    TListView, TTreeView, TRichEdit]);
  RegisterAutomationProvider(TTabSheet, TTabSheetAutomationProvider);
  RegisterAutomationProvider(TToolBar, TToolBarAutomationProvider);
  RegisterAutomationProvider(TToolButton, TToolButtonAutomationProvider);

finalization
  { a window that outlives this unit (a form freed by a later finalization) still runs the engine's code }
  if (RichEditLibrary <> 0) and (RichEditWindows = 0) then
    FreeLibrary(RichEditLibrary);

end.
