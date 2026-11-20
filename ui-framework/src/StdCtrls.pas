{ StdCtrls - the standard controls (controls-standard.md sections 1 ... 11, 18). }
unit StdCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls;

type
  TTextLayout = (tlTop, tlCenter, tlBottom);

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
    function GetSelStart: Integer;
    procedure SetSelStart(Value: Integer);
    function GetSelLength: Integer;
    procedure SetSelLength(Value: Integer);
    function GetSelText: string;
    procedure SetSelText(const Value: string);
    function GetModified: Boolean;
    procedure SetModified(Value: Boolean);
    function GetCanUndo: Boolean;
    procedure AdjustHeight;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    { UIF-033: selection, limit, password character, cue banner and modified flag pushed after creation }
    procedure CreateWnd; override;
    { UIF-034: selection and modified flag saved before the window goes }
    procedure DestroyWnd; override;
    { UIF-620: EN_CHANGE (CN_COMMAND); handle-less text changes; font changes re-apply I2/I3 }
    procedure WndProc(var Message: TMessage); override;
    { layout.md UIF-250 rules I2/I3 }
    procedure IntrinsicSize(var AWidth, AHeight: Integer); override;
    procedure SetParent(AParent: TWinControl); override;
    { rules I2/I3 once all stored properties are known }
    procedure Loaded; override;
    { AutoSelect: select all when the focus arrives without a mouse click }
    procedure DoEnter; override;
    procedure Change; virtual;
    { selects AStart .. AStart + ALength (handle: EM_SETSEL; else the fields) }
    procedure SetSelection(AStart, ALength: Integer);
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
    function GetCaretPos: TPoint;
    procedure SetCaretPos(const Value: TPoint);
  protected
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

  { controls-standard.md sections 4 ... 6: the base of the BUTTON-class controls }
  TButtonControl = class(TWinControl)
  private
    FWordWrap: Boolean;
    procedure SetWordWrap(Value: Boolean);
  protected
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
    property Alignment: TLeftRight read FAlignment write SetAlignment default taRightJustify;
    property AllowGrayed: Boolean read FAllowGrayed write SetAllowGrayed default False;
    property Checked: Boolean read GetChecked write SetChecked default False;
    property State: TCheckBoxState read FState write SetState default cbUnchecked;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TCheckBox = class(TCustomCheckBox)
  published
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
  public
    constructor Create(AOwner: TComponent); override;
  published
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

implementation

uses
  RTLConsts, Forms;

type
  { access to the protected PixelsPerInch / ActiveControl of a form }
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

{ ---------------------------------------------------------------- TCustomLabel }

constructor TCustomLabel.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 34, 15);
  Color := clBtnFace;
  ParentColor := True;
  AutoSize := True;
  FShowAccelChar := True;
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
    CM_TEXTCHANGED, CM_FONTCHANGED, CM_ENABLEDCHANGED:
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

procedure TCustomLabel.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
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
  else
    inherited WndProc(Message);
  end;
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
    RecreateWnd;
  end;
end;

procedure TCustomEdit.SetMaxLength(Value: Integer);
begin
  if FMaxLength <> Value then
  begin
    FMaxLength := Value;
    if HandleAllocated then
      SendMessageW(Handle, EM_LIMITTEXT, WPARAM(Value), 0);
  end;
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
  FLines := TMemoStrings.Create;
  TMemoStrings(FLines).FMemo := Self;
  TMemoStrings(FLines).FList := TStringList.Create;
  FWantReturns := True;
  FWordWrap := True;
  AutoSize := False;
  SetBounds(Left, Top, 185, 89);
end;

destructor TCustomMemo.Destroy;
begin
  inherited Destroy;
  TMemoStrings(FLines).FList.Free;
  FLines.Free;
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
        if not HandleAllocated and not TMemoStrings(FLines).FSyncing then
          TMemoStrings(FLines).FList.Text := LinesTextOf(Text);
        inherited WndProc(Message);
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

{ the native order: locale-aware, case-insensitive; after equal items }
function TCustomListBox.SortedIndex(const S: string): Integer;
begin
  Result := 0;
  while (Result < FSaved.Count) and (AnsiCompareText(FSaved[Result], S) <= 0) do
    Inc(Result);
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

initialization
  RegisterClasses([TLabel, TEdit, TMemo, TButton, TCheckBox, TRadioButton, TGroupBox, TListBox]);
end.
