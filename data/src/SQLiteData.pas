{ SQLiteData - the SQLite driver on the stock sqlite3.dll: ESQLiteError and the mapping of result codes
  (DAT-115...DAT-117), TSQLiteConnection (DAT-120...DAT-129), the type mapping (DAT-130...DAT-132) and
  TSQLiteQuery (DAT-140...DAT-157). No UI-framework unit is used. }
unit SQLiteData;

interface

uses
  SysUtils, Classes, DB, SQLite3API;

type
  ESQLiteError = class(EDatabaseError)
  private
    FErrorCode: Integer;
    FExtendedCode: Integer;
    FSQL: string;
  public
    constructor CreateCode(const Msg: string; AErrorCode, AExtendedCode: Integer; const ASQL: string);
    { Primary result code (the extended code and $FF); -1 = library problem; 0 = not a result code of SQLite. }
    property ErrorCode: Integer read FErrorCode;
    { Extended result code. }
    property ExtendedCode: Integer read FExtendedCode;
    { Statement text involved, '' if none. }
    property SQL: string read FSQL;
  end;

  TSQLiteOpenMode = (omCreateReadWrite, omReadWrite, omReadOnly);

  { How a registered dataset hears from its connection: Destroying = False while the connection closes
    (after the dataset was closed: release prepared statements), True when the connection is destroyed
    (forget the connection). }
  TSQLiteDetachEvent = procedure(Destroying: Boolean) of object;

  TSQLiteClient = record
    DataSet: TDataSet;
    Detach: TSQLiteDetachEvent;
  end;
  TSQLiteClients = array of TSQLiteClient;

  TSQLiteConnection = class(TComponent)
  private
    FHandle: Pointer;
    FDatabase: string;
    FOpenMode: TSQLiteOpenMode;
    FBusyTimeout: Integer;
    FForeignKeys: Boolean;
    FParams: TStrings;
    FLibraryName: string;
    FStreamedConnected: Boolean;
    FClients: TSQLiteClients;
    FBeforeConnect: TNotifyEvent;
    FAfterConnect: TNotifyEvent;
    FBeforeDisconnect: TNotifyEvent;
    FAfterDisconnect: TNotifyEvent;
    function GetConnected: Boolean;
    procedure SetConnected(Value: Boolean);
    procedure SetDatabase(const Value: string);
    procedure SetOpenMode(Value: TSQLiteOpenMode);
    procedure SetLibraryName(const Value: string);
    procedure SetBusyTimeout(Value: Integer);
    procedure SetForeignKeys(Value: Boolean);
    procedure SetParams(Value: TStrings);
    function IsLibraryNameStored: Boolean;
    function GetLibraryVersion: string;
    function GetDataSetCount: Integer;
    function GetDataSet(Index: Integer): TDataSet;
    procedure CheckClosed;
    procedure CheckConnected;
    procedure EnsureLibrary;
    function DatabasePath(out IsUri: Boolean): string;
    procedure ApplyForeignKeys;
    procedure ApplyParams;
    procedure QueryColumn(const SQL: string; Column: Integer; List: TStrings);
    function IndexOfDataSet(DataSet: TDataSet): Integer;
    procedure DetachClients(Destroying: Boolean);
  protected
    procedure Loaded; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure Open;
    procedure Close;
    { Runs every statement of a script; returns the total of rows changed. }
    function ExecSQL(const SQL: string): Integer;
    procedure StartTransaction;
    procedure Commit;
    procedure Rollback;
    function InTransaction: Boolean;
    procedure GetTableNames(List: TStrings);
    procedure GetFieldNames(const TableName: string; List: TStrings);
    function LastInsertRowId: Int64;
    { Attaches a dataset: Close closes it and then calls Detach(False); destroying the connection calls
      Detach(True) after that. Registering a dataset again only replaces its Detach. }
    procedure RegisterDataSet(DataSet: TDataSet; Detach: TSQLiteDetachEvent);
    procedure UnregisterDataSet(DataSet: TDataSet);
    property DataSetCount: Integer read GetDataSetCount;
    property DataSets[Index: Integer]: TDataSet read GetDataSet;
    { Psqlite3, nil when closed. }
    property Handle: Pointer read FHandle;
    { Loads the library if needed. }
    property LibraryVersion: string read GetLibraryVersion;
  published
    { File path, ':memory:' or a 'file:' URI. }
    property Database: string read FDatabase write SetDatabase;
    property Connected: Boolean read GetConnected write SetConnected default False;
    property OpenMode: TSQLiteOpenMode read FOpenMode write SetOpenMode default omCreateReadWrite;
    { Milliseconds; 0 = fail immediately. }
    property BusyTimeout: Integer read FBusyTimeout write SetBusyTimeout default 5000;
    property ForeignKeys: Boolean read FForeignKeys write SetForeignKeys default True;
    { Extra pragmas "name=value", one per line. }
    property Params: TStrings read FParams write SetParams;
    property LibraryName: string read FLibraryName write SetLibraryName stored IsLibraryNameStored;
    property BeforeConnect: TNotifyEvent read FBeforeConnect write FBeforeConnect;
    property AfterConnect: TNotifyEvent read FAfterConnect write FAfterConnect;
    property BeforeDisconnect: TNotifyEvent read FBeforeDisconnect write FBeforeDisconnect;
    property AfterDisconnect: TNotifyEvent read FAfterDisconnect write FAfterDisconnect;
  end;

  { One result column of a query in relation to its update table (DAT-151, DAT-157). }
  TSQLiteColumn = record
    Origin: string;       { the column of the update table the field reads; '' = an expression or another table }
    Updatable: Boolean;   { Origin is a stored (not generated) column of the update table }
    IsRowId: Boolean;     { the field reads the row id of the update table }
    InfoIndex: Integer;   { index of Origin in the column list of the update table, -1 = none }
  end;
  TSQLiteColumns = array of TSQLiteColumn;

  { A dataset over one SQL statement (DAT-140...DAT-157): all rows are fetched at Open; the result set is
    editable when it reads one table and contains a key of it. }
  TSQLiteQuery = class(TRowDataSet)
  private
    FConnection: TSQLiteConnection;
    FSQL: TStrings;
    FParams: TParams;
    FParamCheck: Boolean;
    FReadOnly: Boolean;
    FUpdateTable: string;
    FKeyFields: string;
    FStmt: Pointer;
    FRowsAffected: Integer;
    { DriverOpen ran: DriverInitFieldDefs works on the statement it prepared and bound }
    FOpening: Boolean;
    { the first step is done and its result (FStepRc) waits for DriverLoadRows }
    FStepped: Boolean;
    FStepRc: Integer;
    FColumns: TSQLiteColumns;
    { the update table, '' when unknown; the name of its row id in statements, '' when it has none }
    FTable: string;
    FRowIdName: string;
    { the result columns of the key }
    FKey: array of Integer;
    FReason: string;
    function GetPrepared: Boolean;
    procedure SetPrepared(Value: Boolean);
    procedure SetConnection(Value: TSQLiteConnection);
    procedure SetSQL(Value: TStrings);
    procedure SetUpdateTable(const Value: string);
    procedure SetKeyFields(const Value: string);
    function GetReadOnlyReason: string;
    procedure SQLChanged(Sender: TObject);
    procedure Detach(Destroying: Boolean);
    function StatementText: string;
    procedure CheckResultSet;
    procedure BindParams;
    { One step of the statement; an error resets the statement and is raised. }
    function Step: Integer;
    procedure FirstStep;
    procedure BuildFieldDefs;
    { The name of a mapped column in generated statements. }
    function ColumnRef(Index: Integer): string;
    { "k1 IS ?n AND k2 IS ?m" for the key columns, the first marker being First. }
    function KeyCondition(First: Integer): string;
    procedure BindKey(Stmt: Pointer; First: Integer; Row: TDataRow);
    procedure RereadInserted(Buffer: TDataRow);
  protected
    { Called before the SQL text is assigned and after any change of it. Default: nothing. }
    procedure CheckSQLChange; virtual;
    procedure DriverOpen; override;
    procedure DriverInitFieldDefs; override;
    procedure DriverLoadRows; override;
    procedure DriverClose; override;
    function DriverCanModify: Boolean; override;
    function DriverFieldUpdatable(Field: TField): Boolean; override;
    procedure ApplyInsert(Buffer: TDataRow); override;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); override;
    procedure ApplyDelete(Row: TDataRow); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { Compiles the statement; a closed connection is opened. Nothing to do when prepared. }
    procedure Prepare; virtual;
    procedure Unprepare;
    { Runs a statement without a result set (rows of a result set are ignored). }
    procedure ExecSQL; virtual;
    function ParamByName(const Name: string): TParam;
    property Prepared: Boolean read GetPrepared write SetPrepared;
    { Rows changed by the last ExecSQL; -1 before any. }
    property RowsAffected: Integer read FRowsAffected;
    { Why the open query cannot be modified; '' when it can. }
    property ReadOnlyReason: string read GetReadOnlyReason;
  published
    property Connection: TSQLiteConnection read FConnection write SetConnection;
    property SQL: TStrings read FSQL write SetSQL;
    property Params: TParams read FParams;
    property ParamCheck: Boolean read FParamCheck write FParamCheck default True;
    property ReadOnly: Boolean read FReadOnly write FReadOnly default False;
    { '' = detected from the result columns }
    property UpdateTable: string read FUpdateTable write SetUpdateTable;
    { 'a;b'; '' = the primary key or the row id }
    property KeyFields: string read FKeyFields write SetKeyFields;
    property Active;
    property Filter;
    property Filtered;
    property FilterOptions;
    property BeforeOpen;
    property AfterOpen;
    property BeforeClose;
    property AfterClose;
    property BeforeScroll;
    property AfterScroll;
    property BeforeInsert;
    property AfterInsert;
    property BeforeEdit;
    property AfterEdit;
    property BeforePost;
    property AfterPost;
    property BeforeCancel;
    property AfterCancel;
    property BeforeDelete;
    property AfterDelete;
    property BeforeRefresh;
    property AfterRefresh;
    property OnNewRecord;
    property OnPostError;
    property OnDeleteError;
    property OnEditError;
    property OnCalcFields;
    property OnFilterRecord;
  end;

  { A dataset over all rows of one table (DAT-160...DAT-162): the statement is generated from TableName
    and IndexFieldNames; a table without primary key is edited through its row id, read into the hidden
    first field "rowid". SQL, UpdateTable and KeyFields are managed by the table. }
  TSQLiteTable = class(TSQLiteQuery)
  private
    FTableName: string;
    FIndexFieldNames: string;
    { the statement text the table generated last }
    FGeneratedSQL: string;
    { the table itself assigns the SQL text }
    FGenerating: Boolean;
    { the statement reads the row id into the first column }
    FHiddenRowId: Boolean;
    procedure SetTableName(const Value: string);
    procedure SetIndexFieldNames(const Value: string);
    procedure GenerateSQL;
  protected
    procedure CheckSQLChange; override;
    procedure DriverOpen; override;
    procedure DriverInitFieldDefs; override;
    procedure DriverLoadRows; override;
  public
    procedure Prepare; override;
    { Not supported: raises EDatabaseError. }
    procedure ExecSQL; override;
  published
    property TableName: string read FTableName write SetTableName;
    { 'a;b' orders the rows by these columns; '' = row id order }
    property IndexFieldNames: string read FIndexFieldNames write SetIndexFieldNames;
    property SQL stored False;
    property UpdateTable stored False;
    property KeyFields stored False;
  end;

