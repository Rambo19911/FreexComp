// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Wincodec - declarations of the imaging component of the operating system (windowscodecs.dll, reached through
  CoCreateInstance): the factory, decoder, frame, format converter, scaler, flip-rotator and metadata query reader
  interfaces, the class identifier, container and pixel format identifiers, enumeration values, result codes and
  the rectangle record (rtl/winapi.md, rules RTL-1030 to RTL-1037; task R-237). Uses Windows, ActiveX.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the "Windows Imaging Component"
  reference (wic/-wic-lh) - the interface pages IWICBitmapSource, IWICBitmapFrameDecode, IWICBitmapDecoder,
  IWICFormatConverter, IWICBitmapScaler, IWICBitmapFlipRotator, IWICMetadataQueryReader and IWICImagingFactory with
  their method pages, the enumeration pages WICDecodeOptions, WICBitmapDitherType, WICBitmapPaletteType,
  WICBitmapInterpolationMode and WICBitmapTransformOptions, the structure page WICRect, "WIC GUIDs and CLSIDs"
  (container formats), "Native pixel formats overview" (pixel format names) and "Codec Error Codes".
  Those pages list the methods of an interface alphabetically and give no value for the pixel format, interface and
  factory class identifiers. The order of the methods (which is the binary contract), the interface identifiers,
  the two pixel format identifiers and the class identifier are the ones of the platform header wincodec.h as
  ported, with method slot numbers, by the TerraFX.Interop.Windows project (MIT licence; files um/wincodec/I*.cs,
  CLSID.cs and GUID.cs) - facts only, no text. CLSID_WICImagingFactory is the first version of the factory class
  (the header's CLSID_WICImagingFactory1), which every supported system registers and which implements
  IWICImagingFactory.
  Every interface is declared from its first method on; IWICImagingFactory stops after CreateBitmapFlipRotator (the
  program only calls these objects, it never implements them). A parameter whose interface type is not declared
  here is a Pointer, with the reference type named in a comment. Enumeration parameters are Integer.
  The unit declares no function and has no initialization code: a program that uses it imports nothing more
  (RTL-1036). }
unit Wincodec;

interface

uses Windows, ActiveX;

const
  { the imaging factory class (CoCreateInstance) }
  CLSID_WICImagingFactory: TGUID = '{CACAF262-9370-4615-A13B-9F5539DA4C0A}';

  { container formats (IWICBitmapDecoder.GetContainerFormat) }
  GUID_ContainerFormatBmp: TGUID = '{0AF1D87E-FCFE-4188-BDEB-A7906471CBE3}';
  GUID_ContainerFormatPng: TGUID = '{1B7CFAF4-713F-473C-BBCD-6137425FAEAF}';
  GUID_ContainerFormatJpeg: TGUID = '{19E4A5AA-5662-4FC5-A0C0-1758028E1057}';
  GUID_ContainerFormatGif: TGUID = '{1F8A5601-7D4D-4CBD-9C82-1BC8D4EEB9A5}';
  GUID_ContainerFormatTiff: TGUID = '{163BCC30-E2E9-4F0B-961D-A3E9FDB788A3}';

  { pixel formats: bytes in memory are blue, green, red (and alpha; P = colour channels premultiplied by alpha) }
  GUID_WICPixelFormat24bppBGR: TGUID = '{6FDDC324-4E03-4BFE-B185-3D77768DC90C}';
  GUID_WICPixelFormat32bppPBGRA: TGUID = '{6FDDC324-4E03-4BFE-B185-3D77768DC910}';

  { WICDecodeOptions }
  WICDecodeMetadataCacheOnDemand = 0;
  WICDecodeMetadataCacheOnLoad = 1;

  { WICBitmapDitherType }
  WICBitmapDitherTypeNone = 0;

  { WICBitmapPaletteType }
  WICBitmapPaletteTypeCustom = 0;

  { WICBitmapInterpolationMode }
  WICBitmapInterpolationModeNearestNeighbor = 0;
  WICBitmapInterpolationModeLinear = 1;
  WICBitmapInterpolationModeCubic = 2;
  WICBitmapInterpolationModeFant = 3;

  { WICBitmapTransformOptions: one rotation (clockwise), optionally combined with flips }
  WICBitmapTransformRotate0 = 0;
  WICBitmapTransformRotate90 = 1;
  WICBitmapTransformRotate180 = 2;
  WICBitmapTransformRotate270 = 3;
  WICBitmapTransformFlipHorizontal = 8;
  WICBitmapTransformFlipVertical = 16;

  { result codes of the imaging component }
  WINCODEC_ERR_UNKNOWNIMAGEFORMAT = HResult($88982F07);
  WINCODEC_ERR_PROPERTYNOTFOUND = HResult($88982F40);
  WINCODEC_ERR_COMPONENTNOTFOUND = HResult($88982F50);
  WINCODEC_ERR_BADIMAGE = HResult($88982F60);
  WINCODEC_ERR_BADHEADER = HResult($88982F61);
  WINCODEC_ERR_STREAMREAD = HResult($88982F72);

