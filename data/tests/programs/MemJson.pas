// covers: DAT-310, DAT-311, DAT-312, DAT-313, DAT-314, DAT-315, DAT-316, DAT-317
program MemJson;
{ The JSON form of the in-memory table: the exact text SaveToJSON / SaveToStream / SaveToFile write (equal to
  fixtures/people.json), loading, the round trip and every load error with its offset. }
uses
  SysUtils, Classes, Math, DB, MemData, DataTestUtils, TestDataSet, Conformance;

const
  LF = #10;
  PeopleFile = '..\fixtures\people.json';

type
  TCalls = class
  public
    T: TMemTable;
    Text: string;
    procedure Save;
    procedure Load;
    procedure LoadMissingFile;
    procedure HandleCalc(DataSet: TDataSet);
  end;

var
  Calls: TCalls;

procedure TCalls.Save;
begin
  T.SaveToJSON;
end;

procedure TCalls.Load;
begin
  T.LoadFromJSON(Text);
end;

procedure TCalls.LoadMissingFile;
begin
  T.LoadFromFile('no-such-file.json');
end;

procedure TCalls.HandleCalc(DataSet: TDataSet);
begin
  DataSet.FieldByName('twice').AsInteger := DataSet.FieldByName('n').AsInteger * 2;
end;

function FileBytes(const FileName: string): TBytes;
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmOpenRead);
  try
    SetLength(Result, Integer(S.Size));
    if Length(Result) > 0 then
      S.ReadBuffer(Result[0], Length(Result));
  finally
    S.Free;
  end;
end;

function SameBytes(const A, B: TBytes): Boolean;
var
  I: Integer;
begin
  Result := Length(A) = Length(B);
  if Result then
    for I := 0 to High(A) do
      if A[I] <> B[I] then
      begin
        Result := False;
        Exit;
      end;
end;

{ the table of the format example: two people, the next id is 3 }
function NewPeople: TMemTable;
var
  Png: TBytes;
begin
  Result := TMemTable.Create(nil);
  Result.FieldDefs.Add('id', ftAutoInc);
  Result.FieldDefs.Add('name', ftString, 50, True);
  Result.FieldDefs.Add('score', ftFloat);
  Result.FieldDefs.Add('active', ftBoolean);
  Result.FieldDefs.Add('born', ftDate);
  Result.FieldDefs.Add('photo', ftBlob);
  Result.CreateDataSet;
  SetLength(Png, 8);
  Png[0] := $89;
  Png[1] := $50;
  Png[2] := $4E;
  Png[3] := $47;
  Png[4] := $0D;
  Png[5] := $0A;
  Png[6] := $1A;
  Png[7] := $0A;
  Result.Append;
  Result.FieldByName('name').AsString := 'Ada';
  Result.FieldByName('score').AsFloat := 9.5;
  Result.FieldByName('active').AsBoolean := True;
  Result.FieldByName('born').AsDateTime := EncodeDate(1815, 12, 10);
  Result.FieldByName('photo').AsBytes := Png;
  Result.Post;
  Result.Append;
  Result.FieldByName('name').AsString := 'Bob "B" Stone';
  Result.FieldByName('active').AsBoolean := False;
  Result.Post;
end;

function DefLine(const Name, TypeName: string; Size: Integer; Last: Boolean): string;
begin
  Result := '    { "name": "' + Name + '", "type": "' + TypeName + '", "size": ' + IntToStr(Size) +
    ', "required": false }';
  if not Last then
    Result := Result + ',';
  Result := Result + LF;
end;

{ the definitions as 'id:ftAutoInc:0:False name:ftString:50:True' }
function Defs(T: TDataSet): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to T.FieldDefs.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + T.FieldDefs[I].Name + ':' + FieldTypeName(T.FieldDefs[I].DataType) + ':' +
      IntToStr(T.FieldDefs[I].Size) + ':' + BoolStr(T.FieldDefs[I].Required);
  end;
end;

{ the display texts of the current row as 'a|b|NULL' }
function RowText(T: TDataSet): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to T.FieldCount - 1 do
  begin
    if I > 0 then
      Result := Result + '|';
    if T.Fields[I].IsNull then
      Result := Result + 'NULL'
    else
      Result := Result + T.Fields[I].DisplayText;
  end;
end;

{ every type, the extreme values, the float forms, the string escapes }
function NewTypes: TMemTable;
var
  T: TMemTable;
  Bytes: TBytes;
  Zero: Double;
  I: Integer;
