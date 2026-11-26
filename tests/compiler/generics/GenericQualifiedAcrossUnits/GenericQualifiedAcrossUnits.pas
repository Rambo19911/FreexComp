// covers: LANG-780, CMP-1390, CMP-1391, CMP-1392
program GenericQualifiedAcrossUnits;

uses
  UQGen, UQUser;

procedure Run;
var
  B: UQGen.TBox<Integer>;
  I: UQGen.TOuter.TInner;
  C: UQGen.TOuter.TCell<string>;
  L: TList<Integer>;
  E: UQGen.TList<Integer>.TEnumerator;
  N: Integer;
begin
  B := UQGen.TBox<Integer>.Create(7);
  WriteLn(B.Show, ' ', B.Value);
  B.Free;
  I := UQGen.TOuter.TInner.Create;
  WriteLn(I.Name);
  I.Free;
  C := UQGen.TOuter.TCell<string>.Create;
  C.Item := 'x';
  WriteLn(C.Show, ' ', C.Item);
  C.Free;
  L := TList<Integer>.Create;
  L.Add(1);
  L.Add(2);
  for N in L do
    Write(N, ' ');
  WriteLn;
  E := L.GetEnumerator;
  WriteLn(E.ClassName, ' ', UserCount(L));
  E.Free;
  L.Free;
  WriteLn(UserSum);
  WriteLn(UserBoxClass = UQGen.TBox<Integer>, ' ', UserBoxClass = TBox<Integer>);
  WriteLn(UserInnerClass = UQGen.TOuter.TInner);
  WriteLn(UserCellClass = UQGen.TOuter.TCell<string>, ' ', UserCellClass = TOuter.TCell<string>);
  WriteLn(UserEnumeratorClass = UQGen.TList<Integer>.TEnumerator, ' ', UserEnumeratorClass = TList<Integer>.TEnumerator);
  WriteLn(UserBoxInfo = TypeInfo(UQGen.TBox<Integer>), ' ', UserBoxInfo = TypeInfo(TBox<Integer>));
  WriteLn(UserCellInfo = TypeInfo(UQGen.TOuter.TCell<string>));
  WriteLn(UserEnumeratorInfo = TypeInfo(TList<Integer>.TEnumerator));
end;

var
  Live: NativeInt;
begin
  Live := _LiveAllocCount;
  Run;
  WriteLn('leaks ', _LiveAllocCount - Live);
end.
