// covers: UIF-1301
// needs: desktop
// phase: 8
program Ctl_RichEditLibrary;
{ U-144, rich-edit.md UIF-1301: the engine library cannot be loaded - CreateWnd raises EOSError with
  SRichEditLibrary followed by the system error text, the control stays without a handle and the program goes on;
  with the real library name the same control gets its window. The library name is the protected class-level seam
  of TCustomRichEdit. The system error text depends on the language of the system, so it is compared with
  SysErrorMessage and never printed. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls, RichEdit, UIConsts, TestKit;

type
  TSeamRichEdit = class(TRichEdit)
  public
    class procedure UseLibrary(const Name: string);
  end;

class procedure TSeamRichEdit.UseLibrary(const Name: string);
begin
  FLibraryName := Name;
end;

procedure Run;
var
  F: TForm;
  R: TSeamRichEdit;
  Raised, Msg: string;
  Code: Cardinal;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  try
    R := TSeamRichEdit.Create(F);
    R.Parent := F;
    R.Text := 'survives';

    Section('the library is missing (UIF-1301)');
    Check(GetModuleHandleW(MSFTEDIT_DLL) = 0, 'the engine is not loaded before the first window');
    TSeamRichEdit.UseLibrary('FreexCompNoSuchEngine.dll');
    Raised := '';
    Msg := '';
    Code := 0;
    try
      R.HandleNeeded;
    except
      on E: EOSError do
      begin
        Raised := E.ClassName;
        Msg := E.Message;
        Code := E.ErrorCode;
      end;
    end;
    CheckEqS('EOSError', Raised, 'CreateWnd raises EOSError');
    CheckEqS(SRichEditLibrary, Copy(Msg, 1, Length(SRichEditLibrary)), 'the message starts with SRichEditLibrary');
    Check(Code <> 0, 'ErrorCode is the system error');
    Check(Copy(Msg, Length(SRichEditLibrary) + 1, MaxInt) = ': ' + SysErrorMessage(Code),
      'followed by the system error text');
    Check(not R.HandleAllocated, 'the control has no handle');
    CheckEqS('survives', R.Text, 'the control keeps its state');
    Check(GetModuleHandleW(MSFTEDIT_DLL) = 0, 'nothing was loaded');
    Raised := '';
    try
      R.HandleNeeded;
    except
      on E: EOSError do
        Raised := E.ClassName;
    end;
    CheckEqS('EOSError', Raised, 'a second attempt fails the same way');

    Section('recovery with the real library');
    TSeamRichEdit.UseLibrary(MSFTEDIT_DLL);
    R.HandleNeeded;
    Check(R.HandleAllocated, 'the same control gets its window');
    Check(GetModuleHandleW(MSFTEDIT_DLL) <> 0, 'the engine is loaded');
    CheckEqS('survives', R.Text, 'with its text');
    TSeamRichEdit.UseLibrary('FreexCompNoSuchEngine.dll');
    R.DestroyHandle;
    R.HandleNeeded;
    Check(R.HandleAllocated, 'the library stays loaded: the name is not used again');
  finally
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
