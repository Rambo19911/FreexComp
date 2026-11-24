// covers: RTL-1006, RTL-1007, RTL-1008
// phase: 8
program AX_Layouts;
{ Unit ActiveX (com-support.md section 2): the x64 layouts of the variant, dispatch-parameter and
  exception-information records, the constants, an OLE string round trip, and one call of every import of
  oleaut32, ole32 and shlwapi the unit declares. }
uses Windows, ActiveX, TestKit;

function Offset(Base, Field: Pointer): Integer;
begin
  Result := Integer(NativeUInt(Field) - NativeUInt(Base));
end;

procedure TestLayouts;
var
  V: TVariantArg;
  P: TDispParams;
  X: TExcepInfo;
begin
  Section('record layouts (RTL-1006)');
  CheckEqI(24, SizeOf(TVariantArg), 'SizeOf(TVariantArg)');
  CheckEqI(0, Offset(@V, @V.vt), 'vt');
  CheckEqI(2, Offset(@V, @V.wReserved1), 'wReserved1');
  CheckEqI(6, Offset(@V, @V.wReserved3), 'wReserved3');
  CheckEqI(8, Offset(@V, @V.lVal), 'lVal');
  CheckEqI(8, Offset(@V, @V.llVal), 'llVal');
  CheckEqI(8, Offset(@V, @V.dblVal), 'dblVal');
  CheckEqI(8, Offset(@V, @V.boolVal), 'boolVal');
  CheckEqI(8, Offset(@V, @V.bstrVal), 'bstrVal');
  CheckEqI(8, Offset(@V, @V.byRef), 'byRef');
  CheckEqI(8, Offset(@V, @V.cyVal), 'cyVal');
  CheckEqI(8, Offset(@V, @V.scode), 'scode');
  CheckEqI(SizeOf(TVariantArg), SizeOf(VARIANTARG), 'VARIANTARG');

  CheckEqI(24, SizeOf(TDispParams), 'SizeOf(TDispParams)');
  CheckEqI(8, Offset(@P, @P.rgdispidNamedArgs), 'rgdispidNamedArgs');
  CheckEqI(16, Offset(@P, @P.cArgs), 'cArgs');
  CheckEqI(20, Offset(@P, @P.cNamedArgs), 'cNamedArgs');

  CheckEqI(64, SizeOf(TExcepInfo), 'SizeOf(TExcepInfo)');
  CheckEqI(2, Offset(@X, @X.wReserved), 'wReserved');
  CheckEqI(8, Offset(@X, @X.bstrSource), 'bstrSource');
  CheckEqI(16, Offset(@X, @X.bstrDescription), 'bstrDescription');
  CheckEqI(24, Offset(@X, @X.bstrHelpFile), 'bstrHelpFile');
  CheckEqI(32, Offset(@X, @X.dwHelpContext), 'dwHelpContext');
  CheckEqI(40, Offset(@X, @X.pvReserved), 'pvReserved');
  CheckEqI(48, Offset(@X, @X.pfnDeferredFillIn), 'pfnDeferredFillIn');
  CheckEqI(56, Offset(@X, @X.scode), 'scode');
end;

procedure TestConstants;
begin
  Section('constants (RTL-1006)');
  CheckEqI(8, VT_BSTR, 'VT_BSTR');
  CheckEqI(11, VT_BOOL, 'VT_BOOL');
  CheckEqI(21, VT_UI8, 'VT_UI8');
  CheckEqI($4000, VT_BYREF, 'VT_BYREF');
  CheckEqI(4, DISPATCH_PROPERTYPUT, 'DISPATCH_PROPERTYPUT');
  CheckEqI(-3, DISPID_PROPERTYPUT, 'DISPID_PROPERTYPUT');
  CheckEqI(Integer($80020009), DISP_E_EXCEPTION, 'DISP_E_EXCEPTION');
  CheckEqI(Integer($80020003), DISP_E_MEMBERNOTFOUND, 'DISP_E_MEMBERNOTFOUND');
  CheckEqI(Integer($8002000E), DISP_E_BADPARAMCOUNT, 'DISP_E_BADPARAMCOUNT');
  Check(Succeeded(S_OK) and Succeeded(S_FALSE) and not Succeeded(E_FAIL), 'Succeeded');
  Check(Failed(E_FAIL) and not Failed(S_FALSE), 'Failed');
end;

procedure TestOleStrings;
var
  S, T: string;
  B: TBStr;
begin
  Section('OLE strings (RTL-1007)');
  S := 'ab' + #0 + 'cd';
  B := SysAllocStringLen(PWideChar(S), Length(S));
  Check(B <> nil, 'SysAllocStringLen');
  CheckEqI(5, SysStringLen(B), 'SysStringLen counts the embedded #0');
  SetString(T, B, SysStringLen(B));
  Check(T = S, 'the text comes back unchanged');
  SysFreeString(B);
  CheckEqI(0, SysStringLen(nil), 'the length of a nil OLE string');
  SysFreeString(nil);
  B := SysAllocStringLen(nil, 3);
  CheckEqI(3, SysStringLen(B), 'an uninitialised string of a given length');
  SysFreeString(B);
end;

procedure TestVariants;
var
  V, R: TVariantArg;
  T: string;