begin
  T := TMemTable.Create(nil);
  T.FieldDefs.Add('s', ftString, 20);
  T.FieldDefs.Add('si', ftSmallint);
  T.FieldDefs.Add('i', ftInteger);
  T.FieldDefs.Add('w', ftWord);
  T.FieldDefs.Add('b', ftBoolean);
  T.FieldDefs.Add('f', ftFloat);
  T.FieldDefs.Add('c', ftCurrency);
  T.FieldDefs.Add('d', ftDate);
  T.FieldDefs.Add('t', ftTime);
  T.FieldDefs.Add('dt', ftDateTime);
  T.FieldDefs.Add('a', ftAutoInc);
  T.FieldDefs.Add('bl', ftBlob);
  T.FieldDefs.Add('m', ftMemo);
  T.FieldDefs.Add('l', ftLargeint);
  T.CreateDataSet;

  T.Append;
  T.FieldByName('s').AsString := 'plain';
  T.FieldByName('si').AsInteger := -32768;
  T.FieldByName('i').AsInteger := 2147483647;
  T.FieldByName('w').AsInteger := 65535;
  T.FieldByName('b').AsBoolean := True;
  T.FieldByName('f').AsFloat := 0.1;
  T.FieldByName('c').AsFloat := 1234.5;
  T.FieldByName('d').AsDateTime := EncodeDate(2024, 2, 29);
  T.FieldByName('t').AsDateTime := EncodeTime(13, 45, 10, 0);
  T.FieldByName('dt').AsDateTime := EncodeDate(2024, 2, 29) + EncodeTime(23, 59, 59, 0);
  SetLength(Bytes, 6);
  for I := 0 to 2 do
  begin
    Bytes[I] := I;
    Bytes[I + 3] := 253 + I;
  end;
  T.FieldByName('bl').AsBytes := Bytes;
  T.FieldByName('m').AsString := 'line1' + #10 + 'line2' + #13 + #8 + #12;
  T.FieldByName('l').AsLargeInt := High(Int64);
  T.Post;

  T.Append;
  { quote, backslash, slash, tab, a control character, a Latin-1 letter, a surrogate pair, a lone surrogate }
  T.FieldByName('s').AsString := '"q" \ /' + #9 + #1 + #$E9 + #$D83D + #$DE00 + #$D83D;
  T.FieldByName('si').AsInteger := 0;
  T.FieldByName('i').AsInteger := Low(Integer);
  T.FieldByName('w').AsInteger := 0;
  T.FieldByName('b').AsBoolean := False;
  T.FieldByName('f').AsFloat := 1e30;
  T.FieldByName('c').AsFloat := -0.01;
  T.FieldByName('d').AsDateTime := 0;
  T.FieldByName('t').AsDateTime := EncodeTime(0, 0, 0, 250);
  { a date before day 0: the time of day counts away from zero }
  T.FieldByName('dt').AsDateTime := EncodeDate(1815, 12, 10) - EncodeTime(6, 30, 0, 125);
  SetLength(Bytes, 0);
  T.FieldByName('bl').AsBytes := Bytes;
  T.FieldByName('m').AsString := '';
  T.FieldByName('l').AsLargeInt := Low(Int64);
  T.Post;

  T.Append;
  T.Post;

  T.Append;
  T.FieldByName('f').AsFloat := 1.5e-7;
  T.FieldByName('c').AsFloat := 1.2345678901234568e20;
  T.Post;

  Zero := 0;
  T.Append;
  T.FieldByName('f').AsFloat := Zero * -1.0;
  T.FieldByName('c').AsFloat := 5e-324;
  T.Post;

  T.Append;
  T.FieldByName('f').AsFloat := 1.7976931348623157e308;
  T.FieldByName('c').AsFloat := 1e21;
  T.Post;

  T.Append;
  T.FieldByName('f').AsFloat := 0.000001;
  T.FieldByName('c').AsFloat := 100;
  T.Post;
  Result := T;
end;

function TypesText: string;
begin
  Result :=
    '{' + LF +
    '  "formatVersion": 1,' + LF +
    '  "fields": [' + LF +
    DefLine('s', 'String', 20, False) +
    DefLine('si', 'Smallint', 0, False) +
    DefLine('i', 'Integer', 0, False) +
    DefLine('w', 'Word', 0, False) +
    DefLine('b', 'Boolean', 0, False) +
    DefLine('f', 'Float', 0, False) +
    DefLine('c', 'Currency', 0, False) +
    DefLine('d', 'Date', 0, False) +
    DefLine('t', 'Time', 0, False) +
    DefLine('dt', 'DateTime', 0, False) +
    DefLine('a', 'AutoInc', 0, False) +
    DefLine('bl', 'Blob', 0, False) +
    DefLine('m', 'Memo', 0, False) +
    DefLine('l', 'Largeint', 0, True) +
    '  ],' + LF +
    '  "autoInc": { "a": 8 },' + LF +
    '  "rows": [' + LF +
    '    ["plain", -32768, 2147483647, 65535, true, 0.1, 1234.5, "2024-02-29", "13:45:10", ' +
    '"2024-02-29T23:59:59", 1, "AAEC/f7/", "line1\nline2\r\b\f", 9223372036854775807],' + LF +
    '    ["\"q\" \\ /\t\u0001' + #$E9 + #$D83D + #$DE00 + '\uD83D", 0, -2147483648, 0, false, 1e+30, -0.01, ' +
    '"1899-12-30", "00:00:00.250", "1815-12-10T06:30:00.125", 2, "", "", -9223372036854775808],' + LF +
    '    [null, null, null, null, null, null, null, null, null, null, 3, null, null, null],' + LF +
    '    [null, null, null, null, null, 1.5e-7, 123456789012345680000, null, null, null, 4, null, null, null],' + LF +
    '    [null, null, null, null, null, -0, 5e-324, null, null, null, 5, null, null, null],' + LF +
    '    [null, null, null, null, null, 1.7976931348623157e+308, 1e+21, null, null, null, 6, null, null, null],' + LF +
    '    [null, null, null, null, null, 0.000001, 100, null, null, null, 7, null, null, null]' + LF +
    '  ]' + LF +
    '}' + LF;
