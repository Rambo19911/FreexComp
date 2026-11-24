{ DispKit - the hand-written IDispatch server of Com_DispCallTypes (rtl/com-support.md RTL-1014): Invoke writes
  down the argument records it receives, changes what by-reference records point to and answers with a result. }
unit DispKit;

interface

uses Windows, ActiveX;

type
  PShortInt = ^ShortInt;
  PSmallInt = ^SmallInt;

  TServer = class(TInterfacedObject, IDispatch)
  public
    Log: string;          { what the last Invoke received }
    Other: IDispatch;     { a second object, for the reference arguments }
    Foreign: Boolean;     { results are given in another variant type and must be converted }
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
      DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
    function RefText(P: Pointer): string;
    function ArgText(const V: TVariantArg): string;
    procedure Change(const V: TVariantArg);
    procedure Answer(DispID: Integer; var R: TVariantArg);
  end;

function NewBStr(const S: string): TBStr;

implementation

uses TestKit;

function NewBStr(const S: string): TBStr;
begin
  Result := SysAllocStringLen(PWideChar(S), Length(S));
end;

{ the text of an OLE string, its length in front; #0 is shown as ~ }
function BStrText(B: TBStr): string;
var
  S: string;
  I: Integer;
begin
  if B = nil then
    Exit('nil');
  SetString(S, B, SysStringLen(B));
  for I := 1 to Length(S) do
    if S[I] = #0 then
      S[I] := '~';
  Result := IntStr(Length(S)) + ':' + S;
end;

function TServer.GetTypeInfoCount(out Count: Integer): HResult;
begin
  Count := 0;
  Result := S_OK;
end;

function TServer.GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult;
begin
  Result := E_NOTIMPL;
end;

function TServer.GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer;
  DispIDs: Pointer): HResult;
begin
  Result := E_NOTIMPL;
end;

function TServer.RefText(P: Pointer): string;
begin
  if P = nil then
    Result := 'nil'
  else if P = Pointer(Self as IDispatch) then
    Result := 'self'
  else if P = Pointer(Other) then
    Result := 'other'
  else
    Result := '?';
end;

{ "<type number>=<value>", with & in front when the record is by reference; quarters for the real types }
function TServer.ArgText(const V: TVariantArg): string;
var
  P: Pointer;
begin
  P := @V.llVal;
  Result := '';
  if V.vt and VT_BYREF <> 0 then
  begin
    P := V.byRef;
    Result := '&';
  end;
  Result := Result + IntStr(V.vt and $FFF) + '=';
  case V.vt and $FFF of
    VT_I1: Result := Result + IntStr(PShortInt(P)^);
    VT_I2, VT_BOOL: Result := Result + IntStr(PSmallInt(P)^);
    VT_I4: Result := Result + IntStr(PInteger(P)^);
    VT_I8, VT_CY: Result := Result + IntStr(PInt64(P)^);
    VT_UI1: Result := Result + IntStr(PByte(P)^);
    VT_UI2: Result := Result + IntStr(PWord(P)^);
    VT_UI4: Result := Result + IntStr(PCardinal(P)^);
    VT_UI8: Result := Result + HexStr64(PInt64(P)^);
    VT_R4: Result := Result + IntStr(Round(PSingle(P)^ * 4)) + '/4';
    VT_R8, VT_DATE: Result := Result + IntStr(Round(PDouble(P)^ * 4)) + '/4';
    VT_BSTR: Result := Result + BStrText(PPointer(P)^);
    VT_DISPATCH, VT_UNKNOWN: Result := Result + RefText(PPointer(P)^);
  else
    Result := Result + '?';
  end;
end;

{ a by-reference record: the server changes what it points to }
procedure TServer.Change(const V: TVariantArg);
var
  P: Pointer;
  S: string;
  B: TBStr;
