// covers: DAT-002, DAT-005, DAT-010
program DsFields;
{ Field value access. Read section: the As... getters of every field class, the null rules and the
  conversion, access and range errors. }
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
  end;

procedure TCalls.ReadString;
begin
  WriteLn(Field.AsString);
end;

procedure TCalls.ReadIsNull;
begin
  WriteLn(Field.IsNull);
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

begin
  Calls := TCalls.Create;
  TestMatrix;
  TestStrings;
  TestRange;
  TestCursorAndState;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
