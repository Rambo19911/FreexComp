// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ImgList - image lists: an own store of premultiplied 32-bit cells from which the native image list is derived
  (graphics.md section 5; phase8/image-list.md sections 3 to 5 and 7). }
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
    FAllocBy: Integer;
    FClients: TList;
    FOnChange: TNotifyEvent;
    FCells: TByteDynArray;                                   { UIF-1460: the store, FCount cells of canonical pixels }
    FCount: Integer;
    FUpdateCount: Integer;
    FUpdateChanged: Boolean;
    procedure InitDefaults;
    function GetHandle: HIMAGELIST;
    function GetCount: Integer;
    procedure FreeHandle;
    procedure Emptied;
    procedure ApplyBkColor;
    function CellBytes: Integer;
    procedure CheckIndex(Index, Limit: Integer);
    procedure CheckSize(Value: TCustomImageList);
    procedure Reduce(var Cells: TByteDynArray; N: Integer);
    function CutCells(Image, Mask: TBitmap; UseColor: Boolean; MaskColor: TColor; Alpha: Boolean;
      N: Integer): TByteDynArray;
    function SourceCells(Image, Mask: TBitmap; UseColor: Boolean; MaskColor: TColor; var N: Integer): TByteDynArray;
    function IconCell(Image: TIcon): TByteDynArray;
    function NativeImage(const Cells: TByteDynArray; First, N: Integer; var Mask: HBITMAP): TBitmap;
    function NativeAdd(const Cells: TByteDynArray; First, N: Integer): Boolean;
    function NativeReplace(const Cells: TByteDynArray; Index: Integer): Boolean;
    procedure Commit(const Cells: TByteDynArray; NewCount, Edit, Index: Integer);
    procedure InsertCells(Index: Integer; const Cells: TByteDynArray; N: Integer);
    procedure SetWidth(Value: Integer);
    procedure SetHeight(Value: Integer);
    procedure SetMasked(Value: Boolean);
    procedure SetBkColor(Value: TColor);
    procedure SetDrawingStyle(Value: TDrawingStyle);
    procedure SetColorDepth(Value: TColorDepth);
    procedure SetAllocBy(Value: Integer);
    procedure ReadData(Stream: TStream);
    procedure WriteData(Stream: TStream);
  protected
    procedure Change; virtual;
    procedure DefineProperties(Filer: TFiler); override;
    property AllocBy: Integer read FAllocBy write SetAllocBy default 4;
    property ColorDepth: TColorDepth read FColorDepth write SetColorDepth default cd32Bit;
    property Height: Integer read FHeight write SetHeight default 16;
    property Width: Integer read FWidth write SetWidth default 16;
    property Masked: Boolean read FMasked write SetMasked default True;
    property BkColor: TColor read FBkColor write SetBkColor default clNone;
    property DrawingStyle: TDrawingStyle read FDrawingStyle write SetDrawingStyle default dsNormal;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  public
    constructor Create(AOwner: TComponent); override;
    constructor CreateSize(AWidth, AHeight: Integer);
    destructor Destroy; override;
    procedure Assign(Source: TPersistent); override;
    function HandleAllocated: Boolean;
    procedure SetSize(AWidth, AHeight: Integer);
    procedure BeginUpdate;
    procedure EndUpdate;
    function Add(Image, Mask: TBitmap): Integer;
    function AddMasked(Image: TBitmap; MaskColor: TColor): Integer;
    function AddIcon(Image: TIcon): Integer;
    function AddImage(Value: TCustomImageList; Index: Integer): Integer;
    procedure AddImages(Value: TCustomImageList);
    procedure Insert(Index: Integer; Image, Mask: TBitmap);
    procedure InsertMasked(Index: Integer; Image: TBitmap; MaskColor: TColor);
    procedure InsertIcon(Index: Integer; Image: TIcon);
    procedure Replace(Index: Integer; Image, Mask: TBitmap);
    procedure ReplaceMasked(Index: Integer; NewImage: TBitmap; MaskColor: TColor);
    procedure ReplaceIcon(Index: Integer; Image: TIcon);
    procedure Move(CurIndex, NewIndex: Integer);
    procedure Delete(Index: Integer);
    procedure Clear;
    procedure Draw(Canvas: TCanvas; X, Y, Index: Integer; Enabled: Boolean = True); overload;
    function GetBitmap(Index: Integer; Image: TBitmap): Boolean;
    function GetAlphaBitmap(Index: Integer; Image: TBitmap): Boolean;
    procedure GetIcon(Index: Integer; Image: TIcon);
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
    property AllocBy;
    property OnChange;
  end;

implementation

