// covers: DAT-002, DAT-005, DAT-006, DAT-010, DAT-023, DAT-031, DAT-032, DAT-033
program DsFields;
{ Field value access. Read section: the As... getters of every field class, the null rules and the
  conversion, access and range errors. Write section: the As... setters, Clear, the size, read-only and
  state errors and the assignment sequence with OnValidate and OnChange. Text section: Text and DisplayText,
  OnGetText and OnSetText, DisplayFormat, Precision and DisplayValues. Clear section: ClearFields. Blob
  section: BlobSize, SaveToStream and LoadFromStream of blob and memo fields. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  GetterNames: array[0..6] of string = ('AsString', 'AsInteger', 'AsLargeInt', 'AsFloat', 'AsBoolean',
    'AsDateTime', 'AsBytes');

type
  TCalls = class
    Field: TField;
    procedure ReadString;
    procedure ReadIsNull;
    procedure WriteX;
    procedure WriteAbc;
    procedure WriteLong;
    procedure Write123456;
    procedure WriteFive;
    procedure DoClear;
    procedure TextX;
    procedure TextEmpty;
    { field events }
    procedure Validate(Sender: TField);
    procedure Change(Sender: TField);
    procedure GetText(Sender: TField; var Text: string; DisplayText: Boolean);
    procedure SetText(Sender: TField; const Text: string);
    procedure SetTextIgnore(Sender: TField; const Text: string);
    procedure NewRecord(DataSet: TDataSet);
  end;

procedure TCalls.ReadString;
begin
  WriteLn(Field.AsString);
end;

procedure TCalls.ReadIsNull;
begin
  WriteLn(Field.IsNull);
end;

procedure TCalls.WriteX;
begin
  Field.AsString := 'x';
end;

procedure TCalls.WriteAbc;
begin
  Field.AsString := 'abc';
end;

procedure TCalls.WriteLong;
begin
  Field.AsString := 'abcdef';
end;

procedure TCalls.Write123456;
begin
  Field.AsInteger := 123456;
end;

procedure TCalls.WriteFive;
begin
  Field.AsInteger := 5;
end;

procedure TCalls.DoClear;
begin
  Field.Clear;
end;

procedure TCalls.TextX;
begin
  Field.Text := 'x';
end;

procedure TCalls.TextEmpty;
begin
  Field.Text := '';
end;

{ a value 'bad' is rejected }
procedure TCalls.Validate(Sender: TField);
begin
  AddLog('OnValidate(' + Sender.FieldName + '=' + Sender.AsString + ')');
  if Sender.AsString = 'bad' then
    raise Exception.Create('rejected');
end;

procedure TCalls.Change(Sender: TField);
begin
  AddLog('OnChange(' + Sender.FieldName + '=' + Sender.AsString + ')');
end;

procedure TCalls.GetText(Sender: TField; var Text: string; DisplayText: Boolean);
begin
  if Sender.IsNull then
    Text := '<null|' + BoolStr(DisplayText) + '>'
  else
    Text := '<' + Sender.AsString + '|' + BoolStr(DisplayText) + '>';
end;

procedure TCalls.SetText(Sender: TField; const Text: string);
begin
  AddLog('OnSetText(' + Sender.FieldName + ',' + Text + ')');
  Sender.AsString := UpperCase(Text);
end;

procedure TCalls.SetTextIgnore(Sender: TField; const Text: string);
begin
  AddLog('OnSetText(' + Sender.FieldName + ',' + Text + ')');
end;

procedure TCalls.NewRecord(DataSet: TDataSet);
begin
  DataSet.FieldByName('a').AsInteger := 77;
  DataSet.FieldByName('ro').AsString := 'preset';
end;

var
  Calls: TCalls;

function Hex(const B: TBytes): string;
const
  Digits = '0123456789ABCDEF';
var
  I: Integer;
begin
  if B = nil then
  begin
    Result := 'nil';
    Exit;
  end;
  Result := '';
  for I := 0 to High(B) do
    Result := Result + Digits[B[I] shr 4 + 1] + Digits[B[I] and 15 + 1];
end;

{ the value of one getter as text, or 'E: <message>' for an EDatabaseError }
function Get(F: TField; Which: Integer): string;
begin
  try
    case Which of
      0: Result := '''' + F.AsString + '''';
      1: Result := IntToStr(F.AsInteger);
      2: Result := IntToStr(F.AsLargeInt);
      3: Result := FloatToStr(F.AsFloat);
      4: Result := BoolStr(F.AsBoolean);
      5: Result := FloatToStr(F.AsDateTime);
      6: Result := Hex(F.AsBytes);
    else
      Result := '?';
    end;
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

procedure ShowRow(DS: TDataSet);
var
  I, G: Integer;
  F: TField;
begin
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    WriteLn(F.FieldName, ' (', F.ClassName, ') null=', BoolStr(F.IsNull));
    for G := 0 to 6 do
      WriteLn('  ', GetterNames[G], ' = ', Get(F, G));
  end;
end;

function Bytes3: TBytes;
begin
  SetLength(Result, 3);
  Result[0] := 1;
  Result[1] := 2;
  Result[2] := 255;
end;

procedure TestMatrix;
var
  DS: TTestDataSet;
begin
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s', ftString, 20);
  DS.AddDef('m', ftMemo);
  DS.AddDef('i', ftInteger);
  DS.AddDef('a', ftAutoInc);
  DS.AddDef('l', ftLargeint);
  DS.AddDef('f', ftFloat);
  DS.AddDef('c', ftCurrency);
  DS.AddDef('b', ftBoolean);
  DS.AddDef('dt', ftDateTime);
  DS.AddDef('d', ftDate);
  DS.AddDef('t', ftTime);
  DS.AddDef('bl', ftBlob);
  DS.AddRow([StringValue('42'), StringValue('memo text'), IntValue(7), IntValue(1), IntValue(9000000000), FloatValue(2.5),
    FloatValue(-0.125), IntValue(1), FloatValue(45000.75), FloatValue(45000.75), FloatValue(45000.25),
    BytesValue(Bytes3)]);
  DS.AddRow([NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue,
    NullValue, NullValue, NullValue]);
  DS.AddRow([StringValue(''), StringValue(''), IntValue(0), IntValue(-3), IntValue(-2147483648), FloatValue(0),
    FloatValue(1e20), IntValue(0), FloatValue(0), FloatValue(-1.75), FloatValue(-1.75), BytesValue(nil)]);
  DS.Open;
  Section('read: values');
  ShowRow(DS);
  Section('read: null row');
  DS.Next;
  ShowRow(DS);
  Section('read: zero and negative values');
  DS.Next;
  ShowRow(DS);
  DS.Free;
end;

procedure TestStrings;
var
  DS: TTestDataSet;
  S, M: TField;

  procedure Row(const Text: string);
  begin
    DS.AddRow([StringValue(Text), StringValue(Text)]);
  end;

  procedure ShowText;
  var
    G: Integer;
  begin
    WriteLn('''', S.AsString, '''');
    for G := 1 to 6 do
    begin
      WriteLn('  ', GetterNames[G], ' = ', Get(S, G));
      if StringReplace(Get(M, G), 'field ''m''', 'field ''s''', []) <> Get(S, G) then
        WriteLn('  FAIL memo differs: ', Get(M, G));
    end;
  end;

begin
  Section('read: string and memo conversions');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s', ftString, 40);
  DS.AddDef('m', ftMemo);
  Row('True');
  Row('false');
  Row('TRUE');
  Row('1');
  Row('0');
  Row('yes');
  Row('2');
  Row('-17');
  Row('9000000000');
  Row('abc');
  Row('1e3');
  Row('2023-03-15 18:00:00');
  Row('2023-03-15');
  Row('é€');
  DS.Open;
  S := DS.FieldByName('s');
  M := DS.FieldByName('m');
  while not DS.Eof do
  begin
    ShowText;
    DS.Next;
  end;
  DS.Free;
end;

procedure TestRange;
var
  DS: TTestDataSet;
  L: TField;
begin
  Section('read: largeint range');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('l', ftLargeint);
  DS.AddRow([IntValue(2147483647)]);
  DS.AddRow([IntValue(2147483648)]);
  DS.AddRow([IntValue(-2147483648)]);
  DS.AddRow([IntValue(-2147483649)]);
  DS.Open;
  L := DS.Fields[0];
  while not DS.Eof do
  begin
    WriteLn(L.AsLargeInt, ': AsInteger = ', Get(L, 1));
    DS.Next;
  end;
  DS.Free;
end;

procedure TestCursorAndState;
var
  DS: TTestDataSet;
  F, P: TField;
begin
  Section('read: cursor');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10);
  DS.AddRow([IntValue(10), StringValue('ten')]);
  DS.AddRow([IntValue(20), NullValue]);
  DS.AddRow([IntValue(30), StringValue('thirty')]);
  DS.Open;
  F := DS.FieldByName('name');
  CheckEq(F.AsString, 'ten', 'a field reads the current row');
  DS.Next;
  Check(F.IsNull, 'IsNull on the second row');
  CheckEq(F.AsString, '', 'a null string reads as empty');
  DS.Last;
  CheckEq(F.AsString, 'thirty', 'after Last');
  CheckEqInt(DS.FieldByName('id').AsInteger, 30, 'another field of the same row');
  DS.RecNo := 1;
  CheckEqInt(DS.Fields[0].AsInteger, 10, 'after RecNo := 1');
  DS.Close;

  Section('read: empty dataset');
  DS.ClearRows;
  DS.Open;
  F := DS.FieldByName('id');
  Check(F.IsNull, 'a field of an empty dataset is null');
  CheckEqInt(F.AsInteger, 0, 'and reads as 0');
  CheckEq(DS.FieldByName('name').AsString, '', 'or as an empty string');
  DS.Close;

  Section('read: closed dataset and persistent fields');
  DS.AddRow([IntValue(10), StringValue('ten')]);
  P := TStringField.Create(DS);
  P.FieldName := 'name';
  P.DataSet := DS;
  Calls.Field := P;
  CheckRaises(Calls.ReadString, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'reading a field of a closed dataset');
  CheckRaises(Calls.ReadIsNull, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'IsNull on a closed dataset');
  DS.Open;
  CheckEq(P.AsString, 'ten', 'a persistent field reads its column');
  CheckEqInt(DS.FieldCount, 1, 'only the persistent field exists');
  DS.Close;
  P.DataSet := nil;
  CheckRaises(Calls.ReadString, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'reading a field without a dataset');
  P.Free;
  DS.Free;
end;

{ ---- write section ---- }

const
  SetterNames: array[0..6] of string = ('AsString', 'AsInteger', 'AsLargeInt', 'AsFloat', 'AsBoolean',
    'AsDateTime', 'AsBytes');
  SetterSamples: array[0..6] of string = ('', '42', '9000000000', '2.5', 'True', '45000.75', 'bytes 41 42');

function BytesAB: TBytes;
begin
  SetLength(Result, 2);
  Result[0] := 65;
  Result[1] := 66;
end;

{ the definitions of the conversion matrix: one field of every class }
procedure AddAllDefs(DS: TTestDataSet);
begin
  DS.AddDef('s', ftString, 20);
  DS.AddDef('m', ftMemo);
  DS.AddDef('i', ftInteger);
  DS.AddDef('a', ftAutoInc);
  DS.AddDef('l', ftLargeint);
  DS.AddDef('f', ftFloat);
  DS.AddDef('c', ftCurrency);
  DS.AddDef('b', ftBoolean);
  DS.AddDef('dt', ftDateTime);
  DS.AddDef('d', ftDate);
  DS.AddDef('t', ftTime);
  DS.AddDef('bl', ftBlob);
end;

{ writes through one setter and reports what the field then holds, or 'E: <message>' }
function Put(DS: TTestDataSet; F: TField; Which: Integer; const Text: string): string;
begin
  try
    case Which of
      0: F.AsString := Text;
      1: F.AsInteger := 42;
      2: F.AsLargeInt := 9000000000;
      3: F.AsFloat := 2.5;
      4: F.AsBoolean := True;
      5: F.AsDateTime := 45000.75;
      6: F.AsBytes := BytesAB;
    end;
    Result := DS.Probe(F);
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

function PutString(DS: TTestDataSet; F: TField; const Text: string): string;
begin
  Result := Put(DS, F, 0, Text);
end;

function PutFloat(DS: TTestDataSet; F: TField; Value: Double): string;
begin
  try
    F.AsFloat := Value;
    Result := DS.Probe(F);
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

function PutInt(DS: TTestDataSet; F: TField; Value: Int64): string;
begin
  try
    F.AsLargeInt := Value;
    Result := DS.Probe(F);
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

function PutDateTime(DS: TTestDataSet; F: TField; Value: TDateTime): string;
begin
  try
    F.AsDateTime := Value;
    Result := DS.Probe(F);
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

function PutText(DS: TTestDataSet; F: TField; const Text: string): string;
begin
  try
    F.Text := Text;
    Result := DS.Probe(F);
  except
    on E: EDatabaseError do
      Result := 'E: ' + E.Message;
  end;
end;

procedure TestWriteMatrix;
const
  Texts: array[0..11] of string = ('text', 'text', '12', '12', '9000000000', '1.5', '1.5', 'true',
    '2023-03-15 18:00:00', '2023-03-15', '06:00:00', 'x');
var
  DS: TTestDataSet;
  I, W: Integer;
  F: TField;
begin
  Section('write: every setter on every field class');
  DS := TTestDataSet.Create(nil);
  AddAllDefs(DS);
  DS.Open;
  DS.FieldByName('a').ReadOnly := False;
  DS.Append;
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    WriteLn(F.FieldName, ' (', F.ClassName, ')');
    WriteLn('  AsString := ''', Texts[I], ''' -> ', Put(DS, F, 0, Texts[I]));
    for W := 1 to 6 do
    begin
      F.Clear;
      WriteLn('  ', SetterNames[W], ' := ', SetterSamples[W], ' -> ', Put(DS, F, W, ''));
    end;
  end;
  DS.Cancel;

  Section('write: conversions');
  DS.Append;
  F := DS.FieldByName('i');
  CheckEq(PutString(DS, F, 'abc'), 'E: ''abc'' is not a valid integer value for field ''i''', 'integer: text that is no number');
  CheckEq(PutString(DS, F, '9000000000'), 'E: ''9000000000'' is not a valid integer value for field ''i''',
    'integer: text out of range');
  CheckEq(PutString(DS, F, '-5'), 'vkInt -5', 'integer: negative text');
  CheckEq(PutFloat(DS, F, 3.0), 'vkInt 3', 'integer: an integral float');
  CheckEq(PutFloat(DS, F, 1e10), 'E: Value of field ''i'' is out of range', 'integer: a float out of range');
  CheckEq(PutInt(DS, F, -2147483648), 'vkInt -2147483648', 'integer: the lowest value as LargeInt');
  CheckEq(PutInt(DS, F, 2147483648), 'E: Value of field ''i'' is out of range', 'integer: one above the highest value');
  CheckEq(DS.Probe(F), 'vkInt -2147483648', 'a failed write leaves the value');
  F := DS.FieldByName('l');
  CheckEq(PutString(DS, F, 'x'), 'E: ''x'' is not a valid integer value for field ''l''', 'largeint: text that is no number');
  CheckEq(PutFloat(DS, F, -4.0), 'vkInt -4', 'largeint: an integral float');
  CheckEq(PutFloat(DS, F, 0.5), 'E: Value of field ''l'' is out of range', 'largeint: a fraction');
  CheckEq(PutFloat(DS, F, 1e19), 'E: Value of field ''l'' is out of range', 'largeint: a float out of range');
  F := DS.FieldByName('f');
  CheckEq(PutString(DS, F, 'abc'), 'E: ''abc'' is not a valid floating-point value for field ''f''', 'float: text that is no number');
  CheckEq(PutString(DS, F, '-1e3'), 'vkFloat -1000', 'float: exponent text');
  F := DS.FieldByName('b');
  CheckEq(PutString(DS, F, 'FALSE'), 'vkInt 0', 'boolean: FALSE');
  CheckEq(PutString(DS, F, '1'), 'vkInt 1', 'boolean: 1');
  CheckEq(PutString(DS, F, 'yes'), 'E: ''yes'' is not a valid boolean value for field ''b''', 'boolean: other text');
  CheckEq(PutInt(DS, F, 0), 'vkInt 0', 'boolean: 0');
  CheckEq(PutInt(DS, F, -7), 'vkInt 1', 'boolean: any other number is True');
  F := DS.FieldByName('dt');
  CheckEq(PutString(DS, F, 'abc'), 'E: ''abc'' is not a valid date-time value for field ''dt''', 'datetime: text that is no date');
  CheckEq(PutFloat(DS, F, 45000.75), 'vkFloat 45000.75', 'datetime: a float');
  F := DS.FieldByName('d');
  CheckEq(PutString(DS, F, 'abc'), 'E: ''abc'' is not a valid date-time value for field ''d''', 'date: text that is no date');
  CheckEq(PutFloat(DS, F, 45000.75), 'vkFloat 45000', 'date: a float loses its time part');
  CheckEq(PutDateTime(DS, F, -1.75), 'vkFloat -1', 'date: a negative value is truncated towards zero');
  F := DS.FieldByName('t');
  CheckEq(PutString(DS, F, 'abc'), 'E: ''abc'' is not a valid date-time value for field ''t''', 'time: text that is no time');
  CheckEq(PutFloat(DS, F, 45000.75), 'vkFloat 0.75', 'time: a float loses its date part');
  CheckEq(PutDateTime(DS, F, -1.25), 'vkFloat 0.25', 'time: the time of day of a negative value');
  F := DS.FieldByName('s');
  CheckEq(PutFloat(DS, F, -0.5), 'vkString -0.5', 'string: a float is stored as text');
  CheckEq(PutString(DS, F, ''), 'vkString ', 'string: the empty string is a value');
  Check(not F.IsNull, 'not null');
  DS.Cancel;
  DS.Free;
end;

procedure TestWriteSize;
var
  DS: TTestDataSet;
  F: TField;
begin
  Section('write: size');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s5', ftString, 5);
  DS.AddDef('s0', ftString);
  DS.AddDef('m', ftMemo);
  DS.Open;
  DS.Append;
  F := DS.FieldByName('s5');
  Calls.Field := F;
  CheckEq(PutString(DS, F, 'abcde'), 'vkString abcde', 'a value of exactly Size characters');
  CheckRaises(Calls.WriteLong, 'EDatabaseError', 'Value too long for field ''s5'' (maximum 5 characters)', 'one character more');
  CheckRaises(Calls.Write123456, 'EDatabaseError', 'Value too long for field ''s5'' (maximum 5 characters)',
    'a converted value that is too long');
  CheckEq(F.AsString, 'abcde', 'the value is unchanged');
  CheckEq(PutString(DS, DS.FieldByName('s0'), 'abcdefghijklmnopqrstuvwxyz'), 'vkString abcdefghijklmnopqrstuvwxyz',
    'no limit when Size is 0');
  CheckEq(PutString(DS, DS.FieldByName('m'), 'abcdefghijklmnopqrstuvwxyz'), 'vkString abcdefghijklmnopqrstuvwxyz',
    'no limit for a memo');
  DS.Cancel;
  DS.Free;
end;

procedure TestWriteState;
var
  DS: TTestDataSet;
  F, P: TField;
begin
  Section('write: state');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('a', ftAutoInc);
  DS.AddDef('s', ftString, 10);
  DS.AddDef('ro', ftString, 10);
  DS.AddDef('fix', ftInteger);
  DS.AddRow([IntValue(1), StringValue('one'), StringValue('r'), IntValue(5)]);
  DS.FixedField := 'fix';
  DS.Open;
  F := DS.FieldByName('s');
  Calls.Field := F;
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Dataset not in edit or insert mode', 'a setter in dsBrowse');
  CheckRaises(Calls.DoClear, 'EDatabaseError', 'Dataset not in edit or insert mode', 'Clear in dsBrowse');
  CheckRaises(Calls.TextX, 'EDatabaseError', 'Dataset not in edit or insert mode', 'Text := in dsBrowse');
  CheckRaises(Calls.TextEmpty, 'EDatabaseError', 'Dataset not in edit or insert mode', 'Text := '''' in dsBrowse');
  CheckEq(F.AsString, 'one', 'the value is unchanged');

  Section('write: read-only fields');
  DS.FieldByName('ro').ReadOnly := True;
  CheckEqBool(F.CanModify, True, 'CanModify of a plain field');
  CheckEqBool(DS.FieldByName('ro').CanModify, False, 'CanModify of a read-only field');
  CheckEqBool(DS.FieldByName('a').ReadOnly, True, 'an auto-increment field is read-only by default');
  CheckEqBool(DS.FieldByName('a').CanModify, False, 'so it cannot be modified');
  CheckEqBool(DS.FieldByName('fix').CanModify, False, 'CanModify of a column the driver cannot update');
  DS.Edit;
  Calls.Field := DS.FieldByName('ro');
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Field ''ro'' cannot be modified', 'writing a read-only field');
  CheckRaises(Calls.DoClear, 'EDatabaseError', 'Field ''ro'' cannot be modified', 'clearing a read-only field');
  CheckRaises(Calls.TextX, 'EDatabaseError', 'Field ''ro'' cannot be modified', 'Text := on a read-only field');
  Calls.Field := DS.FieldByName('a');
  CheckRaises(Calls.WriteFive, 'EDatabaseError', 'Field ''a'' cannot be modified', 'writing the auto-increment field');
  CheckRaises(Calls.WriteAbc, 'EDatabaseError', 'Field ''a'' cannot be modified', 'the read-only check comes before the conversion');
  Calls.Field := DS.FieldByName('fix');
  CheckRaises(Calls.WriteFive, 'EDatabaseError', 'Field ''fix'' cannot be modified', 'writing a column the driver cannot update');
  CheckEqBool(DS.Modified, False, 'nothing was modified');
  DS.Cancel;
  Calls.Field := DS.FieldByName('ro');
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Dataset not in edit or insert mode', 'the state check comes first');
  DS.OnNewRecord := Calls.NewRecord;
  DS.Append;
  CheckEq(DS.Probe(DS.FieldByName('a')), 'vkInt 77', 'OnNewRecord may assign a read-only field');
  CheckEq(DS.Probe(DS.FieldByName('ro')), 'vkString preset', 'and another one');
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Field ''ro'' cannot be modified', 'after the event the field is read-only again');
  DS.Cancel;
  DS.DriverReadOnly := True;
  CheckEqBool(F.CanModify, False, 'no field of a read-only dataset can be modified');
  DS.Close;

  Section('write: closed dataset');
  P := TStringField.Create(DS);
  P.FieldName := 's';
  P.DataSet := DS;
  Calls.Field := P;
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'a setter on a closed dataset');
  CheckRaises(Calls.DoClear, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'Clear on a closed dataset');
  CheckEqBool(P.CanModify, False, 'CanModify on a closed dataset');
  P.DataSet := nil;
  CheckRaises(Calls.WriteX, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'a setter without a dataset');
  CheckEqBool(P.CanModify, False, 'CanModify without a dataset');
  P.Free;
  DS.Free;
end;

procedure TestWriteSequence;
var
  DS: TTestDataSet;
  S, I: TField;
begin
  Section('write: assignment sequence');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s', ftString, 10);
  DS.AddDef('i', ftInteger);
  DS.AddRow([StringValue('old'), IntValue(1)]);
  DS.Open;
  S := DS.FieldByName('s');
  I := DS.FieldByName('i');
  S.OnValidate := Calls.Validate;
  S.OnChange := Calls.Change;
  I.OnValidate := Calls.Validate;
  I.OnChange := Calls.Change;
  DS.Edit;
  ClearLog;
  S.AsString := 'new';
  CheckEq(TakeLog, 'OnValidate(s=new),OnChange(s=new),[deFieldChange dsEdit s]', 'OnValidate, OnChange, notification');
  CheckEqBool(DS.Modified, True, 'Modified');
  S.AsString := 'new';
  CheckEq(TakeLog, 'OnValidate(s=new),OnChange(s=new),[deFieldChange dsEdit s]', 'an equal value runs the whole sequence');
  Calls.Field := I;
  CheckRaises(Calls.WriteAbc, 'EDatabaseError', 'is not a valid integer value', 'a conversion error');
  CheckEq(TakeLog, '', 'happens before OnValidate');
  CheckEq(DS.Probe(I), 'vkInt 1', 'and leaves the value');
  S.Clear;
  CheckEq(TakeLog, 'OnValidate(s=),OnChange(s=),[deFieldChange dsEdit s]', 'Clear runs the sequence');
  Check(S.IsNull, 'and sets null');
  S.Text := 'txt';
  CheckEq(TakeLog, 'OnValidate(s=txt),OnChange(s=txt),[deFieldChange dsEdit s]', 'Text := runs the sequence');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[old|1] new=[txt|1] assigned=1', 'only the written field is flagged');

  Section('write: OnValidate rejects');
  DS.Edit;
  ClearLog;
  Calls.Field := S;
  try
    S.AsString := 'bad';
    Check(False, 'the exception of OnValidate propagates');
  except
    on E: Exception do
      CheckEq(E.ClassName + ': ' + E.Message, 'Exception: rejected', 'the exception of OnValidate propagates');
  end;
  CheckEq(TakeLog, 'OnValidate(s=bad)', 'neither OnChange nor a notification');
  CheckEq(DS.Probe(S), 'vkString txt', 'the previous value is restored');
  CheckEqBool(DS.Modified, False, 'Modified is restored');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[txt|1] new=[txt|1] assigned=', 'and so is the flag');
  DS.Edit;
  S.AsString := 'good';
  try
    S.AsString := 'bad';
  except
    on E: Exception do
      ;
  end;
  CheckEq(DS.Probe(S), 'vkString good', 'a rejected second write restores the first one');
  CheckEqBool(DS.Modified, True, 'still Modified');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[txt|1] new=[good|1] assigned=1', 'still flagged');
  DS.Free;
end;

{ ---- text section ---- }

procedure ShowTexts(DS: TDataSet);
var
  I: Integer;
  F: TField;
begin
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    WriteLn(F.FieldName, ': Text=''', F.Text, ''' DisplayText=''', F.DisplayText, '''');
  end;
end;

procedure TestText;
const
  Texts: array[0..11] of string = ('text', 'memo', '12', '13', '9000000001', '1.5', '-2.25', 'false',
    '2024-02-29 12:30:00', '2024-02-29', '12:30:00', 'x');
var
  DS: TTestDataSet;
  I: Integer;
  F: TField;
begin
  DS := TTestDataSet.Create(nil);
  AddAllDefs(DS);
  DS.AddRow([StringValue('42'), StringValue('memo text'), IntValue(7), IntValue(1), IntValue(9000000000), FloatValue(2.5),
    FloatValue(-0.125), IntValue(1), FloatValue(45000.75), FloatValue(45000.75), FloatValue(45000.25),
    BytesValue(Bytes3)]);
  DS.AddRow([NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue, NullValue,
    NullValue, NullValue, NullValue]);
  DS.AddRow([StringValue(''), StringValue(''), IntValue(0), IntValue(-3), IntValue(-1234567), FloatValue(3.14159265),
    FloatValue(1234.5), IntValue(0), FloatValue(0), FloatValue(2), FloatValue(0), BytesValue(nil)]);
  DS.Open;
  DS.FieldByName('a').ReadOnly := False;
  Section('text: defaults');
  ShowTexts(DS);
  Section('text: null row');
  DS.Next;
  ShowTexts(DS);
  Section('text: third row');
  DS.Next;
  ShowTexts(DS);

  Section('text: DisplayFormat, Precision, DisplayValues');
  TIntegerField(DS.FieldByName('i')).DisplayFormat := '000';
  TIntegerField(DS.FieldByName('a')).DisplayFormat := '0.0';
  TLargeintField(DS.FieldByName('l')).DisplayFormat := '#,##0';
  TFloatField(DS.FieldByName('f')).Precision := 3;
  TFloatField(DS.FieldByName('c')).DisplayFormat := '#,##0.0';
  TBooleanField(DS.FieldByName('b')).DisplayValues := 'Yes;No';
  TDateTimeField(DS.FieldByName('dt')).DisplayFormat := 'yyyy/mm/dd hh:nn';
  TDateTimeField(DS.FieldByName('d')).DisplayFormat := 'dd.mm.yyyy';
  TDateTimeField(DS.FieldByName('t')).DisplayFormat := 'hh.nn';
  ShowTexts(DS);
  DS.First;
  ShowTexts(DS);
  CheckEqInt(TFloatField(DS.FieldByName('c')).Precision, 15, 'the default Precision');
  TFloatField(DS.FieldByName('f')).DisplayFormat := '0.000';
  CheckEq(DS.FieldByName('f').DisplayText, '2.500', 'DisplayFormat wins over Precision');
  TBooleanField(DS.FieldByName('b')).DisplayValues := 'On';
  CheckEq(DS.FieldByName('b').DisplayText, 'On', 'DisplayValues with one part: True');
  DS.Last;
  CheckEq(DS.FieldByName('b').DisplayText, '', 'DisplayValues with one part: False');
  DS.Next;
  DS.RecNo := 2;
  CheckEq(DS.FieldByName('l').DisplayText, '', 'a null value has no text with a DisplayFormat either');
  CheckEq(DS.FieldByName('b').DisplayText, '', 'nor with DisplayValues');

  Section('text: OnGetText');
  DS.First;
  F := DS.FieldByName('i');
  F.OnGetText := Calls.GetText;
  CheckEq(F.DisplayText, '<7|True>', 'DisplayText asks the handler');
  CheckEq(F.Text, '<7|False>', 'Text asks the handler');
  CheckEq(F.AsString, '7', 'AsString does not');
  DS.Next;
  CheckEq(F.DisplayText, '<null|True>', 'the handler is asked for a null value too');
  F.OnGetText := nil;

  Section('text: Text := on every field class');
  TBooleanField(DS.FieldByName('b')).DisplayValues := '';
  DS.Append;
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    WriteLn(F.FieldName, ': Text := ''', Texts[I], ''' -> ', PutText(DS, F, Texts[I]));
    WriteLn(F.FieldName, ': Text := '''' -> ', PutText(DS, F, ''));
    WriteLn(F.FieldName, ': Text := ''?'' -> ', PutText(DS, F, '?'));
  end;

  Section('text: Text := on a boolean field with DisplayValues');
  F := DS.FieldByName('b');
  TBooleanField(F).DisplayValues := 'Yes;No';
  CheckEq(PutText(DS, F, 'yes'), 'vkInt 1', 'the first part, any case');
  CheckEq(PutText(DS, F, 'NO'), 'vkInt 0', 'the second part, any case');
  CheckEq(PutText(DS, F, 'True'), 'vkInt 1', 'True');
  CheckEq(PutText(DS, F, '0'), 'vkInt 0', '0');
  CheckEq(PutText(DS, F, 'maybe'), 'E: ''maybe'' is not a valid boolean value for field ''b''', 'other text');
  CheckEq(Put(DS, F, 0, 'yes'), 'E: ''yes'' is not a valid boolean value for field ''b''', 'AsString does not know DisplayValues');

  Section('text: OnSetText');
  F := DS.FieldByName('s');
  F.OnSetText := Calls.SetText;
  ClearLog;
  F.Text := 'abc';
  CheckEq(TakeLog, 'OnSetText(s,abc),[deFieldChange dsInsert s]', 'the handler is called and assigns the field');
  CheckEq(DS.Probe(F), 'vkString ABC', 'with the value it chose');
  F.OnSetText := Calls.SetTextIgnore;
  F.Text := 'zzz';
  CheckEq(TakeLog, 'OnSetText(s,zzz)', 'a handler that assigns nothing');
  CheckEq(DS.Probe(F), 'vkString ABC', 'leaves the value');
  F.Text := '';
  CheckEq(DS.Probe(F), 'vkString ABC', 'also for the empty text');
  DS.Cancel;
  DS.Free;
end;

{ ---------------------------------------------------------------- ClearFields, blob streams }

type
  TMoreCalls = class
    DS: TDataSet;
    Blob: TBlobField;
    Stream: TStream;
    Text, Len: TField;
    Probing: Boolean;
    procedure DoClearFields;
    procedure DoLoad;
    procedure DoSave;
    procedure ReadBlobSize;
    procedure NewRecord(DataSet: TDataSet);
    procedure Calc(DataSet: TDataSet);
    procedure Change(Sender: TField);
  end;

procedure TMoreCalls.DoClearFields;
begin
  DS.ClearFields;
end;

procedure TMoreCalls.DoLoad;
begin
  Blob.LoadFromStream(Stream);
end;

procedure TMoreCalls.DoSave;
begin
  Blob.SaveToStream(Stream);
end;

procedure TMoreCalls.ReadBlobSize;
begin
  WriteLn(Blob.BlobSize);
end;

procedure TMoreCalls.NewRecord(DataSet: TDataSet);
begin
  DataSet.FieldByName('s').AsString := 'preset';
  DataSet.FieldByName('ro').AsString := 'preset';
  DataSet.ClearFields;
end;

procedure TMoreCalls.Calc(DataSet: TDataSet);
begin
  AddLog('OnCalcFields');
  if Probing then
    try
      DataSet.ClearFields;
    except
      on E: Exception do
        AddLog(E.ClassName + ': ' + E.Message);
    end;
  if not Text.IsNull then
    Len.AsInteger := Length(Text.AsString);
end;

procedure TMoreCalls.Change(Sender: TField);
begin
  AddLog('OnChange(' + Sender.FieldName + ')');
end;

var
  More: TMoreCalls;

function MakeBytes(const Values: array of Byte): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(Values));
  for I := 0 to Length(Values) - 1 do
    Result[I] := Values[I];
