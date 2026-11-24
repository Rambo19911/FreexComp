unit AutoTest;
{ The self-test of Contacts, active only with --autotest <result-file>: a script that works the main form the way
  a user does - typing into the editors, clicking the check box, pressing the navigator buttons - and checks what
  the controls and the table show after every step. One line per step goes into the result file, then the
  verdict, then the rows of the table as a second connection reads them from the database file. The program ends
  with exit code 0 when every step passed, else 1; 3 when the form cannot get a window. }

interface

uses
  Forms;

{ Starts the script as soon as the message loop runs: the form is visible and its creation has finished. }
procedure Schedule(Form: TForm; const ResultFile: string);
{ The program could not start: reported in the result file; ends the program with exit code 1. }
procedure StartupFailed(const ResultFile, Msg: string);

implementation

uses
  Windows, Messages, SysUtils, Classes, Controls, StdCtrls, DB, SQLiteData, DBCtrls, DBGrids, MainForm;

const
  { the private message that starts the script }
  WM_AUTOTEST = WM_APP + $1C0;
  StateNames: array[TDataSetState] of string = ('dsInactive', 'dsBrowse', 'dsEdit', 'dsInsert');
  ButtonNames: array[TNavigateBtn] of string = ('nbFirst', 'nbPrior', 'nbNext', 'nbLast', 'nbInsert', 'nbDelete',
    'nbEdit', 'nbPost', 'nbCancel', 'nbRefresh');
  BoolNames: array[Boolean] of string = ('False', 'True');

type
  { a check of a step did not hold; the message is '<expression>=<actual value>' }
  ECheckFailed = class(Exception);

  TStepProc = procedure of object;

  TDriver = class
  private
    FForm: TMainForm;
    FResultFile: string;
    FFormWindowProc: TWndMethod;
    FLines: TStringList;
    FFailed: Boolean;
    { an exception that reached the application from a message handler during the current step }
    FStrayError: string;
    procedure WindowProc(var Message: TMessage);
    procedure AppException(Sender: TObject; E: Exception);
    procedure Run;
    procedure RunStep(const Id, Title: string; Proc: TStepProc);
    procedure Dump;
    { simulated input }
    procedure TypeText(Edit: TWinControl; const S: string);
    procedure ClickCheck(Check: TWinControl);
    procedure Nav(Button: TNavigateBtn);
    procedure NavExpect(Button: TNavigateBtn; const ErrorClass, MessagePart: string);
    { checks }
    function T: TSQLiteTable;
    procedure CheckText(const Expr, Actual, Expected: string);
    procedure CheckInt(const Expr: string; Actual, Expected: Integer);
    procedure CheckBool(const Expr: string; Actual, Expected: Boolean);
    procedure CheckState(Expected: TDataSetState);
    procedure CheckCount(Expected: Integer);
    procedure CheckBtn(Button: TNavigateBtn; Expected: Boolean);
    procedure CheckCell(Col, Row: Integer; const Expected: string);
    procedure CheckStatus(const Expected: string);
    procedure CheckId(const Expected: string);
    procedure CheckName(const Expected: string);
    { the script }
    procedure S01;
    procedure S02;
    procedure S03;
    procedure S04;
    procedure S05;
    procedure S06;
    procedure S07;
    procedure S08;
    procedure S09;
    procedure S10;
    procedure S11;
    procedure S12;
    procedure S13;
    procedure S14;
    procedure S15;
  public
    constructor Create(AForm: TMainForm; const AResultFile: string);
    destructor Destroy; override;
  end;

var
  Driver: TDriver;

{ The result file: UTF-8 without a byte order mark, every line ended by CR LF. }
procedure WriteResult(const FileName: string; Lines: TStrings);
var
  Text: string;
  Bytes: TBytes;
  Stream: TFileStream;
  I: Integer;