const
  { how Commit brings the native list in step with the new store (UIF-1467) }
  EditAppend = 1;                                            { ImageList_Add of the cells from Index on }
  EditReplace = 2;                                           { ImageList_Replace of cell Index }
  EditRemove = 3;                                            { ImageList_Remove(Index) }
  EditRebuild = 4;                                           { ImageList_Remove(-1), then one ImageList_Add of all }

{ ---------------------------------------------------------------- pixels }

{ the I-th 32-bit pixel from P }
function At(P: Pointer; I: Integer): PCardinal;
begin
  Result := PCardinal(NativeUInt(P) + NativeUInt(I) * 4);
end;

function MinInt(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

{ UIF-1461: colour bytes above the alpha byte are lowered to it; alpha 0 is 0, 0, 0, 0 }
function Canonical(P: Cardinal): Cardinal;
var
  A, C: Cardinal;
  I: Integer;
begin
  A := P shr 24;
  Result := 0;
  if A = 0 then
    Exit;
  Result := A shl 24;
  for I := 0 to 2 do
  begin
    C := (P shr (I * 8)) and $FF;
    if C > A then
      C := A;
    Result := Result or (C shl (I * 8));
  end;
end;

{ UIF-1466: canonical to straight alpha, (p x 255 + a div 2) div a capped at 255 }
function Straight(P: Cardinal): Cardinal;
var
  A, C: Cardinal;
  I: Integer;
begin
  A := P shr 24;
  Result := 0;
  if A = 0 then
    Exit;
  Result := A shl 24;
  for I := 0 to 2 do
  begin
    C := (((P shr (I * 8)) and $FF) * 255 + A div 2) div A;
    if C > 255 then
      C := 255;
    Result := Result or (C shl (I * 8));
  end;
end;

{ UIF-1464: straight alpha to canonical, (c x a + 127) div 255 }
function Premultiplied(P: Cardinal): Cardinal;
var
  A, C: Cardinal;
  I: Integer;
begin
  A := P shr 24;
  Result := 0;
  if A = 0 then
    Exit;
  Result := A shl 24;
  for I := 0 to 2 do
  begin
    C := (((P shr (I * 8)) and $FF) * A + 127) div 255;
    Result := Result or (C shl (I * 8));
  end;
end;

{ UIF-1465: alpha >= 128 becomes opaque with the colour un-premultiplied, the rest transparent }
function Reduced(P: Cardinal): Cardinal;
begin
  if (P shr 24) >= 128 then
    Result := Cardinal($FF000000) or (Straight(P) and $FFFFFF)
  else
    Result := 0;
end;

{ ---------------------------------------------------------------- cell arrays }

procedure CopyBytes(const Src: TByteDynArray; SrcOffset: Integer; var Dst: TByteDynArray; DstOffset, N: Integer);
begin
  if N > 0 then
    Move(Src[SrcOffset], Dst[DstOffset], N);
end;

{ cells 0 .. Index - 1 of Cells, then AddedCount cells of Added, then the cells after Index + Removed }
function Spliced(const Cells: TByteDynArray; Count, Index, Removed: Integer; const Added: TByteDynArray;
  AddedCount, CB: Integer): TByteDynArray;
begin
  SetLength(Result, (Count - Removed + AddedCount) * CB);
  CopyBytes(Cells, 0, Result, 0, Index * CB);
  CopyBytes(Added, 0, Result, Index * CB, AddedCount * CB);
  CopyBytes(Cells, (Index + Removed) * CB, Result, (Index + AddedCount) * CB, (Count - Index - Removed) * CB);
end;

procedure CopyMem(Src, Dst: Pointer; N: Integer);
begin
  if N > 0 then
    Move(Src^, Dst^, N);
end;

function Slice(const Cells: TByteDynArray; Index, N, CB: Integer): TByteDynArray;
begin
  SetLength(Result, N * CB);
  CopyBytes(Cells, Index * CB, Result, 0, N * CB);
end;

{ any alpha byte not 0 in the W x H top-left area of a pf32bit bitmap }
function HasAlpha(B: TBitmap; W, H: Integer): Boolean;
var
  X, Y: Integer;
  Row: Pointer;
begin
  Result := False;
  if B.PixelFormat <> pf32bit then
    Exit;
  for Y := 0 to H - 1 do
  begin
    Row := B.ScanLine[Y];
    for X := 0 to W - 1 do
      if (At(Row, X)^ shr 24) <> 0 then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

{ the pixel at X of a row of a pf24bit or pf32bit bitmap, alpha byte 0 for 24 bits }
function RowPixel(Row: Pointer; X: Integer; Bpp: Integer): Cardinal;
var
  P: PByte;
begin
  if Bpp = 4 then
    Result := At(Row, X)^
  else
  begin
    P := PByte(NativeUInt(Row) + NativeUInt(X * 3));
    Result := Cardinal(P^) or (Cardinal(PByte(NativeUInt(P) + 1)^) shl 8) or (Cardinal(PByte(NativeUInt(P) + 2)^) shl 16);
  end;
end;

function BytesPerPixel(B: TBitmap): Integer;
begin
  if B.PixelFormat = pf24bit then
    Result := 3
  else
    Result := 4;
end;

{ the W x H pixels of a bitmap handle as 32-bit top-down pixels (GetDIBits) }
function DIBPixels(DC: HDC; Bitmap: HBITMAP; W, H: Integer): TByteDynArray;
var
  BI: TBitmapInfo;
begin
  SetLength(Result, W * H * 4);
  FillChar(Result[0], W * H * 4, 0);
  FillChar(BI, SizeOf(BI), 0);
  BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  BI.bmiHeader.biWidth := W;
  BI.bmiHeader.biHeight := -H;
  BI.bmiHeader.biPlanes := 1;
  BI.bmiHeader.biBitCount := 32;
  BI.bmiHeader.biCompression := BI_RGB;
  if GetDIBits(DC, Bitmap, 0, H, @Result[0], @BI, DIB_RGB_COLORS) = 0 then
    raise EOutOfResources.Create(SOutOfResources);
end;

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
  InitDefaults;
end;

{ UIF-1468: Create(nil) with the size }
constructor TCustomImageList.CreateSize(AWidth, AHeight: Integer);
begin
  inherited Create(nil);
  InitDefaults;
  FWidth := AWidth;
  FHeight := AHeight;
end;

procedure TCustomImageList.InitDefaults;
begin
  FWidth := 16;
  FHeight := 16;
  FMasked := True;
  FBkColor := clNone;
  FDrawingStyle := dsNormal;
  FColorDepth := cd32Bit;
  FAllocBy := 4;
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

function TCustomImageList.CellBytes: Integer;
begin
  Result := FWidth * FHeight * 4;
end;

procedure TCustomImageList.ApplyBkColor;
begin
  if FBkColor = clNone then
    ImageList_SetBkColor(FHandle, CLR_NONE)
  else
    ImageList_SetBkColor(FHandle, COLORREF(ColorToRGB(FBkColor)));
end;

{ UIF-1466: created on first use; the whole store is handed over as one strip }
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
    FHandle := ImageList_Create(FWidth, FHeight, Flags, FAllocBy, FAllocBy);
    if FHandle = 0 then
      raise EOutOfResources.Create(SOutOfResources);
    ApplyBkColor;
    if not NativeAdd(FCells, 0, FCount) then
    begin
      FreeHandle;
      raise EOutOfResources.Create(SOutOfResources);
    end;
  end;
  Result := FHandle;
end;

procedure TCustomImageList.FreeHandle;
begin
  if FHandle <> 0 then
    ImageList_Destroy(FHandle);
  FHandle := 0;
end;

{ a new size or format starts an empty list }
procedure TCustomImageList.Emptied;
begin
  FreeHandle;
  SetLength(FCells, 0);
  FCount := 0;
end;

function TCustomImageList.GetCount: Integer;
begin
  Result := FCount;
end;

{ UIF-1470: nothing fires inside an update bracket; its end fires once when something changed }
procedure TCustomImageList.Change;
var
  I: Integer;
begin
  if FUpdateCount > 0 then
  begin
    FUpdateChanged := True;
    Exit;
  end;
  for I := 0 to FClients.Count - 1 do
    TChangeLink(FClients[I]).Change;
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TCustomImageList.BeginUpdate;
begin
  Inc(FUpdateCount);
end;

procedure TCustomImageList.EndUpdate;
begin
  if FUpdateCount = 0 then
    Exit;
  Dec(FUpdateCount);
  if (FUpdateCount = 0) and FUpdateChanged then
  begin
    FUpdateChanged := False;
    Change;
  end;
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

procedure TCustomImageList.SetWidth(Value: Integer);
begin
  if Value <> FWidth then
  begin
    FWidth := Value;
    Emptied;
    Change;
  end;
end;

procedure TCustomImageList.SetHeight(Value: Integer);
begin
  if Value <> FHeight then
  begin
    FHeight := Value;
    Emptied;
    Change;
  end;
end;

procedure TCustomImageList.SetSize(AWidth, AHeight: Integer);
begin
  if (AWidth = FWidth) and (AHeight = FHeight) then
    Exit;
  FWidth := AWidth;
  FHeight := AHeight;
  Emptied;
  Change;
end;

procedure TCustomImageList.SetMasked(Value: Boolean);
begin
  if Value <> FMasked then
  begin
    FMasked := Value;
    Emptied;
    Change;
  end;
end;

procedure TCustomImageList.SetColorDepth(Value: TColorDepth);
begin
  if Value <> FColorDepth then
  begin
    FColorDepth := Value;
    Emptied;
    Change;
  end;
end;

procedure TCustomImageList.SetBkColor(Value: TColor);
begin
  if Value <> FBkColor then
  begin
    FBkColor := Value;
    if FHandle <> 0 then
      ApplyBkColor;
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

{ UIF-1468: below 1 is stored as 1; used by handles created afterwards }
procedure TCustomImageList.SetAllocBy(Value: Integer);
begin
  if Value < 1 then
    Value := 1;
  if Value <> FAllocBy then
  begin
    FAllocBy := Value;
    Change;
  end;
end;

procedure TCustomImageList.CheckIndex(Index, Limit: Integer);
begin
  if (Index < 0) or (Index >= Limit) then
    raise EInvalidOperation.Create(SInvalidImageListIndex);
end;

procedure TCustomImageList.CheckSize(Value: TCustomImageList);
begin
  if (Value.FWidth <> FWidth) or (Value.FHeight <> FHeight) then
    raise EInvalidOperation.Create(SImageListSizeMismatch);
end;

{ UIF-1465: the store of a 24-bit list holds the reduced pixels }
procedure TCustomImageList.Reduce(var Cells: TByteDynArray; N: Integer);
var
  I: Integer;
begin
  if FColorDepth <> cd24Bit then
    Exit;
  for I := 0 to N * FWidth * FHeight - 1 do
    At(@Cells[0], I)^ := Reduced(At(@Cells[0], I)^);
end;

{ UIF-1462, UIF-1463: N cells cut from Image; Alpha: the pixels are canonical, else opaque with the mask or the
  mask colour applied (masked lists only) }
function TCustomImageList.CutCells(Image, Mask: TBitmap; UseColor: Boolean; MaskColor: TColor; Alpha: Boolean;
  N: Integer): TByteDynArray;
var
  I, X, Y, SX, Bpp, MaskBpp, Rows, CellPixels: Integer;
  Row, MaskRow: Pointer;
  V, M, Key: Cardinal;
  Hidden: Boolean;
begin
  CellPixels := FWidth * FHeight;
  SetLength(Result, N * CellPixels * 4);
  if N = 0 then
    Exit;
  FillChar(Result[0], N * CellPixels * 4, 0);
  Key := Cardinal(ColorToRGB(MaskColor));
  Key := ((Key and $FF) shl 16) or (Key and $FF00) or ((Key shr 16) and $FF);
  Bpp := BytesPerPixel(Image);
  MaskBpp := 4;
  if (Mask <> nil) and not Mask.Empty then
    MaskBpp := BytesPerPixel(Mask);
  Rows := MinInt(Image.Height, FHeight);
  for Y := 0 to Rows - 1 do
  begin
    Row := Image.ScanLine[Y];
    MaskRow := nil;
    if FMasked and not Alpha and (Mask <> nil) and not Mask.Empty and (Y < Mask.Height) then
      MaskRow := Mask.ScanLine[Y];
    for I := 0 to N - 1 do
      for X := 0 to FWidth - 1 do
      begin
        SX := I * FWidth + X;
        if SX < Image.Width then
        begin
          V := RowPixel(Row, SX, Bpp);
          if Alpha then
            V := Canonical(V)
          else
          begin
            V := (V and $FFFFFF) or Cardinal($FF000000);
            Hidden := False;
            if FMasked then
            begin
              if (MaskRow <> nil) and (SX < Mask.Width) then
              begin
                M := RowPixel(MaskRow, SX, MaskBpp);
                Hidden := (M and $FFFFFF) <> 0;
              end;
              if UseColor and ((V and $FFFFFF) = Key) then
                Hidden := True;
            end;
            if Hidden then
              V := 0;
          end;
          At(@Result[0], I * CellPixels + Y * FWidth + X)^ := V;
        end;
      end;
  end;
  Reduce(Result, N);
end;

{ UIF-1462: the cells of a bitmap given to Add, Insert, Replace and their Masked variants }
function TCustomImageList.SourceCells(Image, Mask: TBitmap; UseColor: Boolean; MaskColor: TColor;
  var N: Integer): TByteDynArray;
var
  Alpha: Boolean;
begin
  if (Image = nil) or Image.Empty then
    raise EInvalidOperation.Create(SInvalidImageSize);
  if Image.Width >= FWidth then
    N := Image.Width div FWidth
  else
    N := 1;
  Alpha := HasAlpha(Image, MinInt(Image.Width, N * FWidth), MinInt(Image.Height, FHeight));
  Result := CutCells(Image, Mask, UseColor, MaskColor, Alpha, N);
end;

{ UIF-1464: one cell from an icon, scaled by nearest neighbour }
function TCustomImageList.IconCell(Image: TIcon): TByteDynArray;
var
  Info: TIconInfo;
  BM: TBitmapRec;
  DC: HDC;
  IW, IH, ColorFirst, X, Y, I, SX, SY: Integer;
  Colors, AndMask: TByteDynArray;
  Alpha: Boolean;
  C, M, V: Cardinal;
begin
  if (Image = nil) or Image.Empty then
    raise EInvalidOperation.Create(SInvalidImageSize);
  FillChar(Info, SizeOf(Info), 0);
  if not GetIconInfo(Image.Handle, Info) then
    raise EInvalidOperation.Create(SInvalidImageSize);
  DC := CreateCompatibleDC(0);
  try
    FillChar(BM, SizeOf(BM), 0);
    if Info.hbmColor <> 0 then
    begin
      GetObjectW(Info.hbmColor, SizeOf(BM), @BM);
      IW := BM.bmWidth;
      IH := BM.bmHeight;
      Colors := DIBPixels(DC, Info.hbmColor, IW, IH);
      AndMask := DIBPixels(DC, Info.hbmMask, IW, IH);
      ColorFirst := 0;
    end
    else
    begin
      GetObjectW(Info.hbmMask, SizeOf(BM), @BM);             { monochrome: AND rows on top, XOR rows below }
      IW := BM.bmWidth;
      IH := BM.bmHeight div 2;
      AndMask := DIBPixels(DC, Info.hbmMask, IW, 2 * IH);
      Colors := AndMask;
      ColorFirst := IW * IH;
    end;
  finally
    DeleteDC(DC);
    if Info.hbmMask <> 0 then
      DeleteObject(Info.hbmMask);
    if Info.hbmColor <> 0 then
      DeleteObject(Info.hbmColor);
  end;
  Alpha := False;
  for I := 0 to IW * IH - 1 do
    if (At(@Colors[0], ColorFirst + I)^ shr 24) <> 0 then
      Alpha := True;
  SetLength(Result, CellBytes);
  for Y := 0 to FHeight - 1 do
    for X := 0 to FWidth - 1 do
    begin
      SX := X * IW div FWidth;
      SY := Y * IH div FHeight;
      C := At(@Colors[0], ColorFirst + SY * IW + SX)^;
      M := At(@AndMask[0], SY * IW + SX)^;
      if Alpha then
        V := Canonical(Premultiplied(C))
      else if FMasked and ((M and $FFFFFF) <> 0) then
        V := 0
      else
        V := (C and $FFFFFF) or Cardinal($FF000000);
      At(@Result[0], Y * FWidth + X)^ := V;
    end;
  Reduce(Result, 1);
end;

{ UIF-1466: N cells from First as a DIB in straight alpha (24 bits for cd24Bit) and, for a masked list, a
  monochrome mask white where alpha is 0 }
function TCustomImageList.NativeImage(const Cells: TByteDynArray; First, N: Integer; var Mask: HBITMAP): TBitmap;
var
  W, X, Y, Stride, CellPixels, Bpp: Integer;
  Bits: TByteDynArray;
  Row: Pointer;
  P: PByte;
  V: Cardinal;
begin
  W := N * FWidth;
  CellPixels := FWidth * FHeight;
  Stride := ((W + 15) div 16) * 2;
  SetLength(Bits, Stride * FHeight);
  FillChar(Bits[0], Stride * FHeight, 0);
  Result := TBitmap.Create;
  try
    if FColorDepth = cd24Bit then
      Result.PixelFormat := pf24bit;
    Bpp := BytesPerPixel(Result);
    Result.SetSize(W, FHeight);
    for Y := 0 to FHeight - 1 do
    begin
      Row := Result.ScanLine[Y];
      for X := 0 to W - 1 do
      begin
        V := At(@Cells[0], (First + X div FWidth) * CellPixels + Y * FWidth + X mod FWidth)^;
        if (V shr 24) = 0 then
          Bits[Y * Stride + X div 8] := Bits[Y * Stride + X div 8] or ($80 shr (X mod 8));
        if Bpp = 4 then
          At(Row, X)^ := Straight(V)
        else
        begin
          P := PByte(NativeUInt(Row) + NativeUInt(X * 3));
          P^ := Byte(V);
          PByte(NativeUInt(P) + 1)^ := Byte(V shr 8);
          PByte(NativeUInt(P) + 2)^ := Byte(V shr 16);
        end;
      end;
    end;
    Mask := 0;
    if FMasked then
    begin
      Mask := CreateBitmap(W, FHeight, 1, 1, @Bits[0]);
      if Mask = 0 then
        raise EOutOfResources.Create(SOutOfResources);
    end;
  except
    Result.Free;
    raise;
  end;
end;

function TCustomImageList.NativeAdd(const Cells: TByteDynArray; First, N: Integer): Boolean;
var
  Image: TBitmap;
  Mask: HBITMAP;
begin
  Result := True;
  if N <= 0 then
    Exit;
  Image := NativeImage(Cells, First, N, Mask);
  try
    Result := ImageList_Add(FHandle, Image.Handle, Mask) >= 0;
  finally
    Image.Free;
    if Mask <> 0 then
      DeleteObject(Mask);
  end;
end;

function TCustomImageList.NativeReplace(const Cells: TByteDynArray; Index: Integer): Boolean;
var
  Image: TBitmap;
  Mask: HBITMAP;
begin
  Image := NativeImage(Cells, Index, 1, Mask);
  try
    Result := ImageList_Replace(FHandle, Index, Image.Handle, Mask);
  finally
    Image.Free;
    if Mask <> 0 then
      DeleteObject(Mask);
  end;
end;

{ UIF-1467, UIF-1470: the native list follows (when there is one), then the store takes Cells, then one Change;
  a failing native call frees the handle, keeps the store and fires nothing }
procedure TCustomImageList.Commit(const Cells: TByteDynArray; NewCount, Edit, Index: Integer);
var
  Ok: Boolean;
begin
  if FHandle <> 0 then
  begin
    case Edit of
      EditAppend: Ok := NativeAdd(Cells, Index, NewCount - Index);
      EditReplace: Ok := NativeReplace(Cells, Index);
      EditRemove: Ok := ImageList_Remove(FHandle, Index);
    else
      Ok := ImageList_Remove(FHandle, -1);
      if Ok then
        Ok := NativeAdd(Cells, 0, NewCount);
    end;
    if not Ok then
    begin
      FreeHandle;
      raise EOutOfResources.Create(SOutOfResources);
    end;
  end;
  FCells := Cells;
  FCount := NewCount;
  Change;
end;

procedure TCustomImageList.InsertCells(Index: Integer; const Cells: TByteDynArray; N: Integer);
var
  NewCells: TByteDynArray;
begin
  NewCells := Spliced(FCells, FCount, Index, 0, Cells, N, CellBytes);
  if Index = FCount then
    Commit(NewCells, FCount + N, EditAppend, Index)
  else
    Commit(NewCells, FCount + N, EditRebuild, 0);
end;

{ UIF-1463: the index of the first new cell }
function TCustomImageList.Add(Image, Mask: TBitmap): Integer;
var
  Cells: TByteDynArray;
  N: Integer;
begin
  Cells := SourceCells(Image, Mask, False, clNone, N);
  Result := FCount;
  InsertCells(FCount, Cells, N);
end;

function TCustomImageList.AddMasked(Image: TBitmap; MaskColor: TColor): Integer;
var
  Cells: TByteDynArray;
  N: Integer;
begin
  Cells := SourceCells(Image, nil, True, MaskColor, N);
  Result := FCount;
  InsertCells(FCount, Cells, N);
end;

function TCustomImageList.AddIcon(Image: TIcon): Integer;
var
  Cells: TByteDynArray;
begin
  Cells := IconCell(Image);
  Result := FCount;
  InsertCells(FCount, Cells, 1);
end;

{ UIF-1469: copies from store to store }
function TCustomImageList.AddImage(Value: TCustomImageList; Index: Integer): Integer;
var
  Cells: TByteDynArray;
begin
  Result := -1;
  if Value = nil then
    Exit;
  CheckSize(Value);
  Value.CheckIndex(Index, Value.FCount);
  Cells := Slice(Value.FCells, Index, 1, CellBytes);
  Reduce(Cells, 1);
  Result := FCount;
  InsertCells(FCount, Cells, 1);
end;

procedure TCustomImageList.AddImages(Value: TCustomImageList);
var
  Cells: TByteDynArray;
  N: Integer;
begin
  if Value = nil then
    Exit;
  CheckSize(Value);
  N := Value.FCount;
  if N = 0 then
    Exit;
  Cells := Slice(Value.FCells, 0, N, CellBytes);
  Reduce(Cells, N);
  InsertCells(FCount, Cells, N);
end;

procedure TCustomImageList.Insert(Index: Integer; Image, Mask: TBitmap);
var
  Cells: TByteDynArray;
  N: Integer;
begin
  CheckIndex(Index, FCount + 1);
  Cells := SourceCells(Image, Mask, False, clNone, N);
  InsertCells(Index, Cells, N);
end;

procedure TCustomImageList.InsertMasked(Index: Integer; Image: TBitmap; MaskColor: TColor);
var
  Cells: TByteDynArray;
  N: Integer;
begin
  CheckIndex(Index, FCount + 1);
  Cells := SourceCells(Image, nil, True, MaskColor, N);
  InsertCells(Index, Cells, N);
end;

procedure TCustomImageList.InsertIcon(Index: Integer; Image: TIcon);
var
  Cells: TByteDynArray;
begin
  CheckIndex(Index, FCount + 1);
  Cells := IconCell(Image);
  InsertCells(Index, Cells, 1);
end;

{ UIF-1463: cell 0 of the source replaces cell Index }
procedure TCustomImageList.Replace(Index: Integer; Image, Mask: TBitmap);
var
  Cells: TByteDynArray;
  N: Integer;
begin
  CheckIndex(Index, FCount);
  Cells := SourceCells(Image, Mask, False, clNone, N);
  Commit(Spliced(FCells, FCount, Index, 1, Cells, 1, CellBytes), FCount, EditReplace, Index);
end;

procedure TCustomImageList.ReplaceMasked(Index: Integer; NewImage: TBitmap; MaskColor: TColor);
var
  Cells: TByteDynArray;
  N: Integer;
begin
  CheckIndex(Index, FCount);
  Cells := SourceCells(NewImage, nil, True, MaskColor, N);
  Commit(Spliced(FCells, FCount, Index, 1, Cells, 1, CellBytes), FCount, EditReplace, Index);
end;

procedure TCustomImageList.ReplaceIcon(Index: Integer; Image: TIcon);
var
  Cells: TByteDynArray;
begin
  CheckIndex(Index, FCount);
  Cells := IconCell(Image);
  Commit(Spliced(FCells, FCount, Index, 1, Cells, 1, CellBytes), FCount, EditReplace, Index);
end;

{ UIF-1468: the cell is taken out at CurIndex and put in at NewIndex }
procedure TCustomImageList.Move(CurIndex, NewIndex: Integer);
var
  Taken, Rest, None: TByteDynArray;
begin
  CheckIndex(CurIndex, FCount);
  CheckIndex(NewIndex, FCount);
  if CurIndex = NewIndex then
    Exit;
  SetLength(None, 0);
  Taken := Slice(FCells, CurIndex, 1, CellBytes);
  Rest := Spliced(FCells, FCount, CurIndex, 1, None, 0, CellBytes);
  Commit(Spliced(Rest, FCount - 1, NewIndex, 0, Taken, 1, CellBytes), FCount, EditRebuild, 0);
end;

procedure TCustomImageList.Delete(Index: Integer);
var
  None: TByteDynArray;
begin
  CheckIndex(Index, FCount);
  SetLength(None, 0);
  Commit(Spliced(FCells, FCount, Index, 1, None, 0, CellBytes), FCount - 1, EditRemove, Index);
end;

procedure TCustomImageList.Clear;
var
  None: TByteDynArray;
begin
  SetLength(None, 0);
  Commit(None, 0, EditRebuild, 0);
end;

{ UIF-1469: properties and cells; the handle stays when the format is the same, else it is made again on use }
procedure TCustomImageList.Assign(Source: TPersistent);
var
  S: TCustomImageList;
begin
  if Source is TCustomImageList then
  begin
    S := TCustomImageList(Source);
    if S = Self then
      Exit;
    if (S.FWidth <> FWidth) or (S.FHeight <> FHeight) or (S.FMasked <> FMasked) or
      (S.FColorDepth <> FColorDepth) then
      FreeHandle;
    FWidth := S.FWidth;
    FHeight := S.FHeight;
    FMasked := S.FMasked;
    FColorDepth := S.FColorDepth;
    FBkColor := S.FBkColor;
    FDrawingStyle := S.FDrawingStyle;
    FAllocBy := S.FAllocBy;
    if FHandle <> 0 then
      ApplyBkColor;
    Commit(Slice(S.FCells, 0, S.FCount, CellBytes), S.FCount, EditRebuild, 0);
  end
  else
    inherited Assign(Source);
end;

{ UIF-566: disabled images are blended 50 % with clBtnFace }
procedure TCustomImageList.Draw(Canvas: TCanvas; X, Y, Index: Integer; Enabled: Boolean = True);
const
  Styles: array[TDrawingStyle] of UINT = (ILD_FOCUS, ILD_SELECTED, ILD_NORMAL, ILD_TRANSPARENT);
var
  Bk: COLORREF;
begin
  if (Index < 0) or (Index >= FCount) then
    Exit;
  if FBkColor = clNone then
    Bk := CLR_NONE
  else
    Bk := COLORREF(ColorToRGB(FBkColor));
  if Enabled then
    ImageList_DrawEx(Handle, Index, Canvas.Handle, X, Y, 0, 0, Bk, CLR_DEFAULT, Styles[FDrawingStyle])
  else
    ImageList_DrawEx(Handle, Index, Canvas.Handle, X, Y, 0, 0, CLR_NONE, COLORREF(ColorToRGB(clBtnFace)),
      ILD_BLEND50);
end;

{ UIF-1471: Width x Height, each pixel the canonical pixel over white (c + 255 - a), alpha byte 0 }
function TCustomImageList.GetBitmap(Index: Integer; Image: TBitmap): Boolean;
var
  X, Y, I, Bpp: Integer;
  Row: Pointer;
  P: PByte;
  V, A: Cardinal;
begin
  Result := (Image <> nil) and (Index >= 0) and (Index < FCount);
  if not Result then
    Exit;
  Image.SetSize(FWidth, FHeight);
  Bpp := BytesPerPixel(Image);
  for Y := 0 to FHeight - 1 do
  begin
    Row := Image.ScanLine[Y];
    for X := 0 to FWidth - 1 do
    begin
      V := At(@FCells[0], Index * FWidth * FHeight + Y * FWidth + X)^;
      A := V shr 24;
      P := PByte(NativeUInt(Row) + NativeUInt(X * Bpp));
      for I := 0 to 2 do
        PByte(NativeUInt(P) + NativeUInt(I))^ := Byte(((V shr (I * 8)) and $FF) + 255 - A);
      if Bpp = 4 then
        PByte(NativeUInt(P) + 3)^ := 0;
    end;
  end;
end;

function TCustomImageList.GetAlphaBitmap(Index: Integer; Image: TBitmap): Boolean;
var
  Y: Integer;
begin
  Result := (Image <> nil) and (Index >= 0) and (Index < FCount);
  if not Result then
    Exit;
  Image.PixelFormat := pf32bit;
  Image.SetSize(FWidth, FHeight);
  for Y := 0 to FHeight - 1 do
    CopyMem(@FCells[(Index * FHeight + Y) * FWidth * 4], Image.ScanLine[Y], FWidth * 4);
end;

procedure TCustomImageList.GetIcon(Index: Integer; Image: TIcon);
var
  H: HICON;
begin
  CheckIndex(Index, FCount);
  if Image = nil then
    raise EInvalidOperation.Create(SInvalidImageSize);
  H := ImageList_GetIcon(Handle, Index, ILD_NORMAL);
  if H = 0 then
    raise EOutOfResources.Create(SOutOfResources);
  Image.Handle := H;
end;

{ ---------------------------------------------------------------- UIF-568, UIF-1472 .. 1474: streaming }

procedure TCustomImageList.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  Filer.DefineBinaryProperty('Bitmap', ReadData, WriteData, FCount > 0);
end;

{ UIF-1472: one 32-bit BMP of all cells side by side, canonical pixels }
procedure TCustomImageList.WriteData(Stream: TStream);
var
  Strip: TBitmap;
  I, Y: Integer;
begin
  Strip := TBitmap.Create;
  try
    Strip.PixelFormat := pf32bit;
    Strip.SetSize(FCount * FWidth, FHeight);
    for I := 0 to FCount - 1 do
      for Y := 0 to FHeight - 1 do
        CopyMem(@FCells[(I * FHeight + Y) * FWidth * 4], At(Strip.ScanLine[Y], I * FWidth), FWidth * 4);
    Strip.SaveToStream(Stream);
  finally
    Strip.Free;
  end;
end;

{ UIF-1473: any BMP the bitmap reader accepts; without alpha the cells are opaque }
procedure TCustomImageList.ReadData(Stream: TStream);
var
  Strip: TBitmap;
  Cells: TByteDynArray;
  N: Integer;
begin
  Strip := TBitmap.Create;
  try
    Strip.LoadFromStream(Stream);
    N := 0;
    if Strip.Width >= FWidth then
      N := Strip.Width div FWidth;
    Cells := CutCells(Strip, nil, False, clNone, HasAlpha(Strip, Strip.Width, Strip.Height), N);
  finally
    Strip.Free;
  end;
  Commit(Cells, N, EditRebuild, 0);
end;

initialization
  RegisterClasses([TImageList]);                           { UIF-002 }
end.
