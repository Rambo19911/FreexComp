// covers: ABI-090, ABI-094, ABI-097, RTL-154, RTL-155, LANG-662
program Sys_IntfHelpers;
{ The interface runtime of rtl/src/System.pas, continued from Sys_Interfaces (types and GUIDs: the IntfKit unit):
  the helpers _IntfClr, _IntfAddRef, _IntfAsg, _IntfCast, _ObjToIntf, _ObjSupports (ABI 9.6) and the language
  "as"/"is"/"= nil" that reach them, GetInterface/QueryInterface into an interface variable (untyped "out Obj"),
  and interface references as managed parts of records, static and dynamic arrays and class fields (_Finalize,
  _AddRef, _CopyRecord with kind 15). }
uses TestKit, IntfKit;

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

begin
  TestHelpers;
  CheckNoLeak(TestUntypedOut);
  CheckNoLeak(TestLanguage);
  TestManaged;
  TestNoGuid;
  Done;
end.
