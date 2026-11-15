unit IntfKit;

{ The interfaces, classes and GUIDs shared by Sys_Interfaces and Sys_IntfHelpers (the interface runtime of
  rtl/src/System.pas), and helpers that read the IMT and the IntfTable by hand. }
{$POINTERMATH ON}

interface

type
  IAlpha = interface
    ['{A1A1A1A1-0001-0002-0304-050607080910}']
    function Alpha: Integer;
  end;

  IBeta = interface(IAlpha)
    ['{B2B2B2B2-0001-0002-0304-050607080910}']
    function Beta: Integer;
  end;

  IGamma = interface
    ['{C3C3C3C3-0001-0002-0304-050607080910}']
    function Gamma: string;
  end;

  INoGuid = interface
    function NoGuid: Integer;
  end;

  { implemented by no class here (GuidOther) }
  IOtherIntf = interface
    ['{D4D4D4D4-0001-0002-0304-050607080910}']
    procedure Other;
  end;

  { implements IBeta (and so IAlpha) and INoGuid }
  TBase = class(TInterfacedObject, IBeta, INoGuid)
    Name: string;
    constructor Create(const AName: string);
    destructor Destroy; override;
    function Alpha: Integer;
    function Beta: Integer;
    function NoGuid: Integer;
  end;

  { adds IGamma; the hidden fields of TBase must be filled too }
  TLeaf = class(TBase, IGamma)
    Extra: Integer;
    function Gamma: string;
  end;

  { takes and drops references to itself inside its constructor (construction guard, RTL-154) }
  TSelfRef = class(TInterfacedObject, IAlpha)
    constructor Create;
    destructor Destroy; override;
    function Alpha: Integer;
  end;

  { a class field of interface type is released by CleanupInstance }
  THolder = class
    Ref: IBeta;
  end;

  TPair = record
    Tag: Integer;
    Ref: IBeta;
    Text: string;
  end;

  TRefs = array[0..2] of IBeta;
  TDynRefs = array of IBeta;

var
  Log: string;
  GuidUnknown, GuidAlpha, GuidBeta, GuidGamma, GuidOther, GuidZero: TGUID;

{ an interface reference called through its IMT slot 1/2 by hand (ABI-091/093) }
type
  TCountFunc = function(Self: Pointer): Integer;

function ImtSlot(Ref: Pointer; Slot: Integer): Pointer;
procedure SetGuid(var G: TGUID; D1: Cardinal; D2, D3: Word; B0, B1, B2, B3, B4, B5, B6, B7: Byte);
function FieldsFilled(Obj: Pointer; VMT: PByte): Boolean;
function EntryRef(Obj: Pointer; Entry: PByte): Pointer;

implementation

uses TestKit;

constructor TBase.Create(const AName: string);
begin
  Name := AName;
end;

destructor TBase.Destroy;
begin
  Log := Log + 'D(' + Name + ')';
  inherited Destroy;
end;

function TBase.Alpha: Integer;
begin
  Result := 1;
end;

function TBase.Beta: Integer;
begin
  Result := 2;
end;

function TBase.NoGuid: Integer;
begin
  Result := 4;
end;

function TLeaf.Gamma: string;
begin
  Result := 'gamma ' + Name;
end;

constructor TSelfRef.Create;
var
  A: IAlpha;
begin
  A := Self;
  Log := Log + 'in ctor ' + IntStr(RefCount) + ';';
  A := nil;
  Log := Log + 'after nil ' + IntStr(RefCount) + ';';
end;

destructor TSelfRef.Destroy;
begin
  Log := Log + 'D(self)';
  inherited Destroy;
end;

function TSelfRef.Alpha: Integer;
begin
  Result := 10;
end;

procedure SetGuid(var G: TGUID; D1: Cardinal; D2, D3: Word; B0, B1, B2, B3, B4, B5, B6, B7: Byte);
begin
  G.D1 := D1;
  G.D2 := D2;
  G.D3 := D3;
  G.D4[0] := B0;
  G.D4[1] := B1;
  G.D4[2] := B2;
  G.D4[3] := B3;
  G.D4[4] := B4;
  G.D4[5] := B5;
  G.D4[6] := B6;
  G.D4[7] := B7;
end;

procedure InitGuids;
begin
  SetGuid(GuidUnknown, 0, 0, 0, $C0, 0, 0, 0, 0, 0, 0, $46);
  SetGuid(GuidAlpha, $A1A1A1A1, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidBeta, $B2B2B2B2, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidGamma, $C3C3C3C3, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidOther, $D4D4D4D4, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidZero, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
end;

function ImtSlot(Ref: Pointer; Slot: Integer): Pointer;
begin
  Result := PPointer(PPointer(Ref)^)[Slot];
end;

{ every hidden field named by the IntfTable of VMT (ABI-095: Imt at +16, Offset at +24 of a 32-byte entry) holds its IMT }
function FieldsFilled(Obj: Pointer; VMT: PByte): Boolean;
var
  Table: PByte;
  I, Count: NativeInt;
begin
  Result := True;
  Table := PPointer(VMT + 32)^;
  if Table = nil then
    Exit;
  Count := PNativeInt(Table)^;
  for I := 0 to Count - 1 do
    if PPointer(PByte(Obj) + PNativeInt(Table + 8 + I * 32 + 24)^)^ <> PPointer(Table + 8 + I * 32 + 16)^ then
      Result := False;
end;

{ the reference GetInterface must give for Entry: the object address plus the entry's Offset }
function EntryRef(Obj: Pointer; Entry: PByte): Pointer;
begin
  Result := PByte(Obj) + PNativeInt(Entry + 24)^;
end;

initialization
  InitGuids;
end.