end;

procedure TestSave;
var
  T: TMemTable;
  Fixture, Written: TBytes;
  Expected, Text: string;
  Stream: TBytesStream;
  Twice: TIntegerField;
  N: TIntegerField;
begin
  Section('save: the people table');
  Fixture := FileBytes(PeopleFile);
  Expected := TEncoding.UTF8.GetString(Fixture);
  Check(Length(Fixture) > 0, 'the fixture is there');
  Check(Pos(#13, Expected) = 0, 'the fixture has LF line ends');
  T := NewPeople;
  Calls.T := T;
  CheckEq(T.SaveToJSON, Expected, 'SaveToJSON equals fixtures/people.json');
  Stream := TBytesStream.Create(nil);
  T.SaveToStream(Stream);
  Written := Stream.Bytes;
  SetLength(Written, Integer(Stream.Size));
  Check(SameBytes(Written, Fixture), 'SaveToStream writes the same bytes, without a byte order mark');
  T.SaveToStream(Stream);
  CheckEqInt(Stream.Size, 2 * Length(Fixture), 'SaveToStream writes at the position of the stream');
  Stream.Free;
  T.SaveToFile('people-out.json');
  Check(SameBytes(FileBytes('people-out.json'), Fixture), 'SaveToFile writes the same bytes');
  T.SaveToFile('people-out.json');
  Check(SameBytes(FileBytes('people-out.json'), Fixture), 'SaveToFile replaces an existing file');
  Check(DeleteFile('people-out.json'), 'the written file is removed');

  Section('save: state');
  T.Filter := 'id = 2';
  T.Filtered := True;
  CheckEqInt(T.RecordCount, 1, 'a filtered view');
  CheckEq(T.SaveToJSON, Expected, 'a filter does not change what is saved');
  T.Filtered := False;
  T.Last;
  CheckEq(T.SaveToJSON, Expected, 'the cursor does not matter');
  CheckEqInt(T.RecNo, 2, 'and stays where it was');
  T.First;
  T.Edit;
  T.FieldByName('name').AsString := 'Ada L.';
  Text := T.SaveToJSON;
  CheckEq(StateName(T.State), 'dsBrowse', 'saving posts a pending edit');
  Check(Pos('[1, "Ada L.", 9.5,', Text) > 0, 'the posted value is saved');
  T.Edit;
  CheckEq(T.SaveToJSON, Text, 'saving in an unmodified edit');
  CheckEq(StateName(T.State), 'dsBrowse', 'cancels the edit');
  T.Close;
  CheckRaises(Calls.Save, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'SaveToJSON on a closed table');
  T.Free;

  Section('save: every type');
  T := NewTypes;
  Calls.T := T;
  CheckEq(T.SaveToJSON, TypesText, 'the text of every field type');
  T.Free;

  Section('save: special cases');
  T := TMemTable.Create(nil);
  Calls.T := T;
  T.FieldDefs.Add('n', ftInteger);
  T.CreateDataSet;
  CheckEq(T.SaveToJSON,
    '{' + LF +
    '  "formatVersion": 1,' + LF +
    '  "fields": [' + LF +
    DefLine('n', 'Integer', 0, True) +
    '  ],' + LF +
    '  "autoInc": {},' + LF +
    '  "rows": []' + LF +
    '}' + LF, 'a table without rows and without auto-increment fields');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  N := TIntegerField.Create(T);
  N.FieldName := 'n';
  N.DataSet := T;
  Twice := TIntegerField.Create(T);
  Twice.FieldName := 'twice';
  Twice.FieldKind := fkCalculated;
  Twice.DataSet := T;
  T.OnCalcFields := Calls.HandleCalc;
  T.Open;
  T.Append;
  N.AsInteger := 4;
  T.Post;
  CheckEqInt(Twice.AsInteger, 8, 'a calculated field');
  CheckEq(T.SaveToJSON,
    '{' + LF +
    '  "formatVersion": 1,' + LF +
    '  "fields": [' + LF +
    DefLine('n', 'Integer', 0, True) +
    '  ],' + LF +
    '  "autoInc": {},' + LF +
    '  "rows": [' + LF +
    '    [4]' + LF +
    '  ]' + LF +
    '}' + LF, 'is not saved');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  T.FieldDefs.Add('a', ftAutoInc);
  T.FieldDefs.Add('score', ftFloat);
  T.FieldDefs.Add('b', ftAutoInc);
  T.CreateDataSet;
  T.Append;
  T.FieldByName('score').AsFloat := 1;
  T.Post;
  Check(Pos('  "autoInc": { "a": 2, "b": 2 },' + LF, T.SaveToJSON) > 0, 'two auto-increment counters');
  T.Edit;
  T.FieldByName('score').AsFloat := NaN;
  T.Post;
  CheckRaises(Calls.Save, 'EDatabaseError', 'Cannot save non-finite value of field ''score''', 'saving a NaN');
  T.Edit;
  T.FieldByName('score').AsFloat := Infinity;
  T.Post;
  CheckRaises(Calls.Save, 'EDatabaseError', 'Cannot save non-finite value of field ''score''', 'saving an infinity');
  T.Edit;
  T.FieldByName('score').AsFloat := NegInfinity;
  T.Post;
  CheckRaises(Calls.Save, 'EDatabaseError', 'Cannot save non-finite value of field ''score''',
    'saving a negative infinity');
  Check(T.Active, 'the table stays open');
  T.Free;
end;

procedure TestLoad;
var
  T, T2: TMemTable;
  Rec: TEventRecorder;
  Fixture: TBytes;
  People, Text: string;
  Stream: TBytesStream;
  WithMark: TBytes;
  I: Integer;
begin
  Section('load: the people table');
  Fixture := FileBytes(PeopleFile);
  People := TEncoding.UTF8.GetString(Fixture);
  T := TMemTable.Create(nil);
  Calls.T := T;
  Rec := TEventRecorder.Create(T);
  T.LoadFromJSON(People);
  CheckEq(Rec.Take, 'BeforeOpen,[deUpdateState],AfterOpen,AfterScroll', 'events of a load into a closed table');
  Check(T.Active, 'the loaded table is open');
  CheckEq(Defs(T), 'id:ftAutoInc:0:False name:ftString:50:True score:ftFloat:0:False active:ftBoolean:0:False ' +
    'born:ftDate:0:False photo:ftBlob:0:False', 'the definitions');
  CheckEqInt(T.RecordCount, 2, 'RecordCount');
  CheckEq(IntToStr(T.RecNo) + ' ' + Flags(T), '1 Bof', 'the cursor is on the first row');
  CheckEq(RowText(T), '1|Ada|9.5|True|1815-12-10|(Blob)', 'the first row');
  CheckEq(IntToStr(Length(T.FieldByName('photo').AsBytes)) + ' ' + IntToStr(T.FieldByName('photo').AsBytes[0]) +
    ' ' + IntToStr(T.FieldByName('photo').AsBytes[7]), '8 137 10', 'the bytes of the photo');
  T.Next;
  CheckEq(RowText(T), '2|Bob "B" Stone|NULL|False|NULL|NULL', 'the second row');
  CheckEq(T.SaveToJSON, People, 'saving the loaded table gives the same text');
  T.Append;
  T.FieldByName('name').AsString := 'Cy';
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 3, 'the auto-increment counter is restored');
  Rec.Clear;
  T.LoadFromJSON(People);
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose,BeforeOpen,[deUpdateState],AfterOpen,AfterScroll',
    'events of a load into an open table');
  CheckEqInt(T.RecordCount, 2, 'a load replaces the rows');
  T.Edit;
  T.FieldByName('name').AsString := 'pending';
  T.LoadFromJSON(People);
  CheckEq(StateName(T.State) + ' ' + T.FieldByName('name').AsString, 'dsBrowse Ada', 'a load discards a pending edit');
  Rec.Free;

  Section('load: stream and file');
  T.Close;
  T.LoadFromFile(PeopleFile);
  CheckEq(T.SaveToJSON, People, 'LoadFromFile');
  SetLength(WithMark, Length(Fixture) + 3);
  WithMark[0] := $EF;
  WithMark[1] := $BB;
  WithMark[2] := $BF;
  for I := 0 to High(Fixture) do
    WithMark[I + 3] := Fixture[I];
  Stream := TBytesStream.Create(WithMark);
  T.EmptyDataSet;
  T.LoadFromStream(Stream);
  CheckEq(T.SaveToJSON, People, 'LoadFromStream skips a byte order mark');
  Stream.Free;
  Stream := TBytesStream.Create(Fixture);
  T.EmptyDataSet;
  T.LoadFromStream(Stream);
  CheckEq(T.SaveToJSON, People, 'LoadFromStream without a byte order mark');
  Stream.Free;
  CheckRaises(Calls.LoadMissingFile, 'EFOpenError', '', 'LoadFromFile of a missing file');
  Check(T.Active and (T.RecordCount = 2), 'a missing file leaves the table as it was');

  Section('load: accepted forms');
  T.LoadFromJSON('{"rows":[[1,"a"],[5,null]],"extra":{"x":[1,2,{"y":null}]},"fields":[{"type":"autoinc","name":"id"},' +
    '{"required":true,"size":3,"name":"s","type":"STRING","note":"ignored"}],"formatVersion":1}');
  CheckEq(Defs(T), 'id:ftAutoInc:0:False s:ftString:3:True', 'any property order, unknown properties, type names without case');
  CheckEqInt(T.RecordCount, 2, 'the rows');
  T.Append;
  T.FieldByName('s').AsString := 'b';
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 6, 'without autoInc the counter is the highest value + 1');
  T.LoadFromJSON(#9 + '{' + #13 + #10 + ' "formatVersion" : 1 , "fields" : [ { "name" : "id" , "type" : "AutoInc" } ] }  ' + #10);
  CheckEqInt(T.RecordCount, 0, 'any whitespace; no rows property');
  T.Append;
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 1, 'without autoInc and rows the counter is 1');
  T.LoadFromJSON('{"formatVersion":1,"fields":[{"name":"id","type":"AutoInc"},{"name":"n","type":"Integer"}],' +
    '"autoInc":{"ID":40,"n":7,"other":1},"rows":[[2,3]]}');
  T.Append;
  T.Post;
  CheckEq(RowText(T), '40|NULL', 'autoInc: names without case; other names are ignored');
  T.LoadFromJSON('{"formatVersion":1,"fields":[{"name":"f","type":"Float"},{"name":"c","type":"Currency"}],' +
    '"rows":[[1,-0],[1E2,1.5e-1],[-12.5e+1,0.0]]}');
  CheckEq(T.SaveToJSON,
    '{' + LF +
    '  "formatVersion": 1,' + LF +
    '  "fields": [' + LF +
    DefLine('f', 'Float', 0, False) +
    DefLine('c', 'Currency', 0, True) +
    '  ],' + LF +
    '  "autoInc": {},' + LF +
    '  "rows": [' + LF +
    '    [1, -0],' + LF +
    '    [100, 0.15],' + LF +
    '    [-125, 0]' + LF +
    '  ]' + LF +
    '}' + LF, 'the number forms of float fields');
  T.LoadFromJSON('{"formatVersion":1,"fields":[{"name":"s","type":"String"},{"name":"m","type":"Memo"}],' +
    '"rows":[["A\/\"\\\b\f\n\r\té😀\ud83d","' + #$E9 + #$D83D + #$DE00 + '"]]}');
  Check(T.FieldByName('s').AsString = 'A/"\' + #8 + #12 + #10 + #13 + #9 + #$E9 + #$D83D + #$DE00 + #$D83D,
    'the string escapes');
  Check(T.FieldByName('m').AsString = '' + #$E9 + #$D83D + #$DE00, 'characters outside ASCII');
  T.LoadFromJSON('{"formatVersion":1,"fields":[{"name":"t","type":"Time"},{"name":"dt","type":"DateTime"},' +
    '{"name":"si","type":"Smallint"},{"name":"w","type":"Word"},{"name":"l","type":"Largeint"},{"name":"b","type":"Blob"}],' +
    '"rows":[["23:59:59.999","0001-01-01T00:00:00",-32768,65535,-9223372036854775808,"QUJD"],' +
    '["00:00:00","9999-12-31T23:59:59.001",32767,0,9223372036854775807,"QQ=="]]}');
  Check((T.FieldByName('t').AsDateTime = EncodeTime(23, 59, 59, 999)) and
    (T.FieldByName('dt').AsDateTime = EncodeDate(1, 1, 1)) and (T.FieldByName('si').AsInteger = -32768) and
    (T.FieldByName('w').AsInteger = 65535) and (T.FieldByName('l').AsLargeInt = Low(Int64)) and
    (Length(T.FieldByName('b').AsBytes) = 3) and (T.FieldByName('b').AsBytes[2] = 67), 'the limits, first row');
  T.Next;
  Check((T.FieldByName('t').AsDateTime = 0) and
    (T.FieldByName('dt').AsDateTime = EncodeDate(9999, 12, 31) + EncodeTime(23, 59, 59, 1)) and
    (T.FieldByName('si').AsInteger = 32767) and (T.FieldByName('w').AsInteger = 0) and
    (T.FieldByName('l').AsLargeInt = High(Int64)) and (Length(T.FieldByName('b').AsBytes) = 1) and
    (T.FieldByName('b').AsBytes[0] = 65), 'the limits, second row');
  T.Free;

  Section('round trip');
  T := NewTypes;
  Text := T.SaveToJSON;
  T2 := TMemTable.Create(nil);
  T2.LoadFromJSON(Text);
  CheckEq(Defs(T2), Defs(T), 'identical definitions');
  CheckEq(T2.SaveToJSON, Text, 'the text of the loaded table is identical');
  CheckEqInt(T2.RecordCount, T.RecordCount, 'the same number of rows');
  T.First;
  T2.First;
  Check((T2.FieldByName('f').AsFloat = T.FieldByName('f').AsFloat) and
    (T2.FieldByName('c').AsFloat = T.FieldByName('c').AsFloat) and
    (T2.FieldByName('t').AsFloat = T.FieldByName('t').AsFloat) and
    (T2.FieldByName('dt').AsFloat = T.FieldByName('dt').AsFloat) and
    (T2.FieldByName('m').AsString = T.FieldByName('m').AsString) and
    (T2.FieldByName('l').AsLargeInt = T.FieldByName('l').AsLargeInt) and
    SameBytes(T2.FieldByName('bl').AsBytes, T.FieldByName('bl').AsBytes), 'identical values in the first row');
  T.Next;
  T2.Next;
  Check((T2.FieldByName('s').AsString = T.FieldByName('s').AsString) and
    (T2.FieldByName('f').AsFloat = 1e30) and (T2.FieldByName('dt').AsFloat = T.FieldByName('dt').AsFloat) and
    (T2.FieldByName('t').AsFloat = T.FieldByName('t').AsFloat) and not T2.FieldByName('bl').IsNull and
    not T2.FieldByName('m').IsNull and (T2.FieldByName('l').AsLargeInt = Low(Int64)), 'identical values in the second row');
  T2.Last;
  T2.MoveBy(-2);
  Check((T2.FieldByName('c').AsFloat = 5e-324) and (T2.FieldByName('f').AsFloat = 0), 'the smallest float and the zero');
  T2.Next;
  Check((T2.FieldByName('f').AsFloat = 1.7976931348623157e308) and (T2.FieldByName('c').AsFloat = 1e21),
    'the largest float');
  T2.SaveToFile('types-out.json');
  T2.Close;
  T2.LoadFromFile('types-out.json');
  CheckEq(T2.SaveToJSON, Text, 'identical text through a file');
  Check(DeleteFile('types-out.json'), 'the written file is removed');
  Stream := TBytesStream.Create(nil);
  T2.SaveToStream(Stream);
  Stream.Position := 0;
  T.LoadFromStream(Stream);
  CheckEq(T.SaveToJSON, Text, 'identical text through a stream');
  Stream.Free;
  T2.Append;
  T2.Post;
  CheckEqInt(T2.FieldByName('a').AsInteger, 8, 'identical auto-increment counter');
  T.Free;
  T2.Free;
end;

{ the message of the load error, or 'no error' }
function LoadError(T: TMemTable; const Text: string): string;
begin
  Result := 'no error';
  try
    T.LoadFromJSON(Text);
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

{ Marked is a table text with one ^ where the error is expected: the load must fail with Reason at that
  offset and leave the table closed. }
procedure Bad(T: TMemTable; const Marked, Reason, Name: string);
var
  P: Integer;
  Got: string;
begin
  P := Pos('^', Marked);
  Got := LoadError(T, Copy(Marked, 1, P - 1) + Copy(Marked, P + 1, Length(Marked) - P));
  if T.Active then
    Got := Got + ' (the table is open)';
  CheckEq(Got, 'EDatabaseError: Invalid table data: ' + Reason + ' at offset ' + IntToStr(P - 1), Name);
end;

{ a table text with one field of the given type and size and one row with the given value text }
function One(const TypeName: string; Size: Integer; const Value: string): string;
begin
  Result := '{"formatVersion":1,"fields":[{"name":"v","type":"' + TypeName + '","size":' + IntToStr(Size) +
    '}],"rows":[[' + Value + ']]}';
end;

procedure TestErrors;
const
  Syntax = 'JSON syntax error';
  WrongType = 'value of field "v" has the wrong type';
  Range = 'value of field "v" is out of range';
  BadDate = 'invalid date/time in field "v"';
  BadBase64 = 'invalid base64 in field "v"';
  Head = '{"formatVersion":1,"fields":';
var
  T: TMemTable;
  Rec: TEventRecorder;
  F: TField;
begin
  Section('errors: state after a failed load');
  T := NewPeople;
  Calls.T := T;
  Rec := TEventRecorder.Create(T);
  Bad(T, '{"formatVersion": ^2, "fields": []}', 'unsupported formatVersion 2', 'a failing load');
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose,BeforeOpen', 'events of a failing load');
  Rec.Free;
  Check(not T.Active, 'the table is closed');
  T.Open;
  CheckEqInt(T.RecordCount, 0, 'and empty');
  T.Append;
  T.FieldByName('name').AsString := 'x';
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 1, 'with a new auto-increment counter');
  Bad(T, One('Integer', 0, '1') + '^x', Syntax, 'an error found after the rows');
  T.Open;
  CheckEqInt(T.RecordCount, 0, 'leaves no row either');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  F := TStringField.Create(T);
  F.FieldName := 'zzz';
  F.DataSet := T;
  CheckEq(LoadError(T, TEncoding.UTF8.GetString(FileBytes(PeopleFile))), 'EDatabaseError: Field ''zzz'' not found',
    'persistent fields must match the loaded definitions');
  Check(not T.Active, 'the table is closed after the mismatch');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  Section('errors: JSON syntax');
  Bad(T, '^', Syntax, 'an empty text');
  Bad(T, '  ^', Syntax, 'only whitespace');
  Bad(T, '^[]', Syntax, 'not an object');
  Bad(T, '{"formatVersion": 1^', Syntax, 'the text ends inside the object');
  Bad(T, '{"formatVersion": 1,^, "fields": []}', Syntax, 'two commas');
  Bad(T, '{"formatVersion": 1, "fields": [^}', Syntax, 'a wrong closing bracket');
  Bad(T, '{"formatVersion" ^1}', Syntax, 'a missing colon');
  Bad(T, '{^formatVersion: 1}', Syntax, 'a name without quotes');
  Bad(T, '{"formatVersion": tru^}', Syntax, 'a broken literal');
  Bad(T, '{"formatVersion": 0^1}', Syntax, 'a number with a leading zero');
  Bad(T, '{"formatVersion": 1.^}', Syntax, 'a number without digits after the point');
  Bad(T, '{"formatVersion": -^}', Syntax, 'a sign without digits');
  Bad(T, '{"formatVersion": 1e^}', Syntax, 'an exponent without digits');
  Bad(T, '{"formatVersion": ^+1}', Syntax, 'a plus sign');
  Bad(T, '{"formatVersion": 1, "a": "\^x"}', Syntax, 'an unknown escape');
  Bad(T, '{"formatVersion": 1, "a": "\u12^G4"}', Syntax, 'a broken \u escape');
  Bad(T, '{"formatVersion": 1, "a": "x^' + #10 + '"}', Syntax, 'a control character in a string');
  Bad(T, '{"formatVersion": 1, "a": "x^', Syntax, 'a string without its end');
  Bad(T, '{"formatVersion": 1, "a": [1,^]}', Syntax, 'a comma before a closing bracket');
  Bad(T, '{"formatVersion": 1, "a": {"b": 1,^}}', Syntax, 'a comma before a closing brace');
  Bad(T, One('Integer', 0, '1') + ' ^{}', Syntax, 'text after the object');
  Bad(T, '{"formatVersion": 1, ^''fields'': []}', Syntax, 'single quotes');

  Section('errors: structure');
  Bad(T, Head + '^5}', Syntax, 'fields is not an array');
  Bad(T, Head + '[^7]}', Syntax, 'a field is not an object');
  Bad(T, Head + '[^{"type":"Integer"}]}', Syntax, 'a field without a name');
  Bad(T, Head + '[{"name":^5,"type":"Integer"}]}', Syntax, 'a name that is not a string');
  Bad(T, Head + '[{"name":"v","type":"Integer","size":^"3"}]}', Syntax, 'a size that is not an integer');
  Bad(T, Head + '[{"name":"v","type":"Integer","required":^1}]}', Syntax, 'a required that is not a boolean');
  Bad(T, Head + '[{"name":"v","type":"Integer"}],"autoInc":^[]}', Syntax, 'autoInc is not an object');
  Bad(T, Head + '[{"name":"v","type":"Integer"}],"rows":^5}', Syntax, 'rows is not an array');
  Bad(T, Head + '[{"name":"v","type":"Integer"}],"rows":[[1],^5]}', Syntax, 'a row is not an array');

  Section('errors: format version and fields');
  Bad(T, '{"formatVersion": ^2, "fields": []}', 'unsupported formatVersion 2', 'version 2');
  Bad(T, '{"formatVersion": ^0, "fields": []}', 'unsupported formatVersion 0', 'version 0');
  Bad(T, '{"formatVersion": ^"1", "fields": []}', 'unsupported formatVersion "1"', 'a version string');
  Bad(T, '{"formatVersion": ^1.0, "fields": []}', 'unsupported formatVersion 1.0', 'a version with a fraction');
  Bad(T, '{"fields": []^}', 'unsupported formatVersion null', 'no version');
  Bad(T, '{"formatVersion": 1^}', 'missing "fields"', 'no fields');
  Bad(T, '{"formatVersion": 1, "rows": [], "fields": ^[]}', 'missing "fields"', 'an empty fields array');
  Bad(T, Head + '[{"name":"v","type":^"Foo"}]}', 'unknown field type "Foo"', 'an unknown type name');
  Bad(T, Head + '[{"name":"v","type":^"Unknown"}]}', 'unknown field type "Unknown"', 'the type Unknown');
  Bad(T, Head + '[{"name":"v","type":^"ftInteger"}]}', 'unknown field type "ftInteger"', 'a type name with its prefix');
  Bad(T, Head + '[{"name":"v","type":^5}]}', 'unknown field type "5"', 'a type that is not a string');
  Bad(T, Head + '[^{"name":"v"}]}', 'unknown field type ""', 'a field without a type');
  Bad(T, Head + '[{"name":"id","type":"Integer"},{"type":"String","name":^"ID"}]}', 'duplicate field "ID"',
    'two fields with one name');

  Section('errors: rows');
  Bad(T, Head + '[{"name":"a","type":"Integer"},{"name":"b","type":"Integer"}],"rows":[[1,2],^[1]]}',
    'row has 1 values, expected 2', 'a short row');
  Bad(T, Head + '[{"name":"a","type":"Integer"},{"name":"b","type":"Integer"}],"rows":[^[1,2,3]]}',
    'row has 3 values, expected 2', 'a long row');
  Bad(T, Head + '[{"name":"a","type":"Integer"}],"rows":[^[]]}', 'row has 0 values, expected 1', 'an empty row');

  Section('errors: wrong type');
  Bad(T, One('Integer', 0, '^"1"'), WrongType, 'a string for an integer');
  Bad(T, One('Integer', 0, '^1.5'), WrongType, 'a fraction for an integer');
  Bad(T, One('Largeint', 0, '^1e2'), WrongType, 'an exponent for an integer');
  Bad(T, One('AutoInc', 0, '^true'), WrongType, 'a boolean for an integer');
  Bad(T, One('String', 0, '^5'), WrongType, 'a number for a string');
  Bad(T, One('Memo', 0, '^false'), WrongType, 'a boolean for a memo');
  Bad(T, One('Boolean', 0, '^1'), WrongType, 'a number for a boolean');
  Bad(T, One('Boolean', 0, '^"true"'), WrongType, 'a string for a boolean');
  Bad(T, One('Float', 0, '^"1.5"'), WrongType, 'a string for a float');
  Bad(T, One('Currency', 0, '^true'), WrongType, 'a boolean for a currency value');
  Bad(T, One('Date', 0, '^5'), WrongType, 'a number for a date');
  Bad(T, One('Time', 0, '^5'), WrongType, 'a number for a time');
  Bad(T, One('DateTime', 0, '^5'), WrongType, 'a number for a date and time');
  Bad(T, One('Blob', 0, '^5'), WrongType, 'a number for a blob');
  Bad(T, One('Integer', 0, '^[1]'), WrongType, 'an array as a value');
  Bad(T, One('String', 0, '^{"a":1}'), WrongType, 'an object as a value');
  Bad(T, Head + '[{"name":"v","type":"AutoInc"}],"autoInc":{"v":^"3"}}', WrongType, 'a counter that is not an integer');

  Section('errors: out of range');
  Bad(T, One('Smallint', 0, '^32768'), Range, 'a small integer above its range');
  Bad(T, One('Smallint', 0, '^-32769'), Range, 'a small integer below its range');
  Bad(T, One('Word', 0, '^-1'), Range, 'a negative word');
  Bad(T, One('Word', 0, '^65536'), Range, 'a word above its range');
  Bad(T, One('Integer', 0, '^2147483648'), Range, 'an integer above its range');
  Bad(T, One('AutoInc', 0, '^-2147483649'), Range, 'an auto-increment value below its range');
  Bad(T, One('Largeint', 0, '^9223372036854775808'), Range, 'a large integer above its range');
  Bad(T, One('Largeint', 0, '^-9223372036854775809'), Range, 'a large integer below its range');
  Bad(T, One('Float', 0, '^1e999'), Range, 'a float above its range');
  Bad(T, One('Currency', 0, '^-1e999'), Range, 'a float below its range');
  Bad(T, One('String', 3, '^"abcd"'), Range, 'a string longer than its size');
  Bad(T, Head + '[{"name":"v","type":"AutoInc"}],"autoInc":{"v":^9223372036854775808}}', Range,
    'a counter above its range');

  Section('errors: date and time');
  Bad(T, One('Date', 0, '^"2023-02-29"'), BadDate, 'a day that does not exist');
  Bad(T, One('Date', 0, '^"2023-1-01"'), BadDate, 'a month with one digit');
  Bad(T, One('Date', 0, '^"20230101"'), BadDate, 'a date without separators');
  Bad(T, One('Date', 0, '^""'), BadDate, 'an empty date');
  Bad(T, One('Date', 0, '^"2023-01-01T00:00:00"'), BadDate, 'a date with a time');
  Bad(T, One('Date', 0, '^"0000-01-01"'), BadDate, 'the year 0');
  Bad(T, One('Time', 0, '^"24:00:00"'), BadDate, 'hour 24');
  Bad(T, One('Time', 0, '^"10:00"'), BadDate, 'a time without seconds');
  Bad(T, One('Time', 0, '^"10:00:00.5"'), BadDate, 'a fraction with one digit');
  Bad(T, One('Time', 0, '^"10:00:00.1234"'), BadDate, 'a fraction with four digits');
  Bad(T, One('Time', 0, '^"1x:00:00"'), BadDate, 'a letter in a time');
  Bad(T, One('DateTime', 0, '^"2023-01-01 10:00:00"'), BadDate, 'a space between date and time');
  Bad(T, One('DateTime', 0, '^"2023-01-01T10:00:60"'), BadDate, 'second 60');
  Bad(T, One('DateTime', 0, '^"2023-01-01"'), BadDate, 'a date and time without a time');
  Bad(T, One('DateTime', 0, '^"2023-13-01T10:00:00"'), BadDate, 'month 13');

  Section('errors: base64');
  Bad(T, One('Blob', 0, '^"abc"'), BadBase64, 'a length that is not a multiple of four');
  Bad(T, One('Blob', 0, '^"ab!d"'), BadBase64, 'a character outside the alphabet');
  Bad(T, One('Blob', 0, '^"ab-_"'), BadBase64, 'the URL alphabet');
  Bad(T, One('Blob', 0, '^"=abc"'), BadBase64, 'padding at the start');
  Bad(T, One('Blob', 0, '^"ab=d"'), BadBase64, 'padding in the middle');
  Bad(T, One('Blob', 0, '^"a==="'), BadBase64, 'three padding characters');
  Bad(T, One('Blob', 0, '^"QUJD QUJD"'), BadBase64, 'a space');
  Bad(T, One('Blob', 0, '^"QQ==QUJD"'), BadBase64, 'padding before the last group');
  T.Free;
end;

begin
  Calls := TCalls.Create;
  TestSave;
  TestLoad;
  TestErrors;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
