// covers: UIF-020, UIF-021, UIF-022
program Core_HelperClasses;
{ U-013, core.md sections 1 and 12: the modal result values equal the Win32 ID* button ids, the component
  message numbers, the cursor ident map (CursorToIdent / IdentToCursor / CursorToString / StringToCursor and the
  RegisterIntegerConsts registration of TCursor), IsPositiveResult, and the helper classes TSizeConstraints,
  TMargins, TPadding (defaults, range check, OnChange count, SetBounds, Assign). }
uses
  Windows, Messages, SysUtils, Classes, TypInfo, Controls, TestKit;

type
  TRecorder = class
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

procedure TRecorder.Changed(Sender: TObject);
begin
  Inc(Count);
end;

procedure CheckConstants;
begin
  Section('modal results (UIF-020)');
  CheckEqI(IDOK, mrOk, 'mrOk');
  CheckEqI(IDCANCEL, mrCancel, 'mrCancel');
  CheckEqI(IDABORT, mrAbort, 'mrAbort');
  CheckEqI(IDRETRY, mrRetry, 'mrRetry');
  CheckEqI(IDIGNORE, mrIgnore, 'mrIgnore');
  CheckEqI(IDYES, mrYes, 'mrYes');
  CheckEqI(IDNO, mrNo, 'mrNo');
  CheckEqI(8, mrClose, 'mrClose');
  CheckEqI(9, mrHelp, 'mrHelp');
  CheckEqI(10, mrTryAgain, 'mrTryAgain');
  CheckEqI(11, mrContinue, 'mrContinue');
  CheckEqI(12, mrAll, 'mrAll');
  CheckEqI(13, mrNoToAll, 'mrNoToAll');
  CheckEqI(14, mrYesToAll, 'mrYesToAll');
  CheckEqB(True, IsPositiveResult(mrOk) and IsPositiveResult(mrYes) and IsPositiveResult(mrAll)
    and IsPositiveResult(mrYesToAll) and IsPositiveResult(mrContinue), 'positive results');
  CheckEqB(False, IsPositiveResult(mrNone) or IsPositiveResult(mrCancel) or IsPositiveResult(mrNo)
    or IsPositiveResult(mrClose) or IsPositiveResult(mrNoToAll), 'other results');

  Section('component messages (UIF-022)');
  CheckEqI($B000, CM_BASE, 'CM_BASE');
  CheckEqI($B800, CN_BASE, 'CN_BASE');
  CheckEqB(True, (CM_BASE >= WM_APP) and (CN_BASE >= WM_APP), 'inside the WM_APP range');
  CheckEqI(CM_BASE + 1, CM_ENABLEDCHANGED, 'CM_ENABLEDCHANGED');
  CheckEqI(CM_BASE + 5, CM_TEXTCHANGED, 'CM_TEXTCHANGED');
  CheckEqI(CM_BASE + 10, CM_MOUSEENTER, 'CM_MOUSEENTER');
  CheckEqI(CM_BASE + 11, CM_MOUSELEAVE, 'CM_MOUSELEAVE');
  CheckEqI(CM_BASE + 16, CM_WANTSPECIALKEY, 'CM_WANTSPECIALKEY');
  CheckEqI(CM_BASE + 21, CM_CHANGED, 'CM_CHANGED');
  CheckEqI(CM_BASE + 27, CM_DPICHANGED, 'CM_DPICHANGED');
  CheckEqI(CN_BASE + WM_COMMAND, CN_COMMAND, 'CN_COMMAND');
  CheckEqI(CN_BASE + WM_NOTIFY, CN_NOTIFY, 'CN_NOTIFY');
  CheckEqI(CN_BASE + WM_CTLCOLORSTATIC, CN_CTLCOLORSTATIC, 'CN_CTLCOLORSTATIC');
end;

procedure CheckCursors;
var
  S: string;
  V: Longint;
  ToInt: TIdentToInt;
  ToIdent: TIntToIdent;
begin
  Section('cursor idents (UIF-021)');
  CheckEqI(0, crDefault, 'crDefault');
  CheckEqI(-21, crHandPoint, 'crHandPoint');
  CheckEqI(-22, crSizeAll, 'crSizeAll');
  CheckEqS('crDefault', CursorToString(crDefault), 'CursorToString crDefault');
  CheckEqS('crHandPoint', CursorToString(crHandPoint), 'CursorToString crHandPoint');
  CheckEqS('crNo', CursorToString(crNo), 'CursorToString crNo');
  CheckEqS('5', CursorToString(5), 'unnamed cursor as number');
  CheckEqI(crIBeam, StringToCursor('CRIBEAM'), 'StringToCursor is case-insensitive');
  CheckEqI(-5, StringToCursor('-5'), 'StringToCursor number');
  CheckEqB(True, CursorToIdent(crHSplit, S) and (S = 'crHSplit'), 'CursorToIdent');
  CheckEqB(True, IdentToCursor('crAppStart', V) and (V = crAppStart), 'IdentToCursor');
  CheckEqB(False, IdentToCursor('crNothing', V), 'unknown ident');
  ToInt := FindIdentToInt(TypeInfo(TCursor));
  ToIdent := FindIntToIdent(TypeInfo(TCursor));
  CheckEqB(True, Assigned(ToInt) and Assigned(ToIdent), 'TCursor registered');
  CheckEqB(True, Assigned(ToIdent) and ToIdent(crHourGlass, S) and (S = 'crHourGlass'), 'registered IntToIdent');
