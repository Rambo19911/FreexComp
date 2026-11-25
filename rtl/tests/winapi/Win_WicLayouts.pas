// covers: RTL-1031, RTL-1033, RTL-1035, RTL-1037
// phase: 8
program Win_WicLayouts;
{ The x64 layouts of the records the image codec bindings add (STATSTG, PROPVARIANT, WICRect, BLENDFUNCTION), the
  values of the new constants, and the proof of the property value record against the system: the imaging component
  fills one for a metadata query and PropVariantClear empties it. The expected numbers are those of the reference
  structure definitions. }
uses Windows, ActiveX, Wincodec, TestKit;

function Offset(Base, Field: Pointer): Integer;
begin
  Result := Integer(NativeUInt(Field) - NativeUInt(Base));
end;

procedure TestLayouts;
var
  S: TStatStg;
  V: TPropVariant;
  R: TWICRect;
  B: TBlendFunction;
begin
  Section('record layouts (RTL-1031, RTL-1033, RTL-1035)');
  CheckEqI(80, SizeOf(TStatStg), 'SizeOf(TStatStg)');
  CheckEqI(0, Offset(@S, @S.pwcsName), 'pwcsName');
  CheckEqI(8, Offset(@S, @S.&type), 'type');
  CheckEqI(16, Offset(@S, @S.cbSize), 'cbSize');
  CheckEqI(24, Offset(@S, @S.mtime), 'mtime');
  CheckEqI(32, Offset(@S, @S.ctime), 'ctime');
  CheckEqI(40, Offset(@S, @S.atime), 'atime');
  CheckEqI(48, Offset(@S, @S.grfMode), 'grfMode');
  CheckEqI(52, Offset(@S, @S.grfLocksSupported), 'grfLocksSupported');
  CheckEqI(56, Offset(@S, @S.clsid), 'clsid');
  CheckEqI(72, Offset(@S, @S.grfStateBits), 'grfStateBits');
  CheckEqI(76, Offset(@S, @S.reserved), 'reserved');
  CheckEqI(SizeOf(TStatStg), SizeOf(STATSTG), 'STATSTG');

  CheckEqI(24, SizeOf(TPropVariant), 'SizeOf(TPropVariant)');
  CheckEqI(0, Offset(@V, @V.vt), 'vt');
  CheckEqI(2, Offset(@V, @V.wReserved1), 'wReserved1');
  CheckEqI(4, Offset(@V, @V.wReserved2), 'wReserved2');
  CheckEqI(6, Offset(@V, @V.wReserved3), 'wReserved3');
  CheckEqI(8, Offset(@V, @V.uiVal), 'uiVal');
  CheckEqI(8, Offset(@V, @V.ulVal), 'ulVal');
  CheckEqI(8, Offset(@V, @V.pwszVal), 'pwszVal');
  CheckEqI(SizeOf(TPropVariant), SizeOf(PROPVARIANT), 'PROPVARIANT');

  CheckEqI(16, SizeOf(TWICRect), 'SizeOf(TWICRect)');
  CheckEqI(0, Offset(@R, @R.X), 'X');
  CheckEqI(4, Offset(@R, @R.Y), 'Y');
  CheckEqI(8, Offset(@R, @R.Width), 'Width');
  CheckEqI(12, Offset(@R, @R.Height), 'Height');
  CheckEqI(SizeOf(TWICRect), SizeOf(WICRect), 'WICRect');

  CheckEqI(4, SizeOf(TBlendFunction), 'SizeOf(TBlendFunction)');
  CheckEqI(0, Offset(@B, @B.BlendOp), 'BlendOp');
  CheckEqI(1, Offset(@B, @B.BlendFlags), 'BlendFlags');
  CheckEqI(2, Offset(@B, @B.SourceConstantAlpha), 'SourceConstantAlpha');
  CheckEqI(3, Offset(@B, @B.AlphaFormat), 'AlphaFormat');
  CheckEqI(SizeOf(TBlendFunction), SizeOf(BLENDFUNCTION), 'BLENDFUNCTION');
end;

