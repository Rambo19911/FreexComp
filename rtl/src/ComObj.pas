// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ComObj - the behaviour of the COM support (rtl/com-support.md section 3; tasks R-230, R-231): the OLE exception
  classes, OleCheck / OleError, the error-information side of safecall methods (HandleSafeCallException), the handler
  this unit installs as System.SafeCallErrorProc and the dispatch call it installs as System.DispCallProc. Needs oleaut32 only; COM is not initialised here (RTL-1015). }
unit ComObj;

interface

uses Windows, ActiveX, SysUtils;

type
  EOleError = class(Exception);

  { a failed result code; an empty message is replaced by the system's text for the code }
  EOleSysError = class(EOleError)
  private
    FErrorCode: HResult;
  public
    constructor Create(const Message: string; ErrorCode: HResult; HelpContext: Integer);
    property ErrorCode: HResult read FErrorCode write FErrorCode;
  end;

  { a failure that came with error information (description, source, help file) }
  EOleException = class(EOleSysError)
  private
    FSource: string;
    FHelpFile: string;
  public
    constructor Create(const Message: string; ErrorCode: HResult; const Source, HelpFile: string;
      HelpContext: Integer);
    property Source: string read FSource write FSource;
    property HelpFile: string read FHelpFile write FHelpFile;
  end;

{ RTL-1011: OleError raises EOleSysError for ErrorCode; OleCheck does so for a negative Result only }
procedure OleError(ErrorCode: HResult);
procedure OleCheck(Result: HResult);

{ RTL-1012: for SafeCallException overrides. Publishes the exception as the thread's error information (interface
  ErrorIID, source ProgID, help file HelpFileName) and returns the code to report: the ErrorCode of an EOleSysError
  when negative, else E_UNEXPECTED. }
function HandleSafeCallException(ExceptObject: TObject; ExceptAddr: Pointer; const ErrorIID: TGUID;
  const ProgID, HelpFileName: string): HResult;

implementation

uses RTLConsts;

constructor EOleSysError.Create(const Message: string; ErrorCode: HResult; HelpContext: Integer);
var
  S: string;
begin
  S := Message;
  if S = '' then
    S := SysErrorMessage(Cardinal(ErrorCode));
  if S = '' then
    S := Format(SOleError, [ErrorCode]);
  inherited Create(S);
  FErrorCode := ErrorCode;
  Self.HelpContext := HelpContext;
end;

constructor EOleException.Create(const Message: string; ErrorCode: HResult; const Source, HelpFile: string;
  HelpContext: Integer);
begin
  inherited Create(Message, ErrorCode, HelpContext);
  FSource := Source;
  FHelpFile := HelpFile;
end;

procedure OleError(ErrorCode: HResult);
var
  NoMessage: string;
begin
  NoMessage := '';
  raise EOleSysError.Create(NoMessage, ErrorCode, 0);
end;

procedure OleCheck(Result: HResult);
begin
  if Result < 0 then
    OleError(Result);
end;

function HandleSafeCallException(ExceptObject: TObject; ExceptAddr: Pointer; const ErrorIID: TGUID;
  const ProgID, HelpFileName: string): HResult;
var
  E: Exception;
  Creator: ICreateErrorInfo;
  Info: IErrorInfo;
begin
  Result := E_UNEXPECTED;
  E := nil;
  if ExceptObject is Exception then
    E := Exception(ExceptObject);
  if (ExceptObject is EOleSysError) and (EOleSysError(ExceptObject).ErrorCode < 0) then
    Result := EOleSysError(ExceptObject).ErrorCode;
  { every step may fail; the code is returned all the same }
  if CreateErrorInfo(Creator) <> S_OK then
    Exit;
  Creator.SetGUID(ErrorIID);
  if ProgID <> '' then
    Creator.SetSource(PWideChar(ProgID));
  if HelpFileName <> '' then
    Creator.SetHelpFile(PWideChar(HelpFileName));
  if E <> nil then
  begin
    Creator.SetDescription(PWideChar(E.Message));
    Creator.SetHelpContext(Cardinal(E.HelpContext));
  end;
  if Creator.QueryInterface(IErrorInfo, Info) = S_OK then
    SetErrorInfo(0, Info);
end;

