{ Graphics - colours, graphics objects, canvases and images over GDI (graphics.md). }
unit Graphics;

interface

uses
  Windows, Types, SysUtils, Classes, UIConsts;

{ ---------------------------------------------------------------- section 1: TColor }

type
  TColor = -$7FFFFFFF - 1..$7FFFFFFF;
  PColor = ^TColor;

const
  clSystemColor = TColor(-16777216);                       { $FF000000 }

  clScrollBar = TColor(clSystemColor or COLOR_SCROLLBAR);
  clBackground = TColor(clSystemColor or COLOR_BACKGROUND);
  clActiveCaption = TColor(clSystemColor or COLOR_ACTIVECAPTION);
  clInactiveCaption = TColor(clSystemColor or COLOR_INACTIVECAPTION);
  clMenu = TColor(clSystemColor or COLOR_MENU);
  clWindow = TColor(clSystemColor or COLOR_WINDOW);
  clWindowFrame = TColor(clSystemColor or COLOR_WINDOWFRAME);
  clMenuText = TColor(clSystemColor or COLOR_MENUTEXT);
  clWindowText = TColor(clSystemColor or COLOR_WINDOWTEXT);
  clCaptionText = TColor(clSystemColor or COLOR_CAPTIONTEXT);
  clActiveBorder = TColor(clSystemColor or COLOR_ACTIVEBORDER);
  clInactiveBorder = TColor(clSystemColor or COLOR_INACTIVEBORDER);
  clAppWorkSpace = TColor(clSystemColor or COLOR_APPWORKSPACE);
  clHighlight = TColor(clSystemColor or COLOR_HIGHLIGHT);
  clHighlightText = TColor(clSystemColor or COLOR_HIGHLIGHTTEXT);
  clBtnFace = TColor(clSystemColor or COLOR_BTNFACE);
  clBtnShadow = TColor(clSystemColor or COLOR_BTNSHADOW);
  clGrayText = TColor(clSystemColor or COLOR_GRAYTEXT);
  clBtnText = TColor(clSystemColor or COLOR_BTNTEXT);
  clInactiveCaptionText = TColor(clSystemColor or COLOR_INACTIVECAPTIONTEXT);
  clBtnHighlight = TColor(clSystemColor or COLOR_BTNHIGHLIGHT);
  cl3DDkShadow = TColor(clSystemColor or COLOR_3DDKSHADOW);
  cl3DLight = TColor(clSystemColor or COLOR_3DLIGHT);
  clInfoText = TColor(clSystemColor or COLOR_INFOTEXT);
  clInfoBk = TColor(clSystemColor or COLOR_INFOBK);
  clHotLight = TColor(clSystemColor or COLOR_HOTLIGHT);
  clGradientActiveCaption = TColor(clSystemColor or COLOR_GRADIENTACTIVECAPTION);
  clGradientInactiveCaption = TColor(clSystemColor or COLOR_GRADIENTINACTIVECAPTION);
  clMenuHighlight = TColor(clSystemColor or COLOR_MENUHILIGHT);
  clMenuBar = TColor(clSystemColor or COLOR_MENUBAR);

  clBlack = TColor($000000);
  clMaroon = TColor($000080);
  clGreen = TColor($008000);
  clOlive = TColor($008080);
  clNavy = TColor($800000);
  clPurple = TColor($800080);
  clTeal = TColor($808000);
  clGray = TColor($808080);
  clSilver = TColor($C0C0C0);
  clRed = TColor($0000FF);
  clLime = TColor($00FF00);
  clYellow = TColor($00FFFF);
  clBlue = TColor($FF0000);
  clFuchsia = TColor($FF00FF);
  clAqua = TColor($FFFF00);
  clWhite = TColor($FFFFFF);
  clLtGray = TColor($C0C0C0);
  clDkGray = TColor($808080);
  clMoneyGreen = TColor($C0DCC0);
  clSkyBlue = TColor($F0CAA6);
  clCream = TColor($F0FBFF);
  clMedGray = TColor($A4A0A0);
  clNone = TColor($1FFFFFFF);
  clDefault = TColor($20000000);

{ ---------------------------------------------------------------- section 2: graphics objects }

