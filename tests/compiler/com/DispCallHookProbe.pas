// covers: LANG-695, LANG-696, LANG-697, CMP-1208, CMP-1209, CMP-1210, ABI-205
program DispCallHookProbe;

{$POINTERMATH ON}

uses SysUtils;

{ Every access to a member of a dispatch interface is one call of System._DispCall. The program installs its own
  System.DispCallProc, which prints the call descriptor (dispid, flags, result code, argument codes) and the raw 8-byte
  argument slots it receives, changes the by-reference arguments and stores a result through Dest. No ComObj and no system
  library: the real marshalling is tested with the run-time library. }

type
  TColor = (Red, Green, Blue);

  IOther = dispinterface
    procedure Ping; dispid 1;
  end;

  IProbe = dispinterface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8E01}']
    procedure Proc; dispid 1;
    procedure Ints(A: ShortInt; B: SmallInt; C: Integer; D: Int64); dispid 2;
    procedure UInts(A: Byte; B: Word; C: Cardinal; D: UInt64); dispid 3;
    procedure Reals(A: Single; B: Double; C: Currency; D: TDateTime); dispid 4;
    procedure Others(A: Boolean; B: WordBool; C: TColor; const S: string); dispid 5;
    procedure Refs(const A: IDispatch; const B: IOther; const C: IInterface); dispid 6;
    function Describe(Id: Integer; var Name: string): string; dispid 7;
    procedure ByRef(var I: Integer; var B: Boolean; var C: TColor; out S: string; out D: Double); dispid 8;
    function GetInt: Integer; dispid 9;
    function GetBool: Boolean; dispid 10;
    function GetColor: TColor; dispid 11;
    function GetDouble: Double; dispid 12;
    function GetDisp: IDispatch; dispid 13;
    function GetInt64: Int64; dispid 14;
    property Count: Integer readonly dispid 20;
    property Title: string dispid 21;
    property Item[const Key: string]: Integer dispid 0; default;
    property Cell[Row, Col: Integer]: Double dispid 22;
    property Link: IDispatch dispid 23;
    property Next: IOther dispid 24;
    property Sink: Word writeonly dispid 25;
  end;

  TDummy = class(TInterfacedObject, IDispatch)
    function GetTypeInfoCount(out Count: Integer): HResult; stdcall;
    function GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult; stdcall;
    function GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer; DispIDs: Pointer): HResult; stdcall;
    function Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
      VarResult, ExcepInfo, ArgErr: Pointer): HResult; stdcall;
  end;

var
  Target: IDispatch;

function TDummy.GetTypeInfoCount(out Count: Integer): HResult;
begin
  Count := 0;
  Result := 0;
end;

function TDummy.GetTypeInfo(Index, LocaleID: Integer; out TypeInfo): HResult;
begin
  Result := HResult($80004001);
end;

function TDummy.GetIDsOfNames(const IID: TGUID; Names: Pointer; NameCount, LocaleID: Integer; DispIDs: Pointer): HResult;
begin
  Result := HResult($80004001);
end;

function TDummy.Invoke(DispID: Integer; const IID: TGUID; LocaleID: Integer; Flags: Word; var Params;
  VarResult, ExcepInfo, ArgErr: Pointer): HResult;
begin
  Result := HResult($80004001);
end;

function Hex(Value: UInt64): string;
const
  Digits: string = '0123456789ABCDEF';
var
  I: Integer;
begin
  Result := '';
  for I := 15 downto 0 do
    Result := Result + Digits[((Value shr (I * 4)) and 15) + 1];
end;

procedure Probe(Dest: Pointer; const Disp: IDispatch; Desc: Pointer; Args: Pointer);
var
  P: PByte;
  Count, I, Code: Integer;
  Slot: UInt64;
  Ref: Pointer;
