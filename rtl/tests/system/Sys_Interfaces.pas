// covers: ABI-072, ABI-090, ABI-094, ABI-095, ABI-096, ABI-097, RTL-151, RTL-154, RTL-155, LANG-662, LANG-663
// opc-run: variants 1 2 3 4
program Sys_Interfaces;
{ The interface runtime of rtl/src/System.pas: TGUID, IInterface/IUnknown, TInterfacedObject (count 0 after
  construction, construction guard, frees itself at 0), the hidden interface fields filled by NewInstance/InitInstance
  for the class and its ancestors, TObject.GetInterface/GetInterfaceEntry and QueryInterface (untyped "out Obj": a
  Pointer or an interface variable), the helpers _IntfClr, _IntfAddRef, _IntfAsg, _IntfCast, _ObjToIntf, _ObjSupports
  (ABI 9.6) and the language "as"/"is"/"= nil" that reach them, and interface references as managed parts of
  records, static and dynamic arrays and class fields (_Finalize, _AddRef, _CopyRecord with kind 15).
  The variant is ParamStr(1). Variant 1: all of the above. Variant 2: freeing a TInterfacedObject that still has a
  reference is runtime error 204 in BeforeDestruction (RTL-154). Variant 3: object "as" an interface the class does
  not implement is runtime error 228. Variant 4: interface "as" an unsupported interface is runtime error 228. }
{$POINTERMATH ON}
uses TestKit;

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

var
  GuidUnknown, GuidAlpha, GuidBeta, GuidGamma, GuidOther, GuidZero: TGUID;

