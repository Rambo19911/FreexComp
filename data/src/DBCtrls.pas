{ DBCtrls - the data-aware controls: each shows (and, where it can, edits) one field of the dataset behind its
  data source through a field data link. }
unit DBCtrls;

interface

uses
  Classes, Controls, StdCtrls, DB;

type
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

implementation

type
  { Reaches the Alignment property of TCustomLabel, which TDBText declares again. (The compiler does not accept
    the inherited-property form yet; replace the casts when it does.) }
  TLabelAccess = class(TCustomLabel);

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
  Result := TLabelAccess(Self).Alignment;
end;

procedure TDBText.SetAlignment(Value: TAlignment);
begin
  FAlignmentSet := True;
  TLabelAccess(Self).Alignment := Value;
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
      TLabelAccess(Self).Alignment := FDataLink.Field.Alignment;
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

end.