{ The exception for a result code (DAT-115): message "SQLite error <extended code>: <text>", the text and
  the extended code from the connection handle, or the generic text of Rc when Db is nil. Build it before
  the failing statement is reset or finalized, raise it afterwards (DAT-116). }
function SQLiteErrorFor(Db: Psqlite3; Rc: Integer; const SQL: string): ESQLiteError;
{ Raises SQLiteErrorFor(...) unless Rc is SQLITE_OK, SQLITE_ROW or SQLITE_DONE. }
procedure SQLiteCheck(Db: Psqlite3; Rc: Integer; const SQL: string);
{ An identifier for generated SQL (DAT-129): in double quotes, every double quote doubled. }
function SQLiteQuoteIdent(const Name: string): string;

implementation

{$POINTERMATH ON}

const
  SSqlEmptyName = 'Database name is empty';
  SSqlConnected = 'Cannot change this property while connected';
  SSqlClosed = 'Connection is closed';
  SSqlPragmaName = 'Invalid pragma name "%s"';
  SSqlInTransaction = 'A transaction is already active';
  SSqlNoTransaction = 'No transaction is active';
  SSqlError = 'SQLite error %d: %s';

{ ---------------------------------------------------------------------------------------------- errors }

constructor ESQLiteError.CreateCode(const Msg: string; AErrorCode, AExtendedCode: Integer; const ASQL: string);
begin
  inherited Create(Msg);
  FErrorCode := AErrorCode;
  FExtendedCode := AExtendedCode;
  FSQL := ASQL;
end;

function SQLiteErrorFor(Db: Psqlite3; Rc: Integer; const SQL: string): ESQLiteError;
var
  Ext: Integer;
  Msg: string;
begin
  Ext := Rc;
  Msg := '';
  if Db <> nil then
  begin
    Ext := sqlite3_extended_errcode(Db);
    Msg := Utf8ToUtf16(sqlite3_errmsg(Db));
  end
  else if Assigned(sqlite3_errstr) then
    Msg := Utf8ToUtf16(sqlite3_errstr(Rc));
  Result := ESQLiteError.CreateCode(Format(SSqlError, [Ext, Msg]), Ext and $FF, Ext, SQL);
end;

procedure SQLiteCheck(Db: Psqlite3; Rc: Integer; const SQL: string);
begin
  if (Rc <> SQLITE_OK) and (Rc <> SQLITE_ROW) and (Rc <> SQLITE_DONE) then
    raise SQLiteErrorFor(Db, Rc, SQL);
end;

function SQLiteQuoteIdent(const Name: string): string;
begin
  Result := '"' + StringReplace(Name, '"', '""', [rfReplaceAll]) + '"';
end;

{ ------------------------------------------------------------------------------------------ script text }

function IsSpaceByte(B: Byte): Boolean;
begin
  Result := (B = 32) or ((B >= 9) and (B <= 13));
end;

{ True when only white space follows up to the terminator. }
function IsBlank(P: PByte): Boolean;
var
  I: NativeInt;
begin
  I := 0;
  while IsSpaceByte(P[I]) do
    Inc(I);
  Result := P[I] = 0;
end;

{ Length in bytes of the first statement of a script, without its semicolon: up to the first semicolon
  outside string literals, quoted identifiers and comments. Only used to name a statement that does not
  compile. }
function StatementLength(P: PByte): NativeInt;
var
  I: NativeInt;
  Q: Byte;
