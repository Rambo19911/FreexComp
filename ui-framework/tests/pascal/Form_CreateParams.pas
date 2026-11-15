// covers: UIF-405, UIF-425, UIF-430, UIF-442
// opc: -U ..\..\src
// needs: desktop
program Form_CreateParams;
{ U-036, application-forms.md 4: CreateNew defaults; the published properties of TForm (Width/Height not stored,
  ClientWidth/ClientHeight always stored); the window class PN.TForm, created hidden, never maximised or minimised
  at creation; the style table per BorderStyle, the BorderIcons rules, WS_EX_APPWINDOW for the main form,
  WS_EX_TOPMOST for fsStayOnTop, the owner window (main form's handle for later forms); BorderStyle changes recreate
  the window; MDI styles raise when the handle is created. CreateForm (UIF-405) sets the reference before OnCreate,
  makes the first TForm the main form; OnCreate (AfterConstruction) and OnDestroy (BeforeDestruction) fire. }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, Forms, TestKit;

const
  StyleMask = WS_POPUP or WS_CHILD or WS_CAPTION or WS_THICKFRAME or WS_SYSMENU or WS_MINIMIZEBOX or WS_MAXIMIZEBOX
    or WS_CLIPCHILDREN or WS_CLIPSIBLINGS or WS_VISIBLE or WS_MAXIMIZE or WS_MINIMIZE;
  ExMask = WS_EX_DLGMODALFRAME or WS_EX_TOOLWINDOW or WS_EX_CONTROLPARENT or WS_EX_APPWINDOW or WS_EX_TOPMOST
    or WS_EX_CONTEXTHELP;
  Clip = WS_CLIPCHILDREN or WS_CLIPSIBLINGS;

type
  TLog = class
  public
    Text: string;
    procedure Created(Sender: TObject);
    procedure Destroyed(Sender: TObject);
  end;

  TMainForm = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  Log: TLog;
  MainForm: TMainForm;
  SeenInCreate: Boolean;

procedure TLog.Created(Sender: TObject);
begin
  Text := Text + 'Create;';
  SeenInCreate := MainForm = Sender;
end;

procedure TLog.Destroyed(Sender: TObject);
begin
  Text := Text + 'Destroy;';
end;

{ a form class without a .form: CreateNew instead of streaming }
constructor TMainForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  OnCreate := Log.Created;
  OnDestroy := Log.Destroyed;
end;

function StyleOf(F: TCustomForm): Cardinal;
begin
  Result := Cardinal(GetWindowLongPtrW(F.Handle, GWL_STYLE)) and StyleMask;
end;

function ExStyleOf(F: TCustomForm): Cardinal;
begin
  Result := Cardinal(GetWindowLongPtrW(F.Handle, GWL_EXSTYLE)) and ExMask;
end;

function ClassOf(Wnd: HWND): string;
var
  Buffer: array[0..63] of Char;
begin
  GetClassNameW(Wnd, @Buffer[0], 64);
  Result := string(PChar(@Buffer[0]));
end;

function TextOf(Wnd: HWND): string;
var
  Buffer: array[0..63] of Char;
begin
  GetWindowTextW(Wnd, @Buffer[0], 64);
  Result := string(PChar(@Buffer[0]));
end;

procedure CheckStyles(BS: TFormBorderStyle; BI: TBorderIcons; Style, ExStyle: Cardinal; const Name: string);
var
  F: TForm;
begin
  F := TForm.CreateNew(nil);
  try
    F.BorderStyle := BS;
    F.BorderIcons := BI;
    CheckEqI(Style, StyleOf(F), Name + ': Style');
    CheckEqI(ExStyle, ExStyleOf(F), Name + ': ExStyle');
  finally
    F.Free;
  end;
end;

procedure Run;
var
  F, G: TForm;
  H: HWND;
  All: TBorderIcons;
begin
  Log := TLog.Create;
  try
    Section('CreateNew defaults');
    F := TForm.CreateNew(nil);
    try
      CheckEqB(False, F.Visible, 'Visible False');
      CheckEqI(clBtnFace, F.Color, 'Color clBtnFace');
      CheckEqI(320, F.ClientWidth, 'ClientWidth 320');
      CheckEqI(240, F.ClientHeight, 'ClientHeight 240');
      CheckEqI(0, F.Left, 'Left 0');
      CheckEqI(0, F.Top, 'Top 0');
      CheckEqB(True, F.BorderStyle = bsSizeable, 'BorderStyle bsSizeable');
      CheckEqB(True, F.BorderIcons = [biSystemMenu, biMinimize, biMaximize], 'BorderIcons');
      CheckEqB(True, F.FormStyle = fsNormal, 'FormStyle fsNormal');
      CheckEqB(True, F.Position = poDesigned, 'Position poDesigned');
      CheckEqB(True, F.WindowState = wsNormal, 'WindowState wsNormal');
      CheckEqB(True, F.Scaled, 'Scaled True');
      CheckEqB(False, F.KeyPreview, 'KeyPreview False');
      CheckEqI(GetDpiForSystem, F.PixelsPerInch, 'PixelsPerInch: current DPI');
      CheckEqB(True, F.ActiveControl = nil, 'ActiveControl nil');
      CheckEqS('', F.Caption, 'Caption empty');
      CheckEqB(False, F.HandleAllocated, 'no handle during construction');
      CheckEqB(True, F.ModalResult = mrNone, 'ModalResult mrNone');

      Section('published properties');
      CheckEqB(True, IsPublishedProp(F, 'BorderIcons') and IsPublishedProp(F, 'BorderStyle')
        and IsPublishedProp(F, 'ClientWidth') and IsPublishedProp(F, 'ClientHeight')
        and IsPublishedProp(F, 'PixelsPerInch') and IsPublishedProp(F, 'Position')
        and IsPublishedProp(F, 'FormStyle') and IsPublishedProp(F, 'KeyPreview') and IsPublishedProp(F, 'Scaled')
        and IsPublishedProp(F, 'WindowState') and IsPublishedProp(F, 'ActiveControl')
        and IsPublishedProp(F, 'Caption') and IsPublishedProp(F, 'Color') and IsPublishedProp(F, 'Font'),
        'properties of the table');
      CheckEqB(True, IsPublishedProp(F, 'OnCreate') and IsPublishedProp(F, 'OnDestroy')
        and IsPublishedProp(F, 'OnShow') and IsPublishedProp(F, 'OnHide') and IsPublishedProp(F, 'OnClose')
        and IsPublishedProp(F, 'OnCloseQuery') and IsPublishedProp(F, 'OnActivate')
        and IsPublishedProp(F, 'OnDeactivate') and IsPublishedProp(F, 'OnPaint') and IsPublishedProp(F, 'OnResize')
        and IsPublishedProp(F, 'OnKeyDown') and IsPublishedProp(F, 'OnKeyPress') and IsPublishedProp(F, 'OnKeyUp')
        and IsPublishedProp(F, 'OnMouseDown') and IsPublishedProp(F, 'OnMouseWheel') and IsPublishedProp(F, 'OnClick')
        and IsPublishedProp(F, 'OnAfterMonitorDpiChanged') and IsPublishedProp(F, 'OnContextPopup'),
        'events');
      CheckEqB(False, IsStoredProp(F, 'Width'), 'Width not stored');
      CheckEqB(False, IsStoredProp(F, 'Height'), 'Height not stored');
      CheckEqB(True, IsStoredProp(F, 'ClientWidth'), 'ClientWidth stored');
      CheckEqB(True, IsStoredProp(F, 'ClientHeight'), 'ClientHeight stored');
      CheckEqB(True, IsStoredProp(F, 'PixelsPerInch'), 'PixelsPerInch stored');

      Section('window');
      F.Caption := 'Hello';
      H := F.Handle;
      CheckEqS('PN.TForm', ClassOf(H), 'window class PN.TForm');
      CheckEqS('Hello', TextOf(H), 'Caption is the window title');
      CheckEqB(False, IsWindowVisible(H), 'created hidden');
      CheckEqB(True, GetWindow(H, GW_OWNER) = 0, 'no main form: no owner');
      F.Caption := 'Changed';
      CheckEqS('Changed', TextOf(H), 'Caption change updates the title');
      F.BorderStyle := bsDialog;
      CheckEqB(True, (StyleOf(F) and WS_THICKFRAME) = 0, 'BorderStyle change recreates the window');
    finally
      F.Free;
    end;

    Section('style table (UIF-425)');
    All := [biSystemMenu, biMinimize, biMaximize];
    CheckStyles(bsNone, All, WS_POPUP or Clip, WS_EX_CONTROLPARENT, 'bsNone');
    CheckStyles(bsSingle, All, WS_CAPTION or WS_SYSMENU or WS_MINIMIZEBOX or WS_MAXIMIZEBOX or Clip,
      WS_EX_CONTROLPARENT, 'bsSingle');
    CheckStyles(bsSizeable, All, WS_CAPTION or WS_THICKFRAME or WS_SYSMENU or WS_MINIMIZEBOX or WS_MAXIMIZEBOX
      or Clip, WS_EX_CONTROLPARENT, 'bsSizeable');
    CheckStyles(bsDialog, All, WS_POPUP or WS_CAPTION or WS_SYSMENU or Clip,
      WS_EX_DLGMODALFRAME or WS_EX_CONTROLPARENT, 'bsDialog');
    CheckStyles(bsToolWindow, All, WS_CAPTION or WS_SYSMENU or Clip, WS_EX_TOOLWINDOW or WS_EX_CONTROLPARENT,
      'bsToolWindow');
    CheckStyles(bsSizeToolWin, All, WS_CAPTION or WS_THICKFRAME or WS_SYSMENU or Clip,
      WS_EX_TOOLWINDOW or WS_EX_CONTROLPARENT, 'bsSizeToolWin');

    Section('BorderIcons');
    CheckStyles(bsSizeable, [], WS_CAPTION or WS_THICKFRAME or Clip, WS_EX_CONTROLPARENT, 'none');
    CheckStyles(bsSizeable, [biMinimize, biMaximize], WS_CAPTION or WS_THICKFRAME or Clip, WS_EX_CONTROLPARENT,
      'boxes need biSystemMenu');
    CheckStyles(bsSingle, [biSystemMenu, biMinimize], WS_CAPTION or WS_SYSMENU or WS_MINIMIZEBOX or Clip,
      WS_EX_CONTROLPARENT, 'minimise box only');
    CheckStyles(bsSingle, [biSystemMenu, biMinimize, biHelp], WS_CAPTION or WS_SYSMENU or WS_MINIMIZEBOX or Clip,
      WS_EX_CONTROLPARENT, 'biHelp with a box: no help button');
    CheckStyles(bsSingle, [biSystemMenu, biHelp], WS_CAPTION or WS_SYSMENU or Clip,
      WS_EX_CONTEXTHELP or WS_EX_CONTROLPARENT, 'biHelp without boxes');
    CheckStyles(bsDialog, [biSystemMenu, biMaximize, biHelp], WS_POPUP or WS_CAPTION or WS_SYSMENU or Clip,
      WS_EX_DLGMODALFRAME or WS_EX_CONTEXTHELP or WS_EX_CONTROLPARENT, 'bsDialog: no boxes, help button');
    CheckStyles(bsNone, [biSystemMenu, biHelp], WS_POPUP or Clip, WS_EX_CONTROLPARENT, 'bsNone ignores BorderIcons');

    Section('FormStyle');
    F := TForm.CreateNew(nil);
    try
      F.FormStyle := fsStayOnTop;
      CheckEqI(WS_EX_TOPMOST, ExStyleOf(F) and WS_EX_TOPMOST, 'fsStayOnTop: WS_EX_TOPMOST');
      F.WindowState := wsMaximized;
      F.FormStyle := fsNormal;
      CheckEqI(0, StyleOf(F) and (WS_MAXIMIZE or WS_VISIBLE), 'never maximised or visible at creation');
      F.DestroyHandle;
      F.FormStyle := fsMDIForm;
      CheckEqB(False, F.HandleAllocated, 'FormStyle without a handle: nothing created');
      Expect('ENotSupportedException', 'MDI forms are not supported');
      try
        F.HandleNeeded;
        CheckRaises('fsMDIForm raises at handle creation', '', '');
      except
        on E: Exception do
          CheckRaises('fsMDIForm raises at handle creation', E.ClassName, E.Message);
      end;
    finally
      F.Free;
    end;

    Section('CreateForm and the main form (UIF-405)');
    CheckEqB(True, Application.MainForm = nil, 'no main form yet');
    Application.CreateForm(TMainForm, MainForm);
    CheckEqS('Create;', Log.Text, 'OnCreate fired');
    CheckEqB(True, SeenInCreate, 'the reference is set during OnCreate');
    CheckEqB(True, Application.MainForm = MainForm, 'first form is the main form');
    CheckEqB(True, MainForm.Owner = Application, 'owned by Application');
    CheckEqB(False, MainForm.HandleAllocated, 'main form window created lazily');
    G := TForm.CreateNew(nil);
    try
      H := G.Handle;
      CheckEqB(True, GetWindow(H, GW_OWNER) = 0, 'main form without handle: no owner');
      G.DestroyHandle;
      CheckEqI(WS_EX_APPWINDOW, ExStyleOf(MainForm) and WS_EX_APPWINDOW, 'main form: WS_EX_APPWINDOW');
      CheckEqB(True, GetWindow(MainForm.Handle, GW_OWNER) = 0, 'main form: no owner');
      CheckEqI(0, ExStyleOf(G) and WS_EX_APPWINDOW, 'other form: no WS_EX_APPWINDOW');
      CheckEqB(True, GetWindow(G.Handle, GW_OWNER) = MainForm.Handle, 'other form: owned by the main form');
    finally
      G.Free;
    end;
    Log.Text := '';
    MainForm.Free;
    CheckEqS('Destroy;', Log.Text, 'OnDestroy fired');
    CheckEqB(True, Application.MainForm = nil, 'destroyed main form: MainForm nil');
  finally
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
