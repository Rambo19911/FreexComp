{ SQLiteData - the SQLite driver on the stock sqlite3.dll: ESQLiteError and the mapping of result codes
  (DAT-115...DAT-117), TSQLiteConnection (DAT-120...DAT-129). No UI-framework unit is used. }
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

end.
