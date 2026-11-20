{ ExtCtrls - extended controls (controls-standard.md section 8). }
unit ExtCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls;

type
  TBevelWidth = 1..32767;
  TBorderWidth = 0..32767;

  { controls-standard.md section 8 }
  TCustomPanel = class(TCustomControl)
  private
    FAlignment: TAlignment;
    FBevelInner: TBevelCut;
    FBevelOuter: TBevelCut;
    FBevelWidth: TBevelWidth;
    FBorderStyle: TBorderStyle;
    FBorderWidth: TBorderWidth;
    FShowCaption: Boolean;
    FVerticalAlignment: TVerticalAlignment;
    procedure SetAlignment(Value: TAlignment);
    procedure SetBevelInner(Value: TBevelCut);
    procedure SetBevelOuter(Value: TBevelCut);
    procedure SetBevelWidth(Value: TBevelWidth);
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetBorderWidth(Value: TBorderWidth);
    procedure SetShowCaption(Value: Boolean);
    procedure SetVerticalAlignment(Value: TVerticalAlignment);
    procedure DrawBevel(var R: TRect; Cut: TBevelCut);
  protected
    { style WS_BORDER when BorderStyle = bsSingle }
    procedure CreateParams(var Params: TCreateParams); override;
    { layout.md UIF-215: bsSingle loses one device pixel on each side }
    function GetClientRect: TRect; override;
    { UIF-680 }
    procedure Paint; override;
    { UIF-681: CM_BORDERCHANGED realigns and repaints; text, font and enabled changes repaint }
    procedure WndProc(var Message: TMessage); override;
    { layout.md UIF-280 step 4: BorderWidth is scaled }
    procedure ChangeScale(M, D: Integer); override;
    property Alignment: TAlignment read FAlignment write SetAlignment default taCenter;
    property BevelInner: TBevelCut read FBevelInner write SetBevelInner default bvNone;
    property BevelOuter: TBevelCut read FBevelOuter write SetBevelOuter default bvRaised;
    property BevelWidth: TBevelWidth read FBevelWidth write SetBevelWidth default 1;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsNone;
    property BorderWidth: TBorderWidth read FBorderWidth write SetBorderWidth default 0;
    property ShowCaption: Boolean read FShowCaption write SetShowCaption default True;
    property VerticalAlignment: TVerticalAlignment read FVerticalAlignment write SetVerticalAlignment
      default taVerticalCenter;
  public
    constructor Create(AOwner: TComponent); override;
    { layout.md UIF-218: inset by BorderWidth and the thickness of each bevel that is not bvNone }
    procedure AdjustClientRect(var Rect: TRect); override;
  end;

  TPanel = class(TCustomPanel)
  published
    property Alignment;
    property BevelInner;
    property BevelOuter;
    property BevelWidth;
    property BorderStyle;
    property BorderWidth;
    property Caption;
    property Color default clBtnFace;
    property Font;
    property Padding;
    property ParentColor default False;
    property ParentFont;
    property ShowCaption;
    property TabOrder;
    property TabStop;
    property VerticalAlignment;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnResize;
  end;

implementation

{ ---------------------------------------------------------------- TCustomPanel }

constructor TCustomPanel.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  SetBounds(Left, Top, 185, 41);
  Color := clBtnFace;
  FAlignment := taCenter;
  FBevelOuter := bvRaised;
  FBevelInner := bvNone;
  FBevelWidth := 1;
  FBorderStyle := bsNone;
  FShowCaption := True;
  FVerticalAlignment := taVerticalCenter;
end;

procedure TCustomPanel.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  if FBorderStyle = bsSingle then
    Params.Style := Params.Style or WS_BORDER;
end;

function TCustomPanel.GetClientRect: TRect;
begin
  if FBorderStyle = bsSingle then
  begin
    Result := Rect(0, 0, Width - 2, Height - 2);
    if Result.Right < 0 then
      Result.Right := 0;
    if Result.Bottom < 0 then
      Result.Bottom := 0;
  end
  else
    Result := inherited GetClientRect;
end;

procedure TCustomPanel.AdjustClientRect(var Rect: TRect);
var
  I: Integer;
begin
  I := FBorderWidth;
  if FBevelOuter <> bvNone then
    I := I + FBevelWidth;
  if FBevelInner <> bvNone then
    I := I + FBevelWidth;
  Rect.Left := Rect.Left + I;
  Rect.Top := Rect.Top + I;
  Rect.Right := Rect.Right - I;
  Rect.Bottom := Rect.Bottom - I;
end;

