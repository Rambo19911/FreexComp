// phase: 4
// covers: RTL-403, RTL-404
program TI_GetSet;
{ The typed accessors of TypInfo (classes.md section 1) over field, static-method and virtual-method accessors:
  ordinals of every size and signedness, chars, enums and sets by value and by name, Int64, Single/Double, every
  string kind incl. short strings, objects (MinClass), events; index specifiers pass the index (RTL-404); a virtual
  accessor is taken from the instance's VMT, so a descendant's override runs (RTL-404); the wrong kind, a read-only
  and a write-only property raise EPropertyError with the RTL-403 texts; an unknown enum name raises
  EPropertyConvertError. IsStoredProp over every stored kind uses hand-built TPropInfo entries (the compiler does
  not emit stored specifiers yet). }
uses
  SysUtils, TypInfo, TestKit;

type
  TMode = (mdOff, mdOn, mdAuto);
  TModes = set of TMode;
  TShort7 = string[7];
  TShort20 = string[20];
  TNotify = procedure(Sender: TObject) of object;

  {$M+}
  TProbe = class
  private
    FByte: Byte;
    FShort: ShortInt;
    FSmall: SmallInt;
    FWord: Word;
    FInt: Integer;
    FCard: Cardinal;
    FBig: Int64;
    FChar: Char;
    FMode: TMode;
    FModes: TModes;
    FFlag: Boolean;
    FSingle: Single;
    FDouble: Double;
    FStr: string;
    FAnsi: AnsiString;
    FS7: TShort7;
    FS20: TShort20;
    FObj: TObject;
    FEvent: TNotify;
    FParts: array[0..3] of Integer;
    FLog: string;
    FStoredFlag: Boolean;
    function GetSmall: SmallInt;
    procedure SetSmall(Value: SmallInt);
    function GetPart(Index: Integer): Integer;
    procedure SetPart(Index: Integer; Value: Integer);
    function GetText(Index: Integer): string;
    procedure SetText(Index: Integer; const Value: string);
    function GetDouble: Double; virtual;
    procedure SetDouble(Value: Double); virtual;
    function GetS7: TShort7;
    procedure SetS7(const Value: TShort7);
    function GetS20: TShort20;
    procedure SetS20(const Value: TShort20);
    function GetEvent: TNotify;
    procedure SetEvent(const Value: TNotify);
    function GetMode: TMode; virtual;
    function GetBig: Int64;
    procedure SetBig(Value: Int64);
    function GetSingle: Single;
    procedure SetSingle(Value: Single);
    function GetAnsi: AnsiString;
    procedure SetAnsi(const Value: AnsiString);
    function GetHalf: Integer;
  published
    function StoredByMethod: Boolean;
    function StoredVirtual: Boolean; virtual;
    property AByte: Byte read FByte write FByte;
    property AShort: ShortInt read FShort write FShort;
    property ASmall: SmallInt read GetSmall write SetSmall;
    property AWord: Word read FWord write FWord;
    property AInt: Integer read FInt write FInt;
    property ACard: Cardinal read FCard write FCard;
    property ABig: Int64 read GetBig write SetBig;
    property AChar: Char read FChar write FChar;
    property Mode: TMode read GetMode write FMode;
    property Modes: TModes read FModes write FModes;
    property Flag: Boolean read FFlag write FFlag;
    property ASingle: Single read GetSingle write SetSingle;
    property FieldSingle: Single read FSingle write FSingle;
    property ADouble: Double read GetDouble write SetDouble;
    property Str: string read FStr write FStr;
    property Ansi: AnsiString read GetAnsi write SetAnsi;
    property FieldAnsi: AnsiString read FAnsi write FAnsi;
    property S7: TShort7 read GetS7 write SetS7;
    property S20: TShort20 read GetS20 write SetS20;
    property FieldS7: TShort7 read FS7 write FS7;
    property Obj: TObject read FObj write FObj;
    property OnEvent: TNotify read GetEvent write SetEvent;
    property FieldEvent: TNotify read FEvent write FEvent;
    property Part1: Integer index 1 read GetPart write SetPart;
    property Part2: Integer index 2 read GetPart write SetPart;
    property Text3: string index 3 read GetText write SetText;
    property Half: Integer read GetHalf;
    property WriteOnly: Integer write FInt;
  end;
  {$M-}

  TDerived = class(TProbe)
  protected
    function GetDouble: Double; override;
    procedure SetDouble(Value: Double); override;
    function GetMode: TMode; override;
  public
    function StoredVirtual: Boolean; override;
  end;

  TOther = class
  end;