begin
  Section('variant functions (RTL-1007)');
  V.vt := $FFFF;
  VariantInit(V);
  CheckEqI(VT_EMPTY, V.vt, 'VariantInit');
  VariantInit(R);
  V.vt := VT_I4;
  V.lVal := 42;
  CheckEqI(S_OK, VariantChangeType(R, V, 0, VT_BSTR), 'VariantChangeType to an OLE string');
  CheckEqI(VT_BSTR, R.vt, 'the type of the result');
  SetString(T, R.bstrVal, SysStringLen(R.bstrVal));
  CheckEqS('42', T, 'the text of the result');
  CheckEqI(S_OK, VariantClear(R), 'VariantClear');
  CheckEqI(VT_EMPTY, R.vt, 'a cleared variant is empty');
  V.vt := VT_BOOL;
  V.boolVal := -1;
  CheckEqI(S_OK, VariantChangeType(R, V, 0, VT_I4), 'VariantChangeType of a Boolean');
  CheckEqI(-1, R.lVal, 'true is -1');
  CheckEqI(S_OK, VariantClear(R), 'VariantClear of a number');
end;

procedure TestErrorInfo;
const
  Marker: TGUID = '{0F1E2D3C-4B5A-4978-8695-A4B3C2D1E0F9}';
var
  Creator: ICreateErrorInfo;
  Info, Got: IErrorInfo;
  B: TBStr;
  T: string;
  G: TGUID;
  Ctx: Cardinal;
begin
  Section('error information (RTL-1006, RTL-1007)');
  CheckEqI(S_OK, CreateErrorInfo(Creator), 'CreateErrorInfo');
  CheckEqI(S_OK, Creator.SetGUID(Marker), 'SetGUID');
  CheckEqI(S_OK, Creator.SetSource('the source'), 'SetSource');
  CheckEqI(S_OK, Creator.SetDescription('the description'), 'SetDescription');
  CheckEqI(S_OK, Creator.SetHelpFile('the help file'), 'SetHelpFile');
  CheckEqI(S_OK, Creator.SetHelpContext(1234), 'SetHelpContext');
  CheckEqI(S_OK, Creator.QueryInterface(IErrorInfo, Info), 'the object is an IErrorInfo');
  CheckEqI(S_OK, SetErrorInfo(0, Info), 'SetErrorInfo');
  CheckEqI(S_OK, GetErrorInfo(0, Got), 'GetErrorInfo');
  Check(Got <> nil, 'the error information of the thread');
  CheckEqI(S_OK, Got.GetGUID(G), 'GetGUID');
  Check(G = Marker, 'the GUID');
  CheckEqI(S_OK, Got.GetSource(B), 'GetSource');
  SetString(T, B, SysStringLen(B));
  SysFreeString(B);
  CheckEqS('the source', T, 'the source');
  CheckEqI(S_OK, Got.GetDescription(B), 'GetDescription');
  SetString(T, B, SysStringLen(B));
  SysFreeString(B);
  CheckEqS('the description', T, 'the description');
  CheckEqI(S_OK, Got.GetHelpFile(B), 'GetHelpFile');
  SetString(T, B, SysStringLen(B));
  SysFreeString(B);
  CheckEqS('the help file', T, 'the help file');
  CheckEqI(S_OK, Got.GetHelpContext(Ctx), 'GetHelpContext');
  CheckEqI(1234, Ctx, 'the help context');
  Got := nil;
  CheckEqI(S_FALSE, GetErrorInfo(0, Got), 'the information is consumed by the first GetErrorInfo');
  Check(Got = nil, 'no object the second time');
end;

procedure TestOle32;
const
  COINIT_APARTMENTTHREADED = 2;
  CLSCTX_INPROC_SERVER = 1;
var
  HR: HResult;
  ClassID, Zero: TGUID;
  Disp: IDispatch;
  Count: Integer;
  Stream: Pointer;
begin
  Section('ole32 and shlwapi imports (RTL-1007, RTL-1008)');
  HR := CoInitializeEx(nil, COINIT_APARTMENTTHREADED);
  Check(Succeeded(HR), 'CoInitializeEx');
  FillChar(Zero, SizeOf(Zero), 0);
  ClassID := Zero;
  CheckEqI(S_OK, CLSIDFromProgID('Scripting.Dictionary', ClassID), 'CLSIDFromProgID');
  Check(ClassID <> Zero, 'a class identifier');
  CheckEqI(S_OK, CoCreateInstance(ClassID, nil, CLSCTX_INPROC_SERVER, IDispatch, Disp), 'CoCreateInstance');
  Check(Disp <> nil, 'an object');
  CheckEqI(S_OK, Disp.GetTypeInfoCount(Count), 'IDispatch.GetTypeInfoCount');
  CheckEqI(1, Count, 'the object has type information');
  Disp := nil;
  CoTaskMemFree(nil);
  Stream := SHCreateMemStream(nil, 0);
  Check(Stream <> nil, 'SHCreateMemStream');
  if Stream <> nil then
    CheckEqI(0, IUnknown(Stream)._Release, 'the stream is released');
  CoUninitialize;
end;

begin
  TestLayouts;
  TestConstants;
  TestOleStrings;
  TestVariants;
  TestErrorInfo;
  TestOle32;
  Done;
end.