{ one bevel of FBevelWidth lines around R; R shrinks by the bevel's thickness (bvNone: nothing, no thickness) }
procedure TCustomPanel.DrawBevel(var R: TRect; Cut: TBevelCut);
var
  I: Integer;
  TopLeft, BottomRight: TColor;
begin
  if Cut = bvNone then
    Exit;
  case Cut of
    bvRaised:
      begin
        TopLeft := clBtnHighlight;
        BottomRight := clBtnShadow;
      end;
    bvLowered:
      begin
        TopLeft := clBtnShadow;
        BottomRight := clBtnHighlight;
      end;
  else
    begin
      TopLeft := Color;
      BottomRight := Color;
    end;
  end;
  for I := 1 to FBevelWidth do
  begin
    if (R.Right <= R.Left) or (R.Bottom <= R.Top) then
      Break;
    Canvas.Brush.Color := TopLeft;
    Canvas.FillRect(Rect(R.Left, R.Top, R.Right, R.Top + 1));
    Canvas.FillRect(Rect(R.Left, R.Top, R.Left + 1, R.Bottom));
    Canvas.Brush.Color := BottomRight;
    Canvas.FillRect(Rect(R.Left, R.Bottom - 1, R.Right, R.Bottom));
    Canvas.FillRect(Rect(R.Right - 1, R.Top, R.Right, R.Bottom));
    InflateRect(R, -1, -1);
  end;
end;

procedure TCustomPanel.Paint;
const
  Horizontal: array[TAlignment] of TTextFormats = (tfLeft, tfRight, tfCenter);
  Vertical: array[TVerticalAlignment] of TTextFormats = (tfTop, tfBottom, tfVerticalCenter);
var
  R: TRect;
  Text: string;
begin
  R := ClientRect;
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := Color;
  Canvas.FillRect(R);
  DrawBevel(R, FBevelOuter);
  InflateRect(R, -FBorderWidth, -FBorderWidth);
  DrawBevel(R, FBevelInner);
  if not FShowCaption then
    Exit;
  Text := Caption;
  if Text = '' then
    Exit;
  { the align rect (layout.md section 3.3) }
  R := ClientRect;
  AdjustClientRect(R);
  R.Left := R.Left + Padding.Left;
  R.Top := R.Top + Padding.Top;
  R.Right := R.Right - Padding.Right;
  R.Bottom := R.Bottom - Padding.Bottom;
  Canvas.Font := Font;
  if not Enabled then
    Canvas.Font.Color := clGrayText;
  Canvas.Brush.Style := bsClear;
  SetBkMode(Canvas.Handle, Windows.TRANSPARENT);
  Canvas.TextRect(R, Text, [tfSingleLine, Horizontal[FAlignment], Vertical[FVerticalAlignment]]);
end;

procedure TCustomPanel.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_BORDERCHANGED:
      begin
        Realign;
        Invalidate;
      end;
    CM_TEXTCHANGED, CM_FONTCHANGED, CM_ENABLEDCHANGED:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomPanel.ChangeScale(M, D: Integer);
begin
  if (M <> D) and (M > 0) and (D > 0) then
    FBorderWidth := LayoutMulDiv(FBorderWidth, M, D);
  inherited ChangeScale(M, D);
end;

procedure TCustomPanel.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    Invalidate;
  end;
end;

procedure TCustomPanel.SetBevelInner(Value: TBevelCut);
begin
  if FBevelInner <> Value then
  begin
    FBevelInner := Value;
    Perform(CM_BORDERCHANGED, 0, 0);
  end;
end;

procedure TCustomPanel.SetBevelOuter(Value: TBevelCut);
begin
  if FBevelOuter <> Value then
  begin
    FBevelOuter := Value;
    Perform(CM_BORDERCHANGED, 0, 0);
  end;
end;

procedure TCustomPanel.SetBevelWidth(Value: TBevelWidth);
begin
  if FBevelWidth <> Value then
  begin
    FBevelWidth := Value;
    Perform(CM_BORDERCHANGED, 0, 0);
  end;
end;

procedure TCustomPanel.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    RecreateWnd;
    Perform(CM_BORDERCHANGED, 0, 0);
  end;
end;

procedure TCustomPanel.SetBorderWidth(Value: TBorderWidth);
begin
  if FBorderWidth <> Value then
  begin
    FBorderWidth := Value;
    Perform(CM_BORDERCHANGED, 0, 0);
  end;
end;

procedure TCustomPanel.SetShowCaption(Value: Boolean);
begin
  if FShowCaption <> Value then
  begin
    FShowCaption := Value;
    Invalidate;
  end;
end;

procedure TCustomPanel.SetVerticalAlignment(Value: TVerticalAlignment);
begin
  if FVerticalAlignment <> Value then
  begin
    FVerticalAlignment := Value;
    Invalidate;
  end;
end;

initialization
  RegisterClasses([TPanel]);
end.
