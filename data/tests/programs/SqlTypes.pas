// covers: DAT-130, DAT-131, DAT-132
program SqlTypes;
{ The SQLite type mapping. Sections: mapping (declared type -> field type and size, the auto-increment
  key, expression columns, Required), read (every accepted storage class per field type, the three
  storage forms of dates and times, convert errors), write (the stored form of every field type, checked
  with typeof() and quote()). }
uses
  SysUtils, Classes, DB, SQLite3API, SQLiteData, DataTestUtils;

type
  TCalls = class
  public
    Q: TSQLiteQuery;
    procedure Open;
  end;

var
  Calls: TCalls;
  C: TSQLiteConnection;
  { appended to the CREATE TABLE of Rd }
  TableSuffix: string;

procedure TCalls.Open;
begin
  Q.Open;
end;

function NewQuery: TSQLiteQuery;
begin
  Result := TSQLiteQuery.Create(nil);
  Result.Connection := C;
end;

function TypeName(T: TFieldType): string;
begin
  case T of
    ftString: Result := 'String';
    ftSmallint: Result := 'Smallint';
    ftInteger: Result := 'Integer';
    ftWord: Result := 'Word';
    ftBoolean: Result := 'Boolean';
    ftFloat: Result := 'Float';
    ftCurrency: Result := 'Currency';
    ftDate: Result := 'Date';
    ftTime: Result := 'Time';
    ftDateTime: Result := 'DateTime';
    ftAutoInc: Result := 'AutoInc';
    ftBlob: Result := 'Blob';
    ftMemo: Result := 'Memo';
    ftLargeint: Result := 'Largeint';
  else
    Result := 'Unknown';
  end;
end;

{ "Type" or "Type(Size)" of a definition. }
function DefText(D: TFieldDef): string;
begin
  Result := TypeName(D.DataType);
  if D.Size <> 0 then
    Result := Result + '(' + IntToStr(D.Size) + ')';
end;

{ The definitions of a statement as "name:Type(Size) ...". }
function Defs(const SQL: string): string;
var
  Q: TSQLiteQuery;
  I: Integer;
begin
  Q := NewQuery;
  Q.SQL.Text := SQL;
  Q.Open;
  Result := '';
  for I := 0 to Q.FieldDefs.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + Q.FieldDefs[I].Name + ':' + DefText(Q.FieldDefs[I]);
  end;
  Q.Free;
end;

{ The field type a column declared as Decl gets. }
function Mapped(const Decl: string): string;
var
  Q: TSQLiteQuery;
begin
  C.ExecSQL('DROP TABLE IF EXISTS m; CREATE TABLE m (k TEXT, c ' + Decl + ')');
  Q := NewQuery;
  Q.SQL.Text := 'SELECT c FROM m';
  Q.Open;
  Result := DefText(Q.FieldDefs[0]);
  Q.Free;
end;

{ The names of the required definitions of a statement. }
function RequiredDefs(const SQL: string; ReadOnly: Boolean): string;
var
  Q: TSQLiteQuery;
  I: Integer;
begin
  Q := NewQuery;
  Q.ReadOnly := ReadOnly;
  Q.SQL.Text := SQL;
  Q.Open;
  Result := '';
  for I := 0 to Q.FieldDefs.Count - 1 do
    if Q.FieldDefs[I].Required then
    begin
      if Result <> '' then
        Result := Result + ',';
      Result := Result + Q.FieldDefs[I].Name;
      if not Q.Fields[I].Required then
        Result := Result + '(field not required)';
    end;
  Q.Free;
end;