procedure InitGuids;
begin
  SetGuid(GuidUnknown, 0, 0, 0, $C0, 0, 0, 0, 0, 0, 0, $46);
  SetGuid(GuidAlpha, $A1A1A1A1, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidBeta, $B2B2B2B2, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidGamma, $C3C3C3C3, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidOther, $D4D4D4D4, 1, 2, 3, 4, 5, 6, 7, 8, 9, $10);
  SetGuid(GuidZero, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
end;

{ an interface reference called through its IMT slot 1/2 by hand (ABI-091/093) }
type
  TCountFunc = function(Self: Pointer): Integer;

function ImtSlot(Ref: Pointer; Slot: Integer): Pointer;
begin
  Result := PPointer(PPointer(Ref)^)[Slot];
end;

procedure TestGuidLayout;
var
  G: TGUID;
begin
  Section('TGUID (ABI-096)');
  CheckEqI(16, SizeOf(TGUID), 'SizeOf(TGUID)');
  SetGuid(G, $04030201, $0605, $0807, 9, 10, 11, 12, 13, 14, 15, 16);
  CheckEqI(1, PByte(@G)[0], 'D1 little-endian at 0');
  CheckEqI(5, PByte(@G)[4], 'D2 at 4');
  CheckEqI(7, PByte(@G)[6], 'D3 at 6');
  CheckEqI(9, PByte(@G)[8], 'D4 at 8');
  CheckEqI(16, PByte(@G)[15], 'D4[7] at 15');
  CheckEqI(0, S_OK, 'S_OK');
  CheckEqI(1, S_FALSE, 'S_FALSE');
  CheckEqI(Integer($80004002), E_NOINTERFACE, 'E_NOINTERFACE');
end;

procedure TestRefCounting;
var
  B: TBase;
  A: IBeta;
  X: IBeta;
  Before: NativeInt;
begin
  Section('TInterfacedObject counting (RTL-154/155)');
  Before := _LiveAllocCount;
  Log := '';
  B := TBase.Create('b');
  CheckEqI(0, B.RefCount, 'count 0 after construction');
  A := B;
  CheckEqI(1, B.RefCount, 'one reference');
  X := A;
  CheckEqI(2, B.RefCount, 'two references');
  X := X;
  CheckEqI(2, B.RefCount, 'self-assignment keeps the count');
  CheckEqI(3, TCountFunc(ImtSlot(Pointer(A), 1))(Pointer(A)), '_AddRef through the IMT returns the new count');
  CheckEqI(2, TCountFunc(ImtSlot(Pointer(A), 2))(Pointer(A)), '_Release through the IMT returns the new count');
  X := nil;
  CheckEqI(1, B.RefCount, 'nil releases');
  CheckEqS('', Log, 'still alive');
  A := nil;
  CheckEqS('D(b)', Log, 'freed at count 0');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestConstructionGuard;
var
  A: IAlpha;
  Before: NativeInt;
begin
  Section('construction guard (RTL-154)');
  Before := _LiveAllocCount;
  Log := '';
  A := TSelfRef.Create;
  CheckEqS('in ctor 2;after nil 1;', Log, 'guard count 1 during the constructor, not freed');
  CheckEqI(10, A.Alpha, 'usable after construction');
  Log := '';
  A := nil;
  CheckEqS('D(self)', Log, 'freed by the last release');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
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

procedure TestHiddenFields;
var
  L: TLeaf;
  U: IInterface;
  Entry: PByte;
  Before: NativeInt;
  P: Pointer;
begin
  Section('hidden interface fields (ABI-072/095)');
  Before := _LiveAllocCount;
  L := TLeaf.Create('leaf');
  U := L;
  Check(PPointer(PByte(PPointer(L)^) + 32)^ <> nil, 'TLeaf has an IntfTable');
  CheckEqB(True, FieldsFilled(Pointer(L), PPointer(L)^), 'own hidden fields hold their IMTs');
  CheckEqB(True, FieldsFilled(Pointer(L), PByte(Pointer(TBase))), 'TBase hidden fields hold their IMTs');
  CheckEqB(True, FieldsFilled(Pointer(L), PByte(Pointer(TInterfacedObject))), 'TInterfacedObject hidden field');
  Entry := TLeaf.GetInterfaceEntry(GuidAlpha);
  Check(Entry <> nil, 'GetInterfaceEntry finds an ancestor class''s interface');
  Check(TLeaf.GetInterfaceEntry(GuidOther) = nil, 'GetInterfaceEntry unknown GUID');
  Check(TLeaf.GetInterfaceEntry(GuidZero) = nil, 'GetInterfaceEntry never finds the zero GUID');
  Check(TObject.GetInterfaceEntry(GuidUnknown) = nil, 'TObject has no interfaces');
  Check(TLeaf.GetInterfaceEntry(GuidUnknown) <> nil, 'IInterface found through TInterfacedObject');
  CheckEqB(True, L.GetInterface(GuidGamma, P), 'GetInterface own');
  Check(P = EntryRef(Pointer(L), TLeaf.GetInterfaceEntry(GuidGamma)), 'GetInterface gives the hidden field address');
  CheckEqI(2, L.RefCount, 'GetInterface counts');
  CheckEqI(1, TCountFunc(ImtSlot(P, 2))(P), 'release the found reference');
  CheckEqB(True, L.GetInterface(GuidBeta, P), 'GetInterface ancestor class');
  Check(P = EntryRef(Pointer(L), TLeaf.GetInterfaceEntry(GuidBeta)), 'ancestor class field address');
  TCountFunc(ImtSlot(P, 2))(P);
  P := Pointer(1);
  CheckEqB(False, L.GetInterface(GuidOther, P), 'GetInterface unknown');
  Check(P = nil, 'GetInterface failure sets nil');
  CheckEqI(1, L.RefCount, 'no count left');
  Log := '';
  U := nil;
  CheckEqS('D(leaf)', Log, 'freed');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestQueryInterface;
var
  L: TLeaf;
  U: IInterface;
  Un: IUnknown;
  P: Pointer;
  Before: NativeInt;
begin
  Section('QueryInterface');
  Before := _LiveAllocCount;
  L := TLeaf.Create('q');
  U := L;
  Un := U;
  CheckEqI(2, L.RefCount, 'IUnknown = IInterface');
  CheckEqI(S_OK, U.QueryInterface(GuidGamma, P), 'S_OK');
  Check(P = EntryRef(Pointer(L), TLeaf.GetInterfaceEntry(GuidGamma)), 'found reference');
  CheckEqI(3, L.RefCount, 'QueryInterface counts');
  TCountFunc(ImtSlot(P, 2))(P);
  P := Pointer(1);
  CheckEqI(E_NOINTERFACE, U.QueryInterface(GuidOther, P), 'E_NOINTERFACE');
  Check(P = nil, 'failure sets nil');
  CheckEqI(S_OK, U.QueryInterface(GuidUnknown, P), 'IInterface itself');
  TCountFunc(ImtSlot(P, 2))(P);
  Un := nil;
  Log := '';
  U := nil;
  CheckEqS('D(q)', Log, 'freed');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestHelpers;
var
  L: TLeaf;
  Dest, Src: Pointer;
  Before: NativeInt;
begin
  Section('helpers (ABI 9.6)');
  Before := _LiveAllocCount;
  L := TLeaf.Create('h');
  Dest := nil;
  _ObjToIntf(Dest, Pointer(L), @GuidGamma);
  Check(Dest <> nil, '_ObjToIntf found');
  CheckEqI(1, L.RefCount, '_ObjToIntf counts');
  Src := nil;
  _IntfAsg(Src, Dest);
  Check(Src = Dest, '_IntfAsg copies');
  CheckEqI(2, L.RefCount, '_IntfAsg adds a reference');
  _IntfAddRef(Src);
  CheckEqI(3, L.RefCount, '_IntfAddRef');
  _IntfAddRef(nil);
  TCountFunc(ImtSlot(Src, 2))(Src);
  _IntfCast(Src, Dest, @GuidBeta);
  Check(Src = EntryRef(Pointer(L), TLeaf.GetInterfaceEntry(GuidBeta)), '_IntfCast to another interface');
  CheckEqI(2, L.RefCount, '_IntfCast released the old destination');
  _IntfAsg(Src, Src);
  CheckEqI(2, L.RefCount, '_IntfAsg self');
  _IntfClr(Src);
  Check(Src = nil, '_IntfClr sets nil');
  CheckEqI(1, L.RefCount, '_IntfClr releases');
  _IntfClr(Src);
  Check(Src = nil, '_IntfClr nil is a no-op');
  _IntfCast(Src, nil, @GuidBeta);
  Check(Src = nil, '_IntfCast nil gives nil');
  _ObjToIntf(Src, nil, @GuidBeta);
  Check(Src = nil, '_ObjToIntf nil gives nil');
  CheckEqB(True, _ObjSupports(Pointer(L), @GuidAlpha), '_ObjSupports ancestor interface');
  CheckEqB(True, _ObjSupports(Pointer(L), @GuidGamma), '_ObjSupports own interface');
  CheckEqB(False, _ObjSupports(Pointer(L), @GuidOther), '_ObjSupports unknown');
  CheckEqB(False, _ObjSupports(Pointer(L), @GuidZero), '_ObjSupports zero GUID');
  CheckEqB(False, _ObjSupports(nil, @GuidAlpha), '_ObjSupports nil');
  CheckEqI(1, L.RefCount, '_ObjSupports does not count');
  Log := '';
  _IntfClr(Dest);
  CheckEqS('D(h)', Log, 'freed by _IntfClr');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure FillPair(var R: TPair; const A: IBeta);
begin
  R.Tag := 7;
  R.Ref := A;
  R.Text := 'pair' + IntStr(R.Tag);
end;

procedure UsePairs(B: TBase);
var
  R1, R2: TPair;
  S: TRefs;
  D: TDynRefs;
  H: THolder;
  A: IBeta;
begin
  A := B;
  CheckEqI(2, B.RefCount, 'parameter copy');
  FillPair(R1, A);
  CheckEqI(3, B.RefCount, 'record field holds a reference');
  R2 := R1;
  CheckEqI(4, B.RefCount, 'record copy (_CopyRecord) adds a reference');
  R2.Ref := nil;
  CheckEqI(3, B.RefCount, 'record field cleared');
  S[0] := A;
  S[2] := A;
  CheckEqI(5, B.RefCount, 'static array elements');
  SetLength(D, 3);
  D[1] := A;
  CheckEqI(6, B.RefCount, 'dynamic array element');
  SetLength(D, 1);
  CheckEqI(5, B.RefCount, 'shrinking a dynamic array releases');
  SetLength(D, 2);
  D[0] := A;
  D[1] := A;
  CheckEqI(7, B.RefCount, 'two dynamic array elements');
  D := nil;
  CheckEqI(5, B.RefCount, 'dynamic array cleared');
  H := THolder.Create;
  H.Ref := A;
  CheckEqI(6, B.RefCount, 'class field');
  H.Free;
  CheckEqI(5, B.RefCount, 'class field released by Free');
end;

procedure TestManaged;
var
  B: TBase;
  A: IBeta;
  Before: NativeInt;
begin
  Section('managed interface parts (kind 15)');
  Before := _LiveAllocCount;
  Log := '';
  B := TBase.Create('m');
  A := B;
  UsePairs(B);
  CheckEqI(1, B.RefCount, 'all locals finalized');
  CheckEqS('', Log, 'alive');
  A := nil;
  CheckEqS('D(m)', Log, 'freed');
  Log := '';
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

procedure TestNoGuid;
var
  B: TBase;
  N: INoGuid;
begin
  Section('interface without a GUID');
  B := TBase.Create('n');
  N := B;
  CheckEqI(4, N.NoGuid, 'class to interface without a GUID');
  CheckEqI(1, B.RefCount, 'counted');
  Log := '';
  N := nil;
  CheckEqS('D(n)', Log, 'freed');
  Log := '';
end;

{ Body leaves no block behind; counted after Body has returned, so its string temporaries are finalized too }
procedure CheckNoLeak(Body: TProcedure);
var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Body;
  CheckEqI(0, _LiveAllocCount - Before, 'leaked');
end;

{ GetInterface / QueryInterface fill an interface variable through the untyped out parameter }
procedure TestUntypedOut;
var
  L: TLeaf;
  U: IInterface;
  G: IGamma;
  B: IBeta;
begin
  Section('untyped out Obj');
  L := TLeaf.Create('u');
  U := L;
  CheckEqB(True, L.GetInterface(GuidGamma, G), 'GetInterface into an interface variable');
  CheckEqS('gamma u', G.Gamma, 'the variable is usable');
  CheckEqI(2, L.RefCount, 'GetInterface counted the variable');
  CheckEqI(S_OK, U.QueryInterface(GuidBeta, B), 'QueryInterface into an interface variable');
  CheckEqI(2, B.Beta, 'QueryInterface result is usable');
  CheckEqI(3, L.RefCount, 'QueryInterface counted the variable');
  B := nil;
  CheckEqI(E_NOINTERFACE, U.QueryInterface(GuidOther, B), 'QueryInterface failure');
  Check(B = nil, 'failure leaves nil');
  G := nil;
  CheckEqI(1, L.RefCount, 'releases by nil');
  Log := '';
  U := nil;
  CheckEqS('D(u)', Log, 'freed');
  Log := '';
end;

{ the language forms: o as I (_ObjToIntf), i as I (_IntfCast), o is I (_ObjSupports), i = nil, i <> nil, i = j }
procedure TestLanguage;
var
  L: TLeaf;
  O: TObject;
  U: IInterface;
  G: IGamma;
  B: IBeta;
  A: IAlpha;
  N: IGamma;
begin
  Section('as / is / = nil');
  L := TLeaf.Create('l');
  O := L;
  G := O as IGamma;
  CheckEqS('gamma l', G.Gamma, 'object as interface');
  CheckEqI(1, L.RefCount, 'object as counts');
  B := G as IBeta;
  CheckEqI(2, B.Beta, 'interface as interface');
  CheckEqI(2, L.RefCount, 'interface as counts');
  A := B;
  U := A as IInterface;
  CheckEqI(4, L.RefCount, 'interface as IInterface');
  CheckEqB(True, O is IGamma, 'object is own interface');
  CheckEqB(True, O is IAlpha, 'object is ancestor interface');
  CheckEqB(False, O is IOtherIntf, 'object is unknown interface');
  CheckEqI(4, L.RefCount, 'is does not count');
  N := nil;
  CheckEqB(True, N = nil, 'nil = nil');
  CheckEqB(False, G = nil, 'reference = nil');
  CheckEqB(True, G <> nil, 'reference <> nil');
  CheckEqB(False, N <> nil, 'nil <> nil');
  N := G;
  CheckEqB(True, N = G, 'same reference');
  CheckEqB(False, B = G, 'two interfaces of one object are different references (LANG-666)');
  O := nil;
  N := O as IGamma;
  Check(N = nil, 'nil object as interface gives nil');
  U := nil;
  N := U as IGamma;
  Check(N = nil, 'nil interface as interface gives nil');
  CheckEqB(False, O is IGamma, 'nil object is nothing');
  A := nil;
  B := nil;
  CheckEqI(1, L.RefCount, 'one reference left');
  Log := '';
  G := nil;
  CheckEqS('D(l)', Log, 'freed');
  Log := '';
end;

{ variant 2: the object still has a reference when it is freed }
procedure FreeReferenced;
var
  B: TBase;
  A: IBeta;
begin
  B := TBase.Create('r');
  A := B;
  Writeln('freeing with count ', B.RefCount);
  B.Free;
  Writeln('not reached');
end;

{ variant 3: object as an interface it does not implement }
procedure ObjAsFails;
var
  O: TObject;
  X: IOtherIntf;
begin
  O := TBase.Create('x');
  Writeln('object as');
  X := O as IOtherIntf;
  Writeln('not reached');
end;

{ variant 4: interface as an interface it does not support }
procedure IntfAsFails;
var
  A: IBeta;
  X: IOtherIntf;
begin
  A := TBase.Create('y');
  Writeln('interface as');
  X := A as IOtherIntf;
  Writeln('not reached');
end;

begin
  InitGuids;
  if ParamStr(1) = '2' then
    FreeReferenced
  else if ParamStr(1) = '3' then
    ObjAsFails
  else if ParamStr(1) = '4' then
    IntfAsFails;
  TestGuidLayout;
  TestRefCounting;
  TestConstructionGuard;
  TestHiddenFields;
  TestQueryInterface;
  TestHelpers;
  CheckNoLeak(TestUntypedOut);
  CheckNoLeak(TestLanguage);
  TestManaged;
  TestNoGuid;
  Done;
end.
