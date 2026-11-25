// covers: RTL-1030, RTL-1031, RTL-1032, RTL-1033, RTL-1034, RTL-1037
// phase: 8
program Win_WicSmoke;
{ The imaging component of the operating system through the Wincodec bindings: every method the image codec class
  will use is called on real objects and checked against a result a wrong method slot cannot produce. The image is
  a 4 x 3 bitmap file with twelve different 24-bit pixels, built here in memory. Needs no desktop and no file. }
uses Windows, ActiveX, Wincodec, TestKit;

const
  W = 4;
  H = 3;
  RowBytes = (W * 3 + 3) div 4 * 4;        // rows of a bitmap file are padded to 4 bytes
  FileSize = 14 + 40 + RowBytes * H;

type
  TQuad = array[0..3] of Byte;             // blue, green, red, alpha
  PQuads = ^TQuads;
  TQuads = array[0..47] of TQuad;

var
  FileBytes: array[0..FileSize - 1] of Byte;
  Factory: IWICImagingFactory;

{ the colour of pixel (X, Y), Y counted from the top: twelve different values in every channel }
function Blue(X, Y: Integer): Byte;
begin
  Result := 16 * (Y * W + X) + 1;
end;

function Green(X, Y: Integer): Byte;
begin
  Result := 255 - 16 * (Y * W + X);
end;

function Red(X, Y: Integer): Byte;
begin
  Result := 7 * (Y * W + X) + 3;
end;

procedure BuildFile;
var
  FH: PBitmapFileHeader;
  IH: PBitmapInfoHeader;
  X, Y, At: Integer;
begin
  FillChar(FileBytes, SizeOf(FileBytes), 0);
  FH := PBitmapFileHeader(@FileBytes[0]);
  FH^.bfType := $4D42;
  FH^.bfSize := FileSize;
  FH^.bfOffBits := 14 + 40;
  IH := PBitmapInfoHeader(@FileBytes[14]);
  IH^.biSize := 40;
  IH^.biWidth := W;
  IH^.biHeight := H;                       // positive: the rows are stored bottom-up
  IH^.biPlanes := 1;
  IH^.biBitCount := 24;
  IH^.biCompression := BI_RGB;
  IH^.biSizeImage := RowBytes * H;
  for Y := 0 to H - 1 do
    for X := 0 to W - 1 do
    begin
      At := 54 + (H - 1 - Y) * RowBytes + X * 3;
      FileBytes[At] := Blue(X, Y);
      FileBytes[At + 1] := Green(X, Y);
      FileBytes[At + 2] := Red(X, Y);
    end;
end;

{ True when Q is pixel (X, Y) of the input with alpha 255 }
function IsPixel(const Q: TQuad; X, Y: Integer): Boolean;
begin
  Result := (Q[0] = Blue(X, Y)) and (Q[1] = Green(X, Y)) and (Q[2] = Red(X, Y)) and (Q[3] = 255);
end;

procedure TestStream(const Stream: IStream);
var
  St: TStatStg;
  Pos: UInt64;
  Got: Cardinal;
  Four: Cardinal;
begin
  Section('the memory stream (RTL-1031, RTL-1037)');
  FillChar(St, SizeOf(St), $FF);
  CheckEqI(S_OK, Stream.Stat(St, STATFLAG_NONAME), 'Stat');
  CheckEqI(FileSize, St.cbSize, 'Stat reports the size');
  Check(St.pwcsName = nil, 'Stat returns no name when none is asked for');
  Pos := 0;
  CheckEqI(S_OK, Stream.Seek(0, STREAM_SEEK_END, @Pos), 'Seek to the end');
  CheckEqI(FileSize, Pos, 'the end position');
  CheckEqI(S_OK, Stream.Seek(14, STREAM_SEEK_SET, @Pos), 'Seek to the information header');
  CheckEqI(14, Pos, 'the new position');
  Four := 0;
  Got := 0;
  CheckEqI(S_OK, Stream.Read(@Four, 4, @Got), 'Read');
  CheckEqI(4, Got, 'the number of bytes read');
  CheckEqI(40, Four, 'the bytes are those of the file');
  CheckEqI(S_OK, Stream.Seek(-4, STREAM_SEEK_CUR, @Pos), 'Seek back');
  CheckEqI(14, Pos, 'the position after seeking back');
  CheckEqI(S_OK, Stream.Seek(0, STREAM_SEEK_SET, nil), 'Seek to the start');
end;

procedure TestDecode(const Stream: IStream);
var
  Decoder: IWICBitmapDecoder;
  Frame: IWICBitmapFrameDecode;
  Converter: IWICFormatConverter;
  Scaler: IWICBitmapScaler;
  Rotator: IWICBitmapFlipRotator;
  Reader: IWICMetadataQueryReader;
  Format: TGUID;
  V: TPropVariant;
  Buf: TQuads;
  CX, CY: UINT;
  X, Y, Bad: Integer;
  HR: HResult;
