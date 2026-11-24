// covers: DAT-110, DAT-111, DAT-112
program SqlUnicode;
{ Text crossing the SQLite API. Helper section: the UTF-16 <-> UTF-8 conversion used for SQL text, file
  names, identifiers and messages. File names and identifiers: a database file and a table whose names
  are not ASCII. Data sections: texts written and read through TSQLiteTable and TSQLiteQuery (surrogate
  pairs, embedded #0, a leading byte order mark character), in a database file, table and column whose
  names are not ASCII; SQL text, parameters and messages. }
uses
  SysUtils, Classes, DB, SQLite3API, SQLiteData, DataTestUtils;

function HexBytes(const B: TBytes): string;
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

function HexUnits(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + IntToHex(Ord(S[I]), 4);
  end;
end;

{ Decodes the given bytes (a zero terminator is added here). }
function Decode(const Bytes: array of Byte): string;
var
  B: TBytes;
  I: Integer;
begin
  SetLength(B, Length(Bytes) + 1);
  for I := 0 to Length(Bytes) - 1 do
    B[I] := Bytes[I];
  B[Length(Bytes)] := 0;
  Result := HexUnits(Utf8ToUtf16(PUtf8(@B[0])));
end;

procedure TestEncode;
begin
  Section('UTF-16 to UTF-8');
  CheckEq(HexBytes(Utf16ToUtf8('')), '00', 'an empty string is the terminator alone');
  CheckEq(HexBytes(Utf16ToUtf8('abc')), '61 62 63 00', 'ASCII, zero-terminated');
  CheckEq(HexBytes(Utf16ToUtf8(#$007F#$0080)), '7F C2 80 00', 'the one-byte and two-byte boundary');
  CheckEq(HexBytes(Utf16ToUtf8(#$00E9)), 'C3 A9 00', 'two bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$07FF#$0800)), 'DF BF E0 A0 80 00', 'the two-byte and three-byte boundary');
  CheckEq(HexBytes(Utf16ToUtf8(#$20AC)), 'E2 82 AC 00', 'three bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$FFFF)), 'EF BF BF 00', 'the last three-byte character');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D#$DE00)), 'F0 9F 98 80 00', 'a surrogate pair is four bytes');
  CheckEq(HexBytes(Utf16ToUtf8(#$D800#$DC00)), 'F0 90 80 80 00', 'the first supplementary character');
  CheckEq(HexBytes(Utf16ToUtf8(#$DBFF#$DFFF)), 'F4 8F BF BF 00', 'the last supplementary character');
  CheckEq(HexBytes(Utf16ToUtf8('a' + #$D83D)), '61 EF BF BD 00', 'an unpaired high surrogate at the end');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D + 'a')), 'EF BF BD 61 00', 'an unpaired high surrogate before a character');
  CheckEq(HexBytes(Utf16ToUtf8(#$DE00 + 'a')), 'EF BF BD 61 00', 'an unpaired low surrogate');
  CheckEq(HexBytes(Utf16ToUtf8(#$DE00#$D83D)), 'EF BF BD EF BF BD 00', 'a reversed pair is two replacement characters');
  CheckEq(HexBytes(Utf16ToUtf8(#$D83D#$D83D#$DE00)), 'EF BF BD F0 9F 98 80 00', 'a high surrogate before a pair');
  CheckEq(HexBytes(Utf16ToUtf8('a' + #0 + 'b')), '61 00 62 00', 'an embedded #0 is kept');
end;

procedure TestDecode;
begin
  Section('UTF-8 to UTF-16');
  CheckEq(Utf8ToUtf16(nil), '', 'nil is the empty string');
  CheckEq(Decode([]), '', 'an empty text');
  CheckEq(Decode([$61, $62, $63]), '0061 0062 0063', 'ASCII');
  CheckEq(Decode([$C3, $A9]), '00E9', 'two bytes');
  CheckEq(Decode([$7F, $C2, $80, $DF, $BF]), '007F 0080 07FF', 'the two-byte range');
  CheckEq(Decode([$E0, $A0, $80, $E2, $82, $AC, $EF, $BF, $BF]), '0800 20AC FFFF', 'the three-byte range');
  CheckEq(Decode([$ED, $9F, $BF, $EE, $80, $80]), 'D7FF E000', 'around the surrogate range');
  CheckEq(Decode([$F0, $9F, $98, $80]), 'D83D DE00', 'four bytes become a surrogate pair');
  CheckEq(Decode([$F0, $90, $80, $80, $F4, $8F, $BF, $BF]), 'D800 DC00 DBFF DFFF', 'the four-byte range');

  Section('invalid UTF-8');
  CheckEq(Decode([$80]), 'FFFD', 'a lone continuation byte');
  CheckEq(Decode([$61, $80, $BF, $62]), '0061 FFFD FFFD 0062', 'each stray continuation byte is one replacement');
  CheckEq(Decode([$C3, $61]), 'FFFD 0061', 'a lead byte without its continuation');
  CheckEq(Decode([$C3]), 'FFFD', 'a lead byte at the end');
  CheckEq(Decode([$E2, $82, $61]), 'FFFD 0061', 'a truncated three-byte sequence is one maximal subpart');
  CheckEq(Decode([$E2, $82]), 'FFFD', 'a three-byte sequence cut by the end');
  CheckEq(Decode([$F0, $9F, $98, $61]), 'FFFD 0061', 'a truncated four-byte sequence is one maximal subpart');
  CheckEq(Decode([$F0, $9F, $C3, $A9]), 'FFFD 00E9', 'decoding continues at the offending byte');
  CheckEq(Decode([$C0, $80]), 'FFFD FFFD', 'an overlong two-byte form');
  CheckEq(Decode([$C1, $BF]), 'FFFD FFFD', 'the other overlong lead byte');
  CheckEq(Decode([$E0, $80, $80]), 'FFFD FFFD FFFD', 'an overlong three-byte form');
  CheckEq(Decode([$E0, $9F, $BF]), 'FFFD FFFD FFFD', 'the last overlong three-byte form');
  CheckEq(Decode([$ED, $A0, $80]), 'FFFD FFFD FFFD', 'an encoded surrogate');
  CheckEq(Decode([$F0, $80, $80, $80]), 'FFFD FFFD FFFD FFFD', 'an overlong four-byte form');
  CheckEq(Decode([$F4, $90, $80, $80]), 'FFFD FFFD FFFD FFFD', 'beyond U+10FFFF');
  CheckEq(Decode([$F5, $80]), 'FFFD FFFD', 'a byte that is never valid');
  CheckEq(Decode([$FF, $FE]), 'FFFD FFFD', 'FF and FE');
end;

procedure TestNames;
var
  C: TSQLiteConnection;
  L: TStringList;
  FileName, Table: string;
begin
  Section('file names and identifiers');
  { Latin, Greek, CJK and a supplementary character in the file name and in the table name }
  FileName := GetCurrentDir + '\d' + #$00E9 + #$03A9 + #$65E5#$672C + #$D83D#$DE00 + '.db';
  Table := 't' + #$00E9 + #$65E5 + #$D83D#$DE00;
  C := TSQLiteConnection.Create(nil);
  L := TStringList.Create;
  C.Database := FileName;
  C.Open;
  Check(FileExists(FileName), 'the database file has the UTF-16 name');
  C.ExecSQL('CREATE TABLE ' + SQLiteQuoteIdent(Table) + ' (' + SQLiteQuoteIdent('c' + #$00FC) + ' TEXT)');
  C.GetTableNames(L);
  CheckEq(HexUnits(L.Text), HexUnits(Table + sLineBreak), 'the table name comes back unchanged');
  C.GetFieldNames(Table, L);
  CheckEq(HexUnits(L.Text), HexUnits('c' + #$00FC + sLineBreak), 'the column name comes back unchanged');
  C.Close;
  C.Open;
  C.GetTableNames(L);
  CheckEq(HexUnits(L.Text), HexUnits(Table + sLineBreak), 'and is stored in the file');
  C.Free;
  L.Free;
  Check(DeleteFile(FileName), 'the file is deleted');
end;

{ the values of one field over all rows, as 'a,b,c' }
function Column(DS: TDataSet; const FieldName: string): string;
var
  F: TField;
begin
  Result := '';
  F := DS.FieldByName(FieldName);
  DS.First;
  while not DS.Eof do
  begin
    if Result <> '' then
      Result := Result + ',';
    Result := Result + F.AsString;
    DS.Next;
  end;
end;

type
  TCalls = class
  public
    T: TSQLiteTable;
    Name: string;
    procedure SetIndex;
    procedure WriteLong;
  end;

procedure TCalls.SetIndex;
begin
  T.IndexFieldNames := Name;
end;

procedure TCalls.WriteLong;
begin
  T.FieldByName('s').AsString := Name;
end;

{ The first column of the first row, in UTF-16 code units; '<none>' without a row, 'NULL' for null. }
function OneUnits(C: TSQLiteConnection; const SQL: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C;
  Q.SQL.Text := SQL;
  Q.Open;
  if Q.IsEmpty then
    Result := '<none>'
  else if Q.Fields[0].IsNull then
    Result := 'NULL'
  else
    Result := HexUnits(Q.Fields[0].AsString);
  Q.Free;
end;

function One(C: TSQLiteConnection; const SQL: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C;
  Q.SQL.Text := SQL;
  Q.Open;
  if Q.IsEmpty then
    Result := '<none>'
  else if Q.Fields[0].IsNull then
    Result := 'NULL'
  else
    Result := Q.Fields[0].AsString;
  Q.Free;
end;

const
  ValueCount = 7;

{ the texts stored by the data section }
function TestValue(Index: Integer): string;
begin
  case Index of
    1: Result := 'z' + #$00E9 + #$20AC;                                { Latin-1 and BMP }
    2: Result := #$D83D#$DE00 + 'a' + #$D800#$DC00 + #$DBFF#$DFFF;     { surrogate pairs }
    3: Result := 'a' + #0 + 'b' + #0;                                  { embedded and final #0 }
    4: Result := '';
    5: Result := #$FEFF + 'x';                                         { a leading byte order mark character }
    6: Result := #$FFFE + 'x';                                         { the reversed mark: a noncharacter }
  else
    Result := #$65E5#$672C#$8A9E + ' ' + #$0391#$03A9 + #$FFFF;
  end;
end;

{ 'ok' when every row reads its text back, else the ids of the rows that differ }
function CompareRows(T: TSQLiteTable; Field: TField): string;
begin
  Result := '';
  T.First;
  while not T.Eof do
  begin
    if Field.IsNull or (Field.AsString <> TestValue(T.FieldByName('id').AsInteger)) then
      Result := Result + ' ' + T.FieldByName('id').AsString + '=[' + HexUnits(Field.AsString) + ']';
    T.Next;
  end;
  if Result = '' then
    Result := 'ok'
  else
    Result := 'differ:' + Result;
end;

procedure TestData;
var
  C: TSQLiteConnection;
  T: TSQLiteTable;
  Q: TSQLiteQuery;
  Calls: TCalls;
  FileName, Table, Col, QT, QC: string;
  F, S: TField;
  I: Integer;
begin
  Section('data: write and read back');
  FileName := GetCurrentDir + '\data' + #$00E9 + #$65E5 + '.db';
  Table := 't' + #$00E9 + #$65E5 + #$D83D#$DE00;
  Col := 'c' + #$00FC + ' ' + #$03A9;
  QT := SQLiteQuoteIdent(Table);
  QC := SQLiteQuoteIdent(Col);
  C := TSQLiteConnection.Create(nil);
  C.Database := FileName;
  C.Open;
  CheckEq(One(C, 'PRAGMA encoding'), 'UTF-8', 'a database we create stores text as UTF-8');
  C.ExecSQL('CREATE TABLE ' + QT + ' (id INTEGER PRIMARY KEY, ' + QC + ' TEXT, s VARCHAR(4))');
  T := TSQLiteTable.Create(nil);
  T.Connection := C;
  T.TableName := Table;
  T.Open;
  F := T.Fields[1];
  S := T.FieldByName('s');
  CheckEq(HexUnits(F.FieldName), HexUnits(Col), 'the field has the name of the column');
  for I := 1 to ValueCount do
  begin
    T.Append;
    F.AsString := TestValue(I);
    T.Post;
    { the row was read back from the database after the insert }
    CheckEq(HexUnits(F.AsString), HexUnits(TestValue(I)), 'value ' + IntToStr(I) + ' after Post');
  end;
  T.Refresh;
  CheckEq(CompareRows(T, F), 'ok', 'every value after Refresh');
  CheckEq(One(C, 'SELECT hex(' + QC + ') FROM ' + QT + ' WHERE id = 1'), '7AC3A9E282AC', 'stored bytes: UTF-8');
  CheckEq(One(C, 'SELECT hex(' + QC + ') FROM ' + QT + ' WHERE id = 2'), 'F09F988061F0908080F48FBFBF',
    'stored bytes: a surrogate pair is one four-byte character');
  CheckEq(One(C, 'SELECT hex(' + QC + ') FROM ' + QT + ' WHERE id = 3'), '61006200', 'stored bytes: #0 is kept');
  CheckEq(One(C, 'SELECT typeof(' + QC + ') || length(' + QC + ') FROM ' + QT + ' WHERE id = 4'), 'text0',
    'an empty string is stored as empty text');
  CheckEq(One(C, 'SELECT hex(' + QC + ') FROM ' + QT + ' WHERE id = 5'), 'EFBBBF78',
    'stored bytes: a leading U+FEFF is kept');
  CheckEq(One(C, 'SELECT hex(' + QC + ') FROM ' + QT + ' WHERE id = 6'), 'EFBFBE78',
    'stored bytes: a leading U+FFFE is kept');
  CheckEq(One(C, 'SELECT group_concat(typeof(' + QC + ')) FROM ' + QT), 'text,text,text,text,text,text,text',
    'every value is TEXT');

  Section('data: string size, edit, key with text');
  Calls := TCalls.Create;
  Calls.T := T;
  T.First;
  T.Edit;
  S.AsString := 'ab' + #$D83D#$DE00;
  T.Post;
  CheckEq(OneUnits(C, 'SELECT s FROM ' + QT + ' WHERE id = 1'), '0061 0062 D83D DE00',
    'a size of 4 holds four UTF-16 code units');
  T.Edit;
  Calls.Name := 'abc' + #$D83D#$DE00;
  CheckRaises(Calls.WriteLong, 'EDatabaseError', 'Value too long for field ''s'' (maximum 4 characters)',
    'five code units are too long');
  F.AsString := TestValue(2) + '!';
  T.Post;
  CheckEq(OneUnits(C, 'SELECT ' + QC + ' FROM ' + QT + ' WHERE id = 1'), HexUnits(TestValue(2) + '!'),
    'an edit writes the new text');
  T.Edit;
  F.AsString := TestValue(1);
  T.Post;
  T.Close;

  Section('data: stored in the file');
  C.Close;
  C.Open;
  T.Open;
  F := T.Fields[1];
  CheckEq(CompareRows(T, F), 'ok', 'every value after the database was reopened');

  Section('data: SQL text, parameters, messages');
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C;
  Q.SQL.Text := 'SELECT id FROM ' + QT + ' WHERE ' + QC + ' = :v';
  for I := 1 to ValueCount do
  begin
    Q.Close;
    Q.ParamByName('v').AsString := TestValue(I);
    Q.Open;
    CheckEq(IntToStr(Q.RecordCount) + '/' + Q.Fields[0].AsString, '1/' + IntToStr(I),
      'value ' + IntToStr(I) + ' as a parameter finds its row');
  end;
  Q.Free;
  CheckEq(OneUnits(C, 'SELECT ''' + TestValue(2) + ''''), HexUnits(TestValue(2)), 'a literal in the SQL text');
  CheckEq(OneUnits(C, 'SELECT id FROM ' + QT + ' WHERE ' + QC + ' = ''' + TestValue(7) + ''''), '0037',
    'a literal in the SQL text finds its row');
  CheckEq(OneUnits(C, 'SELECT 1 AS ' + QC), '0031', 'an alias in the SQL text');
  T.IndexFieldNames := Col;
  CheckEq(HexUnits(Trim(T.SQL.Text)), HexUnits('SELECT * FROM ' + QT + ' ORDER BY ' + QT + '.' + QC),
    'IndexFieldNames with the column name');
  CheckEq(Column(T, 'id'), '4,3,1,7,5,6,2', 'the rows in the binary order of their UTF-8 text');
  Calls.Name := 'n' + #$00F6 + #$D83D#$DE00;
  try
    Calls.SetIndex;
    Check(False, 'an unknown column raises');
  except
    on E: ESQLiteError do
      CheckEq(HexUnits(E.Message), HexUnits('SQLite error 1: no such column: ' + Table + '.' + Calls.Name),
        'the message of SQLite names table and column');
  end;
  T.IndexFieldNames := '';
  T.Open;
  F := T.Fields[1];

  Section('data: dataset operations');
  Check(T.Locate(Col, [TestValue(2)], []), 'Locate by a text with surrogate pairs');
  CheckEqInt(T.FieldByName('id').AsInteger, 2, 'the row found');
  Check(T.Locate(Col, [TestValue(3)], []), 'Locate by a text with #0');
  CheckEqInt(T.FieldByName('id').AsInteger, 3, 'the row found by the text with #0');
  Check(not T.Locate(Col, ['a'], []), 'the text before the #0 is another value');
  T.Filter := '[' + Col + '] = ''' + TestValue(7) + '''';
  T.Filtered := True;
  CheckEq(Column(T, 'id'), '7', 'a filter with the column name and a text');
  T.Filtered := False;
  T.Last;
  T.Delete;
  CheckEq(One(C, 'SELECT count(*) FROM ' + QT), '6', 'a delete');
  Calls.Free;
  T.Free;
  C.Free;
  Check(DeleteFile(FileName), 'the file is deleted');
end;

begin
  TestEncode;
  TestDecode;
  TestNames;
  TestData;
  WriteLn('failed checks: ', FailedChecks);
end.
