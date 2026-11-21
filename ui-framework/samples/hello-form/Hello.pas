program Hello;

uses
  Forms, MainUnit;

begin
  Application.Initialize;
  Application.Title := 'Hello';
  Application.CreateForm(TMainForm, MainForm);
  Application.Run;
end.