end;

procedure CheckConstraints;
var
  C, D: TSizeConstraints;
  R: TRecorder;
begin
  Section('TSizeConstraints');
  C := TSizeConstraints.Create;
  D := TSizeConstraints.Create;
  R := TRecorder.Create;
  try
    CheckEqI(0, C.MaxHeight, 'MaxHeight');
    CheckEqI(0, C.MaxWidth, 'MaxWidth');
    CheckEqI(0, C.MinHeight, 'MinHeight');
    CheckEqI(0, C.MinWidth, 'MinWidth');
    C.OnChange := R.Changed;
    C.MinWidth := 10;
    C.MinWidth := 10;
    C.MaxHeight := 32767;
    CheckEqI(2, R.Count, 'one OnChange per real change');
    Expect('EInvalidOperation', 'Value out of range (32768)');
    try
      C.MaxWidth := 32768;
      Check(False, 'MaxWidth 32768 raises');
    except
      on E: Exception do
        CheckRaises('MaxWidth 32768 raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidOperation', 'Value out of range (-1)');
    try
      C.MinHeight := -1;
      Check(False, 'MinHeight -1 raises');
    except
      on E: Exception do
        CheckRaises('MinHeight -1 raises', E.ClassName, E.Message);
    end;
    CheckEqI(2, R.Count, 'rejected values fire nothing');
    D.Assign(C);
    CheckEqI(10, D.MinWidth, 'Assign MinWidth');
    CheckEqI(32767, D.MaxHeight, 'Assign MaxHeight');
  finally
    C.Free;
    D.Free;
    R.Free;
  end;
end;

procedure CheckMargins;
var
  M, N: TMargins;
  P: TPadding;
  R: TRecorder;
begin
  Section('TMargins');
  M := TMargins.Create;
  N := TMargins.Create;
  P := TPadding.Create;
  R := TRecorder.Create;
  try
    CheckEqI(3, M.Left, 'Left');
    CheckEqI(3, M.Top, 'Top');
    CheckEqI(3, M.Right, 'Right');
    CheckEqI(3, M.Bottom, 'Bottom');
    M.OnChange := R.Changed;
    M.Left := 3;
    CheckEqI(0, R.Count, 'unchanged value fires nothing');
    M.Left := 10;
    CheckEqI(1, R.Count, 'Left fires OnChange');
    M.SetBounds(1, 2, 4, 5);
    CheckEqI(2, R.Count, 'SetBounds fires once');
    CheckEqS('1 2 4 5', IntToStr(M.Left) + ' ' + IntToStr(M.Top) + ' ' + IntToStr(M.Right) + ' ' + IntToStr(M.Bottom),
      'SetBounds values');
    M.SetBounds(1, 2, 4, 5);
    CheckEqI(2, R.Count, 'SetBounds unchanged fires nothing');
    Expect('EInvalidOperation', 'Value out of range (40000)');
    try
      M.Bottom := 40000;
      Check(False, 'Bottom 40000 raises');
    except
      on E: Exception do
        CheckRaises('Bottom 40000 raises', E.ClassName, E.Message);
    end;
    N.Assign(M);
    CheckEqS('1 2 4 5', IntToStr(N.Left) + ' ' + IntToStr(N.Top) + ' ' + IntToStr(N.Right) + ' ' + IntToStr(N.Bottom),
      'Assign');

    Section('TPadding');
    CheckEqB(True, P is TMargins, 'TPadding is a TMargins');
    CheckEqS('0 0 0 0', IntToStr(P.Left) + ' ' + IntToStr(P.Top) + ' ' + IntToStr(P.Right) + ' ' + IntToStr(P.Bottom),
      'defaults');
    P.Top := 8;
    CheckEqI(8, P.Top, 'Top');
    CheckEqI(3, GetPropInfo(TMargins, 'Left')^.Default, 'TMargins.Left RTTI default 3');
    CheckEqI(0, GetPropInfo(TPadding, 'Left')^.Default, 'TPadding.Left RTTI default 0');
    CheckEqI(0, GetPropInfo(TPadding, 'Bottom')^.Default, 'TPadding.Bottom RTTI default 0');
  finally
    M.Free;
    N.Free;
    P.Free;
    R.Free;
  end;
end;

begin
  CheckConstants;
  CheckCursors;
  CheckConstraints;
  CheckMargins;
  Done;
end.
