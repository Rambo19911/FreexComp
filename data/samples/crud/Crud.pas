program Crud;
{ Contacts: a small data-entry program over a SQLite database file.
  Crud.exe [--db <path>] [--autotest <result-file>] }

uses
  Forms, MainForm;

begin
  Application.Initialize;
  Application.Title := 'Contacts';
  Application.CreateForm(TMainForm, MainForm);
  Application.Run;
end.
