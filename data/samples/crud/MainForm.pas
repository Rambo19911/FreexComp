unit MainForm;
{ The main form of Contacts: a grid over the table, a navigator, and editors of the current record. }

interface

uses
  SysUtils, Classes, Controls, Forms, StdCtrls, DB, SQLiteData, DBCtrls, DBGrids, AutoTest;

type
  TMainForm = class(TForm)
    Connection: TSQLiteConnection;
    ContactsTable: TSQLiteTable;
    ContactsSource: TDataSource;
    Navigator: TDBNavigator;
    Grid: TDBGrid;
    IdCaption: TLabel;
    IdText: TDBText;
    NameCaption: TLabel;
    NameEdit: TDBEdit;
    EmailCaption: TLabel;
    EmailEdit: TDBEdit;
    AgeCaption: TLabel;
    AgeEdit: TDBEdit;
    ActiveCheck: TDBCheckBox;
    NotesMemo: TDBMemo;
    StatusLabel: TLabel;
    procedure FormCreate(Sender: TObject);
    procedure FormDestroy(Sender: TObject);
    procedure ContactsTableNewRecord(DataSet: TDataSet);
    procedure ContactsTableBeforePost(DataSet: TDataSet);
    procedure ContactsTableAfterScroll(DataSet: TDataSet);
    procedure ContactsTableChanged(DataSet: TDataSet);
  private
    procedure OpenDatabase(const FileName: string);
  end;

var
  MainForm: TMainForm;

implementation

{$FORM MainForm.form}

const
  Schema =
    'CREATE TABLE IF NOT EXISTS contacts (' + #10 +
    '  id     INTEGER PRIMARY KEY AUTOINCREMENT,' + #10 +
    '  name   VARCHAR(50) NOT NULL,' + #10 +
    '  email  VARCHAR(100),' + #10 +
    '  age    INTEGER CONSTRAINT age_range CHECK (age IS NULL OR (age >= 0 AND age <= 150)),' + #10 +
    '  active BOOLEAN NOT NULL DEFAULT 1,' + #10 +
    '  notes  TEXT' + #10 +
    ');';

{ The value that follows the option on the command line; '' when the option is absent. }
function OptionValue(const Option: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to ParamCount - 1 do
    if ParamStr(I) = Option then
    begin
      Result := ParamStr(I + 1);
      Exit;
    end;
end;

procedure TMainForm.OpenDatabase(const FileName: string);
begin
  Connection.Database := FileName;
  Connection.Open;
  Connection.ExecSQL(Schema);
  ContactsTable.Open;
end;

procedure TMainForm.FormCreate(Sender: TObject);
var
  FileName, ResultFile: string;
begin
  FileName := OptionValue('--db');
  if FileName = '' then
    FileName := ExtractFilePath(ParamStr(0)) + 'contacts.db';
  ResultFile := OptionValue('--autotest');
  if ResultFile = '' then
  begin
    OpenDatabase(FileName);
    Exit;
  end;
  { the self-test: nothing may wait for an answer, and a failure to start is reported in the result file }
  Navigator.ConfirmDelete := False;
  Grid.Options := Grid.Options - [dgConfirmDelete];
  try
    OpenDatabase(FileName);
  except
    on E: Exception do
      AutoTest.StartupFailed(ResultFile, E.Message);
  end;
  AutoTest.Schedule(Self, ResultFile);
end;

procedure TMainForm.FormDestroy(Sender: TObject);
begin
  ContactsTable.Close;
  Connection.Close;
end;

procedure TMainForm.ContactsTableNewRecord(DataSet: TDataSet);
begin
  DataSet.FieldByName('active').AsBoolean := True;
end;

procedure TMainForm.ContactsTableBeforePost(DataSet: TDataSet);
begin
  if Trim(DataSet.FieldByName('name').AsString) = '' then
    DatabaseError('Name is required');
end;

procedure TMainForm.ContactsTableAfterScroll(DataSet: TDataSet);
begin
  ContactsTableChanged(DataSet);
end;

procedure TMainForm.ContactsTableChanged(DataSet: TDataSet);
begin
  if DataSet.IsEmpty then
    StatusLabel.Caption := 'No records'
  else
    StatusLabel.Caption := Format('Record %d of %d', [DataSet.RecNo, DataSet.RecordCount]);
end;

end.
