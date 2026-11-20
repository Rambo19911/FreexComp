// covers: UIF-272, UIF-274
// needs: desktop
program Form_MenuClientSize;
{ U-074, layout.md UIF-274: a main menu occupies non-client area. Assigning or removing Form.Menu keeps
  ClientWidth/ClientHeight and changes the outer height by the menu bar (the frame of AdjustWindowRectExForDpi with
  bMenu True), with and without a window; the native client rectangle equals the stored client size; the menu bar
  is attached from the window's creation (also after RecreateWnd); freeing the menu detaches it and keeps the
  client size; destroying the form window keeps the menu bar alive; a .form with Menu = MainMenu1 loads with its
  stored client size. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, Menus, TestKit;

type
  TMenuSizeForm = class(TForm)
  published
    MainMenu1: TMainMenu;
    FileItem: TMenuItem;
  end;

{$FORM Form_MenuClientSize.form}

function NativeClient(F: TForm): string;
var
  R: TRect;
begin
  Windows.GetClientRect(F.Handle, R);
  Result := Format('%dx%d', [R.Right, R.Bottom]);
end;

function ClientStr(F: TForm): string;
begin
  Result := Format('%dx%d', [F.ClientWidth, F.ClientHeight]);
end;

function WindowHeight(F: TForm): Integer;
var
  R: TRect;
begin
  GetWindowRect(F.Handle, R);
  Result := R.Bottom - R.Top;
end;

function NewMenu: TMainMenu;
var
  Item: TMenuItem;
begin
  Result := TMainMenu.Create(nil);
  Item := TMenuItem.Create(Result);
  Item.Caption := '&File';
  Result.Items.Add(Item);
end;

procedure Run;
var
  F: TForm;
  SF: TMenuSizeForm;
  M, M2: TMainMenu;
  H0, Bar, H: Integer;
  Bar0: HMENU;
begin
  Application.Initialize;
  M := NewMenu;
  M2 := NewMenu;
  try
    Section('without a window');
    F := TForm.CreateNew(nil);
    try
      F.SetBounds(60, 60, 400, 300);
      F.ClientWidth := 320;
      F.ClientHeight := 180;
      H0 := F.Height;
      Bar := GetSystemMetricsForDpi(SM_CYMENU, F.PixelsPerInch);
      F.Menu := M;
      CheckEqS('320x180', ClientStr(F), 'assigning Menu keeps the client size');
      CheckEqI(H0 + Bar, F.Height, 'outer height grows by the menu bar');
      F.Menu := nil;
      CheckEqS('320x180', ClientStr(F), 'removing Menu keeps the client size');
      CheckEqI(H0, F.Height, 'outer height back');
      F.Menu := M;

      Section('with a window');
      F.HandleNeeded;
      Bar := GetSystemMetricsForDpi(SM_CYMENU, F.PixelsPerInch);
      CheckEqB(True, GetMenu(F.Handle) = M.Handle, 'menu bar attached at creation');
      CheckEqB(True, M.WindowHandle = F.Handle, 'WindowHandle');
      CheckEqS('320x180', ClientStr(F), 'client size kept at creation');
      CheckEqS(ClientStr(F), NativeClient(F), 'native client = stored client');
      CheckEqI(F.Height, WindowHeight(F), 'outer height = window height');
      H := F.Height;
      F.Menu := nil;
      CheckEqB(True, GetMenu(F.Handle) = 0, 'removed from the window');
      CheckEqB(True, M.WindowHandle = 0, 'WindowHandle cleared');
      CheckEqS('320x180', ClientStr(F), 'removing keeps the client size');
      CheckEqS(ClientStr(F), NativeClient(F), 'native client after removing');
      CheckEqI(H - Bar, F.Height, 'outer height shrinks by the menu bar');
      CheckEqI(F.Height, WindowHeight(F), 'window shrank');
      F.Menu := M;
      CheckEqB(True, GetMenu(F.Handle) = M.Handle, 'assigned again');
      CheckEqS('320x180', ClientStr(F), 'assigning keeps the client size');
      CheckEqS(ClientStr(F), NativeClient(F), 'native client after assigning');
      CheckEqI(H, F.Height, 'outer height grows again');
      F.Menu := M2;
      CheckEqB(True, GetMenu(F.Handle) = M2.Handle, 'replaced by another menu');
      CheckEqB(True, M.WindowHandle = 0, 'old menu released');
      CheckEqI(1, GetMenuItemCount(M.Handle), 'old menu bar still alive');
      CheckEqS('320x180', ClientStr(F), 'replacing keeps the client size');
      F.Menu := M;

      Section('re-creation');
      Bar0 := M.Handle;
      F.BorderStyle := bsSingle;
      CheckEqB(True, GetMenu(F.Handle) = Bar0, 'attached again after RecreateWnd');
      CheckEqS('320x180', ClientStr(F), 'client size after RecreateWnd');
      CheckEqS(ClientStr(F), NativeClient(F), 'native client after RecreateWnd');

      Section('freeing the menu');
      M.Free;
      M := nil;
      CheckEqB(True, F.Menu = nil, 'Menu cleared');
      CheckEqB(True, GetMenu(F.Handle) = 0, 'detached');
      CheckEqS('320x180', ClientStr(F), 'client size kept');
      CheckEqS(ClientStr(F), NativeClient(F), 'native client kept');

      Section('destroying the window');
      F.Menu := M2;
      Bar0 := M2.Handle;
    finally
      F.Free;
    end;
    CheckEqI(1, GetMenuItemCount(Bar0), 'the menu bar outlives the form window');
    CheckEqB(True, M2.WindowHandle = 0, 'WindowHandle cleared with the window');

    Section('streaming');
    SF := TMenuSizeForm.Create(nil);
    try
      CheckEqB(True, SF.Menu = SF.MainMenu1, 'Menu reference resolved');
      CheckEqS('320x180', ClientStr(SF), 'stored client size');
      SF.HandleNeeded;
      CheckEqB(True, GetMenu(SF.Handle) = SF.MainMenu1.Handle, 'attached');
      CheckEqS('320x180', ClientStr(SF), 'client size with the window');
      CheckEqS(ClientStr(SF), NativeClient(SF), 'native client');
    finally
      SF.Free;
    end;
  finally
    M.Free;
    M2.Free;
  end;
end;

begin
  Run;
  Done;
end.
