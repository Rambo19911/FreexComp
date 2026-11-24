{ ComObj - the behaviour of the COM support (rtl/com-support.md section 3; task R-230): the OLE exception classes,
  OleCheck / OleError, the error-information side of safecall methods (HandleSafeCallException) and the handler
  this unit installs as System.SafeCallErrorProc. Needs oleaut32 only; COM is not initialised here (RTL-1015). }
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

initialization
  SafeCallErrorProc := @SafeCallError;

finalization
  SafeCallErrorProc := nil;
end.