begin
  P := V.byRef;
  case V.vt and $FFF of
    VT_I1: PShortInt(P)^ := PShortInt(P)^ + 1;
    VT_I2: PSmallInt(P)^ := PSmallInt(P)^ + 1;
    VT_I4: PInteger(P)^ := PInteger(P)^ + 1;
    VT_I8, VT_CY: PInt64(P)^ := PInt64(P)^ + 1;
    VT_UI1: PByte(P)^ := PByte(P)^ + 1;
    VT_UI2: PWord(P)^ := PWord(P)^ + 1;
    VT_UI4: PCardinal(P)^ := PCardinal(P)^ + 1;
    VT_UI8: PUInt64(P)^ := PUInt64(P)^ + 1;
    VT_R4: PSingle(P)^ := PSingle(P)^ * 2;
    VT_R8, VT_DATE: PDouble(P)^ := PDouble(P)^ * 2;
    VT_BOOL:
      if PSmallInt(P)^ = 0 then
        PSmallInt(P)^ := -1
      else
        PSmallInt(P)^ := 0;
    VT_BSTR:
      begin
        { the callee frees the string it was given and stores a new one }
        B := PPointer(P)^;
        SetString(S, B, SysStringLen(B));
        SysFreeString(B);
        PPointer(P)^ := NewBStr(S + '+new'#0'end');
      end;
    VT_DISPATCH:
      begin
        if PPointer(P)^ <> nil then
          IInterface(PPointer(P)^)._Release;
        Other._AddRef;
        PPointer(P)^ := Pointer(Other);
      end;
    VT_UNKNOWN:
      begin
        if PPointer(P)^ <> nil then
          IInterface(PPointer(P)^)._Release;
        _AddRef;
        PPointer(P)^ := Pointer(Self as IInterface);
      end;
  end;
end;

procedure TServer.Answer(DispID: Integer; var R: TVariantArg);
var
  T: Integer;
begin
  T := DispID mod 100;
  if Foreign then
  begin
    { another type than the caller declared: the helper converts }
    if DispID = 103 then
    begin
      R.vt := VT_BSTR;
      R.bstrVal := NewBStr('-45');
      Exit;
    end;
    case T of
      VT_BSTR:
        begin
          R.vt := VT_I4;
          R.lVal := 123;
        end;
      VT_BOOL:
        begin
          R.vt := VT_I4;
          R.lVal := 7;
        end;
      VT_DISPATCH:
        begin
          R.vt := VT_UNKNOWN;
          _AddRef;
          R.unkVal := Pointer(Self as IInterface);
        end;
      VT_UNKNOWN:
        begin
          R.vt := VT_DISPATCH;
          _AddRef;
          R.unkVal := Pointer(Self as IDispatch);
        end;
    else
      R.vt := VT_I2;
      R.boolVal := 2;
    end;
    Exit;
  end;
  R.vt := T;
  case T of
    VT_I1: R.lVal := -8;
    VT_I2: R.lVal := -300;
    VT_I4:
      if DispID > 200 then
        R.lVal := 2
      else
        R.lVal := -70000;
    VT_R4: R.fltVal := 1.5;
    VT_R8: R.dblVal := -2.25;
    VT_CY: R.llVal := 123400;
    VT_DATE: R.date := 36526.5;
    VT_BSTR: R.bstrVal := NewBStr('re'#0'sult');
    VT_DISPATCH:
      begin
        Other._AddRef;
        R.unkVal := Pointer(Other);
      end;
    VT_BOOL: R.boolVal := -1;
    VT_UNKNOWN:
      begin
        _AddRef;
        R.unkVal := Pointer(Self as IInterface);
      end;
    VT_UI1: R.lVal := 200;
    VT_UI2: R.lVal := 60000;
    VT_UI4: R.llVal := 4000000000;
    VT_I8: R.llVal := -5000000000;
    VT_UI8: R.llVal := -2;
  end;
end;

function TServer.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
var
  DP: PDispParams;
  V: PVariantArg;
  I: Cardinal;
  Zero: TGUID;
begin
  DP := @Params;
  Log := 'id=' + IntStr(DispID) + ' flags=' + IntStr(Flags) + ' args=' + IntStr(DP^.cArgs) + ' named=' +
    IntStr(DP^.cNamedArgs);
  FillChar(Zero, SizeOf(Zero), 0);
  if not (IID = Zero) then
    Log := Log + ' IID-NOT-ZERO';
  if LocaleID <> $0400 then
    Log := Log + ' LOCALE-' + IntStr(LocaleID);
  if (ExcepInfo = nil) or (ArgErr = nil) then
    Log := Log + ' NO-EXCEPINFO-OR-ARGERR';
  if VarResult = nil then
    Log := Log + ' noresult'
  else
    Log := Log + ' result' + IntStr(PVariantArg(VarResult)^.vt);
  V := DP^.rgvarg;
  if DP^.cArgs > 0 then
    for I := 0 to DP^.cArgs - 1 do
    begin
      Log := Log + ' [' + ArgText(V^) + ']';
      Inc(V);
    end;
  V := DP^.rgvarg;
  if (DispID >= 6) and (DispID <= 10) then
    for I := 0 to DP^.cArgs - 1 do
    begin
      Change(V^);
      Inc(V);
    end;
  if VarResult <> nil then
    Answer(DispID, PVariantArg(VarResult)^);
  Result := S_OK;
end;

end.