function TProbe.GetSmall: SmallInt; begin Result := FSmall; end;
procedure TProbe.SetSmall(Value: SmallInt); begin FSmall := Value; FLog := FLog + 'SetSmall;'; end;
function TProbe.GetPart(Index: Integer): Integer; begin Result := FParts[Index] * 10 + Index; end;
procedure TProbe.SetPart(Index: Integer; Value: Integer); begin FParts[Index] := Value; end;
function TProbe.GetText(Index: Integer): string; begin Result := 'text' + IntToStr(Index) + FStr; end;
procedure TProbe.SetText(Index: Integer; const Value: string); begin FStr := Value + IntToStr(Index); end;
function TProbe.GetDouble: Double; begin Result := FDouble; end;
procedure TProbe.SetDouble(Value: Double); begin FDouble := Value; end;
function TProbe.GetS7: TShort7; begin Result := FS7; end;
procedure TProbe.SetS7(const Value: TShort7); begin FS7 := Value; FLog := FLog + 'SetS7;'; end;
function TProbe.GetS20: TShort20; begin Result := FS20; end;
procedure TProbe.SetS20(const Value: TShort20); begin FS20 := Value; end;
function TProbe.GetEvent: TNotify; begin Result := FEvent; end;
procedure TProbe.SetEvent(const Value: TNotify); begin FEvent := Value; FLog := FLog + 'SetEvent;'; end;
function TProbe.GetMode: TMode; begin Result := FMode; end;
function TProbe.GetBig: Int64; begin Result := FBig; end;
procedure TProbe.SetBig(Value: Int64); begin FBig := Value; end;
function TProbe.GetSingle: Single; begin Result := FSingle; end;
procedure TProbe.SetSingle(Value: Single); begin FSingle := Value; end;
function TProbe.GetAnsi: AnsiString; begin Result := FAnsi; end;
procedure TProbe.SetAnsi(const Value: AnsiString); begin FAnsi := Value; end;
function TProbe.GetHalf: Integer; begin Result := FInt div 2; end;
function TProbe.StoredByMethod: Boolean; begin Result := FInt > 0; end;
function TProbe.StoredVirtual: Boolean; begin Result := False; end;

function TDerived.GetDouble: Double; begin Result := FDouble * 2; end;
procedure TDerived.SetDouble(Value: Double); begin FDouble := Value + 1; end;
function TDerived.GetMode: TMode; begin Result := mdAuto; end;
function TDerived.StoredVirtual: Boolean; begin Result := True; end;

var
  Handled: string;

type
  THandler = class
  published
    procedure Handle(Sender: TObject);
  end;

procedure THandler.Handle(Sender: TObject);
begin
  Handled := Handled + 'handled;';
end;

function ErrorOf(Instance: TObject; const PropName: string; Kind: Integer): string;
begin
  Result := '';
  try
    case Kind of
      0: GetStrProp(Instance, PropName);
      1: SetOrdProp(Instance, PropName, 1);
      2: GetOrdProp(Instance, PropName);
      3: SetEnumProp(Instance, PropName, 'mdNone');
      4: GetFloatProp(Instance, PropName);
    end;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

var
  P: TProbe;
  D: TDerived;
  H: THandler;
  M: TMethod;
  Info: TPropInfo;
  Obj: TOther;