begin
  Text := '';
  for I := 0 to Lines.Count - 1 do
    Text := Text + Lines[I] + #13#10;
  Bytes := TEncoding.UTF8.GetBytes(Text);
  Stream := TFileStream.Create(FileName, fmCreate);
  try
    if Length(Bytes) > 0 then
      Stream.WriteBuffer(Bytes[0], Length(Bytes));
  finally
    Stream.Free;
  end;
end;

procedure StartupFailed(const ResultFile, Msg: string);
var
  Lines: TStringList;
begin
  Lines := TStringList.Create;
  try
    Lines.Add('FAIL S00 startup: ' + Msg);
    Lines.Add('RESULT FAIL');
    WriteResult(ResultFile, Lines);
  finally
    Lines.Free;
  end;
  Halt(1);
end;

procedure Schedule(Form: TForm; const ResultFile: string);
begin
  try
    Form.HandleNeeded;
  except
    Halt(3);
  end;
  Driver := TDriver.Create(Form as TMainForm, ResultFile);
  PostMessageW(Form.Handle, WM_AUTOTEST, 0, 0);
end;

{ ---------------------------------------------------------------- the driver }

constructor TDriver.Create(AForm: TMainForm; const AResultFile: string);
begin
  inherited Create;
  FForm := AForm;
  FResultFile := AResultFile;
  FLines := TStringList.Create;
  FFormWindowProc := AForm.WindowProc;
  AForm.WindowProc := WindowProc;
end;

destructor TDriver.Destroy;
begin
  FLines.Free;
  inherited Destroy;
end;

procedure TDriver.WindowProc(var Message: TMessage);
begin
  if Message.Msg = WM_AUTOTEST then
  begin
    FForm.WindowProc := FFormWindowProc;
    Run;
  end
  else
    FFormWindowProc(Message);
end;

{ An exception inside a message handler never reaches the step that sent the message: it is kept, not shown. }
procedure TDriver.AppException(Sender: TObject; E: Exception);
begin
  if FStrayError = '' then
    FStrayError := E.ClassName + ': ' + E.Message;
end;

procedure TDriver.RunStep(const Id, Title: string; Proc: TStepProc);
var
  Failure: string;
begin
  Failure := '';
  FStrayError := '';
  try
    Proc;
    Failure := FStrayError;
  except
    on E: ECheckFailed do
      Failure := E.Message;
    on E: Exception do
      Failure := E.ClassName + ': ' + E.Message;
  end;
  if Failure = '' then
    FLines.Add('PASS ' + Id + ' ' + Title)
  else
  begin
    FFailed := True;
    FLines.Add('FAIL ' + Id + ' ' + Title + ': ' + Failure);
  end;
end;

procedure TDriver.Run;
var
  Rows: Integer;
begin
  Application.OnException := AppException;
  RunStep('S01', 'initial state', S01);
  RunStep('S02', 'insert enters dsInsert', S02);
  RunStep('S03', 'post first record', S03);
  RunStep('S04', 'post second record', S04);
  RunStep('S05', 'post third record', S05);
  RunStep('S06', 'refresh orders by id', S06);
  RunStep('S07', 'auto-edit and auto-post on scroll', S07);
  RunStep('S08', 'application validation', S08);
  RunStep('S09', 'database constraint', S09);
  RunStep('S10', 'delete', S10);
  RunStep('S11', 'cancel insert', S11);
  RunStep('S12', 'autoincrement not reused', S12);
  RunStep('S13', 'transaction rollback', S13);
  RunStep('S14', 'locate and filter', S14);
  RunStep('S15', 'grid follows dataset', S15);
  { the rows are read before the verdict is written: reading them can fail too }
  Rows := FLines.Count;
  RunStep('S16', 'dump', Dump);
  if FLines.Count > Rows then
    if Copy(FLines[FLines.Count - 1], 1, 4) = 'PASS' then
      FLines.Delete(FLines.Count - 1)                        { the step that reads the rows is silent when it passes }
    else
    begin
      FLines.Move(FLines.Count - 1, Rows);
      Inc(Rows);
    end;
  if FFailed then
    FLines.Insert(Rows, 'RESULT FAIL')
  else
    FLines.Insert(Rows, 'RESULT PASS');
  WriteResult(FResultFile, FLines);
  Application.OnException := nil;
  if FFailed then
    ExitCode := 1
  else
    ExitCode := 0;
  Application.Terminate;