procedure TestMapping;
begin
  Section('mapping: declared types');
  CheckEq(Mapped('INTEGER'), 'Integer', 'INTEGER');
  CheckEq(Mapped('INT'), 'Integer', 'INT');
  CheckEq(Mapped('MEDIUMINT'), 'Integer', 'MEDIUMINT');
  CheckEq(Mapped('SMALLINT'), 'Integer', 'SMALLINT');
  CheckEq(Mapped('TINYINT'), 'Integer', 'TINYINT');
  CheckEq(Mapped('INT2'), 'Integer', 'INT2');
  CheckEq(Mapped('INT4'), 'Integer', 'INT4');
  CheckEq(Mapped('BIGINT'), 'Largeint', 'BIGINT');
  CheckEq(Mapped('INT8'), 'Largeint', 'INT8');
  CheckEq(Mapped('UNSIGNED BIG INT'), 'Largeint', 'UNSIGNED BIG INT');
  CheckEq(Mapped('LARGEINT'), 'Largeint', 'LARGEINT');
  CheckEq(Mapped('BOOLEAN'), 'Boolean', 'BOOLEAN');
  CheckEq(Mapped('BOOL'), 'Boolean', 'BOOL');
  CheckEq(Mapped('BIT'), 'Boolean', 'BIT');
  CheckEq(Mapped('CHAR'), 'String(255)', 'CHAR without a length');
  CheckEq(Mapped('CHAR(3)'), 'String(3)', 'CHAR(3)');
  CheckEq(Mapped('VARCHAR(40)'), 'String(40)', 'VARCHAR(40)');
  CheckEq(Mapped('NCHAR(5)'), 'String(5)', 'NCHAR(5)');
  CheckEq(Mapped('NVARCHAR(12)'), 'String(12)', 'NVARCHAR(12)');
  CheckEq(Mapped('VARYING CHARACTER(7)'), 'String(7)', 'VARYING CHARACTER(7)');
  CheckEq(Mapped('NATIVE CHARACTER(8)'), 'String(8)', 'NATIVE CHARACTER(8)');
  CheckEq(Mapped('CHARACTER(9)'), 'String(9)', 'CHARACTER(9)');
  CheckEq(Mapped('VARCHAR'), 'String(255)', 'VARCHAR without a length');
  CheckEq(Mapped('TEXT'), 'Memo', 'TEXT');
  CheckEq(Mapped('CLOB'), 'Memo', 'CLOB');
  CheckEq(Mapped('MEMO'), 'Memo', 'MEMO');
  CheckEq(Mapped('LONGTEXT'), 'Memo', 'LONGTEXT');
  CheckEq(Mapped('BLOB'), 'Blob', 'BLOB');
  CheckEq(Mapped('BINARY'), 'Blob', 'BINARY');
  CheckEq(Mapped('VARBINARY(16)'), 'Blob', 'VARBINARY(16)');
  CheckEq(Mapped('IMAGE'), 'Blob', 'IMAGE');
  CheckEq(Mapped('REAL'), 'Float', 'REAL');
  CheckEq(Mapped('DOUBLE'), 'Float', 'DOUBLE');
  CheckEq(Mapped('DOUBLE PRECISION'), 'Float', 'DOUBLE PRECISION');
  CheckEq(Mapped('FLOAT'), 'Float', 'FLOAT');
  CheckEq(Mapped('NUMERIC'), 'Float', 'NUMERIC');
  CheckEq(Mapped('DECIMAL(10,2)'), 'Float', 'DECIMAL(10,2)');
  CheckEq(Mapped('MONEY'), 'Float', 'MONEY');
  CheckEq(Mapped('CURRENCY'), 'Float', 'CURRENCY');
  CheckEq(Mapped('DATE'), 'Date', 'DATE');
  CheckEq(Mapped('TIME'), 'Time', 'TIME');
  CheckEq(Mapped('DATETIME'), 'DateTime', 'DATETIME');
  CheckEq(Mapped('TIMESTAMP'), 'DateTime', 'TIMESTAMP');

  Section('mapping: normalisation');
  CheckEq(Mapped('integer'), 'Integer', 'lower case');
  CheckEq(Mapped('VarChar ( 20 )'), 'String(20)', 'mixed case and spaces around the length');
  CheckEq(Mapped('double' + #9 + '  precision'), 'Float', 'white space is collapsed');
  CheckEq(Mapped('unsigned   big' + #10 + 'int'), 'Largeint', 'also a line break');
  CheckEq(Mapped('NUMERIC(8, 3)'), 'Float', 'two numbers in the suffix');
  CheckEq(Mapped('"DATETIME"'), 'DateTime', 'a quoted type name');

  Section('mapping: affinity rules for other names');
  CheckEq(Mapped('BIGINTEGER'), 'Integer', 'contains INT');
  CheckEq(Mapped('FLOATING POINT'), 'Integer', 'INT wins over FLOA');
  CheckEq(Mapped('VARCHAR2(30)'), 'String(255)', 'contains CHAR');
  CheckEq(Mapped('NCLOB'), 'Memo', 'contains CLOB');
  CheckEq(Mapped('MEDIUMTEXT'), 'Memo', 'contains TEXT');
  CheckEq(Mapped('TINYBLOB'), 'Blob', 'contains BLOB');
  CheckEq(Mapped('REALLY'), 'Float', 'contains REAL');
  CheckEq(Mapped('DOUBLOON'), 'Float', 'contains DOUB');
  CheckEq(Mapped('STUFF'), 'Float', 'anything else');
  CheckEq(Mapped('YEAR'), 'Float', 'another unknown name');

  Section('mapping: the auto-increment key');
  C.ExecSQL('CREATE TABLE k1 (id INTEGER PRIMARY KEY, v TEXT);' +
    'CREATE TABLE k2 (id integer primary key autoincrement, v TEXT);' +
    'CREATE TABLE k3 (id INT PRIMARY KEY, v TEXT);' +
    'CREATE TABLE k4 (a INTEGER, b INTEGER, v TEXT, PRIMARY KEY (a, b));' +
    'CREATE TABLE k5 (id INTEGER PRIMARY KEY, v TEXT) WITHOUT ROWID;' +
    'CREATE TABLE k6 (v TEXT, id INTEGER, PRIMARY KEY (id));' +
    'CREATE TABLE k7 (id BIGINT PRIMARY KEY, v TEXT)');
  CheckEq(Defs('SELECT * FROM k1'), 'id:AutoInc v:Memo', 'INTEGER PRIMARY KEY is the auto-increment key');
  CheckEq(Defs('SELECT * FROM k2'), 'id:AutoInc v:Memo', 'in any spelling, with AUTOINCREMENT');
  CheckEq(Defs('SELECT * FROM k6'), 'v:Memo id:AutoInc', 'declared as a table constraint');
  CheckEq(Defs('SELECT v, id AS key FROM k1'), 'v:Memo key:AutoInc', 'under another column name');
  CheckEq(Defs('SELECT * FROM k3'), 'id:Integer v:Memo', 'INT PRIMARY KEY is not the row id');
  CheckEq(Defs('SELECT * FROM k4'), 'a:Integer b:Integer v:Memo', 'a key of two columns is not');
  CheckEq(Defs('SELECT * FROM k5'), 'id:Integer v:Memo', 'the key of a WITHOUT ROWID table is not');
  CheckEq(Defs('SELECT * FROM k7'), 'id:Largeint v:Memo', 'BIGINT PRIMARY KEY is not');
  CheckEq(Defs('SELECT rowid, v FROM k3'), 'rowid:Largeint v:Memo', 'an explicit rowid column is a largeint');

  Section('mapping: expression columns');
  CheckEq(Defs('SELECT 1 AS i, 1.5 AS f, ''x'' AS s, x''00'' AS b, NULL AS n'),
    'i:Largeint f:Float s:String b:Blob n:String', 'typed by the storage class of the first row');
  CheckEq(Defs('SELECT 1 AS i, 1.5 AS f WHERE 0'), 'i:String f:String', 'no rows: strings');
  CheckEq(Defs('SELECT count(*) AS n, max(v) AS m, id + 1 AS p, v || ''!'' AS e FROM k1'),
    'n:Largeint m:String p:String e:String', 'aggregates and expressions over an empty table');
  C.ExecSQL('INSERT INTO k1 (v) VALUES (''one''), (''two'')');
  CheckEq(Defs('SELECT count(*) AS n, max(v) AS m FROM k1'), 'n:Largeint m:String', 'aggregates with rows');
  CheckEq(Defs('SELECT id + 1 AS p, v || ''!'' AS e, id * 0.5 AS h FROM k1'), 'p:Largeint e:String h:Float',
    'expressions over table columns');
  C.ExecSQL('CREATE TABLE untyped (a, b); INSERT INTO untyped VALUES (7, ''seven''), (NULL, NULL)');
  CheckEq(Defs('SELECT a, b FROM untyped'), 'a:Largeint b:String', 'columns declared without a type follow the first row');
  CheckEq(Defs('SELECT CAST(v AS INTEGER) AS c FROM k1'), 'c:Largeint', 'a CAST is an expression');

  Section('mapping: Required');
  C.ExecSQL('CREATE TABLE r (id INTEGER PRIMARY KEY, must TEXT NOT NULL, dflt TEXT NOT NULL DEFAULT ''x'',' +
    ' opt TEXT, num INTEGER NOT NULL, numd INTEGER NOT NULL DEFAULT 0);' +
    'CREATE TABLE r2 (code VARCHAR(5) NOT NULL PRIMARY KEY, label TEXT NOT NULL)');
  CheckEq(RequiredDefs('SELECT * FROM r', False), 'must,num', 'NOT NULL columns without a default');
  CheckEq(RequiredDefs('SELECT id, must, upper(must) AS u FROM r', False), 'must', 'never an expression column');
  CheckEq(RequiredDefs('SELECT * FROM r2', False), 'code,label', 'a NOT NULL key that is not auto-increment');
  CheckEq(RequiredDefs('SELECT * FROM r', True), '', 'none in a ReadOnly query');
  CheckEq(RequiredDefs('SELECT must, num FROM r', False), '', 'none without a key: the query is read-only');
  CheckEq(RequiredDefs('SELECT r.id, r.must, r2.label FROM r, r2', False), '', 'none in a join');
end;

function Pad(N, Width: Integer): string;
begin
  Result := IntToStr(N);
  while Length(Result) < Width do
    Result := '0' + Result;
end;

{ A date-time value as "YYYY-MM-DD HH:MM:SS.mmm". }
function Stamp(V: TDateTime): string;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDate(V, Y, M, D);
  DecodeTime(V, H, N, S, MS);
  Result := Pad(Y, 4) + '-' + Pad(M, 2) + '-' + Pad(D, 2) + ' ' + Pad(H, 2) + ':' + Pad(N, 2) + ':' + Pad(S, 2) +
    '.' + Pad(MS, 3);
end;

function HexBytes(const B: TBytes): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(B) - 1 do
    Result := Result + IntToHex(B[I], 2);
end;

{ Stores Value (an SQL expression) in a column declared as Decl and reads it back through the field:
  "null", or the value in the form of the field type. }
function Rd(const Decl, Value: string): string;
var
  Q: TSQLiteQuery;
  F: TField;
begin
  C.ExecSQL('DROP TABLE IF EXISTS rd; CREATE TABLE rd (c ' + Decl + ')' + TableSuffix + '; INSERT INTO rd VALUES (' +
    Value + ')');
  Q := NewQuery;
  Q.SQL.Text := 'SELECT c FROM rd';
  Result := '';
  try
    Q.Open;
    F := Q.Fields[0];
    if F.IsNull then
      Result := 'null'
    else
      case F.DataType of
        ftInteger, ftAutoInc:
          Result := IntToStr(F.AsInteger);
        ftLargeint:
          Result := IntToStr(F.AsLargeInt);
        ftBoolean:
          if F.AsBoolean then
            Result := 'True'
          else
            Result := 'False';
        ftFloat:
          Result := FloatToStr(F.AsFloat, InvariantFormatSettings);
        ftBlob:
          Result := 'x' + HexBytes(F.AsBytes);
        ftDate, ftTime, ftDateTime:
          Result := Stamp(F.AsDateTime);
      else
        Result := F.AsString;
      end;
  except
    on E: EDatabaseError do
      Result := E.ClassName + ': ' + E.Message;
  end;
  if (Copy(Result, 1, 1) = 'E') and Q.Active then
    Result := Result + ' (dataset open)';
  Q.Free;
end;

procedure TestRead;
var
  Q: TSQLiteQuery;
  S: string;
begin
  Section('read: integer fields');
  CheckEq(Rd('INTEGER', 'NULL'), 'null', 'NULL is null');
  CheckEq(Rd('INTEGER', '42'), '42', 'INTEGER storage');
  CheckEq(Rd('INTEGER', '-2147483648'), '-2147483648', 'the lowest 32-bit value');
  CheckEq(Rd('INTEGER', '2147483647'), '2147483647', 'the highest 32-bit value');
  CheckEq(Rd('INTEGER', '2147483648'), 'EDatabaseError: Value of field ''c'' is out of range', 'above the 32-bit range');
  CheckEq(Rd('INTEGER', '-2147483649'), 'EDatabaseError: Value of field ''c'' is out of range', 'below the 32-bit range');
  CheckEq(Rd('INTEGER', '1e20'), 'EDatabaseError: Value of field ''c'' is out of range', 'an integral FLOAT out of range');
  CheckEq(Rd('INTEGER', '1.5'), 'EDatabaseError: ''1.5'' is not a valid integer value for field ''c''',
    'a FLOAT with a fraction');
  CheckEq(Rd('INTEGER', '''12abc'''), 'EDatabaseError: ''12abc'' is not a valid integer value for field ''c''',
    'TEXT that is not a number');
  CheckEq(Rd('INTEGER', 'x''01'''), 'EDatabaseError: ''(Blob)'' is not a valid integer value for field ''c''',
    'BLOB storage');
  CheckEq(Rd('INTEGER', '''$1F'''), '31', 'TEXT the column kept as text is parsed as an integer');
  CheckEq(Rd('INTEGER PRIMARY KEY', '7'), '7', 'the auto-increment key');
  CheckEq(Rd('INTEGER PRIMARY KEY', '4294967296'), 'EDatabaseError: Value of field ''c'' is out of range',
    'an auto-increment key above the 32-bit range');

  Section('read: largeint fields');
  CheckEq(Rd('BIGINT', '9223372036854775807'), '9223372036854775807', 'the highest 64-bit value');
  CheckEq(Rd('BIGINT', '-9223372036854775807 - 1'), '-9223372036854775808', 'the lowest 64-bit value');
  CheckEq(Rd('BIGINT', '1e20'), 'EDatabaseError: Value of field ''c'' is out of range', 'an integral FLOAT out of range');
  CheckEq(Rd('BIGINT', '0.25'), 'EDatabaseError: ''0.25'' is not a valid integer value for field ''c''',
    'a FLOAT with a fraction');
  CheckEq(Rd('BIGINT', '''x'''), 'EDatabaseError: ''x'' is not a valid integer value for field ''c''', 'TEXT that is not a number');
  CheckEq(Rd('BIGINT', 'NULL'), 'null', 'NULL is null');

  Section('read: boolean fields');
  CheckEq(Rd('BOOLEAN', '1'), 'True', 'INTEGER 1');
  CheckEq(Rd('BOOLEAN', '0'), 'False', 'INTEGER 0');
  CheckEq(Rd('BOOLEAN', '-5'), 'True', 'any other INTEGER');
  CheckEq(Rd('BOOLEAN', '0.5'), 'True', 'a FLOAT that is not zero');
  CheckEq(Rd('BOOLEAN', '''true'''), 'True', 'TEXT true');
  CheckEq(Rd('BOOLEAN', '''FALSE'''), 'False', 'TEXT FALSE');
  CheckEq(Rd('BOOLEAN', '''T'''), 'True', 'TEXT T');
  CheckEq(Rd('BOOLEAN', '''f'''), 'False', 'TEXT f');
  CheckEq(Rd('BOOLEAN', '''Yes'''), 'True', 'TEXT Yes');
  CheckEq(Rd('BOOLEAN', '''no'''), 'False', 'TEXT no');
  CheckEq(Rd('BOOLEAN', '''maybe'''), 'EDatabaseError: ''maybe'' is not a valid boolean value for field ''c''',
    'another TEXT');
  CheckEq(Rd('BOOLEAN', 'x''01'''), 'EDatabaseError: ''(Blob)'' is not a valid boolean value for field ''c''', 'BLOB storage');
  CheckEq(Rd('BOOLEAN', 'NULL'), 'null', 'NULL is null');

  Section('read: float fields');
  CheckEq(Rd('REAL', '1.25'), '1.25', 'FLOAT storage');
  CheckEq(Rd('NUMERIC', '3'), '3', 'INTEGER storage');
  CheckEq(Rd('REAL', '-0.5'), '-0.5', 'a negative value');
  TableSuffix := ' STRICT';
  CheckEq(Rd('ANY', '''2.5'''), '2.5', 'TEXT is parsed');
  CheckEq(Rd('ANY', '''1e3'''), '1000', 'TEXT with an exponent');
  CheckEq(Rd('ANY', '''-.5'''), '-0.5', 'TEXT without a leading digit');
  TableSuffix := '';
  CheckEq(Rd('REAL', '''abc'''), 'EDatabaseError: ''abc'' is not a valid floating-point value for field ''c''',
    'TEXT that is not a number');
  CheckEq(Rd('REAL', '''1,5'''), 'EDatabaseError: ''1,5'' is not a valid floating-point value for field ''c''',
    'a decimal comma is not accepted');
  CheckEq(Rd('REAL', 'x''01'''), 'EDatabaseError: ''(Blob)'' is not a valid floating-point value for field ''c''',
    'BLOB storage');
  CheckEq(Rd('REAL', 'NULL'), 'null', 'NULL is null');

  Section('read: string and memo fields');
  CheckEq(Rd('VARCHAR(10)', '''abc'''), 'abc', 'TEXT storage');
  CheckEq(Rd('VARCHAR(10)', ''''''), '', 'an empty text is not null');
  CheckEq(Rd('VARCHAR(3)', '''longer than three'''), 'longer than three', 'the declared length does not cut a stored value');
  CheckEq(Rd('TEXT', '''memo text'''), 'memo text', 'a memo');
  CheckEq(Rd('TEXT', '12'), '12', 'a number is stored as text by the column');
  CheckEq(Rd('VARCHAR(10)', 'NULL'), 'null', 'NULL is null');
  Q := NewQuery;
  Q.SQL.Text := 'SELECT ''t'' AS s UNION ALL SELECT 1 UNION ALL SELECT 2.5 UNION ALL SELECT NULL';
  Q.Open;
  S := '';
  while not Q.Eof do
  begin
    if Q.Fields[0].IsNull then
      S := S + 'null;'
    else
      S := S + Q.Fields[0].AsString + ';';
    Q.Next;
  end;
  CheckEq(S, 't;1;2.5;null;', 'a string column typed by its first row takes the numbers of later rows as text');
  Q.Free;

  Section('read: blob fields');
  CheckEq(Rd('BLOB', 'x''0001FF'''), 'x0001FF', 'BLOB storage');
  CheckEq(Rd('BLOB', 'x'''''), 'x', 'a zero-length blob is empty, not null');
  CheckEq(Rd('BLOB', '''abc'''), 'x616263', 'TEXT gives its bytes');
  CheckEq(Rd('BLOB', '12'), 'x3132', 'a number gives the bytes of its text');
  CheckEq(Rd('BLOB', 'NULL'), 'null', 'NULL is null');

  Section('read: date-time fields');
  CheckEq(Rd('DATETIME', '''2024-03-05'''), '2024-03-05 00:00:00.000', 'YYYY-MM-DD');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20'''), '2024-03-05 10:20:00.000', 'YYYY-MM-DD HH:MM');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20:30'''), '2024-03-05 10:20:30.000', 'YYYY-MM-DD HH:MM:SS');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20:30.250'''), '2024-03-05 10:20:30.250', 'YYYY-MM-DD HH:MM:SS.SSS');
  CheckEq(Rd('DATETIME', '''2024-03-05T10:20'''), '2024-03-05 10:20:00.000', 'T instead of the space');
  CheckEq(Rd('DATETIME', '''2024-03-05T10:20:30'''), '2024-03-05 10:20:30.000', 'T with seconds');
  CheckEq(Rd('DATETIME', '''2024-03-05T10:20:30.125'''), '2024-03-05 10:20:30.125', 'T with milliseconds');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20:30Z'''), '2024-03-05 10:20:30.000', 'a trailing Z is ignored');
  CheckEq(Rd('DATETIME', '''2024-03-05T10:20:30.500+02:00'''), '2024-03-05 10:20:30.500', 'a time-zone offset is ignored');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20-08:00'''), '2024-03-05 10:20:00.000', 'also a negative one');
  CheckEq(Rd('DATETIME', '''  2024-03-05 10:20:30  '''), '2024-03-05 10:20:30.000', 'spaces around are ignored');
  CheckEq(Rd('DATETIME', '''10:20:30'''), '1899-12-30 10:20:30.000', 'a time alone has date part 0');
  CheckEq(Rd('DATETIME', '''2024-02-29 23:59:59.999'''), '2024-02-29 23:59:59.999', 'the last millisecond of a leap day');
  CheckEq(Rd('DATETIME', '''1850-06-15 18:00:00'''), '1850-06-15 18:00:00.000', 'a date before day 0');
  CheckEq(Rd('DATETIME', '0'), '1970-01-01 00:00:00.000', 'INTEGER: Unix seconds');
  CheckEq(Rd('DATETIME', '951786061'), '2000-02-29 01:01:01.000', 'Unix seconds with a time of day');
  CheckEq(Rd('DATETIME', '-1'), '1969-12-31 23:59:59.000', 'negative Unix seconds');
  CheckEq(Rd('DATETIME', '2440587.5'), '1970-01-01 00:00:00.000', 'FLOAT: Julian day number');
  CheckEq(Rd('DATETIME', '2451544.75'), '2000-01-01 06:00:00.000', 'a Julian day with a time of day');
  CheckEq(Rd('DATETIME', '2415018.5'), '1899-12-30 00:00:00.000', 'the Julian day of day 0');
  CheckEq(Rd('TIMESTAMP', '''2001-02-03 04:05:06'''), '2001-02-03 04:05:06.000', 'a TIMESTAMP column');
  CheckEq(Rd('DATETIME', 'NULL'), 'null', 'NULL is null');

  Section('read: date fields');
  CheckEq(Rd('DATE', '''2024-03-05'''), '2024-03-05 00:00:00.000', 'YYYY-MM-DD');
  CheckEq(Rd('DATE', '''2024-03-05 10:20:30'''), '2024-03-05 00:00:00.000', 'the time of a date-time text is dropped');
  CheckEq(Rd('DATE', '''10:20:30'''), '1899-12-30 00:00:00.000', 'a time alone: date part 0');
  CheckEq(Rd('DATE', '951786061'), '2000-02-29 00:00:00.000', 'Unix seconds');
  CheckEq(Rd('DATE', '2451544.75'), '2000-01-01 00:00:00.000', 'a Julian day number');
  CheckEq(Rd('DATE', 'NULL'), 'null', 'NULL is null');

  Section('read: time fields');
  CheckEq(Rd('TIME', '''10:20'''), '1899-12-30 10:20:00.000', 'HH:MM');
  CheckEq(Rd('TIME', '''10:20:30'''), '1899-12-30 10:20:30.000', 'HH:MM:SS');
  CheckEq(Rd('TIME', '''10:20:30.250'''), '1899-12-30 10:20:30.250', 'HH:MM:SS.SSS');
  CheckEq(Rd('TIME', '''23:59:59.5'''), '1899-12-30 23:59:59.500', 'a shorter fraction');
  CheckEq(Rd('TIME', '''10:20:30.123456'''), '1899-12-30 10:20:30.123', 'a longer fraction is cut to milliseconds');
  CheckEq(Rd('TIME', '''10:20:30Z'''), '1899-12-30 10:20:30.000', 'a trailing Z is ignored');
  CheckEq(Rd('TIME', '''2024-03-05'''), '1899-12-30 00:00:00.000', 'a date alone: time part 0');
  CheckEq(Rd('TIME', '''2024-03-05 10:20:30'''), '1899-12-30 10:20:30.000', 'the date of a date-time text is dropped');
  CheckEq(Rd('TIME', '951786061'), '1899-12-30 01:01:01.000', 'Unix seconds');
  CheckEq(Rd('TIME', '2451544.75'), '1899-12-30 06:00:00.000', 'a Julian day number');
  CheckEq(Rd('TIME', 'NULL'), 'null', 'NULL is null');

  Section('read: date-time convert errors');
  CheckEq(Rd('DATETIME', '''yesterday'''), 'EDatabaseError: ''yesterday'' is not a valid date-time value for field ''c''',
    'a word');
  CheckEq(Rd('DATETIME', '''2024-13-01'''), 'EDatabaseError: ''2024-13-01'' is not a valid date-time value for field ''c''',
    'month 13');
  CheckEq(Rd('DATE', '''2023-02-29'''), 'EDatabaseError: ''2023-02-29'' is not a valid date-time value for field ''c''',
    'a day that does not exist');
  CheckEq(Rd('TIME', '''10:61'''), 'EDatabaseError: ''10:61'' is not a valid date-time value for field ''c''', 'minute 61');
  CheckEq(Rd('TIME', '''24:00:00'''), 'EDatabaseError: ''24:00:00'' is not a valid date-time value for field ''c''', 'hour 24');
  CheckEq(Rd('DATETIME', '''2024-03-05 10'''), 'EDatabaseError: ''2024-03-05 10'' is not a valid date-time value for field ''c''',
    'hours alone');
  CheckEq(Rd('DATETIME', '''2024-3-5'''), 'EDatabaseError: ''2024-3-5'' is not a valid date-time value for field ''c''',
    'no zero padding');
  CheckEq(Rd('DATETIME', '''2024-03-05 10:20:30 pm'''),
    'EDatabaseError: ''2024-03-05 10:20:30 pm'' is not a valid date-time value for field ''c''', 'text after the time');
  CheckEq(Rd('DATETIME', '''2024-03-05Z'''), 'EDatabaseError: ''2024-03-05Z'' is not a valid date-time value for field ''c''',
    'a zone after a date alone');
  CheckEq(Rd('DATETIME', '''10:20:30.'''), 'EDatabaseError: ''10:20:30.'' is not a valid date-time value for field ''c''',
    'a point without digits');
  CheckEq(Rd('DATETIME', '''''' ), 'EDatabaseError: '''' is not a valid date-time value for field ''c''', 'an empty text');
  CheckEq(Rd('DATETIME', 'x''01'''), 'EDatabaseError: ''(Blob)'' is not a valid date-time value for field ''c''', 'BLOB storage');
  CheckEq(Rd('DATETIME', '1e300'), 'EDatabaseError: ''1.0e+300'' is not a valid date-time value for field ''c''',
    'a Julian day number out of range');