end;

function HexOf(const B: TBytes): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(B) - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(B[I], 2);
  end;
end;

{ the whole content of the stream; the position is kept }
function StreamHex(Stream: TStream): string;
var
  B: TBytes;
  Saved: Int64;
begin
  Saved := Stream.Position;
  SetLength(B, Stream.Size);
  Stream.Position := 0;
  if Length(B) > 0 then
    Stream.ReadBuffer(B[0], Length(B));
  Stream.Position := Saved;
  Result := HexOf(B);
end;

procedure PutBytes(Stream: TStream; const Values: array of Byte);
var
  B: TBytes;
begin
  B := MakeBytes(Values);
  if Length(B) > 0 then
    Stream.WriteBuffer(B[0], Length(B));
end;

procedure TestClearFields;
var
  DS: TTestDataSet;
  S, I, Ro, Fx: TField;
begin
  Section('clear: ClearFields');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s', ftString, 10);
  DS.AddDef('i', ftInteger);
  DS.AddDef('ro', ftString, 10);
  DS.AddDef('fx', ftInteger);
  DS.AddRow([StringValue('old'), IntValue(1), StringValue('keep'), IntValue(5)]);
  DS.FixedField := 'fx';
  More.DS := DS;
  CheckRaises(More.DoClearFields, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'ClearFields on a closed dataset');
  DS.Open;
  S := DS.FieldByName('s');
  I := DS.FieldByName('i');
  Ro := DS.FieldByName('ro');
  Fx := DS.FieldByName('fx');
  Ro.ReadOnly := True;
  S.OnValidate := Calls.Validate;
  S.OnChange := Calls.Change;
  CheckRaises(More.DoClearFields, 'EDatabaseError', 'Dataset not in edit or insert mode', 'ClearFields in dsBrowse');
  DS.Edit;
  ClearLog;
  DS.ClearFields;
  CheckEq(TakeLog, '[deRecordChange dsEdit]', 'one record notification and no field events');
  Check(S.IsNull and I.IsNull, 'the modifiable fields are null');
  CheckEq(Ro.AsString + ' ' + Fx.AsString, 'keep 5',
    'a read-only field and a column the driver cannot update keep their values');
  CheckEqBool(DS.Modified, True, 'Modified after ClearFields');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[old|1|keep|5] new=[NULL|NULL|keep|5] assigned=1,2',
    'the cleared fields are flagged as assigned');
  DS.Edit;
  DS.OverrideRow(0);
  CheckRaises(More.DoClearFields, 'EDatabaseError', 'Dataset not in edit or insert mode',
    'ClearFields under an active-row override');
  DS.OverrideRow(-1);
  DS.Cancel;

  Section('clear: nothing to clear');
  S.ReadOnly := True;
  I.ReadOnly := True;
  DS.Edit;
  ClearLog;
  DS.ClearFields;
  CheckEq(TakeLog, '[deRecordChange dsEdit]', 'the notification is sent');
  CheckEqBool(DS.Modified, False, 'Modified stays False');
  DS.Cancel;
  S.ReadOnly := False;
  I.ReadOnly := False;

  Section('clear: while inserting');
  DS.Append;
  S.AsString := 'x';
  I.AsInteger := 7;
  ClearLog;
  DS.ClearFields;
  CheckEq(TakeLog, '[deRecordChange dsInsert]', 'the notification in dsInsert');
  Check(S.IsNull and I.IsNull and Ro.IsNull and Fx.IsNull, 'every field is null');
  DS.Post;
  CheckEq(DS.LastApply, 'insert new=[NULL|NULL|NULL|NULL] assigned=1,2', 'the insert that follows');
  DS.OnNewRecord := More.NewRecord;
  DS.Append;
  Check(S.IsNull and Ro.IsNull, 'inside OnNewRecord a read-only field is cleared too');
  CheckEqBool(DS.Modified, False, 'Modified is False after OnNewRecord');
  DS.Cancel;
  DS.Free;

  Section('clear: calculated fields');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('s', ftString, 10);
  DS.AddRow([StringValue('four')]);
  More.DS := DS;
  More.Text := TStringField.Create(DS);
  More.Text.FieldName := 's';
  More.Text.DataSet := DS;
  More.Len := TIntegerField.Create(DS);
  More.Len.FieldName := 'len';
  More.Len.FieldKind := fkCalculated;
  More.Len.DataSet := DS;
  More.Probing := False;
  DS.OnCalcFields := More.Calc;
  DS.Open;
  DS.Edit;
  CheckEqInt(More.Len.AsInteger, 4, 'the calculated value before');
  ClearLog;
  DS.ClearFields;
  CheckEq(TakeLog, 'OnCalcFields,[deRecordChange dsEdit]', 'the record is calculated again before the notification');
  Check(More.Len.IsNull, 'the calculated field follows the cleared data');
  DS.Cancel;
  DS.AutoCalcFields := False;
  DS.Edit;
  ClearLog;
  DS.ClearFields;
  CheckEq(TakeLog, '[deRecordChange dsEdit]', 'no calculation without AutoCalcFields');
  DS.Cancel;
  DS.AutoCalcFields := True;
  More.Probing := True;
  DS.Edit;
  ClearLog;
  More.Text.AsString := 'ab';
  CheckEq(TakeLog,
    'OnCalcFields,EDatabaseError: Operation not allowed while calculating fields,[deFieldChange dsEdit s]',
    'ClearFields is refused while OnCalcFields runs');
  DS.Cancel;
  DS.Free;