begin
  P := Desc;
  Count := PWord(P + 8)^;
  Write('dispid ', PInteger(P)^, ' flags ', PWord(P + 4)^, ' result ', PWord(P + 6)^, ' args ', Count);
  if Pointer(Disp) <> Pointer(Target) then
    Write(' WRONG TARGET');
  if (Count = 0) and (Args <> nil) then
    Write(' ARGS NOT NIL');
  if (PWord(P + 6)^ = 0) and (Dest <> nil) then
    Write(' DEST NOT NIL');
  WriteLn;
  for I := 0 to Count - 1 do
  begin
    Code := PWord(P + 10 + 2 * I)^;
    Slot := PUInt64(PByte(Args) + 8 * I)^;
    Write('  ', I, ': code ', Code);
    if Code and $4000 <> 0 then
    begin
      { by reference: the slot is the address of the variable }
      Ref := Pointer(Slot);
      case Code and $1FF of
        3:
          begin
            WriteLn(' ref integer ', PInteger(Ref)^);
            PInteger(Ref)^ := PInteger(Ref)^ + 100;
          end;
        5:
          begin
            WriteLn(' ref double');
            PDouble(Ref)^ := 2.5;
          end;
        11:
          begin
            WriteLn(' ref boolean ', PBoolean(Ref)^);
            PBoolean(Ref)^ := not PBoolean(Ref)^;
          end;
        $100:
          begin
            WriteLn(' ref string "', PString(Ref)^, '"');
            PString(Ref)^ := PString(Ref)^ + '+changed';
          end;
      else
        WriteLn(' ref ?');
      end;
    end
    else if Code = $100 then
      WriteLn(' string "', PString(@Slot)^, '"')
    else if (Code = 9) or (Code = 13) then
    begin
      if Pointer(Slot) = nil then
        WriteLn(' nil reference')
      else if Pointer(Slot) = Pointer(Target) then
        WriteLn(' the target')
      else
        WriteLn(' another reference');
    end
    else
      WriteLn(' slot ', Hex(Slot));
  end;
  if Dest <> nil then
    case PWord(P + 6)^ of
      3: PInteger(Dest)^ := 42 + PInteger(P)^;
      5: PDouble(Dest)^ := 1.25;
      9: IDispatch(Dest^) := Target;
      11 + $200: PBoolean(Dest)^ := True;
      20: PInt64(Dest)^ := -5000000000;
      $100: PString(Dest)^ := 'result of ' + IntToStr(PInteger(P)^);
    end;
end;

procedure Run;
var
  D: IProbe;
  Other: IOther;
  Unknown: IInterface;
  S, Name: string;
  I: Integer;
  B: Boolean;
  C: TColor;
  X: Double;
  Back: IDispatch;
begin
  Target := TDummy.Create;
  D := Target;                       { IDispatch converts to any dispatch interface: same pointer }
  Other := D;
  Unknown := D;
  WriteLn('same pointer ', (Pointer(D) = Pointer(Target)) and (Pointer(Other) = Pointer(Target)));

  WriteLn('-- methods');
  D.Proc;
  D.Ints(-1, -2, -3, -4);
  D.Ints(127, 32767, 2147483647, 9223372036854775807);
  D.UInts(255, 65535, 4294967295, 18446744073709551615);
  D.Reals(1.5, -2.25, 12.34, 36526.5);
  D.Others(True, True, Blue, 'text');
  D.Others(False, False, Red, '');
  D.Refs(Target, Other, Unknown);
  D.Refs(nil, nil, nil);

  WriteLn('-- function with var');
  Name := 'name';
  S := D.Describe(42, Name);
  WriteLn(S, ' / ', Name);
  D.Describe(1, Name);
  WriteLn(Name);

  WriteLn('-- by reference');
  I := 5;
  B := False;
  C := Green;
  S := 'old';
  D.ByRef(I, B, C, S, X);
  WriteLn(I, ' ', B, ' ', Ord(C), ' "', S, '" ', X:0:2);

  WriteLn('-- results');
  WriteLn(D.GetInt);
  WriteLn(D.GetBool);
  WriteLn(Ord(D.GetColor));
  WriteLn(D.GetDouble:0:2);
  Back := D.GetDisp;
  WriteLn(Pointer(Back) = Pointer(Target));
  WriteLn(D.GetInt64);

  WriteLn('-- properties');
  WriteLn(D.Count);
  WriteLn(D.Title);
  D.Title := 'new title';
  WriteLn(D.Item['key']);
  WriteLn(D['key']);
  D.Item['key'] := 7;
  D['other'] := 8;
  WriteLn(D.Cell[2, 3]:0:2);
  D.Cell[2, 3] := 4.5;
  Back := nil;
  Back := D.Link;
  WriteLn(Pointer(Back) = Pointer(Target));
  D.Link := Back;
  D.Next := Other;
  D.Next := nil;
  D.Sink := 65535;
end;

begin
  DispCallProc := Probe;
  Run;
  Target := nil;
  WriteLn('done');
end.