type
  { UIF-505: attributes live in fields; the GDI handle is created lazily and replaced (detach, Changed, delete) on change }
  TGraphicsObject = class(TPersistent)
  private
    FOnChange: TNotifyEvent;
  protected
    FHandle: HGDIOBJ;
    FStockHandle: Boolean;                                  { a stock object is never deleted (UIF-574) }
    procedure Changed; virtual;
    { UIF-505 steps 1-3: detach the cached handle, fire Changed, delete the detached handle }
    procedure HandleChanged;
    procedure FreeHandle;
  public
    destructor Destroy; override;
    function HandleAllocated: Boolean;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  end;

  TFontCharset = 0..255;
  TFontName = type string;
  TFontPitch = (fpDefault, fpVariable, fpFixed);
  TFontQuality = (fqDefault, fqDraft, fqProof, fqNonAntialiased, fqAntialiased, fqClearType, fqClearTypeNatural);
  TFontStyle = (fsBold, fsItalic, fsUnderline, fsStrikeOut);
  TFontStyles = set of TFontStyle;

  { UIF-510: the framework's constant default font }
  TFontData = record
    Height: Integer;
    Pitch: TFontPitch;
    Style: TFontStyles;
    Charset: TFontCharset;
    Name: string;
  end;

  TFont = class(TGraphicsObject)
  private
    FCharset: TFontCharset;
    FColor: TColor;
    FHeight: Integer;
    FName: TFontName;
    FOrientation: Integer;
    FPitch: TFontPitch;
    FQuality: TFontQuality;
    FStyle: TFontStyles;
    FPixelsPerInch: Integer;
    function GetHandle: HFONT;
    procedure SetHandle(Value: HFONT);
    procedure SetCharset(Value: TFontCharset);
    procedure SetColor(Value: TColor);
    procedure SetHeight(Value: Integer);
    procedure SetName(const Value: TFontName);
    procedure SetOrientation(Value: Integer);
    procedure SetPitch(Value: TFontPitch);
    procedure SetQuality(Value: TFontQuality);
    procedure SetStyle(Value: TFontStyles);
    function GetSize: Integer;
    procedure SetSize(Value: Integer);
  public
    constructor Create;
    procedure Assign(Source: TPersistent); override;
    property Handle: HFONT read GetHandle write SetHandle;
    property PixelsPerInch: Integer read FPixelsPerInch write FPixelsPerInch;
  published
    property Charset: TFontCharset read FCharset write SetCharset default 1;
    property Color: TColor read FColor write SetColor default clWindowText;
    property Height: Integer read FHeight write SetHeight default -12;
    property Name: TFontName read FName write SetName;
    property Orientation: Integer read FOrientation write SetOrientation default 0;
    property Pitch: TFontPitch read FPitch write SetPitch default fpDefault;
    property Quality: TFontQuality read FQuality write SetQuality default fqDefault;
    property Style: TFontStyles read FStyle write SetStyle default [];
    property Size: Integer read GetSize write SetSize stored False;
  end;

  TPenMode = (pmBlack, pmWhite, pmNop, pmNot, pmCopy, pmNotCopy, pmMergePenNot, pmMaskPenNot, pmMergeNotPen,
    pmMaskNotPen, pmMerge, pmNotMerge, pmMask, pmNotMask, pmXor, pmNotXor);
  TPenStyle = (psSolid, psDash, psDot, psDashDot, psDashDotDot, psClear, psInsideFrame);

  { UIF-520: Mode is applied with SetROP2 by the canvas; it is not part of the GDI pen }
  TPen = class(TGraphicsObject)
  private
    FColor: TColor;
    FMode: TPenMode;
    FStyle: TPenStyle;
    FWidth: Integer;
    function GetHandle: HPEN;
    procedure SetHandle(Value: HPEN);
    procedure SetColor(Value: TColor);
    procedure SetMode(Value: TPenMode);
    procedure SetStyle(Value: TPenStyle);
    procedure SetWidth(Value: Integer);
  public
    constructor Create;
    procedure Assign(Source: TPersistent); override;
    property Handle: HPEN read GetHandle write SetHandle;
  published
    property Color: TColor read FColor write SetColor default clBlack;
    property Mode: TPenMode read FMode write SetMode default pmCopy;
    property Style: TPenStyle read FStyle write SetStyle default psSolid;
    property Width: Integer read FWidth write SetWidth default 1;
  end;

  TBrushStyle = (bsSolid, bsClear, bsHorizontal, bsVertical, bsFDiagonal, bsBDiagonal, bsCross, bsDiagCross);

  TBrush = class(TGraphicsObject)
  private
    FColor: TColor;
    FStyle: TBrushStyle;
    function GetHandle: HBRUSH;
    procedure SetHandle(Value: HBRUSH);
    procedure SetColor(Value: TColor);
    procedure SetStyle(Value: TBrushStyle);
  public
    constructor Create;
    procedure Assign(Source: TPersistent); override;
    property Handle: HBRUSH read GetHandle write SetHandle;
  published
    property Color: TColor read FColor write SetColor default clWhite;
    property Style: TBrushStyle read FStyle write SetStyle default bsSolid;
  end;

{ ---------------------------------------------------------------- section 3: TCanvas }

  TGraphic = class;

  TCanvasStates = (csHandleValid, csFontValid, csPenValid, csBrushValid);
  TCanvasState = set of TCanvasStates;

  TTextFormats = (tfLeft, tfCenter, tfRight, tfTop, tfVerticalCenter, tfBottom, tfSingleLine, tfWordBreak,
    tfEndEllipsis, tfPathEllipsis, tfNoPrefix, tfCalcRect, tfExpandTabs, tfNoClip);
  TTextFormat = set of TTextFormats;

  { UIF-530: wraps one HDC; selects its font, pen and brush on demand and tracks what is selected }
  TCanvas = class(TPersistent)
  private
    FHandle: HDC;
    FState: TCanvasState;
    FFont: TFont;
    FPen: TPen;
    FBrush: TBrush;
    FOrigFont, FOrigPen, FOrigBrush: HGDIOBJ;              { selected when the DC was attached (UIF-531) }
    FCopyMode: Longint;
    FOnChange: TNotifyEvent;
    FOnChanging: TNotifyEvent;
    procedure FontChanged(Sender: TObject);
    procedure PenChanged(Sender: TObject);
    procedure BrushChanged(Sender: TObject);
    procedure DeselectHandles;
    function GetHandle: HDC;
    procedure SetHandle(Value: HDC);
    function GetPenPos: TPoint;
    procedure SetPenPos(const Value: TPoint);
    function GetPixel(X, Y: Integer): TColor;
    procedure SetPixel(X, Y: Integer; Value: TColor);
    function GetClipRect: TRect;
    procedure SetFont(Value: TFont);
    procedure SetPen(Value: TPen);
    procedure SetBrush(Value: TBrush);
  protected
    procedure Changing; virtual;
    procedure Changed; virtual;
    procedure CreateHandle; virtual;
    procedure RequiredState(ReqState: TCanvasState);
  public
    constructor Create;
    destructor Destroy; override;
    function HandleAllocated: Boolean;
    procedure MoveTo(X, Y: Integer);
    procedure LineTo(X, Y: Integer);
    procedure Rectangle(X1, Y1, X2, Y2: Integer); overload;
    procedure Rectangle(const R: TRect); overload;
    procedure Ellipse(X1, Y1, X2, Y2: Integer); overload;
    procedure Ellipse(const R: TRect); overload;
    procedure RoundRect(X1, Y1, X2, Y2, X3, Y3: Integer);
    procedure Arc(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
    procedure Pie(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
    procedure Chord(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
    procedure Polygon(const Points: array of TPoint);
    procedure Polyline(const Points: array of TPoint);
    procedure FillRect(const R: TRect);
    procedure FrameRect(const R: TRect);
    procedure DrawFocusRect(const R: TRect);
    procedure TextOut(X, Y: Integer; const Text: string);
    procedure TextRect(const Rect: TRect; X, Y: Integer; const Text: string); overload;
    procedure TextRect(var Rect: TRect; var Text: string; TextFormat: TTextFormat); overload;
    function TextExtent(const Text: string): TSize;
    function TextWidth(const Text: string): Integer;
    function TextHeight(const Text: string): Integer;
    procedure Draw(X, Y: Integer; Graphic: TGraphic);
    procedure StretchDraw(const Rect: TRect; Graphic: TGraphic);
    procedure CopyRect(const Dest: TRect; Canvas: TCanvas; const Source: TRect);
    procedure Lock;
    procedure Unlock;
    function TryLock: Boolean;
    function LockCount: Integer;
    procedure Refresh;
    property Handle: HDC read GetHandle write SetHandle;
    property PenPos: TPoint read GetPenPos write SetPenPos;
    property Pixels[X, Y: Integer]: TColor read GetPixel write SetPixel;
    property ClipRect: TRect read GetClipRect;
    property CopyMode: Longint read FCopyMode write FCopyMode;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnChanging: TNotifyEvent read FOnChanging write FOnChanging;
  published
    property Brush: TBrush read FBrush write SetBrush;
    property Font: TFont read FFont write SetFont;
    property Pen: TPen read FPen write SetPen;
  end;

{ ---------------------------------------------------------------- section 4: TGraphic, TBitmap }

  TGraphic = class(TPersistent)
  private
    FModified: Boolean;
    FTransparent: Boolean;
    FOnChange: TNotifyEvent;
  protected
    procedure Changed(Sender: TObject); virtual;
    procedure Draw(ACanvas: TCanvas; const Rect: TRect); virtual; abstract;
    function GetEmpty: Boolean; virtual; abstract;
    function GetHeight: Integer; virtual; abstract;
    function GetWidth: Integer; virtual; abstract;
    procedure SetHeight(Value: Integer); virtual; abstract;
    procedure SetWidth(Value: Integer); virtual; abstract;
    procedure SetTransparent(Value: Boolean); virtual;
  public
    constructor Create; virtual;
    procedure LoadFromFile(const FileName: string); virtual;
    procedure SaveToFile(const FileName: string); virtual;
    procedure LoadFromStream(Stream: TStream); virtual; abstract;
    procedure SaveToStream(Stream: TStream); virtual; abstract;
    procedure SetSize(AWidth, AHeight: Integer); virtual;
    property Empty: Boolean read GetEmpty;
    property Width: Integer read GetWidth write SetWidth;
    property Height: Integer read GetHeight write SetHeight;
    property Transparent: Boolean read FTransparent write SetTransparent;
    property Modified: Boolean read FModified write FModified;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  end;
  TGraphicClass = class of TGraphic;

  EInvalidGraphic = class(Exception);
  EInvalidGraphicOperation = class(Exception);

  TPixelFormat = (pfDevice, pf1bit, pf4bit, pf8bit, pf15bit, pf16bit, pf24bit, pf32bit, pfCustom);
  TTransparentMode = (tmAuto, tmFixed);

  { UIF-540: a top-down DIB section of 24 or 32 bits per pixel }
  TBitmap = class(TGraphic)
  private
    FHandle: HBITMAP;
    FBits: Pointer;
    FWidth: Integer;                                         { requested size; the DIB exists only when both are > 0 }
    FHeight: Integer;
    FPixelFormat: TPixelFormat;
    FDC: HDC;                                                { memory DC of the canvas (UIF-542) }
    FOldBitmap: HGDIOBJ;
    FCanvas: TCanvas;
    FTransparentColor: TColor;
    FTransparentMode: TTransparentMode;
    procedure NewImage(AWidth, AHeight: Integer; AFormat: TPixelFormat; Source: TBitmap);
    procedure FreeContext;
    procedure CanvasChanged(Sender: TObject);
    function GetCanvas: TCanvas;
    function GetScanLine(Row: Integer): Pointer;
    procedure SetPixelFormat(Value: TPixelFormat);
    function GetTransparentColor: TColor;
    procedure SetTransparentColor(Value: TColor);
    procedure SetTransparentMode(Value: TTransparentMode);
  protected
    procedure Draw(ACanvas: TCanvas; const Rect: TRect); override;
    function GetEmpty: Boolean; override;
    function GetHeight: Integer; override;
    function GetWidth: Integer; override;
    procedure SetHeight(Value: Integer); override;
    procedure SetWidth(Value: Integer); override;
  public
    constructor Create; override;
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    procedure LoadFromStream(Stream: TStream); override;
    procedure SaveToStream(Stream: TStream); override;
    procedure SetSize(AWidth, AHeight: Integer); override;
    function ReleaseHandle: HBITMAP;
    property Canvas: TCanvas read GetCanvas;
    property Handle: HBITMAP read FHandle;
    property PixelFormat: TPixelFormat read FPixelFormat write SetPixelFormat;
    property ScanLine[Row: Integer]: Pointer read GetScanLine;
    property TransparentColor: TColor read GetTransparentColor write SetTransparentColor;
    property TransparentMode: TTransparentMode read FTransparentMode write SetTransparentMode;
  end;

  { UIF-550: one HICON, owned }
  TIcon = class(TGraphic)
  private
    FHandle: HICON;
    FWidth: Integer;
    FHeight: Integer;
    procedure SetHandle(Value: HICON);
  protected
    procedure Draw(ACanvas: TCanvas; const Rect: TRect); override;
    function GetEmpty: Boolean; override;
    function GetHeight: Integer; override;
    function GetWidth: Integer; override;
    procedure SetHeight(Value: Integer); override;
    procedure SetWidth(Value: Integer); override;
  public
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    procedure LoadFromFile(const FileName: string); override;
    procedure LoadFromStream(Stream: TStream); override;
    procedure SaveToStream(Stream: TStream); override;
    function ReleaseHandle: HICON;
    property Handle: HICON read FHandle write SetHandle;
  end;

  TPicture = class(TPersistent)
  private
    FGraphic: TGraphic;
    FOnChange: TNotifyEvent;
    FReader: TReader;                                        { U-118: the reader while Data is read }
    procedure GraphicChanged(Sender: TObject);
    procedure ReplaceGraphic(Value: TGraphic);
    procedure SetGraphic(Value: TGraphic);
    function GetBitmap: TBitmap;
    procedure SetBitmap(Value: TBitmap);
    function GetIcon: TIcon;
    procedure SetIcon(Value: TIcon);
    function GetWidth: Integer;
    function GetHeight: Integer;
    procedure ReadData(Stream: TStream);
    procedure WriteData(Stream: TStream);
  protected
    procedure Changed(Sender: TObject); virtual;
    procedure DefineProperties(Filer: TFiler); override;
  public
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    procedure LoadFromFile(const FileName: string);
    procedure SaveToFile(const FileName: string);
    class procedure RegisterFileFormat(const AExtension, ADescription: string; AGraphicClass: TGraphicClass);
    property Graphic: TGraphic read FGraphic write SetGraphic;
    property Bitmap: TBitmap read GetBitmap write SetBitmap;
    property Icon: TIcon read GetIcon write SetIcon;
    property Width: Integer read GetWidth;
    property Height: Integer read GetHeight;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  end;

const
  { CopyMode values: the Win32 ternary raster-operation codes }
  cmBlackness = Longint($00000042);
  cmDstInvert = Longint($00550009);
  cmMergeCopy = Longint($00C000CA);
  cmMergePaint = Longint($00BB0226);
  cmNotSrcCopy = Longint($00330008);
  cmNotSrcErase = Longint($001100A6);
  cmPatCopy = Longint($00F00021);
  cmPatInvert = Longint($005A0049);
  cmPatPaint = Longint($00FB0A09);
  cmSrcAnd = Longint($008800C6);
  cmSrcCopy = Longint($00CC0020);
  cmSrcErase = Longint($00440328);
  cmSrcInvert = Longint($00660046);
  cmSrcPaint = Longint($00EE0086);
  cmWhiteness = Longint($00FF0062);

  { UIF-520: SetROP2 value of each TPenMode }
  PenModes: array[TPenMode] of Integer = (R2_BLACK, R2_WHITE, R2_NOP, R2_NOT, R2_COPYPEN, R2_NOTCOPYPEN,
    R2_MERGEPENNOT, R2_MASKPENNOT, R2_MERGENOTPEN, R2_MASKNOTPEN, R2_MERGEPEN, R2_NOTMERGEPEN, R2_MASKPEN,
    R2_NOTMASKPEN, R2_XORPEN, R2_NOTXORPEN);

  DefFontData: TFontData =(Height: -12; Pitch: fpDefault; Style: []; Charset: DEFAULT_CHARSET; Name: 'Segoe UI');

function ColorToRGB(Color: TColor): Longint;
function ColorToString(Color: TColor): string;
function StringToColor(const S: string): TColor;
function ColorToIdent(Color: Longint; var Ident: string): Boolean;
function IdentToColor(const Ident: string; var Color: Longint): Boolean;
procedure GetColorValues(Proc: TGetStrProc);

implementation

uses
  RTLConsts;

{ ---------------------------------------------------------------- section 1: colour table }

const
  { the names ColorToString returns come first; the two aliases are last so that IntToIdent never yields them }
  ColorCount = 54;
  AliasCount = 2;
  Colors: array[0..ColorCount - 1] of TIdentMapEntry = (
    (Value: clBlack; Name: 'clBlack'),
    (Value: clMaroon; Name: 'clMaroon'),
    (Value: clGreen; Name: 'clGreen'),
    (Value: clOlive; Name: 'clOlive'),
    (Value: clNavy; Name: 'clNavy'),
    (Value: clPurple; Name: 'clPurple'),
    (Value: clTeal; Name: 'clTeal'),
    (Value: clWhite; Name: 'clWhite'),
    (Value: clGray; Name: 'clGray'),
    (Value: clSilver; Name: 'clSilver'),
    (Value: clRed; Name: 'clRed'),
    (Value: clLime; Name: 'clLime'),
    (Value: clYellow; Name: 'clYellow'),
    (Value: clBlue; Name: 'clBlue'),
    (Value: clFuchsia; Name: 'clFuchsia'),
    (Value: clAqua; Name: 'clAqua'),
    (Value: clMoneyGreen; Name: 'clMoneyGreen'),
    (Value: clSkyBlue; Name: 'clSkyBlue'),
    (Value: clCream; Name: 'clCream'),
    (Value: clMedGray; Name: 'clMedGray'),
    (Value: clNone; Name: 'clNone'),
    (Value: clDefault; Name: 'clDefault'),
    (Value: clScrollBar; Name: 'clScrollBar'),
    (Value: clBackground; Name: 'clBackground'),
    (Value: clActiveCaption; Name: 'clActiveCaption'),
    (Value: clInactiveCaption; Name: 'clInactiveCaption'),
    (Value: clMenu; Name: 'clMenu'),
    (Value: clWindow; Name: 'clWindow'),
    (Value: clWindowFrame; Name: 'clWindowFrame'),
    (Value: clMenuText; Name: 'clMenuText'),
    (Value: clWindowText; Name: 'clWindowText'),
    (Value: clCaptionText; Name: 'clCaptionText'),
    (Value: clActiveBorder; Name: 'clActiveBorder'),
    (Value: clInactiveBorder; Name: 'clInactiveBorder'),
    (Value: clAppWorkSpace; Name: 'clAppWorkSpace'),
    (Value: clHighlight; Name: 'clHighlight'),
    (Value: clHighlightText; Name: 'clHighlightText'),
    (Value: clBtnFace; Name: 'clBtnFace'),
    (Value: clBtnShadow; Name: 'clBtnShadow'),
    (Value: clGrayText; Name: 'clGrayText'),
    (Value: clBtnText; Name: 'clBtnText'),
    (Value: clInactiveCaptionText; Name: 'clInactiveCaptionText'),
    (Value: clBtnHighlight; Name: 'clBtnHighlight'),
    (Value: cl3DDkShadow; Name: 'cl3DDkShadow'),
    (Value: cl3DLight; Name: 'cl3DLight'),
    (Value: clInfoText; Name: 'clInfoText'),
    (Value: clInfoBk; Name: 'clInfoBk'),
    (Value: clHotLight; Name: 'clHotLight'),
    (Value: clGradientActiveCaption; Name: 'clGradientActiveCaption'),
    (Value: clGradientInactiveCaption; Name: 'clGradientInactiveCaption'),
    (Value: clMenuHighlight; Name: 'clMenuHighlight'),
    (Value: clMenuBar; Name: 'clMenuBar'),
    (Value: clLtGray; Name: 'clLtGray'),
    (Value: clDkGray; Name: 'clDkGray'));

function ColorToRGB(Color: TColor): Longint;
begin
  if Color < 0 then
    Result := Longint(GetSysColor(Color and $FF))
  else
    Result := Color and $00FFFFFF;
end;

function ColorToIdent(Color: Longint; var Ident: string): Boolean;
begin
  Result := IntToIdent(Color, Ident, Colors);
end;

function IdentToColor(const Ident: string; var Color: Longint): Boolean;
begin
  Result := IdentToInt(Ident, Color, Colors);
end;

function ColorToString(Color: TColor): string;
begin
  if not ColorToIdent(Color, Result) then
    Result := '$' + IntToHex(Color, 8);
end;

function StringToColor(const S: string): TColor;
var
  V: Longint;
begin
  if IdentToColor(S, V) then
    Result := V
  else
    Result := StrToInt(S);
end;

{ lists the names ColorToString can return (the aliases clLtGray, clDkGray are not listed) }
procedure GetColorValues(Proc: TGetStrProc);
var
  I: Integer;
begin
  for I := 0 to ColorCount - AliasCount - 1 do
    Proc(Colors[I].Name);
end;

procedure OutOfResources;
begin
  raise EOutOfResources.Create(SOutOfResources);
end;

{ ---------------------------------------------------------------- section 2: TGraphicsObject }

destructor TGraphicsObject.Destroy;
begin
  FreeHandle;
  inherited Destroy;
end;

procedure TGraphicsObject.Changed;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

function TGraphicsObject.HandleAllocated: Boolean;
begin
  Result := FHandle <> 0;
end;

procedure TGraphicsObject.FreeHandle;
begin
  if (FHandle <> 0) and not FStockHandle then
    DeleteObject(FHandle);
  FHandle := 0;
  FStockHandle := False;
end;

procedure TGraphicsObject.HandleChanged;
var
  Old: HGDIOBJ;
  OldStock: Boolean;
begin
  Old := FHandle;
  OldStock := FStockHandle;
  FHandle := 0;
  FStockHandle := False;
  Changed;
  if (Old <> 0) and not OldStock then
    DeleteObject(Old);
end;

{ ---------------------------------------------------------------- section 2.1: TFont }

constructor TFont.Create;
begin
  inherited Create;
  FCharset := DefFontData.Charset;
  FColor := clWindowText;
  FHeight := DefFontData.Height;
  FName := DefFontData.Name;
  FPitch := DefFontData.Pitch;
  FStyle := DefFontData.Style;
  FQuality := fqDefault;
  FPixelsPerInch := 96;
end;

function TFont.GetHandle: HFONT;
const
  Pitches: array[TFontPitch] of Byte = (DEFAULT_PITCH, VARIABLE_PITCH, FIXED_PITCH);
var
  LF: TLogFontW;
  I: Integer;
begin
  if FHandle = 0 then
  begin
    FillChar(LF, SizeOf(LF), 0);
    LF.lfHeight := FHeight;
    LF.lfEscapement := FOrientation;
    LF.lfOrientation := FOrientation;
    if fsBold in FStyle then
      LF.lfWeight := FW_BOLD
    else
      LF.lfWeight := FW_NORMAL;
    LF.lfItalic := Byte(fsItalic in FStyle);
    LF.lfUnderline := Byte(fsUnderline in FStyle);
    LF.lfStrikeOut := Byte(fsStrikeOut in FStyle);
    LF.lfCharSet := FCharset;
    LF.lfQuality := Ord(FQuality);                        { the enum order is DEFAULT_QUALITY .. CLEARTYPE_NATURAL_QUALITY }
    LF.lfPitchAndFamily := Pitches[FPitch];
    for I := 1 to Length(FName) do
      LF.lfFaceName[I - 1] := FName[I];
    FHandle := CreateFontIndirectW(LF);
    if FHandle = 0 then
      OutOfResources;
  end;
  Result := FHandle;
end;

{ UIF-507: ownership of an external font passes to this object; the attributes are read back }
procedure TFont.SetHandle(Value: HFONT);
var
  LF: TLogFontW;
  Old: HGDIOBJ;
  S: string;
  I: Integer;
begin
  if Value = FHandle then
    Exit;
  FillChar(LF, SizeOf(LF), 0);
  if (Value <> 0) and (GetObjectW(Value, SizeOf(LF), @LF) <> 0) then
  begin
    FHeight := LF.lfHeight;
    FOrientation := LF.lfEscapement;
    FStyle := [];
    if LF.lfWeight >= FW_BOLD then
      Include(FStyle, fsBold);
    if LF.lfItalic <> 0 then
      Include(FStyle, fsItalic);
    if LF.lfUnderline <> 0 then
      Include(FStyle, fsUnderline);
    if LF.lfStrikeOut <> 0 then
      Include(FStyle, fsStrikeOut);
    FCharset := LF.lfCharSet;
    if LF.lfQuality <= Ord(High(TFontQuality)) then
      FQuality := TFontQuality(LF.lfQuality)
    else
      FQuality := fqDefault;
    case LF.lfPitchAndFamily and 3 of
      VARIABLE_PITCH: FPitch := fpVariable;
      FIXED_PITCH: FPitch := fpFixed;
    else
      FPitch := fpDefault;
    end;
    S := '';
    I := 0;
    while (I < LF_FACESIZE) and (LF.lfFaceName[I] <> #0) do
    begin
      S := S + LF.lfFaceName[I];
      Inc(I);
    end;
    FName := S;
  end;
  Old := FHandle;
  FHandle := Value;
  FStockHandle := False;
  Changed;
  if Old <> 0 then
    DeleteObject(Old);
end;

procedure TFont.SetCharset(Value: TFontCharset);
begin
  if Value <> FCharset then
  begin
    FCharset := Value;
    HandleChanged;
  end;
end;

{ Color is not part of the GDI font: the handle is kept, holders are told through OnChange }
procedure TFont.SetColor(Value: TColor);
begin
  if Value <> FColor then
  begin
    FColor := Value;
    Changed;
  end;
end;

procedure TFont.SetHeight(Value: Integer);
begin
  if Value = 0 then
    Value := -MulDiv(-DefFontData.Height, FPixelsPerInch, 96);   { UIF-512 }
  if Value <> FHeight then
  begin
    FHeight := Value;
    HandleChanged;
  end;
end;

procedure TFont.SetName(const Value: TFontName);
var
  S: string;
begin
  S := Copy(Value, 1, LF_FACESIZE - 1);
  if S <> FName then
  begin
    FName := S;
    HandleChanged;
  end;
end;

procedure TFont.SetOrientation(Value: Integer);
begin
  if Value <> FOrientation then
  begin
    FOrientation := Value;
    HandleChanged;
  end;
end;

procedure TFont.SetPitch(Value: TFontPitch);
begin
  if Value <> FPitch then
  begin
    FPitch := Value;
    HandleChanged;
  end;
end;

procedure TFont.SetQuality(Value: TFontQuality);
begin
  if Value <> FQuality then
  begin
    FQuality := Value;
    HandleChanged;
  end;
end;

procedure TFont.SetStyle(Value: TFontStyles);
begin
  if Value <> FStyle then
  begin
    FStyle := Value;
    HandleChanged;
  end;
end;

function TFont.GetSize: Integer;
begin
  Result := -MulDiv(FHeight, 72, FPixelsPerInch);
end;

procedure TFont.SetSize(Value: Integer);
begin
  SetHeight(-MulDiv(Value, FPixelsPerInch, 72));
end;

{ UIF-506: copies every attribute and fires at most one OnChange }
procedure TFont.Assign(Source: TPersistent);
var
  F: TFont;
  HandleSame: Boolean;
begin
  if Source is TFont then
  begin
    F := TFont(Source);
    HandleSame := (FCharset = F.FCharset) and (FHeight = F.FHeight) and (FName = F.FName)
      and (FOrientation = F.FOrientation) and (FPitch = F.FPitch) and (FQuality = F.FQuality) and (FStyle = F.FStyle);
    if HandleSame and (FColor = F.FColor) then
      Exit;
    FCharset := F.FCharset;
    FColor := F.FColor;
    FHeight := F.FHeight;
    FName := F.FName;
    FOrientation := F.FOrientation;
    FPitch := F.FPitch;
    FQuality := F.FQuality;
    FStyle := F.FStyle;
    if HandleSame then
      Changed
    else
      HandleChanged;
  end
  else
    inherited Assign(Source);
end;

{ ---------------------------------------------------------------- section 2.2: TPen }

type
  TLogPenRec = record
    lopnStyle: UINT;
    lopnWidth: TPoint;
    lopnColor: COLORREF;
  end;

constructor TPen.Create;
begin
  inherited Create;
  FColor := clBlack;
  FMode := pmCopy;
  FStyle := psSolid;
  FWidth := 1;
end;

function TPen.GetHandle: HPEN;
begin
  if FHandle = 0 then
  begin
    FHandle := CreatePen(Ord(FStyle), FWidth, COLORREF(ColorToRGB(FColor)));   { psSolid..psInsideFrame = PS_SOLID..PS_INSIDEFRAME }
    if FHandle = 0 then
      OutOfResources;
  end;
  Result := FHandle;
end;

procedure TPen.SetHandle(Value: HPEN);
var
  L: TLogPenRec;
  Old: HGDIOBJ;
begin
  if Value = FHandle then
    Exit;
  FillChar(L, SizeOf(L), 0);
  if (Value <> 0) and (GetObjectW(Value, SizeOf(L), @L) <> 0) then
  begin
    if L.lopnStyle and $F <= Ord(High(TPenStyle)) then
      FStyle := TPenStyle(L.lopnStyle and $F)
    else
      FStyle := psSolid;
    FWidth := L.lopnWidth.X;
    FColor := TColor(L.lopnColor and $00FFFFFF);
  end;
  Old := FHandle;
  FHandle := Value;
  FStockHandle := False;
  Changed;
  if Old <> 0 then
    DeleteObject(Old);
end;

procedure TPen.SetColor(Value: TColor);
begin
  if Value <> FColor then
  begin
    FColor := Value;
    HandleChanged;
  end;
end;

procedure TPen.SetMode(Value: TPenMode);
begin
  if Value <> FMode then
  begin
    FMode := Value;
    Changed;
  end;
end;

procedure TPen.SetStyle(Value: TPenStyle);
begin
  if Value <> FStyle then
  begin
    FStyle := Value;
    HandleChanged;
  end;
end;

procedure TPen.SetWidth(Value: Integer);
begin
  if Value <> FWidth then
  begin
    FWidth := Value;
    HandleChanged;
  end;
end;

procedure TPen.Assign(Source: TPersistent);
var
  P: TPen;
  HandleSame: Boolean;
begin
  if Source is TPen then
  begin
    P := TPen(Source);
    HandleSame := (FColor = P.FColor) and (FStyle = P.FStyle) and (FWidth = P.FWidth);
    if HandleSame and (FMode = P.FMode) then
      Exit;
    FColor := P.FColor;
    FMode := P.FMode;
    FStyle := P.FStyle;
    FWidth := P.FWidth;
    if HandleSame then
      Changed
    else
      HandleChanged;
  end
  else
    inherited Assign(Source);
end;

{ ---------------------------------------------------------------- section 2.3: TBrush }

constructor TBrush.Create;
begin
  inherited Create;
  FColor := clWhite;
  FStyle := bsSolid;
end;

function TBrush.GetHandle: HBRUSH;
var
  L: TLogBrush;
begin
  if FHandle = 0 then
  begin
    case FStyle of
      bsSolid:
        FHandle := CreateSolidBrush(COLORREF(ColorToRGB(FColor)));
      bsClear:
        begin
          FHandle := GetStockObject(NULL_BRUSH);
          FStockHandle := True;
        end;
    else
      L.lbStyle := BS_HATCHED;
      L.lbColor := COLORREF(ColorToRGB(FColor));
      L.lbHatch := Ord(FStyle) - Ord(bsHorizontal);        { HS_HORIZONTAL .. HS_DIAGCROSS }
      FHandle := CreateBrushIndirect(L);
    end;
    if FHandle = 0 then
      OutOfResources;
  end;
  Result := FHandle;
end;

procedure TBrush.SetHandle(Value: HBRUSH);
var
  L: TLogBrush;
  Old: HGDIOBJ;
  OldStock: Boolean;
begin
  if Value = FHandle then
    Exit;
  FillChar(L, SizeOf(L), 0);
  if (Value <> 0) and (GetObjectW(Value, SizeOf(L), @L) <> 0) then
  begin
    FColor := TColor(L.lbColor and $00FFFFFF);
    case L.lbStyle of
      BS_NULL: FStyle := bsClear;
      BS_HATCHED:
        if L.lbHatch <= HS_DIAGCROSS then
          FStyle := TBrushStyle(Ord(bsHorizontal) + Integer(L.lbHatch))
        else
          FStyle := bsSolid;
    else
      FStyle := bsSolid;
    end;
  end;
  Old := FHandle;
  OldStock := FStockHandle;
  FHandle := Value;
  FStockHandle := Value = GetStockObject(NULL_BRUSH);
  Changed;
  if (Old <> 0) and not OldStock then
    DeleteObject(Old);
end;

procedure TBrush.SetColor(Value: TColor);
begin
  if (Value <> FColor) or (FStyle = bsClear) then
  begin
    FColor := Value;
    if FStyle = bsClear then
      FStyle := bsSolid;                                   { UIF-525 }
    HandleChanged;
  end;
end;

procedure TBrush.SetStyle(Value: TBrushStyle);
begin
  if Value <> FStyle then
  begin
    FStyle := Value;
    HandleChanged;
  end;
end;

procedure TBrush.Assign(Source: TPersistent);
var
  B: TBrush;
begin
  if Source is TBrush then
  begin
    B := TBrush(Source);
    if (FColor = B.FColor) and (FStyle = B.FStyle) then
      Exit;
    FColor := B.FColor;
    FStyle := B.FStyle;
    HandleChanged;
  end
  else
    inherited Assign(Source);
end;

{ ---------------------------------------------------------------- section 3: TCanvas }

constructor TCanvas.Create;
begin
  inherited Create;
  FFont := TFont.Create;
  FFont.OnChange := FontChanged;
  FPen := TPen.Create;
  FPen.OnChange := PenChanged;
  FBrush := TBrush.Create;
  FBrush.OnChange := BrushChanged;
  FCopyMode := cmSrcCopy;
end;

destructor TCanvas.Destroy;
begin
  SetHandle(0);
  FFont.Free;
  FPen.Free;
  FBrush.Free;
  inherited Destroy;
end;

procedure TCanvas.Changing;
begin
  if Assigned(FOnChanging) then
    FOnChanging(Self);
end;

procedure TCanvas.Changed;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

{ a plain canvas has no way to obtain a DC }
procedure TCanvas.CreateHandle;
begin
end;

function TCanvas.HandleAllocated: Boolean;
begin
  Result := FHandle <> 0;
end;

function TCanvas.GetHandle: HDC;
begin
  RequiredState([csHandleValid]);
  Result := FHandle;
end;

{ UIF-530: an object change deselects it first (its old handle is deleted right after the event, UIF-505) }
procedure TCanvas.FontChanged(Sender: TObject);
begin
  if csFontValid in FState then
  begin
    Exclude(FState, csFontValid);
    SelectObject(FHandle, FOrigFont);
  end;
end;

procedure TCanvas.PenChanged(Sender: TObject);
begin
  if csPenValid in FState then
  begin
    Exclude(FState, csPenValid);
    SelectObject(FHandle, FOrigPen);
  end;
end;

procedure TCanvas.BrushChanged(Sender: TObject);
begin
  if csBrushValid in FState then
  begin
    Exclude(FState, csBrushValid);
    SelectObject(FHandle, FOrigBrush);
  end;
end;

procedure TCanvas.DeselectHandles;
begin
  if (FHandle <> 0) and (FState - [csHandleValid] <> []) then
  begin
    SelectObject(FHandle, FOrigFont);
    SelectObject(FHandle, FOrigPen);
    SelectObject(FHandle, FOrigBrush);
  end;
  FState := FState - [csFontValid, csPenValid, csBrushValid];
end;

{ UIF-531: the objects selected at attach time are selected back before another DC is attached }
procedure TCanvas.SetHandle(Value: HDC);
begin
  if Value = FHandle then
    Exit;
  if FHandle <> 0 then
    DeselectHandles;
  FHandle := Value;
  FState := [];
  if Value <> 0 then
  begin
    FOrigFont := GetCurrentObject(Value, OBJ_FONT);
    FOrigPen := GetCurrentObject(Value, OBJ_PEN);
    FOrigBrush := GetCurrentObject(Value, OBJ_BRUSH);
    Include(FState, csHandleValid);
  end;
end;

procedure TCanvas.RequiredState(ReqState: TCanvasState);
var
  Need: TCanvasState;
begin
  Need := ReqState - FState;
  if Need = [] then
    Exit;
  if csHandleValid in Need then
  begin
    CreateHandle;
    if FHandle = 0 then
      raise EInvalidOperation.Create(SNoCanvasHandle);
  end;
  if csFontValid in Need then
  begin
    SelectObject(FHandle, FFont.Handle);
    SetTextColor(FHandle, COLORREF(ColorToRGB(FFont.Color)));
    Include(FState, csFontValid);
  end;
  if csPenValid in Need then
  begin
    SelectObject(FHandle, FPen.Handle);
    SetROP2(FHandle, PenModes[FPen.Mode]);
    Include(FState, csPenValid);
  end;
  if csBrushValid in Need then
  begin
    SelectObject(FHandle, FBrush.Handle);
    SetBkColor(FHandle, COLORREF(ColorToRGB(FBrush.Color)));
    if FBrush.Style = bsSolid then
      SetBkMode(FHandle, OPAQUE)
    else
      SetBkMode(FHandle, TRANSPARENT);
    Include(FState, csBrushValid);
  end;
end;

procedure TCanvas.SetFont(Value: TFont);
begin
  FFont.Assign(Value);
end;

procedure TCanvas.SetPen(Value: TPen);
begin
  FPen.Assign(Value);
end;

procedure TCanvas.SetBrush(Value: TBrush);
begin
  FBrush.Assign(Value);
end;

function TCanvas.GetPenPos: TPoint;
begin
  RequiredState([csHandleValid]);
  MoveToEx(FHandle, 0, 0, @Result);                      { reads the current position ... }
  MoveToEx(FHandle, Result.X, Result.Y, nil);            { ... and puts it back }
end;

procedure TCanvas.SetPenPos(const Value: TPoint);
begin
  MoveTo(Value.X, Value.Y);
end;

procedure TCanvas.MoveTo(X, Y: Integer);
begin
  RequiredState([csHandleValid]);
  MoveToEx(FHandle, X, Y, nil);
end;

procedure TCanvas.LineTo(X, Y: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.LineTo(FHandle, X, Y);
  Changed;
end;

procedure TCanvas.Rectangle(X1, Y1, X2, Y2: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.Rectangle(FHandle, X1, Y1, X2, Y2);
  Changed;
end;

procedure TCanvas.Rectangle(const R: TRect);
begin
  Rectangle(R.Left, R.Top, R.Right, R.Bottom);
end;

procedure TCanvas.Ellipse(X1, Y1, X2, Y2: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.Ellipse(FHandle, X1, Y1, X2, Y2);
  Changed;
end;

procedure TCanvas.Ellipse(const R: TRect);
begin
  Ellipse(R.Left, R.Top, R.Right, R.Bottom);
end;

procedure TCanvas.RoundRect(X1, Y1, X2, Y2, X3, Y3: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.RoundRect(FHandle, X1, Y1, X2, Y2, X3, Y3);
  Changed;
end;

procedure TCanvas.Arc(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.Arc(FHandle, X1, Y1, X2, Y2, X3, Y3, X4, Y4);
  Changed;
end;

procedure TCanvas.Pie(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.Pie(FHandle, X1, Y1, X2, Y2, X3, Y3, X4, Y4);
  Changed;
end;

procedure TCanvas.Chord(X1, Y1, X2, Y2, X3, Y3, X4, Y4: Integer);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  Windows.Chord(FHandle, X1, Y1, X2, Y2, X3, Y3, X4, Y4);
  Changed;
end;

procedure TCanvas.Polygon(const Points: array of TPoint);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  if Length(Points) > 0 then
    Windows.Polygon(FHandle, @Points[0], Length(Points));
  Changed;
end;

procedure TCanvas.Polyline(const Points: array of TPoint);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid, csBrushValid]);
  if Length(Points) > 0 then
    Windows.Polyline(FHandle, @Points[0], Length(Points));
  Changed;
end;

procedure TCanvas.FillRect(const R: TRect);
begin
  Changing;
  RequiredState([csHandleValid, csBrushValid]);
  Windows.FillRect(FHandle, R, FBrush.Handle);
  Changed;
end;

procedure TCanvas.FrameRect(const R: TRect);
begin
  Changing;
  RequiredState([csHandleValid, csBrushValid]);
  Windows.FrameRect(FHandle, R, FBrush.Handle);
  Changed;
end;

procedure TCanvas.DrawFocusRect(const R: TRect);
begin
  Changing;
  RequiredState([csHandleValid, csBrushValid]);
  Windows.DrawFocusRect(FHandle, R);
  Changed;
end;

{ background follows the brush rule of UIF-530 (no ETO_OPAQUE); the pen position moves to the end of the text }
procedure TCanvas.TextOut(X, Y: Integer; const Text: string);
begin
  Changing;
  RequiredState([csHandleValid, csFontValid, csBrushValid]);
  ExtTextOutW(FHandle, X, Y, 0, nil, PWideChar(Text), Length(Text), nil);
  MoveTo(X + TextWidth(Text), Y);
  Changed;
end;

procedure TCanvas.TextRect(const Rect: TRect; X, Y: Integer; const Text: string);
var
  Options: UINT;
  R: TRect;
begin
  Changing;
  RequiredState([csHandleValid, csFontValid, csBrushValid]);
  Options := ETO_CLIPPED;
  if FBrush.Style = bsSolid then
    Options := Options or ETO_OPAQUE;
  R := Rect;
  ExtTextOutW(FHandle, X, Y, Options, @R, PWideChar(Text), Length(Text), nil);
  Changed;
end;

procedure TCanvas.TextRect(var Rect: TRect; var Text: string; TextFormat: TTextFormat);
const
  Flags: array[TTextFormats] of UINT = (DT_LEFT, DT_CENTER, DT_RIGHT, DT_TOP, DT_VCENTER, DT_BOTTOM,
    DT_SINGLELINE, DT_WORDBREAK, DT_END_ELLIPSIS, DT_PATH_ELLIPSIS, DT_NOPREFIX, DT_CALCRECT, DT_EXPANDTABS,
    DT_NOCLIP);
var
  F: TTextFormats;
  Format: UINT;
begin
  Format := 0;
  for F := Low(TTextFormats) to High(TTextFormats) do
    if F in TextFormat then
      Format := Format or Flags[F];
  Changing;
  RequiredState([csHandleValid, csFontValid, csBrushValid]);
  DrawTextW(FHandle, PWideChar(Text), Length(Text), Rect, Format);
  Changed;
end;

{ GetTextExtentPoint32W; an empty text has width 0 and the font's tmHeight }
function TCanvas.TextExtent(const Text: string): TSize;
var
  TM: TTextMetricW;
begin
  RequiredState([csHandleValid, csFontValid]);
  Result.cx := 0;
  Result.cy := 0;
  if Text = '' then
  begin
    FillChar(TM, SizeOf(TM), 0);
    GetTextMetricsW(FHandle, TM);
    Result.cy := TM.tmHeight;
  end
  else
    GetTextExtentPoint32W(FHandle, PWideChar(Text), Length(Text), Result);
end;

function TCanvas.TextWidth(const Text: string): Integer;
begin
  Result := TextExtent(Text).cx;
end;

function TCanvas.TextHeight(const Text: string): Integer;
begin
  Result := TextExtent(Text).cy;
end;

procedure TCanvas.Draw(X, Y: Integer; Graphic: TGraphic);
begin
  if (Graphic <> nil) and not Graphic.Empty then
    Graphic.Draw(Self, Types.Rect(X, Y, X + Graphic.Width, Y + Graphic.Height));
end;

procedure TCanvas.StretchDraw(const Rect: TRect; Graphic: TGraphic);
begin
  if (Graphic <> nil) and not Graphic.Empty then
    Graphic.Draw(Self, Rect);
end;

procedure TCanvas.CopyRect(const Dest: TRect; Canvas: TCanvas; const Source: TRect);
var
  Src: HDC;
begin
  Changing;
  RequiredState([csHandleValid]);
  Src := Canvas.Handle;
  StretchBlt(FHandle, Dest.Left, Dest.Top, Dest.Right - Dest.Left, Dest.Bottom - Dest.Top, Src, Source.Left,
    Source.Top, Source.Right - Source.Left, Source.Bottom - Source.Top, DWORD(FCopyMode));
  Changed;
end;

function TCanvas.GetPixel(X, Y: Integer): TColor;
begin
  RequiredState([csHandleValid]);
  Result := TColor(Windows.GetPixel(FHandle, X, Y));      { CLR_INVALID outside the clip region = -1 }
end;

procedure TCanvas.SetPixel(X, Y: Integer; Value: TColor);
begin
  Changing;
  RequiredState([csHandleValid, csPenValid]);
  Windows.SetPixel(FHandle, X, Y, COLORREF(ColorToRGB(Value)));
  Changed;
end;

function TCanvas.GetClipRect: TRect;
begin
  RequiredState([csHandleValid]);
  GetClipBox(FHandle, Result);
end;

{ UIF-414: the framework is single-threaded; locking never blocks }
procedure TCanvas.Lock;
begin
end;

procedure TCanvas.Unlock;
begin
end;

function TCanvas.TryLock: Boolean;
begin
  Result := True;
end;

function TCanvas.LockCount: Integer;
begin
  Result := 0;
end;

procedure TCanvas.Refresh;
begin
  DeselectHandles;
end;

{ ---------------------------------------------------------------- section 4: TGraphic }

constructor TGraphic.Create;
begin
  inherited Create;
end;

procedure TGraphic.Changed(Sender: TObject);
begin
  FModified := True;
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TGraphic.SetTransparent(Value: Boolean);
begin
  if Value <> FTransparent then
  begin
    FTransparent := Value;
    Changed(Self);
  end;
end;

procedure TGraphic.SetSize(AWidth, AHeight: Integer);
begin
  Width := AWidth;
  Height := AHeight;
end;

procedure TGraphic.LoadFromFile(const FileName: string);
var
  Stream: TFileStream;
begin
  Stream := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    LoadFromStream(Stream);
  finally
    Stream.Free;
  end;
end;

procedure TGraphic.SaveToFile(const FileName: string);
var
  Stream: TFileStream;
begin
  Stream := TFileStream.Create(FileName, fmCreate);
  try
    SaveToStream(Stream);
  finally
    Stream.Free;
  end;
end;

{ ---------------------------------------------------------------- section 4.1: TBitmap }

type
  { UIF-542: the canvas of a bitmap draws into a memory DC holding the DIB }
  TBitmapCanvas = class(TCanvas)
  private
    FBitmap: TBitmap;
  protected
    procedure CreateHandle; override;
  end;

procedure TBitmapCanvas.CreateHandle;
var
  DC: HDC;
begin
  if FBitmap.FDC = 0 then
  begin
    DC := CreateCompatibleDC(0);
    if DC = 0 then
      OutOfResources;
    FBitmap.FOldBitmap := GetCurrentObject(DC, OBJ_BITMAP);
    if FBitmap.FHandle <> 0 then
      SelectObject(DC, FBitmap.FHandle);
    FBitmap.FDC := DC;
  end;
  Handle := FBitmap.FDC;
end;

function BitsOf(Format: TPixelFormat): Integer;
begin
  if Format = pf24bit then
    Result := 24
  else
    Result := 32;
end;

{ UIF-543: bytes per DIB row }
function RowStride(Width, Bits: Integer): Integer;
begin
  Result := ((Width * Bits + 31) div 32) * 4;
end;

function RowAddress(Bits: Pointer; Row, Stride: Integer): PByte;
begin
  Result := PByte(NativeUInt(Bits) + NativeUInt(Row) * NativeUInt(Stride));
end;

constructor TBitmap.Create;
begin
  inherited Create;
  FPixelFormat := pf32bit;
  FTransparentColor := clDefault;
  FTransparentMode := tmAuto;
end;

destructor TBitmap.Destroy;
begin
  FreeContext;
  FCanvas.Free;
  if FHandle <> 0 then
    DeleteObject(FHandle);
  inherited Destroy;
end;

{ UIF-573: the canvas lets go of the DC, the DIB is deselected, then the DC is deleted }
procedure TBitmap.FreeContext;
begin
  if FDC <> 0 then
  begin
    if FCanvas <> nil then
      FCanvas.Handle := 0;
    SelectObject(FDC, FOldBitmap);
    DeleteDC(FDC);
    FDC := 0;
  end;
end;

{ Replaces the DIB by one of AWidth x AHeight in AFormat. The new pixels are white (alpha 0); the overlapping
  area is copied from Source (the bitmap itself on a resize or format change, another bitmap for Assign). }
procedure TBitmap.NewImage(AWidth, AHeight: Integer; AFormat: TPixelFormat; Source: TBitmap);
var
  BI: TBitmapInfo;
  NewHandle: HBITMAP;
  NewBits: Pointer;
  NewBpp, OldBpp, NewStride, OldStride, X, Y, CopyW, CopyH: Integer;
  P, Q: PByte;
begin
  if (AWidth < 0) or (AHeight < 0) then
    raise EInvalidGraphicOperation.Create(SInvalidBitmapSize);
  NewHandle := 0;
  NewBits := nil;
  NewBpp := BitsOf(AFormat) div 8;
  if (AWidth > 0) and (AHeight > 0) then
  begin
    FillChar(BI, SizeOf(BI), 0);
    BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
    BI.bmiHeader.biWidth := AWidth;
    BI.bmiHeader.biHeight := -AHeight;                     { top-down }
    BI.bmiHeader.biPlanes := 1;
    BI.bmiHeader.biBitCount := BitsOf(AFormat);
    BI.bmiHeader.biCompression := BI_RGB;
    NewHandle := CreateDIBSection(0, @BI, DIB_RGB_COLORS, NewBits, 0, 0);
    if (NewHandle = 0) or (NewBits = nil) then
      OutOfResources;
    NewStride := RowStride(AWidth, BitsOf(AFormat));
    for Y := 0 to AHeight - 1 do
    begin
      P := RowAddress(NewBits, Y, NewStride);
      for X := 0 to AWidth - 1 do
      begin
        P^ := $FF;
        Inc(P);
        P^ := $FF;
        Inc(P);
        P^ := $FF;
        Inc(P);
        if NewBpp = 4 then
          Inc(P);                                          { alpha stays 0 }
      end;
    end;
    if (Source <> nil) and (Source.FHandle <> 0) then
    begin
      GdiFlush;
      OldBpp := BitsOf(Source.FPixelFormat) div 8;
      OldStride := RowStride(Source.FWidth, BitsOf(Source.FPixelFormat));
      CopyW := Source.FWidth;
      if AWidth < CopyW then
        CopyW := AWidth;
      CopyH := Source.FHeight;
      if AHeight < CopyH then
        CopyH := AHeight;
      for Y := 0 to CopyH - 1 do
      begin
        P := RowAddress(NewBits, Y, NewStride);
        Q := RowAddress(Source.FBits, Y, OldStride);
        if OldBpp = NewBpp then
          Move(Q^, P^, CopyW * NewBpp)
        else
          for X := 0 to CopyW - 1 do
          begin
            Move(Q^, P^, 3);                               { B G R; a new alpha byte stays 0 }
            Inc(P, NewBpp);
            Inc(Q, OldBpp);
          end;
      end;
    end;
  end;
  FreeContext;
  if FHandle <> 0 then
    DeleteObject(FHandle);
  FHandle := NewHandle;
  FBits := NewBits;
  FWidth := AWidth;
  FHeight := AHeight;
  FPixelFormat := AFormat;
end;

procedure TBitmap.SetSize(AWidth, AHeight: Integer);
begin
  if (AWidth < 0) or (AHeight < 0) then
    raise EInvalidGraphicOperation.Create(SInvalidBitmapSize);
  if (AWidth = FWidth) and (AHeight = FHeight) then
    Exit;
  NewImage(AWidth, AHeight, FPixelFormat, Self);
  Changed(Self);
end;

function TBitmap.GetEmpty: Boolean;
begin
  Result := FHandle = 0;
end;

function TBitmap.GetWidth: Integer;
begin
  Result := FWidth;
end;

function TBitmap.GetHeight: Integer;
begin
  Result := FHeight;
end;

procedure TBitmap.SetWidth(Value: Integer);
begin
  SetSize(Value, FHeight);
end;

procedure TBitmap.SetHeight(Value: Integer);
begin
  SetSize(FWidth, Value);
end;

procedure TBitmap.SetPixelFormat(Value: TPixelFormat);
begin
  if not (Value in [pf24bit, pf32bit]) then
    raise EInvalidGraphicOperation.Create(SInvalidPixelFormat);
  if Value = FPixelFormat then
    Exit;
  NewImage(FWidth, FHeight, Value, Self);
  Changed(Self);
end;

procedure TBitmap.CanvasChanged(Sender: TObject);
begin
  Changed(Self);
end;

function TBitmap.GetCanvas: TCanvas;
begin
  if FCanvas = nil then
  begin
    FCanvas := TBitmapCanvas.Create;
    TBitmapCanvas(FCanvas).FBitmap := Self;
    FCanvas.OnChange := CanvasChanged;
  end;
  Result := FCanvas;
end;

function TBitmap.GetScanLine(Row: Integer): Pointer;
begin
  if (FHandle = 0) or (Row < 0) or (Row >= FHeight) then
    raise EInvalidGraphicOperation.CreateFmt(SValueOutOfRange, [Row]);
  GdiFlush;
  Result := RowAddress(FBits, Row, RowStride(FWidth, BitsOf(FPixelFormat)));
end;

function TBitmap.ReleaseHandle: HBITMAP;
begin
  FreeContext;
  Result := FHandle;
  FHandle := 0;
  FBits := nil;
  FWidth := 0;
  FHeight := 0;
end;

{ UIF-546: tmAuto takes the bottom-left pixel; tmFixed the stored colour }
function TBitmap.GetTransparentColor: TColor;
begin
  if FTransparentMode = tmFixed then
    Result := FTransparentColor
  else if Empty then
    Result := clDefault
  else
    Result := Canvas.Pixels[0, FHeight - 1];
end;

{ assigning a colour selects tmFixed }
procedure TBitmap.SetTransparentColor(Value: TColor);
begin
  if (Value <> FTransparentColor) or (FTransparentMode <> tmFixed) then
  begin
    FTransparentColor := Value;
    FTransparentMode := tmFixed;
    Changed(Self);
  end;
end;

procedure TBitmap.SetTransparentMode(Value: TTransparentMode);
begin
  if Value <> FTransparentMode then
  begin
    FTransparentMode := Value;
    Changed(Self);
  end;
end;

{ UIF-547: a deep copy into a new DIB; nil clears }
procedure TBitmap.Assign(Source: TPersistent);
var
  B: TBitmap;
begin
  if Source = nil then
  begin
    NewImage(0, 0, FPixelFormat, nil);
    Changed(Self);
  end
  else if Source is TBitmap then
  begin
    B := TBitmap(Source);
    if B = Self then
      Exit;
    NewImage(B.FWidth, B.FHeight, B.FPixelFormat, B);
    FTransparentColor := B.FTransparentColor;
    FTransparentMode := B.FTransparentMode;
    FTransparent := B.FTransparent;
    Changed(Self);
  end
  else
    inherited Assign(Source);
end;

{ copies W x H of Src to the destination rectangle: BitBlt when the size is unchanged, else StretchBlt }
procedure Blit(Dest: HDC; const R: TRect; Src: HDC; W, H: Integer; Rop: DWORD);
begin
  if (R.Right - R.Left = W) and (R.Bottom - R.Top = H) then
    BitBlt(Dest, R.Left, R.Top, W, H, Src, 0, 0, Rop)
  else
  begin
    SetStretchBltMode(Dest, COLORONCOLOR);
    StretchBlt(Dest, R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top, Src, 0, 0, W, H, Rop);
  end;
end;

{ UIF-546: transparent drawing with a monochrome mask (pixels of the transparent colour = 1) and the AND/OR
  sequence: destination AND mask, then OR the image whose transparent pixels were made black }
procedure TBitmap.Draw(ACanvas: TCanvas; const Rect: TRect);
var
  Src, Dest, MaskDC, ImageDC: HDC;
  MaskBmp, ImageBmp: HBITMAP;
  OldMask, OldImage: HGDIOBJ;
  OldSrcBk, OldBk, OldText: COLORREF;
  Trans: COLORREF;
begin
  if Empty then
    Exit;
  Trans := COLORREF(ColorToRGB(TransparentColor));
  Src := Canvas.Handle;
  ACanvas.Changing;
  Dest := ACanvas.Handle;
  if not Transparent then
    Blit(Dest, Rect, Src, FWidth, FHeight, DWORD(ACanvas.CopyMode))
  else
  begin
    MaskDC := CreateCompatibleDC(0);
    MaskBmp := CreateBitmap(FWidth, FHeight, 1, 1, nil);
    ImageDC := CreateCompatibleDC(0);
    ImageBmp := CreateCompatibleBitmap(Src, FWidth, FHeight);
    if (MaskDC = 0) or (MaskBmp = 0) or (ImageDC = 0) or (ImageBmp = 0) then
      OutOfResources;
    OldMask := SelectObject(MaskDC, MaskBmp);
    OldImage := SelectObject(ImageDC, ImageBmp);
    try
      OldSrcBk := SetBkColor(Src, Trans);
      BitBlt(MaskDC, 0, 0, FWidth, FHeight, Src, 0, 0, SRCCOPY);       { colour -> mono: Trans = 1 }
      SetBkColor(Src, OldSrcBk);
      BitBlt(ImageDC, 0, 0, FWidth, FHeight, Src, 0, 0, SRCCOPY);
      SetBkColor(ImageDC, $000000);                                     { mono -> colour: 1 = bk, 0 = text }
      SetTextColor(ImageDC, $FFFFFF);
      BitBlt(ImageDC, 0, 0, FWidth, FHeight, MaskDC, 0, 0, SRCAND);    { transparent pixels black }
      OldBk := SetBkColor(Dest, $FFFFFF);
      OldText := SetTextColor(Dest, $000000);
      Blit(Dest, Rect, MaskDC, FWidth, FHeight, SRCAND);               { keep the destination where Trans }
      Blit(Dest, Rect, ImageDC, FWidth, FHeight, SRCPAINT);
      SetBkColor(Dest, OldBk);
      SetTextColor(Dest, OldText);
    finally
      SelectObject(MaskDC, OldMask);
      SelectObject(ImageDC, OldImage);
      DeleteObject(MaskBmp);
      DeleteObject(ImageBmp);
      DeleteDC(MaskDC);
      DeleteDC(ImageDC);
    end;
  end;
  ACanvas.Changed;
end;

procedure InvalidBitmap;
begin
  raise EInvalidGraphic.Create(SInvalidBitmap);
end;

procedure UnsupportedBitmap;
begin
  raise EInvalidGraphic.Create(SUnsupportedBitmap);
end;

procedure ReadExact(Stream: TStream; var Buffer; Count: Integer);
begin
  if (Count > 0) and (Stream.Read(Buffer, Count) <> Count) then
    InvalidBitmap;
end;

{ UIF-544: a BMP file image - file header, info header (40 bytes or a longer V4/V5 header), optional
  bit masks and palette, pixels. BI_RGB 1/4/8/24/32 and BI_BITFIELDS 32 with the standard masks. }
procedure TBitmap.LoadFromStream(Stream: TStream);
type
  TBytes = array of Byte;
var
  Start: Int64;
  FH: TBitmapFileHeader;
  IH: TBitmapInfoHeader;
  Extra: TBytes;
  Masks: array[0..2] of Cardinal;
  Palette: array of TRGBQuad;
  Pixels: TBytes;
  Width, Height, Bits, Colors, SrcStride, DstStride, Row, SrcRow, X, Index, Skip: Integer;
  TopDown: Boolean;
  Format: TPixelFormat;
  Src, Dst: PByte;
  Image: TBitmap;
begin
  Start := Stream.Position;
  FillChar(FH, SizeOf(FH), 0);
  ReadExact(Stream, FH, SizeOf(FH));
  if FH.bfType <> $4D42 then
    InvalidBitmap;
  FillChar(IH, SizeOf(IH), 0);
  ReadExact(Stream, IH.biSize, 4);
  if IH.biSize = 12 then
    UnsupportedBitmap;                                     { OS/2 core header }
  if IH.biSize < SizeOf(IH) then
    InvalidBitmap;
  ReadExact(Stream, PByte(NativeUInt(@IH) + 4)^, SizeOf(IH) - 4);
  Masks[0] := $00FF0000;
  Masks[1] := $0000FF00;
  Masks[2] := $000000FF;
  if IH.biSize > SizeOf(IH) then
  begin
    SetLength(Extra, IH.biSize - SizeOf(IH));
    ReadExact(Stream, Extra[0], Length(Extra));
    if (IH.biCompression = BI_BITFIELDS) and (Length(Extra) >= 12) then
      Move(Extra[0], Masks[0], 12);                        { V4/V5: the masks are part of the header }
  end
  else if IH.biCompression = BI_BITFIELDS then
    ReadExact(Stream, Masks[0], 12);
  Bits := IH.biBitCount;
  if IH.biCompression = BI_RGB then
  begin
    if not ((Bits = 1) or (Bits = 4) or (Bits = 8) or (Bits = 24) or (Bits = 32)) then
      UnsupportedBitmap;
  end
  else if IH.biCompression = BI_BITFIELDS then
  begin
    if (Bits <> 32) or (Masks[0] <> $00FF0000) or (Masks[1] <> $0000FF00) or (Masks[2] <> $000000FF) then
      UnsupportedBitmap;
  end
  else
    UnsupportedBitmap;
  Width := IH.biWidth;
  Height := IH.biHeight;
  TopDown := Height < 0;
  if TopDown then
    Height := -Height;
  if (Width <= 0) or (Height <= 0) then
    InvalidBitmap;
  Colors := 0;
  if Bits <= 8 then
  begin
    Colors := IH.biClrUsed;
    if (Colors <= 0) or (Colors > 1 shl Bits) then
      Colors := 1 shl Bits;
    SetLength(Palette, Colors);
    ReadExact(Stream, Palette[0], Colors * SizeOf(TRGBQuad));
  end;
  { the pixels start at bfOffBits from the start of the file image }
  Skip := Integer(Int64(FH.bfOffBits) - (Stream.Position - Start));
  if Skip < 0 then
    InvalidBitmap;
  if Skip > 0 then
  begin
    SetLength(Extra, Skip);
    ReadExact(Stream, Extra[0], Skip);
  end;
  SrcStride := RowStride(Width, Bits);
  SetLength(Pixels, SrcStride * Height);
  ReadExact(Stream, Pixels[0], Length(Pixels));

  if Bits = 32 then
    Format := pf32bit
  else
    Format := pf24bit;                                     { palettised input is expanded to 24 bits }
  Image := TBitmap.Create;
  try
    Image.NewImage(Width, Height, Format, nil);
    DstStride := RowStride(Width, BitsOf(Format));
    for Row := 0 to Height - 1 do
    begin
      if TopDown then
        SrcRow := Row
      else
        SrcRow := Height - 1 - Row;
      Src := PByte(NativeUInt(@Pixels[0]) + NativeUInt(SrcRow * SrcStride));
      Dst := RowAddress(Image.FBits, Row, DstStride);
      if Bits >= 24 then
        Move(Src^, Dst^, Width * (Bits div 8))
      else
        for X := 0 to Width - 1 do
        begin
          case Bits of
            1: Index := (PByte(NativeUInt(Src) + NativeUInt(X div 8))^ shr (7 - X mod 8)) and 1;
            4: Index := (PByte(NativeUInt(Src) + NativeUInt(X div 2))^ shr (4 * (1 - X mod 2))) and 15;
          else
            Index := PByte(NativeUInt(Src) + NativeUInt(X))^;
          end;
          if Index < Colors then
          begin
            Dst^ := Palette[Index].rgbBlue;
            PByte(NativeUInt(Dst) + 1)^ := Palette[Index].rgbGreen;
            PByte(NativeUInt(Dst) + 2)^ := Palette[Index].rgbRed;
          end
          else
          begin
            Dst^ := 0;
            PByte(NativeUInt(Dst) + 1)^ := 0;
            PByte(NativeUInt(Dst) + 2)^ := 0;
          end;
          Inc(Dst, 3);
        end;
    end;
    NewImage(Width, Height, Format, Image);
  finally
    Image.Free;
  end;
  Changed(Self);
end;

{ UIF-545: uncompressed bottom-up BMP in the bitmap's pixel format, 40-byte info header, zero padding.
  An empty bitmap writes nothing. }
procedure TBitmap.SaveToStream(Stream: TStream);
var
  FH: TBitmapFileHeader;
  IH: TBitmapInfoHeader;
  Stride, Row: Integer;
begin
  if Empty then
    Exit;
  GdiFlush;
  Stride := RowStride(FWidth, BitsOf(FPixelFormat));
  FillChar(FH, SizeOf(FH), 0);
  FH.bfType := $4D42;
  FH.bfOffBits := SizeOf(TBitmapFileHeader) + SizeOf(TBitmapInfoHeader);
  FH.bfSize := FH.bfOffBits + Cardinal(Stride * FHeight);
  FillChar(IH, SizeOf(IH), 0);
  IH.biSize := SizeOf(TBitmapInfoHeader);
  IH.biWidth := FWidth;
  IH.biHeight := FHeight;
  IH.biPlanes := 1;
  IH.biBitCount := BitsOf(FPixelFormat);
  IH.biCompression := BI_RGB;
  IH.biSizeImage := Stride * FHeight;
  Stream.WriteBuffer(FH, SizeOf(FH));
  Stream.WriteBuffer(IH, SizeOf(IH));
  for Row := FHeight - 1 downto 0 do
    Stream.WriteBuffer(RowAddress(FBits, Row, Stride)^, Stride);
end;

{ ---------------------------------------------------------------- section 4.2: TIcon }

{ a new icon from the bitmaps of H (handles are never shared, UIF-506) }
function DuplicateIconHandle(H: HICON): HICON;
var
  Info: TIconInfo;
begin
  Result := 0;
  FillChar(Info, SizeOf(Info), 0);
  if GetIconInfo(H, Info) then
  begin
    Result := CreateIconIndirect(Info);
    if Info.hbmMask <> 0 then
      DeleteObject(Info.hbmMask);
    if Info.hbmColor <> 0 then
      DeleteObject(Info.hbmColor);
  end;
  if Result = 0 then
    OutOfResources;
end;

destructor TIcon.Destroy;
begin
  if FHandle <> 0 then
    DestroyIcon(FHandle);
  inherited Destroy;
end;

{ takes ownership; the size comes from the icon's bitmaps }
procedure TIcon.SetHandle(Value: HICON);
var
  Info: TIconInfo;
  BM: TBitmapRec;
begin
  if Value = FHandle then
    Exit;
  if FHandle <> 0 then
    DestroyIcon(FHandle);
  FHandle := Value;
  FWidth := 0;
  FHeight := 0;
  FillChar(Info, SizeOf(Info), 0);
  if (Value <> 0) and GetIconInfo(Value, Info) then
  begin
    FillChar(BM, SizeOf(BM), 0);
    if Info.hbmColor <> 0 then
    begin
      GetObjectW(Info.hbmColor, SizeOf(BM), @BM);
      FWidth := BM.bmWidth;
      FHeight := BM.bmHeight;
    end
    else if Info.hbmMask <> 0 then
    begin
      GetObjectW(Info.hbmMask, SizeOf(BM), @BM);          { monochrome icon: AND and XOR masks stacked }
      FWidth := BM.bmWidth;
      FHeight := BM.bmHeight div 2;
    end;
    if Info.hbmMask <> 0 then
      DeleteObject(Info.hbmMask);
    if Info.hbmColor <> 0 then
      DeleteObject(Info.hbmColor);
  end;
  Changed(Self);
end;

function TIcon.ReleaseHandle: HICON;
begin
  Result := FHandle;
  FHandle := 0;
  FWidth := 0;
  FHeight := 0;
end;

function TIcon.GetEmpty: Boolean;
begin
  Result := FHandle = 0;
end;

function TIcon.GetWidth: Integer;
begin
  Result := FWidth;
end;

function TIcon.GetHeight: Integer;
begin
  Result := FHeight;
end;

{ the size of an icon is the size of its image }
procedure TIcon.SetWidth(Value: Integer);
begin
end;

procedure TIcon.SetHeight(Value: Integer);
begin
end;

procedure TIcon.Assign(Source: TPersistent);
begin
  if Source = nil then
    SetHandle(0)
  else if Source is TIcon then
  begin
    if TIcon(Source).FHandle = 0 then
      SetHandle(0)
    else
      SetHandle(DuplicateIconHandle(TIcon(Source).FHandle));
  end
  else
    inherited Assign(Source);
end;

procedure TIcon.Draw(ACanvas: TCanvas; const Rect: TRect);
begin
  if FHandle = 0 then
    Exit;
  ACanvas.Changing;
  DrawIconEx(ACanvas.Handle, Rect.Left, Rect.Top, FHandle, Rect.Right - Rect.Left, Rect.Bottom - Rect.Top, 0, 0,
    DI_NORMAL);
  ACanvas.Changed;
end;

procedure TIcon.LoadFromFile(const FileName: string);
var
  H: HICON;
begin
  H := HICON(LoadImageW(0, PWideChar(FileName), IMAGE_ICON, 0, 0, LR_LOADFROMFILE or LR_DEFAULTSIZE));
  if H = 0 then
    raise EInvalidGraphic.Create(SInvalidBitmap);
  SetHandle(H);
end;

procedure TIcon.LoadFromStream(Stream: TStream);
begin
  raise EInvalidGraphicOperation.Create(SIconStreaming);
end;

procedure TIcon.SaveToStream(Stream: TStream);
begin
  raise EInvalidGraphicOperation.Create(SIconStreaming);
end;

{ ---------------------------------------------------------------- section 4.3: TPicture }

type
  TFileFormat = record
    Extension: string;
    Description: string;
    GraphicClass: TGraphicClass;
  end;

var
  FileFormats: array of TFileFormat;

class procedure TPicture.RegisterFileFormat(const AExtension, ADescription: string; AGraphicClass: TGraphicClass);
var
  N: Integer;
begin
  N := Length(FileFormats);
  SetLength(FileFormats, N + 1);
  FileFormats[N].Extension := LowerCase(AExtension);
  if (FileFormats[N].Extension <> '') and (FileFormats[N].Extension[1] = '.') then
    Delete(FileFormats[N].Extension, 1, 1);
  FileFormats[N].Description := ADescription;
  FileFormats[N].GraphicClass := AGraphicClass;
end;

{ later registrations win }
function FindFileFormat(const Extension: string): TGraphicClass;
var
  I: Integer;
  Ext: string;
begin
  Ext := LowerCase(Extension);
  if (Ext <> '') and (Ext[1] = '.') then
    Delete(Ext, 1, 1);
  for I := High(FileFormats) downto 0 do
    if FileFormats[I].Extension = Ext then
    begin
      Result := FileFormats[I].GraphicClass;
      Exit;
    end;
  Result := nil;
end;

destructor TPicture.Destroy;
begin
  FGraphic.Free;
  inherited Destroy;
end;

procedure TPicture.Changed(Sender: TObject);
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TPicture.GraphicChanged(Sender: TObject);
begin
  Changed(Self);
end;

{ takes ownership of Value (no copy) }
procedure TPicture.ReplaceGraphic(Value: TGraphic);
begin
  FGraphic.Free;
  FGraphic := Value;
  if FGraphic <> nil then
    FGraphic.OnChange := GraphicChanged;
  Changed(Self);
end;

procedure TPicture.SetGraphic(Value: TGraphic);
var
  Copy: TGraphic;
begin
  if Value = FGraphic then
    Exit;
  Copy := nil;
  if Value <> nil then
  begin
    Copy := TGraphicClass(Value.ClassType).Create;
    try
      Copy.Assign(Value);
    except
      Copy.Free;
      raise;
    end;
  end;
  ReplaceGraphic(Copy);
end;

function TPicture.GetBitmap: TBitmap;
begin
  if not (FGraphic is TBitmap) then
    ReplaceGraphic(TBitmap.Create);
  Result := TBitmap(FGraphic);
end;

procedure TPicture.SetBitmap(Value: TBitmap);
begin
  SetGraphic(Value);
end;

function TPicture.GetIcon: TIcon;
begin
  if not (FGraphic is TIcon) then
    ReplaceGraphic(TIcon.Create);
  Result := TIcon(FGraphic);
end;

procedure TPicture.SetIcon(Value: TIcon);
begin
  SetGraphic(Value);
end;

function TPicture.GetWidth: Integer;
begin
  if FGraphic = nil then
    Result := 0
  else
    Result := FGraphic.Width;
end;

function TPicture.GetHeight: Integer;
begin
  if FGraphic = nil then
    Result := 0
  else
    Result := FGraphic.Height;
end;

procedure TPicture.Assign(Source: TPersistent);
begin
  if Source = nil then
    SetGraphic(nil)
  else if Source is TPicture then
    SetGraphic(TPicture(Source).FGraphic)
  else if Source is TGraphic then
    SetGraphic(TGraphic(Source))
  else
    inherited Assign(Source);
end;

procedure TPicture.LoadFromFile(const FileName: string);
var
  Ext: string;
  GraphicClass: TGraphicClass;
  G: TGraphic;
begin
  Ext := ExtractFileExt(FileName);
  if (Ext <> '') and (Ext[1] = '.') then
    Delete(Ext, 1, 1);
  GraphicClass := FindFileFormat(Ext);
  if GraphicClass = nil then
    raise EInvalidGraphic.CreateFmt(SUnknownExtension, [Ext]);
  G := GraphicClass.Create;
  try
    G.LoadFromFile(FileName);
  except
    G.Free;
    raise;
  end;
  ReplaceGraphic(G);
end;

procedure TPicture.SaveToFile(const FileName: string);
begin
  if FGraphic <> nil then
    FGraphic.SaveToFile(FileName);
end;

{ UIF-555: Data = byte N, N bytes ASCII class name, then the graphic's SaveToStream bytes }
procedure TPicture.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  FReader := nil;
  if Filer is TReader then
    FReader := TReader(Filer);
  try
    Filer.DefineBinaryProperty('Data', ReadData, WriteData, (FGraphic <> nil) and not FGraphic.Empty);
  finally
    FReader := nil;
  end;
end;

procedure TPicture.ReadData(Stream: TStream);
var
  N: Byte;
  Name: AnsiString;
  ClassRef: TPersistentClass;
  G: TGraphic;
begin
  N := 0;
  Name := '';
  if Stream.Read(N, 1) = 1 then
  begin
    SetLength(Name, N);
    if (N > 0) and (Stream.Read(Name[1], N) <> N) then
      Name := '';
  end;
  ClassRef := nil;
  if Name <> '' then
    ClassRef := GetClass(string(Name));
  if (ClassRef = nil) or not ClassRef.InheritsFrom(TGraphic) then
  begin
    if FReader <> nil then
      FReader.PropValueError;                                { U-118: F0103 at the value, through OnError }
    raise EReadError.CreateFmt(SFormInvalidPropertyValue, ['Data']);
  end;
  G := TGraphicClass(ClassRef).Create;
  try
    G.LoadFromStream(Stream);
  except
    G.Free;
    raise;
  end;
  ReplaceGraphic(G);
end;

procedure TPicture.WriteData(Stream: TStream);
var
  Name: AnsiString;
  N: Byte;
begin
  Name := AnsiString(FGraphic.ClassName);
  N := Length(Name);
  Stream.WriteBuffer(N, 1);
  Stream.WriteBuffer(Name[1], N);
  FGraphic.SaveToStream(Stream);
end;

initialization
  RegisterIntegerConsts(TypeInfo(TColor), IdentToColor, ColorToIdent);
  RegisterClasses([TBitmap, TIcon]);                       { UIF-002 }
  TPicture.RegisterFileFormat('bmp', 'Bitmaps', TBitmap);
  TPicture.RegisterFileFormat('ico', 'Icons', TIcon);
end.
