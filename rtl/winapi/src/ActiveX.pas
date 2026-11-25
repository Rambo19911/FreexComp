// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ActiveX - declarations of the OLE automation layer: OLE strings, the variant, dispatch-parameter and
  exception-information records, type and dispatch constants, result codes, the error-information interfaces and
  the oleaut32 / ole32 / shlwapi imports the RTL uses (rtl/com-support.md section 2; task R-230). Uses Windows.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the reference pages of VARIANT,
  DISPPARAMS and EXCEPINFO (oaidl.h), the VARENUM enumeration (wtypes.h), IDispatch::Invoke, IErrorInfo,
  ICreateErrorInfo and ISupportErrorInfo (oaidl.h), SysAllocStringLen, SysFreeString, SysStringLen, VariantInit,
  VariantClear, VariantChangeType, SetErrorInfo, GetErrorInfo and CreateErrorInfo (oleauto.h), CoInitializeEx,
  CoUninitialize, CoCreateInstance, CLSIDFromProgID and CoTaskMemFree (combaseapi.h), SHCreateMemStream (shlwapi.h)
  and the common result-code pages (winerror.h). Declarations only: the unit has no initialization code. }
unit ActiveX;

interface

uses Windows;

const
  oleaut32 = 'oleaut32.dll';
  ole32 = 'ole32.dll';
  shlwapi = 'shlwapi.dll';

type
  { an OLE string: a length-prefixed wide string owned by the oleaut32 allocator (SysAllocStringLen / SysFreeString) }
  TBStr = PWideChar;
  PBStr = ^TBStr;

  { VARIANT / VARIANTARG, 24 bytes: the type number, three reserved words, then the value at offset 8 }
  PVariantArg = ^TVariantArg;
  TVariantArg = record
    vt: Word;
    wReserved1, wReserved2, wReserved3: Word;
    case Integer of
      0: (lVal: Integer);
      1: (llVal: Int64);
      2: (dblVal: Double);
      3: (fltVal: Single);
      4: (boolVal: SmallInt);
      5: (bstrVal: TBStr);
      6: (unkVal: Pointer);
      7: (byRef: Pointer);
      8: (cyVal: Currency);
      9: (date: TDateTime);
      10: (scode: HResult);
      11: (pad: array[0..1] of Pointer);
  end;
  VARIANTARG = TVariantArg;

  { DISPPARAMS, 24 bytes: rgvarg holds the arguments in reverse order }
  PDispParams = ^TDispParams;
  TDispParams = record
    rgvarg: PVariantArg;
    rgdispidNamedArgs: PInteger;
    cArgs, cNamedArgs: Cardinal;
  end;
  DISPPARAMS = TDispParams;

  { EXCEPINFO, 64 bytes: filled by IDispatch.Invoke when it returns DISP_E_EXCEPTION; pfnDeferredFillIn, when
    assigned, is a function (stdcall, one parameter: the record) that fills the strings on demand }
  PExcepInfo = ^TExcepInfo;
  TExcepInfo = record
    wCode, wReserved: Word;
    bstrSource, bstrDescription, bstrHelpFile: TBStr;
    dwHelpContext: Cardinal;
    pvReserved: Pointer;
    pfnDeferredFillIn: Pointer;
    scode: HResult;
  end;
  EXCEPINFO = TExcepInfo;

const
  { VARENUM: variant type numbers }
  VT_EMPTY = 0;
  VT_I2 = 2;
  VT_I4 = 3;
  VT_R4 = 4;
  VT_R8 = 5;
  VT_CY = 6;
  VT_DATE = 7;
  VT_BSTR = 8;
  VT_DISPATCH = 9;
  VT_ERROR = 10;
  VT_BOOL = 11;
  VT_VARIANT = 12;
  VT_UNKNOWN = 13;
  VT_I1 = 16;
  VT_UI1 = 17;
  VT_UI2 = 18;
  VT_UI4 = 19;
  VT_I8 = 20;
  VT_UI8 = 21;
  VT_BYREF = $4000;

  { IDispatch.Invoke: wFlags and the reserved member identifiers }
  DISPATCH_METHOD = 1;
  DISPATCH_PROPERTYGET = 2;
  DISPATCH_PROPERTYPUT = 4;
  DISPATCH_PROPERTYPUTREF = 8;
  DISPID_VALUE = 0;
  DISPID_UNKNOWN = -1;
  DISPID_PROPERTYPUT = -3;

  { result codes of the dispatch layer (the general ones are in System and Windows) }
  DISP_E_UNKNOWNINTERFACE = HResult($80020001);
  DISP_E_MEMBERNOTFOUND = HResult($80020003);
  DISP_E_PARAMNOTFOUND = HResult($80020004);
  DISP_E_TYPEMISMATCH = HResult($80020005);
  DISP_E_UNKNOWNNAME = HResult($80020006);
  DISP_E_EXCEPTION = HResult($80020009);
  DISP_E_BADPARAMCOUNT = HResult($8002000E);