end;

{ typeof() and quote() of the columns of the row with this id in table w. }
function Stored(const Column: string; Id: Integer): string;
var
  Q: TSQLiteQuery;
begin
  Q := NewQuery;
  Q.SQL.Text := 'SELECT typeof(' + Column + ') || '' '' || quote(' + Column + ') FROM w WHERE id = ' + IntToStr(Id);
  Q.Open;
  Result := Q.Fields[0].AsString;
  Q.Free;
end;

procedure TestWrite;
var
  Q: TSQLiteQuery;
  B: TBytes;
  Id: Integer;
  When: TDateTime;
begin
  Section('write');
  C.ExecSQL('CREATE TABLE w (id INTEGER PRIMARY KEY, i INTEGER, big BIGINT, flag BOOLEAN, f REAL,' +
    ' s VARCHAR(20), m TEXT, b BLOB, d DATE, t TIME, dt DATETIME)');
  Q := NewQuery;
  Q.SQL.Text := 'SELECT * FROM w ORDER BY id';
  Q.Open;
  CheckEqBool(Q.CanModify, True, 'the query is updatable');

  Q.Append;
  Q.FieldByName('i').AsInteger := -42;
  Q.FieldByName('big').AsLargeInt := 1099511627776;
  Q.FieldByName('flag').AsBoolean := True;
  Q.FieldByName('f').AsFloat := 2;
  Q.FieldByName('s').AsString := 'it''s';
  Q.FieldByName('m').AsString := 'memo ' + #$00E9;
  SetLength(B, 3);
  B[0] := 0;
  B[1] := 127;
  B[2] := 255;
  Q.FieldByName('b').AsBytes := B;
  When := EncodeDate(2024, 3, 5) + EncodeTime(10, 20, 30, 0);
  Q.FieldByName('d').AsDateTime := When;
  Q.FieldByName('t').AsDateTime := When;
  Q.FieldByName('dt').AsDateTime := When;
  Q.Post;
  Id := Q.FieldByName('id').AsInteger;
  CheckEqInt(Id, 1, 'the row is inserted');
  CheckEq(Stored('i', Id), 'integer -42', 'an integer field is INTEGER');
  CheckEq(Stored('big', Id), 'integer 1099511627776', 'a largeint field is INTEGER');
  CheckEq(Stored('flag', Id), 'integer 1', 'a boolean field is INTEGER 1');
  CheckEq(Stored('f', Id), 'real 2.0', 'a float field is REAL');
  CheckEq(Stored('s', Id), 'text ''it''''s''', 'a string field is TEXT');
  CheckEq(Stored('m', Id), 'text ''memo ' + #$00E9 + '''', 'a memo field is TEXT');
  CheckEq(Stored('b', Id), 'blob X''007FFF''', 'a blob field is BLOB');
  CheckEq(Stored('d', Id), 'text ''2024-03-05''', 'a date field is TEXT YYYY-MM-DD');
  CheckEq(Stored('t', Id), 'text ''10:20:30''', 'a time field is TEXT HH:MM:SS');
  CheckEq(Stored('dt', Id), 'text ''2024-03-05 10:20:30''', 'a date-time field is TEXT YYYY-MM-DD HH:MM:SS');

  Q.Append;
  Q.FieldByName('flag').AsBoolean := False;
  Q.FieldByName('f').AsFloat := -0.125;
  Q.FieldByName('s').AsString := '';
  Q.FieldByName('m').AsString := '';
  SetLength(B, 0);
  Q.FieldByName('b').AsBytes := B;
  { before day 0 the time of day is the absolute fraction }
  When := EncodeDate(987, 1, 2) - EncodeTime(0, 0, 5, 7);
  Q.FieldByName('d').AsDateTime := When;
  Q.FieldByName('t').AsDateTime := When;
  Q.FieldByName('dt').AsDateTime := When;
  Q.Post;
  Id := Q.FieldByName('id').AsInteger;
  CheckEqInt(Id, 2, 'a second row');
  CheckEq(Stored('i', Id), 'null NULL', 'an unassigned field is NULL');
  CheckEq(Stored('flag', Id), 'integer 0', 'False is INTEGER 0');
  CheckEq(Stored('f', Id), 'real -0.125', 'a negative float');
  CheckEq(Stored('s', Id), 'text ''''', 'an empty string is empty TEXT, not NULL');
  CheckEq(Stored('m', Id), 'text ''''', 'an empty memo too');
  CheckEq(Stored('b', Id), 'blob X''''', 'an empty blob is an empty BLOB, not NULL');
  CheckEq(Stored('d', Id), 'text ''0987-01-02''', 'a year below 1000 has four digits');
  CheckEq(Stored('t', Id), 'text ''00:00:05.007''', 'milliseconds are appended when not zero');
  CheckEq(Stored('dt', Id), 'text ''0987-01-02 00:00:05.007''', 'also in a date-time');

  Q.Edit;
  Q.FieldByName('i').AsInteger := 5;
  Q.FieldByName('s').Clear;
  Q.FieldByName('dt').AsDateTime := EncodeDate(1999, 12, 31) + EncodeTime(23, 59, 59, 999);
  Q.Post;
  CheckEq(Stored('i', Id), 'integer 5', 'an update binds the same way');
  CheckEq(Stored('s', Id), 'null NULL', 'a cleared field is NULL');
  CheckEq(Stored('dt', Id), 'text ''1999-12-31 23:59:59.999''', 'a date-time with milliseconds');

  Q.Close;
  Q.Open;
  Q.First;
  CheckEq(Stamp(Q.FieldByName('d').AsDateTime), '2024-03-05 00:00:00.000', 'the date reads back');
  CheckEq(Stamp(Q.FieldByName('t').AsDateTime), '1899-12-30 10:20:30.000', 'the time reads back');
  CheckEq(Stamp(Q.FieldByName('dt').AsDateTime), '2024-03-05 10:20:30.000', 'the date-time reads back');
  CheckEq(HexBytes(Q.FieldByName('b').AsBytes), '007FFF', 'the blob reads back');
  CheckEqBool(Q.FieldByName('flag').AsBoolean, True, 'the boolean reads back');
  CheckEq(Q.FieldByName('m').AsString, 'memo ' + #$00E9, 'the memo reads back');
  Q.Next;
  CheckEq(Stamp(Q.FieldByName('dt').AsDateTime), '1999-12-31 23:59:59.999', 'milliseconds read back');
  CheckEq(Stamp(Q.FieldByName('t').AsDateTime), '1899-12-30 00:00:05.007', 'in a time too');
  Check(not Q.FieldByName('b').IsNull and (Length(Q.FieldByName('b').AsBytes) = 0), 'the empty blob reads back as empty');
  Check(not Q.FieldByName('m').IsNull and (Q.FieldByName('m').AsString = ''), 'the empty memo reads back as empty');
  Check(Q.FieldByName('s').IsNull, 'the cleared string reads back as null');
  Q.Free;
end;

begin
  Calls := TCalls.Create;
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  C.Open;
  TestMapping;
  TestRead;
  TestWrite;
  C.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
