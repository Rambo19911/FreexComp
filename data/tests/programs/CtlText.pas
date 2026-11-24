// covers: DAT-220, DAT-226, DAT-235
program CtlText;
{ TDBText: the caption shows the display text of its field and follows every change of the data; empty while the
  link is inactive or the field is not resolvable; the name at design time; alignment follows the field until it
  is set explicitly; DataSource is cleared when the source is destroyed. The control has no window: the test
  reads its state through properties and shows nothing on the screen. }
uses
  SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls, DataTestUtils, TestDataSet;

type
  { an owner in design state: components created with it are in design state too }
  TDesignRoot = class(TComponent)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTextHandler = class
  public
    procedure GetText(Sender: TField; var Text: string; DisplayText: Boolean);
  end;

constructor TDesignRoot.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetDesigning(True);
end;

procedure TTextHandler.GetText(Sender: TField; var Text: string; DisplayText: Boolean);
begin
  if DisplayText then
    Text := '<' + Sender.AsString + '>'
  else
    Text := 'edit:' + Sender.AsString;
end;

{ the caption of a control (TDBText does not publish Caption) }
function Cap(Control: TControl): string;
begin
  SetLength(Result, Control.GetTextLen);
  if Result <> '' then
    Control.GetTextBuf(PChar(Result), Length(Result) + 1);
end;

function AlignName(Value: TAlignment): string;
begin
  case Value of
    taLeftJustify: Result := 'left';
    taRightJustify: Result := 'right';
  else
    Result := 'center';
  end;
end;

{ id, name, flag; rows 1 n1 True, 2 n2 False, 3 NULL NULL; not opened }
function NewDS(AOwner: TComponent): TTestDataSet;
begin
  Result := TTestDataSet.Create(AOwner);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddDef('flag', ftBoolean);
  Result.AddRow([IntValue(1), StringValue('n1'), IntValue(1)]);
  Result.AddRow([IntValue(2), StringValue('n2'), IntValue(0)]);
  Result.AddRow([IntValue(3), NullValue, NullValue]);
end;

procedure TestDefaults(F: TForm);
var
  T: TDBText;
  C: TControl;
begin
  Section('defaults');
  T := TDBText.Create(F);
  T.Parent := F;
  C := T;
  Check(C is TCustomLabel, 'a label control');
  Check(not (C is TWinControl), 'no window: never focusable');
  CheckEqBool(T.AutoSize, False, 'AutoSize is False by default');
  CheckEqInt(T.Width, 65, 'Width');
  CheckEqInt(T.Height, 17, 'Height');
  CheckEq(Cap(T), '', 'Caption is empty');
  CheckEq(AlignName(T.Alignment), 'left', 'Alignment');
  Check(T.DataSource = nil, 'DataSource');
  CheckEq(T.DataField, '', 'DataField');
  Check(T.Field = nil, 'Field');
  T.Name := 'IdText';
  CheckEq(Cap(T), '', 'the name is not shown at run time');
  T.Free;
end;