{ the text of an OLE string (by its length: embedded #0 are kept), which is then freed }
function TakeBStr(var B: TBStr): string;
begin
  SetString(Result, B, SysStringLen(B));
  SysFreeString(B);
  B := nil;
end;

{ RTL-1013: System.SafeCallErrorProc. A failed safecall call becomes EOleException when the callee left error
  information for this thread, else EOleSysError; raised at the address of the call. }
procedure SafeCallError(ErrorCode: HResult; ErrorAddr: Pointer);
var
  Info: IErrorInfo;
  Source, Description, HelpFile: TBStr;
  HelpContext: Cardinal;
  S, D, H, NoMessage: string;
begin
  if GetErrorInfo(0, Info) = S_OK then
  begin
    if Info.GetSource(Source) <> S_OK then
      Source := nil;
    if Info.GetDescription(Description) <> S_OK then
      Description := nil;
    if Info.GetHelpFile(HelpFile) <> S_OK then
      HelpFile := nil;
    if Info.GetHelpContext(HelpContext) <> S_OK then
      HelpContext := 0;
    S := TakeBStr(Source);
    D := TakeBStr(Description);
    H := TakeBStr(HelpFile);
    Info := nil;
    raise EOleException.Create(D, ErrorCode, S, H, Integer(HelpContext)) at ErrorAddr;
  end;
  NoMessage := '';
  raise EOleSysError.Create(NoMessage, ErrorCode, 0) at ErrorAddr;
end;

{ ---- RTL-1014: dispatch calls ---- }

type
  { the call descriptor the compiler passes (rtl-abi.md ABI-205) }
  PDispDesc = ^TDispDesc;
  TDispDesc = packed record
    DispId: Integer;
    Flags: Word;
    ResultType: Word;
    ArgCount: Word;
    ArgTypes: array[0..65534] of Word;
  end;
  { the argument slots, 8 bytes each, in declaration order }
  PDispSlots = ^TDispSlots;
  TDispSlots = array[0..65534] of UInt64;
  PSmallInt = ^SmallInt;
  TDeferredFillIn = function(Info: PExcepInfo): HResult; stdcall;

const
  dcString = $0100;       { descriptor type code: a string, marshalled as an OLE string }
  dcTypeMask = $01FF;
  dcByteBool = $0200;     { the Pascal variable is a 1-byte Boolean }
  dcByRef = $4000;

{ a new OLE string with the text of S (nil for the empty string) }
function NewBStr(const S: string): TBStr;
begin
  if S = '' then
    Result := nil
  else
    Result := SysAllocStringLen(PWideChar(S), Length(S));
end;

{ step 5: Res, already converted to the variant type of Code, is stored in the Pascal variable at Dest }
procedure StoreDispResult(Dest: Pointer; Code: Integer; const Res: TVariantArg);
begin
  case Code and dcTypeMask of
    dcString: SetString(PString(Dest)^, Res.bstrVal, SysStringLen(Res.bstrVal));
    VT_DISPATCH, VT_UNKNOWN: IInterface(Dest^) := IInterface(Res.unkVal);
    VT_BOOL:
      if Code and dcByteBool <> 0 then
        PBoolean(Dest)^ := Res.boolVal <> 0
      else
        PSmallInt(Dest)^ := Res.boolVal;
    VT_I1, VT_UI1: PByte(Dest)^ := Byte(Res.lVal and $FF);
    VT_I2, VT_UI2: PWord(Dest)^ := Word(Res.lVal and $FFFF);
    VT_I4, VT_UI4, VT_R4: PInteger(Dest)^ := Res.lVal;
  else
    { VT_R8, VT_CY, VT_DATE, VT_I8, VT_UI8 }
    PInt64(Dest)^ := Res.llVal;
  end;
end;

{ System.DispCallProc: one member access on a dispatch interface. Desc is the constant call descriptor, Args the
  argument slots, Dest the variable that receives the result (nil without one). }
procedure DispCall(Dest: Pointer; const Disp: IDispatch; Desc: Pointer; Args: Pointer);
var
  D: PDispDesc;
  Slots: PDispSlots;
  Vars: array of TVariantArg;
  { one per argument: the OLE string of a by-reference string, or (in its low 16 bits) the value of a
    by-reference Boolean }
  Temps: array of Pointer;
  Count, I, Code, ResType: Integer;
  V: PVariantArg;
  Ref: Pointer;
  Params: TDispParams;
  Res: TVariantArg;
  ResPtr: Pointer;
  Info: TExcepInfo;
  ArgErr: Cardinal;
  PutId: Integer;
  NoIID: TGUID;
  Status: HResult;
  Source, Description, HelpFile, NoMessage: string;
begin
  D := Desc;
  Slots := Args;
  Count := D^.ArgCount;
  FillChar(Res, SizeOf(Res), 0);
  FillChar(Info, SizeOf(Info), 0);
  FillChar(NoIID, SizeOf(NoIID), 0);
  ArgErr := 0;
  PutId := DISPID_PROPERTYPUT;
  SetLength(Vars, Count);
  SetLength(Temps, Count);
  try
    { step 1: the argument records, last argument first }
    for I := 0 to Count - 1 do
    begin
      V := @Vars[Count - 1 - I];
      Code := D^.ArgTypes[I];
      if Code and dcByRef <> 0 then
      begin
        Ref := Pointer(Slots^[I]);
        if Code and dcTypeMask = dcString then
        begin
          Temps[I] := NewBStr(PString(Ref)^);
          V^.vt := VT_BSTR or VT_BYREF;
          V^.byRef := @Temps[I];
        end
        else if (Code and dcTypeMask = VT_BOOL) and (Code and dcByteBool <> 0) then
        begin
          if PBoolean(Ref)^ then
            PSmallInt(@Temps[I])^ := -1;
          V^.vt := VT_BOOL or VT_BYREF;
          V^.byRef := @Temps[I];
        end
        else
        begin
          V^.vt := Word((Code and dcTypeMask) or VT_BYREF);
          V^.byRef := Ref;
        end;
      end
      else if Code and dcTypeMask = dcString then
      begin
        V^.vt := VT_BSTR;
        V^.bstrVal := NewBStr(PString(@Slots^[I])^);
      end
      else if Code and dcTypeMask = VT_BOOL then
      begin
        V^.vt := VT_BOOL;
        if Slots^[I] <> 0 then
          V^.boolVal := -1;
      end
      else
      begin
        V^.vt := Word(Code and dcTypeMask);
        V^.llVal := Int64(Slots^[I]);
      end;
    end;

    { step 2: a property put names its value, the first record }
    Params.rgvarg := nil;
    if Count > 0 then
      Params.rgvarg := @Vars[0];
    Params.cArgs := Cardinal(Count);
    Params.rgdispidNamedArgs := nil;
    Params.cNamedArgs := 0;
    if D^.Flags and (DISPATCH_PROPERTYPUT or DISPATCH_PROPERTYPUTREF) <> 0 then
    begin
      Params.rgdispidNamedArgs := @PutId;
      Params.cNamedArgs := 1;
    end;

    { step 3 }
    ResType := D^.ResultType;
    ResPtr := nil;
    if ResType <> 0 then
      ResPtr := @Res;
    Status := Disp.Invoke(D^.DispId, NoIID, $0400, D^.Flags, Params, ResPtr, @Info, @ArgErr);

    { step 4 }
    if Status = DISP_E_EXCEPTION then
    begin
      if Info.pfnDeferredFillIn <> nil then
        TDeferredFillIn(Info.pfnDeferredFillIn)(@Info);
      Source := TakeBStr(Info.bstrSource);
      Description := TakeBStr(Info.bstrDescription);
      HelpFile := TakeBStr(Info.bstrHelpFile);
      if Info.scode <> 0 then
        Status := Info.scode;
      raise EOleException.Create(Description, Status, Source, HelpFile, Integer(Info.dwHelpContext));
    end;
    NoMessage := '';
    if Status < 0 then
      raise EOleSysError.Create(NoMessage, Status, 0);

    { step 5: the result, converted to the declared type }
    if ResType <> 0 then
    begin
      if ResType and dcTypeMask = dcString then
        Status := VariantChangeType(Res, Res, 0, VT_BSTR)
      else
        Status := VariantChangeType(Res, Res, 0, Word(ResType and dcTypeMask));
      if Status < 0 then
        raise EOleSysError.Create(NoMessage, Status, 0);
      StoreDispResult(Dest, ResType, Res);
    end;

    { by-reference strings and Booleans go back to their variables: only now, after every possible failure }
    for I := 0 to Count - 1 do
    begin
      Code := D^.ArgTypes[I];
      if Code and dcByRef <> 0 then
      begin
        Ref := Pointer(Slots^[I]);
        if Code and dcTypeMask = dcString then
          SetString(PString(Ref)^, TBStr(Temps[I]), SysStringLen(TBStr(Temps[I])))
        else if (Code and dcTypeMask = VT_BOOL) and (Code and dcByteBool <> 0) then
          PBoolean(Ref)^ := PSmallInt(@Temps[I])^ <> 0;
      end;
    end;
  finally
    { step 6: every temporary, whatever the outcome }
    for I := 0 to Count - 1 do
    begin
      Code := D^.ArgTypes[I];
      if Code and dcTypeMask = dcString then
        if Code and dcByRef <> 0 then
          SysFreeString(TBStr(Temps[I]))
        else
          SysFreeString(Vars[Count - 1 - I].bstrVal);
    end;
    VariantClear(Res);
  end;
end;

initialization
  SafeCallErrorProc := @SafeCallError;
  DispCallProc := @DispCall;

finalization
  DispCallProc := nil;
  SafeCallErrorProc := nil;
end.
