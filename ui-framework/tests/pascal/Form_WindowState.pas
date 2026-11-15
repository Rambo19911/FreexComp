// covers: UIF-272, UIF-275, UIF-432
// needs: desktop
program Form_WindowState;
{ U-040, layout.md UIF-275, application-forms.md UIF-432 step 4: WindowState is applied with ShowWindow when the
  form is shown (never as a creation style); on a visible form a change is applied at once; a state change made by
  the system (maximise / minimise / restore) is read back; while maximised the live client size is adopted, while
  minimised the client size stays the restored one, and restoring brings the restored size back. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TLog = class
  public
    Resizes: Integer;
    procedure Resized(Sender: TObject);
  end;

procedure TLog.Resized(Sender: TObject);
begin
  Inc(Resizes);
end;

function NewForm: TForm;
begin
  Result := TForm.CreateNew(nil);
  Result.ClientWidth := 300;
  Result.ClientHeight := 200;
  Result.Left := 100;
  Result.Top := 100;
end;

procedure CheckClient(F: TForm; const Name: string);
var
  R: TRect;
begin
  Windows.GetClientRect(F.Handle, R);
  CheckEqB(True, (R.Right = F.ClientWidth) and (R.Bottom = F.ClientHeight), Name + ': client size of the window');
end;

procedure Run;
var
  F: TForm;
  Log: TLog;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := NewForm;
  try
    Section('maximised at the first show');
    F.OnResize := Log.Resized;
    F.WindowState := wsMaximized;
    CheckEqB(False, IsZoomed(F.Handle), 'the handle is not created maximised');
    F.Show;
    CheckEqB(True, IsZoomed(F.Handle), 'shown maximised');
    CheckEqB(True, F.WindowState = wsMaximized, 'WindowState');
    Check(F.ClientWidth > 300, 'the live client size is adopted');
    CheckClient(F, 'maximised');
    CheckEqI(1, Log.Resizes, 'OnResize once during the first show');

    Section('changes on a visible form');
    F.WindowState := wsNormal;
    CheckEqB(False, IsZoomed(F.Handle), 'restored');
    CheckEqI(300, F.ClientWidth, 'restored client width');
    CheckEqI(200, F.ClientHeight, 'restored client height');
    F.WindowState := wsMinimized;
    CheckEqB(True, IsIconic(F.Handle), 'minimised');
    CheckEqI(300, F.ClientWidth, 'minimised: the restored client width stays');
    CheckEqI(200, F.ClientHeight, 'minimised: the restored client height stays');
    F.WindowState := wsNormal;
    CheckEqB(False, IsIconic(F.Handle), 'restored from minimised');
    CheckEqI(300, F.ClientWidth, 'restored size back');
    CheckClient(F, 'restored');

    Section('changes made by the system');
    ShowWindow(F.Handle, SW_MAXIMIZE);
    CheckEqB(True, F.WindowState = wsMaximized, 'SW_MAXIMIZE read back');
    CheckClient(F, 'system maximise');
    ShowWindow(F.Handle, SW_MINIMIZE);
    CheckEqB(True, F.WindowState = wsMinimized, 'SW_MINIMIZE read back');
    ShowWindow(F.Handle, SW_RESTORE);
    CheckEqB(True, F.WindowState = wsMaximized, 'SW_RESTORE of a minimised maximised window: maximised');
    ShowWindow(F.Handle, SW_RESTORE);
    CheckEqB(True, F.WindowState = wsNormal, 'SW_RESTORE read back');
    CheckEqI(300, F.ClientWidth, 'restored client width after the system changes');

    Section('hidden form');
    F.Hide;
    F.WindowState := wsMaximized;
    CheckEqB(False, IsWindowVisible(F.Handle), 'a hidden form stays hidden');
    F.Show;
    CheckEqB(True, IsZoomed(F.Handle), 'the next show applies the state');
  finally
    F.Free;
  end;
  F := NewForm;
  try
    Section('minimised at the first show');
    F.WindowState := wsMinimized;
    F.Show;
    CheckEqB(True, IsIconic(F.Handle), 'shown minimised');
    CheckEqI(300, F.ClientWidth, 'client width: the restored size');
    CheckEqI(200, F.ClientHeight, 'client height: the restored size');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
