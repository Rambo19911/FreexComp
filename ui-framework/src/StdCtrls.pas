// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ StdCtrls - the standard controls (controls-standard.md sections 1 ... 11, 18). }
unit StdCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, ActnList, Controls;

type
  TTextLayout = (tlTop, tlCenter, tlBottom);

  { UIF-882, UIF-884: tells the user that a modification was rejected }
  TRejectBeepHook = procedure;

  { controls-standard.md section 1 }
  TCustomLabel = class(TGraphicControl)
  private
    FAlignment: TAlignment;
    FFocusControl: TWinControl;
    FLayout: TTextLayout;
    FShowAccelChar: Boolean;
    FTransparent: Boolean;
    FWordWrap: Boolean;
    procedure SetAlignment(Value: TAlignment);
    procedure SetFocusControl(Value: TWinControl);
    procedure SetLayout(Value: TTextLayout);
    procedure SetShowAccelChar(Value: Boolean);
    procedure SetTransparent(Value: Boolean);
    procedure SetWordWrap(Value: Boolean);
    function TextFormat: TTextFormat;
  protected
    { UIF-610 }
    procedure Paint; override;
    { UIF-611: CM_DIALOGCHAR; text, font and enabled changes repaint }
    procedure WndProc(var Message: TMessage); override;
    { UIF-612 }
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { layout.md UIF-255: measured size of the caption (outside loading, scaling and dump mode) }
    procedure AdjustSize; override;
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property AutoSize default True;
    property FocusControl: TWinControl read FFocusControl write SetFocusControl;
    property Layout: TTextLayout read FLayout write SetLayout default tlTop;
    property ShowAccelChar: Boolean read FShowAccelChar write SetShowAccelChar default True;
    property Transparent: Boolean read FTransparent write SetTransparent default True;
    property WordWrap: Boolean read FWordWrap write SetWordWrap default False;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLabel = class(TCustomLabel)
  published
    property Alignment;
    property AutoSize;
    property Caption;
    property Color default clBtnFace;
    property FocusControl;
    property Font;
    property Layout;
    property ParentColor;
    property ParentFont;
    property ShowAccelChar;
    property Transparent;
    property WordWrap;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  TEditCharCase = (ecNormal, ecUpperCase, ecLowerCase);
  TScrollStyle = (ssNone, ssHorizontal, ssVertical, ssBoth);

  { controls-standard.md section 2 }
  TCustomEdit = class(TWinControl)
  private
    FAlignment: TAlignment;
    FAutoSelect: Boolean;
    FBorderStyle: TBorderStyle;
    FCharCase: TEditCharCase;
    FHideSelection: Boolean;
    FMaxLength: Integer;
    FNumbersOnly: Boolean;
    FPasswordChar: Char;
    FReadOnly: Boolean;
    FRejectSilently: Boolean;
    FTextHint: string;
    FSelStart: Integer;                                      { UIF-621: the selection without a handle }
    FSelLength: Integer;
    FModified: Boolean;
    FOnChange: TNotifyEvent;
    procedure SetAlignment(Value: TAlignment);
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetCharCase(Value: TEditCharCase);
    procedure SetHideSelection(Value: Boolean);
    procedure SetMaxLength(Value: Integer);
    procedure SetNumbersOnly(Value: Boolean);
    procedure SetPasswordChar(Value: Char);
    procedure SetReadOnly(Value: Boolean);
    procedure SetTextHint(const Value: string);
    procedure SetSelStart(Value: Integer);
    procedure SetSelLength(Value: Integer);
    procedure SetSelText(const Value: string);
    function GetModified: Boolean;
    procedure SetModified(Value: Boolean);
    function GetCanUndo: Boolean;
    procedure AdjustHeight;
    function UserModify: Boolean;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-033: selection, limit, password character, cue banner and modified flag pushed after creation }
    procedure CreateWnd; override;
    { UIF-034: selection and modified flag saved before the window goes }
    procedure DestroyWnd; override;
    { UIF-620: EN_CHANGE (CN_COMMAND); handle-less text changes; font changes re-apply I2/I3;
      UIF-882: paste, cut, clear and undo (the messages, and so the methods that send them) ask EditCanModify }
    procedure WndProc(var Message: TMessage); override;
    { layout.md UIF-250 rules I2/I3 }
    procedure IntrinsicSize(var AWidth, AHeight: Integer); override;
    procedure SetParent(AParent: TWinControl); override;
    { rules I2/I3 once all stored properties are known }
    procedure Loaded; override;
    { AutoSelect: select all when the focus arrives without a mouse click }
    procedure DoEnter; override;
    { UIF-881: called exactly when OnChange fires; the base fires it }
    procedure Change; virtual;
    { UIF-882: asked before a modification by the user is applied (never while ReadOnly, never for code);
      False discards the input }
    function EditCanModify: Boolean; virtual;
    { UIF-882: set by EditCanModify to refuse without the beep; False again before every question }
    property RejectSilently: Boolean read FRejectSilently write FRejectSilently;
    { selects AStart .. AStart + ALength (handle: EM_SETSEL; else the fields) }
    procedure SetSelection(AStart, ALength: Integer); virtual;
    { virtual for a descendant whose native control uses other messages (rich-edit.md UIF-1305, UIF-1310):
      the selection, and how a changed HideSelection / MaxLength reaches the window }
    function GetSelStart: Integer; virtual;
    function GetSelLength: Integer; virtual;
    function GetSelText: string; virtual;
    procedure ApplyHideSelection; virtual;
    procedure ApplyMaxLength; virtual;
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property AutoSelect: Boolean read FAutoSelect write FAutoSelect default True;
    property AutoSize default True;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property CharCase: TEditCharCase read FCharCase write SetCharCase default ecNormal;
    property HideSelection: Boolean read FHideSelection write SetHideSelection default True;
    property MaxLength: Integer read FMaxLength write SetMaxLength default 0;
    property NumbersOnly: Boolean read FNumbersOnly write SetNumbersOnly default False;
    property PasswordChar: Char read FPasswordChar write SetPasswordChar default #0;
    property ReadOnly: Boolean read FReadOnly write SetReadOnly default False;
    property TextHint: string read FTextHint write SetTextHint;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  public
    constructor Create(AOwner: TComponent); override;
    { UIF-882: a character, Backspace and the Delete key ask EditCanModify after KeyPress / KeyDown }
    procedure DefaultHandler(var Message); override;
    procedure Clear; virtual;
    procedure ClearSelection;
    procedure SelectAll;
    procedure Undo;
    procedure CopyToClipboard;
    procedure CutToClipboard;
    procedure PasteFromClipboard;
    property SelStart: Integer read GetSelStart write SetSelStart;
    property SelLength: Integer read GetSelLength write SetSelLength;
    property SelText: string read GetSelText write SetSelText;
    property Modified: Boolean read GetModified write SetModified;
    property CanUndo: Boolean read GetCanUndo;
    property Text;
  end;

  TEdit = class(TCustomEdit)
  published
    property Alignment;
    property AutoSelect;
    property AutoSize;
    property BorderStyle;
    property CharCase;
    property Color default clWindow;
    property Font;
    property HideSelection;
    property MaxLength;
    property NumbersOnly;
    property ParentColor default False;
    property ParentFont;
    property PasswordChar;
    property ReadOnly;
    property TabOrder;
    property TabStop default True;
    property Text;
    property TextHint;
    property OnChange;
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
  end;

  TCustomMemo = class;

  { UIF-630: the logical lines of a memo's text }
  TMemoStrings = class(TStrings)
  private
    FMemo: TCustomMemo;
    FList: TStringList;                                      { the lines while the memo has no handle }
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
  end;

  { controls-standard.md section 3 }
  TCustomMemo = class(TCustomEdit)
  private
    FLines: TStrings;
    FScrollBars: TScrollStyle;
    FWantReturns: Boolean;
    FWantTabs: Boolean;
    FWordWrap: Boolean;
    procedure SetLines(Value: TStrings);
    procedure SetScrollBars(Value: TScrollStyle);
    procedure SetWantReturns(Value: Boolean);
    procedure SetWordWrap(Value: Boolean);
  protected
    { rich-edit.md UIF-1302: the TStrings behind Lines, created once by the constructor; the base returns the
      TMemoStrings of UIF-630 }
    function CreateLines: TStrings; virtual;
    function GetCaretPos: TPoint; virtual;
    procedure SetCaretPos(const Value: TPoint); virtual;
    procedure CreateParams(var Params: TCreateParams); override;
    procedure DestroyWnd; override;
    { CM_WANTSPECIALKEY (WantReturns, WantTabs), WM_GETDLGCODE without DLGC_WANTALLKEYS, Escape (UIF-632) }
    procedure WndProc(var Message: TMessage); override;
    { no intrinsic height (rules I2/I3 exclude memos) }
    procedure IntrinsicSize(var AWidth, AHeight: Integer); override;
    property Lines: TStrings read FLines write SetLines;
    property ScrollBars: TScrollStyle read FScrollBars write SetScrollBars default ssNone;
    property WantReturns: Boolean read FWantReturns write SetWantReturns default True;
    property WantTabs: Boolean read FWantTabs write FWantTabs default False;
    property WordWrap: Boolean read FWordWrap write SetWordWrap default True;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-632: Escape never reaches the native multi-line edit (it would close its parent) }
    procedure DefaultHandler(var Message); override;
    property CaretPos: TPoint read GetCaretPos write SetCaretPos;
  end;

  TMemo = class(TCustomMemo)
  published
    property Alignment;
    property BorderStyle;
    property Color default clWindow;
    property Font;
    property HideSelection;
    property Lines;
    property MaxLength;
    property ParentColor default False;
    property ParentFont;
    property ReadOnly;
    property ScrollBars;
    property TabOrder;
    property TabStop default True;
    property WantReturns;
    property WantTabs;
    property WordWrap;
    property OnChange;
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
  end;

  { actions-menus.md UIF-905: a button control's link adds Checked (check boxes and radio buttons) }
  TButtonActionLink = class(TControlActionLink)
  protected
    function IsCheckedLinked: Boolean; override;
    procedure SetChecked(Value: Boolean); override;
  end;

  { controls-standard.md sections 4 ... 6: the base of the BUTTON-class controls }
  TButtonControl = class(TWinControl)
  private
    FWordWrap: Boolean;
    FClicksDisabled: Boolean;                                { Checked copied from an action: no Click }
    procedure SetWordWrap(Value: Boolean);
  protected
    function GetActionLinkClass: TControlActionLinkClass; override;
    { Click unless the state is being copied from an action }
    procedure Click; override;
    { sets Checked of a check box or radio button without Click (an action's value) }
    procedure SetCheckedFromAction(Value: Boolean);
    procedure CreateParams(var Params: TCreateParams); override;
    property WordWrap: Boolean read FWordWrap write SetWordWrap default False;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  { controls-standard.md section 4 }
  TButton = class(TButtonControl)
  private
    FCancel: Boolean;
    FDefault: Boolean;
    FModalResult: TModalResult;
    FActive: Boolean;                                        { UIF-641: drawn as the default push button }
    procedure SetDefault(Value: Boolean);
    procedure UpdateActive(Focused: TWinControl);
    function IsFirst(Defaults: Boolean): Boolean;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { BN_CLICKED, CM_DIALOGKEY (UIF-115, UIF-642, UIF-643), CM_DIALOGCHAR (UIF-120), CM_FOCUSCHANGED (UIF-641) }
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    { UIF-640 }
    procedure Click; override;
  published
    property Action;
    property Cancel: Boolean read FCancel write FCancel default False;
    property Caption;
    property Default: Boolean read FDefault write SetDefault default False;
    property Font;
    property ModalResult: TModalResult read FModalResult write FModalResult default mrNone;
    property ParentFont;
    property TabOrder;
    property TabStop default True;
    property WordWrap;
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
  end;

  TCheckBoxState = (cbUnchecked, cbChecked, cbGrayed);

  { controls-standard.md section 5 }
  TCustomCheckBox = class(TButtonControl)
  private
    FAlignment: TLeftRight;
    FAllowGrayed: Boolean;
    FState: TCheckBoxState;
    procedure SetAlignment(Value: TLeftRight);
    procedure SetAllowGrayed(Value: Boolean);
    procedure SetState(Value: TCheckBoxState);
    function GetChecked: Boolean;
    procedure SetChecked(Value: Boolean);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { BN_CLICKED and the accelerator toggle (UIF-650) }
    procedure WndProc(var Message: TMessage); override;
    { UIF-650, UIF-883: the single path of user-initiated state changes }
    procedure Toggle; virtual;
    { UIF-906: Checked too }
    procedure ActionChange(Sender: TObject; CheckDefaults: Boolean); override;
    property Alignment: TLeftRight read FAlignment write SetAlignment default taRightJustify;
    property AllowGrayed: Boolean read FAllowGrayed write SetAllowGrayed default False;
    property Checked: Boolean read GetChecked write SetChecked default False;
    property State: TCheckBoxState read FState write SetState default cbUnchecked;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TCheckBox = class(TCustomCheckBox)
  published
    property Action;
    property Alignment;
    property AllowGrayed;
    property Caption;
    property Checked;
    property Font;
    property ParentFont;
    property State;
    property TabOrder;
    property TabStop default True;
    property WordWrap;
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
  end;

  { controls-standard.md section 6 }
  TRadioButton = class(TButtonControl)
  private
    FAlignment: TLeftRight;
    FChecked: Boolean;
    procedure SetAlignment(Value: TLeftRight);
    procedure SetChecked(Value: Boolean);
    procedure SetCheckedState(Value: Boolean);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { BN_CLICKED and the accelerator check the button (UIF-661) }
    procedure WndProc(var Message: TMessage); override;
    { UIF-662: the last checked button of a group in creation order stays checked }
    procedure Loaded; override;
    { UIF-906: Checked too }
    procedure ActionChange(Sender: TObject; CheckDefaults: Boolean); override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    property Action;
    property Alignment: TLeftRight read FAlignment write SetAlignment default taRightJustify;
    property Caption;
    property Checked: Boolean read FChecked write SetChecked default False;
    property Font;
    property ParentFont;
    property TabOrder;
    property TabStop default False;
    property WordWrap;
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
  end;

  { controls-standard.md section 7 }
  TCustomGroupBox = class(TCustomControl)
  private
    function CaptionHeight: Integer;
  protected
    { UIF-670 }
    procedure Paint; override;
    { UIF-671: CM_DIALOGCHAR; font changes realign (layout.md UIF-218), text/font/enabled changes repaint }
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    { layout.md UIF-218: (s(2), CapH, CW - s(2), CH - s(2)) }
    procedure AdjustClientRect(var Rect: TRect); override;
  end;

  TGroupBox = class(TCustomGroupBox)
  published
    property Caption;
    property Color default clBtnFace;
    property Font;
    property Padding;
    property ParentColor;
    property ParentFont;
    property TabOrder;
    property TabStop;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

  TListBoxStyle = (lbStandard, lbOwnerDrawFixed, lbOwnerDrawVariable);

  TCustomListBox = class;

  { UIF-690: the items of a list box (LB_* messages; the list box's saved list without a handle) }
  TListBoxStrings = class(TStrings)
  private
    FListBox: TCustomListBox;
  protected
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    function GetObject(Index: Integer): TObject; override;
    procedure Put(Index: Integer; const S: string); override;
    procedure PutObject(Index: Integer; AObject: TObject); override;
    { Items.BeginUpdate/EndUpdate -> WM_SETREDRAW }
    procedure SetUpdateState(Updating: Boolean); override;
  public
    { UIF-693: the actual index (sorted lists insert in order) }
    function Add(const S: string): Integer; override;
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    { UIF-693: not allowed on a sorted list }
    procedure Insert(Index: Integer; const S: string); override;
  end;

  { controls-standard.md section 9 }
  TCustomListBox = class(TWinControl)
  private
    FBorderStyle: TBorderStyle;
    FColumns: Integer;
    FExtendedSelect: Boolean;
    FItemHeight: Integer;
    FItems: TStrings;
    FMultiSelect: Boolean;
    FSorted: Boolean;
    FStyle: TListBoxStyle;
    { UIF-690: the state while there is no handle }
    FSaved: TStringList;                                     { the items and their objects }
    FSavedSel: string;                                       { one '0'/'1' per item: selected }
    FSavedCaret: Integer;                                    { multi-select: the focused item }
    FSavedTop: Integer;
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetColumns(Value: Integer);
    procedure SetExtendedSelect(Value: Boolean);
    function GetItemHeight: Integer;
    procedure SetItemHeight(Value: Integer);
    procedure SetItems(Value: TStrings);
    procedure SetMultiSelect(Value: Boolean);
    procedure SetSorted(Value: Boolean);
    procedure SetStyle(Value: TListBoxStyle);
    function GetCount: Integer;
    function GetItemIndex: Integer;
    procedure SetItemIndex(Value: Integer);
    function GetSelected(Index: Integer): Boolean;
    procedure SetSelected(Index: Integer; Value: Boolean);
    function GetSelCount: Integer;
    function GetTopIndex: Integer;
    procedure SetTopIndex(Value: Integer);
    procedure CheckIndex(Index: Integer);
    procedure SavedInsert(Index: Integer; const S: string; AObject: TObject; Sel: Boolean);
    function SortedIndex(const S: string): Integer;
    procedure SortSaved;
    procedure UpdateColumnWidth;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-690: items, objects, selection, top item pushed after creation }
    procedure CreateWnd; override;
    { UIF-690: the same pulled back before the window goes }
    procedure DestroyWnd; override;
    { UIF-691: LBN_SELCHANGE -> Click, LBN_DBLCLK -> DblClick; WM_SIZE keeps the column width }
    procedure WndProc(var Message: TMessage); override;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property Columns: Integer read FColumns write SetColumns default 0;
    property ExtendedSelect: Boolean read FExtendedSelect write SetExtendedSelect default True;
    property ItemHeight: Integer read GetItemHeight write SetItemHeight stored False;
    property MultiSelect: Boolean read FMultiSelect write SetMultiSelect default False;
    property Sorted: Boolean read FSorted write SetSorted default False;
    property Style: TListBoxStyle read FStyle write SetStyle default lbStandard;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function ItemAtPos(Pos: TPoint; Existing: Boolean): Integer;
    function ItemRect(Index: Integer): TRect;
    procedure AddItem(const Item: string; AObject: TObject);
    procedure Clear;
    procedure ClearSelection;
    procedure DeleteSelected;
    procedure SelectAll;
    property Count: Integer read GetCount;
    property ItemIndex: Integer read GetItemIndex write SetItemIndex;
    property Items: TStrings read FItems write SetItems;
    property SelCount: Integer read GetSelCount;
    property Selected[Index: Integer]: Boolean read GetSelected write SetSelected;
    property TopIndex: Integer read GetTopIndex write SetTopIndex;
  end;

  TListBox = class(TCustomListBox)
  published
    property BorderStyle;
    property Color default clWindow;
    property Columns;
    property ExtendedSelect;
    property Font;
    property ItemHeight;
    property Items;
    property MultiSelect;
    property ParentColor default False;
    property ParentFont;
    property Sorted;
    property Style;
    property TabOrder;
    property TabStop default True;
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
  end;

  TComboBoxStyle = (csDropDown, csSimple, csDropDownList, csOwnerDrawFixed, csOwnerDrawVariable);

  TCustomComboBox = class;

  { UIF-703: the items of a combo box (CB_* messages; the combo box's saved list without a handle) }
  TComboBoxStrings = class(TStrings)
  private
    FComboBox: TCustomComboBox;
  protected
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    function GetObject(Index: Integer): TObject; override;
    procedure Put(Index: Integer; const S: string); override;
    procedure PutObject(Index: Integer; AObject: TObject); override;
    procedure SetUpdateState(Updating: Boolean); override;
  public
    function Add(const S: string): Integer; override;
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    procedure Insert(Index: Integer; const S: string); override;
  end;

  { controls-standard.md section 10 }
  TCustomComboBox = class(TWinControl)
  private
    FCharCase: TEditCharCase;
    FDropDownCount: Integer;
    FItems: TStrings;
    FMaxLength: Integer;
    FSorted: Boolean;
    FStyle: TComboBoxStyle;
    FTextHint: string;
    FSaved: TStringList;                                     { UIF-703: the items while there is no handle }
    FSavedIndex: Integer;
    FSettingIndex: Boolean;
    FEditHandle: HWND;                                       { UIF-884: the edit part (0: list styles, no handle) }
    FEditProc: Pointer;                                      { its window procedure before ours }
    FEditRelay: Cardinal;                                    { the key message of the edit part being handled }
    FUserIndex: Integer;                                     { UIF-884: the selection and text before the user's }
    FUserText: string;                                       { input, restored when a selection is rejected }
    FRestorePending: Boolean;
    FRejectSilently: Boolean;
    FSelecting: Boolean;
    FOnChange: TNotifyEvent;
    FOnCloseUp: TNotifyEvent;
    FOnDropDown: TNotifyEvent;
    FOnSelect: TNotifyEvent;
    procedure SetCharCase(Value: TEditCharCase);
    procedure SetDropDownCount(Value: Integer);
    procedure SetItems(Value: TStrings);
    procedure SetMaxLength(Value: Integer);
    procedure SetSorted(Value: Boolean);
    procedure SetStyle(Value: TComboBoxStyle);
    procedure SetTextHint(const Value: string);
    function GetItemIndex: Integer;
    procedure SetItemIndex(Value: Integer);
    function GetDroppedDown: Boolean;
    procedure SetDroppedDown(Value: Boolean);
    function GetSelStart: Integer;
    procedure SetSelStart(Value: Integer);
    function GetSelLength: Integer;
    procedure SetSelLength(Value: Integer);
    function GetSelText: string;
    procedure SetSelText(const Value: string);
    function IsListStyle: Boolean;
    function IndexOfText(const S: string): Integer;
    procedure SetItemIndexText(Index: Integer);
    procedure SortSaved;
    procedure AdjustHeight;
    procedure UpdateItemHeight;
    procedure HookEdit;
    procedure UnhookEdit;
    procedure EditWndProc(var Message: TMessage);
    function UserModify: Boolean;
    procedure SaveUserState;
    procedure RestoreUserState;
    procedure UserSelect;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-701, UIF-703: items, selection, text, limit, cue banner, visible item count pushed after creation }
    procedure CreateWnd; override;
    procedure DestroyWnd; override;
    { UIF-702 notifications, UIF-704 special keys, list-style text assignment, colours of the inner windows }
    procedure WndProc(var Message: TMessage); override;
    { layout.md UIF-250 rule I1 }
    procedure IntrinsicSize(var AWidth, AHeight: Integer); override;
    procedure SetParent(AParent: TWinControl); override;
    procedure Loaded; override;
    { UIF-884: each fires its event }
    procedure Change; virtual;
    procedure Select; virtual;
    procedure DropDown; virtual;
    procedure CloseUp; virtual;
    { UIF-884: asked before the user's typing into the edit part or the user's selection is applied; False
      discards the input (a selection is taken back) }
    function EditCanModify: Boolean; virtual;
    { UIF-884: set by EditCanModify to refuse without the beep; False again before every question }
    property RejectSilently: Boolean read FRejectSilently write FRejectSilently;
    { UIF-884: True while a selection by the user is handled (EditCanModify, Change, Select, Click of
      CBN_SELCHANGE); False for typing into the edit part and for code }
    property IsSelecting: Boolean read FSelecting;
    property CharCase: TEditCharCase read FCharCase write SetCharCase default ecNormal;
    property DropDownCount: Integer read FDropDownCount write SetDropDownCount default 8;
    property ItemIndex: Integer read GetItemIndex write SetItemIndex default -1;
    property MaxLength: Integer read FMaxLength write SetMaxLength default 0;
    property Sorted: Boolean read FSorted write SetSorted default False;
    property Style: TComboBoxStyle read FStyle write SetStyle default csDropDown;
    property TextHint: string read FTextHint write SetTextHint;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnCloseUp: TNotifyEvent read FOnCloseUp write FOnCloseUp;
    property OnDropDown: TNotifyEvent read FOnDropDown write FOnDropDown;
    property OnSelect: TNotifyEvent read FOnSelect write FOnSelect;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-884: the key messages of the edit part end in the edit part's own window procedure }
    procedure DefaultHandler(var Message); override;
    procedure AddItem(const Item: string; AObject: TObject);
    procedure Clear;
    procedure SelectAll;
    property DroppedDown: Boolean read GetDroppedDown write SetDroppedDown;
    property Items: TStrings read FItems write SetItems;
    property SelLength: Integer read GetSelLength write SetSelLength;
    property SelStart: Integer read GetSelStart write SetSelStart;
    property SelText: string read GetSelText write SetSelText;
  end;

  TComboBox = class(TCustomComboBox)
  published
    property CharCase;
    property Color default clWindow;
    property DropDownCount;
    property Font;
    property Items;
    property ItemIndex;
    property MaxLength;
    property ParentColor default False;
    property ParentFont;
    property Sorted;
    property Style;
    property TabOrder;
    property TabStop default True;
    property Text;
    property TextHint;
    property OnChange;
    property OnClick;
    property OnCloseUp;
    property OnContextPopup;
    property OnDblClick;
    property OnDropDown;
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
    property OnSelect;
  end;

  { TScrollBarKind is declared in Controls (also used by Forms.TControlScrollBar) }
  TScrollCode = (scLineUp, scLineDown, scPageUp, scPageDown, scPosition, scTrack, scTop, scBottom, scEndScroll);
  TScrollEvent = procedure(Sender: TObject; ScrollCode: TScrollCode; var ScrollPos: Integer) of object;

  { controls-standard.md section 11 }
  TScrollBar = class(TWinControl)
  private
    FKind: TScrollBarKind;
    FLargeChange: Integer;
    FMax: Integer;
    FMin: Integer;
    FPageSize: Integer;
    FPosition: Integer;
    FSmallChange: Integer;
    FOnChange: TNotifyEvent;
    FOnScroll: TScrollEvent;
    procedure SetKind(Value: TScrollBarKind);
    procedure SetLargeChange(Value: Integer);
    procedure SetMax(Value: Integer);
    procedure SetMin(Value: Integer);
    procedure SetPageSize(Value: Integer);
    procedure SetPosition(Value: Integer);
    procedure SetSmallChange(Value: Integer);
    function ClampPosition(Value: Integer): Integer;
    procedure UpdateScrollInfo;
    procedure DoScroll(Code: Integer);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { range, page and position pushed after creation }
    procedure CreateWnd; override;
    { UIF-710: CN_HSCROLL / CN_VSCROLL }
    procedure WndProc(var Message: TMessage); override;
    { the range is checked once all stored properties are known }
    procedure Loaded; override;
    procedure Change; virtual;
    procedure Scroll(ScrollCode: TScrollCode; var ScrollPos: Integer); virtual;
  public
    constructor Create(AOwner: TComponent); override;
    procedure SetParams(APosition, AMin, AMax: Integer);
  published
    property Kind: TScrollBarKind read FKind write SetKind default sbHorizontal;
    property LargeChange: Integer read FLargeChange write SetLargeChange default 1;
    property Max: Integer read FMax write SetMax default 100;
    property Min: Integer read FMin write SetMin default 0;
    property PageSize: Integer read FPageSize write SetPageSize default 0;
    property Position: Integer read FPosition write SetPosition default 0;
    property SmallChange: Integer read FSmallChange write SetSmallChange default 1;
    property TabOrder;
    property TabStop default True;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnScroll: TScrollEvent read FOnScroll write FOnScroll;
  end;

  TStaticBorderStyle = (sbsNone, sbsSingle, sbsSunken);

  { controls-standard.md section 18: a windowed TLabel }
  TCustomStaticText = class(TWinControl)
  private
    FAlignment: TAlignment;
    FBorderStyle: TStaticBorderStyle;
    FFocusControl: TWinControl;
    FShowAccelChar: Boolean;
    procedure SetAlignment(Value: TAlignment);
    procedure SetBorderStyle(Value: TStaticBorderStyle);
    procedure SetFocusControl(Value: TWinControl);
    procedure SetShowAccelChar(Value: Boolean);
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { CM_DIALOGCHAR as TLabel (UIF-611) }
    procedure WndProc(var Message: TMessage); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { layout.md UIF-255: measured size of the caption plus the border }
    procedure AdjustSize; override;
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property AutoSize default True;
    property BorderStyle: TStaticBorderStyle read FBorderStyle write SetBorderStyle default sbsNone;
    property FocusControl: TWinControl read FFocusControl write SetFocusControl;
    property ShowAccelChar: Boolean read FShowAccelChar write SetShowAccelChar default True;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TStaticText = class(TCustomStaticText)
  published
    property Alignment;
    property AutoSize;
    property BorderStyle;
    property Caption;
    property Color default clBtnFace;
    property FocusControl;
    property Font;
    property ParentColor;
    property ParentFont;
    property ShowAccelChar;
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
  end;

var
  { called instead of MessageBeep(0) when assigned (a program that must stay silent, a test that counts) }
  RejectBeepHook: TRejectBeepHook;

implementation

uses
  RTLConsts, Forms, UIAutomation, Accessibility;

type
  { access to the protected PixelsPerInch / ActiveControl of a form }
  TFormAccess = class(TCustomForm);

  { ui-automation.md UIF-1411, UIF-1417, UIF-1421: the rules of the classes of this unit }
  TLabelAutomationProvider = class(TGraphicAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
    class function DefaultControlType(Control: TControl): Integer; override;
    class function LabelTarget(Control: TControl): TControl; override;
  end;

  { native: asked for its rules only (annotation, UIF-1414) }
  TStaticTextAutomationProvider = class(TWindowAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
    class function LabelTarget(Control: TControl): TControl; override;
  end;

  { native buttons, check boxes and radio buttons: their caption is their own text }
  TButtonControlAutomationProvider = class(TWindowAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
  end;

  TGroupBoxAutomationProvider = class(TWindowAutomationProvider)
  public
    class function OwnText(Control: TControl): string; override;
    class function DefaultControlType(Control: TControl): Integer; override;
  end;

class function TLabelAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
end;

class function TLabelAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_TextControlTypeId;
end;

class function TLabelAutomationProvider.LabelTarget(Control: TControl): TControl;
begin
  Result := TCustomLabel(Control).FocusControl;
end;

class function TStaticTextAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
end;

class function TStaticTextAutomationProvider.LabelTarget(Control: TControl): TControl;
begin
  Result := TCustomStaticText(Control).FocusControl;
end;

class function TButtonControlAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
end;

class function TGroupBoxAutomationProvider.OwnText(Control: TControl): string;
begin
  Result := ControlCaption(Control);
end;

class function TGroupBoxAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_GroupControlTypeId;
end;

const
  { posted by a combo box to itself: show the state before a rejected selection again, after the native control
    has finished the selection change (UIF-884) }
  CM_RESTORESELECTION = CM_BASE + 90;
  { the property of a combo box's edit part that holds its window procedure before ours }
  ComboEditProp = 'PN.ComboEdit';

{ UIF-882, UIF-884: the user's input was discarded }
procedure RejectBeep;
begin
  if Assigned(RejectBeepHook) then
    RejectBeepHook
  else
    MessageBeep(0);
end;

{ UIF-882: a WM_CHAR that modifies the text of an edit control: a printable character or Backspace; in a
  multi-line control also a line break and a tab. (Ctrl+V, Ctrl+X and Ctrl+Z arrive as WM_PASTE, WM_CUT, EM_UNDO.) }
function IsModifyingChar(Ch: Word; MultiLine: Boolean): Boolean;
begin
  Result := (Ch >= 32) or (Ch = 8) or (MultiLine and ((Ch = 9) or (Ch = 10) or (Ch = 13)));
end;

{ UIF-882: the Delete key modifies the text (Shift+Delete arrives as WM_CUT) }
function IsModifyingKey(Key: Word): Boolean;
begin
  Result := (Key = VK_DELETE) and (GetKeyState(VK_SHIFT) >= 0);
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

{ layout.md UIF-255: the caption measured with DrawTextW (DT_CALCRECT) in the control font on a screen DC; with
  DT_WORDBREAK the width W stays and only H is measured; an empty caption is tmHeight high (and 0 wide) }
procedure MeasureCaption(AFont: TFont; const Text: string; Flags: UINT; var W, H: Integer);
var
  DC: HDC;
  Old: HGDIOBJ;
  R: TRect;
  TM: TTextMetricW;
begin
  DC := GetDC(0);
  try
    Old := SelectObject(DC, AFont.Handle);
    if Text = '' then
    begin
      GetTextMetricsW(DC, TM);
      if Flags and DT_WORDBREAK = 0 then
        W := 0;
      H := TM.tmHeight;
    end
    else
    begin
      if Flags and DT_WORDBREAK <> 0 then
        R := Rect(0, 0, W, 0)
      else
        R := Rect(0, 0, 0, 0);
      DrawTextW(DC, PWideChar(Text), Length(Text), R, Flags or DT_CALCRECT);
      if Flags and DT_WORDBREAK = 0 then
        W := R.Right - R.Left;
      H := R.Bottom - R.Top;
    end;
    SelectObject(DC, Old);
  finally
    ReleaseDC(0, DC);
  end;
end;

{ layout.md UIF-255 position rule: a right-justified control keeps its right edge when the width changes }
procedure ApplyMeasuredSize(Control: TControl; Alignment: TAlignment; W, H: Integer);
var
  L: Integer;
begin
  L := Control.Left;
  if Alignment = taRightJustify then
    L := L + Control.Width - W;
  Control.SetBounds(L, Control.Top, W, H);
end;

{ ---------------------------------------------------------------- TCustomLabel }

constructor TCustomLabel.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FShowAccelChar := True;
  AutoSize := True;
  SetBounds(Left, Top, 34, 15);                               { after AutoSize: a new label has the default size }
  Color := clBtnFace;
  ParentColor := True;
  FTransparent := True;
  ControlStyle := ControlStyle - [csOpaque];
end;

function TCustomLabel.TextFormat: TTextFormat;
begin
  Result := [tfExpandTabs];
  case FAlignment of
    taRightJustify: Include(Result, tfRight);
    taCenter: Include(Result, tfCenter);
  else
    Include(Result, tfLeft);
  end;
  if FWordWrap then
    Include(Result, tfWordBreak);
  if not FShowAccelChar then
    Include(Result, tfNoPrefix);
end;

procedure TCustomLabel.Paint;
var
  R, Calc: TRect;
  Text: string;
  Offset: Integer;
begin
  R := ClientRect;
  if not FTransparent then
  begin
    Canvas.Brush.Style := bsSolid;
    Canvas.Brush.Color := Color;
    Canvas.FillRect(R);
  end;
  Canvas.Font := Font;
  if not Enabled then
    Canvas.Font.Color := clGrayText;
  Text := Caption;
  if FLayout <> tlTop then
  begin
    Calc := R;
    Canvas.TextRect(Calc, Text, TextFormat + [tfCalcRect]);
    Offset := R.Bottom - R.Top - (Calc.Bottom - Calc.Top);
    if FLayout = tlCenter then
      Offset := Offset div 2;
    OffsetRect(R, 0, Offset);
  end;
  Canvas.Brush.Style := bsClear;
  SetBkMode(Canvas.Handle, Windows.TRANSPARENT);             { the Transparent property hides the constant }
  Canvas.TextRect(R, Text, TextFormat);
end;

procedure TCustomLabel.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_DIALOGCHAR:
      if Enabled and FShowAccelChar and (FFocusControl <> nil) and IsAccel(Word(Message.WParam), Caption)
        and FFocusControl.CanFocus then
      begin
        FFocusControl.SetFocus;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_TEXTCHANGED, CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        AdjustSize;
        Invalidate;
      end;
    CM_ENABLEDCHANGED:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomLabel.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FFocusControl) then
    FFocusControl := nil;
end;

procedure TCustomLabel.AdjustSize;
var
  W, H: Integer;
  Flags: UINT;
begin
  if not MeasuringAllowed then
    Exit;
  Flags := DT_EXPANDTABS;
  if FWordWrap then
    Flags := Flags or DT_WORDBREAK;
  if not FShowAccelChar then
    Flags := Flags or DT_NOPREFIX;
  W := Width;
  H := Height;
  MeasureCaption(Font, Caption, Flags, W, H);
  ApplyMeasuredSize(Self, FAlignment, W, H);
end;

procedure TCustomLabel.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    AdjustSize;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetFocusControl(Value: TWinControl);
begin
  if FFocusControl <> Value then
  begin
    FFocusControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

procedure TCustomLabel.SetLayout(Value: TTextLayout);
begin
  if FLayout <> Value then
  begin
    FLayout := Value;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetShowAccelChar(Value: Boolean);
begin
  if FShowAccelChar <> Value then
  begin
    FShowAccelChar := Value;
    AdjustSize;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetTransparent(Value: Boolean);
begin
  if FTransparent <> Value then
  begin
    FTransparent := Value;
    if Value then
      ControlStyle := ControlStyle - [csOpaque]
    else
      ControlStyle := ControlStyle + [csOpaque];
    Invalidate;
  end;
end;

procedure TCustomLabel.SetWordWrap(Value: Boolean);
begin
  if FWordWrap <> Value then
  begin
    FWordWrap := Value;
    AdjustSize;
    Invalidate;
  end;
end;

{ ---------------------------------------------------------------- TCustomEdit }

constructor TCustomEdit.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAutoSelect := True;
  FBorderStyle := bsSingle;
  FHideSelection := True;
  AutoSize := True;
  TabStop := True;
  Color := clWindow;
  SetBounds(Left, Top, 121, 23);
end;

procedure TCustomEdit.CreateParams(var Params: TCreateParams);
const
  Alignments: array[TAlignment] of DWORD = (ES_LEFT, ES_RIGHT, ES_CENTER);
  Cases: array[TEditCharCase] of DWORD = (0, ES_UPPERCASE, ES_LOWERCASE);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'EDIT';
  Params.Style := Params.Style or ES_AUTOHSCROLL or Alignments[FAlignment] or Cases[FCharCase];
  if FPasswordChar <> #0 then
    Params.Style := Params.Style or ES_PASSWORD;
  if FReadOnly then
    Params.Style := Params.Style or ES_READONLY;
  if FNumbersOnly then
    Params.Style := Params.Style or ES_NUMBER;
  if not FHideSelection then
    Params.Style := Params.Style or ES_NOHIDESEL;
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
end;

procedure TCustomEdit.CreateWnd;
var
  S, L: Integer;
begin
  S := GetSelStart;
  L := GetSelLength;
  inherited CreateWnd;
  SendMessageW(Handle, EM_LIMITTEXT, WPARAM(FMaxLength), 0);
  if FPasswordChar <> #0 then
    SendMessageW(Handle, EM_SETPASSWORDCHAR, WPARAM(Ord(FPasswordChar)), 0);
  SendMessageW(Handle, EM_SETCUEBANNER, 0, LPARAM(PChar(FTextHint)));
  SendMessageW(Handle, EM_SETSEL, WPARAM(S), LPARAM(S + L));
  SendMessageW(Handle, EM_SETMODIFY, WPARAM(Ord(FModified)), 0);
end;

procedure TCustomEdit.DestroyWnd;
begin
  FSelStart := GetSelStart;
  FSelLength := GetSelLength;
  FModified := GetModified;
  inherited DestroyWnd;
end;

procedure TCustomEdit.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_COMMAND:
      if HiWord(Message.WParam) = EN_CHANGE then
      begin
        if not (csCreating in ControlState) then
          Change;
      end
      else
        inherited WndProc(Message);
    CM_TEXTCHANGED:
      begin
        inherited WndProc(Message);
        if not HandleAllocated then                          { with a handle EN_CHANGE reports it }
        begin
          FModified := False;
          Change;
        end;
      end;
    CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        AdjustHeight;
      end;
    { Ctrl+V, Ctrl+X and Ctrl+Z arrive as WM_PASTE, WM_CUT and EM_UNDO from the native control }
    WM_PASTE, WM_CUT, WM_CLEAR, WM_UNDO, EM_UNDO:
      if UserModify then
        inherited WndProc(Message);
  else
    inherited WndProc(Message);
  end;
end;

{ UIF-882: False = the modification is discarded (the user is told) }
function TCustomEdit.UserModify: Boolean;
begin
  FRejectSilently := False;
  Result := FReadOnly or EditCanModify;
  if not Result and not FRejectSilently then
    RejectBeep;
  FRejectSilently := False;
end;

function TCustomEdit.EditCanModify: Boolean;
begin
  Result := True;
end;

{ the key events have run and left the key (core.md UIF-085): the native control is about to apply it }
procedure TCustomEdit.DefaultHandler(var Message);
var
  M: PMessage;
  Modifies: Boolean;
begin
  M := @Message;
  case M^.Msg of
    WM_CHAR:
      Modifies := IsModifyingChar(Word(M^.WParam),
        HandleAllocated and ((GetWindowLongPtrW(Handle, GWL_STYLE) and ES_MULTILINE) <> 0));
    WM_KEYDOWN:
      Modifies := IsModifyingKey(Word(M^.WParam));
  else
    Modifies := False;
  end;
  if Modifies and not UserModify then
    M^.Result := 0
  else
    inherited DefaultHandler(Message);
end;

{ UIF-620: never while loading }
procedure TCustomEdit.Change;
begin
  if (csLoading in ComponentState) or (csCreating in ControlState) then
    Exit;
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

{ I2: Abs(Font.Height) + s(11) with a border, I3: + s(3) without }
procedure TCustomEdit.IntrinsicSize(var AWidth, AHeight: Integer);
begin
  { while loading, AutoSize/BorderStyle/Font may still follow the bounds in the file: Loaded applies the rule }
  if not AutoSize or (csLoading in ComponentState) then
    Exit;
  if FBorderStyle = bsSingle then
    AHeight := Abs(Font.Height) + LayoutMulDiv(11, ControlDPI(Self), 96)
  else
    AHeight := Abs(Font.Height) + LayoutMulDiv(3, ControlDPI(Self), 96);
end;

{ the intrinsic height depends on font, border, AutoSize and the form's DPI: re-apply the bounds }
procedure TCustomEdit.AdjustHeight;
begin
  SetBounds(Left, Top, Width, Height);
end;

procedure TCustomEdit.Loaded;
begin
  inherited Loaded;
  AdjustHeight;
end;

procedure TCustomEdit.SetParent(AParent: TWinControl);
begin
  inherited SetParent(AParent);
  if not (csDestroying in ComponentState) then
    AdjustHeight;
end;

procedure TCustomEdit.DoEnter;
begin
  if FAutoSelect and (GetKeyState(VK_LBUTTON) >= 0) then
    SelectAll;
  inherited DoEnter;
end;

procedure TCustomEdit.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    RecreateWnd;
  end;
end;

procedure TCustomEdit.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
    AdjustHeight;
  end;
end;

{ the existing text is converted }
procedure TCustomEdit.SetCharCase(Value: TEditCharCase);
begin
  if FCharCase <> Value then
  begin
    FCharCase := Value;
    RecreateWnd;
    case Value of
      ecUpperCase: Text := AnsiUpperCase(Text);
      ecLowerCase: Text := AnsiLowerCase(Text);
    end;
  end;
end;

procedure TCustomEdit.SetHideSelection(Value: Boolean);
begin
  if FHideSelection <> Value then
  begin
    FHideSelection := Value;
    ApplyHideSelection;
  end;
end;

procedure TCustomEdit.ApplyHideSelection;
begin
  RecreateWnd;
end;

procedure TCustomEdit.SetMaxLength(Value: Integer);
begin
  if FMaxLength <> Value then
  begin
    FMaxLength := Value;
    ApplyMaxLength;
  end;
end;

procedure TCustomEdit.ApplyMaxLength;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_LIMITTEXT, WPARAM(FMaxLength), 0);
end;

procedure TCustomEdit.SetNumbersOnly(Value: Boolean);
begin
  if FNumbersOnly <> Value then
  begin
    FNumbersOnly := Value;
    RecreateWnd;
  end;
end;

{ #0 <-> non-zero recreates (ES_PASSWORD); otherwise EM_SETPASSWORDCHAR }
procedure TCustomEdit.SetPasswordChar(Value: Char);
var
  Recreate: Boolean;
begin
  if FPasswordChar <> Value then
  begin
    Recreate := (FPasswordChar = #0) <> (Value = #0);
    FPasswordChar := Value;
    if Recreate then
      RecreateWnd
    else if HandleAllocated then
    begin
      SendMessageW(Handle, EM_SETPASSWORDCHAR, WPARAM(Ord(Value)), 0);
      Invalidate;
    end;
  end;
end;

procedure TCustomEdit.SetReadOnly(Value: Boolean);
begin
  if FReadOnly <> Value then
  begin
    FReadOnly := Value;
    if HandleAllocated then
      SendMessageW(Handle, EM_SETREADONLY, WPARAM(Ord(Value)), 0);
  end;
end;

procedure TCustomEdit.SetTextHint(const Value: string);
begin
  if FTextHint <> Value then
  begin
    FTextHint := Value;
    if HandleAllocated then
      SendMessageW(Handle, EM_SETCUEBANNER, 0, LPARAM(PChar(FTextHint)));
  end;
end;

{ UIF-621: the fields are clamped to the text length }
function TCustomEdit.GetSelStart: Integer;
var
  A, B: DWORD;
begin
  if HandleAllocated then
  begin
    A := 0;
    B := 0;
    SendMessageW(Handle, EM_GETSEL, WPARAM(@A), LPARAM(@B));
    Result := Integer(A);
  end
  else
  begin
    Result := FSelStart;
    if Result > Length(Text) then
      Result := Length(Text);
  end;
end;

function TCustomEdit.GetSelLength: Integer;
var
  A, B: DWORD;
begin
  if HandleAllocated then
  begin
    A := 0;
    B := 0;
    SendMessageW(Handle, EM_GETSEL, WPARAM(@A), LPARAM(@B));
    Result := Integer(B) - Integer(A);
  end
  else
  begin
    Result := FSelLength;
    if Result > Length(Text) - GetSelStart then
      Result := Length(Text) - GetSelStart;
  end;
end;

procedure TCustomEdit.SetSelection(AStart, ALength: Integer);
begin
  if AStart < 0 then
    AStart := 0;
  if ALength < 0 then
    ALength := 0;
  if HandleAllocated then
    SendMessageW(Handle, EM_SETSEL, WPARAM(AStart), LPARAM(AStart + ALength))
  else
  begin
    FSelStart := AStart;
    FSelLength := ALength;
  end;
end;

procedure TCustomEdit.SetSelStart(Value: Integer);
begin
  SetSelection(Value, 0);
end;

procedure TCustomEdit.SetSelLength(Value: Integer);
begin
  SetSelection(GetSelStart, Value);
end;

function TCustomEdit.GetSelText: string;
begin
  Result := Copy(Text, GetSelStart + 1, GetSelLength);
end;

{ handle: EM_REPLACESEL (undoable); else the text field, caret after the inserted text }
procedure TCustomEdit.SetSelText(const Value: string);
var
  S, L: Integer;
  T: string;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_REPLACESEL, 1, LPARAM(PChar(Value)))
  else
  begin
    S := GetSelStart;
    L := GetSelLength;
    T := Text;
    System.Delete(T, S + 1, L);
    System.Insert(Value, T, S + 1);
    Text := T;
    FSelStart := S + Length(Value);
    FSelLength := 0;
  end;
end;

function TCustomEdit.GetModified: Boolean;
begin
  if HandleAllocated then
    Result := SendMessageW(Handle, EM_GETMODIFY, 0, 0) <> 0
  else
    Result := FModified;
end;

procedure TCustomEdit.SetModified(Value: Boolean);
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_SETMODIFY, WPARAM(Ord(Value)), 0)
  else
    FModified := Value;
end;

function TCustomEdit.GetCanUndo: Boolean;
begin
  Result := HandleAllocated and (SendMessageW(Handle, EM_CANUNDO, 0, 0) <> 0);
end;

procedure TCustomEdit.Clear;
begin
  Text := '';
end;

procedure TCustomEdit.ClearSelection;
begin
  SetSelText('');
end;

procedure TCustomEdit.SelectAll;
begin
  SetSelection(0, Length(Text));
end;

procedure TCustomEdit.Undo;
begin
  if HandleAllocated then
    SendMessageW(Handle, EM_UNDO, 0, 0);
end;

procedure TCustomEdit.CopyToClipboard;
begin
  if HandleAllocated then
    SendMessageW(Handle, WM_COPY, 0, 0);
end;

procedure TCustomEdit.CutToClipboard;
begin
  if HandleAllocated then
    SendMessageW(Handle, WM_CUT, 0, 0);
end;

procedure TCustomEdit.PasteFromClipboard;
begin
  if HandleAllocated then
    SendMessageW(Handle, WM_PASTE, 0, 0);
end;

{ ---------------------------------------------------------------- memo line helpers (UIF-630) }

{ logical lines of a raw memo text: separated by CR LF; the empty text has none }
function LineCountOf(const S: string): Integer;
var
  I: Integer;
begin
  if S = '' then
  begin
    Result := 0;
    Exit;
  end;
  Result := 1;
  for I := 1 to Length(S) - 1 do
    if (S[I] = #13) and (S[I + 1] = #10) then
      Inc(Result);
end;

{ 0-based offset of the first character of line Index (0 <= Index < LineCountOf(S)) }
function LineStartOf(const S: string; Index: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  I := 1;
  while (Index > 0) and (I < Length(S)) do
  begin
    if (S[I] = #13) and (S[I + 1] = #10) then
    begin
      Dec(Index);
      Inc(I);
      Result := I;
    end;
    Inc(I);
  end;
end;

{ 0-based offset just after the last character of line Index (before its CR LF) }
function LineEndOf(const S: string; Index: Integer): Integer;
var
  I: Integer;
begin
  I := LineStartOf(S, Index) + 1;
  while I < Length(S) do
  begin
    if (S[I] = #13) and (S[I + 1] = #10) then
    begin
      Result := I - 1;
      Exit;
    end;
    Inc(I);
  end;
  Result := Length(S);
end;

{ the text of a memo as TStrings sees it: a trailing line break added (RTL-420) }
function LinesTextOf(const S: string): string;
begin
  if S = '' then
    Result := ''
  else
    Result := S + #13#10;
end;

{ ---------------------------------------------------------------- TMemoStrings }

destructor TMemoStrings.Destroy;
begin
  FList.Free;
  inherited Destroy;
end;

{ handle: EM_SETSEL + EM_REPLACESEL; never called without one }
procedure TMemoStrings.Replace(AStart, ALength: Integer; const S: string);
begin
  SendMessageW(FMemo.Handle, EM_SETSEL, WPARAM(AStart), LPARAM(AStart + ALength));
  SendMessageW(FMemo.Handle, EM_REPLACESEL, 0, LPARAM(PChar(S)));
end;

{ the handle-less list became the memo's text }
procedure TMemoStrings.Synchronize;
var
  S: string;
begin
  S := FList.Text;
  if (Length(S) >= 2) and (Copy(S, Length(S) - 1, 2) = #13#10) then
    SetLength(S, Length(S) - 2);
  FSyncing := True;
  try
    FMemo.Text := S;
  finally
    FSyncing := False;
  end;
end;

function TMemoStrings.Get(Index: Integer): string;
var
  S: string;
begin
  if not FMemo.HandleAllocated then
  begin
    Result := FList[Index];
    Exit;
  end;
  S := FMemo.Text;
  if (Index < 0) or (Index >= LineCountOf(S)) then
    Error(SListIndexError, Index);
  Result := Copy(S, LineStartOf(S, Index) + 1, LineEndOf(S, Index) - LineStartOf(S, Index));
end;

function TMemoStrings.GetCount: Integer;
begin
  if FMemo.HandleAllocated then
    Result := LineCountOf(FMemo.Text)
  else
    Result := FList.Count;
end;

procedure TMemoStrings.Put(Index: Integer; const S: string);
var
  T: string;
begin
  if not FMemo.HandleAllocated then
  begin
    FList[Index] := S;
    Synchronize;
    Exit;
  end;
  T := FMemo.Text;
  if (Index < 0) or (Index >= LineCountOf(T)) then
    Error(SListIndexError, Index);
  Replace(LineStartOf(T, Index), LineEndOf(T, Index) - LineStartOf(T, Index), S);
end;

procedure TMemoStrings.Insert(Index: Integer; const S: string);
var
  T: string;
  N: Integer;
begin
  if not FMemo.HandleAllocated then
  begin
    FList.Insert(Index, S);
    Synchronize;
    Exit;
  end;
  T := FMemo.Text;
  N := LineCountOf(T);
  if (Index < 0) or (Index > N) then
    Error(SListIndexError, Index);
  if N = 0 then
    Replace(0, 0, S)
  else if Index < N then
    Replace(LineStartOf(T, Index), 0, S + #13#10)
  else
    Replace(Length(T), 0, #13#10 + S);
end;

procedure TMemoStrings.Delete(Index: Integer);
var
  T: string;
  N, A: Integer;
begin
  if not FMemo.HandleAllocated then
  begin
    FList.Delete(Index);
    Synchronize;
    Exit;
  end;
  T := FMemo.Text;
  N := LineCountOf(T);
  if (Index < 0) or (Index >= N) then
    Error(SListIndexError, Index);
  if N = 1 then
    Replace(0, Length(T), '')
  else if Index < N - 1 then
  begin
    A := LineStartOf(T, Index);
    Replace(A, LineStartOf(T, Index + 1) - A, '');
  end
  else
  begin
    A := LineEndOf(T, Index - 1);
    Replace(A, Length(T) - A, '');
  end;
end;

procedure TMemoStrings.Clear;
begin
  if not FMemo.HandleAllocated then
  begin
    FList.Clear;
    Synchronize;
  end
  else
    FMemo.Text := '';
end;

function TMemoStrings.GetTextStr: string;
begin
  if FMemo.HandleAllocated then
    Result := LinesTextOf(FMemo.Text)
  else
    Result := FList.Text;
end;

{ a trailing line break is removed (RTL-420) }
procedure TMemoStrings.SetTextStr(const Value: string);
var
  S: string;
begin
  if not FMemo.HandleAllocated then
  begin
    FList.Text := Value;
    Synchronize;
    Exit;
  end;
  S := Value;
  if (Length(S) >= 2) and (Copy(S, Length(S) - 1, 2) = #13#10) then
    SetLength(S, Length(S) - 2);
  FMemo.Text := S;
end;

{ ---------------------------------------------------------------- TCustomMemo }

constructor TCustomMemo.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FLines := CreateLines;
  FWantReturns := True;
  FWordWrap := True;
  AutoSize := False;
  SetBounds(Left, Top, 185, 89);
end;

destructor TCustomMemo.Destroy;
begin
  inherited Destroy;
  FLines.Free;
end;

function TCustomMemo.CreateLines: TStrings;
var
  L: TMemoStrings;
begin
  L := TMemoStrings.Create;
  L.FMemo := Self;
  L.FList := TStringList.Create;
  Result := L;
end;

procedure TCustomMemo.CreateParams(var Params: TCreateParams);
const
  Bars: array[TScrollStyle] of DWORD = (0, WS_HSCROLL, WS_VSCROLL, WS_HSCROLL or WS_VSCROLL);
begin
  inherited CreateParams(Params);
  Params.Style := Params.Style or ES_MULTILINE or ES_AUTOVSCROLL or Bars[FScrollBars];
  if FWordWrap then
    Params.Style := Params.Style and not DWORD(ES_AUTOHSCROLL);
  if FWantReturns then
    Params.Style := Params.Style or ES_WANTRETURN;
end;

procedure TCustomMemo.DestroyWnd;
begin
  inherited DestroyWnd;
  if FLines is TMemoStrings then
    TMemoStrings(FLines).FList.Text := LinesTextOf(Text);
end;

procedure TCustomMemo.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_WANTSPECIALKEY:
      case Message.WParam of
        VK_RETURN:
          Message.Result := Ord(FWantReturns or (GetKeyState(VK_CONTROL) < 0));
        VK_TAB:
          Message.Result := Ord(FWantTabs);
      else
        inherited WndProc(Message);
      end;
    WM_GETDLGCODE:
      begin
        inherited WndProc(Message);
        Message.Result := Message.Result and not (DLGC_WANTALLKEYS or DLGC_WANTTAB);
        if FWantTabs then
          Message.Result := Message.Result or DLGC_WANTTAB;
      end;
    CM_TEXTCHANGED:
      begin
        if not HandleAllocated and (FLines is TMemoStrings) and not TMemoStrings(FLines).FSyncing then
          TMemoStrings(FLines).FList.Text := LinesTextOf(Text);
        inherited WndProc(Message);
        { UIF-620: a multi-line edit sends no EN_CHANGE for WM_SETTEXT, so a Text assignment that changed the text
          reports it here (U-133) }
        if HandleAllocated and not (csCreating in ControlState) then
          Change;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomMemo.DefaultHandler(var Message);
var
  M: PMessage;
begin
  M := @Message;
  if ((M^.Msg = WM_KEYDOWN) or (M^.Msg = WM_CHAR)) and (M^.WParam = VK_ESCAPE) then
    M^.Result := 0
  else
    inherited DefaultHandler(Message);
end;

procedure TCustomMemo.IntrinsicSize(var AWidth, AHeight: Integer);
begin
end;

procedure TCustomMemo.SetLines(Value: TStrings);
begin
  FLines.Assign(Value);
end;

procedure TCustomMemo.SetScrollBars(Value: TScrollStyle);
begin
  if FScrollBars <> Value then
  begin
    FScrollBars := Value;
    RecreateWnd;
  end;
end;

procedure TCustomMemo.SetWantReturns(Value: Boolean);
begin
  if FWantReturns <> Value then
  begin
    FWantReturns := Value;
    RecreateWnd;
  end;
end;

procedure TCustomMemo.SetWordWrap(Value: Boolean);
begin
  if FWordWrap <> Value then
  begin
    FWordWrap := Value;
    RecreateWnd;
  end;
end;

{ (column, logical line) of the caret = start of the selection, 0-based }
function TCustomMemo.GetCaretPos: TPoint;
var
  S: string;
  P, I: Integer;
begin
  S := Text;
  P := SelStart;
  Result.Y := 0;
  Result.X := 0;
  I := 1;
  while I <= P do
  begin
    if (I < Length(S)) and (S[I] = #13) and (S[I + 1] = #10) and (I + 1 <= P) then
    begin
      Inc(Result.Y);
      Result.X := 0;
      Inc(I, 2);
    end
    else
    begin
      Inc(Result.X);
      Inc(I);
    end;
  end;
end;

procedure TCustomMemo.SetCaretPos(const Value: TPoint);
var
  S: string;
  N, A, E: Integer;
begin
  S := Text;
  N := LineCountOf(S);
  if N = 0 then
  begin
    SetSelection(0, 0);
    Exit;
  end;
  if Value.Y >= N then
  begin
    SetSelection(Length(S), 0);
    Exit;
  end;
  A := LineStartOf(S, Value.Y);
  E := LineEndOf(S, Value.Y);
  if A + Value.X < E then
    E := A + Value.X;
  SetSelection(E, 0);
end;

{ ---------------------------------------------------------------- TButtonControl }

constructor TButtonControl.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { the native control reports clicks with BN_CLICKED: no click from the mouse messages as well }
  ControlStyle := ControlStyle - [csClickEvents];
  TabStop := True;
end;

function TButtonControl.GetActionLinkClass: TControlActionLinkClass;
begin
  Result := TButtonActionLink;
end;

procedure TButtonControl.Click;
begin
  if not FClicksDisabled then
    inherited Click;
end;

procedure TButtonControl.SetCheckedFromAction(Value: Boolean);
begin
  FClicksDisabled := True;
  try
    if Self is TCustomCheckBox then
      TCustomCheckBox(Self).Checked := Value
    else if Self is TRadioButton then
      TRadioButton(Self).Checked := Value;
  finally
    FClicksDisabled := False;
  end;
end;

{ ---------------------------------------------------------------- TButtonActionLink }

function TButtonActionLink.IsCheckedLinked: Boolean;
begin
  if FClient is TCustomCheckBox then
    Result := TCustomCheckBox(FClient).Checked = FChecked
  else if FClient is TRadioButton then
    Result := TRadioButton(FClient).Checked = FChecked
  else
    Result := False;
end;

procedure TButtonActionLink.SetChecked(Value: Boolean);
begin
  if FClient is TButtonControl then
    TButtonControl(FClient).SetCheckedFromAction(Value);
end;

procedure TButtonControl.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'BUTTON';
  if FWordWrap then
    Params.Style := Params.Style or BS_MULTILINE;
end;

procedure TButtonControl.SetWordWrap(Value: Boolean);
begin
  if FWordWrap <> Value then
  begin
    FWordWrap := Value;
    RecreateWnd;
  end;
end;

{ ---------------------------------------------------------------- TButton }

{ the focused control of the button's form }
function FormActiveControl(Control: TControl): TWinControl;
var
  F: TCustomForm;
begin
  F := GetParentForm(Control);
  if F <> nil then
    Result := TFormAccess(F).ActiveControl
  else
    Result := nil;
end;

{ UIF-643: visible and enabled up to the form }
function Usable(Control: TControl): Boolean;
begin
  Result := True;
  while Control <> nil do
  begin
    if not (Control.Visible and Control.Enabled) then
    begin
      Result := False;
      Exit;
    end;
    Control := Control.Parent;
  end;
end;

constructor TButton.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 75, 25);
end;

procedure TButton.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  FActive := (FormActiveControl(Self) = Self) or (FDefault and not (FormActiveControl(Self) is TButton));
  if FActive then
    Params.Style := Params.Style or BS_DEFPUSHBUTTON
  else
    Params.Style := Params.Style or BS_PUSHBUTTON;
end;

{ UIF-641 }
procedure TButton.UpdateActive(Focused: TWinControl);
var
  A: Boolean;
begin
  A := (Focused = Self) or (FDefault and not (Focused is TButton));
  if A <> FActive then
  begin
    FActive := A;
    if HandleAllocated then
      if A then
        SendMessageW(Handle, BM_SETSTYLE, BS_DEFPUSHBUTTON, 1)
      else
        SendMessageW(Handle, BM_SETSTYLE, BS_PUSHBUTTON, 1);
  end;
end;

procedure TButton.SetDefault(Value: Boolean);
begin
  if FDefault <> Value then
  begin
    FDefault := Value;
    UpdateActive(FormActiveControl(Self));
  end;
end;

{ UIF-642: Self is the first usable button of its form in tab order with Default (Cancel) set }
function TButton.IsFirst(Defaults: Boolean): Boolean;
var
  F: TCustomForm;
  List: TList;
  I: Integer;
  B: TButton;
begin
  F := GetParentForm(Self);
  if F = nil then
  begin
    Result := Usable(Self);
    Exit;
  end;
  Result := False;
  List := TList.Create;
  try
    F.GetTabOrderList(List);
    for I := 0 to List.Count - 1 do
      if TObject(List[I]) is TButton then
      begin
        B := TButton(List[I]);
        if ((Defaults and B.FDefault) or (not Defaults and B.FCancel)) and Usable(B) then
        begin
          Result := B = Self;
          Exit;
        end;
      end;
  finally
    List.Free;
  end;
end;

procedure TButton.WndProc(var Message: TMessage);
var
  Focused: TWinControl;
begin
  case Message.Msg of
    CN_COMMAND:
      if HiWord(Message.WParam) = BN_CLICKED then
        Click
      else
        inherited WndProc(Message);
    { UIF-115: Return clicks the focused button, else the default button; Escape the cancel button }
    CM_DIALOGKEY:
      begin
        Message.Result := 0;
        case Message.WParam of
          VK_RETURN:
            begin
              Focused := FormActiveControl(Self);
              if Focused is TButton then
              begin
                if (Focused = Self) and Usable(Self) then
                  Message.Result := 1;
              end
              else if FDefault and IsFirst(True) then
                Message.Result := 1;
            end;
          VK_ESCAPE, VK_CANCEL:
            if FCancel and IsFirst(False) then
              Message.Result := 1;
        end;
        if Message.Result <> 0 then
          Click;
      end;
    { UIF-120: focus + Click }
    CM_DIALOGCHAR:
      if IsAccel(Word(Message.WParam), Caption) and CanFocus then
      begin
        SetFocus;
        Click;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_FOCUSCHANGED:
      begin
        UpdateActive(TWinControl(Pointer(Message.LParam)));
        inherited WndProc(Message);
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ UIF-640: the form's ModalResult first (mrNone never touches it), then OnClick }
procedure TButton.Click;
var
  F: TCustomForm;
begin
  F := GetParentForm(Self);
  if (F <> nil) and (FModalResult <> mrNone) then
    F.ModalResult := FModalResult;
  inherited Click;
end;

{ ---------------------------------------------------------------- TCustomCheckBox }

const
  CheckStates: array[TCheckBoxState] of WPARAM = (BST_UNCHECKED, BST_CHECKED, BST_INDETERMINATE);

constructor TCustomCheckBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAlignment := taRightJustify;
  SetBounds(Left, Top, 97, 17);
end;

{ not the AUTO variants: state changes are ours }
procedure TCustomCheckBox.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  if FAllowGrayed then
    Params.Style := Params.Style or BS_3STATE
  else
    Params.Style := Params.Style or BS_CHECKBOX;
  if FAlignment = taLeftJustify then
    Params.Style := Params.Style or BS_LEFTTEXT;
end;

procedure TCustomCheckBox.CreateWnd;
begin
  inherited CreateWnd;
  SendMessageW(Handle, BM_SETCHECK, CheckStates[FState], 0);
end;

procedure TCustomCheckBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_COMMAND:
      if HiWord(Message.WParam) = BN_CLICKED then
        Toggle
      else
        inherited WndProc(Message);
    CM_DIALOGCHAR:
      if IsAccel(Word(Message.WParam), Caption) and CanFocus then
      begin
        SetFocus;
        Toggle;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomCheckBox.Toggle;
begin
  case FState of
    cbUnchecked: State := cbChecked;
    cbChecked:
      if FAllowGrayed then
        State := cbGrayed
      else
        State := cbUnchecked;
  else
    State := cbUnchecked;
  end;
end;

{ UIF-651: OnClick on every change, by the user or by code, except while loading }
procedure TCustomCheckBox.SetState(Value: TCheckBoxState);
begin
  if FState <> Value then
  begin
    FState := Value;
    if HandleAllocated then
      SendMessageW(Handle, BM_SETCHECK, CheckStates[Value], 0);
    if not (csLoading in ComponentState) then
      Click;
  end;
end;

function TCustomCheckBox.GetChecked: Boolean;
begin
  Result := FState = cbChecked;
end;

procedure TCustomCheckBox.SetChecked(Value: Boolean);
begin
  if Value then
    State := cbChecked
  else
    State := cbUnchecked;
end;

procedure TCustomCheckBox.ActionChange(Sender: TObject; CheckDefaults: Boolean);
begin
  inherited ActionChange(Sender, CheckDefaults);
  if (Sender is TCustomAction) and (not CheckDefaults or not Checked) then
    SetCheckedFromAction(TCustomAction(Sender).Checked);
end;

procedure TCustomCheckBox.SetAlignment(Value: TLeftRight);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    RecreateWnd;
  end;
end;

procedure TCustomCheckBox.SetAllowGrayed(Value: Boolean);
begin
  if FAllowGrayed <> Value then
  begin
    FAllowGrayed := Value;
    RecreateWnd;
  end;
end;

{ ---------------------------------------------------------------- TRadioButton }

constructor TRadioButton.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAlignment := taRightJustify;
  TabStop := False;
  SetBounds(Left, Top, 113, 17);
end;

procedure TRadioButton.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := Params.Style or BS_RADIOBUTTON;
  if FAlignment = taLeftJustify then
    Params.Style := Params.Style or BS_LEFTTEXT;
end;

procedure TRadioButton.CreateWnd;
begin
  inherited CreateWnd;
  SendMessageW(Handle, BM_SETCHECK, WPARAM(Ord(FChecked)), 0);
end;

{ the field, the window and TabStop (True while checked); no event }
procedure TRadioButton.SetCheckedState(Value: Boolean);
begin
  FChecked := Value;
  TabStop := Value;
  if HandleAllocated then
    SendMessageW(Handle, BM_SETCHECK, WPARAM(Ord(Value)), 0);
end;

{ UIF-660: checking unchecks the rest of the group (same Parent) silently, then fires OnClick; unchecking by code
  fires nothing. While loading the group is resolved in Loaded (UIF-662) }
procedure TRadioButton.SetChecked(Value: Boolean);
var
  I: Integer;
  C: TControl;
begin
  if FChecked = Value then
    Exit;
  SetCheckedState(Value);
  if not Value or (csLoading in ComponentState) then
    Exit;
  if Parent <> nil then
    for I := 0 to Parent.ControlCount - 1 do
    begin
      C := Parent.Controls[I];
      if (C <> Self) and (C is TRadioButton) and TRadioButton(C).FChecked then
        TRadioButton(C).SetCheckedState(False);
    end;
  Click;
end;

procedure TRadioButton.Loaded;
var
  I: Integer;
  C: TControl;
  After: Boolean;
begin
  inherited Loaded;
  if not FChecked or (Parent = nil) then
    Exit;
  After := False;
  for I := 0 to Parent.ControlCount - 1 do
  begin
    C := Parent.Controls[I];
    if C = Self then
      After := True
    else if After and (C is TRadioButton) and TRadioButton(C).FChecked then
    begin
      SetCheckedState(False);
      Exit;
    end;
  end;
end;

procedure TRadioButton.ActionChange(Sender: TObject; CheckDefaults: Boolean);
begin
  inherited ActionChange(Sender, CheckDefaults);
  if (Sender is TCustomAction) and (not CheckDefaults or not FChecked) then
    SetCheckedFromAction(TCustomAction(Sender).Checked);
end;

procedure TRadioButton.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_COMMAND:
      if HiWord(Message.WParam) = BN_CLICKED then
        Checked := True
      else
        inherited WndProc(Message);
    CM_DIALOGCHAR:
      if IsAccel(Word(Message.WParam), Caption) and CanFocus then
      begin
        SetFocus;
        Checked := True;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
  else
    inherited WndProc(Message);
  end;
end;

procedure TRadioButton.SetAlignment(Value: TLeftRight);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    RecreateWnd;
  end;
end;

{ ---------------------------------------------------------------- TCustomGroupBox }

constructor TCustomGroupBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  SetBounds(Left, Top, 185, 105);
  Color := clBtnFace;
  ParentColor := True;
end;

{ UIF-670: CapH = Abs(Font.Height) + s(5) }
function TCustomGroupBox.CaptionHeight: Integer;
begin
  Result := Abs(Font.Height) + LayoutMulDiv(5, ControlDPI(Self), 96);
end;

procedure TCustomGroupBox.AdjustClientRect(var Rect: TRect);
var
  S2: Integer;
begin
  S2 := LayoutMulDiv(2, ControlDPI(Self), 96);
  Rect.Left := Rect.Left + S2;
  Rect.Top := Rect.Top + CaptionHeight;
  Rect.Right := Rect.Right - S2;
  Rect.Bottom := Rect.Bottom - S2;
end;

procedure TCustomGroupBox.Paint;
var
  R, T: TRect;
  DPI, S2, S8, W, H: Integer;
  Text: string;
begin
  DPI := ControlDPI(Self);
  S2 := LayoutMulDiv(2, DPI, 96);
  S8 := LayoutMulDiv(8, DPI, 96);
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := Color;
  Canvas.FillRect(Rect(0, 0, Width, Height));
  R := Rect(0, CaptionHeight div 2, Width, Height);
  DrawEdge(Canvas.Handle, R, EDGE_ETCHED, BF_RECT);
  Text := Caption;
  if Text = '' then
    Exit;
  Canvas.Font := Font;
  if not Enabled then
    Canvas.Font.Color := clGrayText;
  T := Rect(0, 0, 0, 0);
  Canvas.TextRect(T, Text, [tfSingleLine, tfCalcRect]);
  W := T.Right - T.Left;
  H := T.Bottom - T.Top;
  Canvas.FillRect(Rect(S8 - S2, 0, S8 + W + S2, H));
  T := Rect(S8, 0, S8 + W, H);
  Canvas.Brush.Style := bsClear;
  SetBkMode(Canvas.Handle, Windows.TRANSPARENT);
  Canvas.TextRect(T, Text, [tfSingleLine, tfLeft, tfTop]);
end;

procedure TCustomGroupBox.WndProc(var Message: TMessage);
var
  F: TWinControl;
begin
  case Message.Msg of
    CM_DIALOGCHAR:
      if IsAccel(Word(Message.WParam), Caption) then
      begin
        { SelectFirst, consumed when a child took the focus }
        F := FindNextControl(nil, True, True, False);
        if F <> nil then
        begin
          F.SetFocus;
          Message.Result := 1;
        end
        else
          Message.Result := 0;
      end
      else
        inherited WndProc(Message);
    CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        Realign;
        Invalidate;
      end;
    CM_TEXTCHANGED, CM_ENABLEDCHANGED:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ ---------------------------------------------------------------- TListBoxStrings }

const
  SListBoxNoSpace = 'Not enough memory for the list box items';

{ the index at which a sorted native list (locale-aware, case-insensitive) inserts S: after equal items }
function SortedInsertIndex(List: TStrings; const S: string): Integer;
begin
  Result := 0;
  while (Result < List.Count) and (AnsiCompareText(List[Result], S) <= 0) do
    Inc(Result);
end;

function TListBoxStrings.Get(Index: Integer): string;
var
  N: Integer;
begin
  if not FListBox.HandleAllocated then
  begin
    Result := FListBox.FSaved[Index];
    Exit;
  end;
  N := Integer(SendMessageW(FListBox.Handle, LB_GETTEXTLEN, WPARAM(Index), 0));
  if N = LB_ERR then
    Error(SListIndexError, Index);
  SetLength(Result, N + 1);
  N := Integer(SendMessageW(FListBox.Handle, LB_GETTEXT, WPARAM(Index), LPARAM(PChar(Result))));
  if N < 0 then
    N := 0;
  SetLength(Result, N);
end;

function TListBoxStrings.GetCount: Integer;
begin
  if FListBox.HandleAllocated then
    Result := Integer(SendMessageW(FListBox.Handle, LB_GETCOUNT, 0, 0))
  else
    Result := FListBox.FSaved.Count;
end;

function TListBoxStrings.GetObject(Index: Integer): TObject;
var
  D: LRESULT;
begin
  if not FListBox.HandleAllocated then
  begin
    Result := FListBox.FSaved.Objects[Index];
    Exit;
  end;
  D := SendMessageW(FListBox.Handle, LB_GETITEMDATA, WPARAM(Index), 0);
  if D = LB_ERR then
    Error(SListIndexError, Index);
  Result := TObject(Pointer(D));
end;

procedure TListBoxStrings.PutObject(Index: Integer; AObject: TObject);
begin
  if not FListBox.HandleAllocated then
    FListBox.FSaved.Objects[Index] := AObject
  else if SendMessageW(FListBox.Handle, LB_SETITEMDATA, WPARAM(Index), LPARAM(Pointer(AObject))) = LB_ERR then
    Error(SListIndexError, Index);
end;

{ the object and the selection of the item stay }
procedure TListBoxStrings.Put(Index: Integer; const S: string);
var
  Obj: TObject;
  Sel: Boolean;
begin
  if FListBox.FSorted then
    raise EStringListError.Create(SSortedListError);
  Obj := GetObject(Index);
  Sel := FListBox.Selected[Index];
  if not FListBox.HandleAllocated then
  begin
    FListBox.FSaved[Index] := S;
    Exit;
  end;
  SendMessageW(FListBox.Handle, LB_DELETESTRING, WPARAM(Index), 0);
  SendMessageW(FListBox.Handle, LB_INSERTSTRING, WPARAM(Index), LPARAM(PChar(S)));
  SendMessageW(FListBox.Handle, LB_SETITEMDATA, WPARAM(Index), LPARAM(Pointer(Obj)));
  if Sel then
    FListBox.Selected[Index] := True;
end;

procedure TListBoxStrings.SetUpdateState(Updating: Boolean);
begin
  if not FListBox.HandleAllocated then
    Exit;
  SendMessageW(FListBox.Handle, WM_SETREDRAW, WPARAM(Ord(not Updating)), 0);
  if not Updating then
    FListBox.Invalidate;
end;

function TListBoxStrings.Add(const S: string): Integer;
begin
  if not FListBox.HandleAllocated then
  begin
    if FListBox.FSorted then
      Result := FListBox.SortedIndex(S)
    else
      Result := FListBox.FSaved.Count;
    FListBox.SavedInsert(Result, S, nil, False);
    Exit;
  end;
  Result := Integer(SendMessageW(FListBox.Handle, LB_ADDSTRING, 0, LPARAM(PChar(S))));
  if Result < 0 then
    raise EOutOfResources.Create(SListBoxNoSpace);
end;

procedure TListBoxStrings.Insert(Index: Integer; const S: string);
begin
  if FListBox.FSorted then
    raise EStringListError.Create(SSortedListError);
  if (Index < 0) or (Index > GetCount) then
    Error(SListIndexError, Index);
  if not FListBox.HandleAllocated then
    FListBox.SavedInsert(Index, S, nil, False)
  else if SendMessageW(FListBox.Handle, LB_INSERTSTRING, WPARAM(Index), LPARAM(PChar(S))) < 0 then
    raise EOutOfResources.Create(SListBoxNoSpace);
end;

procedure TListBoxStrings.Delete(Index: Integer);
begin
  if not FListBox.HandleAllocated then
  begin
    FListBox.FSaved.Delete(Index);
    System.Delete(FListBox.FSavedSel, Index + 1, 1);
  end
  else if SendMessageW(FListBox.Handle, LB_DELETESTRING, WPARAM(Index), 0) = LB_ERR then
    Error(SListIndexError, Index);
end;

procedure TListBoxStrings.Clear;
begin
  if FListBox.HandleAllocated then
    SendMessageW(FListBox.Handle, LB_RESETCONTENT, 0, 0)
  else
  begin
    FListBox.FSaved.Clear;
    FListBox.FSavedSel := '';
  end;
end;

{ ---------------------------------------------------------------- TCustomListBox }

constructor TCustomListBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { UIF-691: the native notifications drive OnClick and OnDblClick }
  ControlStyle := ControlStyle - [csClickEvents, csDoubleClicks, csSetCaption];
  FSaved := TStringList.Create;
  FItems := TListBoxStrings.Create;
  TListBoxStrings(FItems).FListBox := Self;
  FBorderStyle := bsSingle;
  FExtendedSelect := True;
  FItemHeight := 15;
  FSavedCaret := -1;
  TabStop := True;
  Color := clWindow;
  SetBounds(Left, Top, 121, 97);
end;

destructor TCustomListBox.Destroy;
begin
  inherited Destroy;
  FItems.Free;
  FSaved.Free;
end;

procedure TCustomListBox.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'LISTBOX';
  Params.Style := Params.Style or LBS_NOTIFY or LBS_HASSTRINGS or LBS_NOINTEGRALHEIGHT or WS_VSCROLL;
  if FSorted then
    Params.Style := Params.Style or LBS_SORT;
  if FMultiSelect then
    if FExtendedSelect then
      Params.Style := Params.Style or LBS_EXTENDEDSEL
    else
      Params.Style := Params.Style or LBS_MULTIPLESEL;
  if FColumns > 0 then
    Params.Style := Params.Style or LBS_MULTICOLUMN or WS_HSCROLL;
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
end;

procedure TCustomListBox.CreateWnd;
var
  I, N: Integer;
  Sel: Integer;
begin
  inherited CreateWnd;
  Sel := -1;
  SendMessageW(Handle, WM_SETREDRAW, 0, 0);
  try
    for I := 0 to FSaved.Count - 1 do
    begin
      N := Integer(SendMessageW(Handle, LB_ADDSTRING, 0, LPARAM(PChar(FSaved[I]))));
      if N < 0 then
        Continue;
      SendMessageW(Handle, LB_SETITEMDATA, WPARAM(N), LPARAM(Pointer(FSaved.Objects[I])));
      if (Sel >= 0) and (N <= Sel) then                      { a sorted list inserted before the selected item }
        Inc(Sel);
      if FSavedSel[I + 1] = '1' then
        if FMultiSelect then
          SendMessageW(Handle, LB_SETSEL, 1, LPARAM(N))
        else
          Sel := N;
    end;
    if Sel >= 0 then
      SendMessageW(Handle, LB_SETCURSEL, WPARAM(Sel), 0);
    if FMultiSelect and (FSavedCaret >= 0) and (FSavedCaret < FSaved.Count) then
      SendMessageW(Handle, LB_SETCARETINDEX, WPARAM(FSavedCaret), 0);
    if FSavedTop > 0 then
      SendMessageW(Handle, LB_SETTOPINDEX, WPARAM(FSavedTop), 0);
  finally
    SendMessageW(Handle, WM_SETREDRAW, 1, 0);
  end;
  FSaved.Clear;
  FSavedSel := '';
  UpdateColumnWidth;
end;

procedure TCustomListBox.DestroyWnd;
var
  I, N: Integer;
begin
  FSaved.Clear;
  FSavedSel := '';
  N := Integer(SendMessageW(Handle, LB_GETCOUNT, 0, 0));
  for I := 0 to N - 1 do
    SavedInsert(I, FItems[I], FItems.Objects[I], SendMessageW(Handle, LB_GETSEL, WPARAM(I), 0) > 0);
  if FMultiSelect then
    FSavedCaret := Integer(SendMessageW(Handle, LB_GETCARETINDEX, 0, 0))
  else
    FSavedCaret := -1;
  FSavedTop := Integer(SendMessageW(Handle, LB_GETTOPINDEX, 0, 0));
  FItemHeight := Integer(SendMessageW(Handle, LB_GETITEMHEIGHT, 0, 0));
  inherited DestroyWnd;
end;

procedure TCustomListBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_COMMAND:
      case HiWord(Message.WParam) of
        LBN_SELCHANGE: Click;
        LBN_DBLCLK: DblClick;
      else
        inherited WndProc(Message);
      end;
    WM_SIZE:
      begin
        inherited WndProc(Message);
        UpdateColumnWidth;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ multi-column: the columns share the client width }
procedure TCustomListBox.UpdateColumnWidth;
var
  W: Integer;
begin
  if (FColumns > 0) and HandleAllocated then
  begin
    W := ClientWidth div FColumns;
    if W < 1 then
      W := 1;
    SendMessageW(Handle, LB_SETCOLUMNWIDTH, WPARAM(W), 0);
  end;
end;

procedure TCustomListBox.SavedInsert(Index: Integer; const S: string; AObject: TObject; Sel: Boolean);
begin
  FSaved.InsertObject(Index, S, AObject);
  if Sel then
    System.Insert('1', FSavedSel, Index + 1)
  else
    System.Insert('0', FSavedSel, Index + 1);
end;

function TCustomListBox.SortedIndex(const S: string): Integer;
begin
  Result := SortedInsertIndex(FSaved, S);
end;

{ Sorted became True without a handle: the saved items in the native order }
procedure TCustomListBox.SortSaved;
var
  Items: TStringList;
  Sel: string;
  I, N: Integer;
begin
  Items := TStringList.Create;
  try
    Items.Assign(FSaved);
    Sel := FSavedSel;
    FSaved.Clear;
    FSavedSel := '';
    for I := 0 to Items.Count - 1 do
    begin
      N := SortedIndex(Items[I]);
      SavedInsert(N, Items[I], Items.Objects[I], Sel[I + 1] = '1');
    end;
  finally
    Items.Free;
  end;
end;

procedure TCustomListBox.CheckIndex(Index: Integer);
begin
  if (Index < 0) or (Index >= GetCount) then
    TListBoxStrings(FItems).Error(SListIndexError, Index);
end;

function TCustomListBox.GetCount: Integer;
begin
  Result := FItems.Count;
end;

{ single selection: the selected item; multi-select: the focused item }
function TCustomListBox.GetItemIndex: Integer;
begin
  if HandleAllocated then
  begin
    if GetCount = 0 then
      Result := -1
    else if FMultiSelect then
      Result := Integer(SendMessageW(Handle, LB_GETCARETINDEX, 0, 0))
    else
      Result := Integer(SendMessageW(Handle, LB_GETCURSEL, 0, 0));
  end
  else if FMultiSelect then
  begin
    if FSavedCaret < FSaved.Count then
      Result := FSavedCaret
    else
      Result := -1;
  end
  else
    Result := Pos('1', FSavedSel) - 1;
end;

{ UIF-692: out of range is ignored; never an event (UIF-691) }
procedure TCustomListBox.SetItemIndex(Value: Integer);
var
  I: Integer;
begin
  if (Value < -1) or (Value >= GetCount) then
    Exit;
  if HandleAllocated then
  begin
    if not FMultiSelect then
      SendMessageW(Handle, LB_SETCURSEL, WPARAM(Value), 0)
    else if Value >= 0 then
      SendMessageW(Handle, LB_SETCARETINDEX, WPARAM(Value), 0);
  end
  else if FMultiSelect then
    FSavedCaret := Value
  else
  begin
    for I := 1 to Length(FSavedSel) do
      FSavedSel[I] := '0';
    if Value >= 0 then
      FSavedSel[Value + 1] := '1';
  end;
end;

function TCustomListBox.GetSelected(Index: Integer): Boolean;
begin
  CheckIndex(Index);
  if HandleAllocated then
    Result := SendMessageW(Handle, LB_GETSEL, WPARAM(Index), 0) > 0
  else
    Result := FSavedSel[Index + 1] = '1';
end;

procedure TCustomListBox.SetSelected(Index: Integer; Value: Boolean);
begin
  CheckIndex(Index);
  if not FMultiSelect then
  begin
    if Value then
      SetItemIndex(Index)
    else if GetItemIndex = Index then
      SetItemIndex(-1);
  end
  else if HandleAllocated then
    SendMessageW(Handle, LB_SETSEL, WPARAM(Ord(Value)), LPARAM(Index))
  else if Value then
    FSavedSel[Index + 1] := '1'
  else
    FSavedSel[Index + 1] := '0';
end;

function TCustomListBox.GetSelCount: Integer;
var
  I: Integer;
begin
  if not FMultiSelect then
    Result := Ord(GetItemIndex >= 0)
  else if HandleAllocated then
    Result := Integer(SendMessageW(Handle, LB_GETSELCOUNT, 0, 0))
  else
  begin
    Result := 0;
    for I := 1 to Length(FSavedSel) do
      if FSavedSel[I] = '1' then
        Inc(Result);
  end;
end;

function TCustomListBox.GetTopIndex: Integer;
begin
  if HandleAllocated then
    Result := Integer(SendMessageW(Handle, LB_GETTOPINDEX, 0, 0))
  else
    Result := FSavedTop;
end;

procedure TCustomListBox.SetTopIndex(Value: Integer);
begin
  if HandleAllocated then
    SendMessageW(Handle, LB_SETTOPINDEX, WPARAM(Value), 0)
  else
    FSavedTop := Value;
end;

{ lbStandard: the native item height }
function TCustomListBox.GetItemHeight: Integer;
begin
  if HandleAllocated then
    Result := Integer(SendMessageW(Handle, LB_GETITEMHEIGHT, 0, 0))
  else
    Result := FItemHeight;
end;

{ read-only in effect for lbStandard (owner-draw styles: Phase 8+) }
procedure TCustomListBox.SetItemHeight(Value: Integer);
begin
end;

{ the item at Pos (client coordinates); not on an item: -1 if Existing, else the index of the next new item }
function TCustomListBox.ItemAtPos(Pos: TPoint; Existing: Boolean): Integer;
var
  R: LRESULT;
  N: Integer;
begin
  N := GetCount;
  R := SendMessageW(Handle, LB_ITEMFROMPOINT, 0, MakeLParam(Word(Pos.X), Word(Pos.Y)));
  Result := LoWord(R);
  if (Result < N) and PtInRect(ItemRect(Result), Pos) then
    Exit;
  if Existing then
    Result := -1
  else if (N = 0) or (Pos.Y >= ItemRect(N - 1).Bottom) then
    Result := N;
end;

function TCustomListBox.ItemRect(Index: Integer): TRect;
begin
  Result := Rect(0, 0, 0, 0);
  SendMessageW(Handle, LB_GETITEMRECT, WPARAM(Index), LPARAM(@Result));
end;

procedure TCustomListBox.AddItem(const Item: string; AObject: TObject);
begin
  FItems.AddObject(Item, AObject);
end;

procedure TCustomListBox.Clear;
begin
  FItems.Clear;
end;

procedure TCustomListBox.ClearSelection;
var
  I: Integer;
begin
  if not FMultiSelect then
    SetItemIndex(-1)
  else if HandleAllocated then
    SendMessageW(Handle, LB_SETSEL, 0, LPARAM(-1))
  else
    for I := 1 to Length(FSavedSel) do
      FSavedSel[I] := '0';
end;

{ multi-select only }
procedure TCustomListBox.SelectAll;
var
  I: Integer;
begin
  if not FMultiSelect then
    Exit;
  if HandleAllocated then
    SendMessageW(Handle, LB_SETSEL, 1, LPARAM(-1))
  else
    for I := 1 to Length(FSavedSel) do
      FSavedSel[I] := '1';
end;

procedure TCustomListBox.DeleteSelected;
var
  I: Integer;
begin
  if not FMultiSelect then
  begin
    if GetItemIndex >= 0 then
      FItems.Delete(GetItemIndex);
    Exit;
  end;
  FItems.BeginUpdate;
  try
    for I := GetCount - 1 downto 0 do
      if GetSelected(I) then
        FItems.Delete(I);
  finally
    FItems.EndUpdate;
  end;
end;

procedure TCustomListBox.SetItems(Value: TStrings);
begin
  FItems.Assign(Value);
end;

procedure TCustomListBox.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListBox.SetColumns(Value: Integer);
begin
  if Value < 0 then
    Value := 0;
  if FColumns <> Value then
  begin
    FColumns := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListBox.SetExtendedSelect(Value: Boolean);
begin
  if FExtendedSelect <> Value then
  begin
    FExtendedSelect := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListBox.SetMultiSelect(Value: Boolean);
begin
  if FMultiSelect <> Value then
  begin
    FMultiSelect := Value;
    RecreateWnd;
  end;
end;

procedure TCustomListBox.SetSorted(Value: Boolean);
begin
  if FSorted <> Value then
  begin
    FSorted := Value;
    if Value and not HandleAllocated then
      SortSaved;
    RecreateWnd;
  end;
end;

{ owner-draw values: Phase 8+ (behave as lbStandard) }
procedure TCustomListBox.SetStyle(Value: TListBoxStyle);
begin
  FStyle := Value;
end;

{ ---------------------------------------------------------------- TComboBoxStrings }

function TComboBoxStrings.Get(Index: Integer): string;
var
  N: Integer;
begin
  if not FComboBox.HandleAllocated then
  begin
    Result := FComboBox.FSaved[Index];
    Exit;
  end;
  N := Integer(SendMessageW(FComboBox.Handle, CB_GETLBTEXTLEN, WPARAM(Index), 0));
  if N = CB_ERR then
    Error(SListIndexError, Index);
  SetLength(Result, N + 1);
  N := Integer(SendMessageW(FComboBox.Handle, CB_GETLBTEXT, WPARAM(Index), LPARAM(PChar(Result))));
  if N < 0 then
    N := 0;
  SetLength(Result, N);
end;

function TComboBoxStrings.GetCount: Integer;
begin
  if FComboBox.HandleAllocated then
    Result := Integer(SendMessageW(FComboBox.Handle, CB_GETCOUNT, 0, 0))
  else
    Result := FComboBox.FSaved.Count;
end;

function TComboBoxStrings.GetObject(Index: Integer): TObject;
var
  D: LRESULT;
begin
  if not FComboBox.HandleAllocated then
  begin
    Result := FComboBox.FSaved.Objects[Index];
    Exit;
  end;
  D := SendMessageW(FComboBox.Handle, CB_GETITEMDATA, WPARAM(Index), 0);
  if D = CB_ERR then
    Error(SListIndexError, Index);
  Result := TObject(Pointer(D));
end;

procedure TComboBoxStrings.PutObject(Index: Integer; AObject: TObject);
begin
  if not FComboBox.HandleAllocated then
    FComboBox.FSaved.Objects[Index] := AObject
  else if SendMessageW(FComboBox.Handle, CB_SETITEMDATA, WPARAM(Index), LPARAM(Pointer(AObject))) = CB_ERR then
    Error(SListIndexError, Index);
end;

{ the object and the selection of the item stay }
procedure TComboBoxStrings.Put(Index: Integer; const S: string);
var
  Obj: TObject;
  Sel: Boolean;
begin
  if FComboBox.FSorted then
    raise EStringListError.Create(SSortedListError);
  Obj := GetObject(Index);
  if not FComboBox.HandleAllocated then
  begin
    FComboBox.FSaved[Index] := S;
    Exit;
  end;
  Sel := FComboBox.ItemIndex = Index;
  SendMessageW(FComboBox.Handle, CB_DELETESTRING, WPARAM(Index), 0);
  SendMessageW(FComboBox.Handle, CB_INSERTSTRING, WPARAM(Index), LPARAM(PChar(S)));
  SendMessageW(FComboBox.Handle, CB_SETITEMDATA, WPARAM(Index), LPARAM(Pointer(Obj)));
  if Sel then
    FComboBox.ItemIndex := Index;
end;

procedure TComboBoxStrings.SetUpdateState(Updating: Boolean);
begin
  if not FComboBox.HandleAllocated then
    Exit;
  SendMessageW(FComboBox.Handle, WM_SETREDRAW, WPARAM(Ord(not Updating)), 0);
  if not Updating then
    FComboBox.Invalidate;
end;

function TComboBoxStrings.Add(const S: string): Integer;
begin
  if not FComboBox.HandleAllocated then
  begin
    if FComboBox.FSorted then
      Result := SortedInsertIndex(FComboBox.FSaved, S)
    else
      Result := FComboBox.FSaved.Count;
    FComboBox.FSaved.Insert(Result, S);
    if (FComboBox.FSavedIndex >= Result) then
      Inc(FComboBox.FSavedIndex);
    Exit;
  end;
  Result := Integer(SendMessageW(FComboBox.Handle, CB_ADDSTRING, 0, LPARAM(PChar(S))));
  if Result < 0 then
    raise EOutOfResources.Create(SListBoxNoSpace);
end;

procedure TComboBoxStrings.Insert(Index: Integer; const S: string);
begin
  if FComboBox.FSorted then
    raise EStringListError.Create(SSortedListError);
  if (Index < 0) or (Index > GetCount) then
    Error(SListIndexError, Index);
  if not FComboBox.HandleAllocated then
  begin
    FComboBox.FSaved.Insert(Index, S);
    if FComboBox.FSavedIndex >= Index then
      Inc(FComboBox.FSavedIndex);
  end
  else if SendMessageW(FComboBox.Handle, CB_INSERTSTRING, WPARAM(Index), LPARAM(PChar(S))) < 0 then
    raise EOutOfResources.Create(SListBoxNoSpace);
end;

procedure TComboBoxStrings.Delete(Index: Integer);
begin
  if not FComboBox.HandleAllocated then
  begin
    FComboBox.FSaved.Delete(Index);
    if FComboBox.FSavedIndex = Index then
      FComboBox.FSavedIndex := -1
    else if FComboBox.FSavedIndex > Index then
      Dec(FComboBox.FSavedIndex);
  end
  else if SendMessageW(FComboBox.Handle, CB_DELETESTRING, WPARAM(Index), 0) = CB_ERR then
    Error(SListIndexError, Index);
end;

procedure TComboBoxStrings.Clear;
begin
  if FComboBox.HandleAllocated then
    SendMessageW(FComboBox.Handle, CB_RESETCONTENT, 0, 0)
  else
  begin
    FComboBox.FSaved.Clear;
    FComboBox.FSavedIndex := -1;
  end;
end;

{ ---------------------------------------------------------------- TCustomComboBox }

constructor TCustomComboBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { UIF-702: the native notifications drive the events }
  ControlStyle := ControlStyle - [csClickEvents, csDoubleClicks, csSetCaption];
  FSaved := TStringList.Create;
  FItems := TComboBoxStrings.Create;
  TComboBoxStrings(FItems).FComboBox := Self;
  FSavedIndex := -1;
  FDropDownCount := 8;
  TabStop := True;
  Color := clWindow;
  SetBounds(Left, Top, 145, 23);
end;

destructor TCustomComboBox.Destroy;
begin
  inherited Destroy;
  FItems.Free;
  FSaved.Free;
end;

{ owner-draw styles behave as csDropDownList in Phase 4 }
function TCustomComboBox.IsListStyle: Boolean;
begin
  Result := FStyle in [csDropDownList, csOwnerDrawFixed, csOwnerDrawVariable];
end;

procedure TCustomComboBox.CreateParams(var Params: TCreateParams);
const
  Cases: array[TEditCharCase] of DWORD = (0, CBS_UPPERCASE, CBS_LOWERCASE);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'COMBOBOX';
  Params.Style := Params.Style or CBS_HASSTRINGS or CBS_AUTOHSCROLL or WS_VSCROLL or Cases[FCharCase];
  if FStyle = csSimple then
    Params.Style := Params.Style or CBS_SIMPLE
  else if IsListStyle then
    Params.Style := Params.Style or CBS_DROPDOWNLIST
  else
    Params.Style := Params.Style or CBS_DROPDOWN;
  if FSorted then
    Params.Style := Params.Style or CBS_SORT;
end;

procedure TCustomComboBox.CreateWnd;
var
  I, N, Sel: Integer;
  S: string;
begin
  S := Text;
  inherited CreateWnd;
  Sel := -1;
  for I := 0 to FSaved.Count - 1 do
  begin
    N := Integer(SendMessageW(Handle, CB_ADDSTRING, 0, LPARAM(PChar(FSaved[I]))));
    if N < 0 then
      Continue;
    SendMessageW(Handle, CB_SETITEMDATA, WPARAM(N), LPARAM(Pointer(FSaved.Objects[I])));
    if I = FSavedIndex then
      Sel := N
    else if (Sel >= 0) and (N <= Sel) then                   { a sorted list inserted before the selected item }
      Inc(Sel);
  end;
  FSaved.Clear;
  FSavedIndex := -1;
  if Sel >= 0 then
    SendMessageW(Handle, CB_SETCURSEL, WPARAM(Sel), 0);
  if not IsListStyle and (Text <> S) then
    SendMessageW(Handle, WM_SETTEXT, 0, LPARAM(PChar(S)));
  SendMessageW(Handle, CB_LIMITTEXT, WPARAM(FMaxLength), 0);
  SendMessageW(Handle, CB_SETCUEBANNER, 0, LPARAM(PChar(FTextHint)));
  SendMessageW(Handle, CB_SETMINVISIBLE, WPARAM(FDropDownCount), 0);
  UpdateItemHeight;
  HookEdit;
end;

{ UIF-884: the window procedure of the edit part of a combo box. The edit part is a window of the native control,
  so its keys, paste, cut, clear and undo are brought to the combo box object here }
function ComboEditWndProc(Wnd: HWND; Msg: UINT; WParam: WPARAM; LParam: LPARAM): LRESULT; stdcall;
var
  Prev: Pointer;
  C: TWinControl;
  M: TMessage;
begin
  Prev := Pointer(GetPropW(Wnd, ComboEditProp));
  if Prev = nil then
  begin
    Result := DefWindowProcW(Wnd, Msg, WParam, LParam);
    Exit;
  end;
  C := FindControl(GetParent(Wnd));
  if (Msg = WM_NCDESTROY) or not (C is TCustomComboBox) or (TCustomComboBox(C).FEditHandle <> Wnd) then
  begin
    if Msg = WM_NCDESTROY then
    begin
      RemovePropW(Wnd, ComboEditProp);
      if (C is TCustomComboBox) and (TCustomComboBox(C).FEditHandle = Wnd) then
        TCustomComboBox(C).FEditHandle := 0;
    end;
    Result := CallWindowProcW(TFNWndProc(Prev), Wnd, Msg, WParam, LParam);
    Exit;
  end;
  M.Msg := Msg;
  M.WParam := WParam;
  M.LParam := LParam;
  M.Result := 0;
  try
    TCustomComboBox(C).EditWndProc(M);
  except
    if Assigned(ApplicationHandleException) then
      ApplicationHandleException(C)
    else if not (ExceptObject is EAbort) then
      ShowException(ExceptObject, ExceptAddr);
  end;
  Result := M.Result;
end;

procedure TCustomComboBox.HookEdit;
begin
  FEditHandle := 0;
  if IsListStyle then
    Exit;
  FEditHandle := FindWindowExW(Handle, 0, 'Edit', nil);
  if FEditHandle = 0 then
    Exit;
  FEditProc := Pointer(GetWindowLongPtrW(FEditHandle, GWLP_WNDPROC));
  SetPropW(FEditHandle, ComboEditProp, THandle(FEditProc));
  SetWindowLongPtrW(FEditHandle, GWLP_WNDPROC, LONG_PTR(@ComboEditWndProc));
end;

procedure TCustomComboBox.UnhookEdit;
begin
  if FEditHandle = 0 then
    Exit;
  SetWindowLongPtrW(FEditHandle, GWLP_WNDPROC, LONG_PTR(FEditProc));
  RemovePropW(FEditHandle, ComboEditProp);
  FEditHandle := 0;
end;

{ a message of the edit part. Key messages take the path of the combo box's own (core.md UIF-085: KeyDown,
  KeyPress, KeyUp and the events; DefaultHandler then hands them to the edit part) }
procedure TCustomComboBox.EditWndProc(var Message: TMessage);
var
  Relay: Cardinal;
begin
  case Message.Msg of
    WM_KEYDOWN, WM_SYSKEYDOWN, WM_KEYUP, WM_SYSKEYUP, WM_CHAR:
      begin
        Relay := FEditRelay;
        FEditRelay := Message.Msg;
        try
          WindowProc(Message);
        finally
          FEditRelay := Relay;
        end;
      end;
    WM_PASTE, WM_CUT, WM_CLEAR, WM_UNDO, EM_UNDO:
      if UserModify then
        Message.Result := CallWindowProcW(TFNWndProc(FEditProc), FEditHandle, Message.Msg, Message.WParam,
          Message.LParam);
  else
    Message.Result := CallWindowProcW(TFNWndProc(FEditProc), FEditHandle, Message.Msg, Message.WParam,
      Message.LParam);
  end;
end;

procedure TCustomComboBox.DefaultHandler(var Message);
var
  M: PMessage;
  Modifies: Boolean;
  Edit: HWND;
begin
  M := @Message;
  if (FEditRelay = 0) or (M^.Msg <> FEditRelay) then
  begin
    inherited DefaultHandler(Message);
    Exit;
  end;
  FEditRelay := 0;
  case M^.Msg of
    WM_CHAR: Modifies := IsModifyingChar(Word(M^.WParam), False);
    WM_KEYDOWN: Modifies := IsModifyingKey(Word(M^.WParam));
  else
    Modifies := False;
  end;
  Edit := FEditHandle;
  if Modifies and not UserModify then
    M^.Result := 0
  else if Edit <> 0 then
    M^.Result := CallWindowProcW(TFNWndProc(FEditProc), Edit, M^.Msg, M^.WParam, M^.LParam);
end;

function TCustomComboBox.UserModify: Boolean;
begin
  FRejectSilently := False;
  Result := EditCanModify;
  if not Result and not FRejectSilently then
    RejectBeep;
  FRejectSilently := False;
end;

function TCustomComboBox.EditCanModify: Boolean;
begin
  Result := True;
end;

{ UIF-884: the selection and the text the user's next input starts from }
procedure TCustomComboBox.SaveUserState;
begin
  FRestorePending := False;
  FUserIndex := ItemIndex;
  if not IsListStyle then
    FUserText := Text;
end;

{ UIF-884: the state before the rejected selection, without events }
procedure TCustomComboBox.RestoreUserState;
begin
  if not HandleAllocated then
    Exit;
  SendMessageW(Handle, CB_SETCURSEL, WPARAM(FUserIndex), 0);
  if not IsListStyle and (Text <> FUserText) then
    SendMessageW(Handle, WM_SETTEXT, 0, LPARAM(PChar(FUserText)));
end;

{ UIF-702, UIF-884: CBN_SELCHANGE }
procedure TCustomComboBox.UserSelect;
var
  I: Integer;
  S: string;
  Nested: Boolean;
begin
  Nested := FSelecting;
  FSelecting := True;
  try
    if not UserModify then
    begin
      { the native control may still copy the item it selected into the edit part after this notification: the
        state is restored now, and again when the native control has finished }
      RestoreUserState;
      FRestorePending := True;
      PostMessageW(Handle, CM_RESTORESELECTION, 0, 0);
      Exit;
    end;
    I := ItemIndex;
    if not IsListStyle and (I >= 0) then
    begin
      S := FItems[I];
      SendMessageW(Handle, WM_SETTEXT, 0, LPARAM(PChar(S)));
    end;
    SaveUserState;
    Change;
    Select;
    Click;
  finally
    FSelecting := Nested;
  end;
end;

procedure TCustomComboBox.DestroyWnd;
var
  I, N: Integer;
begin
  UnhookEdit;
  FRestorePending := False;
  FSaved.Clear;
  N := Integer(SendMessageW(Handle, CB_GETCOUNT, 0, 0));
  for I := 0 to N - 1 do
    FSaved.AddObject(FItems[I], FItems.Objects[I]);
  FSavedIndex := Integer(SendMessageW(Handle, CB_GETCURSEL, 0, 0));
  inherited DestroyWnd;
end;

{ rule I1 and UIF-701: the native selection field gets the height that makes the window as high as the control.
  CB_SETITEMHEIGHT(-1, H - s(6)) is the first guess; the native frame is then measured and the item height
  corrected (the system may round the requested value) }
procedure TCustomComboBox.UpdateItemHeight;
var
  R: TRect;
  IH, I: Integer;
begin
  if not HandleAllocated or (FStyle = csSimple) then
    Exit;
  IH := Height - LayoutMulDiv(6, ControlDPI(Self), 96);
  for I := 1 to 3 do
  begin
    if IH < 1 then
      IH := 1;
    SendMessageW(Handle, CB_SETITEMHEIGHT, WPARAM(-1), LPARAM(IH));
    GetWindowRect(Handle, R);
    if R.Bottom - R.Top = Height then
      Exit;
    IH := Integer(SendMessageW(Handle, CB_GETITEMHEIGHT, WPARAM(-1), 0)) + Height - (R.Bottom - R.Top);
  end;
end;

procedure TCustomComboBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_COMMAND:
      case HiWord(Message.WParam) of
        CBN_EDITCHANGE:
          begin
            SaveUserState;
            Change;
          end;
        CBN_SELCHANGE: UserSelect;
        CBN_DROPDOWN:
          begin
            SaveUserState;
            DropDown;
          end;
        CBN_CLOSEUP: CloseUp;
      else
        inherited WndProc(Message);
      end;
    WM_KEYDOWN, WM_SYSKEYDOWN, WM_CHAR, WM_MOUSEWHEEL:
      begin
        { UIF-884: input that may change the selection (in the dropped list the selection follows the mouse: the
          state saved when the list opened stays) }
        if HandleAllocated and not GetDroppedDown then
          SaveUserState;
        inherited WndProc(Message);
        if FRestorePending then
        begin
          FRestorePending := False;
          RestoreUserState;
        end;
      end;
    CM_RESTORESELECTION:
      if FRestorePending then
      begin
        FRestorePending := False;
        RestoreUserState;
      end;
    CM_WANTSPECIALKEY:
      if ((Message.WParam = VK_RETURN) or (Message.WParam = VK_ESCAPE)) and GetDroppedDown then
        Message.Result := 1
      else
        inherited WndProc(Message);
    WM_SETTEXT:
      { list styles: the text selects the matching item (exact, case-insensitive) or none }
      if IsListStyle and not FSettingIndex then
      begin
        SetItemIndexText(IndexOfText(string(PChar(Message.LParam))));
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    WM_CTLCOLOREDIT, WM_CTLCOLORLISTBOX:
      { the inner edit and list windows take the colours of the combo box }
      if FindControl(HWND(Message.LParam)) = nil then
        CNCtlColor(Message)
      else
        inherited WndProc(Message);
    CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        AdjustHeight;
      end;
  else
    inherited WndProc(Message);
  end;
end;

function TCustomComboBox.IndexOfText(const S: string): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to FItems.Count - 1 do
    if AnsiSameText(FItems[I], S) then
    begin
      Result := I;
      Exit;
    end;
end;

{ the selection and the text of the item (none: empty), without events }
procedure TCustomComboBox.SetItemIndexText(Index: Integer);
var
  S: string;
begin
  if HandleAllocated then
  begin
    SendMessageW(Handle, CB_SETCURSEL, WPARAM(Index), 0);
    Exit;
  end;
  FSavedIndex := Index;
  if Index >= 0 then
    S := FSaved[Index]
  else
    S := '';
  FSettingIndex := True;
  try
    Perform(WM_SETTEXT, 0, LPARAM(PChar(S)));
  finally
    FSettingIndex := False;
  end;
end;

function TCustomComboBox.GetItemIndex: Integer;
begin
  if HandleAllocated then
    Result := Integer(SendMessageW(Handle, CB_GETCURSEL, 0, 0))
  else
    Result := FSavedIndex;
end;

{ out of range is ignored (as UIF-692); never an event (UIF-702) }
procedure TCustomComboBox.SetItemIndex(Value: Integer);
begin
  if (Value < -1) or (Value >= FItems.Count) then
    Exit;
  SetItemIndexText(Value);
end;

{ UIF-702: never while loading }
procedure TCustomComboBox.Change;
begin
  if (csLoading in ComponentState) or (csCreating in ControlState) then
    Exit;
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TCustomComboBox.Select;
begin
  if Assigned(FOnSelect) then
    FOnSelect(Self);
end;

procedure TCustomComboBox.DropDown;
begin
  if Assigned(FOnDropDown) then
    FOnDropDown(Self);
end;

procedure TCustomComboBox.CloseUp;
begin
  if Assigned(FOnCloseUp) then
    FOnCloseUp(Self);
end;

{ rule I1: Abs(Font.Height) + s(11) unless csSimple }
procedure TCustomComboBox.IntrinsicSize(var AWidth, AHeight: Integer);
begin
  if (FStyle = csSimple) or (csLoading in ComponentState) then
    Exit;
  AHeight := Abs(Font.Height) + LayoutMulDiv(11, ControlDPI(Self), 96);
end;

procedure TCustomComboBox.AdjustHeight;
begin
  SetBounds(Left, Top, Width, Height);
  UpdateItemHeight;
end;

procedure TCustomComboBox.Loaded;
begin
  inherited Loaded;
  AdjustHeight;
end;

procedure TCustomComboBox.SetParent(AParent: TWinControl);
begin
  inherited SetParent(AParent);
  if not (csDestroying in ComponentState) then
    AdjustHeight;
end;

function TCustomComboBox.GetDroppedDown: Boolean;
begin
  Result := HandleAllocated and (SendMessageW(Handle, CB_GETDROPPEDSTATE, 0, 0) <> 0);
end;

procedure TCustomComboBox.SetDroppedDown(Value: Boolean);
begin
  if HandleAllocated then
    SendMessageW(Handle, CB_SHOWDROPDOWN, WPARAM(Ord(Value)), 0);
end;

function TCustomComboBox.GetSelStart: Integer;
var
  A, B: DWORD;
begin
  Result := 0;
  if HandleAllocated then
  begin
    A := 0;
    B := 0;
    SendMessageW(Handle, CB_GETEDITSEL, WPARAM(@A), LPARAM(@B));
    Result := Integer(A);
  end;
end;

function TCustomComboBox.GetSelLength: Integer;
var
  A, B: DWORD;
begin
  Result := 0;
  if HandleAllocated then
  begin
    A := 0;
    B := 0;
    SendMessageW(Handle, CB_GETEDITSEL, WPARAM(@A), LPARAM(@B));
    Result := Integer(B) - Integer(A);
  end;
end;

procedure TCustomComboBox.SetSelStart(Value: Integer);
begin
  if HandleAllocated then
    SendMessageW(Handle, CB_SETEDITSEL, 0, MakeLParam(Word(Value), Word(Value)));
end;

procedure TCustomComboBox.SetSelLength(Value: Integer);
var
  S: Integer;
begin
  S := GetSelStart;
  if HandleAllocated then
    SendMessageW(Handle, CB_SETEDITSEL, 0, MakeLParam(Word(S), Word(S + Value)));
end;

function TCustomComboBox.GetSelText: string;
begin
  Result := Copy(Text, GetSelStart + 1, GetSelLength);
end;

{ replaces the selection; the caret follows the new text }
procedure TCustomComboBox.SetSelText(const Value: string);
var
  S: string;
  A: Integer;
begin
  A := GetSelStart;
  S := Text;
  Text := Copy(S, 1, A) + Value + Copy(S, A + GetSelLength + 1, MaxInt);
  SetSelStart(A + Length(Value));
end;

procedure TCustomComboBox.SelectAll;
begin
  if HandleAllocated then
    SendMessageW(Handle, CB_SETEDITSEL, 0, MakeLParam(0, $FFFF));
end;

procedure TCustomComboBox.AddItem(const Item: string; AObject: TObject);
begin
  FItems.AddObject(Item, AObject);
end;

procedure TCustomComboBox.Clear;
begin
  FItems.Clear;
  Text := '';
end;

procedure TCustomComboBox.SetItems(Value: TStrings);
begin
  FItems.Assign(Value);
end;

procedure TCustomComboBox.SetCharCase(Value: TEditCharCase);
begin
  if FCharCase <> Value then
  begin
    FCharCase := Value;
    RecreateWnd;
  end;
end;

procedure TCustomComboBox.SetDropDownCount(Value: Integer);
begin
  FDropDownCount := Value;
  if HandleAllocated then
    SendMessageW(Handle, CB_SETMINVISIBLE, WPARAM(Value), 0);
end;

procedure TCustomComboBox.SetMaxLength(Value: Integer);
begin
  if Value < 0 then
    Value := 0;
  FMaxLength := Value;
  if HandleAllocated then
    SendMessageW(Handle, CB_LIMITTEXT, WPARAM(Value), 0);
end;

procedure TCustomComboBox.SetTextHint(const Value: string);
begin
  FTextHint := Value;
  if HandleAllocated then
    SendMessageW(Handle, CB_SETCUEBANNER, 0, LPARAM(PChar(FTextHint)));
end;

{ Sorted became True without a handle: the saved items in the native order, the selected item kept }
procedure TCustomComboBox.SortSaved;
var
  Items: TStringList;
  I, N: Integer;
  SelObj: Integer;
begin
  Items := TStringList.Create;
  try
    Items.Assign(FSaved);
    SelObj := FSavedIndex;
    FSaved.Clear;
    FSavedIndex := -1;
    for I := 0 to Items.Count - 1 do
    begin
      N := SortedInsertIndex(FSaved, Items[I]);
      FSaved.InsertObject(N, Items[I], Items.Objects[I]);
      if (FSavedIndex >= N) then
        Inc(FSavedIndex);
      if I = SelObj then
        FSavedIndex := N;
    end;
  finally
    Items.Free;
  end;
end;

procedure TCustomComboBox.SetSorted(Value: Boolean);
begin
  if FSorted <> Value then
  begin
    FSorted := Value;
    if Value and not HandleAllocated then
      SortSaved;
    RecreateWnd;
  end;
end;

procedure TCustomComboBox.SetStyle(Value: TComboBoxStyle);
begin
  if FStyle <> Value then
  begin
    FStyle := Value;
    RecreateWnd;
    AdjustHeight;
  end;
end;

{ ---------------------------------------------------------------- TScrollBar }

const
  SScrollBarRange = 'Scrollbar property out of range';

constructor TScrollBar.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle - [csClickEvents, csDoubleClicks, csSetCaption];
  FMax := 100;
  FLargeChange := 1;
  FSmallChange := 1;
  TabStop := True;
  SetBounds(Left, Top, 121, 17);
end;

procedure TScrollBar.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'SCROLLBAR';
  if FKind = sbVertical then
    Params.Style := Params.Style or SBS_VERT
  else
    Params.Style := Params.Style or SBS_HORZ;
end;

procedure TScrollBar.CreateWnd;
begin
  inherited CreateWnd;
  UpdateScrollInfo;
end;

procedure TScrollBar.UpdateScrollInfo;
var
  Info: TScrollInfo;
begin
  if not HandleAllocated then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  Info.nMin := FMin;
  Info.nMax := FMax;
  Info.nPage := Cardinal(FPageSize);
  Info.nPos := FPosition;
  SetScrollInfo(Handle, SB_CTL, Info, True);
end;

{ Min ... Max - max(PageSize - 1, 0) }
function TScrollBar.ClampPosition(Value: Integer): Integer;
var
  Top: Integer;
begin
  Top := FMax;
  if FPageSize > 1 then
    Top := FMax - (FPageSize - 1);
  if Top < FMin then
    Top := FMin;
  Result := Value;
  if Result > Top then
    Result := Top;
  if Result < FMin then
    Result := FMin;
end;

{ UIF-711: OnChange on every change of Position; while loading only the fields (checked in Loaded) }
procedure TScrollBar.SetParams(APosition, AMin, AMax: Integer);
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
    raise EInvalidOperation.Create(SScrollBarRange);
  Old := FPosition;
  FMin := AMin;
  FMax := AMax;
  FPosition := ClampPosition(APosition);
  UpdateScrollInfo;
  if FPosition <> Old then
    Change;
end;

{ a stored range is never rejected: Max below Min becomes Min, the position is clamped, no event }
procedure TScrollBar.Loaded;
begin
  inherited Loaded;
  if FMax < FMin then
    FMax := FMin;
  FPosition := ClampPosition(FPosition);
  UpdateScrollInfo;
end;

procedure TScrollBar.SetMin(Value: Integer);
begin
  SetParams(FPosition, Value, FMax);
end;

procedure TScrollBar.SetMax(Value: Integer);
begin
  SetParams(FPosition, FMin, Value);
end;

procedure TScrollBar.SetPosition(Value: Integer);
begin
  SetParams(Value, FMin, FMax);
end;

procedure TScrollBar.SetPageSize(Value: Integer);
begin
  if Value < 0 then
    Value := 0;
  FPageSize := Value;
  SetParams(FPosition, FMin, FMax);
end;

procedure TScrollBar.SetLargeChange(Value: Integer);
begin
  if Value < 1 then
    Value := 1
  else if Value > 32767 then
    Value := 32767;
  FLargeChange := Value;
end;

procedure TScrollBar.SetSmallChange(Value: Integer);
begin
  if Value < 1 then
    Value := 1
  else if Value > 32767 then
    Value := 32767;
  FSmallChange := Value;
end;

{ changing Kind at design time swaps Width and Height }
procedure TScrollBar.SetKind(Value: TScrollBarKind);
begin
  if FKind <> Value then
  begin
    FKind := Value;
    if (csDesigning in ComponentState) and not (csLoading in ComponentState) then
      SetBounds(Left, Top, Height, Width);
    RecreateWnd;
  end;
end;

procedure TScrollBar.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TScrollBar.Scroll(ScrollCode: TScrollCode; var ScrollPos: Integer);
begin
  if Assigned(FOnScroll) then
    FOnScroll(Self, ScrollCode, ScrollPos);
end;

{ UIF-710 }
procedure TScrollBar.DoScroll(Code: Integer);
var
  NewPos: Integer;
  Info: TScrollInfo;
begin
  if (Code < Ord(Low(TScrollCode))) or (Code > Ord(High(TScrollCode))) then
    Exit;
  NewPos := FPosition;
  case Code of
    SB_LINEUP: NewPos := FPosition - FSmallChange;
    SB_LINEDOWN: NewPos := FPosition + FSmallChange;
    SB_PAGEUP: NewPos := FPosition - FLargeChange;
    SB_PAGEDOWN: NewPos := FPosition + FLargeChange;
    SB_TOP: NewPos := FMin;
    SB_BOTTOM: NewPos := FMax;
    SB_THUMBPOSITION, SB_THUMBTRACK:
      begin
        FillChar(Info, SizeOf(Info), 0);
        Info.cbSize := SizeOf(Info);
        Info.fMask := SIF_TRACKPOS;
        if GetScrollInfo(Handle, SB_CTL, Info) then
          NewPos := Info.nTrackPos;
      end;
  end;
  NewPos := ClampPosition(NewPos);
  Scroll(TScrollCode(Code), NewPos);
  SetPosition(ClampPosition(NewPos));
end;

procedure TScrollBar.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CN_HSCROLL, CN_VSCROLL:
      begin
        DoScroll(LoWord(Message.WParam));
        Message.Result := 0;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ ---------------------------------------------------------------- TCustomStaticText }

constructor TCustomStaticText.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FShowAccelChar := True;
  AutoSize := True;
  SetBounds(Left, Top, 65, 17);                               { after AutoSize: a new control has the default size }
  Color := clBtnFace;
  ParentColor := True;
end;

procedure TCustomStaticText.CreateParams(var Params: TCreateParams);
const
  Alignments: array[TAlignment] of DWORD = (SS_LEFT, SS_RIGHT, SS_CENTER);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'STATIC';
  Params.Style := Params.Style or SS_NOTIFY or Alignments[FAlignment];
  if not FShowAccelChar then
    Params.Style := Params.Style or SS_NOPREFIX;
  case FBorderStyle of
    sbsSingle: Params.Style := Params.Style or WS_BORDER;
    sbsSunken: Params.Style := Params.Style or SS_SUNKEN;
  end;
end;

procedure TCustomStaticText.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_DIALOGCHAR:
      if Enabled and FShowAccelChar and (FFocusControl <> nil) and IsAccel(Word(Message.WParam), Caption)
        and FFocusControl.CanFocus then
      begin
        FFocusControl.SetFocus;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_TEXTCHANGED, CM_FONTCHANGED:
      begin
        inherited WndProc(Message);
        AdjustSize;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomStaticText.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FFocusControl) then
    FFocusControl := nil;
end;

{ border allowance per side: 2 px for sbsSingle, 4 px for sbsSunken (logical, not scaled) }
procedure TCustomStaticText.AdjustSize;
const
  Border: array[TStaticBorderStyle] of Integer = (0, 2, 4);
var
  W, H: Integer;
  Flags: UINT;
begin
  if not MeasuringAllowed then
    Exit;
  Flags := DT_EXPANDTABS;
  if not FShowAccelChar then
    Flags := Flags or DT_NOPREFIX;
  W := Width;
  H := Height;
  MeasureCaption(Font, Caption, Flags, W, H);
  ApplyMeasuredSize(Self, FAlignment, W + 2 * Border[FBorderStyle], H + 2 * Border[FBorderStyle]);
end;

procedure TCustomStaticText.SetFocusControl(Value: TWinControl);
begin
  if FFocusControl <> Value then
  begin
    FFocusControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

procedure TCustomStaticText.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    RecreateWnd;
    AdjustSize;
  end;
end;

procedure TCustomStaticText.SetBorderStyle(Value: TStaticBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
    AdjustSize;
  end;
end;

procedure TCustomStaticText.SetShowAccelChar(Value: Boolean);
begin
  if FShowAccelChar <> Value then
  begin
    FShowAccelChar := Value;
    RecreateWnd;
    AdjustSize;
  end;
end;

initialization
  RegisterClasses([TLabel, TEdit, TMemo, TButton, TCheckBox, TRadioButton, TGroupBox, TListBox, TComboBox,
    TScrollBar, TStaticText]);
  RegisterAutomationProvider(TCustomLabel, TLabelAutomationProvider);
  RegisterAutomationProvider(TCustomStaticText, TStaticTextAutomationProvider);
  RegisterAutomationProvider(TButtonControl, TButtonControlAutomationProvider);
  RegisterAutomationProvider(TCustomGroupBox, TGroupBoxAutomationProvider);
end.
