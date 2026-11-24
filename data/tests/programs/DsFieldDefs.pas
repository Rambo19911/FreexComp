// covers: DAT-030, DAT-040, DAT-041, DAT-042, DAT-043, DAT-044
program DsFieldDefs;
{ Field definitions, the field list, field defaults, and field creation at open: dynamic fields, persistent
  fields, mismatch errors, name rules. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

type
  TCalls = class
    Defs: TFieldDefs;
    DS: TTestDataSet;
    Field: TField;
    procedure AddDuplicateDef;
    procedure AddDefToDataSet;
    procedure ClearDataSetDefs;
    procedure FieldByNameMissing;
    procedure FieldOutOfRange;
    procedure AttachField;
    procedure DetachField;
    procedure RenameToDuplicate;
    procedure RenameField;
    procedure ChangeKind;
    procedure DoOpen;
  end;

procedure TCalls.AddDuplicateDef;
begin
  Defs.Add('NAME', ftInteger);
end;

procedure TCalls.AddDefToDataSet;
begin
  DS.FieldDefs.Add('extra', ftInteger);
end;

procedure TCalls.ClearDataSetDefs;
begin
  DS.FieldDefs.Clear;
end;

procedure TCalls.FieldByNameMissing;
begin
  DS.FieldByName('nope');
end;

procedure TCalls.FieldOutOfRange;
begin
  WriteLn(DS.Fields[DS.Fields.Count].FieldName);
end;

procedure TCalls.AttachField;
begin
  Field.DataSet := DS;
end;

procedure TCalls.DetachField;
begin
  Field.DataSet := nil;
end;

procedure TCalls.RenameToDuplicate;
begin
  Field.FieldName := 'ID';
end;

procedure TCalls.RenameField;
begin
  Field.FieldName := 'other';
end;

procedure TCalls.ChangeKind;
begin
  Field.FieldKind := fkCalculated;
end;

procedure TCalls.DoOpen;
begin
  DS.Open;
end;

var
  Calls: TCalls;

function AlignName(A: TAlignment): string;
begin
  case A of
    taLeftJustify: Result := 'left';
    taRightJustify: Result := 'right';
    taCenter: Result := 'centre';
  else
    Result := '?';
  end;
end;

function Names(DS: TDataSet): string;
var
  List: TStringList;
  I: Integer;
begin
  List := TStringList.Create;
  DS.GetFieldNames(List);
  Result := '';
  for I := 0 to List.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + List[I];
  end;
  List.Free;
end;

procedure TestDefs;
var
  Defs: TFieldDefs;
  D: TFieldDef;
begin
  Section('defs');
  Defs := TFieldDefs.Create(nil);
  Calls.Defs := Defs;
  CheckEqInt(Defs.Count, 0, 'a new list is empty');
  D := Defs.Add('id', ftAutoInc);
  Check((D.Name = 'id') and (D.DataType = ftAutoInc) and (D.Size = 0) and not D.Required,
    'Add: Size 0 and Required False by default');
  D := Defs.Add('Name', ftString, 50, True);
  Check((D.Name = 'Name') and (D.DataType = ftString) and (D.Size = 50) and D.Required, 'Add with Size and Required');
  Defs.Add('Größe', ftFloat);
  CheckEqInt(Defs.Count, 3, 'Count');
  CheckEq(Defs[1].Name, 'Name', 'Items is the default property');
  CheckEq(Defs.Items[2].Name, 'Größe', 'Items in order of Add');
  CheckEqInt(Defs.IndexOf('name'), 1, 'IndexOf ignores case');
  CheckEqInt(Defs.IndexOf('GRÖSSE'), -1, 'IndexOf compares code unit by code unit');
  CheckEqInt(Defs.IndexOf('gröSSe'), -1, 'IndexOf does not fold sharp s');
  CheckEqInt(Defs.IndexOf('GRÖßE'), 2, 'IndexOf upper-cases beyond ASCII');
  CheckEqInt(Defs.IndexOf('missing'), -1, 'IndexOf of a missing name');
  Check(Defs.Find('ID') = Defs[0], 'Find ignores case');
  Check(Defs.Find('missing') = nil, 'Find of a missing name is nil');
  CheckRaises(Calls.AddDuplicateDef, 'EDatabaseError', 'Duplicate field name ''NAME''', 'duplicate definition');
  CheckEqInt(Defs.Count, 3, 'a rejected definition is not added');
  Check(Defs.DataSet = nil, 'a free-standing list has no dataset');
  Defs.Update;
  CheckEqInt(Defs.Count, 3, 'Update without a dataset does nothing');
  Defs.Clear;
  CheckEqInt(Defs.Count, 0, 'Clear');
  Defs.Free;
end;

procedure ShowDefaults(F: TField; const What: string);
begin
  WriteLn(What, ': ', F.ClassName, ' ', FieldTypeName(F.DataType), ' width=', F.DisplayWidth, ' align=',
    AlignName(F.Alignment), ' readonly=', BoolStr(F.ReadOnly));
end;

procedure TestFieldDefaults;
var
  F: TField;
  S: TStringField;
begin
  Section('fields: defaults');
  S := TStringField.Create(nil);
  S.FieldName := 'city';
  CheckEq(S.DisplayLabel, 'city', 'DisplayLabel defaults to FieldName');
  S.DisplayLabel := 'City';
  CheckEq(S.DisplayLabel, 'City', 'DisplayLabel when set');
  S.DisplayLabel := '';
  CheckEq(S.DisplayLabel, 'city', 'an empty DisplayLabel falls back to FieldName');
  Check(S.Visible and not S.ReadOnly and not S.Required and (S.Size = 0) and (S.FieldKind = fkData),
    'Visible, ReadOnly, Required, Size, FieldKind defaults');
  Check((S.DataSet = nil) and (S.Index = -1) and (S.FieldNo = 0), 'a free-standing field has no dataset, index or number');
  ShowDefaults(S, 'string size 0');
  S.Size := 1;
  ShowDefaults(S, 'string size 1');
  S.Size := 50;
  ShowDefaults(S, 'string size 50');
  S.Size := 51;
  ShowDefaults(S, 'string size 51');
  S.DisplayWidth := 7;
  ShowDefaults(S, 'string width set');
  S.Alignment := taCenter;
  ShowDefaults(S, 'string alignment set');
  S.Free;
  F := TIntegerField.Create(nil);
  ShowDefaults(F, 'integer');
  F.Free;
  F := TAutoIncField.Create(nil);
  ShowDefaults(F, 'autoinc');
  F.Free;
  F := TLargeintField.Create(nil);
  ShowDefaults(F, 'largeint');
  F.Free;
  F := TFloatField.Create(nil);
  ShowDefaults(F, 'float');
  F.Free;
  F := TBooleanField.Create(nil);
  ShowDefaults(F, 'boolean');
  F.Free;
  F := TDateField.Create(nil);
  ShowDefaults(F, 'date');
  F.Free;
  F := TTimeField.Create(nil);
  ShowDefaults(F, 'time');
  F.Free;
  F := TDateTimeField.Create(nil);
  ShowDefaults(F, 'datetime');
  F.Free;
  F := TMemoField.Create(nil);
  ShowDefaults(F, 'memo');
  F.Free;
  F := TBlobField.Create(nil);
  ShowDefaults(F, 'blob');
  Check((F is TField) and not (F is TMemoField), 'TBlobField');
  F.Free;
  F := TAutoIncField.Create(nil);
  Check((F is TIntegerField) and (F is TNumericField), 'TAutoIncField is an integer field');
  F.Free;
  F := TMemoField.Create(nil);
  Check(F is TBlobField, 'TMemoField is a blob field');
  F.Free;
  F := TDateField.Create(nil);
  Check(F is TDateTimeField, 'TDateField is a date-time field');
  F.Free;
end;

procedure TestFieldList;
var
  DS: TTestDataSet;
  A, B, C, D: TField;
begin
  Section('fields: list');
  DS := TTestDataSet.Create(nil);
  Calls.DS := DS;
  CheckEqInt(DS.FieldCount, 0, 'a new dataset has no fields');
  A := TIntegerField.Create(DS);
  A.FieldName := 'id';
  A.DataSet := DS;
  B := TStringField.Create(DS);
  B.FieldName := 'Name';
  B.DataSet := DS;
  C := TFloatField.Create(nil);
  C.FieldName := 'amount';
  C.DataSet := DS;
  CheckEqInt(DS.FieldCount, 3, 'FieldCount');
  CheckEqInt(DS.Fields.Count, 3, 'Fields.Count');
  Check(DS.Fields.DataSet = DS, 'Fields.DataSet');
  Check((DS.Fields[0] = A) and (DS.Fields[1] = B) and (DS.Fields[2] = C), 'creation order');
  Check((A.Index = 0) and (B.Index = 1) and (C.Index = 2), 'Index is the position');
  CheckEqInt(DS.Fields.IndexOf(C), 2, 'Fields.IndexOf');
  Check(DS.FieldByName('NAME') = B, 'FieldByName ignores case');
  Check(DS.Fields.FieldByName('name') = B, 'Fields.FieldByName');
  Check(DS.FindField('Amount') = C, 'FindField ignores case');
  Check(DS.Fields.FindField('amount') = C, 'Fields.FindField');
  Check(DS.FindField('nope') = nil, 'FindField of a missing name is nil');
  CheckRaises(Calls.FieldByNameMissing, 'EDatabaseError', 'Field ''nope'' not found', 'FieldByName of a missing name');
  CheckRaises(Calls.FieldOutOfRange, 'EListError', '', 'Fields[i] out of range');
  CheckEq(Names(DS), 'id,Name,amount', 'GetFieldNames');

  Section('fields: name rules');
  D := TStringField.Create(nil);
  D.FieldName := 'NAME';
  Calls.Field := D;
  CheckRaises(Calls.AttachField, 'EDatabaseError', 'Duplicate field name ''NAME''', 'attaching a duplicate name');
  Check((D.DataSet = nil) and (DS.FieldCount = 3), 'the rejected field is not attached');
  D.FieldName := 'note';
  D.DataSet := DS;
  CheckEqInt(D.Index, 3, 'attached after renaming');
  CheckRaises(Calls.RenameToDuplicate, 'EDatabaseError', 'Duplicate field name ''ID''', 'renaming to a duplicate name');
  CheckEq(D.FieldName, 'note', 'the name is unchanged after the error');
  D.FieldName := 'NOTE';
  CheckEq(D.FieldName, 'NOTE', 'changing only the case of the own name is allowed');

  Section('fields: order');
  ClearLog;
  D.Index := 0;
  CheckEq(Names(DS), 'NOTE,id,Name,amount', 'Index := 0 moves the field to the front');
  CheckEq(TakeLog, '[deLayoutChange dsInactive]', 'reordering notifies a layout change');
  D.Index := 0;
  CheckEq(TakeLog, '', 'assigning the same index does nothing');
  D.Index := 3;
  CheckEq(Names(DS), 'id,Name,amount,NOTE', 'Index := 3 moves it to the end');
  ClearLog;

  Section('fields: detaching');
  D.DataSet := nil;
  Check((DS.FieldCount = 3) and (D.DataSet = nil) and (D.Index = -1), 'DataSet := nil detaches');
  D.Free;
  C.Free;
  CheckEq(Names(DS), 'id,Name', 'a destroyed field leaves the list');
  DS.Free;
end;

procedure ShowFields(DS: TDataSet);
var
  I: Integer;
  F: TField;
begin
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    WriteLn(I, ' ', F.FieldName, ': ', F.ClassName, ' ', FieldTypeName(F.DataType), ' size=', F.Size, ' required=',
      BoolStr(F.Required), ' no=', F.FieldNo, ' readonly=', BoolStr(F.ReadOnly), ' name=''', F.Name, ''' owned=',
      BoolStr(F.Owner = DS));
  end;
end;

procedure TestDynamic;
var
  DS: TTestDataSet;
begin
  Section('dynamic fields');
  DS := TTestDataSet.Create(nil);
  Calls.DS := DS;
  DS.AddDef('id', ftAutoInc);
  DS.AddDef('name', ftString, 50, True);
  DS.AddDef('small', ftSmallint);
  DS.AddDef('int', ftInteger);
  DS.AddDef('word', ftWord);
  DS.AddDef('big', ftLargeint);
  DS.AddDef('amount', ftFloat);
  DS.AddDef('price', ftCurrency);
  DS.AddDef('flag', ftBoolean);
  DS.AddDef('born', ftDate);
  DS.AddDef('at', ftTime);
  DS.AddDef('stamp', ftDateTime);
  DS.AddDef('note', ftMemo);
  DS.AddDef('data', ftBlob);
  CheckEqInt(DS.FieldDefs.Count, 0, 'no definitions before the driver was asked');
  DS.FieldDefs.Update;
  CheckEqInt(DS.FieldDefs.Count, 14, 'FieldDefs.Update asks the driver on a closed dataset');
  CheckEqInt(DS.FieldCount, 0, 'Update creates no fields');
  DS.Open;
  CheckEqInt(DS.FieldCount, 14, 'one field per definition');
  ShowFields(DS);
  Check(DS.FieldByName('NAME').Index = 1, 'FieldByName on dynamic fields');
  CheckRaises(Calls.AddDefToDataSet, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'FieldDefs.Add on an open dataset');
  CheckRaises(Calls.ClearDataSetDefs, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'FieldDefs.Clear on an open dataset');
  DS.FieldDefs.Update;
  CheckEqInt(DS.FieldDefs.Count, 14, 'FieldDefs.Update on an open dataset changes nothing');
  Calls.Field := TStringField.Create(nil);
  Calls.Field.FieldName := 'late';
  CheckRaises(Calls.AttachField, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'attaching a field to an open dataset');
  Calls.Field.Free;
  Calls.Field := DS.Fields[1];
  CheckRaises(Calls.DetachField, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'detaching a field from an open dataset');
  CheckRaises(Calls.RenameField, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'renaming a field of an open dataset');
  CheckRaises(Calls.ChangeKind, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'changing the kind of a field of an open dataset');
  DS.Close;
  CheckEqInt(DS.FieldCount, 0, 'dynamic fields are destroyed at Close');
  CheckEqInt(DS.ComponentCount, 0, 'and no longer owned by the dataset');
  DS.Open;
  CheckEqInt(DS.FieldCount, 14, 'and created again at the next Open');
  DS.Close;

  Section('dynamic fields: unsupported type');
  DS.ClearDefs;
  DS.AddDef('id', ftInteger);
  DS.AddDef('u', ftUnknown);
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Field ''u'' has an unsupported type', 'ftUnknown in a definition');
  CheckEq(StateName(DS.State), 'dsInactive', 'the dataset stays inactive');
  CheckEqInt(DS.FieldCount, 0, 'no fields remain');
  DS.Free;
end;

procedure TestPersistent;
var
  DS: TTestDataSet;
  FName, FId, FCalc, FNote: TField;
begin
  Section('persistent fields');
  DS := TTestDataSet.Create(nil);
  Calls.DS := DS;
  DS.AddDef('id', ftAutoInc);
  DS.AddDef('name', ftString, 50, True);
  DS.AddDef('amount', ftFloat);
  DS.AddDef('note', ftMemo);
  FName := TStringField.Create(DS);
  FName.FieldName := 'NAME';
  FName.DataSet := DS;
  FCalc := TIntegerField.Create(DS);
  FCalc.FieldName := 'twice';
  FCalc.FieldKind := fkCalculated;
  FCalc.DataSet := DS;
  FId := TIntegerField.Create(DS);
  FId.FieldName := 'id';
  FId.DataSet := DS;
  DS.Open;
  CheckEqInt(DS.FieldCount, 3, 'only the persistent fields exist');
  ShowFields(DS);
  Check(DS.FindField('amount') = nil, 'a column without a persistent field is not accessible');
  CheckEqInt(FName.FieldNo, 2, 'FieldNo is the position in FieldDefs');
  CheckEqInt(FId.FieldNo, 1, 'an integer field binds to an auto-increment column');
  CheckEqInt(FCalc.FieldNo, -1, 'a calculated field has FieldNo -1');
  DS.Close;
  CheckEqInt(DS.FieldCount, 3, 'persistent fields are kept across Close');
  Check((DS.Fields[0] = FName) and (DS.Fields[1] = FCalc) and (DS.Fields[2] = FId), 'in creation order');
  CheckEqInt(FName.FieldNo, 0, 'a closed dataset leaves its fields unbound');
  DS.Open;
  CheckEqInt(DS.FieldCount, 3, 'and across the next Open');
  DS.Close;

  Section('persistent fields: mismatch');
  FName.FieldName := 'zzz';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Field ''zzz'' not found', 'no definition with that name');
  CheckEq(StateName(DS.State), 'dsInactive', 'the dataset stays inactive');
  CheckEqInt(DS.FieldCount, 3, 'the persistent fields are kept after the failed open');
  FName.FieldName := 'amount';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Type mismatch for field ''amount''', 'string field on a float column');
  FName.FieldName := 'note';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Type mismatch for field ''note''', 'string field on a memo column');
  FId.FieldName := 'name';
  FName.FieldName := 'id';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'Type mismatch for field ''id''', 'string field on an integer column');
  FName.FieldName := 'x';
  FId.FieldName := 'id';
  FName.FieldName := 'name';
  FNote := TMemoField.Create(DS);
  FNote.FieldName := 'note';
  FNote.DataSet := DS;
  DS.Open;
  CheckEqInt(FNote.FieldNo, 4, 'a memo field binds to a memo column');
  DS.Close;
  FNote.Free;
  FCalc.Free;
  FId.Free;
  FName.Free;
  CheckEqInt(DS.FieldCount, 0, 'all persistent fields destroyed');
  DS.Open;
  CheckEqInt(DS.FieldCount, 4, 'without persistent fields the dataset creates dynamic ones again');
  DS.Close;

  Section('persistent fields: compatible integer types');
  DS.ClearDefs;
  DS.AddDef('a', ftSmallint);
  DS.AddDef('b', ftWord);
  DS.AddDef('c', ftInteger);
  DS.AddDef('d', ftCurrency);
  FId := TAutoIncField.Create(DS);
  FId.FieldName := 'a';
  FId.DataSet := DS;
  FName := TIntegerField.Create(DS);
  FName.FieldName := 'b';
  FName.DataSet := DS;
  FCalc := TAutoIncField.Create(DS);
  FCalc.FieldName := 'c';
  FCalc.DataSet := DS;
  FNote := TFloatField.Create(DS);
  FNote.FieldName := 'd';
  FNote.DataSet := DS;
  DS.Open;
  Check((FId.FieldNo = 1) and (FName.FieldNo = 2) and (FCalc.FieldNo = 3) and (FNote.FieldNo = 4),
    'integer field classes bind to every integer type, a float field to a currency column');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  TestDefs;
  TestFieldDefaults;
  TestFieldList;
  TestDynamic;
  TestPersistent;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
