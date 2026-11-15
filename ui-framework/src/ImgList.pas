{ ImgList - image lists over the native image list (graphics.md section 5). }
unit ImgList;

interface

uses
  Windows, Types, SysUtils, Classes, CommCtrl, Graphics, UIConsts;

type
  TColorDepth = (cdDeviceDependent, cd4Bit, cd8Bit, cd16Bit, cd24Bit, cd32Bit);
  TDrawingStyle = (dsFocus, dsSelected, dsNormal, dsTransparent);

  TCustomImageList = class;

  { UIF-567: a client of an image list holds a link and refreshes on OnChange }
  TChangeLink = class(TObject)
  private
    FSender: TCustomImageList;
    FOnChange: TNotifyEvent;
  public
    destructor Destroy; override;
    procedure Change; virtual;
    property Sender: TCustomImageList read FSender write FSender;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  end;

  TCustomImageList = class(TComponent)
  private
    FHandle: HIMAGELIST;
    FWidth: Integer;
    FHeight: Integer;
    FMasked: Boolean;
    FBkColor: TColor;
    FDrawingStyle: TDrawingStyle;
    FColorDepth: TColorDepth;
    FClients: TList;
    FOnChange: TNotifyEvent;
    function GetHandle: HIMAGELIST;
    function GetCount: Integer;
    procedure FreeHandle;
    procedure SetWidth(Value: Integer);
    procedure SetHeight(Value: Integer);
    procedure SetMasked(Value: Boolean);
    procedure SetBkColor(Value: TColor);
    procedure SetDrawingStyle(Value: TDrawingStyle);
    procedure SetColorDepth(Value: TColorDepth);
    procedure ReadData(Stream: TStream);
    procedure WriteData(Stream: TStream);
  protected
    procedure Change; virtual;
    procedure DefineProperties(Filer: TFiler); override;
    property ColorDepth: TColorDepth read FColorDepth write SetColorDepth default cd32Bit;
    property Height: Integer read FHeight write SetHeight default 16;
    property Width: Integer read FWidth write SetWidth default 16;
    property Masked: Boolean read FMasked write SetMasked default True;
    property BkColor: TColor read FBkColor write SetBkColor default clNone;
    property DrawingStyle: TDrawingStyle read FDrawingStyle write SetDrawingStyle default dsNormal;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function HandleAllocated: Boolean;
    function Add(Image, Mask: TBitmap): Integer;
    function AddMasked(Image: TBitmap; MaskColor: TColor): Integer;
    function AddIcon(Image: TIcon): Integer;
    procedure Delete(Index: Integer);
    procedure Clear;
    procedure Draw(Canvas: TCanvas; X, Y, Index: Integer; Enabled: Boolean = True);
    function GetBitmap(Index: Integer; Image: TBitmap): Boolean;
    procedure RegisterChanges(Value: TChangeLink);
    procedure UnRegisterChanges(Value: TChangeLink);
    property Count: Integer read GetCount;
    property Handle: HIMAGELIST read GetHandle;
  end;

  TImageList = class(TCustomImageList)
  published
    property ColorDepth;
    property Height;
    property Width;
    property Masked;
    property BkColor;
    property DrawingStyle;
    property OnChange;
  end;

implementation

{ ---------------------------------------------------------------- TChangeLink }

destructor TChangeLink.Destroy;
begin
  if FSender <> nil then
    FSender.UnRegisterChanges(Self);
  inherited Destroy;
end;

procedure TChangeLink.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(FSender);
end;

{ ---------------------------------------------------------------- TCustomImageList }

constructor TCustomImageList.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FWidth := 16;
  FHeight := 16;
  FMasked := True;
  FBkColor := clNone;
  FDrawingStyle := dsNormal;
  FColorDepth := cd32Bit;
  FClients := TList.Create;
end;

destructor TCustomImageList.Destroy;
var
  I: Integer;
begin
  for I := 0 to FClients.Count - 1 do
    TChangeLink(FClients[I]).FSender := nil;
  FClients.Free;
  FreeHandle;
  inherited Destroy;
end;

function TCustomImageList.HandleAllocated: Boolean;
begin
  Result := FHandle <> 0;
end;

{ ImageList_Create(Width, Height, ILC_COLOR32 or ILC_MASK, 4, 4); only cd24Bit and cd32Bit are honoured }
function TCustomImageList.GetHandle: HIMAGELIST;
var
  Flags: UINT;
