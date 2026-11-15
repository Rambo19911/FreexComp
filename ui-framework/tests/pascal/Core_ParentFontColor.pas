// covers: UIF-022, UIF-071
// needs: desktop
program Core_ParentFontColor;
{ U-027, core.md 1.1 and 7.5: Color, Font and ShowHint flow from a parent to every descendant whose Parent*
  flag is True (immediately on insertion, on every parent change, and when the flag is set True again); assigning
  the property clears the flag and breaks the chain below; the brush follows Color; a font change is pushed to the
  window (WM_SETFONT); a parent's WM_CTLCOLOR* for a child window is answered by the child (CN_CTLCOLOR*): text
  colour = Font.Color, background = Brush.Color, result = Brush.Handle; a foreign window gets default
  processing. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    property Color;
    property Font;
    property ParentColor;
    property ParentFont;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestEdit = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestGraphic = class(TControl)
  public
    property Color;
    property Font;
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TTestEdit.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'EDIT';
end;

procedure Run;
var
  Root: TTopWin;
  A, Inner, B: TTestWin;
  E: TTestEdit;
  G: TTestGraphic;
  DC: HDC;
  R: LRESULT;
begin
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 300, 200);
    A := TTestWin.Create(Root);
    A.Parent := Root;
    Inner := TTestWin.Create(Root);
    Inner.Parent := A;
    G := TTestGraphic.Create(Root);
    G.Parent := A;

    Section('colour (CM_COLORCHANGED, CM_PARENTCOLORCHANGED)');
    Root.Color := clRed;
    CheckEqB(True, (A.Color = clRed) and (Inner.Color = clRed) and (G.Color = clRed), 'descendants follow');
    CheckEqB(True, A.ParentColor and Inner.ParentColor, 'flags stay True');
    CheckEqB(False, Root.ParentColor, 'assignment cleared the flag');
    A.Color := clBlue;
    CheckEqB(False, A.ParentColor, 'assigning clears ParentColor');
    CheckEqB(True, (Inner.Color = clBlue) and (G.Color = clBlue), 'grandchildren follow the new colour');
    Root.Color := clGreen;
    CheckEqB(True, A.Color = clBlue, 'chain broken');
    CheckEqB(True, Inner.Color = clBlue, 'below the break too');
    A.ParentColor := True;
    CheckEqB(True, (A.Color = clGreen) and (Inner.Color = clGreen), 'ParentColor := True copies now');
    CheckEqB(True, A.Brush.Color = clGreen, 'brush follows Color');
    A.Color := clYellow;
    CheckEqB(True, A.Brush.Color = clYellow, 'brush follows an assignment');

    Section('font (CM_FONTCHANGED, CM_PARENTFONTCHANGED)');
    Root.Font.Size := 14;
    CheckEqI(14, A.Font.Size, 'child font follows');
    CheckEqI(14, Inner.Font.Size, 'grandchild font follows');
    CheckEqI(14, G.Font.Size, 'graphic child font follows');
    CheckEqB(True, A.ParentFont and Inner.ParentFont, 'flags stay True');
    A.Font.Name := 'Courier New';
    CheckEqB(False, A.ParentFont, 'changing a sub-property clears ParentFont');
    CheckEqS('Courier New', Inner.Font.Name, 'grandchild follows the child');
    Root.Font.Size := 9;
    CheckEqI(14, A.Font.Size, 'chain broken');
    A.ParentFont := True;
    CheckEqI(9, A.Font.Size, 'ParentFont := True copies now');
    CheckEqS(Root.Font.Name, A.Font.Name, 'whole font copied');
    CheckEqI(9, Inner.Font.Size, 'and passes it on');
    B := TTestWin.Create(Root);
    B.Parent := Root;
    CheckEqI(9, B.Font.Size, 'inserted child takes the parent font');

    Section('font pushed to the window');
    E := TTestEdit.Create(Root);
    E.Parent := Root;
    Root.HandleNeeded;
    Root.Font.Size := 12;
    CheckEqI(12, E.Font.Size, 'edit follows');
    CheckEqB(True, HFONT(SendMessageW(E.Handle, WM_GETFONT, 0, 0)) = E.Font.Handle, 'WM_SETFONT with the new handle');

    Section('ShowHint (CM_PARENTSHOWHINTCHANGED)');
    Root.ShowHint := True;
    CheckEqB(True, A.ShowHint and Inner.ShowHint and G.ShowHint, 'descendants follow');
    A.ShowHint := False;
    CheckEqB(False, A.ParentShowHint, 'assigning clears ParentShowHint');
    CheckEqB(False, Inner.ShowHint, 'grandchild follows the child');
    Root.ShowHint := False;
    Root.ShowHint := True;
    CheckEqB(False, A.ShowHint, 'chain broken');
    A.ParentShowHint := True;
    CheckEqB(True, A.ShowHint and Inner.ShowHint, 'ParentShowHint := True copies now');

    Section('WM_CTLCOLOR* reflection (UIF-071)');
    E.Color := clYellow;
    E.Font.Color := clRed;
    DC := CreateCompatibleDC(0);
    try
      R := SendMessageW(Root.Handle, WM_CTLCOLOREDIT, WPARAM(DC), LPARAM(E.Handle));
      CheckEqB(True, HBRUSH(R) = E.Brush.Handle, 'result is the child brush');
      CheckEqI(ColorToRGB(clRed), GetTextColor(DC), 'text colour from Font.Color');
      CheckEqI(ColorToRGB(clYellow), GetBkColor(DC), 'background from Brush.Color');
      R := SendMessageW(Root.Handle, WM_CTLCOLORSTATIC, WPARAM(DC), LPARAM(E.Handle));
      CheckEqB(True, HBRUSH(R) = E.Brush.Handle, 'every WM_CTLCOLOR* id');
      R := SendMessageW(Root.Handle, WM_CTLCOLOREDIT, WPARAM(DC), LPARAM(GetDesktopWindow));
      CheckEqB(True, (R <> 0) and (HBRUSH(R) <> E.Brush.Handle), 'foreign window: default processing');
    finally
      DeleteDC(DC);
    end;
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
