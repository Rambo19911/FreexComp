// covers: RTL-1012, RTL-1013
// phase: 8
program Com_SafecallErrorInfo;
{ Error information across a safecall boundary (com-support.md RTL-1012, RTL-1013): a class whose
  SafeCallException calls ComObj.HandleSafeCallException. A raw stdcall caller of the same method table sees the
  result code and the error information of the thread; a safecall caller gets EOleException built from it by the
  handler ComObj installs as System.SafeCallErrorProc. }
uses SysUtils, Windows, ActiveX, ComObj, TestKit;

const
  ProgID = 'Test.Thing';
  HelpName = 'thing.hlp';

type
  IThing = interface
    ['{3C5A7E91-2B4D-4F60-A1C3-5E7092B4D6F8}']
    procedure Boom; safecall;
    procedure NoInterface; safecall;
    procedure Foreign; safecall;
    function Value: Integer; safecall;
  end;

  IThingRaw = interface
    ['{3C5A7E91-2B4D-4F60-A1C3-5E7092B4D6F8}']
    function Boom: HResult; stdcall;
    function NoInterface: HResult; stdcall;
    function Foreign: HResult; stdcall;
    function Value(out Res: Integer): HResult; stdcall;
  end;

  TThing = class(TInterfacedObject, IThing)
  public
    procedure Boom; safecall;
    procedure NoInterface; safecall;
    procedure Foreign; safecall;
    function Value: Integer; safecall;
    function SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult; override;
  end;

procedure TThing.Boom;
begin
  raise Exception.CreateHelp('boom 42', 7);
end;

procedure TThing.NoInterface;
begin
  OleError(E_NOINTERFACE);
end;

procedure TThing.Foreign;
begin
  raise TObject.Create;                { not an Exception: no description }
end;

function TThing.Value: Integer;
begin
  Result := 5;
end;

function TThing.SafeCallException(ExceptObject: TObject; ExceptAddr: Pointer): HResult;
begin
  Result := HandleSafeCallException(ExceptObject, ExceptAddr, IThing, ProgID, HelpName);
end;

function Take(var B: TBStr): string;
begin
  SetString(Result, B, SysStringLen(B));
  SysFreeString(B);
  B := nil;
end;

procedure TestRawCaller(const Raw: IThingRaw);
var
  Info: IErrorInfo;
  B: TBStr;
  G: TGUID;
  Ctx: Cardinal;
  N: Integer;
begin
  Section('a raw stdcall caller (RTL-1012)');
  CheckEqI(S_OK, Raw.Value(N), 'a method that succeeds returns S_OK');
  CheckEqI(5, N, 'its result');
  CheckEqS('8000FFFF', IntToHex(Raw.Boom, 8), 'the result code of an ordinary exception');
  CheckEqI(S_OK, GetErrorInfo(0, Info), 'error information was published');
  Info.GetDescription(B);
  CheckEqS('boom 42', Take(B), 'description = the message');
  Info.GetSource(B);
  CheckEqS(ProgID, Take(B), 'source = the ProgID');
  Info.GetHelpFile(B);
  CheckEqS(HelpName, Take(B), 'the help file');
  Info.GetHelpContext(Ctx);
  CheckEqI(7, Ctx, 'the help context of the exception');
  Info.GetGUID(G);
  Check(G = IThing, 'the interface identifier');
  Info := nil;

  CheckEqS('80004002', IntToHex(Raw.NoInterface, 8), 'the code of an EOleSysError travels');
  CheckEqI(S_OK, GetErrorInfo(0, Info), 'error information of the second failure');
  Info.GetDescription(B);
  Check(Take(B) <> '', 'its description is the system message');
  Info := nil;

  CheckEqS('8000FFFF', IntToHex(Raw.Foreign, 8), 'an exception object that is not an Exception');
  CheckEqI(S_OK, GetErrorInfo(0, Info), 'error information without a description');
  Info.GetDescription(B);
  CheckEqS('', Take(B), 'the empty description');
  Info.GetSource(B);
  CheckEqS(ProgID, Take(B), 'the source is still set');
  Info := nil;
end;

procedure TestSafecallCaller(const Thing: IThing);
var
  Raised: Boolean;
begin
  Section('a safecall caller (RTL-1013)');
  Check(Assigned(SafeCallErrorProc), 'ComObj installed SafeCallErrorProc');
  CheckEqI(5, Thing.Value, 'a method that succeeds');

  Raised := False;
  try
    Thing.Boom;
  except
    on E: EOleException do
    begin
      Raised := True;
      CheckEqS('EOleException', E.ClassName, 'the class');
      CheckEqS('boom 42', E.Message, 'Message');
      CheckEqS('8000FFFF', IntToHex(E.ErrorCode, 8), 'ErrorCode');
      CheckEqS(ProgID, E.Source, 'Source');
      CheckEqS(HelpName, E.HelpFile, 'HelpFile');
      CheckEqI(7, E.HelpContext, 'HelpContext');
      Check(ExceptAddr <> nil, 'raised at an address');
    end;
  end;
  Check(Raised, 'Boom raises EOleException');

  Raised := False;
  try
    Thing.NoInterface;
  except
    on E: EOleSysError do
    begin
      Raised := True;
      CheckEqS('EOleException', E.ClassName, 'the class of the second failure');
      CheckEqS('80004002', IntToHex(E.ErrorCode, 8), 'the code raised inside arrives');
      Check(E.Message <> '', 'with the message raised inside');
    end;
  end;
  Check(Raised, 'NoInterface raises');
end;

procedure TestNoLeak(const Thing: IThing);
var
  Live: NativeInt;
  I: Integer;
begin
  Section('nothing stays allocated');
  Live := _LiveAllocCount;
  for I := 1 to 3 do
    try
      Thing.Boom;
    except
      on E: EOleException do
        ;
    end;
  CheckEqI(0, _LiveAllocCount - Live, 'live allocations after three failing calls');
end;

var
  Thing: IThing;

begin
  Thing := TThing.Create;
  TestRawCaller(IThingRaw(Pointer(Thing)));
  TestSafecallCaller(Thing);
  TestNoLeak(Thing);
  Thing := nil;
  Done;
end.