begin
  P := TProbe.Create;
  D := TDerived.Create;
  H := THandler.Create;
  Obj := TOther.Create;

  Section('ordinals');
  SetOrdProp(P, 'AByte', 250);
  CheckEqI(250, GetOrdProp(P, 'AByte'), 'Byte');
  SetOrdProp(P, 'AShort', -100);
  CheckEqI(-100, GetOrdProp(P, 'AShort'), 'ShortInt sign-extended');
  SetOrdProp(P, 'ASmall', -30000);
  CheckEqI(-30000, P.ASmall, 'SmallInt through a static setter');
  CheckEqI(-30000, GetOrdProp(P, 'ASmall'), 'SmallInt through a static getter');
  SetOrdProp(P, 'AWord', 65535);
  CheckEqI(65535, GetOrdProp(P, 'AWord'), 'Word');
  SetOrdProp(P, 'AInt', -7);
  CheckEqI(-7, GetOrdProp(P, 'AInt'), 'Integer');
  SetOrdProp(P, 'ACard', $FFFFFFFE);
  CheckEqI($FFFFFFFE, GetOrdProp(P, 'ACard'), 'Cardinal not sign-extended');
  SetInt64Prop(P, 'ABig', -123456789012);
  CheckEqI(-123456789012, GetInt64Prop(P, 'ABig'), 'Int64');
  SetOrdProp(P, 'AChar', Ord('Z'));
  Check(P.AChar = 'Z', 'Char');
  CheckEqI(90, GetOrdProp(P, 'AChar'), 'Char read');
  SetOrdProp(P, 'Flag', 1);
  CheckEqB(True, P.Flag, 'Boolean');
  SetOrdProp(P, 'Obj', NativeInt(Pointer(Obj)));
  Check(P.Obj = Obj, 'class through SetOrdProp');

  Section('enums and sets');
  SetEnumProp(P, 'Mode', 'MDON');
  CheckEqS('mdOn', GetEnumProp(P, 'Mode'), 'enum by name, virtual getter');
  CheckEqS('mdAuto', GetEnumProp(D, 'Mode'), 'virtual getter of the descendant');
  SetSetProp(P, 'Modes', '[mdOff,mdAuto]');
  Check(P.Modes = [mdOff, mdAuto], 'set by names');
  CheckEqS('mdOff,mdAuto', GetSetProp(P, 'Modes'), 'set names');
  CheckEqS('[mdOff,mdAuto]', GetSetProp(P, 'Modes', True), 'set names with brackets');
  CheckEqI(5, GetOrdProp(P, 'Modes'), 'set bits');
  CheckEqS('False', GetEnumProp(D, 'Flag'), 'Boolean name');

  Section('index specifiers');
  SetOrdProp(P, 'Part1', 4);
  SetOrdProp(P, 'Part2', 6);
  CheckEqI(41, GetOrdProp(P, 'Part1'), 'index 1');
  CheckEqI(62, GetOrdProp(P, 'Part2'), 'index 2');
  SetStrProp(P, 'Text3', 'v');
  CheckEqS('v3', P.Str, 'string setter with index');
  CheckEqS('text3v3', GetStrProp(P, 'Text3'), 'string getter with index');

  Section('floats');
  SetFloatProp(P, 'ADouble', 1.5);
  CheckEqF(1.5, GetFloatProp(P, 'ADouble'), 0, 'Double, virtual accessors');
  SetFloatProp(D, 'ADouble', 1.5);
  CheckEqF(5.0, GetFloatProp(D, 'ADouble'), 0, 'overrides run');
  SetFloatProp(P, 'ASingle', 0.25);
  CheckEqF(0.25, GetFloatProp(P, 'ASingle'), 0, 'Single through methods');
  SetFloatProp(P, 'FieldSingle', 0.75);
  CheckEqF(0.75, GetFloatProp(P, 'FieldSingle'), 0, 'Single field');

  Section('strings');
  SetStrProp(P, 'Str', 'héllo');
  CheckEqS('héllo', GetStrProp(P, 'Str'), 'UnicodeString field');
  SetStrProp(P, 'Ansi', 'äb');
  CheckEqS('äb', GetStrProp(P, 'Ansi'), 'AnsiString through methods');
  SetStrProp(P, 'FieldAnsi', 'xyz');
  CheckEqS('xyz', GetStrProp(P, 'FieldAnsi'), 'AnsiString field');
  SetStrProp(P, 'S7', 'abcdefghij');
  CheckEqS('abcdefg', GetStrProp(P, 'S7'), 'string[7] (8 bytes in a register) truncated');
  SetStrProp(P, 'S20', 'twenty');
  CheckEqS('twenty', GetStrProp(P, 'S20'), 'string[20] through methods');
  SetStrProp(P, 'FieldS7', 'q');
  CheckEqS('q', GetStrProp(P, 'FieldS7'), 'string[7] field');

  Section('objects and events');
  SetObjectProp(P, 'Obj', H);
  Check(GetObjectProp(P, 'Obj') = H, 'object');
  Check(GetObjectProp(P, 'Obj', TOther) = nil, 'MinClass mismatch gives nil');
  Check(GetObjectProp(P, 'Obj', THandler) = H, 'MinClass match');
  Check(GetObjectPropClass(P, 'Obj') = TObject, 'declared class');
  M.Code := H.MethodAddress('Handle');
  M.Data := Pointer(H);
  SetMethodProp(P, 'OnEvent', M);
  P.OnEvent(P);
  CheckEqS('handled;', Handled, 'event set through a const TMethod setter');
  Check(GetMethodProp(P, 'OnEvent').Data = Pointer(H), 'event read');
  Check(GetMethodProp(P, 'OnEvent').Code = M.Code, 'event code');
  SetMethodProp(P, 'FieldEvent', M);
  Check(GetMethodProp(P, 'FieldEvent').Data = Pointer(H), 'event field');
  CheckEqS('SetSmall;SetS7;SetEvent;', P.FLog, 'setters ran');

  Section('errors (RTL-403)');
  CheckEqS('EPropertyError: Property "AInt" is of the wrong type', ErrorOf(P, 'AInt', 0), 'wrong type');
  CheckEqS('EPropertyError: Property "Str" is of the wrong type', ErrorOf(P, 'Str', 2), 'string as ordinal');
  CheckEqS('EPropertyError: Property "AInt" is of the wrong type', ErrorOf(P, 'AInt', 4), 'integer as float');
  CheckEqS('EPropertyError: Property "Half" is read-only', ErrorOf(P, 'Half', 1), 'read-only');
  CheckEqS('EPropertyError: Property "WriteOnly" is write-only', ErrorOf(P, 'WriteOnly', 2), 'write-only');
  CheckEqS('EPropertyError: Property "Nope" does not exist', ErrorOf(P, 'Nope', 2), 'unknown');
  CheckEqS('EPropertyConvertError: Invalid property value', ErrorOf(P, 'Mode', 3), 'unknown enum name');

  Section('stored');
  FillChar(Info, SizeOf(Info), 0);
  Info.PropType := TypeInfo(Integer);
  Info.StoredKind := 0;
  Info.StoredProc := 0;
  CheckEqB(False, IsStoredProp(P, @Info), 'constant False');
  Info.StoredProc := 1;
  CheckEqB(True, IsStoredProp(P, @Info), 'constant True');
  Info.StoredKind := 1;
  Info.StoredProc := NativeInt(@P.FStoredFlag) - NativeInt(Pointer(P));
  P.FStoredFlag := True;
  CheckEqB(True, IsStoredProp(P, @Info), 'field');
  Info.StoredKind := 2;
  Info.StoredProc := NativeInt(TProbe.MethodAddress('StoredByMethod'));
  P.AInt := 0;
  CheckEqB(False, IsStoredProp(P, @Info), 'static method false');
  P.AInt := 3;
  CheckEqB(True, IsStoredProp(P, @Info), 'static method true');
  Info.StoredKind := 3;
  Info.StoredProc := 64 + 8 * 14;             { slots 11..14: GetDouble, SetDouble, GetMode, StoredVirtual }
  CheckEqB(False, IsStoredProp(P, @Info), 'virtual method of the base');
  CheckEqB(True, IsStoredProp(D, @Info), 'virtual method of the descendant');
  Info.Name := '';

  Obj.Free;
  H.Free;
  D.Free;
  P.Free;
  Done;
end.
