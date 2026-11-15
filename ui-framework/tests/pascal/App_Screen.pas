// covers: UIF-400
// opc: -U ..\..\src
// needs: desktop
program App_Screen;
{ U-035, application-forms.md 1 and 3: Forms creates Screen after Application; Screen lists the forms in creation
  order (Forms: TForm descendants only, CustomForms: every TCustomForm) and drops destroyed ones; metrics come from
  the system (primary monitor size, virtual screen, primary work area, system DPI); Fonts is the sorted, de-duplicated
  list of face names, rebuilt by ResetFonts; Monitors has one TMonitor per display monitor with exactly one primary;
  the system fonts are snapshots of the non-client metrics; Classes.FindGlobalComponent finds a form by Name. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TPlainForm = class(TCustomForm);

function SameRect(const A, B: TRect): Boolean;
begin
  Result := (A.Left = B.Left) and (A.Top = B.Top) and (A.Right = B.Right) and (A.Bottom = B.Bottom);
end;

function FaceOf(const LF: TLogFontW): string;
begin
  Result := string(PChar(@LF.lfFaceName[0]));
end;

procedure Run;
var
  A, B, C: TForm;
  P: TPlainForm;
  R: TRect;
  I, N, Primaries: Integer;
  Sorted, Unique: Boolean;
  Metrics: TNonClientMetricsW;
  Icon: TLogFontW;
begin
  Section('globals (UIF-400)');
  CheckEqB(True, Screen <> nil, 'Screen exists');
  CheckEqB(True, Application <> nil, 'Application exists');

  Section('metrics');
  CheckEqI(GetSystemMetrics(SM_CXSCREEN), Screen.Width, 'Width');
  CheckEqI(GetSystemMetrics(SM_CYSCREEN), Screen.Height, 'Height');
  R := Screen.DesktopRect;
  CheckEqB(True, SameRect(Rect(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
    GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
    GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN)), R), 'DesktopRect: virtual screen');
  SystemParametersInfoW(SPI_GETWORKAREA, 0, @R, 0);
  CheckEqB(True, SameRect(R, Screen.WorkAreaRect), 'WorkAreaRect: primary work area');
  CheckEqI(GetDpiForSystem, Screen.PixelsPerInch, 'PixelsPerInch: system DPI');

  Section('monitors');
  CheckEqI(GetSystemMetrics(SM_CMONITORS), Screen.MonitorCount, 'MonitorCount');
  Primaries := 0;
  for I := 0 to Screen.MonitorCount - 1 do
    if Screen.Monitors[I].Primary then
    begin
      Inc(Primaries);
      CheckEqB(True, SameRect(Rect(0, 0, Screen.Width, Screen.Height), Screen.Monitors[I].BoundsRect),
        'primary BoundsRect');
      CheckEqB(True, SameRect(Screen.WorkAreaRect, Screen.Monitors[I].WorkareaRect), 'primary WorkareaRect');
      CheckEqI(Screen.PixelsPerInch, Screen.Monitors[I].PixelsPerInch, 'primary PixelsPerInch (no form on it)');
    end;
  CheckEqI(1, Primaries, 'exactly one primary monitor');
  CheckEqB(True, Screen.Monitors[0].Handle <> 0, 'Handle');

  Section('fonts');
  N := Screen.Fonts.Count;
  Check(N > 0, 'Fonts not empty');
  Sorted := True;
  Unique := True;
  for I := 1 to N - 1 do
  begin
    if CompareText(Screen.Fonts[I - 1], Screen.Fonts[I]) > 0 then
      Sorted := False;
    if CompareText(Screen.Fonts[I - 1], Screen.Fonts[I]) = 0 then
      Unique := False;
  end;
  CheckEqB(True, Sorted, 'sorted');
  CheckEqB(True, Unique, 'de-duplicated');
  Check(Screen.Fonts.IndexOf('Segoe UI') >= 0, 'contains Segoe UI');
  Screen.ResetFonts;
  CheckEqI(N, Screen.Fonts.Count, 'ResetFonts rebuilds the same list');

  Section('system fonts');
  FillChar(Metrics, SizeOf(Metrics), 0);
  Metrics.cbSize := SizeOf(Metrics);
  SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, SizeOf(Metrics), @Metrics, 0, GetDpiForSystem);
  SystemParametersInfoForDpi(SPI_GETICONTITLELOGFONT, SizeOf(Icon), @Icon, 0, GetDpiForSystem);
  CheckEqS(FaceOf(Metrics.lfMessageFont), Screen.MessageFont.Name, 'MessageFont name');
  CheckEqI(Metrics.lfMessageFont.lfHeight, Screen.MessageFont.Height, 'MessageFont height');
  CheckEqS(FaceOf(Metrics.lfMenuFont), Screen.MenuFont.Name, 'MenuFont name');
  CheckEqS(FaceOf(Metrics.lfStatusFont), Screen.HintFont.Name, 'HintFont name (status font)');
  CheckEqS(FaceOf(Icon), Screen.IconFont.Name, 'IconFont name');
  CheckEqI(Icon.lfHeight, Screen.IconFont.Height, 'IconFont height');

  Section('form lists');
  CheckEqI(0, Screen.FormCount, 'no forms yet');
  CheckEqB(True, Screen.ActiveForm = nil, 'no active form');
  CheckEqB(True, Screen.ActiveCustomForm = nil, 'no active custom form');
  CheckEqB(True, Screen.ActiveControl = nil, 'no active control');
  A := TForm.CreateNew(nil);
  A.Name := 'FormA';
  P := TPlainForm.CreateNew(nil);
  P.Name := 'FormP';
  B := TForm.CreateNew(nil);
  B.Name := 'FormB';
  C := TForm.CreateNew(nil);
  C.Name := 'FormC';
  CheckEqI(3, Screen.FormCount, 'FormCount: TForm descendants');
  CheckEqI(4, Screen.CustomFormCount, 'CustomFormCount: all forms');
  CheckEqB(True, (Screen.Forms[0] = A) and (Screen.Forms[1] = B) and (Screen.Forms[2] = C), 'creation order');
  CheckEqB(True, (Screen.CustomForms[0] = A) and (Screen.CustomForms[1] = P) and (Screen.CustomForms[3] = C),
    'CustomForms: creation order');
  CheckEqB(True, FindGlobalComponent('FormB') = B, 'FindGlobalComponent by Name');
  CheckEqB(True, FindGlobalComponent('formp') = P, 'FindGlobalComponent ignores case');
  CheckEqB(True, FindGlobalComponent('Nothing') = nil, 'FindGlobalComponent: unknown name');
  B.Free;
  CheckEqI(2, Screen.FormCount, 'destroyed form removed');
  CheckEqB(True, (Screen.Forms[0] = A) and (Screen.Forms[1] = C), 'order kept');
  CheckEqI(3, Screen.CustomFormCount, 'removed from CustomForms');
  P.Free;
  A.Free;
  C.Free;
  CheckEqI(0, Screen.CustomFormCount, 'all removed');
end;

begin
  Run;
  Done;
end.