begin
  if FHandle = 0 then
  begin
    if FColorDepth = cd24Bit then
      Flags := ILC_COLOR24
    else
      Flags := ILC_COLOR32;
    if FMasked then
      Flags := Flags or ILC_MASK;
    FHandle := ImageList_Create(FWidth, FHeight, Flags, 4, 4);
    if FHandle = 0 then
      raise EOutOfResources.Create(SOutOfResources);
    if FBkColor = clNone then
      ImageList_SetBkColor(FHandle, CLR_NONE)
    else
      ImageList_SetBkColor(FHandle, COLORREF(ColorToRGB(FBkColor)));
  end;
  Result := FHandle;
end;

procedure TCustomImageList.FreeHandle;
begin
  if FHandle <> 0 then
    ImageList_Destroy(FHandle);
  FHandle := 0;
end;

function TCustomImageList.GetCount: Integer;
begin
  if FHandle = 0 then
    Result := 0
  else
    Result := ImageList_GetImageCount(FHandle);
end;

procedure TCustomImageList.Change;
var
  I: Integer;
begin
  for I := 0 to FClients.Count - 1 do
    TChangeLink(FClients[I]).Change;
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TCustomImageList.RegisterChanges(Value: TChangeLink);
begin
  Value.FSender := Self;
  if FClients.IndexOf(Pointer(Value)) < 0 then
    FClients.Add(Pointer(Value));
end;

procedure TCustomImageList.UnRegisterChanges(Value: TChangeLink);
begin
  FClients.Remove(Pointer(Value));
  if Value.FSender = Self then
    Value.FSender := nil;
end;

{ a new size or format starts an empty list }
procedure TCustomImageList.SetWidth(Value: Integer);
begin
  if Value <> FWidth then
  begin
    FWidth := Value;
    FreeHandle;
    Change;
  end;
end;

procedure TCustomImageList.SetHeight(Value: Integer);
begin
  if Value <> FHeight then
  begin
    FHeight := Value;
    FreeHandle;
    Change;
  end;
end;

procedure TCustomImageList.SetMasked(Value: Boolean);
begin
  if Value <> FMasked then
  begin
    FMasked := Value;
    FreeHandle;
    Change;
  end;
end;

procedure TCustomImageList.SetColorDepth(Value: TColorDepth);
begin
  if Value <> FColorDepth then
  begin
    FColorDepth := Value;
    FreeHandle;
    Change;
  end;
end;

procedure TCustomImageList.SetBkColor(Value: TColor);
begin
  if Value <> FBkColor then
  begin
    FBkColor := Value;
    if FHandle <> 0 then
      if Value = clNone then
        ImageList_SetBkColor(FHandle, CLR_NONE)
      else
        ImageList_SetBkColor(FHandle, COLORREF(ColorToRGB(Value)));
    Change;
  end;
end;

procedure TCustomImageList.SetDrawingStyle(Value: TDrawingStyle);
begin
  if Value <> FDrawingStyle then
  begin
    FDrawingStyle := Value;
    Change;
  end;
end;

{ UIF-565: the native list splits an image that is a multiple of Width wide; the first index is returned }
function TCustomImageList.Add(Image, Mask: TBitmap): Integer;
var
  MaskHandle: HBITMAP;
begin
  MaskHandle := 0;
  if Mask <> nil then
    MaskHandle := Mask.Handle;
  Result := ImageList_Add(Handle, Image.Handle, MaskHandle);
  Change;
end;

{ the native call blackens the masked pixels of the bitmap it gets, so it gets a copy }
function TCustomImageList.AddMasked(Image: TBitmap; MaskColor: TColor): Integer;
var
  Copy: TBitmap;
begin
  Copy := TBitmap.Create;
  try
    Copy.Assign(Image);
    Result := ImageList_AddMasked(Handle, Copy.Handle, COLORREF(ColorToRGB(MaskColor)));
  finally
    Copy.Free;
  end;
  Change;
end;

function TCustomImageList.AddIcon(Image: TIcon): Integer;
begin
  Result := ImageList_ReplaceIcon(Handle, -1, Image.Handle);
  Change;
end;

procedure TCustomImageList.Delete(Index: Integer);
begin
  if (Index < 0) or (Index >= Count) then
    raise EInvalidOperation.Create(SInvalidImageListIndex);
  ImageList_Remove(FHandle, Index);
  Change;
end;

procedure TCustomImageList.Clear;
begin
  if FHandle <> 0 then
    ImageList_Remove(FHandle, -1);
  Change;
end;

{ UIF-566: disabled images are blended 50 % with clBtnFace }
procedure TCustomImageList.Draw(Canvas: TCanvas; X, Y, Index: Integer; Enabled: Boolean = True);
const
  Styles: array[TDrawingStyle] of UINT = (ILD_FOCUS, ILD_SELECTED, ILD_NORMAL, ILD_TRANSPARENT);
var
  Bk: COLORREF;