begin
  Section('decoding (RTL-1033, RTL-1034, RTL-1037)');
  CheckEqI(S_OK, Factory.CreateDecoderFromStream(Stream, nil, WICDecodeMetadataCacheOnDemand, Decoder),
    'CreateDecoderFromStream');
  CheckEqI(S_OK, Decoder.GetContainerFormat(Format), 'GetContainerFormat');
  Check(Format = GUID_ContainerFormatBmp, 'the container is the bitmap container');
  CheckEqI(S_OK, Decoder.GetFrame(0, Frame), 'GetFrame');
  CheckEqI(S_OK, Frame.GetSize(CX, CY), 'GetSize');
  Check((CX = W) and (CY = H), 'the size is 4 x 3');
  CheckEqI(S_OK, Frame.GetPixelFormat(Format), 'GetPixelFormat');
  Check(Format = GUID_WICPixelFormat24bppBGR, 'the pixel format is the 24-bit one');

  CheckEqI(S_OK, Factory.CreateFormatConverter(Converter), 'CreateFormatConverter');
  CheckEqI(S_OK, Converter.Initialize(Frame, GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nil, 0.0,
    WICBitmapPaletteTypeCustom), 'IWICFormatConverter.Initialize');
  CheckEqI(S_OK, Converter.GetPixelFormat(Format), 'GetPixelFormat of the converter');
  Check(Format = GUID_WICPixelFormat32bppPBGRA, 'premultiplied 32-bit');
  FillChar(Buf, SizeOf(Buf), 0);
  CheckEqI(S_OK, Converter.CopyPixels(nil, W * 4, W * H * 4, @Buf[0]), 'CopyPixels');
  Bad := 0;
  for Y := 0 to H - 1 do
    for X := 0 to W - 1 do
      if not IsPixel(Buf[Y * W + X], X, Y) then
        Inc(Bad);
  CheckEqI(0, Bad, 'the twelve pixels equal the input with alpha 255');

  Section('scaling and rotating (RTL-1034, RTL-1037)');
  CheckEqI(S_OK, Factory.CreateBitmapScaler(Scaler), 'CreateBitmapScaler');
  CheckEqI(S_OK, Scaler.Initialize(Converter, W * 2, H * 2, WICBitmapInterpolationModeNearestNeighbor),
    'IWICBitmapScaler.Initialize');
  CheckEqI(S_OK, Scaler.GetSize(CX, CY), 'GetSize of the scaler');
  Check((CX = W * 2) and (CY = H * 2), 'the size is 8 x 6');
  FillChar(Buf, SizeOf(Buf), 0);
  CheckEqI(S_OK, Scaler.CopyPixels(nil, W * 2 * 4, W * 2 * H * 2 * 4, @Buf[0]), 'CopyPixels of the scaler');
  Bad := 0;
  for Y := 0 to H * 2 - 1 do
    for X := 0 to W * 2 - 1 do
      if not IsPixel(Buf[Y * W * 2 + X], X div 2, Y div 2) then
        Inc(Bad);
  CheckEqI(0, Bad, 'every pixel became a 2 x 2 block');

  CheckEqI(S_OK, Factory.CreateBitmapFlipRotator(Rotator), 'CreateBitmapFlipRotator');
  CheckEqI(S_OK, Rotator.Initialize(Converter, WICBitmapTransformRotate90), 'IWICBitmapFlipRotator.Initialize');
  CheckEqI(S_OK, Rotator.GetSize(CX, CY), 'GetSize of the rotator');
  Check((CX = H) and (CY = W), 'the size is 3 x 4');
  FillChar(Buf, SizeOf(Buf), 0);
  CheckEqI(S_OK, Rotator.CopyPixels(nil, H * 4, H * W * 4, @Buf[0]), 'CopyPixels of the rotator');
  Check(IsPixel(Buf[0], 0, H - 1), 'top left is the bottom left of the input');
  Check(IsPixel(Buf[H - 1], 0, 0), 'top right is the top left of the input');
  Check(IsPixel(Buf[(W - 1) * H], W - 1, H - 1), 'bottom left is the bottom right of the input');
  Check(IsPixel(Buf[W * H - 1], W - 1, 0), 'bottom right is the top right of the input');

  Section('metadata (RTL-1034)');
  HR := Frame.GetMetadataQueryReader(Reader);
  if Succeeded(HR) then
  begin
    FillChar(V, SizeOf(V), 0);
    HR := Reader.GetMetadataByName('/nothere', V);
    Check(HR = WINCODEC_ERR_PROPERTYNOTFOUND, 'no metadata');
  end
  else
    Check(Reader = nil, 'no metadata');
end;

procedure TestGarbage;
var
  Junk: array[0..63] of Byte;
  Stream: IStream;
  Raw: Pointer;
  Decoder: IWICBitmapDecoder;
begin
  Section('bytes that are no image (RTL-1034)');
  FillChar(Junk, SizeOf(Junk), $A5);
  Raw := SHCreateMemStream(@Junk[0], SizeOf(Junk));
  Stream := IStream(Raw);       // the variable takes over the reference the creator returned
  if Raw <> nil then
    Stream._Release;
  Check(Stream <> nil, 'a memory stream of garbage');
  Check(Failed(Factory.CreateDecoderFromStream(Stream, nil, WICDecodeMetadataCacheOnDemand, Decoder)), 'failed');
  Check(Decoder = nil, 'no decoder');
end;

var
  Stream: IStream;
  Raw: Pointer;
begin
  BuildFile;
  Section('the imaging component (RTL-1030, RTL-1032)');
  Check(Succeeded(CoInitializeEx(nil, COINIT_APARTMENTTHREADED)), 'CoInitializeEx');
  CheckEqI(S_OK, CoCreateInstance(CLSID_WICImagingFactory, nil, CLSCTX_INPROC_SERVER, IWICImagingFactory, Factory),
    'CoCreateInstance of the imaging factory');
  Raw := SHCreateMemStream(@FileBytes[0], FileSize);
  Stream := IStream(Raw);       // the variable takes over the reference the creator returned
  if Raw <> nil then
    Stream._Release;
  Check(Stream <> nil, 'SHCreateMemStream');
  TestStream(Stream);
  TestDecode(Stream);
  TestGarbage;
  Section('release');
  CheckEqI(2, Stream._AddRef, 'only the program still holds the stream');
  CheckEqI(1, Stream._Release, 'one reference is left');
  Stream := nil;
  Factory := nil;
  CoUninitialize;
  Done;
end.