procedure TestDisplay(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  T: TDBText;
  H: TTextHandler;
begin
  Section('display');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  T := TDBText.Create(F);
  T.Parent := F;
  T.DataSource := S;
  T.DataField := 'name';
  Check(T.DataSource = S, 'DataSource reads back');
  CheckEq(T.DataField, 'name', 'DataField reads back');
  CheckEq(Cap(T), '', 'closed dataset: empty');
  Check(T.Field = nil, 'closed dataset: no field');
  DS.Open;
  CheckEq(Cap(T), 'n1', 'Open: the value of the current row');
  Check(T.Field = DS.FieldByName('name'), 'Field');
  DS.Next;
  CheckEq(Cap(T), 'n2', 'Next');
  DS.Last;
  CheckEq(Cap(T), '', 'a null value: empty');
  DS.First;
  DS.Edit;
  DS.FieldByName('name').AsString := 'a&b';
  CheckEq(Cap(T), 'a&b', 'a field write shows at once');
  DS.FieldByName('id').AsInteger := 10;
  CheckEq(Cap(T), 'a&b', 'a write to another field changes nothing');
  DS.Cancel;
  CheckEq(Cap(T), 'n1', 'Cancel restores');
  DS.Insert;
  CheckEq(Cap(T), '', 'Insert: the new row is empty');
  DS.FieldByName('name').AsString := 'new';
  CheckEq(Cap(T), 'new', 'typed into the new row');
  DS.Cancel;
  CheckEq(Cap(T), 'n1', 'Cancel of the insert');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the control never edits');

  H := TTextHandler.Create;
  DS.FieldByName('name').OnGetText := H.GetText;
  DS.Next;
  CheckEq(Cap(T), '<n2>', 'the display text, not the edit text');
  DS.FieldByName('name').OnGetText := nil;
  DS.Prior;
  CheckEq(Cap(T), 'n1', 'without the handler');
  H.Free;

  T.DataField := 'flag';
  CheckEq(Cap(T), DS.FieldByName('flag').DisplayText, 'DataField changed: the other field');
  Check(Cap(T) <> '', 'the boolean has a display text');
  T.DataField := 'ID';
  CheckEq(Cap(T), '1', 'DataField without regard to case');
  T.DataField := 'nope';
  CheckEq(Cap(T), '', 'an unknown field: empty, no exception');
  Check(T.Field = nil, 'an unknown field: Field is nil');
  T.DataField := 'name';
  CheckEq(Cap(T), 'n1', 'resolved again');
  DS.Close;
  CheckEq(Cap(T), '', 'Close: empty');
  DS.Open;
  CheckEq(Cap(T), 'n1', 'Open again');
  S.Enabled := False;
  CheckEq(Cap(T), '', 'a disabled source: empty');
  S.Enabled := True;
  CheckEq(Cap(T), 'n1', 'enabled again');
  DS.DisableControls;
  DS.Next;
  CheckEq(Cap(T), 'n1', 'controls disabled: no update');
  DS.EnableControls;
  CheckEq(Cap(T), 'n2', 'EnableControls refreshes');
  T.Free;
  DS.Next;
  CheckEq(DS.FieldByName('id').AsString, '3', 'the dataset works after the control is destroyed');
  S.Free;
  DS.Free;
end;

procedure TestAlignment(F: TForm);
var
  DS: TTestDataSet;
  S: TDataSource;
  T: TDBText;
begin
  Section('alignment');
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  DS.Open;
  T := TDBText.Create(F);
  T.Parent := F;
  T.DataSource := S;
  T.DataField := 'id';
  CheckEq(AlignName(DS.FieldByName('id').Alignment), 'right', 'an integer field is right-aligned');
  CheckEq(AlignName(T.Alignment), 'right', 'the control follows the field');
  T.DataField := 'name';
  CheckEq(AlignName(T.Alignment), 'left', 'a string field: left');
  DS.FieldByName('name').Alignment := taCenter;
  DS.Next;
  CheckEq(AlignName(T.Alignment), 'center', 'a changed field alignment is picked up with the next data change');
  T.DataField := 'nope';
  CheckEq(AlignName(T.Alignment), 'center', 'no field: the alignment stays');
  T.Alignment := taLeftJustify;
  T.DataField := 'id';
  CheckEq(AlignName(T.Alignment), 'left', 'set explicitly: the field no longer decides');
  DS.Next;
  CheckEq(AlignName(T.Alignment), 'left', 'also after a data change');
  CheckEq(Cap(T), '3', 'the caption still follows');
  T.Free;
  S.Free;
  DS.Free;
end;

procedure TestSourceLifetime(F: TForm);
var
  DS, DS2: TTestDataSet;
  S, S2: TDataSource;
  T: TDBText;
  Box: TComponent;
begin
  Section('source changes and lifetime');
  DS := NewDS(nil);
  DS2 := NewDS(nil);
  DS.Open;
  DS2.Open;
  DS2.Next;
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  Box := TComponent.Create(nil);
  S2 := TDataSource.Create(Box);
  S2.DataSet := DS2;
  T := TDBText.Create(F);
  T.Parent := F;
  T.DataField := 'name';
  CheckEq(Cap(T), '', 'no source: empty');
  T.DataSource := S;
  CheckEq(Cap(T), 'n1', 'DataSource assigned');
  T.DataSource := S2;
  CheckEq(Cap(T), 'n2', 'another DataSource: its current row');
  DS.Next;
  CheckEq(Cap(T), 'n2', 'the old source no longer updates the control');
  S2.DataSet := DS;
  CheckEq(Cap(T), 'n2', 'another dataset of the source');
  DS.Prior;
  CheckEq(Cap(T), 'n1', 'follows the new dataset');
  Box.Free;
  Check(T.DataSource = nil, 'destroying the source clears DataSource');
  CheckEq(Cap(T), '', 'and the caption');
  T.DataSource := S;
  CheckEq(Cap(T), 'n1', 'attached again');
  DS.Free;
  CheckEq(Cap(T), '', 'destroying the dataset: empty');
  Check(T.Field = nil, 'no field');
  Check(T.DataSource = S, 'the source stays');
  S.Free;
  Check(T.DataSource = nil, 'the source is gone');
  T.Free;
  DS2.Free;
end;

procedure TestDesignTime;
var
  Root: TDesignRoot;
  DS: TTestDataSet;
  S: TDataSource;
  T: TDBText;
begin
  Section('design time');
  Root := TDesignRoot.Create(nil);
  DS := NewDS(nil);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  T := TDBText.Create(Root);
  Check(csDesigning in T.ComponentState, 'the control is in design state');
  T.Name := 'DBText1';
  CheckEq(Cap(T), 'DBText1', 'shows its name');
  T.DataSource := S;
  T.DataField := 'name';
  DS.Open;
  CheckEq(Cap(T), 'DBText1', 'shows its name also with an open dataset');
  T.Name := 'NameText';
  CheckEq(Cap(T), 'NameText', 'follows a new name');
  DS.Close;
  CheckEq(Cap(T), 'NameText', 'and after Close');
  Root.Free;
  S.Free;
  DS.Free;
end;

{ everything owned by one form and destroyed with it }
procedure TestOwnedByForm;
var
  F: TForm;
  DS: TTestDataSet;
  S: TDataSource;
  T: TDBText;
begin
  Section('destroyed with the form');
  F := TForm.CreateNew(nil);
  DS := NewDS(F);
  S := TDataSource.Create(F);
  S.DataSet := DS;
  T := TDBText.Create(F);
  T.Parent := F;
  T.DataSource := S;
  T.DataField := 'name';
  DS.Open;
  CheckEq(Cap(T), 'n1', 'linked');
  F.Free;
  Check(True, 'the form is destroyed with an open dataset');
  F := TForm.CreateNew(nil);
  T := TDBText.Create(F);
  T.Parent := F;
  S := TDataSource.Create(F);
  DS := NewDS(F);
  S.DataSet := DS;
  T.DataSource := S;
  T.DataField := 'id';
  DS.Open;
  CheckEq(Cap(T), '1', 'linked, control created first');
  F.Free;
  Check(True, 'the form is destroyed, control first');
end;

var
  F: TForm;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  try
    TestDefaults(F);
    TestDisplay(F);
    TestAlignment(F);
    TestSourceLifetime(F);
  finally
    F.Free;
  end;
  TestDesignTime;
  TestOwnedByForm;
  WriteLn('failed checks: ', FailedChecks);
end.