begin
  if (Index < 0) or (Index >= Count) then
    Exit;
  if FBkColor = clNone then
    Bk := CLR_NONE
  else
    Bk := COLORREF(ColorToRGB(FBkColor));
  if Enabled then
    ImageList_DrawEx(FHandle, Index, Canvas.Handle, X, Y, 0, 0, Bk, CLR_DEFAULT, Styles[FDrawingStyle])
  else
    ImageList_DrawEx(FHandle, Index, Canvas.Handle, X, Y, 0, 0, CLR_NONE, COLORREF(ColorToRGB(clBtnFace)),
      ILD_BLEND50);
end;

{ Image becomes Width x Height, white, with image Index drawn on it }
function TCustomImageList.GetBitmap(Index: Integer; Image: TBitmap): Boolean;
begin
  Result := (Image <> nil) and (Index >= 0) and (Index < Count);
  if not Result then
    Exit;
  Image.SetSize(FWidth, FHeight);
  Image.Canvas.Brush.Color := clWhite;
  Image.Canvas.Brush.Style := bsSolid;
  Image.Canvas.FillRect(Rect(0, 0, FWidth, FHeight));
  ImageList_Draw(FHandle, Index, Image.Canvas.Handle, 0, 0, ILD_NORMAL);
end;

{ ---------------------------------------------------------------- UIF-568: streaming }

procedure TCustomImageList.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  Filer.DefineBinaryProperty('Bitmap', ReadData, WriteData, Count > 0);
end;

type
  TPixel = array[0..3] of Byte;
  PPixel = ^TPixel;

function PixelAt(B: TBitmap; X, Y: Integer): PPixel;
begin
  Result := PPixel(NativeUInt(B.ScanLine[Y]) + NativeUInt(X * 4));
end;

{ all images side by side in one 32-bit row, alpha = mask (FF opaque, 0 transparent) }
procedure TCustomImageList.WriteData(Stream: TStream);
var
  Strip, Mask: TBitmap;
  I, X, Y, N: Integer;
  P: PPixel;
begin
  N := Count;
  Strip := TBitmap.Create;
  Mask := TBitmap.Create;
  try
    Strip.SetSize(N * FWidth, FHeight);
    Strip.Canvas.Brush.Color := clBlack;
    Strip.Canvas.FillRect(Rect(0, 0, N * FWidth, FHeight));
    Mask.SetSize(N * FWidth, FHeight);
    Mask.Canvas.Brush.Color := clWhite;
    Mask.Canvas.FillRect(Rect(0, 0, N * FWidth, FHeight));
    for I := 0 to N - 1 do
    begin
      ImageList_DrawEx(FHandle, I, Strip.Canvas.Handle, I * FWidth, 0, 0, 0, CLR_NONE, CLR_NONE, ILD_NORMAL);
      ImageList_Draw(FHandle, I, Mask.Canvas.Handle, I * FWidth, 0, ILD_MASK);
    end;
    for Y := 0 to FHeight - 1 do
      for X := 0 to N * FWidth - 1 do
      begin
        P := PixelAt(Strip, X, Y);
        if PixelAt(Mask, X, Y)^[0] = 0 then
          P^[3] := $FF
        else
        begin
          P^[0] := 0;
          P^[1] := 0;
          P^[2] := 0;
          P^[3] := 0;
        end;
      end;
    Strip.SaveToStream(Stream);
  finally
    Strip.Free;
    Mask.Free;
  end;
end;

procedure TCustomImageList.ReadData(Stream: TStream);
var
  Strip, Mask: TBitmap;
  X, Y: Integer;
  P, Q: PPixel;
begin
  Strip := TBitmap.Create;
  Mask := TBitmap.Create;
  try
    Strip.LoadFromStream(Stream);
    Strip.PixelFormat := pf32bit;
    Mask.SetSize(Strip.Width, Strip.Height);
    for Y := 0 to Strip.Height - 1 do
      for X := 0 to Strip.Width - 1 do
      begin
        P := PixelAt(Strip, X, Y);
        Q := PixelAt(Mask, X, Y);
        if P^[3] = 0 then
        begin
          Q^[0] := $FF;                                    { transparent: white mask }
          Q^[1] := $FF;
          Q^[2] := $FF;
        end
        else
        begin
          Q^[0] := 0;
          Q^[1] := 0;
          Q^[2] := 0;
        end;
        P^[3] := 0;                                        { the mask, not the alpha channel, decides }
      end;
    FreeHandle;
    if Strip.Width >= FWidth then
      ImageList_Add(Handle, Strip.Handle, Mask.Handle);
  finally
    Strip.Free;
    Mask.Free;
  end;
  Change;
end;

initialization
  RegisterClasses([TImageList]);                           { UIF-002 }
end.