begin
  I := 0;
  while (P[I] <> 0) and (P[I] <> Ord(';')) do
  begin
    Q := 0;
    if (P[I] = Ord('''')) or (P[I] = Ord('"')) or (P[I] = Ord('`')) then
      Q := P[I]
    else if P[I] = Ord('[') then
      Q := Ord(']');
    if Q <> 0 then
    begin
      Inc(I);
      while (P[I] <> 0) and (P[I] <> Q) do
        Inc(I);
      if P[I] <> 0 then
        Inc(I);
    end
    else if (P[I] = Ord('-')) and (P[I + 1] = Ord('-')) then
    begin
      while (P[I] <> 0) and (P[I] <> 10) do
        Inc(I);
    end
    else if (P[I] = Ord('/')) and (P[I + 1] = Ord('*')) then
    begin
      Inc(I, 2);
      while (P[I] <> 0) and not ((P[I] = Ord('*')) and (P[I + 1] = Ord('/'))) do
        Inc(I);
      if P[I] <> 0 then
        Inc(I, 2);
    end
    else
      Inc(I);
  end;
  Result := I;
end;

{ The statement in Len bytes at P as text: trimmed, without its terminating semicolon. }
function StatementText(P: PByte; Len: NativeInt): string;
begin
  Result := Trim(Utf8ToUtf16Len(PUtf8(P), Len));
  if (Result <> '') and (Result[Length(Result)] = ';') then
    Result := Trim(Copy(Result, 1, Length(Result) - 1));
end;

function IsPragmaName(const Name: string): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := Name <> '';
  for I := 1 to Length(Name) do
  begin
    C := Name[I];
    if not (((C >= 'A') and (C <= 'Z')) or ((C >= 'a') and (C <= 'z')) or (C = '_') or
      ((I > 1) and (C >= '0') and (C <= '9'))) then
      Result := False;
  end;
end;

{ ------------------------------------------------------------------------------------ TSQLiteConnection }

constructor TSQLiteConnection.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FParams := TStringList.Create;
  FOpenMode := omCreateReadWrite;
  FBusyTimeout := 5000;
  FForeignKeys := True;
  FLibraryName := SQLiteDefaultLibraryName;
end;

destructor TSQLiteConnection.Destroy;
begin
  Close;
  DetachClients(True);
  FParams.Free;
  inherited Destroy;
end;

procedure TSQLiteConnection.Loaded;
begin
  inherited Loaded;
  if FStreamedConnected then
  begin
    FStreamedConnected := False;
    Open;
  end;
end;

function TSQLiteConnection.GetConnected: Boolean;
begin
  Result := FHandle <> nil;
end;

procedure TSQLiteConnection.SetConnected(Value: Boolean);
begin
  if Value and (csLoading in ComponentState) then
    FStreamedConnected := True
  else if Value then
    Open
  else
    Close;
end;

procedure TSQLiteConnection.CheckClosed;
begin
  if FHandle <> nil then
    raise ESQLiteError.Create(SSqlConnected);
end;

procedure TSQLiteConnection.CheckConnected;
begin
  if FHandle = nil then
    raise ESQLiteError.Create(SSqlClosed);
end;

procedure TSQLiteConnection.SetDatabase(const Value: string);
begin
  if Value <> FDatabase then
  begin
    CheckClosed;
    FDatabase := Value;
  end;
end;

procedure TSQLiteConnection.SetOpenMode(Value: TSQLiteOpenMode);
begin
  if Value <> FOpenMode then
  begin
    CheckClosed;
    FOpenMode := Value;
  end;
end;

procedure TSQLiteConnection.SetLibraryName(const Value: string);
begin
  if Value <> FLibraryName then
  begin
    CheckClosed;
    FLibraryName := Value;
  end;
end;

function TSQLiteConnection.IsLibraryNameStored: Boolean;
begin
  Result := FLibraryName <> SQLiteDefaultLibraryName;
end;

procedure TSQLiteConnection.SetBusyTimeout(Value: Integer);
begin
  FBusyTimeout := Value;
  if FHandle <> nil then
    SQLiteCheck(FHandle, sqlite3_busy_timeout(FHandle, FBusyTimeout), '');
end;

procedure TSQLiteConnection.SetForeignKeys(Value: Boolean);
begin
  FForeignKeys := Value;
  if FHandle <> nil then
    ApplyForeignKeys;
end;

procedure TSQLiteConnection.SetParams(Value: TStrings);
begin
  FParams.Assign(Value);
end;

procedure TSQLiteConnection.EnsureLibrary;
var
  Reason: string;
begin
  if not SQLiteLoad(FLibraryName, Reason) then
    raise ESQLiteError.CreateCode(Reason, -1, -1, '');
end;

function TSQLiteConnection.GetLibraryVersion: string;
begin
  EnsureLibrary;
  Result := Utf8ToUtf16(sqlite3_libversion());
end;

function TSQLiteConnection.DatabasePath(out IsUri: Boolean): string;
begin
  IsUri := Copy(FDatabase, 1, 5) = 'file:';
  if IsUri or (FDatabase = ':memory:') then
    Result := FDatabase
  else if IsRelativePath(FDatabase) and not ((Length(FDatabase) >= 2) and (FDatabase[2] = ':')) then
    { against the directory of the executable, not the current directory (DAT-121) }
    Result := ExpandFileName(ExtractFilePath(ParamStr(0)) + FDatabase)
  else
    Result := ExpandFileName(FDatabase);
end;

procedure TSQLiteConnection.ApplyForeignKeys;
begin
  if FForeignKeys then
    ExecSQL('PRAGMA foreign_keys = ON')
  else
    ExecSQL('PRAGMA foreign_keys = OFF');
end;

procedure TSQLiteConnection.ApplyParams;
var
  I, Eq: Integer;
  Line, Name: string;
begin
  for I := 0 to FParams.Count - 1 do
  begin
    Line := Trim(FParams[I]);
    if Line <> '' then
    begin
      Eq := Pos('=', Line);
      if Eq = 0 then
        { a line without a value is not a "name=value" line }
        raise ESQLiteError.CreateFmt(SSqlPragmaName, [Line]);
      Name := Trim(Copy(Line, 1, Eq - 1));
      if not IsPragmaName(Name) then
        raise ESQLiteError.CreateFmt(SSqlPragmaName, [Name]);
      ExecSQL('PRAGMA ' + Name + ' = ' + Trim(Copy(Line, Eq + 1, Length(Line))));
    end;
  end;
end;

procedure TSQLiteConnection.Open;
var
  Path: TBytes;
  IsUri: Boolean;
  Flags, Rc: Integer;
  Db: Psqlite3;
  Error: ESQLiteError;
begin
  if FHandle <> nil then
    Exit;
  if FDatabase = '' then
    raise ESQLiteError.Create(SSqlEmptyName);
  if Assigned(FBeforeConnect) then
    FBeforeConnect(Self);
  EnsureLibrary;
  Path := Utf16ToUtf8(DatabasePath(IsUri));
  case FOpenMode of
    omReadWrite: Flags := SQLITE_OPEN_READWRITE;
    omReadOnly: Flags := SQLITE_OPEN_READONLY;
  else
    Flags := SQLITE_OPEN_READWRITE or SQLITE_OPEN_CREATE;
  end;
  Flags := Flags or SQLITE_OPEN_FULLMUTEX;
  if IsUri then
    Flags := Flags or SQLITE_OPEN_URI;
  Rc := sqlite3_open_v2(PUtf8(@Path[0]), Db, Flags, nil);
  if Rc <> SQLITE_OK then
  begin
    { the handle of a failed open still has the message and must still be closed }
    Error := SQLiteErrorFor(Db, Rc, '');
    if Db <> nil then
      sqlite3_close_v2(Db);
    raise Error;
  end;
  FHandle := Db;
  try
    SQLiteCheck(FHandle, sqlite3_extended_result_codes(FHandle, 1), '');
    SQLiteCheck(FHandle, sqlite3_busy_timeout(FHandle, FBusyTimeout), '');
    ApplyForeignKeys;
    ApplyParams;
  except
    FHandle := nil;
    sqlite3_close_v2(Db);
    raise;
  end;
  if Assigned(FAfterConnect) then
    FAfterConnect(Self);
end;

procedure TSQLiteConnection.DetachClients(Destroying: Boolean);
var
  Clients: TSQLiteClients;
  I: Integer;
begin
  { a snapshot: a client may unregister itself from the call }
  SetLength(Clients, Length(FClients));
  for I := 0 to Length(FClients) - 1 do
    Clients[I] := FClients[I];
  if Destroying then
    FClients := nil;
  for I := 0 to Length(Clients) - 1 do
    if Assigned(Clients[I].Detach) then
      Clients[I].Detach(Destroying);
end;

procedure TSQLiteConnection.Close;
var
  DataSets: array of TDataSet;
  I: Integer;
  Db: Psqlite3;
begin
  if FHandle = nil then
    Exit;
  if Assigned(FBeforeDisconnect) then
    FBeforeDisconnect(Self);
  SetLength(DataSets, Length(FClients));
  for I := 0 to Length(FClients) - 1 do
    DataSets[I] := FClients[I].DataSet;
  for I := 0 to Length(DataSets) - 1 do
    if IndexOfDataSet(DataSets[I]) >= 0 then
      DataSets[I].Close;
  DetachClients(False);
  try
    if InTransaction then
      ExecSQL('ROLLBACK');
  finally
    Db := FHandle;
    FHandle := nil;
    sqlite3_close_v2(Db);
  end;
  if Assigned(FAfterDisconnect) then
    FAfterDisconnect(Self);
end;

function TSQLiteConnection.ExecSQL(const SQL: string): Integer;
var
  Script: TBytes;
  P: PByte;
  Tail: PUtf8;
  Stmt: Psqlite3_stmt;
  Rc, Before: Integer;
  Error: ESQLiteError;
begin
  CheckConnected;
  Result := 0;
  Script := Utf16ToUtf8(SQL);
  P := @Script[0];
  while True do
  begin
    Rc := sqlite3_prepare_v2(FHandle, PUtf8(P), -1, Stmt, Tail);
    if Rc <> SQLITE_OK then
      raise SQLiteErrorFor(FHandle, Rc, StatementText(P, StatementLength(P)));
    if Stmt = nil then
    begin
      { nothing to run in this piece: an empty statement or the end of the script }
      if (Tail = nil) or (PByte(Tail) = P) or IsBlank(PByte(Tail)) then
        Break;
      P := PByte(Tail);
      Continue;
    end;
    Before := sqlite3_total_changes(FHandle);
    repeat
      Rc := sqlite3_step(Stmt);
    until Rc <> SQLITE_ROW;
    if Rc <> SQLITE_DONE then
    begin
      Error := SQLiteErrorFor(FHandle, Rc, StatementText(P, NativeInt(NativeUInt(Tail) - NativeUInt(P))));
      sqlite3_finalize(Stmt);
      raise Error;
    end;
    { sqlite3_changes keeps the count of the last INSERT, UPDATE or DELETE, so it is read only after a
      statement that changed rows itself }
    if (sqlite3_stmt_readonly(Stmt) = 0) and (sqlite3_total_changes(FHandle) <> Before) then
      Inc(Result, sqlite3_changes(FHandle));
    sqlite3_finalize(Stmt);
    P := PByte(Tail);
  end;
end;

procedure TSQLiteConnection.StartTransaction;
begin
  CheckConnected;
  if InTransaction then
    raise ESQLiteError.Create(SSqlInTransaction);
  ExecSQL('BEGIN');
end;

procedure TSQLiteConnection.Commit;
begin
  if not InTransaction then
    raise ESQLiteError.Create(SSqlNoTransaction);
  ExecSQL('COMMIT');
end;

procedure TSQLiteConnection.Rollback;
begin
  if not InTransaction then
    raise ESQLiteError.Create(SSqlNoTransaction);
  ExecSQL('ROLLBACK');
end;

function TSQLiteConnection.InTransaction: Boolean;
begin
  Result := (FHandle <> nil) and (sqlite3_get_autocommit(FHandle) = 0);
end;

procedure TSQLiteConnection.QueryColumn(const SQL: string; Column: Integer; List: TStrings);
var
  Text: TBytes;
  Stmt: Psqlite3_stmt;
  Tail: PUtf8;
  Rc: Integer;
  Value: string;
  P: Pointer;
  Error: ESQLiteError;
begin
  CheckConnected;
  List.Clear;
  Text := Utf16ToUtf8(SQL);
  SQLiteCheck(FHandle, sqlite3_prepare_v2(FHandle, PUtf8(@Text[0]), -1, Stmt, Tail), SQL);
  Rc := sqlite3_step(Stmt);
  while Rc = SQLITE_ROW do
  begin
    P := sqlite3_column_text16(Stmt, Column);
    SetString(Value, PChar(P), sqlite3_column_bytes16(Stmt, Column) div 2);
    List.Add(Value);
    Rc := sqlite3_step(Stmt);
  end;
  if Rc <> SQLITE_DONE then
  begin
    Error := SQLiteErrorFor(FHandle, Rc, SQL);
    sqlite3_finalize(Stmt);
    raise Error;
  end;
  sqlite3_finalize(Stmt);
end;

procedure TSQLiteConnection.GetTableNames(List: TStrings);
begin
  QueryColumn('SELECT name FROM sqlite_master WHERE type = ''table'' AND name NOT LIKE ''sqlite_%'' ORDER BY name',
    0, List);
end;

procedure TSQLiteConnection.GetFieldNames(const TableName: string; List: TStrings);
begin
  QueryColumn('PRAGMA table_info(' + SQLiteQuoteIdent(TableName) + ')', 1, List);
end;

function TSQLiteConnection.LastInsertRowId: Int64;
begin
  CheckConnected;
  Result := sqlite3_last_insert_rowid(FHandle);
end;

function TSQLiteConnection.IndexOfDataSet(DataSet: TDataSet): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to Length(FClients) - 1 do
    if FClients[I].DataSet = DataSet then
    begin
      Result := I;
      Exit;
    end;
end;

procedure TSQLiteConnection.RegisterDataSet(DataSet: TDataSet; Detach: TSQLiteDetachEvent);
var
  I: Integer;
begin
  I := IndexOfDataSet(DataSet);
  if I < 0 then
  begin
    I := Length(FClients);
    SetLength(FClients, I + 1);
    FClients[I].DataSet := DataSet;
  end;
  FClients[I].Detach := Detach;
end;

procedure TSQLiteConnection.UnregisterDataSet(DataSet: TDataSet);
var
  I, J: Integer;
  Rest: TSQLiteClients;
begin
  I := IndexOfDataSet(DataSet);
  if I < 0 then
    Exit;
  SetLength(Rest, Length(FClients) - 1);
  for J := 0 to Length(Rest) - 1 do
    if J < I then
      Rest[J] := FClients[J]
    else
      Rest[J] := FClients[J + 1];
  FClients := Rest;
end;

function TSQLiteConnection.GetDataSetCount: Integer;
begin
  Result := Length(FClients);
end;

function TSQLiteConnection.GetDataSet(Index: Integer): TDataSet;
begin
  Result := FClients[Index].DataSet;
end;

{ ------------------------------------------------------------------------------------------- statements }

const
  SQLITE_NOMEM = 7;
  MSecsPerDay = 86400000;
  { TDateTime of 1970-01-01 and the Julian day number of TDateTime 0 }
  UnixEpochDay = 25569;
  JulianDayZero = 2415018.5;

var
  { the text of an empty string value: a zero word that stays valid }
  ZeroWord: Word;

{ Compiles a generated statement. }
function PrepareOne(Db: Psqlite3; const SQL: string): Psqlite3_stmt;
var
  Text: TBytes;
  Tail: PUtf8;
  Stmt: Psqlite3_stmt;
begin
  Text := Utf16ToUtf8(SQL);
  SQLiteCheck(Db, sqlite3_prepare_v2(Db, PUtf8(@Text[0]), -1, Stmt, Tail), SQL);
  Result := Stmt;
end;

{ Steps a generated statement to its end and finalizes it, also on failure (DAT-116); rows are discarded. }
procedure RunToEnd(Db: Psqlite3; Stmt: Psqlite3_stmt; const SQL: string);
var
  Rc: Integer;
  Error: ESQLiteError;
begin
  repeat
    Rc := sqlite3_step(Stmt);
  until Rc <> SQLITE_ROW;
  if Rc <> SQLITE_DONE then
  begin
    Error := SQLiteErrorFor(Db, Rc, SQL);
    sqlite3_finalize(Stmt);
    raise Error;
  end;
  sqlite3_finalize(Stmt);
end;

function Digits(Value, Width: Integer): string;
begin
  Result := IntToStr(Value);
  while Length(Result) < Width do
    Result := '0' + Result;
end;

{ The stored text of a date, time or date-time value (DAT-132): YYYY-MM-DD, HH:MM:SS[.SSS] or both,
  Gregorian, independent of the locale. The time of day is the absolute fraction of the value. }
function DateTimeText(DataType: TFieldType; Value: Double): string;
var
  Day: Double;
  M: Int64;
  MSecs: Integer;
  Y, Mo, D: Word;
  DateText, TimeText: string;
begin
  Day := Int(Value);
  M := Round(Abs(Value - Day) * MSecsPerDay);
  if M >= MSecsPerDay then
  begin
    M := M - MSecsPerDay;
    Day := Day + 1;
  end;
  MSecs := Integer(M);
  DecodeDate(Day, Y, Mo, D);
  DateText := Digits(Y, 4) + '-' + Digits(Mo, 2) + '-' + Digits(D, 2);
  TimeText := Digits(MSecs div 3600000, 2) + ':' + Digits((MSecs div 60000) mod 60, 2) + ':' +
    Digits((MSecs div 1000) mod 60, 2);
  if MSecs mod 1000 <> 0 then
    TimeText := TimeText + '.' + Digits(MSecs mod 1000, 3);
  case DataType of
    ftDate: Result := DateText;
    ftTime: Result := TimeText;
  else
    Result := DateText + ' ' + TimeText;
  end;
end;

{ Binds one value (DAT-132). The value is in the canonical kind of DataType, or null. }
procedure BindValue(Db: Psqlite3; Stmt: Psqlite3_stmt; Index: Integer; DataType: TFieldType; const V: TDBValue);
var
  Rc: Integer;
  S: string;
  IsText: Boolean;
  Utf8: TBytes;
begin
  Rc := SQLITE_OK;
  IsText := False;
  S := '';
  case V.Kind of
    vkNull:
      Rc := sqlite3_bind_null(Stmt, Index);
    vkInt:
      if DataType = ftBoolean then
        Rc := sqlite3_bind_int64(Stmt, Index, Ord(V.IntVal <> 0))
      else
        Rc := sqlite3_bind_int64(Stmt, Index, V.IntVal);
    vkFloat:
      if DataType in [ftDate, ftTime, ftDateTime] then
      begin
        S := DateTimeText(DataType, V.FloatVal);
        IsText := True;
      end
      else
        Rc := sqlite3_bind_double(Stmt, Index, V.FloatVal);
    vkString:
      begin
        S := V.StrVal;
        IsText := True;
      end;
    vkBytes:
      if Length(V.BytesVal) = 0 then
        Rc := sqlite3_bind_zeroblob(Stmt, Index, 0)
      else
        Rc := sqlite3_bind_blob(Stmt, Index, @V.BytesVal[0], Length(V.BytesVal), SQLITE_TRANSIENT);
  end;
  if IsText then
  begin
    { the length is in bytes, never -1: an embedded #0 is kept; an empty string is empty TEXT, not NULL }
    if S = '' then
      Rc := sqlite3_bind_text16(Stmt, Index, @ZeroWord, 0, SQLITE_STATIC)
    else if (S[1] = #$FEFF) or (S[1] = #$FFFE) then
    begin
      { SQLite takes these as the byte order mark of UTF-16 text: it would drop the first and swap the
        bytes of the whole text after the second. UTF-8 text is stored as it is. }
      Utf8 := Utf16ToUtf8(S);
      Rc := sqlite3_bind_text(Stmt, Index, PUtf8(@Utf8[0]), Length(Utf8) - 1, SQLITE_TRANSIENT);
    end
    else
      Rc := sqlite3_bind_text16(Stmt, Index, Pointer(PChar(S)), Length(S) * 2, SQLITE_TRANSIENT);
  end;
  SQLiteCheck(Db, Rc, '');
end;

{ The value and the type a parameter is bound with; unbound or null: null. }
function ParamValue(P: TParam; out DataType: TFieldType): TDBValue;
begin
  DataType := P.DataType;
  Result := NullValue;
  if not P.Bound or P.IsNull then
    Exit;
  case P.DataType of
    ftString, ftMemo:
      Result := StringValue(P.AsString);
    ftSmallint, ftInteger, ftWord, ftLargeint, ftAutoInc:
      Result := IntValue(P.AsLargeInt);
    ftBoolean:
      Result := IntValue(Ord(P.AsBoolean));
    ftFloat, ftCurrency:
      Result := FloatValue(P.AsFloat);
    ftDate, ftTime, ftDateTime:
      Result := FloatValue(P.AsDateTime);
    ftBlob:
      Result := BytesValue(P.AsBytes);
  end;
end;

{ -------------------------------------------------------------------------------------- reading columns }

{ The text of a column of the current row (DAT-112). }
function ColumnText(Db: Psqlite3; Stmt: Psqlite3_stmt; Col: Integer): string;
var
  P: Pointer;
  N: Integer;
begin
  P := sqlite3_column_text16(Stmt, Col);
  N := sqlite3_column_bytes16(Stmt, Col);
  Result := '';
  if P = nil then
  begin
    if sqlite3_errcode(Db) = SQLITE_NOMEM then
      raise SQLiteErrorFor(Db, SQLITE_NOMEM, '');
    Exit;
  end;
  SetString(Result, PChar(P), N div 2);
end;

function ColumnBytes(Db: Psqlite3; Stmt: Psqlite3_stmt; Col: Integer): TBytes;
var
  P: Pointer;
  N: Integer;
begin
  P := sqlite3_column_blob(Stmt, Col);
  N := sqlite3_column_bytes(Stmt, Col);
  Result := nil;
  if N <= 0 then
    Exit;
  if P = nil then
    raise SQLiteErrorFor(Db, SQLITE_NOMEM, '');
  SetLength(Result, N);
  Move(P^, Result[0], N);
end;

{ Count decimal digits at S[I]; I moves behind them. }
function ParseDigits(const S: string; var I: Integer; Count: Integer; out Value: Integer): Boolean;
var
  K: Integer;
begin
  Result := False;
  Value := 0;
  if I + Count - 1 > Length(S) then
    Exit;
  for K := 0 to Count - 1 do
  begin
    if (S[I + K] < '0') or (S[I + K] > '9') then
      Exit;
    Value := Value * 10 + (Ord(S[I + K]) - Ord('0'));
  end;
  I := I + Count;
  Result := True;
end;

{ HH:MM[:SS[.S...]] with an optional zone (Z or +HH:MM / -HH:MM, ignored) up to the end of S. }
function ParseIsoTime(const S: string; I: Integer; out Time: TDateTime): Boolean;
var
  H, M, Sec, MS, Scale, Zone: Integer;
begin
  Result := False;
  Time := 0;
  Sec := 0;
  MS := 0;
  if not ParseDigits(S, I, 2, H) then
    Exit;
  if (I > Length(S)) or (S[I] <> ':') then
    Exit;
  Inc(I);
  if not ParseDigits(S, I, 2, M) then
    Exit;
  if (I <= Length(S)) and (S[I] = ':') then
  begin
    Inc(I);
    if not ParseDigits(S, I, 2, Sec) then
      Exit;
    if (I <= Length(S)) and (S[I] = '.') then
    begin
      Inc(I);
      if (I > Length(S)) or (S[I] < '0') or (S[I] > '9') then
        Exit;
      Scale := 100;
      while (I <= Length(S)) and (S[I] >= '0') and (S[I] <= '9') do
      begin
        { digits beyond the milliseconds are dropped }
        MS := MS + (Ord(S[I]) - Ord('0')) * Scale;
        Scale := Scale div 10;
        Inc(I);
      end;
    end;
  end;
  if (I <= Length(S)) and (S[I] = 'Z') then
    Inc(I)
  else if (I <= Length(S)) and ((S[I] = '+') or (S[I] = '-')) then
  begin
    Inc(I);
    if not ParseDigits(S, I, 2, Zone) then
      Exit;
    if (I > Length(S)) or (S[I] <> ':') then
      Exit;
    Inc(I);
    if not ParseDigits(S, I, 2, Zone) then
      Exit;
  end;
  if (I <= Length(S)) or (H > 23) or (M > 59) or (Sec > 59) then
    Exit;
  Result := TryEncodeTime(H, M, Sec, MS, Time);
end;

{ The ISO-8601 forms of DAT-131: a date, a time, or a date and a time separated by a space or T. The
  parts that are absent are 0. }
function ParseIso(const Text: string; out DatePart, TimePart: Double): Boolean;
var
  S: string;
  I, Y, M, D: Integer;
  Value: TDateTime;
begin
  Result := False;
  DatePart := 0;
  TimePart := 0;
  S := Trim(Text);
  I := 1;
  if (Length(S) >= 5) and (S[5] = '-') then
  begin
    if not ParseDigits(S, I, 4, Y) then
      Exit;
    Inc(I);
    if not ParseDigits(S, I, 2, M) then
      Exit;
    if (I > Length(S)) or (S[I] <> '-') then
      Exit;
    Inc(I);
    if not ParseDigits(S, I, 2, D) then
      Exit;
    if (Y < 1) or not TryEncodeDate(Y, M, D, Value) then
      Exit;
    DatePart := Value;
    if I > Length(S) then
    begin
      Result := True;
      Exit;
    end;
    if (S[I] <> ' ') and (S[I] <> 'T') then
      Exit;
    Inc(I);
  end;
  Result := ParseIsoTime(S, I, Value);
  if Result then
    TimePart := Value;
end;

function TimeOfMSecs(MSecs: Int64): Double;
var
  Value: TDateTime;
begin
  Value := 0;
  TryEncodeTime(Integer(MSecs div 3600000), Integer((MSecs div 60000) mod 60), Integer((MSecs div 1000) mod 60),
    Integer(MSecs mod 1000), Value);
  Result := Value;
end;

{ A column of the current row as the value of a field of type DataType (DAT-131). }
function ReadColumn(Db: Psqlite3; Stmt: Psqlite3_stmt; Col: Integer; DataType: TFieldType;
  const FieldName: string): TDBValue;
var
  Storage: Integer;
  I, Days, Rest: Int64;
  F, DatePart, TimePart: Double;
  S: string;

  { the storage class is read before any value: the text is what SQLite renders }
  procedure ConvertError(const Kind: string);
  var
    Shown: string;
  begin
    if Storage = SQLITE_BLOB then
      Shown := '(Blob)'
    else
      Shown := ColumnText(Db, Stmt, Col);
    DatabaseErrorFmt(SDatFieldConvert, [Shown, Kind, FieldName]);
  end;

begin
  Result := NullValue;
  Storage := sqlite3_column_type(Stmt, Col);
  if Storage = SQLITE_NULL then
    Exit;
  case DataType of
    ftSmallint, ftInteger, ftWord, ftAutoInc, ftLargeint:
      begin
        I := 0;
        case Storage of
          SQLITE_INTEGER:
            I := sqlite3_column_int64(Stmt, Col);
          SQLITE_FLOAT:
            begin
              F := sqlite3_column_double(Stmt, Col);
              if not (Abs(F) < 1e300) or (F <> Int(F)) then
                ConvertError('integer');
              if Abs(F) >= 9.2233720368547758e18 then
                DatabaseErrorFmt(SDatFieldRange, [FieldName]);
              I := Trunc(F);
            end;
          SQLITE_TEXT:
            if not TryStrToInt64(ColumnText(Db, Stmt, Col), I) then
              ConvertError('integer');
        else
          ConvertError('integer');
        end;
        if (DataType <> ftLargeint) and ((I < Low(Integer)) or (I > High(Integer))) then
          DatabaseErrorFmt(SDatFieldRange, [FieldName]);
        Result := IntValue(I);
      end;
    ftBoolean:
      case Storage of
        SQLITE_INTEGER:
          Result := IntValue(Ord(sqlite3_column_int64(Stmt, Col) <> 0));
        SQLITE_FLOAT:
          Result := IntValue(Ord(sqlite3_column_double(Stmt, Col) <> 0));
        SQLITE_TEXT:
          begin
            S := ColumnText(Db, Stmt, Col);
            if SameText(S, 'true') or SameText(S, 't') or SameText(S, 'yes') or (S = '1') then
              Result := IntValue(1)
            else if SameText(S, 'false') or SameText(S, 'f') or SameText(S, 'no') or (S = '0') then
              Result := IntValue(0)
            else
              ConvertError('boolean');
          end;
      else
        ConvertError('boolean');
      end;
    ftFloat, ftCurrency:
      case Storage of
        SQLITE_INTEGER, SQLITE_FLOAT:
          Result := FloatValue(sqlite3_column_double(Stmt, Col));
        SQLITE_TEXT:
          begin
            if not TryStrToFloat(ColumnText(Db, Stmt, Col), F, InvariantFormatSettings) then
              ConvertError('floating-point');
            Result := FloatValue(F);
          end;
      else
        ConvertError('floating-point');
      end;
    ftString, ftMemo:
      Result := StringValue(ColumnText(Db, Stmt, Col));
    ftBlob:
      Result := BytesValue(ColumnBytes(Db, Stmt, Col));
    ftDate, ftTime, ftDateTime:
      begin
        DatePart := 0;
        TimePart := 0;
        case Storage of
          SQLITE_TEXT:
            if not ParseIso(ColumnText(Db, Stmt, Col), DatePart, TimePart) then
              ConvertError('date-time');
          SQLITE_INTEGER:
            begin
              { Unix seconds, UTC }
              I := sqlite3_column_int64(Stmt, Col);
              if (I > 400000000000) or (I < -400000000000) then
                ConvertError('date-time');
              Days := I div 86400;
              Rest := I mod 86400;
              if Rest < 0 then
              begin
                Rest := Rest + 86400;
                Days := Days - 1;
              end;
              DatePart := UnixEpochDay + Days;
              TimePart := TimeOfMSecs(Rest * 1000);
            end;
          SQLITE_FLOAT:
            begin
              { Julian day number }
              F := sqlite3_column_double(Stmt, Col) - JulianDayZero;
              if not (Abs(F) < 5000000) then
                ConvertError('date-time');
              DatePart := Int(F);
              if DatePart > F then
                DatePart := DatePart - 1;
              I := Round((F - DatePart) * MSecsPerDay);
              if I >= MSecsPerDay then
              begin
                I := I - MSecsPerDay;
                DatePart := DatePart + 1;
              end;
              TimePart := TimeOfMSecs(I);
            end;
        else
          ConvertError('date-time');
        end;
        case DataType of
          ftDate: Result := FloatValue(DatePart);
          ftTime: Result := FloatValue(TimePart);
        else
          { before day 0 the time of day is the absolute fraction }
          if DatePart >= 0 then
            Result := FloatValue(DatePart + TimePart)
          else
            Result := FloatValue(DatePart - TimePart);
        end;
      end;
  else
    DatabaseErrorFmt(SDatFieldType, [FieldName]);
  end;
end;

{ ------------------------------------------------------------------------------------- declared types }

const
  DeclInteger = '|INTEGER|INT|MEDIUMINT|SMALLINT|TINYINT|INT2|INT4|';
  DeclLargeint = '|BIGINT|INT8|UNSIGNED BIG INT|LARGEINT|';
  DeclBoolean = '|BOOLEAN|BOOL|BIT|';
  DeclString = '|CHAR|VARCHAR|NCHAR|NVARCHAR|VARYING CHARACTER|NATIVE CHARACTER|CHARACTER|';
  DeclMemo = '|TEXT|CLOB|MEMO|LONGTEXT|';
  DeclBlob = '|BLOB|BINARY|VARBINARY|IMAGE|';
  DeclFloat = '|REAL|DOUBLE|DOUBLE PRECISION|FLOAT|NUMERIC|DECIMAL|MONEY|CURRENCY|';
  DefaultStringSize = 255;

{ Normalises a declared type: Full is upper case with single spaces; Base is the part before a
  parenthesised suffix; Arg is the first number of the suffix, -1 when there is none. }
procedure SplitDecl(const Decl: string; out Full, Base: string; out Arg: Integer);
var
  I, P: Integer;
  C: Char;
  Space: Boolean;
begin
  Full := '';
  Space := False;
  for I := 1 to Length(Decl) do
  begin
    C := Decl[I];
    if (C = ' ') or ((C >= #9) and (C <= #13)) then
      Space := Full <> ''
    else
    begin
      if Space then
        Full := Full + ' ';
      Space := False;
      if (C >= 'a') and (C <= 'z') then
        C := Char(Ord(C) - 32);
      Full := Full + C;
    end;
  end;
  Arg := -1;
  P := Pos('(', Full);
  if P = 0 then
    Base := Full
  else
  begin
    Base := Trim(Copy(Full, 1, P - 1));
    I := P + 1;
    while (I <= Length(Full)) and (Full[I] = ' ') do
      Inc(I);
    if (I <= Length(Full)) and (Full[I] >= '0') and (Full[I] <= '9') then
    begin
      Arg := 0;
      while (I <= Length(Full)) and (Full[I] >= '0') and (Full[I] <= '9') do
      begin
        if Arg < 100000000 then
          Arg := Arg * 10 + (Ord(Full[I]) - Ord('0'));
        Inc(I);
      end;
    end;
  end;
end;

{ Declared type -> field type and size (DAT-130): the exact names first, then the affinity rules of
  SQLite on the whole text. }
function DeclFieldType(const Decl: string; out Size: Integer): TFieldType;
var
  Full, Base, Key: string;
  Arg: Integer;
begin
  SplitDecl(Decl, Full, Base, Arg);
  Size := 0;
  Key := '|' + Base + '|';
  if Pos('|', Base) > 0 then
    Key := '';
  if (Key <> '') and (Pos(Key, DeclInteger) > 0) then
    Result := ftInteger
  else if (Key <> '') and (Pos(Key, DeclLargeint) > 0) then
    Result := ftLargeint
  else if (Key <> '') and (Pos(Key, DeclBoolean) > 0) then
    Result := ftBoolean
  else if (Key <> '') and (Pos(Key, DeclString) > 0) then
  begin
    Result := ftString;
    if Arg > 0 then
      Size := Arg
    else
      Size := DefaultStringSize;
  end
  else if (Key <> '') and (Pos(Key, DeclMemo) > 0) then
    Result := ftMemo
  else if (Key <> '') and (Pos(Key, DeclBlob) > 0) then
    Result := ftBlob
  else if (Key <> '') and (Pos(Key, DeclFloat) > 0) then
    Result := ftFloat
  else if Base = 'DATE' then
    Result := ftDate
  else if Base = 'TIME' then
    Result := ftTime
  else if (Base = 'DATETIME') or (Base = 'TIMESTAMP') then
    Result := ftDateTime
  else if Pos('INT', Full) > 0 then
    Result := ftInteger
  else if (Pos('CLOB', Full) > 0) or (Pos('TEXT', Full) > 0) then
    Result := ftMemo
  else if Pos('CHAR', Full) > 0 then
  begin
    Result := ftString;
    Size := DefaultStringSize;
  end
  else if Pos('BLOB', Full) > 0 then
    Result := ftBlob
  else
    Result := ftFloat;
end;

{ ------------------------------------------------------------------------------------------ table info }

type
  TSQLiteTableColumn = record
    Name: string;
    DeclType: string;
    NotNull: Boolean;
    HasDefault: Boolean;
    Pk: Integer;          { 1-based position in the primary key, 0 = not part of it }
  end;
  TSQLiteTableColumns = array of TSQLiteTableColumn;

{ The stored columns of a table or view (PRAGMA table_info); empty when it does not exist. }
function TableColumns(Db: Psqlite3; const Table: string): TSQLiteTableColumns;
var
  SQL: string;
  Stmt: Psqlite3_stmt;
  Rc, N: Integer;
  Error: ESQLiteError;
begin
  Result := nil;
  SQL := 'PRAGMA table_info(' + SQLiteQuoteIdent(Table) + ')';
  Stmt := PrepareOne(Db, SQL);
  N := 0;
  Rc := sqlite3_step(Stmt);
  while Rc = SQLITE_ROW do
  begin
    SetLength(Result, N + 1);
    Result[N].Name := ColumnText(Db, Stmt, 1);
    Result[N].DeclType := ColumnText(Db, Stmt, 2);
    Result[N].NotNull := sqlite3_column_int64(Stmt, 3) <> 0;
    Result[N].HasDefault := sqlite3_column_type(Stmt, 4) <> SQLITE_NULL;
    Result[N].Pk := Integer(sqlite3_column_int64(Stmt, 5));
    N := N + 1;
    Rc := sqlite3_step(Stmt);
  end;
  if Rc <> SQLITE_DONE then
  begin
    Error := SQLiteErrorFor(Db, Rc, SQL);
    sqlite3_finalize(Stmt);
    raise Error;
  end;
  sqlite3_finalize(Stmt);
end;

function IndexOfTableColumn(const Columns: TSQLiteTableColumns; const Name: string): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to Length(Columns) - 1 do
    if SameText(Columns[I].Name, Name) then
    begin
      Result := I;
      Exit;
    end;
end;

function IsRowIdName(const Name: string): Boolean;
begin
  Result := SameText(Name, 'rowid') or SameText(Name, '_rowid_') or SameText(Name, 'oid');
end;

{ Whether the statement compiles; nothing is run. }
function Compiles(Db: Psqlite3; const SQL: string): Boolean;
var
  Text: TBytes;
  Tail: PUtf8;
  Stmt: Psqlite3_stmt;
begin
  Text := Utf16ToUtf8(SQL);
  Stmt := nil;
  Result := sqlite3_prepare_v2(Db, PUtf8(@Text[0]), -1, Stmt, Tail) = SQLITE_OK;
  if Stmt <> nil then
    sqlite3_finalize(Stmt);
end;

{ The name under which statements reach the row id of a table: the first of rowid, _rowid_, oid that is
  not a column of the table; '' for a table without row id. The names are not quoted in statements: a
  quoted name that is no column would be taken as a string. }
function RowIdNameOf(Db: Psqlite3; const Table: string; const Columns: TSQLiteTableColumns): string;
var
  I: Integer;
  Name: string;
begin
  Result := '';
  for I := 0 to 2 do
  begin
    case I of
      0: Name := 'rowid';
      1: Name := '_rowid_';
    else
      Name := 'oid';
    end;
    if IndexOfTableColumn(Columns, Name) < 0 then
    begin
      if Compiles(Db, 'SELECT ' + Name + ' FROM ' + SQLiteQuoteIdent(Table)) then
        Result := Name;
      Exit;
    end;
  end;
end;

{ sqlite3_table_column_metadata fails for the columns of a view. }
function IsView(Db: Psqlite3; const Table, Column: string): Boolean;
var
  T, Col: TBytes;
  DataType, CollSeq: PUtf8;
  NotNull, PrimaryKey, AutoInc: Integer;
begin
  T := Utf16ToUtf8(Table);
  Col := Utf16ToUtf8(Column);
  Result := sqlite3_table_column_metadata(Db, nil, PUtf8(@T[0]), PUtf8(@Col[0]), DataType, CollSeq, NotNull,
    PrimaryKey, AutoInc) <> SQLITE_OK;
end;

{ ----------------------------------------------------------------------------------------- TSQLiteQuery }

const
  SSqlNoConnection = 'Connection is not assigned';
  SSqlEmptySQL = 'SQL text is empty';
  SSqlOneStatement = 'Only one statement is allowed in a query; use TSQLiteConnection.ExecSQL for scripts';
  SSqlPositional = 'Positional parameters are not supported; use :name';
  SSqlNoParamValue = 'No value for parameter ''%s''';
  SSqlNoResultSet = 'Statement does not return a result set; use ExecSQL';
  SSqlRowChanged = 'Record was changed or deleted by another user';
  SSqlKeyNotUnique = 'Update affected %d records; key is not unique';
  SSqlReasonDataSet = 'Dataset is read-only';
  SSqlReasonConnection = 'Connection is read-only';
  SSqlReasonManyTables = 'Query selects from more than one table';
  SSqlReasonNoTable = 'Query has no table columns';
  SSqlReasonUnknownTable = 'Update table is not known; set UpdateTable';
  SSqlReasonTableMissing = 'Update table does not exist';
  SSqlReasonView = 'Update table is a view';
  SSqlReasonNoKey = 'No key columns in the result set';
  SSqlReasonKeyMissing = 'Key field ''%s'' not found';
  SSqlReasonKeyNoColumn = 'Key field ''%s'' is not a column of the update table';

constructor TSQLiteQuery.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FSQL := TStringList.Create;
  TStringList(FSQL).OnChange := SQLChanged;
  FParams := TParams.Create;
  FParamCheck := True;
  FRowsAffected := -1;
end;

destructor TSQLiteQuery.Destroy;
begin
  { the dataset was closed in BeforeDestruction }
  Unprepare;
  if FConnection <> nil then
    FConnection.UnregisterDataSet(Self);
  FConnection := nil;
  TStringList(FSQL).OnChange := nil;
  FSQL.Free;
  FParams.Free;
  inherited Destroy;
end;

procedure TSQLiteQuery.SetConnection(Value: TSQLiteConnection);
begin
  if Value = FConnection then
    Exit;
  CheckInactive;
  Unprepare;
  if FConnection <> nil then
    FConnection.UnregisterDataSet(Self);
  FConnection := Value;
  if FConnection <> nil then
    FConnection.RegisterDataSet(Self, Detach);
end;

{ The connection closes (the dataset is closed already) or is destroyed. }
procedure TSQLiteQuery.Detach(Destroying: Boolean);
begin
  Unprepare;
  if Destroying then
    FConnection := nil;
end;

procedure TSQLiteQuery.CheckSQLChange;
begin
end;

procedure TSQLiteQuery.SetSQL(Value: TStrings);
begin
  CheckSQLChange;
  FSQL.Assign(Value);
end;

procedure TSQLiteQuery.SQLChanged(Sender: TObject);
begin
  CheckSQLChange;
  if Active then
    Close;
  Unprepare;
  if FParamCheck then
    FParams.ParseSQL(FSQL.Text, True);
end;

procedure TSQLiteQuery.SetUpdateTable(const Value: string);
begin
  if Value <> FUpdateTable then
  begin
    CheckInactive;
    FUpdateTable := Value;
  end;
end;

procedure TSQLiteQuery.SetKeyFields(const Value: string);
begin
  if Value <> FKeyFields then
  begin
    CheckInactive;
    FKeyFields := Value;
  end;
end;

function TSQLiteQuery.ParamByName(const Name: string): TParam;
begin
  Result := FParams.ParamByName(Name);
end;

function TSQLiteQuery.GetPrepared: Boolean;
begin
  Result := FStmt <> nil;
end;

procedure TSQLiteQuery.SetPrepared(Value: Boolean);
begin
  if Value then
    Prepare
  else
    Unprepare;
end;

function TSQLiteQuery.StatementText: string;
begin
  Result := Trim(FSQL.Text);
end;

procedure TSQLiteQuery.Prepare;
var
  Db: Psqlite3;
  Text: TBytes;
  Tail, Name: PUtf8;
  Stmt: Psqlite3_stmt;
  I: Integer;
begin
  if FStmt <> nil then
    Exit;
  if FConnection = nil then
    raise ESQLiteError.Create(SSqlNoConnection);
  FConnection.Open;
  Db := FConnection.Handle;
  Text := Utf16ToUtf8(FSQL.Text);
  SQLiteCheck(Db, sqlite3_prepare_v2(Db, PUtf8(@Text[0]), -1, Stmt, Tail), StatementText);
  if Stmt = nil then
    raise ESQLiteError.Create(SSqlEmptySQL);
  if (Tail <> nil) and not IsBlank(PByte(Tail)) then
  begin
    sqlite3_finalize(Stmt);
    raise ESQLiteError.Create(SSqlOneStatement);
  end;
  for I := 1 to sqlite3_bind_parameter_count(Stmt) do
  begin
    { "?" has no name, "?NNN" has the name "?NNN" }
    Name := sqlite3_bind_parameter_name(Stmt, I);
    if (Name = nil) or (PByte(Name)[0] = Ord('?')) then
    begin
      sqlite3_finalize(Stmt);
      raise ESQLiteError.Create(SSqlPositional);
    end;
  end;
  FStmt := Stmt;
end;

procedure TSQLiteQuery.Unprepare;
begin
  if FStmt <> nil then
  begin
    sqlite3_finalize(FStmt);
    FStmt := nil;
  end;
  FStepped := False;
end;

procedure TSQLiteQuery.BindParams;
var
  Db: Psqlite3;
  I: Integer;
  Name: string;
  P: TParam;
  DataType: TFieldType;
  Value: TDBValue;
begin
  Db := FConnection.Handle;
  sqlite3_reset(FStmt);
  sqlite3_clear_bindings(FStmt);
  for I := 1 to sqlite3_bind_parameter_count(FStmt) do
  begin
    Name := Utf8ToUtf16(sqlite3_bind_parameter_name(FStmt, I));
    { without its prefix character (":", "@" or "$") }
    Name := Copy(Name, 2, Length(Name));
    P := FParams.FindParam(Name);
    if P = nil then
      raise ESQLiteError.CreateFmt(SSqlNoParamValue, [Name]);
    Value := ParamValue(P, DataType);
    BindValue(Db, FStmt, I, DataType, Value);
  end;
end;

function TSQLiteQuery.Step: Integer;
var
  Error: ESQLiteError;
begin
  Result := sqlite3_step(FStmt);
  if (Result <> SQLITE_ROW) and (Result <> SQLITE_DONE) then
  begin
    Error := SQLiteErrorFor(FConnection.Handle, Result, StatementText);
    sqlite3_reset(FStmt);
    raise Error;
  end;
end;

procedure TSQLiteQuery.ExecSQL;
var
  Db: Psqlite3;
  Before, Rc: Integer;
begin
  if Active then
    DatabaseError(SDatActiveChange);
  Prepare;
  BindParams;
  Db := FConnection.Handle;
  Before := sqlite3_total_changes(Db);
  repeat
    Rc := Step;
  until Rc <> SQLITE_ROW;
  { sqlite3_changes keeps the count of the last INSERT, UPDATE or DELETE (the rule of DAT-125) }
  if (sqlite3_stmt_readonly(FStmt) = 0) and (sqlite3_total_changes(Db) <> Before) then
    FRowsAffected := sqlite3_changes(Db)
  else
    FRowsAffected := 0;
  sqlite3_reset(FStmt);
end;

procedure TSQLiteQuery.CheckResultSet;
begin
  if sqlite3_column_count(FStmt) = 0 then
    raise ESQLiteError.Create(SSqlNoResultSet);
end;

procedure TSQLiteQuery.FirstStep;
begin
  FStepRc := Step;
  FStepped := True;
end;

procedure TSQLiteQuery.DriverOpen;
begin
  Prepare;
  CheckResultSet;
  BindParams;
  FOpening := True;
end;

procedure TSQLiteQuery.DriverInitFieldDefs;
begin
  if FOpening then
  begin
    { the first step tells the storage classes that type the expression columns }
    FirstStep;
    BuildFieldDefs;
  end
  else
  begin
    { FieldDefs.Update on a closed dataset: the statement runs to its first row and is reset }
    Prepare;
    CheckResultSet;
    BindParams;
    try
      FirstStep;
      BuildFieldDefs;
    finally
      FStepped := False;
      sqlite3_reset(FStmt);
    end;
  end;
end;

procedure TSQLiteQuery.DriverLoadRows;
var
  Db: Psqlite3;
  Rc, I, N: Integer;
  Row: TDataRow;
begin
  if FStepped then
  begin
    FStepped := False;
    Rc := FStepRc;
  end
  else
  begin
    { Refresh: the statement runs again with the current parameter values }
    Prepare;
    BindParams;
    Rc := Step;
  end;
  Db := FConnection.Handle;
  N := FieldDefs.Count;
  try
    while Rc = SQLITE_ROW do
    begin
      Row := AddLoadedRow;
      for I := 0 to N - 1 do
        Row.Values[I] := ReadColumn(Db, FStmt, I, FieldDefs[I].DataType, FieldDefs[I].Name);
      Rc := Step;
    end;
  finally
    { no statement stays active: no read lock is held while the dataset is open }
    sqlite3_reset(FStmt);
  end;
end;

procedure TSQLiteQuery.DriverClose;
begin
  FOpening := False;
  FStepped := False;
  if FStmt <> nil then
    sqlite3_reset(FStmt);
end;

{ Column names (DAT-146), the update table and the key (DAT-151, DAT-152), the field definitions
  (DAT-130). The statement is on its first row, or at its end when FStepRc is SQLITE_DONE. }
procedure TSQLiteQuery.BuildFieldDefs;
var
  Db: Psqlite3;
  N, I, J, K, Size, PkCount, AutoIndex: Integer;
  Names, Raw, Decls, OriginTables, OriginColumns: array of string;
  Types: array of TFieldType;
  Sizes: array of Integer;
  HaveOrigins, Used, Found, Required, Updatable: Boolean;
  Info: TSQLiteTableColumns;
  Candidate, Part, Rest: string;
  DataType: TFieldType;
begin
  Db := FConnection.Handle;
  N := sqlite3_column_count(FStmt);
  SetLength(Names, N);
  SetLength(Raw, N);
  SetLength(Decls, N);
  SetLength(OriginTables, N);
  SetLength(OriginColumns, N);
  SetLength(Types, N);
  SetLength(Sizes, N);
  HaveOrigins := Assigned(sqlite3_column_table_name) and Assigned(sqlite3_column_origin_name);
  for I := 0 to N - 1 do
  begin
    Raw[I] := Utf8ToUtf16(sqlite3_column_name(FStmt, I));
    Decls[I] := Utf8ToUtf16(sqlite3_column_decltype(FStmt, I));
    OriginTables[I] := '';
    OriginColumns[I] := '';
    if HaveOrigins then
    begin
      OriginTables[I] := Utf8ToUtf16(sqlite3_column_table_name(FStmt, I));
      OriginColumns[I] := Utf8ToUtf16(sqlite3_column_origin_name(FStmt, I));
    end;
  end;

  { names: an empty name is Column<N>; a name that occurred before gets the suffix _1, _2, ... }
  for I := 0 to N - 1 do
  begin
    Candidate := Raw[I];
    if Candidate = '' then
      Candidate := 'Column' + IntToStr(I + 1);
    K := 0;
    if Raw[I] <> '' then
      for J := 0 to I - 1 do
        if SameText(Raw[J], Raw[I]) then
          K := K + 1;
    Part := Candidate;
    repeat
      if K > 0 then
        Candidate := Part + '_' + IntToStr(K);
      Used := False;
      for J := 0 to I - 1 do
        if SameText(Names[J], Candidate) then
          Used := True;
      if Used then
        K := K + 1;
    until not Used;
    Names[I] := Candidate;
  end;

  { the update table }
  FTable := '';
  FRowIdName := '';
  FReason := '';
  FKey := nil;
  Info := nil;
  AutoIndex := -1;
  SetLength(FColumns, N);
  for I := 0 to N - 1 do
  begin
    FColumns[I].Origin := '';
    FColumns[I].Updatable := False;
    FColumns[I].IsRowId := False;
    FColumns[I].InfoIndex := -1;
  end;
  if FUpdateTable <> '' then
    FTable := FUpdateTable
  else if not HaveOrigins then
    FReason := SSqlReasonUnknownTable
  else
  begin
    for I := 0 to N - 1 do
      if OriginTables[I] <> '' then
      begin
        if FTable = '' then
          FTable := OriginTables[I]
        else if not SameText(FTable, OriginTables[I]) then
          FReason := SSqlReasonManyTables;
      end;
    if FTable = '' then
      FReason := SSqlReasonNoTable;
  end;
  if FReason = '' then
  begin
    Info := TableColumns(Db, FTable);
    if Length(Info) = 0 then
      FReason := SSqlReasonTableMissing
    else if IsView(Db, FTable, Info[0].Name) then
      FReason := SSqlReasonView;
  end;
  if FReason <> '' then
    FTable := ''
  else
  begin
    FRowIdName := RowIdNameOf(Db, FTable, Info);
    PkCount := 0;
    for J := 0 to Length(Info) - 1 do
      if Info[J].Pk > PkCount then
        PkCount := Info[J].Pk;
    { a single-column primary key declared exactly INTEGER is the row id: it is assigned by the database }
    if (PkCount = 1) and (FRowIdName <> '') then
      for J := 0 to Length(Info) - 1 do
        if (Info[J].Pk = 1) and SameText(Trim(Info[J].DeclType), 'INTEGER') then
          AutoIndex := J;

    { which result column reads which column of the table: by origin, else by name }
    for I := 0 to N - 1 do
    begin
      if HaveOrigins then
      begin
        Found := SameText(OriginTables[I], FTable);
        Candidate := OriginColumns[I];
      end
      else
      begin
        Found := True;
        Candidate := Raw[I];
      end;
      if Found and (Candidate <> '') then
      begin
        J := IndexOfTableColumn(Info, Candidate);
        if J >= 0 then
        begin
          FColumns[I].Origin := Info[J].Name;
          FColumns[I].Updatable := True;
          FColumns[I].InfoIndex := J;
        end
        else if IsRowIdName(Candidate) and (FRowIdName <> '') then
          FColumns[I].IsRowId := True
        else if HaveOrigins then
          { a generated column: read, never written }
          FColumns[I].Origin := Candidate;
      end;
    end;

    { the key }
    if FKeyFields <> '' then
    begin
      Rest := FKeyFields;
      while (Rest <> '') and (FReason = '') do
      begin
        K := Pos(';', Rest);
        if K = 0 then
          K := Length(Rest) + 1;
        Part := Trim(Copy(Rest, 1, K - 1));
        Rest := Copy(Rest, K + 1, Length(Rest));
        if Part <> '' then
        begin
          J := -1;
          for I := 0 to N - 1 do
            if SameText(Names[I], Part) then
              J := I;
          if J < 0 then
            FReason := Format(SSqlReasonKeyMissing, [Part])
          else if (FColumns[J].Origin = '') and not FColumns[J].IsRowId then
            FReason := Format(SSqlReasonKeyNoColumn, [Part])
          else
          begin
            SetLength(FKey, Length(FKey) + 1);
            FKey[Length(FKey) - 1] := J;
          end;
        end;
      end;
      if (FReason = '') and (Length(FKey) = 0) then
        FReason := SSqlReasonNoKey;
    end
    else
    begin
      { the primary key when all its columns are in the result set }
      Found := PkCount > 0;
      for K := 1 to PkCount do
      begin
        J := -1;
        for I := N - 1 downto 0 do
          if (FColumns[I].InfoIndex >= 0) and (Info[FColumns[I].InfoIndex].Pk = K) then
            J := I;
        if J < 0 then
          Found := False
        else
        begin
          SetLength(FKey, Length(FKey) + 1);
          FKey[Length(FKey) - 1] := J;
        end;
      end;
      if not Found then
      begin
        { else an explicitly selected row id }
        FKey := nil;
        for I := N - 1 downto 0 do
          if FColumns[I].IsRowId then
          begin
            SetLength(FKey, 1);
            FKey[0] := I;
          end;
        if Length(FKey) = 0 then
          FReason := SSqlReasonNoKey;
      end;
    end;
  end;

  { the types }
  Updatable := DriverCanModify;
  for I := 0 to N - 1 do
  begin
    Size := 0;
    if FColumns[I].IsRowId then
      DataType := ftLargeint
    else if Decls[I] <> '' then
    begin
      DataType := DeclFieldType(Decls[I], Size);
      if (DataType = ftInteger) and (AutoIndex >= 0) and (FColumns[I].InfoIndex = AutoIndex) then
        DataType := ftAutoInc;
    end
    else
    begin
      { an expression column: the storage class of the first row; unlimited strings }
      DataType := ftString;
      if FStepRc = SQLITE_ROW then
        case sqlite3_column_type(FStmt, I) of
          SQLITE_INTEGER: DataType := ftLargeint;
          SQLITE_FLOAT: DataType := ftFloat;
          SQLITE_BLOB: DataType := ftBlob;
        end;
    end;
    Types[I] := DataType;
    Sizes[I] := Size;
  end;
  FieldDefs.Clear;
  for I := 0 to N - 1 do
  begin
    Required := False;
    if Updatable and FColumns[I].Updatable and (Types[I] <> ftAutoInc) then
    begin
      J := FColumns[I].InfoIndex;
      Required := Info[J].NotNull and not Info[J].HasDefault;
    end;
    FieldDefs.Add(Names[I], Types[I], Sizes[I], Required);
  end;
end;

function TSQLiteQuery.GetReadOnlyReason: string;
begin
  if FReadOnly then
    Result := SSqlReasonDataSet
  else if (FConnection <> nil) and (FConnection.OpenMode = omReadOnly) then
    Result := SSqlReasonConnection
  else
    Result := FReason;
end;

function TSQLiteQuery.DriverCanModify: Boolean;
begin
  Result := (FConnection <> nil) and (GetReadOnlyReason = '');
end;

function TSQLiteQuery.DriverFieldUpdatable(Field: TField): Boolean;
begin
  Result := (Field.FieldNo >= 1) and (Field.FieldNo <= Length(FColumns)) and FColumns[Field.FieldNo - 1].Updatable;
end;

function TSQLiteQuery.ColumnRef(Index: Integer): string;
begin
  if FColumns[Index].IsRowId then
    Result := FRowIdName
  else
    Result := SQLiteQuoteIdent(FColumns[Index].Origin);
end;

function TSQLiteQuery.KeyCondition(First: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(FKey) - 1 do
  begin
    if I > 0 then
      Result := Result + ' AND ';
    { IS: a NULL key matches its row }
    Result := Result + ColumnRef(FKey[I]) + ' IS ?' + IntToStr(First + I);
  end;
end;

procedure TSQLiteQuery.BindKey(Stmt: Pointer; First: Integer; Row: TDataRow);
var
  I: Integer;
begin
  for I := 0 to Length(FKey) - 1 do
    BindValue(FConnection.Handle, Stmt, First + I, FieldDefs[FKey[I]].DataType, Row.Values[FKey[I]]);
end;

procedure TSQLiteQuery.ApplyInsert(Buffer: TDataRow);
var
  Db: Psqlite3;
  Stmt: Psqlite3_stmt;
  SQL, Cols, Marks: string;
  I, N: Integer;
begin
  Db := FConnection.Handle;
  Cols := '';
  Marks := '';
  N := 0;
  for I := 0 to Length(FColumns) - 1 do
    if FColumns[I].Updatable and (IsAssigned(Buffer, I + 1) or (Buffer.Values[I].Kind <> vkNull)) then
    begin
      N := N + 1;
      if N > 1 then
      begin
        Cols := Cols + ', ';
        Marks := Marks + ', ';
      end;
      Cols := Cols + ColumnRef(I);
      Marks := Marks + '?' + IntToStr(N);
    end;
  if N = 0 then
    SQL := 'INSERT INTO ' + SQLiteQuoteIdent(FTable) + ' DEFAULT VALUES'
  else
    SQL := 'INSERT INTO ' + SQLiteQuoteIdent(FTable) + ' (' + Cols + ') VALUES (' + Marks + ')';
  Stmt := PrepareOne(Db, SQL);
  try
    N := 0;
    for I := 0 to Length(FColumns) - 1 do
      if FColumns[I].Updatable and (IsAssigned(Buffer, I + 1) or (Buffer.Values[I].Kind <> vkNull)) then
      begin
        N := N + 1;
        BindValue(Db, Stmt, N, FieldDefs[I].DataType, Buffer.Values[I]);
      end;
  except
    sqlite3_finalize(Stmt);
    raise;
  end;
  RunToEnd(Db, Stmt, SQL);
  RereadInserted(Buffer);
end;

{ Reads the inserted row back into the buffer: the auto-increment key, column defaults and values set
  by triggers. The row is found by its row id, in a table without row id by the key values. }
procedure TSQLiteQuery.RereadInserted(Buffer: TDataRow);
var
  Db: Psqlite3;
  Stmt: Psqlite3_stmt;
  SQL, Cols: string;
  I, N, Rc: Integer;
  Error: ESQLiteError;
begin
  Db := FConnection.Handle;
  Cols := '';
  for I := 0 to Length(FColumns) - 1 do
    if FColumns[I].IsRowId or (FColumns[I].Origin <> '') then
    begin
      if Cols <> '' then
        Cols := Cols + ', ';
      Cols := Cols + ColumnRef(I);
    end;
  if Cols = '' then
    Exit;
  SQL := 'SELECT ' + Cols + ' FROM ' + SQLiteQuoteIdent(FTable) + ' WHERE ';
  if FRowIdName <> '' then
    SQL := SQL + FRowIdName + ' = ?1'
  else
    SQL := SQL + KeyCondition(1);
  Stmt := PrepareOne(Db, SQL);
  try
    if FRowIdName <> '' then
      SQLiteCheck(Db, sqlite3_bind_int64(Stmt, 1, sqlite3_last_insert_rowid(Db)), SQL)
    else
      BindKey(Stmt, 1, Buffer);
    Rc := sqlite3_step(Stmt);
    if (Rc <> SQLITE_ROW) and (Rc <> SQLITE_DONE) then
    begin
      Error := SQLiteErrorFor(Db, Rc, SQL);
      raise Error;
    end;
    if Rc = SQLITE_ROW then
    begin
      N := 0;
      for I := 0 to Length(FColumns) - 1 do
        if FColumns[I].IsRowId or (FColumns[I].Origin <> '') then
        begin
          Buffer.Values[I] := ReadColumn(Db, Stmt, N, FieldDefs[I].DataType, FieldDefs[I].Name);
          N := N + 1;
        end;
    end;
  finally
    sqlite3_finalize(Stmt);
  end;
end;

procedure TSQLiteQuery.ApplyUpdate(OldRow, Buffer: TDataRow);
var
  Db: Psqlite3;
  Stmt: Psqlite3_stmt;
  SQL, Sets: string;
  I, N, Changed: Integer;
begin
  Db := FConnection.Handle;
  Sets := '';
  N := 0;
  for I := 0 to Length(FColumns) - 1 do
    if FColumns[I].Updatable and IsAssigned(Buffer, I + 1) then
    begin
      N := N + 1;
      if N > 1 then
        Sets := Sets + ', ';
      Sets := Sets + ColumnRef(I) + ' = ?' + IntToStr(N);
    end;
  if N = 0 then
    Exit;
  SQL := 'UPDATE ' + SQLiteQuoteIdent(FTable) + ' SET ' + Sets + ' WHERE ' + KeyCondition(N + 1);
  Stmt := PrepareOne(Db, SQL);
  try
    N := 0;
    for I := 0 to Length(FColumns) - 1 do
      if FColumns[I].Updatable and IsAssigned(Buffer, I + 1) then
      begin
        N := N + 1;
        BindValue(Db, Stmt, N, FieldDefs[I].DataType, Buffer.Values[I]);
      end;
    { the key columns with the values the row was loaded with }
    BindKey(Stmt, N + 1, OldRow);
  except
    sqlite3_finalize(Stmt);
    raise;
  end;
  RunToEnd(Db, Stmt, SQL);
  Changed := sqlite3_changes(Db);
  if Changed = 0 then
    DatabaseError(SSqlRowChanged);
  if Changed > 1 then
    DatabaseErrorFmt(SSqlKeyNotUnique, [Changed]);
end;

procedure TSQLiteQuery.ApplyDelete(Row: TDataRow);
var
  Db: Psqlite3;
  Stmt: Psqlite3_stmt;
  SQL: string;
begin
  Db := FConnection.Handle;
  SQL := 'DELETE FROM ' + SQLiteQuoteIdent(FTable) + ' WHERE ' + KeyCondition(1);
  Stmt := PrepareOne(Db, SQL);
  try
    BindKey(Stmt, 1, Row);
  except
    sqlite3_finalize(Stmt);
    raise;
  end;
  RunToEnd(Db, Stmt, SQL);
  if sqlite3_changes(Db) = 0 then
    DatabaseError(SSqlRowChanged);
end;

{ ----------------------------------------------------------------------------------------- TSQLiteTable }

const
  SSqlNoTableName = 'Table name is empty';
  SSqlReasonNoUsableKey = 'No usable key';

procedure TSQLiteTable.SetTableName(const Value: string);
var
  WasActive: Boolean;
begin
  if Value = FTableName then
    Exit;
  WasActive := Active;
  if WasActive then
    Close;
  FTableName := Value;
  Unprepare;
  if WasActive then
    Open;
end;

procedure TSQLiteTable.SetIndexFieldNames(const Value: string);
var
  WasActive: Boolean;
begin
  if Value = FIndexFieldNames then
    Exit;
  WasActive := Active;
  if WasActive then
    Close;
  FIndexFieldNames := Value;
  Unprepare;
  if WasActive then
    Open;
end;

{ The SQL text belongs to the table: a change from outside is undone and refused. }
procedure TSQLiteTable.CheckSQLChange;
begin
  if FGenerating then
    Exit;
  if Trim(FSQL.Text) <> FGeneratedSQL then
  begin
    TStringList(FSQL).OnChange := nil;
    FSQL.Text := FGeneratedSQL;
    TStringList(FSQL).OnChange := SQLChanged;
  end;
  DatabaseError(SDatNotSupported);
end;

{ SELECT * FROM "T" [ORDER BY ...]; for a table without primary key the row id is selected as the first
  column "rowid", unless a column of that name hides it. }
procedure TSQLiteTable.GenerateSQL;
var
  Db: Psqlite3;
  Info: TSQLiteTableColumns;
  Text, Order, Rest, Part: string;
  I, K: Integer;
  HasKey: Boolean;
begin
  if FTableName = '' then
    raise ESQLiteError.Create(SSqlNoTableName);
  if FConnection = nil then
    raise ESQLiteError.Create(SSqlNoConnection);
  FConnection.Open;
  Db := FConnection.Handle;
  Info := TableColumns(Db, FTableName);
  HasKey := False;
  for I := 0 to Length(Info) - 1 do
    if Info[I].Pk > 0 then
      HasKey := True;
  { a table without primary key always has a row id; a view has neither; a missing table gets SQLite's
    own error from the plain statement }
  FHiddenRowId := (Length(Info) > 0) and not HasKey and (IndexOfTableColumn(Info, 'rowid') < 0) and
    not IsView(Db, FTableName, Info[0].Name);
  if FHiddenRowId then
    Text := 'SELECT rowid AS "rowid", * FROM '
  else
    Text := 'SELECT * FROM ';
  Text := Text + SQLiteQuoteIdent(FTableName);
  Order := '';
  Rest := FIndexFieldNames;
  while Rest <> '' do
  begin
    K := Pos(';', Rest);
    if K = 0 then
      K := Length(Rest) + 1;
    Part := Trim(Copy(Rest, 1, K - 1));
    Rest := Copy(Rest, K + 1, Length(Rest));
    if Part <> '' then
    begin
      if Order <> '' then
        Order := Order + ', ';
      { qualified: SQLite would take a quoted name that is no column as a string }
      Order := Order + SQLiteQuoteIdent(FTableName) + '.' + SQLiteQuoteIdent(Part);
    end;
  end;
  if Order <> '' then
    Text := Text + ' ORDER BY ' + Order;
  FUpdateTable := FTableName;
  FKeyFields := '';
  FGeneratedSQL := Text;
  if Trim(FSQL.Text) <> Text then
  begin
    FGenerating := True;
    try
      FSQL.Text := Text;
    finally
      FGenerating := False;
    end;
  end;
end;

procedure TSQLiteTable.Prepare;
begin
  if not Prepared then
    GenerateSQL;
  inherited Prepare;
end;

procedure TSQLiteTable.ExecSQL;
begin
  DatabaseError(SDatNotSupported);
end;

procedure TSQLiteTable.DriverOpen;
begin
  { the table may have been redefined since the statement was prepared: it is generated again }
  Unprepare;
  inherited DriverOpen;
end;

procedure TSQLiteTable.DriverInitFieldDefs;
begin
  inherited DriverInitFieldDefs;
  if FReason = SSqlReasonNoKey then
    FReason := SSqlReasonNoUsableKey;
end;

procedure TSQLiteTable.DriverLoadRows;
var
  F: TField;
begin
  { at Open (the first step waits): the fields exist now }
  if FStepped and FHiddenRowId then
  begin
    F := FindField('rowid');
    if (F <> nil) and (F.FieldNo = 1) then
    begin
      F.Visible := False;
      F.ReadOnly := True;
    end;
  end;
  inherited DriverLoadRows;
end;

end.
