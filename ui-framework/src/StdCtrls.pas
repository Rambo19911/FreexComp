{ StdCtrls - the standard controls (controls-standard.md sections 1 ... 11, 18). }
unit StdCtrls;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls;

type
  TTextLayout = (tlTop, tlCenter, tlBottom);

  { controls-standard.md section 1 }
  TCustomLabel = class(TGraphicControl)
  private
    FAlignment: TAlignment;
    FFocusControl: TWinControl;
    FLayout: TTextLayout;
    FShowAccelChar: Boolean;
    FTransparent: Boolean;
    FWordWrap: Boolean;
    procedure SetAlignment(Value: TAlignment);
    procedure SetFocusControl(Value: TWinControl);
    procedure SetLayout(Value: TTextLayout);
    procedure SetShowAccelChar(Value: Boolean);
    procedure SetTransparent(Value: Boolean);
    procedure SetWordWrap(Value: Boolean);
    function TextFormat: TTextFormat;
  protected
    { UIF-610 }
    procedure Paint; override;
    { UIF-611: CM_DIALOGCHAR; text, font and enabled changes repaint }
    procedure WndProc(var Message: TMessage); override;
    { UIF-612 }
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    property Alignment: TAlignment read FAlignment write SetAlignment default taLeftJustify;
    property AutoSize default True;
    property FocusControl: TWinControl read FFocusControl write SetFocusControl;
    property Layout: TTextLayout read FLayout write SetLayout default tlTop;
    property ShowAccelChar: Boolean read FShowAccelChar write SetShowAccelChar default True;
    property Transparent: Boolean read FTransparent write SetTransparent default True;
    property WordWrap: Boolean read FWordWrap write SetWordWrap default False;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLabel = class(TCustomLabel)
  published
    property Alignment;
    property AutoSize;
    property Caption;
    property Color default clBtnFace;
    property FocusControl;
    property Font;
    property Layout;
    property ParentColor;
    property ParentFont;
    property ShowAccelChar;
    property Transparent;
    property WordWrap;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
  end;

implementation

uses
  Forms;

{ ---------------------------------------------------------------- TCustomLabel }

constructor TCustomLabel.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(Left, Top, 34, 15);
  Color := clBtnFace;
  ParentColor := True;
  AutoSize := True;
  FShowAccelChar := True;
  FTransparent := True;
  ControlStyle := ControlStyle - [csOpaque];
end;

function TCustomLabel.TextFormat: TTextFormat;
begin
  Result := [tfExpandTabs];
  case FAlignment of
    taRightJustify: Include(Result, tfRight);
    taCenter: Include(Result, tfCenter);
  else
    Include(Result, tfLeft);
  end;
  if FWordWrap then
    Include(Result, tfWordBreak);
  if not FShowAccelChar then
    Include(Result, tfNoPrefix);
end;

procedure TCustomLabel.Paint;
var
  R, Calc: TRect;
  Text: string;
  Offset: Integer;
begin
  R := ClientRect;
  if not FTransparent then
  begin
    Canvas.Brush.Style := bsSolid;
    Canvas.Brush.Color := Color;
    Canvas.FillRect(R);
  end;
  Canvas.Font := Font;
  if not Enabled then
    Canvas.Font.Color := clGrayText;
  Text := Caption;
  if FLayout <> tlTop then
  begin
    Calc := R;
    Canvas.TextRect(Calc, Text, TextFormat + [tfCalcRect]);
    Offset := R.Bottom - R.Top - (Calc.Bottom - Calc.Top);
    if FLayout = tlCenter then
      Offset := Offset div 2;
    OffsetRect(R, 0, Offset);
  end;
  Canvas.Brush.Style := bsClear;
  SetBkMode(Canvas.Handle, Windows.TRANSPARENT);             { the Transparent property hides the constant }
  Canvas.TextRect(R, Text, TextFormat);
end;

procedure TCustomLabel.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_DIALOGCHAR:
      if Enabled and FShowAccelChar and (FFocusControl <> nil) and IsAccel(Word(Message.WParam), Caption)
        and FFocusControl.CanFocus then
      begin
        FFocusControl.SetFocus;
        Message.Result := 1;
      end
      else
        inherited WndProc(Message);
    CM_TEXTCHANGED, CM_FONTCHANGED, CM_ENABLEDCHANGED:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

procedure TCustomLabel.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FFocusControl) then
    FFocusControl := nil;
end;

procedure TCustomLabel.SetAlignment(Value: TAlignment);
begin
  if FAlignment <> Value then
  begin
    FAlignment := Value;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetFocusControl(Value: TWinControl);
begin
  if FFocusControl <> Value then
  begin
    FFocusControl := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

procedure TCustomLabel.SetLayout(Value: TTextLayout);
begin
  if FLayout <> Value then
  begin
    FLayout := Value;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetShowAccelChar(Value: Boolean);
begin
  if FShowAccelChar <> Value then
  begin
    FShowAccelChar := Value;
    Invalidate;
  end;
end;

procedure TCustomLabel.SetTransparent(Value: Boolean);
begin
  if FTransparent <> Value then
  begin
    FTransparent := Value;
    if Value then
      ControlStyle := ControlStyle - [csOpaque]
    else
      ControlStyle := ControlStyle + [csOpaque];
    Invalidate;
  end;
end;

procedure TCustomLabel.SetWordWrap(Value: Boolean);
begin
  if FWordWrap <> Value then
  begin
    FWordWrap := Value;
    Invalidate;
  end;
end;

initialization
  RegisterClasses([TLabel]);
end.