type
  { WICRect, 16 bytes }
  PWICRect = ^TWICRect;
  TWICRect = record
    X: Integer;
    Y: Integer;
    Width: Integer;
    Height: Integer;
  end;
  WICRect = TWICRect;

  { a source of pixels; CopyPixels with a nil rectangle copies the whole image }
  IWICBitmapSource = interface(IUnknown)
    ['{00000120-A8F2-4877-BA0A-FD2B6645FB94}']
    function GetSize(out puiWidth, puiHeight: UINT): HResult; stdcall;
    function GetPixelFormat(out pPixelFormat: TGUID): HResult; stdcall;
    function GetResolution(out pDpiX, pDpiY: Double): HResult; stdcall;
    function CopyPalette(pIPalette: Pointer { IWICPalette }): HResult; stdcall;
    function CopyPixels(prc: PWICRect; cbStride, cbBufferSize: UINT; pbBuffer: PByte): HResult; stdcall;
  end;

  { reads metadata by a query name such as '/imgdesc/Width'; the value is released with PropVariantClear }
  IWICMetadataQueryReader = interface(IUnknown)
    ['{30989668-E1C9-4597-B395-458EEDB808DF}']
    function GetContainerFormat(out pguidContainerFormat: TGUID): HResult; stdcall;
    function GetLocation(cchMaxLength: UINT; wzNamespace: PWideChar; out pcchActualLength: UINT): HResult; stdcall;
    function GetMetadataByName(wzName: PWideChar; var pvarValue: TPropVariant): HResult; stdcall;
    function GetEnumerator(out ppIEnumString: Pointer { IEnumString }): HResult; stdcall;
  end;

  { one frame of an image }
  IWICBitmapFrameDecode = interface(IWICBitmapSource)
    ['{3B16811B-6A43-4EC9-A813-3D930C13B940}']
    function GetMetadataQueryReader(out ppIMetadataQueryReader: IWICMetadataQueryReader): HResult; stdcall;
    function GetColorContexts(cCount: UINT; ppIColorContexts: Pointer { ^IWICColorContext };
      out pcActualCount: UINT): HResult; stdcall;
    function GetThumbnail(out ppIThumbnail: IWICBitmapSource): HResult; stdcall;
  end;

  { the decoder of one image stream }
  IWICBitmapDecoder = interface(IUnknown)
    ['{9EDDE9E7-8DEE-47EA-99DF-E6FAF2ED44BF}']
    function QueryCapability(const pIStream: IStream; out pdwCapability: DWORD): HResult; stdcall;
    function Initialize(const pIStream: IStream; cacheOptions: Integer): HResult; stdcall;
    function GetContainerFormat(out pguidContainerFormat: TGUID): HResult; stdcall;
    function GetDecoderInfo(out ppIDecoderInfo: Pointer { IWICBitmapDecoderInfo }): HResult; stdcall;
    function CopyPalette(pIPalette: Pointer { IWICPalette }): HResult; stdcall;
    function GetMetadataQueryReader(out ppIMetadataQueryReader: IWICMetadataQueryReader): HResult; stdcall;
    function GetPreview(out ppIBitmapSource: IWICBitmapSource): HResult; stdcall;
    function GetColorContexts(cCount: UINT; ppIColorContexts: Pointer { ^IWICColorContext };
      out pcActualCount: UINT): HResult; stdcall;
    function GetThumbnail(out ppIThumbnail: IWICBitmapSource): HResult; stdcall;
    function GetFrameCount(out pCount: UINT): HResult; stdcall;
    function GetFrame(index: UINT; out ppIBitmapFrame: IWICBitmapFrameDecode): HResult; stdcall;
  end;

  { converts a source to another pixel format }
  IWICFormatConverter = interface(IWICBitmapSource)
    ['{00000301-A8F2-4877-BA0A-FD2B6645FB94}']
    function Initialize(const pISource: IWICBitmapSource; const dstFormat: TGUID; dither: Integer;
      pIPalette: Pointer { IWICPalette }; alphaThresholdPercent: Double; paletteTranslate: Integer): HResult; stdcall;
    function CanConvert(const srcPixelFormat, dstPixelFormat: TGUID; out pfCanConvert: BOOL): HResult; stdcall;
  end;

  { resamples a source to another size }
  IWICBitmapScaler = interface(IWICBitmapSource)
    ['{00000302-A8F2-4877-BA0A-FD2B6645FB94}']
    function Initialize(const pISource: IWICBitmapSource; uiWidth, uiHeight: UINT; mode: Integer): HResult; stdcall;
  end;

  { rotates and flips a source }
  IWICBitmapFlipRotator = interface(IWICBitmapSource)
    ['{5009834F-2D6A-41CE-9E1B-17C5AFF7A782}']
    function Initialize(const pISource: IWICBitmapSource; options: Integer): HResult; stdcall;
  end;

  { the factory: declared up to CreateBitmapFlipRotator; pguidVendor may be nil }
  IWICImagingFactory = interface(IUnknown)
    ['{EC5EC8A9-C395-4314-9C77-54D7A935FF70}']
    function CreateDecoderFromFilename(wzFilename: PWideChar; pguidVendor: PGUID; dwDesiredAccess: DWORD;
      metadataOptions: Integer; out ppIDecoder: IWICBitmapDecoder): HResult; stdcall;
    function CreateDecoderFromStream(const pIStream: IStream; pguidVendor: PGUID; metadataOptions: Integer;
      out ppIDecoder: IWICBitmapDecoder): HResult; stdcall;
    function CreateDecoderFromFileHandle(hFile: ULONG_PTR; pguidVendor: PGUID; metadataOptions: Integer;
      out ppIDecoder: IWICBitmapDecoder): HResult; stdcall;
    function CreateComponentInfo(const clsidComponent: TGUID;
      out ppIInfo: Pointer { IWICComponentInfo }): HResult; stdcall;
    function CreateDecoder(const guidContainerFormat: TGUID; pguidVendor: PGUID;
      out ppIDecoder: IWICBitmapDecoder): HResult; stdcall;
    function CreateEncoder(const guidContainerFormat: TGUID; pguidVendor: PGUID;
      out ppIEncoder: Pointer { IWICBitmapEncoder }): HResult; stdcall;
    function CreatePalette(out ppIPalette: Pointer { IWICPalette }): HResult; stdcall;
    function CreateFormatConverter(out ppIFormatConverter: IWICFormatConverter): HResult; stdcall;
    function CreateBitmapScaler(out ppIBitmapScaler: IWICBitmapScaler): HResult; stdcall;
    function CreateBitmapClipper(out ppIBitmapClipper: Pointer { IWICBitmapClipper }): HResult; stdcall;
    function CreateBitmapFlipRotator(out ppIBitmapFlipRotator: IWICBitmapFlipRotator): HResult; stdcall;
  end;

implementation

end.