end;

{ The table and the connection of the form are closed; a second connection reads what the file holds. }
procedure TDriver.Dump;
var
  Conn: TSQLiteConnection;
  Query: TSQLiteQuery;
  Line: string;
  I: Integer;
  F: TField;
begin
  FForm.ContactsTable.Close;
  FForm.Connection.Close;
  Conn := TSQLiteConnection.Create(nil);
  Query := TSQLiteQuery.Create(nil);
  try
    Conn.Database := FForm.Connection.Database;
    Conn.Open;
    Query.Connection := Conn;
    Query.SQL.Text := 'SELECT id, name, email, age, active, notes FROM contacts ORDER BY id';
    Query.Open;
    while not Query.Eof do
    begin
      Line := '';
      for I := 0 to Query.FieldCount - 1 do
      begin
        F := Query.Fields[I];
        if I > 0 then
          Line := Line + '|';
        if F.IsNull then
          Line := Line + 'NULL'
        else if F.DataType = ftBoolean then
          Line := Line + IntToStr(Ord(F.AsBoolean))
        else
          Line := Line + F.AsString;
      end;
      FLines.Add('DUMP ' + Line);
      Query.Next;
    end;
    Query.Close;
    Conn.Close;
  finally
    Query.Free;
    Conn.Free;
  end;
end;

{ ---------------------------------------------------------------- simulated input }

{ Typing replaces the selection: everything is selected first, then one character message per character. }
procedure TDriver.TypeText(Edit: TWinControl; const S: string);
var
  I: Integer;
begin
  Edit.SetFocus;
  SendMessageW(Edit.Handle, EM_SETSEL, 0, -1);
  for I := 1 to Length(S) do
    SendMessageW(Edit.Handle, WM_CHAR, WPARAM(Ord(S[I])), 1);
  Application.ProcessMessages;
end;

{ The click notification as the form reflects it to the check box when the user clicks it or presses Space
  (a simulated mouse click works only in the active window, which a test cannot rely on). }
procedure TDriver.ClickCheck(Check: TWinControl);
begin
  Check.Perform(CN_COMMAND, WPARAM(BN_CLICKED shl 16), LPARAM(Check.Handle));
  Application.ProcessMessages;
end;

procedure TDriver.Nav(Button: TNavigateBtn);
begin
  FForm.Navigator.BtnClick(Button);
  Application.ProcessMessages;
end;

{ The button must raise an exception of exactly this class whose message contains the part. }
procedure TDriver.NavExpect(Button: TNavigateBtn; const ErrorClass, MessagePart: string);
var
  Expr: string;