procedure TestConstants;
begin
  Section('constants (RTL-1031, RTL-1033, RTL-1035)');
  CheckEqI(1, CLSCTX_INPROC_SERVER, 'CLSCTX_INPROC_SERVER');
  CheckEqI(2, COINIT_APARTMENTTHREADED, 'COINIT_APARTMENTTHREADED');
  CheckEqI(0, COINIT_MULTITHREADED, 'COINIT_MULTITHREADED');
  CheckEqI(Integer($80010106), RPC_E_CHANGED_MODE, 'RPC_E_CHANGED_MODE');
  CheckEqI(0, STREAM_SEEK_SET, 'STREAM_SEEK_SET');
  CheckEqI(1, STREAM_SEEK_CUR, 'STREAM_SEEK_CUR');
  CheckEqI(2, STREAM_SEEK_END, 'STREAM_SEEK_END');
  CheckEqI(1, STATFLAG_NONAME, 'STATFLAG_NONAME');
  CheckEqI(0, WICDecodeMetadataCacheOnDemand, 'WICDecodeMetadataCacheOnDemand');
  CheckEqI(1, WICDecodeMetadataCacheOnLoad, 'WICDecodeMetadataCacheOnLoad');
  CheckEqI(0, WICBitmapDitherTypeNone, 'WICBitmapDitherTypeNone');
  CheckEqI(0, WICBitmapPaletteTypeCustom, 'WICBitmapPaletteTypeCustom');
  CheckEqI(0, WICBitmapInterpolationModeNearestNeighbor, 'WICBitmapInterpolationModeNearestNeighbor');
  CheckEqI(1, WICBitmapInterpolationModeLinear, 'WICBitmapInterpolationModeLinear');
  CheckEqI(2, WICBitmapInterpolationModeCubic, 'WICBitmapInterpolationModeCubic');
  CheckEqI(3, WICBitmapInterpolationModeFant, 'WICBitmapInterpolationModeFant');
  CheckEqI(0, WICBitmapTransformRotate0, 'WICBitmapTransformRotate0');
  CheckEqI(1, WICBitmapTransformRotate90, 'WICBitmapTransformRotate90');
  CheckEqI(2, WICBitmapTransformRotate180, 'WICBitmapTransformRotate180');
  CheckEqI(3, WICBitmapTransformRotate270, 'WICBitmapTransformRotate270');
  CheckEqI(8, WICBitmapTransformFlipHorizontal, 'WICBitmapTransformFlipHorizontal');
  CheckEqI(16, WICBitmapTransformFlipVertical, 'WICBitmapTransformFlipVertical');
  CheckEqI(Integer($88982F40), WINCODEC_ERR_PROPERTYNOTFOUND, 'WINCODEC_ERR_PROPERTYNOTFOUND');
  CheckEqI(Integer($88982F50), WINCODEC_ERR_COMPONENTNOTFOUND, 'WINCODEC_ERR_COMPONENTNOTFOUND');
  CheckEqI(Integer($88982F07), WINCODEC_ERR_UNKNOWNIMAGEFORMAT, 'WINCODEC_ERR_UNKNOWNIMAGEFORMAT');
  CheckEqI(Integer($88982F61), WINCODEC_ERR_BADHEADER, 'WINCODEC_ERR_BADHEADER');
  CheckEqI(Integer($88982F60), WINCODEC_ERR_BADIMAGE, 'WINCODEC_ERR_BADIMAGE');
  CheckEqI(Integer($88982F72), WINCODEC_ERR_STREAMREAD, 'WINCODEC_ERR_STREAMREAD');
  CheckEqI(0, AC_SRC_OVER, 'AC_SRC_OVER');
  CheckEqI(1, AC_SRC_ALPHA, 'AC_SRC_ALPHA');
end;

{ A property value the system fills: the image descriptor width of a one-pixel image in the graphics interchange
  format, built here in memory (header, logical screen descriptor, a two-colour table, image descriptor, one data
  block, trailer). }
procedure TestPropVariantAgainstTheSystem;
const
  Image: array[0..34] of Byte = (
    $47, $49, $46, $38, $39, $61, $01, $00, $01, $00, $80, $00, $00,
    $00, $00, $00, $FF, $FF, $FF,
    $2C, $00, $00, $00, $00, $01, $00, $01, $00, $00,
    $02, $02, $44, $01, $00, $3B);
var
  Factory: IWICImagingFactory;
  Stream: IStream;
  Raw: Pointer;
  Decoder: IWICBitmapDecoder;
  Frame: IWICBitmapFrameDecode;
  Reader: IWICMetadataQueryReader;
  Format: TGUID;
  V: TPropVariant;
begin
  Section('a property value filled by the system (RTL-1037)');
  Check(Succeeded(CoInitializeEx(nil, COINIT_APARTMENTTHREADED)), 'CoInitializeEx');
  CheckEqI(RPC_E_CHANGED_MODE, CoInitializeEx(nil, COINIT_MULTITHREADED), 'the other threading model is refused');
  CheckEqI(S_OK, CoCreateInstance(CLSID_WICImagingFactory, nil, CLSCTX_INPROC_SERVER, IWICImagingFactory, Factory),
    'the imaging factory');
  Raw := SHCreateMemStream(@Image[0], SizeOf(Image));
  Stream := IStream(Raw);       // the variable takes over the reference the creator returned
  if Raw <> nil then
    Stream._Release;
  Check(Stream <> nil, 'a memory stream');
  CheckEqI(S_OK, Factory.CreateDecoderFromStream(Stream, nil, WICDecodeMetadataCacheOnDemand, Decoder), 'a decoder');
  CheckEqI(S_OK, Decoder.GetContainerFormat(Format), 'GetContainerFormat');
  Check(Format = GUID_ContainerFormatGif, 'the container format');
  CheckEqI(S_OK, Decoder.GetFrame(0, Frame), 'GetFrame');
  CheckEqI(S_OK, Frame.GetMetadataQueryReader(Reader), 'GetMetadataQueryReader');
  FillChar(V, SizeOf(V), $FF);
  V.vt := VT_EMPTY;
  CheckEqI(S_OK, Reader.GetMetadataByName('/imgdesc/Width', V), 'GetMetadataByName');
  CheckEqI(VT_UI2, V.vt, 'the value is a 16-bit unsigned number');
  CheckEqI(1, V.uiVal, 'the width');
  CheckEqI(S_OK, PropVariantClear(V), 'PropVariantClear');
  CheckEqI(VT_EMPTY, V.vt, 'a cleared value is empty');
  CheckEqI(WINCODEC_ERR_PROPERTYNOTFOUND, Reader.GetMetadataByName('/grctlext/Delay', V),
    'a block the image does not have');
  Reader := nil;
  Frame := nil;
  Decoder := nil;
  Stream := nil;
  Factory := nil;
  CoUninitialize;
end;

begin
  TestLayouts;
  TestConstants;
  TestPropVariantAgainstTheSystem;
  Done;
end.
