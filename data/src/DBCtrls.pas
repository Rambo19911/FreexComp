// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ DBCtrls - the data-aware controls: each shows (and, where it can, edits) one field of the dataset behind its
  data source through a field data link. UI Automation clients see the drawn buttons of the navigator as Button
  elements with the Invoke pattern (A-083). }
unit DBCtrls;

interface

uses
  Windows, Messages, SysUtils, Classes, Graphics, Controls, StdCtrls, DB;

type
  { A single-line edit for one field. It shows the edit text of the field while it has the focus and the display
    text otherwise; the first modification by the user puts the dataset into edit mode; the text is written to
    the field when the record is posted, when the dataset is about to scroll and when the focus leaves. }
  TDBEdit = class(TCustomEdit)
  private
    FDataLink: TFieldDataLink;
    { the control is the entered control of its form (between DoEnter and DoExit) }
    FEntered: Boolean;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    function GetReadOnly: Boolean;
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure SetReadOnly(Value: Boolean);
    procedure DataChange(Sender: TObject);
    procedure UpdateData(Sender: TObject);
    { the native read-only state and the input limit follow the link and the field }
    procedure UpdateNative;
    function NativeReadOnly: Boolean;
  protected
    procedure CreateWnd; override;
    { Escape is wanted while a modification is pending }
    procedure WndProc(var Message: TMessage); override;
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    { the start of a modification by the user: the dataset enters edit mode, or the input is refused }
    function EditCanModify: Boolean; override;
    procedure DoEnter; override;
    { writes a pending modification; when the field rejects it the control keeps the focus }
    procedure DoExit; override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure SetName(const NewName: TComponentName); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { nil while the link is inactive or the field does not exist }
    property Field: TField read GetField;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property DataField: string read GetDataField write SetDataField;
    property ReadOnly: Boolean read GetReadOnly write SetReadOnly default False;
    property Align;
    property Anchors;
    property AutoSelect;
    property CharCase;
    property Color default clWindow;
    property Enabled;
    property Font;
    { 0: a string field limits the input to its size }
    property MaxLength;
    property PasswordChar;
    property TabOrder;
    property TabStop default True;
    property Visible;
    property OnChange;
    property OnClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
  end;

  { A label that shows the display text of a field. It has no window, never takes the focus and never edits. }
  TDBText = class(TCustomLabel)
  private
    FDataLink: TFieldDataLink;
    { Alignment was assigned: the field no longer decides it }
    FAlignmentSet: Boolean;
    function GetAlignment: TAlignment;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    procedure SetAlignment(Value: TAlignment);
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure DataChange(Sender: TObject);
  protected
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { at design time the caption is the name }
    procedure SetName(const NewName: TComponentName); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { nil while the link is inactive or the field does not exist }
    property Field: TField read GetField;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property DataField: string read GetDataField write SetDataField;
    property Align;
    { follows the alignment of the field until it is assigned }
    property Alignment: TAlignment read GetAlignment write SetAlignment stored FAlignmentSet;
    property Anchors;
    property AutoSize default False;
    property Color;
    property Font;
    property Transparent;
    property Visible;
    property WordWrap;
  end;

  { A multi-line edit for a string or memo field; fields of other types are shown read-only. Lone line feeds and
    carriage returns of the stored text are shown as line breaks. Enter inserts a line break. }
  TDBMemo = class(TCustomMemo)
  private
    FDataLink: TFieldDataLink;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    function GetReadOnly: Boolean;
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure SetReadOnly(Value: Boolean);
    procedure DataChange(Sender: TObject);
    procedure UpdateData(Sender: TObject);
    function NativeReadOnly: Boolean;
  protected
    { Escape is wanted while a modification is pending }
    procedure WndProc(var Message: TMessage); override;
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    function EditCanModify: Boolean; override;
    procedure DoExit; override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure SetName(const NewName: TComponentName); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property Field: TField read GetField;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property DataField: string read GetDataField write SetDataField;
    property ReadOnly: Boolean read GetReadOnly write SetReadOnly default False;
    property Align;
    property Anchors;
    property Font;
    property MaxLength;
    property ScrollBars;
    property TabOrder;
    property TabStop default True;
    property Visible;
    property WantTabs;
    property WordWrap;
    property OnChange;
    property OnClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
  end;

  { A check box for one field. A click by the user is written to the field at once. }
  TDBCheckBox = class(TCustomCheckBox)
  private
    FDataLink: TFieldDataLink;
    FValueChecked: string;
    FValueUnchecked: string;
    { the state is being set from the field or by a click that is not written yet: no OnClick }
    FQuiet: Boolean;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    function GetReadOnly: Boolean;
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure SetReadOnly(Value: Boolean);
    procedure SetValueChecked(const Value: string);
    procedure SetValueUnchecked(const Value: string);
    function FieldState: TCheckBoxState;
    procedure WriteCheckState;
    procedure DataChange(Sender: TObject);
  protected
    { the path of every click by the user: ignored in the read-only state, rejected when the dataset cannot be
      put into edit mode, else the next state is written to the field }
    procedure Toggle; override;
    { OnClick for clicks of the user and assignments by the application, not for data changes }
    procedure Click; override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property Field: TField read GetField;
    { assignments by the application change the control only: applications assign to the field }
    property Checked;
    property State;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property DataField: string read GetDataField write SetDataField;
    property ReadOnly: Boolean read GetReadOnly write SetReadOnly default False;
    property Alignment;
    property AllowGrayed;
    property Anchors;
    property Caption;
    property Font;
    property TabOrder;
    property TabStop default True;
    { the texts of a non-boolean field that mean checked, separated by ';'; the first one is written }
    property ValueChecked: string read FValueChecked write SetValueChecked;
    property ValueUnchecked: string read FValueUnchecked write SetValueUnchecked;
    property Visible;
    property OnClick;
  end;

  { A combo box for one field. A selected item is written to the field at once, typed text when the record is
    posted, when the dataset is about to scroll and when the focus leaves. }
  TDBComboBox = class(TCustomComboBox)
  private
    FDataLink: TFieldDataLink;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    function GetReadOnly: Boolean;
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure SetReadOnly(Value: Boolean);
    procedure DataChange(Sender: TObject);
    procedure UpdateData(Sender: TObject);
  protected
    { Escape is wanted while typed text is pending }
    procedure WndProc(var Message: TMessage); override;
    { Escape discards pending typed text }
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    { typing and selecting start a modification: ignored in the read-only state, refused when the dataset cannot
      be put into edit mode }
    function EditCanModify: Boolean; override;
    { a selected item is written to the field before OnChange }
    procedure Change; override;
    procedure DoExit; override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property Field: TField read GetField;
    { assignments by the application change the control only }
    property ItemIndex;
    property Text;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property DataField: string read GetDataField write SetDataField;
    property ReadOnly: Boolean read GetReadOnly write SetReadOnly default False;
    property Anchors;
    property DropDownCount;
    property Font;
    property Items;
    property Sorted;
    { csDropDown or csDropDownList }
    property Style;
    property TabOrder;
    property TabStop default True;
    property Visible;
    property OnChange;
    property OnClick;
    property OnDropDown;
  end;

  { A drop-down list whose items come from a second dataset: the data field stores the key of the selected
    row. The list dataset is only read, through a row window, without moving its cursor. }
  TDBLookupComboBox = class(TCustomComboBox)
  private
    FDataLink: TFieldDataLink;
    FListLink: TDataLink;
    FKeyField: string;
    FListField: string;
    FNullValueText: string;
    { the key text of every item }
    FKeys: TStringList;
    { the field is null: NullValueText is the displayed text }
    FNullShown: Boolean;
    function GetDataField: string;
    function GetDataSource: TDataSource;
    function GetField: TField;
    function GetListSource: TDataSource;
    function GetReadOnly: Boolean;
    function GetDropDownRows: Integer;
    function GetKeyValue: string;
    function GetDisplayText: string;
    procedure SetDataField(const Value: string);
    procedure SetDataSource(Value: TDataSource);
    procedure SetListSource(Value: TDataSource);
    procedure SetReadOnly(Value: Boolean);
    procedure SetDropDownRows(Value: Integer);
    procedure SetKeyField(const Value: string);
    procedure SetListField(const Value: string);
    procedure SetNullValueText(const Value: string);
    procedure RebuildList;
    procedure DataChange(Sender: TObject);
  protected
    { a selection starts a modification: ignored in the read-only state, refused when the dataset cannot be put
      into edit mode }
    function EditCanModify: Boolean; override;
    { the key of the selected item is written to the field before OnChange }
    procedure Change; override;
    { Delete and Backspace on the closed control clear the field }
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property Field: TField read GetField;
    { the stored key as text; '' for null }
    property KeyValue: string read GetKeyValue;
    { the displayed text: the item of the key, NullValueText for null, '' when the key is not in the list }
    property Text: string read GetDisplayText;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    { the field that stores the key }
    property DataField: string read GetDataField write SetDataField;
    property ListSource: TDataSource read GetListSource write SetListSource;
    property KeyField: string read FKeyField write SetKeyField;
    { '' shows the key field }
    property ListField: string read FListField write SetListField;
    property DropDownRows: Integer read GetDropDownRows write SetDropDownRows default 7;
    property NullValueText: string read FNullValueText write SetNullValueText;
    property ReadOnly: Boolean read GetReadOnly write SetReadOnly default False;
    property Anchors;
    property Font;
    property TabOrder;
    property TabStop default True;
    property Visible;
    property OnClick;
    property OnDropDown;
  end;

  TNavigateBtn = (nbFirst, nbPrior, nbNext, nbLast, nbInsert, nbDelete, nbEdit, nbPost, nbCancel, nbRefresh);
  TNavButtonSet = set of TNavigateBtn;
  TNavClick = procedure(Sender: TObject; Button: TNavigateBtn) of object;

  { A row of buttons that navigate and edit the dataset of a data source. The control draws its buttons; UI
    Automation clients see each visible button as an element of its own (A-083). }
  TDBNavigator = class(TCustomControl)
  private
    FDataLink: TDataLink;
    FVisibleButtons: TNavButtonSet;
    FConfirmDelete: Boolean;
    FHints: TStrings;
    FBeforeAction: TNavClick;
    FOnNavClick: TNavClick;
    { the left mouse button went down on FDownButton }
    FDown: Boolean;
    FDownButton: TNavigateBtn;
    function GetDataSource: TDataSource;
    procedure SetDataSource(Value: TDataSource);
    procedure SetVisibleButtons(Value: TNavButtonSet);
    procedure SetHints(Value: TStrings);
    function ButtonAt(X, Y: Integer; var Index: TNavigateBtn): Boolean;
    function ConfirmDeletion: Boolean;
    procedure DrawGlyph(Index: TNavigateBtn; const R: TRect; AColor: TColor);
    { the link reports a change of what the enabling depends on }
    procedure LinkChanged;
  protected
    procedure Paint; override;
    procedure MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    procedure MouseMove(Shift: TShiftState; X, Y: Integer); override;
    procedure MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    procedure CMEnabledChanged(var Message: TMessage); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    { the click a UI Automation client's Invoke posted (A-083) }
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { BeforeAction, the action of the button on the dataset, OnClick; nothing for a disabled button }
    procedure BtnClick(Index: TNavigateBtn); virtual;
    function ButtonEnabled(Index: TNavigateBtn): Boolean;
    { the rectangle of a visible button in client coordinates; empty for a hidden one }
    function ButtonRect(Index: TNavigateBtn): TRect;
    { the line of Hints for the button, else the default hint }
    function ButtonHint(Index: TNavigateBtn): string;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property VisibleButtons: TNavButtonSet read FVisibleButtons write SetVisibleButtons
      default [nbFirst, nbPrior, nbNext, nbLast, nbInsert, nbDelete, nbEdit, nbPost, nbCancel, nbRefresh];
    property ConfirmDelete: Boolean read FConfirmDelete write FConfirmDelete default True;
    { optional hint texts, one line per button in the order of TNavigateBtn }
    property Hints: TStrings read FHints write SetHints;
    property Align;
    property Anchors;
    property Enabled;
    property ShowHint;
    property TabOrder;
    property TabStop default False;
    property Visible;
    property BeforeAction: TNavClick read FBeforeAction write FBeforeAction;
    property OnClick: TNavClick read FOnNavClick write FOnNavClick;
  end;

  TMessageBoxHook = function(const Text: string): Integer;

var
  { When assigned, it answers the confirmation of the navigator in place of the message box (IDOK proceeds). }
  MessageBoxHook: TMessageBoxHook;

{ Tells the user that a modification was rejected: StdCtrls.RejectBeepHook when assigned, else the beep. }
procedure RejectBeep;

implementation

uses
  ActiveX, UIAutomation, Accessibility;

type
  { the link of the list dataset of a lookup combo box: data and layout changes rebuild the list, scrolling and
    field writes do not }
  TLookupListLink = class(TDataLink)
  private
    FCombo: TDBLookupComboBox;
  protected
    procedure ActiveChanged; override;
    procedure DataSetChanged; override;
    procedure DataSetScrolled(Distance: Integer); override;
    procedure LayoutChanged; override;
    procedure RecordChanged(Field: TField); override;
  end;

  TNavigatorLink = class(TDataLink)
  private
    FNavigator: TDBNavigator;
  protected
    procedure ActiveChanged; override;
    procedure EditingChanged; override;
    procedure DataSetChanged; override;
  end;

  TNavButtonProvider = class;

  { A-083 (accessibility.md section 12 follow-up): the navigator, a fragment root of control type ToolBar whose
    children are its visible buttons in the order of TNavigateBtn; one provider object per button while one is
    alive }
  TNavigatorAutomationProvider = class(TWindowAutomationProvider)
  private
    FButtons: array[TNavigateBtn] of TNavButtonProvider;    { the live button providers (no counted references) }
    function Nav: TDBNavigator;
    { the live provider of a button or a new one (a new one has no reference yet: the caller takes one at once) }
    function ButtonObject(B: TNavigateBtn): TNavButtonProvider;
  protected
    procedure DisconnectFragments; override;
  public
    class function DefaultControlType(Control: TControl): Integer; override;
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; override; stdcall;
    function ElementProviderFromPoint(x, y: Double; out pRetVal: IRawElementProviderFragment): HResult; override;
      stdcall;
  end;

  { a drawn button of the navigator: a Button element named by its hint, enabled as the button is (the dataset's
    state), with the Invoke pattern }
  TNavButtonProvider = class(TAutomationProvider, IInvokeProvider)
  private
    FOwner: TNavigatorAutomationProvider;                    { nil once the navigator let its provider go }
    FButton: TNavigateBtn;
    { Alive, and the button is still visible }
    function Live(out Res: HResult): Boolean;
    function Nav: TDBNavigator;
    procedure Forget;
  protected
    function IsOffscreen: Boolean; override;
  public
    constructor CreateButton(AOwner: TNavigatorAutomationProvider; AButton: TNavigateBtn);
    destructor Destroy; override;
    function GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult; override; stdcall;
    function GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult; override; stdcall;
    function Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult; override; stdcall;
    function GetRuntimeId(out pRetVal: PSafeArray): HResult; override; stdcall;
    function get_BoundingRectangle(out pRetVal: TUiaRect): HResult; override; stdcall;
    function get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult; override; stdcall;
    { IInvokeProvider }
    function Invoke: HResult; stdcall;
  end;

const
  SDeleteRecordQuestion = 'Delete record?';
  SConfirmCaption = 'Confirm';
  { rows of the list dataset a lookup combo box reads at most }
  MaxLookupItems = 10000;
  DefaultHints: array[TNavigateBtn] of string = ('First record', 'Prior record', 'Next record', 'Last record',
    'Insert record', 'Delete record', 'Edit record', 'Post edit', 'Cancel edit', 'Refresh data');

{ gives the focus to the control of a field link (DB.DataLinkFocusHook) }
procedure FocusLinkControl(Control: TComponent);
begin
  if (Control is TWinControl) and TWinControl(Control).CanFocus then
    TWinControl(Control).SetFocus;
end;

procedure RejectBeep;
begin
  if Assigned(RejectBeepHook) then
    RejectBeepHook
  else
    MessageBeep(0);
end;

{ a field whose value is bytes, not text }
function IsBinaryField(Field: TField): Boolean;
begin
  Result := (Field is TBlobField) and not (Field is TMemoField);
end;

{ every line feed or carriage return that is not part of a CR LF pair becomes CR LF }
function NormalizeLineBreaks(const S: string): string;
var
  I, N: Integer;
begin
  Result := '';
  N := Length(S);
  I := 1;
  while I <= N do
  begin
    if S[I] = #13 then
    begin
      Result := Result + #13#10;
      if (I < N) and (S[I + 1] = #10) then
        Inc(I);
    end
    else if S[I] = #10 then
      Result := Result + #13#10
    else
      Result := Result + S[I];
    Inc(I);
  end;
end;

{ the part of a ';'-separated list before the first separator }
function FirstAlternative(const List: string): string;
var
  P: Integer;
begin
  P := Pos(';', List);
  if P > 0 then
    Result := Copy(List, 1, P - 1)
  else
    Result := List;
end;

{ S equals one of the ';'-separated alternatives, without regard to case }
function IsAlternative(const List, S: string): Boolean;
var
  Rest: string;
  P: Integer;
begin
  Result := True;
  Rest := List;
  repeat
    P := Pos(';', Rest);
    if P = 0 then
      P := Length(Rest) + 1;
    if AnsiSameText(Copy(Rest, 1, P - 1), S) then
      Exit;
    Delete(Rest, 1, P);
  until Rest = '';
  Result := False;
end;

{ TDBEdit }

constructor TDBEdit.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { the text is data: the name is not copied into it }
  ControlStyle := ControlStyle - [csSetCaption];
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
  FDataLink.OnUpdateData := UpdateData;
  UpdateNative;
end;

destructor TDBEdit.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
end;

function TDBEdit.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBEdit.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
  UpdateNative;
end;

function TDBEdit.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBEdit.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
  UpdateNative;
end;

function TDBEdit.GetField: TField;
begin
  Result := FDataLink.Field;
end;

function TDBEdit.GetReadOnly: Boolean;
begin
  Result := FDataLink.ReadOnly;
end;

procedure TDBEdit.SetReadOnly(Value: Boolean);
begin
  FDataLink.ReadOnly := Value;
  UpdateNative;
end;

function TDBEdit.NativeReadOnly: Boolean;
begin
  Result := inherited ReadOnly;
end;

procedure TDBEdit.UpdateNative;
var
  F: TField;
  Limit: Integer;
begin
  F := FDataLink.Field;
  inherited ReadOnly := not FDataLink.CanModify or IsBinaryField(F);
  Limit := MaxLength;
  if (Limit = 0) and (F is TStringField) and (F.Size > 0) then
    Limit := F.Size;
  if HandleAllocated then
    SendMessageW(Handle, EM_LIMITTEXT, WPARAM(Limit), 0);
end;

procedure TDBEdit.CreateWnd;
begin
  inherited CreateWnd;
  UpdateNative;
end;

procedure TDBEdit.DataChange(Sender: TObject);
var
  S: string;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  UpdateNative;
  if csDesigning in ComponentState then
    S := Name
  else if FDataLink.Field = nil then
    S := ''
  else if FEntered and not NativeReadOnly then
    S := FDataLink.Field.Text
  else
    S := FDataLink.Field.DisplayText;
  if Text <> S then
    Text := S;
end;

procedure TDBEdit.UpdateData(Sender: TObject);
begin
  FDataLink.Field.Text := Text;
end;

function TDBEdit.EditCanModify: Boolean;
begin
  Result := (FDataLink <> nil) and FDataLink.Edit;
  if Result then
    FDataLink.Modified;
end;

procedure TDBEdit.WndProc(var Message: TMessage);
begin
  if (Message.Msg = CM_WANTSPECIALKEY) and (Message.WParam = VK_ESCAPE) and (FDataLink <> nil)
    and FDataLink.IsModified then
  begin
    Message.Result := 1;
    Exit;
  end;
  inherited WndProc(Message);
end;

procedure TDBEdit.KeyDown(var Key: Word; Shift: TShiftState);
begin
  inherited KeyDown(Key, Shift);
  if (Key = VK_ESCAPE) and FDataLink.IsModified then
  begin
    FDataLink.Reset;
    SelectAll;
    Key := 0;
  end;
end;

procedure TDBEdit.DoEnter;
begin
  FEntered := True;
  { the control still holds a text the field rejected: it stays }
  if not FDataLink.IsModified then
    DataChange(Self);
  inherited DoEnter;
end;

procedure TDBEdit.DoExit;
begin
  try
    FDataLink.UpdateRecord;
  except
    if CanFocus then
      SetFocus;
    raise;
  end;
  FEntered := False;
  DataChange(Self);
  inherited DoExit;
end;

procedure TDBEdit.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

procedure TDBEdit.SetName(const NewName: TComponentName);
begin
  inherited SetName(NewName);
  if csDesigning in ComponentState then
    Text := Name;
end;


{ TDBText }

constructor TDBText.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { the caption is data: the name is not copied into it, and '&' is an ordinary character }
  ControlStyle := ControlStyle - [csSetCaption];
  ShowAccelChar := False;
  AutoSize := False;
  SetBounds(Left, Top, 65, 17);
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
end;

destructor TDBText.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
end;

function TDBText.GetAlignment: TAlignment;
begin
  Result := inherited Alignment;
end;

procedure TDBText.SetAlignment(Value: TAlignment);
begin
  FAlignmentSet := True;
  inherited Alignment := Value;
end;

function TDBText.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBText.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
end;

function TDBText.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBText.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
end;

function TDBText.GetField: TField;
begin
  Result := FDataLink.Field;
end;

procedure TDBText.DataChange(Sender: TObject);
begin
  if csDestroying in ComponentState then
    Exit;
  if csDesigning in ComponentState then
    Caption := Name
  else if FDataLink.Field <> nil then
  begin
    if not FAlignmentSet then
      inherited Alignment := FDataLink.Field.Alignment;
    Caption := FDataLink.Field.DisplayText;
  end
  else
    Caption := '';
end;

procedure TDBText.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

procedure TDBText.SetName(const NewName: TComponentName);
begin
  inherited SetName(NewName);
  if csDesigning in ComponentState then
    Caption := Name;
end;

{ TDBMemo }

constructor TDBMemo.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle - [csSetCaption];
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
  FDataLink.OnUpdateData := UpdateData;
  inherited ReadOnly := True;
end;

destructor TDBMemo.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
end;

function TDBMemo.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBMemo.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
  DataChange(Self);
end;

function TDBMemo.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBMemo.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
  DataChange(Self);
end;

function TDBMemo.GetField: TField;
begin
  Result := FDataLink.Field;
end;

function TDBMemo.GetReadOnly: Boolean;
begin
  Result := FDataLink.ReadOnly;
end;

procedure TDBMemo.SetReadOnly(Value: Boolean);
begin
  FDataLink.ReadOnly := Value;
  DataChange(Self);
end;

function TDBMemo.NativeReadOnly: Boolean;
begin
  Result := inherited ReadOnly;
end;

procedure TDBMemo.DataChange(Sender: TObject);
var
  F: TField;
  IsText: Boolean;
  S: string;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  F := FDataLink.Field;
  IsText := (F is TStringField) or (F is TMemoField);
  inherited ReadOnly := not (FDataLink.CanModify and IsText);
  { a pending modification is kept when only the read-only state is evaluated again }
  if (Sender = Self) and FDataLink.IsModified then
    Exit;
  if csDesigning in ComponentState then
    S := Name
  else if F = nil then
    S := ''
  else if IsText then
    S := NormalizeLineBreaks(F.AsString)
  else
    S := F.DisplayText;
  if Text <> S then
    Text := S;
end;

procedure TDBMemo.UpdateData(Sender: TObject);
begin
  FDataLink.Field.AsString := Text;
end;

function TDBMemo.EditCanModify: Boolean;
begin
  Result := (FDataLink <> nil) and FDataLink.Edit;
  if Result then
    FDataLink.Modified;
end;

procedure TDBMemo.WndProc(var Message: TMessage);
begin
  if (Message.Msg = CM_WANTSPECIALKEY) and (Message.WParam = VK_ESCAPE) and (FDataLink <> nil)
    and FDataLink.IsModified then
  begin
    Message.Result := 1;
    Exit;
  end;
  inherited WndProc(Message);
end;

procedure TDBMemo.KeyDown(var Key: Word; Shift: TShiftState);
begin
  inherited KeyDown(Key, Shift);
  if (Key = VK_ESCAPE) and FDataLink.IsModified then
  begin
    FDataLink.Reset;
    Key := 0;
  end;
end;

procedure TDBMemo.DoExit;
begin
  try
    FDataLink.UpdateRecord;
  except
    if CanFocus then
      SetFocus;
    raise;
  end;
  inherited DoExit;
end;

procedure TDBMemo.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

procedure TDBMemo.SetName(const NewName: TComponentName);
begin
  inherited SetName(NewName);
  if csDesigning in ComponentState then
    Text := Name;
end;

{ TDBCheckBox }

constructor TDBCheckBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FValueChecked := 'True';
  FValueUnchecked := 'False';
  TabStop := True;
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
end;

destructor TDBCheckBox.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
end;

function TDBCheckBox.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBCheckBox.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
  DataChange(Self);
end;

function TDBCheckBox.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBCheckBox.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
end;

function TDBCheckBox.GetField: TField;
begin
  Result := FDataLink.Field;
end;

function TDBCheckBox.GetReadOnly: Boolean;
begin
  Result := FDataLink.ReadOnly;
end;

procedure TDBCheckBox.SetReadOnly(Value: Boolean);
begin
  FDataLink.ReadOnly := Value;
end;

procedure TDBCheckBox.SetValueChecked(const Value: string);
begin
  FValueChecked := Value;
  DataChange(Self);
end;

procedure TDBCheckBox.SetValueUnchecked(const Value: string);
begin
  FValueUnchecked := Value;
  DataChange(Self);
end;

function TDBCheckBox.FieldState: TCheckBoxState;
var
  F: TField;
  S: string;
begin
  F := FDataLink.Field;
  if F = nil then
    Result := cbUnchecked
  else if F.IsNull then
    Result := cbGrayed
  else if F is TBooleanField then
  begin
    if F.AsBoolean then
      Result := cbChecked
    else
      Result := cbUnchecked;
  end
  else
  begin
    S := F.Text;
    if IsAlternative(FValueChecked, S) then
      Result := cbChecked
    else if IsAlternative(FValueUnchecked, S) then
      Result := cbUnchecked
    else
      Result := cbGrayed;
  end;
end;

procedure TDBCheckBox.DataChange(Sender: TObject);
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  FQuiet := True;
  try
    State := FieldState;
  finally
    FQuiet := False;
  end;
end;

procedure TDBCheckBox.WriteCheckState;
var
  F: TField;
begin
  F := FDataLink.Field;
  if State = cbGrayed then
    F.Clear
  else if F is TBooleanField then
    F.AsBoolean := State = cbChecked
  else if State = cbChecked then
    F.Text := FirstAlternative(FValueChecked)
  else
    F.Text := FirstAlternative(FValueUnchecked);
end;

procedure TDBCheckBox.Toggle;
begin
  if not FDataLink.CanModify then
    Exit;
  if not FDataLink.Edit then
  begin
    RejectBeep;
    Exit;
  end;
  FQuiet := True;
  try
    inherited Toggle;
  finally
    FQuiet := False;
  end;
  try
    WriteCheckState;
  except
    FDataLink.Reset;
    raise;
  end;
  Click;
end;

procedure TDBCheckBox.Click;
begin
  if not FQuiet then
    inherited Click;
end;

procedure TDBCheckBox.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

{ TDBComboBox }

constructor TDBComboBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
  FDataLink.OnUpdateData := UpdateData;
end;

destructor TDBComboBox.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
end;

function TDBComboBox.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBComboBox.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
end;

function TDBComboBox.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBComboBox.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
end;

function TDBComboBox.GetField: TField;
begin
  Result := FDataLink.Field;
end;

function TDBComboBox.GetReadOnly: Boolean;
begin
  Result := FDataLink.ReadOnly;
end;

procedure TDBComboBox.SetReadOnly(Value: Boolean);
begin
  FDataLink.ReadOnly := Value;
end;

{ the item equal to the field text is selected; the drop-down style shows the field text itself }
procedure TDBComboBox.DataChange(Sender: TObject);
var
  S: string;
  I, Index: Integer;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  if FDataLink.Field <> nil then
    S := FDataLink.Field.Text
  else
    S := '';
  Index := -1;
  for I := 0 to Items.Count - 1 do
    if AnsiSameText(Items[I], S) then
    begin
      Index := I;
      Break;
    end;
  if ItemIndex <> Index then
    ItemIndex := Index;
  if (Style = csDropDown) and (Text <> S) then
    Text := S;
end;

procedure TDBComboBox.UpdateData(Sender: TObject);
begin
  FDataLink.Field.Text := Text;
end;

function TDBComboBox.EditCanModify: Boolean;
begin
  if (FDataLink = nil) or (csDesigning in ComponentState) then
  begin
    Result := inherited EditCanModify;
    Exit;
  end;
  if not FDataLink.CanModify then
  begin
    RejectSilently := True;
    Result := False;
    Exit;
  end;
  Result := FDataLink.Edit;
  if Result and not IsSelecting then
    FDataLink.Modified;
end;

procedure TDBComboBox.Change;
var
  I: Integer;
begin
  if IsSelecting and (FDataLink <> nil) and FDataLink.Editing then
  begin
    I := ItemIndex;
    if I >= 0 then
      try
        FDataLink.Field.Text := Items[I];
      except
        FDataLink.Reset;
        raise;
      end;
  end;
  inherited Change;
end;

procedure TDBComboBox.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_WANTSPECIALKEY:
      if (Message.WParam = VK_ESCAPE) and (FDataLink <> nil) and FDataLink.IsModified and not DroppedDown then
      begin
        Message.Result := 1;
        Exit;
      end;
  end;
  inherited WndProc(Message);
end;

procedure TDBComboBox.KeyDown(var Key: Word; Shift: TShiftState);
begin
  inherited KeyDown(Key, Shift);
  if (Key = VK_ESCAPE) and FDataLink.IsModified and not DroppedDown then
  begin
    FDataLink.Reset;
    Key := 0;
  end;
end;

procedure TDBComboBox.DoExit;
begin
  try
    FDataLink.UpdateRecord;
  except
    if CanFocus then
      SetFocus;
    raise;
  end;
  inherited DoExit;
end;

procedure TDBComboBox.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

{ TLookupListLink }

procedure TLookupListLink.ActiveChanged;
begin
  FCombo.RebuildList;
end;

procedure TLookupListLink.DataSetChanged;
begin
  FCombo.RebuildList;
end;

procedure TLookupListLink.DataSetScrolled(Distance: Integer);
begin
end;

procedure TLookupListLink.LayoutChanged;
begin
  FCombo.RebuildList;
end;

procedure TLookupListLink.RecordChanged(Field: TField);
begin
end;

{ TDBLookupComboBox }

constructor TDBLookupComboBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Style := csDropDownList;
  DropDownCount := 7;
  FKeys := TStringList.Create;
  FDataLink := TFieldDataLink.Create;
  FDataLink.Control := Self;
  FDataLink.OnDataChange := DataChange;
  FListLink := TLookupListLink.Create;
  TLookupListLink(FListLink).FCombo := Self;
  FListLink.BufferCount := MaxLookupItems;
end;

destructor TDBLookupComboBox.Destroy;
begin
  FListLink.Free;
  FListLink := nil;
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
  FKeys.Free;
end;

function TDBLookupComboBox.GetDataField: string;
begin
  Result := FDataLink.FieldName;
end;

procedure TDBLookupComboBox.SetDataField(const Value: string);
begin
  FDataLink.FieldName := Value;
end;

function TDBLookupComboBox.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBLookupComboBox.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
end;

function TDBLookupComboBox.GetListSource: TDataSource;
begin
  Result := FListLink.DataSource;
end;

procedure TDBLookupComboBox.SetListSource(Value: TDataSource);
begin
  if Value = FListLink.DataSource then
    Exit;
  FListLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
  RebuildList;
end;

function TDBLookupComboBox.GetField: TField;
begin
  Result := FDataLink.Field;
end;

function TDBLookupComboBox.GetReadOnly: Boolean;
begin
  Result := FDataLink.ReadOnly;
end;

procedure TDBLookupComboBox.SetReadOnly(Value: Boolean);
begin
  FDataLink.ReadOnly := Value;
end;

function TDBLookupComboBox.GetDropDownRows: Integer;
begin
  Result := DropDownCount;
end;

procedure TDBLookupComboBox.SetDropDownRows(Value: Integer);
begin
  DropDownCount := Value;
end;

procedure TDBLookupComboBox.SetKeyField(const Value: string);
begin
  if FKeyField <> Value then
  begin
    FKeyField := Value;
    RebuildList;
  end;
end;

procedure TDBLookupComboBox.SetListField(const Value: string);
begin
  if FListField <> Value then
  begin
    FListField := Value;
    RebuildList;
  end;
end;

procedure TDBLookupComboBox.SetNullValueText(const Value: string);
begin
  FNullValueText := Value;
  DataChange(Self);
end;

function TDBLookupComboBox.GetKeyValue: string;
begin
  if (FDataLink.Field <> nil) and not FDataLink.Field.IsNull then
    Result := FDataLink.Field.AsString
  else
    Result := '';
end;

function TDBLookupComboBox.GetDisplayText: string;
var
  I: Integer;
begin
  I := ItemIndex;
  if I >= 0 then
    Result := Items[I]
  else if FNullShown then
    Result := FNullValueText
  else
    Result := '';
end;

{ one item per row of the list dataset, read through the row window of the list link: the cursor stays }
procedure TDBLookupComboBox.RebuildList;
var
  KF, LF: TField;
  I, Saved: Integer;
begin
  if (FListLink = nil) or (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  Items.BeginUpdate;
  try
    Items.Clear;
    FKeys.Clear;
    if FListLink.Active and (FKeyField <> '') then
    begin
      KF := FListLink.DataSet.FindField(FKeyField);
      if FListField = '' then
        LF := KF
      else
        LF := FListLink.DataSet.FindField(FListField);
      if (KF <> nil) and (LF <> nil) then
      begin
        Saved := FListLink.ActiveRecord;
        try
          for I := 0 to FListLink.RecordCount - 1 do
          begin
            FListLink.ActiveRecord := I;
            FKeys.Add(KF.AsString);
            Items.Add(LF.DisplayText);
          end;
        finally
          FListLink.ActiveRecord := Saved;
        end;
      end;
    end;
  finally
    Items.EndUpdate;
  end;
  DataChange(Self);
end;

{ selects the item whose key is the field value; null shows NullValueText as the cue text of the control }
procedure TDBLookupComboBox.DataChange(Sender: TObject);
var
  F: TField;
  S: string;
  I, Index: Integer;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  F := FDataLink.Field;
  Index := -1;
  FNullShown := (F <> nil) and F.IsNull;
  if (F <> nil) and not FNullShown then
  begin
    S := F.AsString;
    for I := 0 to FKeys.Count - 1 do
      if FKeys[I] = S then
      begin
        Index := I;
        Break;
      end;
  end;
  if ItemIndex <> Index then
    ItemIndex := Index;
  if FNullShown then
    TextHint := FNullValueText
  else
    TextHint := '';
end;

function TDBLookupComboBox.EditCanModify: Boolean;
begin
  if (FDataLink = nil) or (csDesigning in ComponentState) then
  begin
    Result := inherited EditCanModify;
    Exit;
  end;
  if not FDataLink.CanModify then
  begin
    RejectSilently := True;
    Result := False;
    Exit;
  end;
  Result := FDataLink.Edit;
end;

procedure TDBLookupComboBox.Change;
var
  I: Integer;
begin
  if (FDataLink <> nil) and FDataLink.Editing then
  begin
    I := ItemIndex;
    if (I >= 0) and (I < FKeys.Count) then
      try
        FDataLink.Field.Text := FKeys[I];
      except
        FDataLink.Reset;
        raise;
      end;
  end;
  inherited Change;
end;

procedure TDBLookupComboBox.KeyDown(var Key: Word; Shift: TShiftState);
begin
  inherited KeyDown(Key, Shift);
  if ((Key = VK_DELETE) or (Key = VK_BACK)) and not DroppedDown then
  begin
    Key := 0;
    if not FDataLink.CanModify or FDataLink.Field.Required then
      Exit;
    if FDataLink.Edit then
      FDataLink.Field.Clear
    else
      RejectBeep;
  end;
end;

procedure TDBLookupComboBox.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if Operation <> opRemove then
    Exit;
  if (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
  if (FListLink <> nil) and (AComponent = FListLink.DataSource) then
  begin
    FListLink.DataSource := nil;
    RebuildList;
  end;
end;

{ TNavigatorLink }

procedure TNavigatorLink.ActiveChanged;
begin
  FNavigator.LinkChanged;
end;

procedure TNavigatorLink.EditingChanged;
begin
  FNavigator.LinkChanged;
end;

procedure TNavigatorLink.DataSetChanged;
begin
  FNavigator.LinkChanged;
end;

{ ---------------------------------------------------------------- UI Automation of the navigator (A-083) }

const
  { posted to the navigator by the Invoke of a button's provider: WParam = Ord of the button (private to DBCtrls;
    posting keeps a click that shows the delete confirmation from blocking the client, as UIF-1430) }
  CM_NAVIGATORCLICK = CM_BASE + 200;
  NavButtonIds: array[TNavigateBtn] of string = ('nbFirst', 'nbPrior', 'nbNext', 'nbLast', 'nbInsert', 'nbDelete',
    'nbEdit', 'nbPost', 'nbCancel', 'nbRefresh');

procedure PutStr(var V: TVariantArg; const S: string);
begin
  V.vt := VT_BSTR;
  V.bstrVal := SysAllocStringLen(PWideChar(S), Length(S));
end;

procedure PutInt(var V: TVariantArg; I: Integer);
begin
  V.vt := VT_I4;
  V.lVal := I;
end;

procedure PutBool(var V: TVariantArg; B: Boolean);
begin
  V.vt := VT_BOOL;
  if B then
    V.boolVal := -1
  else
    V.boolVal := 0;
end;

{ the visible button after / before B in the order of TNavigateBtn; False = none }
function NextNavButton(N: TDBNavigator; var B: TNavigateBtn): Boolean;
var
  I: TNavigateBtn;
begin
  Result := False;
  if B = High(TNavigateBtn) then
    Exit;
  for I := Succ(B) to High(TNavigateBtn) do
    if I in N.FVisibleButtons then
    begin
      B := I;
      Result := True;
      Exit;
    end;
end;

function PrevNavButton(N: TDBNavigator; var B: TNavigateBtn): Boolean;
var
  I: TNavigateBtn;
begin
  Result := False;
  if B = Low(TNavigateBtn) then
    Exit;
  for I := Pred(B) downto Low(TNavigateBtn) do
    if I in N.FVisibleButtons then
    begin
      B := I;
      Result := True;
      Exit;
    end;
end;

{ the button is enabled for a client: the navigator and every parent enabled, and the button enabled by the
  dataset's state }
function NavButtonEnabled(N: TDBNavigator; B: TNavigateBtn): Boolean;
begin
  Result := AccessibleEnabledOf(N) and N.ButtonEnabled(B);
end;

{ the click of a button posted by its Invoke: clicked only when it is still visible and enabled; then
  Invoke_Invoked when the navigator still has a provider and a client listens }
procedure NavigatorClickPosted(N: TDBNavigator; B: TNavigateBtn);
var
  P: TNavigatorAutomationProvider;
  Root, Button: IRawElementProviderSimple;
begin
  if not (B in N.FVisibleButtons) or not NavButtonEnabled(N, B) then
    Exit;
  if not (N.AutomationObject is TNavigatorAutomationProvider) then
  begin
    N.BtnClick(B);
    Exit;
  end;
  P := TNavigatorAutomationProvider(N.AutomationObject);
  Root := P;                                                 { P lives through the click, also when N goes }
  N.BtnClick(B);
  if (P.Control = nil) or (N.AutomationObject <> P) or not (B in N.FVisibleButtons) then
    Exit;
  Button := P.ButtonObject(B);
  RaiseFragmentEvent(N, Button, UIA_Invoke_InvokedEventId);
end;

{ ---------------------------------------------------------------- TNavigatorAutomationProvider }

class function TNavigatorAutomationProvider.DefaultControlType(Control: TControl): Integer;
begin
  Result := UIA_ToolBarControlTypeId;
end;

function TNavigatorAutomationProvider.Nav: TDBNavigator;
begin
  Result := TDBNavigator(Control);
end;

function TNavigatorAutomationProvider.ButtonObject(B: TNavigateBtn): TNavButtonProvider;
begin
  Result := FButtons[B];
  if Result = nil then
    Result := TNavButtonProvider.CreateButton(Self, B);
end;

{ UIF-1426 (as for grid cells): the buttons are disconnected while the navigator's provider still answers for the
  navigator, each kept alive by a reference of its own through its disconnect }
procedure TNavigatorAutomationProvider.DisconnectFragments;
var
  Held: array of IRawElementProviderSimple;
  B: TNavigateBtn;
  I: Integer;
begin
  for B := Low(TNavigateBtn) to High(TNavigateBtn) do
    if FButtons[B] <> nil then
    begin
      SetLength(Held, Length(Held) + 1);
      Held[Length(Held) - 1] := FButtons[B];
    end;
  for I := 0 to Length(Held) - 1 do
    UiaDisconnectProvider(Held[I]);
  for B := Low(TNavigateBtn) to High(TNavigateBtn) do
    if FButtons[B] <> nil then
    begin
      FButtons[B].Forget;
      FButtons[B] := nil;
    end;
  Held := nil;
end;

{ the children: the visible buttons }
function TNavigatorAutomationProvider.Navigate(direction: Integer;
  out pRetVal: IRawElementProviderFragment): HResult;
var
  N: TDBNavigator;
  B: TNavigateBtn;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  N := Nav;
  case direction of
    NavigateDirection_FirstChild:
      begin
        B := Low(TNavigateBtn);
        if (B in N.FVisibleButtons) or NextNavButton(N, B) then
          pRetVal := ButtonObject(B);
      end;
    NavigateDirection_LastChild:
      begin
        B := High(TNavigateBtn);
        if (B in N.FVisibleButtons) or PrevNavButton(N, B) then
          pRetVal := ButtonObject(B);
      end;
  end;
end;

{ the button under the point, else the navigator }
function TNavigatorAutomationProvider.ElementProviderFromPoint(x, y: Double;
  out pRetVal: IRawElementProviderFragment): HResult;
var
  N: TDBNavigator;
  P: TPoint;
  B: TNavigateBtn;
begin
  PPointer(@pRetVal)^ := nil;
  if not Alive(Result) then
    Exit;
  N := Nav;
  if N.HandleAllocated then
  begin
    P := Point(Round(x), Round(y));
    ScreenToClient(N.Handle, P);
    if N.ButtonAt(P.X, P.Y, B) then
    begin
      pRetVal := ButtonObject(B);
      Exit;
    end;
  end;
  pRetVal := Self;
end;

{ ---------------------------------------------------------------- TNavButtonProvider }

constructor TNavButtonProvider.CreateButton(AOwner: TNavigatorAutomationProvider; AButton: TNavigateBtn);
begin
  inherited Create(AOwner.Control);
  FOwner := AOwner;
  FButton := AButton;
  AOwner.FButtons[AButton] := Self;
end;

destructor TNavButtonProvider.Destroy;
begin
  if (FOwner <> nil) and (FOwner.FButtons[FButton] = Self) then
    FOwner.FButtons[FButton] := nil;
  inherited Destroy;
end;

procedure TNavButtonProvider.Forget;
begin
  FOwner := nil;
  ForgetControl;
end;

function TNavButtonProvider.Nav: TDBNavigator;
begin
  Result := TDBNavigator(Control);
end;

{ a button hidden by VisibleButtons answers UIA_E_ELEMENTNOTAVAILABLE (again available when it is shown again) }
function TNavButtonProvider.Live(out Res: HResult): Boolean;
begin
  Result := Alive(Res);
  if Result and not (FButton in Nav.FVisibleButtons) then
  begin
    Result := False;
    Res := UIA_E_ELEMENTNOTAVAILABLE;
  end;
end;

function TNavButtonProvider.IsOffscreen: Boolean;
begin
  Result := not Nav.Showing;
end;

function TNavButtonProvider.GetPatternProvider(patternId: Integer; out pRetVal: IUnknown): HResult;
var
  Ip: IInvokeProvider;
begin
  PPointer(@pRetVal)^ := nil;
  if Live(Result) and (patternId = UIA_InvokePatternId) then
  begin
    Ip := Self;
    pRetVal := Ip;
  end;
end;

function TNavButtonProvider.GetPropertyValue(propertyId: Integer; out pRetVal: TVariantArg): HResult;
var
  N: TDBNavigator;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  if not Live(Result) then
    Exit;
  N := Nav;
  try
    case propertyId of
      UIA_NamePropertyId:
        PutStr(pRetVal, GetShortHint(N.ButtonHint(FButton)));
      UIA_AutomationIdPropertyId:
        PutStr(pRetVal, NavButtonIds[FButton]);
      UIA_FrameworkIdPropertyId:
        PutStr(pRetVal, 'FreexComp');
      UIA_ControlTypePropertyId:
        PutInt(pRetVal, UIA_ButtonControlTypeId);
      UIA_IsEnabledPropertyId:
        PutBool(pRetVal, NavButtonEnabled(N, FButton));
      UIA_IsOffscreenPropertyId:
        PutBool(pRetVal, IsOffscreen);
      UIA_IsKeyboardFocusablePropertyId, UIA_HasKeyboardFocusPropertyId:
        PutBool(pRetVal, False);
      UIA_IsControlElementPropertyId, UIA_IsContentElementPropertyId:
        PutBool(pRetVal, True);
    end;
  except
    FillChar(pRetVal, SizeOf(pRetVal), 0);
    Result := E_FAIL;
  end;
end;

{ siblings: the neighbouring visible buttons }
function TNavButtonProvider.Navigate(direction: Integer; out pRetVal: IRawElementProviderFragment): HResult;
var
  B: TNavigateBtn;
begin
  PPointer(@pRetVal)^ := nil;
  if not Live(Result) then
    Exit;
  B := FButton;
  case direction of
    NavigateDirection_Parent:
      pRetVal := FOwner;
    NavigateDirection_NextSibling:
      if NextNavButton(Nav, B) then
        pRetVal := FOwner.ButtonObject(B);
    NavigateDirection_PreviousSibling:
      if PrevNavButton(Nav, B) then
        pRetVal := FOwner.ButtonObject(B);
  end;
end;

{ [UiaAppendRuntimeId, Ord of the button]. The identity (runtime id, fragment root) is answered as long as the
  navigator exists, also for a hidden button: the disconnect of UIF-1426 asks for it }
function TNavButtonProvider.GetRuntimeId(out pRetVal: PSafeArray): HResult;
var
  I, V: Integer;
begin
  pRetVal := nil;
  if not Alive(Result) then
    Exit;
  pRetVal := SafeArrayCreateVector(VT_I4, 0, 2);
  if pRetVal = nil then
  begin
    Result := E_OUTOFMEMORY;
    Exit;
  end;
  I := 0;
  V := UiaAppendRuntimeId;
  SafeArrayPutElement(pRetVal, @I, @V);
  I := 1;
  V := Ord(FButton);
  SafeArrayPutElement(pRetVal, @I, @V);
end;

{ ButtonRect in screen coordinates }
function TNavButtonProvider.get_BoundingRectangle(out pRetVal: TUiaRect): HResult;
var
  N: TDBNavigator;
  R: TRect;
  P: TPoint;
begin
  FillChar(pRetVal, SizeOf(pRetVal), 0);
  if not Live(Result) then
    Exit;
  N := Nav;
  if not N.HandleAllocated or IsOffscreen then
    Exit;
  R := N.ButtonRect(FButton);
  P := Point(R.Left, R.Top);
  ClientToScreen(N.Handle, P);
  pRetVal.left := P.X;
  pRetVal.top := P.Y;
  pRetVal.width := R.Right - R.Left;
  pRetVal.height := R.Bottom - R.Top;
end;

function TNavButtonProvider.get_FragmentRoot(out pRetVal: IRawElementProviderFragmentRoot): HResult;
begin
  PPointer(@pRetVal)^ := nil;
  if Alive(Result) then
    pRetVal := FOwner;
end;

{ a disabled button answers UIA_E_ELEMENTNOTENABLED; else the click is posted to the navigator (BtnClick when the
  message is handled, as a mouse click) }
function TNavButtonProvider.Invoke: HResult;
var
  N: TDBNavigator;
begin
  if not Live(Result) then
    Exit;
  N := Nav;
  if not NavButtonEnabled(N, FButton) then
    Result := UIA_E_ELEMENTNOTENABLED
  else if not N.HandleAllocated or not PostMessageW(N.Handle, CM_NAVIGATORCLICK, WPARAM(Ord(FButton)), 0) then
    Result := UIA_E_INVALIDOPERATION;
end;

{ TDBNavigator }

constructor TDBNavigator.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  { the buttons have their own click event; the control has no caption }
  ControlStyle := ControlStyle - [csClickEvents, csDoubleClicks, csSetCaption];
  FVisibleButtons := [nbFirst, nbPrior, nbNext, nbLast, nbInsert, nbDelete, nbEdit, nbPost, nbCancel, nbRefresh];
  FConfirmDelete := True;
  FHints := TStringList.Create;
  FDataLink := TNavigatorLink.Create;
  TNavigatorLink(FDataLink).FNavigator := Self;
  SetBounds(Left, Top, 240, 25);
end;

destructor TDBNavigator.Destroy;
begin
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
  FHints.Free;
end;

function TDBNavigator.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBNavigator.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
  LinkChanged;
end;

procedure TDBNavigator.SetVisibleButtons(Value: TNavButtonSet);
begin
  FVisibleButtons := Value;
  Invalidate;
end;

procedure TDBNavigator.SetHints(Value: TStrings);
begin
  FHints.Assign(Value);
end;

procedure TDBNavigator.LinkChanged;
begin
  if (FDataLink <> nil) and not (csDestroying in ComponentState) then
    Invalidate;
end;

procedure TDBNavigator.CMEnabledChanged(var Message: TMessage);
begin
  inherited CMEnabledChanged(Message);
  Invalidate;
end;

function TDBNavigator.ButtonEnabled(Index: TNavigateBtn): Boolean;
var
  DS: TDataSet;
  CanChange, Editing: Boolean;
begin
  Result := False;
  if (FDataLink = nil) or not Enabled or not FDataLink.Active then
    Exit;
  DS := FDataLink.DataSet;
  CanChange := DS.CanModify;
  Editing := DS.State in [dsEdit, dsInsert];
  case Index of
    nbFirst, nbPrior: Result := not DS.Bof;
    nbNext, nbLast: Result := not DS.Eof;
    nbInsert: Result := CanChange;
    nbDelete: Result := CanChange and not DS.IsEmpty and (DS.State <> dsInsert);
    nbEdit: Result := CanChange and not Editing;
    nbPost, nbCancel: Result := Editing;
    nbRefresh: Result := True;
  end;
end;

function TDBNavigator.ButtonRect(Index: TNavigateBtn): TRect;
var
  B: TNavigateBtn;
  Count, Position, W: Integer;
begin
  Result := Rect(0, 0, 0, 0);
  if not (Index in FVisibleButtons) then
    Exit;
  Count := 0;
  Position := 0;
  for B := nbFirst to nbRefresh do
    if B in FVisibleButtons then
    begin
      if B = Index then
        Position := Count;
      Inc(Count);
    end;
  W := ClientWidth div Count;
  Result := Rect(Position * W, 0, (Position + 1) * W, ClientHeight);
  if Position = Count - 1 then
    Result.Right := ClientWidth;
end;

function TDBNavigator.ButtonAt(X, Y: Integer; var Index: TNavigateBtn): Boolean;
var
  B: TNavigateBtn;
  R: TRect;
begin
  Result := True;
  for B := nbFirst to nbRefresh do
    if B in FVisibleButtons then
    begin
      R := ButtonRect(B);
      if (X >= R.Left) and (X < R.Right) and (Y >= R.Top) and (Y < R.Bottom) then
      begin
        Index := B;
        Exit;
      end;
    end;
  Result := False;
end;

function TDBNavigator.ButtonHint(Index: TNavigateBtn): string;
begin
  if Ord(Index) < FHints.Count then
    Result := FHints[Ord(Index)]
  else
    Result := '';
  if Result = '' then
    Result := DefaultHints[Index];
end;

function TDBNavigator.ConfirmDeletion: Boolean;
var
  Answer: Integer;
  Wnd: HWND;
begin
  if Assigned(MessageBoxHook) then
    Answer := MessageBoxHook(SDeleteRecordQuestion)
  else
  begin
    if HandleAllocated then
      Wnd := Handle
    else
      Wnd := 0;
    Answer := MessageBoxW(Wnd, PChar(SDeleteRecordQuestion), PChar(SConfirmCaption),
      MB_OKCANCEL or MB_ICONQUESTION);
  end;
  Result := Answer = IDOK;
end;

procedure TDBNavigator.BtnClick(Index: TNavigateBtn);
var
  DS: TDataSet;
begin
  if not ButtonEnabled(Index) then
    Exit;
  if Assigned(FBeforeAction) then
    FBeforeAction(Self, Index);
  { the handler may have changed the situation }
  if not FDataLink.Active then
    Exit;
  DS := FDataLink.DataSet;
  case Index of
    nbFirst: DS.First;
    nbPrior: DS.Prior;
    nbNext: DS.Next;
    nbLast: DS.Last;
    nbInsert: DS.Insert;
    nbDelete:
      begin
        if FConfirmDelete and not ConfirmDeletion then
          Exit;
        DS.Delete;
      end;
    nbEdit: DS.Edit;
    nbPost: DS.Post;
    nbCancel: DS.Cancel;
    nbRefresh: DS.Refresh;
  end;
  if Assigned(FOnNavClick) then
    FOnNavClick(Self, Index);
end;

{ simple shapes in a square around the centre of the button }
procedure TDBNavigator.DrawGlyph(Index: TNavigateBtn; const R: TRect; AColor: TColor);
var
  CX, CY, H: Integer;
begin
  H := R.Right - R.Left;
  if R.Bottom - R.Top < H then
    H := R.Bottom - R.Top;
  H := (H - 12) div 2;
  if H < 3 then
    H := 3;
  CX := (R.Left + R.Right) div 2;
  CY := (R.Top + R.Bottom) div 2;
  Canvas.Pen.Color := AColor;
  Canvas.Pen.Width := 1;
  Canvas.Brush.Color := AColor;
  case Index of
    nbFirst:
      begin
        Canvas.FillRect(Rect(CX - H, CY - H, CX - H + 2, CY + H + 1));
        Canvas.Polygon([Point(CX + H, CY - H), Point(CX + H, CY + H), Point(CX - H + 3, CY)]);
      end;
    nbPrior:
      Canvas.Polygon([Point(CX + H div 2 + 1, CY - H), Point(CX + H div 2 + 1, CY + H), Point(CX - H div 2 - 1, CY)]);
    nbNext:
      Canvas.Polygon([Point(CX - H div 2 - 1, CY - H), Point(CX - H div 2 - 1, CY + H), Point(CX + H div 2 + 1, CY)]);
    nbLast:
      begin
        Canvas.Polygon([Point(CX - H, CY - H), Point(CX - H, CY + H), Point(CX + H - 3, CY)]);
        Canvas.FillRect(Rect(CX + H - 1, CY - H, CX + H + 1, CY + H + 1));
      end;
    nbInsert:
      begin
        Canvas.FillRect(Rect(CX - H, CY - 1, CX + H + 1, CY + 1));
        Canvas.FillRect(Rect(CX - 1, CY - H, CX + 1, CY + H + 1));
      end;
    nbDelete: Canvas.FillRect(Rect(CX - H, CY - 1, CX + H + 1, CY + 1));
    nbEdit: Canvas.Polygon([Point(CX - H, CY + H), Point(CX + H, CY + H), Point(CX, CY - H)]);
    nbPost:
      begin
        Canvas.Pen.Width := 2;
        Canvas.MoveTo(CX - H, CY);
        Canvas.LineTo(CX - H div 3, CY + H);
        Canvas.LineTo(CX + H, CY - H);
      end;
    nbCancel:
      begin
        Canvas.Pen.Width := 2;
        Canvas.MoveTo(CX - H, CY - H);
        Canvas.LineTo(CX + H, CY + H);
        Canvas.MoveTo(CX + H, CY - H);
        Canvas.LineTo(CX - H, CY + H);
      end;
    nbRefresh:
      begin
        Canvas.Arc(CX - H, CY - H, CX + H + 1, CY + H + 1, CX + H, CY, CX, CY + H);
        Canvas.Polygon([Point(CX + H - 3, CY - 1), Point(CX + H + 3, CY - 1), Point(CX + H, CY + 3)]);
      end;
  end;
  Canvas.Pen.Width := 1;
end;

procedure TDBNavigator.Paint;
var
  B: TNavigateBtn;
  R: TRect;
  Edge: UINT;
begin
  Canvas.Brush.Color := clBtnFace;
  Canvas.FillRect(ClientRect);
  for B := nbFirst to nbRefresh do
    if B in FVisibleButtons then
    begin
      R := ButtonRect(B);
      if FDown and (FDownButton = B) then
        Edge := EDGE_SUNKEN
      else
        Edge := EDGE_RAISED;
      DrawEdge(Canvas.Handle, R, Edge, BF_RECT);
      if ButtonEnabled(B) then
        DrawGlyph(B, R, clBtnText)
      else
        DrawGlyph(B, R, clGrayText);
    end;
end;

procedure TDBNavigator.MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
var
  B: TNavigateBtn;
begin
  inherited MouseDown(Button, Shift, X, Y);
  if (Button = mbLeft) and ButtonAt(X, Y, B) and ButtonEnabled(B) then
  begin
    FDown := True;
    FDownButton := B;
    Invalidate;
  end;
end;

procedure TDBNavigator.MouseMove(Shift: TShiftState; X, Y: Integer);
var
  B: TNavigateBtn;
  S: string;
begin
  inherited MouseMove(Shift, X, Y);
  if ButtonAt(X, Y, B) then
    S := ButtonHint(B)
  else
    S := '';
  if Hint <> S then
    Hint := S;
end;

procedure TDBNavigator.MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
var
  B: TNavigateBtn;
begin
  inherited MouseUp(Button, Shift, X, Y);
  if (Button = mbLeft) and FDown then
  begin
    FDown := False;
    Invalidate;
    if ButtonAt(X, Y, B) and (B = FDownButton) then
      BtnClick(B);
  end;
end;

procedure TDBNavigator.WndProc(var Message: TMessage);
begin
  if Message.Msg <> CM_NAVIGATORCLICK then
  begin
    inherited WndProc(Message);
    Exit;
  end;
  Message.Result := 0;
  if (Message.WParam <= WPARAM(Ord(High(TNavigateBtn)))) and not (csDestroying in ComponentState) then
    NavigatorClickPosted(Self, TNavigateBtn(Message.WParam));
end;

procedure TDBNavigator.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    FDataLink.DataSource := nil;
end;

initialization
  DataLinkFocusHook := FocusLinkControl;
  RegisterClasses([TDBEdit, TDBText, TDBMemo, TDBCheckBox, TDBComboBox, TDBLookupComboBox, TDBNavigator]);
  RegisterAutomationProvider(TDBNavigator, TNavigatorAutomationProvider);

end.
