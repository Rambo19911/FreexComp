{ ExtCtrls - extended controls (controls-standard.md sections 8, 14, 15, 16). }
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

  { controls-standard.md section 14 }
  TImage = class(TGraphicControl)
  private
    FCenter: Boolean;
    FPicture: TPicture;
    FProportional: Boolean;
    FStretch: Boolean;
    FTransparent: Boolean;
    procedure PictureChanged(Sender: TObject);
    procedure SetCenter(Value: Boolean);
    procedure SetPicture(Value: TPicture);
    procedure SetProportional(Value: Boolean);
    procedure SetStretch(Value: Boolean);
    procedure SetTransparent(Value: Boolean);
  protected
    { UIF-740: StretchDraw into DestRect }
    procedure Paint; override;
    { layout.md UIF-255: the picture size (outside loading, scaling and dump mode; no change when empty) }
    procedure AdjustSize; override;
    { UIF-740: the destination rectangle of the picture in client coordinates (empty when nothing is drawn) }
    function DestRect: TRect;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property AutoSize default False;
    property Center: Boolean read FCenter write SetCenter default False;
    property Picture: TPicture read FPicture write SetPicture;
    property Proportional: Boolean read FProportional write SetProportional default False;
    property Stretch: Boolean read FStretch write SetStretch default False;
    property Transparent: Boolean read FTransparent write SetTransparent default False;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  TShapeType = (stRectangle, stSquare, stRoundRect, stRoundSquare, stEllipse, stCircle);

  { controls-standard.md section 15 }
  TShape = class(TGraphicControl)
  private
    FBrush: TBrush;
    FPen: TPen;
    FShape: TShapeType;
    procedure StyleChanged(Sender: TObject);
    procedure SetBrush(Value: TBrush);
    procedure SetPen(Value: TPen);
    procedure SetShape(Value: TShapeType);
  protected
    { UIF-745 }
    procedure Paint; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Brush: TBrush read FBrush write SetBrush;
    property Pen: TPen read FPen write SetPen;
    property Shape: TShapeType read FShape write SetShape default stRectangle;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

  TBevelShape = (bsBox, bsFrame, bsTopLine, bsBottomLine, bsLeftLine, bsRightLine, bsSpacer);
  TBevelStyle = (bsLowered, bsRaised);

  { controls-standard.md section 16 }
  TBevel = class(TGraphicControl)
  private
    FShape: TBevelShape;
    FStyle: TBevelStyle;
    procedure SetShape(Value: TBevelShape);
    procedure SetStyle(Value: TBevelStyle);
  protected
    { UIF-748 }
    procedure Paint; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    property Shape: TBevelShape read FShape write SetShape default bsBox;
    property Style: TBevelStyle read FStyle write SetStyle default bsLowered;
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

{ ---------------------------------------------------------------- TImage }

constructor TImage.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FPicture := TPicture.Create;
  FPicture.OnChange := PictureChanged;
  SetBounds(Left, Top, 105, 105);
end;

destructor TImage.Destroy;
begin
  FPicture.OnChange := nil;
  inherited Destroy;
  FPicture.Free;
end;

{ Picture.OnChange: a new graphic takes the image's Transparent; then AdjustSize (if AutoSize) and Invalidate }
procedure TImage.PictureChanged(Sender: TObject);
begin
  if (FPicture.Graphic <> nil) and (FPicture.Graphic.Transparent <> FTransparent) then
    FPicture.Graphic.Transparent := FTransparent;              { fires Picture.OnChange again: same result }
  AdjustSize;
  Invalidate;
end;

procedure TImage.AdjustSize;
begin
  if not MeasuringAllowed then
    Exit;
  if (FPicture.Width > 0) and (FPicture.Height > 0) then
    SetBounds(Left, Top, FPicture.Width, FPicture.Height);
end;

function TImage.DestRect: TRect;
var
  PW, PH, W, H, DW, DH, X, Y: Integer;
begin
  PW := FPicture.Width;
  PH := FPicture.Height;
  W := Width;
  H := Height;
  if (PW = 0) or (PH = 0) then
  begin
    Result := Rect(0, 0, 0, 0);
    Exit;
  end;
  if FStretch and not FProportional then
  begin
    DW := W;
    DH := H;
  end
  else if FProportional and (FStretch or (PW > W) or (PH > H)) then
  begin
    if Int64(PW) * H > Int64(PH) * W then
    begin
      DW := W;
      DH := MulDiv(PH, W, PW);
    end
    else
    begin
      DH := H;
      DW := MulDiv(PW, H, PH);
    end;
  end
  else
  begin
    DW := PW;
    DH := PH;
  end;
  if FCenter then
  begin
    X := (W - DW) div 2;
    Y := (H - DH) div 2;
  end
  else
  begin
    X := 0;
    Y := 0;
  end;
  Result := Bounds(X, Y, DW, DH);
end;

procedure TImage.Paint;
var
  R: TRect;
begin
  R := DestRect;
  if (R.Right > R.Left) and (R.Bottom > R.Top) then
    Canvas.StretchDraw(R, FPicture.Graphic);
end;

procedure TImage.SetCenter(Value: Boolean);
begin
  if FCenter <> Value then
  begin
    FCenter := Value;
    Invalidate;
  end;
end;

procedure TImage.SetPicture(Value: TPicture);
begin
  FPicture.Assign(Value);
end;

procedure TImage.SetProportional(Value: Boolean);
begin
  if FProportional <> Value then
  begin
    FProportional := Value;
    Invalidate;
  end;
end;

procedure TImage.SetStretch(Value: Boolean);
begin
  if FStretch <> Value then
  begin
    FStretch := Value;
    Invalidate;
  end;
end;

procedure TImage.SetTransparent(Value: Boolean);
begin
  if FTransparent <> Value then
  begin
    FTransparent := Value;
    if FPicture.Graphic <> nil then
      FPicture.Graphic.Transparent := Value                    { Picture.OnChange invalidates }
    else
      Invalidate;
  end;
end;

{ ---------------------------------------------------------------- TShape }

constructor TShape.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 65, 65);
  FPen := TPen.Create;
  FPen.OnChange := StyleChanged;
  FBrush := TBrush.Create;
  FBrush.OnChange := StyleChanged;