begin
  Expr := 'Nav(' + ButtonNames[Button] + ') raises ' + ErrorClass + '(''' + MessagePart + ''')';
  try
    FForm.Navigator.BtnClick(Button);
  except
    on E: Exception do
    begin
      Application.ProcessMessages;
      if (E.ClassName = ErrorClass) and (Pos(MessagePart, E.Message) > 0) then
        Exit;
      raise ECheckFailed.Create(Expr + '=' + E.ClassName + ': ' + E.Message);
    end;
  end;
  raise ECheckFailed.Create(Expr + '=no exception');
end;

{ ---------------------------------------------------------------- checks }

function TDriver.T: TSQLiteTable;
begin
  Result := FForm.ContactsTable;
end;

procedure TDriver.CheckText(const Expr, Actual, Expected: string);
begin
  if Actual <> Expected then
    raise ECheckFailed.Create(Expr + '=''' + Actual + '''');
end;

procedure TDriver.CheckInt(const Expr: string; Actual, Expected: Integer);
begin
  if Actual <> Expected then
    raise ECheckFailed.Create(Expr + '=' + IntToStr(Actual));
end;

procedure TDriver.CheckBool(const Expr: string; Actual, Expected: Boolean);
begin
  if Actual <> Expected then
    raise ECheckFailed.Create(Expr + '=' + BoolNames[Actual]);
end;

procedure TDriver.CheckState(Expected: TDataSetState);
begin
  if T.State <> Expected then
    raise ECheckFailed.Create('State=' + StateNames[T.State]);
end;

procedure TDriver.CheckCount(Expected: Integer);
begin
  CheckInt('RecordCount', T.RecordCount, Expected);
end;

procedure TDriver.CheckBtn(Button: TNavigateBtn; Expected: Boolean);
begin
  CheckBool('Btn(' + ButtonNames[Button] + ')', FForm.Navigator.ButtonEnabled(Button), Expected);
end;

procedure TDriver.CheckCell(Col, Row: Integer; const Expected: string);
begin
  CheckText(Format('Cell(%d,%d)', [Col, Row]), FForm.Grid.CellText(Col, Row), Expected);
end;

procedure TDriver.CheckStatus(const Expected: string);
begin
  CheckText('StatusLabel.Caption', FForm.StatusLabel.Caption, Expected);
end;

{ The text of the field label as the control reports it (it does not publish a caption). }
procedure TDriver.CheckId(const Expected: string);
var
  S: string;
begin
  SetLength(S, FForm.IdText.GetTextLen);
  if S <> '' then
    FForm.IdText.GetTextBuf(PChar(S), Length(S) + 1);
  CheckText('IdText.Caption', S, Expected);
end;

procedure TDriver.CheckName(const Expected: string);
begin
  CheckText('NameEdit.Text', FForm.NameEdit.Text, Expected);
end;

{ ---------------------------------------------------------------- the script }

procedure TDriver.S01;
begin
  CheckCount(0);
  CheckState(dsBrowse);
  CheckBtn(nbInsert, True);
  CheckBtn(nbDelete, False);
  CheckBtn(nbPost, False);
  CheckBtn(nbFirst, False);
  CheckStatus('No records');
end;

procedure TDriver.S02;
begin
  Nav(nbInsert);
  CheckState(dsInsert);
  CheckBool('ActiveCheck.Checked', FForm.ActiveCheck.Checked, True);
  CheckId('');
  CheckBtn(nbPost, True);
  CheckBtn(nbInsert, True);
  CheckBtn(nbDelete, False);
end;

procedure TDriver.S03;
begin
  TypeText(FForm.NameEdit, 'Ada Lovelace');
  TypeText(FForm.EmailEdit, 'ada@example.org');
  TypeText(FForm.AgeEdit, '36');
  TypeText(FForm.NotesMemo, 'First');
  Nav(nbPost);
  CheckState(dsBrowse);
  CheckCount(1);
  CheckId('1');
  CheckCell(1, 0, 'Ada Lovelace');
  CheckCell(4, 0, 'True');
  CheckStatus('Record 1 of 1');
end;

procedure TDriver.S04;
begin
  Nav(nbInsert);
  TypeText(FForm.NameEdit, 'Bob Stone');
  TypeText(FForm.AgeEdit, '41');
  ClickCheck(FForm.ActiveCheck);
  Nav(nbPost);
  CheckCount(2);
  CheckId('2');
  CheckBool('ActiveCheck.Checked', FForm.ActiveCheck.Checked, False);
  CheckText('EmailEdit.Text', FForm.EmailEdit.Text, '');
end;

procedure TDriver.S05;
begin
  Nav(nbInsert);
  TypeText(FForm.NameEdit, 'Cy Young');
  TypeText(FForm.EmailEdit, 'cy@example.org');
  TypeText(FForm.AgeEdit, '29');
  Nav(nbPost);
  CheckCount(3);
  CheckId('3');
end;

procedure TDriver.S06;
begin
  Nav(nbRefresh);
  Nav(nbFirst);
  CheckCell(1, 0, 'Ada Lovelace');
  CheckCell(1, 1, 'Bob Stone');
  CheckCell(1, 2, 'Cy Young');
  CheckName('Ada Lovelace');
  CheckBtn(nbPrior, False);
  CheckBtn(nbNext, True);
end;

procedure TDriver.S07;
begin
  TypeText(FForm.AgeEdit, '37');
  CheckState(dsEdit);
  Nav(nbNext);
  CheckState(dsBrowse);
  CheckName('Bob Stone');
  Nav(nbPrior);
  CheckText('AgeEdit.Text', FForm.AgeEdit.Text, '37');
  CheckCell(3, 0, '37');
end;

procedure TDriver.S08;
begin
  Nav(nbNext);
  TypeText(FForm.NameEdit, ' ');
  NavExpect(nbPost, 'EDatabaseError', 'Name is required');
  CheckState(dsEdit);
  Nav(nbCancel);
  CheckState(dsBrowse);
  CheckName('Bob Stone');
end;

procedure TDriver.S09;
begin
  TypeText(FForm.AgeEdit, '200');
  NavExpect(nbPost, 'ESQLiteError', 'CHECK constraint failed');
  CheckState(dsEdit);
  Nav(nbCancel);
  CheckText('AgeEdit.Text', FForm.AgeEdit.Text, '41');
  CheckState(dsBrowse);
end;

procedure TDriver.S10;
begin
  Nav(nbLast);
  CheckName('Cy Young');
  Nav(nbDelete);
  CheckCount(2);
  CheckName('Bob Stone');
  CheckStatus('Record 2 of 2');
end;

procedure TDriver.S11;
begin
  Nav(nbInsert);
  TypeText(FForm.NameEdit, 'Dan');
  Nav(nbCancel);
  CheckState(dsBrowse);
  CheckCount(2);
  CheckName('Bob Stone');
end;

procedure TDriver.S12;
begin
  Nav(nbInsert);
  TypeText(FForm.NameEdit, 'Eve Moss');
  Nav(nbPost);
  CheckId('4');
  CheckBool('ActiveCheck.Checked', FForm.ActiveCheck.Checked, True);
  CheckText('AgeEdit.Text', FForm.AgeEdit.Text, '');
  CheckCount(3);
end;

procedure TDriver.S13;
begin
  FForm.Connection.StartTransaction;
  try
    Nav(nbInsert);
    TypeText(FForm.NameEdit, 'Tmp User');
    Nav(nbPost);
    CheckCount(4);
    CheckBool('Connection.InTransaction', FForm.Connection.InTransaction, True);
  finally
    { also when a check failed: the following steps do not run inside this transaction }
    if FForm.Connection.InTransaction then
      FForm.Connection.Rollback;
  end;
  Nav(nbRefresh);
  CheckCount(3);
  CheckBool('T.Locate(''name'', [''Tmp User''], [])', T.Locate('name', ['Tmp User'], []), False);
  CheckBool('Connection.InTransaction', FForm.Connection.InTransaction, False);
end;

procedure TDriver.S14;
begin
  CheckBool('T.Locate(''name'', [''eve moss''], [loCaseInsensitive])',
    T.Locate('name', ['eve moss'], [loCaseInsensitive]), True);
  Application.ProcessMessages;
  CheckId('4');
  T.Filter := 'age >= 40';
  T.Filtered := True;
  try
    Application.ProcessMessages;
    CheckCount(1);
    CheckName('Bob Stone');
  finally
    T.Filtered := False;
  end;
  Application.ProcessMessages;
  CheckCount(3);
end;

procedure TDriver.S15;
begin
  Nav(nbFirst);
  Nav(nbNext);
  CheckText('Grid.SelectedField.FieldName', FForm.Grid.SelectedField.FieldName, 'id');
  CheckInt('T.RecNo', T.RecNo, 2);
  CheckCell(1, 1, 'Bob Stone');
  CheckName('Bob Stone');
end;

initialization

finalization
  Driver.Free;
end.