end;

procedure TestBlobStreams;
var
  DS: TTestDataSet;
  B, M: TBlobField;
  Stream: TMemoryStream;
begin
  Section('blob: BlobSize and SaveToStream');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('b', ftBlob);
  DS.AddDef('m', ftMemo);
  DS.AddRow([BytesValue(MakeBytes([1, 2, 3])), StringValue('h' + #$00E9)]);
  DS.AddRow([NullValue, NullValue]);
  DS.AddRow([BytesValue(nil), StringValue('')]);
  Stream := TMemoryStream.Create;
  More.Stream := Stream;
  DS.Open;
  B := TBlobField(DS.FieldByName('b'));
  M := TBlobField(DS.FieldByName('m'));
  Check(M is TMemoField, 'the memo field is a blob field');
  CheckEqInt(B.BlobSize, 3, 'BlobSize of a blob is its number of bytes');
  CheckEqInt(M.BlobSize, 3, 'BlobSize of a memo is the length of its UTF-8 form');
  PutBytes(Stream, [$AA]);
  B.SaveToStream(Stream);
  CheckEq(StreamHex(Stream), 'AA 01 02 03', 'SaveToStream writes the bytes at the position of the stream');
  CheckEqInt(Stream.Position, 4, 'and leaves the position behind them');
  M.SaveToStream(Stream);
  CheckEq(StreamHex(Stream), 'AA 01 02 03 68 C3 A9', 'a memo is written as UTF-8 without a byte order mark');
  DS.Next;
  CheckEqInt(B.BlobSize + M.BlobSize, 0, 'BlobSize of a null value is 0');
  B.SaveToStream(Stream);
  M.SaveToStream(Stream);
  CheckEqInt(Stream.Size, 7, 'a null value writes nothing');
  DS.Next;
  CheckEqInt(B.BlobSize + M.BlobSize, 0, 'BlobSize of an empty value is 0');
  B.SaveToStream(Stream);
  M.SaveToStream(Stream);
  CheckEqInt(Stream.Size, 7, 'an empty value writes nothing');

  Section('blob: LoadFromStream');
  DS.First;
  More.Blob := B;
  Stream.Clear;
  PutBytes(Stream, [9, 8, 7, 6]);
  Stream.Position := 1;
  CheckRaises(More.DoLoad, 'EDatabaseError', 'Dataset not in edit or insert mode', 'LoadFromStream in dsBrowse');
  CheckEqInt(Stream.Position, 1, 'the stream was not read');
  B.OnChange := More.Change;
  DS.Edit;
  ClearLog;
  B.LoadFromStream(Stream);
  CheckEq(HexOf(B.AsBytes), '08 07 06', 'the bytes from the position to the end are the new value');
  CheckEqInt(Stream.Position, 4, 'the stream is at its end');
  CheckEq(TakeLog, 'OnChange(b),[deFieldChange dsEdit b]', 'the assignment sequence runs');
  B.OnChange := nil;
  B.LoadFromStream(Stream);
  Check(not B.IsNull and (B.BlobSize = 0), 'nothing left in the stream gives an empty value, not null');
  Stream.Clear;
  PutBytes(Stream, [$61, $E2, $82, $AC]);
  Stream.Position := 0;
  M.LoadFromStream(Stream);
  CheckEq(M.AsString, 'a' + #$20AC, 'a memo decodes the bytes as UTF-8');
  Stream.Clear;
  PutBytes(Stream, [$EF, $BB, $BF, $61]);
  Stream.Position := 0;
  M.LoadFromStream(Stream);
  CheckEq(M.AsString, #$FEFF + 'a', 'a byte order mark is not removed');
  B.ReadOnly := True;
  Stream.Position := 0;
  CheckRaises(More.DoLoad, 'EDatabaseError', 'Field ''b'' cannot be modified', 'LoadFromStream on a read-only field');
  CheckEqInt(Stream.Position, 0, 'the stream was not read either');
  Stream.Clear;
  More.DoSave;
  CheckEqInt(Stream.Size, 0, 'SaveToStream in dsEdit reads the edit buffer');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[bytes(3)|h' + #$00E9 + '] new=[bytes(0)|' + #$FEFF + 'a] assigned=1,2',
    'the row that is posted');
  DS.Close;
  CheckRaises(More.ReadBlobSize, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'BlobSize on a closed dataset');
  CheckRaises(More.DoSave, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'SaveToStream on a closed dataset');
  CheckRaises(More.DoLoad, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'LoadFromStream on a closed dataset');
  Stream.Free;
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  TestMatrix;
  TestStrings;
  TestRange;
  TestCursorAndState;
  TestWriteMatrix;
  TestWriteSize;
  TestWriteState;
  TestWriteSequence;
  TestText;
  More := TMoreCalls.Create;
  TestClearFields;
  TestBlobStreams;
  More.Free;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