end;

destructor TShape.Destroy;
begin
  inherited Destroy;
  FPen.Free;
  FBrush.Free;
end;

procedure TShape.StyleChanged(Sender: TObject);
begin
  Invalidate;
end;

procedure TShape.SetBrush(Value: TBrush);
begin
  FBrush.Assign(Value);
end;

procedure TShape.SetPen(Value: TPen);
begin
  FPen.Assign(Value);
end;

procedure TShape.SetShape(Value: TShapeType);
begin
  if FShape <> Value then
  begin
    FShape := Value;
    Invalidate;
  end;
end;

procedure TShape.Paint;
var
  X, Y, W, H, S: Integer;
begin
  X := FPen.Width div 2;
  Y := X;
  W := Width - FPen.Width + 1;
  H := Height - FPen.Width + 1;
  if FPen.Width = 0 then
  begin
    Dec(W);
    Dec(H);
  end;
  if W < H then
    S := W
  else
    S := H;
  if FShape in [stSquare, stRoundSquare, stCircle] then
  begin
    X := X + (W - S) div 2;
    Y := Y + (H - S) div 2;
    W := S;
    H := S;
  end;
  Canvas.Pen := FPen;
  Canvas.Brush := FBrush;
  case FShape of
    stRectangle, stSquare:
      Canvas.Rectangle(X, Y, X + W, Y + H);
    stRoundRect, stRoundSquare:
      Canvas.RoundRect(X, Y, X + W, Y + H, S div 4, S div 4);
    stEllipse, stCircle:
      Canvas.Ellipse(X, Y, X + W, Y + H);
  end;
end;

{ ---------------------------------------------------------------- TBevel }

constructor TBevel.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 50, 50);
end;

procedure TBevel.SetShape(Value: TBevelShape);
begin
  if FShape <> Value then
  begin
    FShape := Value;
    Invalidate;
  end;
end;

procedure TBevel.SetStyle(Value: TBevelStyle);
begin
  if FStyle <> Value then
  begin
    FStyle := Value;
    Invalidate;
  end;
end;

{ 1-px lines as filled rectangles; a frame is drawn top, left, bottom, right; the second colour is drawn last }
procedure TBevel.Paint;
var
  C1, C2: TColor;
  W, H: Integer;

  procedure Line(X1, Y1, X2, Y2: Integer; C: TColor);
  begin
    Canvas.Brush.Color := C;
    Canvas.FillRect(Rect(X1, Y1, X2, Y2));
  end;

  { the outline of R (right and bottom exclusive) }
  procedure Frame(const R: TRect; C: TColor);
  begin
    Line(R.Left, R.Top, R.Right, R.Top + 1, C);
    Line(R.Left, R.Top, R.Left + 1, R.Bottom, C);
    Line(R.Left, R.Bottom - 1, R.Right, R.Bottom, C);
    Line(R.Right - 1, R.Top, R.Right, R.Bottom, C);
  end;

begin
  if FStyle = bsLowered then
  begin
    C1 := clBtnShadow;
    C2 := clBtnHighlight;
  end
  else
  begin
    C1 := clBtnHighlight;
    C2 := clBtnShadow;
  end;
  W := Width;
  H := Height;
  Canvas.Brush.Style := bsSolid;
  case FShape of
    bsBox:
      begin
        Line(0, 0, W, 1, C1);
        Line(0, 0, 1, H, C1);
        Line(0, H - 1, W, H, C2);
        Line(W - 1, 0, W, H, C2);
      end;
    bsFrame:
      begin
        Frame(Rect(0, 0, W - 1, H - 1), C1);
        Frame(Rect(1, 1, W, H), C2);
      end;
    bsTopLine:
      begin
        Line(0, 0, W, 1, C1);
        Line(0, 1, W, 2, C2);
      end;
    bsBottomLine:
      begin
        Line(0, H - 2, W, H - 1, C1);
        Line(0, H - 1, W, H, C2);
      end;
    bsLeftLine:
      begin
        Line(0, 0, 1, H, C1);
        Line(1, 0, 2, H, C2);
      end;
    bsRightLine:
      begin
        Line(W - 2, 0, W - 1, H, C1);
        Line(W - 1, 0, W, H, C2);
      end;
  end;
end;

initialization
  RegisterClasses([TPanel, TImage, TShape, TBevel]);
end.
