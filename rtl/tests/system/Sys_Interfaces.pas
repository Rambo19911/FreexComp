// covers: ABI-072, ABI-090, ABI-094, ABI-095, ABI-096, RTL-151, RTL-154, RTL-155, LANG-662, LANG-663
// opc-run: variants 1 2 3 4
program Sys_Interfaces;
{ The interface runtime of rtl/src/System.pas (types and GUIDs: the IntfKit unit): TGUID, IInterface/IUnknown,
  TInterfacedObject (count 0 after construction, construction guard, frees itself at 0), the hidden interface
  fields filled by NewInstance/InitInstance for the class and its ancestors, TObject.GetInterface/GetInterfaceEntry
  and QueryInterface. The helpers, the language forms and managed interface parts: Sys_IntfHelpers.
  The variant is ParamStr(1). Variant 1: all of the above. Variant 2: freeing a TInterfacedObject that still has a
  reference is runtime error 204 in BeforeDestruction (RTL-154). Variant 3: object "as" an interface the class does
  not implement is runtime error 228. Variant 4: interface "as" an unsupported interface is runtime error 228. }
{$POINTERMATH ON}
uses TestKit, IntfKit;

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
  Done;
end.
