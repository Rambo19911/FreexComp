// covers: UIF-464
// needs: desktop
program App_UnhandledException;
{ U-125, application-forms.md UIF-464: an exception outside the guarded sites - here a form constructor called by
  CreateForm from the program block - is not given to Application.HandleException (OnException stays silent); it
  propagates normally (CreateForm clears the reference, enclosing finally blocks run) and ends in the RTL
  unhandled-exception path (RTL-136): the stderr line of App_UnhandledException.err.txt and exit code 217. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, TestKit;

type
  TLog = class
  public
    procedure Exc(Sender: TObject; E: Exception);
  end;

  TBadForm = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  Log: TLog;
  BadForm: TBadForm;

procedure TLog.Exc(Sender: TObject; E: Exception);
begin
  WriteLn('OnException: ', E.ClassName, ': ', E.Message);
end;

constructor TBadForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  raise EConvertError.Create('in constructor');
end;

begin
  Log := TLog.Create;
  Application.Initialize;
  Application.OnException := Log.Exc;
  Section('CreateForm from the program block (UIF-464)');
  BadForm := TBadForm(Pointer(1));
  try
    Application.CreateForm(TBadForm, BadForm);
  finally
    CheckEqB(True, BadForm = nil, 'the exception propagates: finally runs, the reference is nil');
    CheckEqB(True, Application.MainForm = nil, 'no main form');
  end;
  WriteLn('not reached');
end.