type
  { the error information of the calling thread (SetErrorInfo / GetErrorInfo); strings are OLE strings the caller
    of a Get method frees }
  IErrorInfo = interface(IUnknown)
    ['{1CF2B120-547D-101B-8E65-08002B2BD119}']
    function GetGUID(out guid: TGUID): HResult; stdcall;
    function GetSource(out bstrSource: TBStr): HResult; stdcall;
    function GetDescription(out bstrDescription: TBStr): HResult; stdcall;
    function GetHelpFile(out bstrHelpFile: TBStr): HResult; stdcall;
    function GetHelpContext(out dwHelpContext: Cardinal): HResult; stdcall;
  end;

  { fills an error-information object made by CreateErrorInfo; the object copies the strings }
  ICreateErrorInfo = interface(IUnknown)
    ['{22F03340-547D-101B-8E65-08002B2BD119}']
    function SetGUID(const guid: TGUID): HResult; stdcall;
    function SetSource(szSource: TBStr): HResult; stdcall;
    function SetDescription(szDescription: TBStr): HResult; stdcall;
    function SetHelpFile(szHelpFile: TBStr): HResult; stdcall;
    function SetHelpContext(dwHelpContext: Cardinal): HResult; stdcall;
  end;

  ISupportErrorInfo = interface(IUnknown)
    ['{DF0B3D60-548F-101B-8E65-08002B2BD119}']
    function InterfaceSupportsErrorInfo(const riid: TGUID): HResult; stdcall;
  end;

function Succeeded(Res: HResult): Boolean; inline;
function Failed(Res: HResult): Boolean; inline;

{ oleaut32 }
function SysAllocStringLen(strIn: PWideChar; ui: UINT): TBStr; stdcall; external oleaut32 name 'SysAllocStringLen';
procedure SysFreeString(bstrString: TBStr); stdcall; external oleaut32 name 'SysFreeString';
function SysStringLen(pbstr: TBStr): UINT; stdcall; external oleaut32 name 'SysStringLen';
procedure VariantInit(var pvarg: TVariantArg); stdcall; external oleaut32 name 'VariantInit';
function VariantClear(var pvarg: TVariantArg): HResult; stdcall; external oleaut32 name 'VariantClear';
function VariantChangeType(var pvargDest: TVariantArg; const pvarSrc: TVariantArg; wFlags: Word; vt: Word): HResult;
  stdcall; external oleaut32 name 'VariantChangeType';
function SetErrorInfo(dwReserved: ULONG; const perrinfo: IErrorInfo): HResult; stdcall;
  external oleaut32 name 'SetErrorInfo';
function GetErrorInfo(dwReserved: ULONG; out pperrinfo: IErrorInfo): HResult; stdcall;
  external oleaut32 name 'GetErrorInfo';
function CreateErrorInfo(out pperrinfo: ICreateErrorInfo): HResult; stdcall; external oleaut32 name 'CreateErrorInfo';

{ ole32 }
function CoInitializeEx(pvReserved: Pointer; dwCoInit: DWORD): HResult; stdcall; external ole32 name 'CoInitializeEx';
procedure CoUninitialize; stdcall; external ole32 name 'CoUninitialize';
function CoCreateInstance(const rclsid: TGUID; pUnkOuter: Pointer; dwClsContext: DWORD; const riid: TGUID;
  out ppv): HResult; stdcall; external ole32 name 'CoCreateInstance';
function CLSIDFromProgID(lpszProgID: PWideChar; out lpclsid: TGUID): HResult; stdcall;
  external ole32 name 'CLSIDFromProgID';
procedure CoTaskMemFree(pv: Pointer); stdcall; external ole32 name 'CoTaskMemFree';

{ shlwapi: a memory stream (an IStream reference the caller releases; nil when it cannot be created) }
function SHCreateMemStream(pInit: PByte; cbInit: UINT): Pointer; stdcall; external shlwapi name 'SHCreateMemStream';

implementation

function Succeeded(Res: HResult): Boolean;
begin
  Result := Res >= 0;
end;

function Failed(Res: HResult): Boolean;
begin
  Result := Res < 0;
end;

end.
