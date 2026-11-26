// phase: 8
// covers: RTL-670, RTL-672
program Gen_NestedTypeNames;
{ generics.md section 11.3, other-units.md section 8.2: the nested type names of the containers are aliases of the
  top-level enumerator and view classes - TList<T>.TEnumerator, TDictionary<K, V>.TKeyCollection, .TValueCollection,
  .TPairEnumerator, .TKeyEnumerator, .TValueEnumerator, TQueue<T>.TEnumerator, TStack<T>.TEnumerator - and the
  object-owning containers inherit them. Variables of the nested types are fed from the existing members; is / as
  and class references see one class per name pair. }
uses
  SysUtils, Generics.Collections, TestKit;

var
  L: TList<Integer>;
  LE: TList<Integer>.TEnumerator;
  D: TDictionary<string, Integer>;
  KC: TDictionary<string, Integer>.TKeyCollection;
  VC: TDictionary<string, Integer>.TValueCollection;
  PE: TDictionary<string, Integer>.TPairEnumerator;
  KE: TDictionary<string, Integer>.TKeyEnumerator;
  VE: TDictionary<string, Integer>.TValueEnumerator;
  Q: TQueue<Integer>;
  QE: TQueue<Integer>.TEnumerator;
  S: TStack<string>;
  SE: TStack<string>.TEnumerator;
  OL: TObjectList<TObject>;
  OLE: TObjectList<TObject>.TEnumerator;
  OD: TObjectDictionary<string, TObject>;
  OKC: TObjectDictionary<string, TObject>.TKeyCollection;
  OQ: TObjectQueue<TObject>;
  OQE: TObjectQueue<TObject>.TEnumerator;
  OS: TObjectStack<TObject>;
  OSE: TObjectStack<TObject>.TEnumerator;
  E: TObject;
  C1, C2: TClass;
  Text: string;
begin
  Section('TList<T>.TEnumerator');
  L := TList<Integer>.Create;
  L.Add(1);
  L.Add(2);
  L.Add(3);
  LE := L.GetEnumerator as TList<Integer>.TEnumerator;
  Text := '';
  while LE.MoveNext do
    Text := Text + IntStr(LE.Current) + ';';
  CheckEqS('1;2;3;', Text, 'list walk through the nested type');
  Check(LE is TList<Integer>.TEnumerator, 'is nested name');
  Check(LE is TListEnumerator<Integer>, 'is top-level name');
  Check(TList<Integer>.TEnumerator = TListEnumerator<Integer>, 'nested name = top-level class');
  C1 := TList<Integer>.TEnumerator;
  C2 := TList<string>.TEnumerator;
  Check(C1 <> C2, 'instances differ');
  C2 := TList<Integer>.TEnumerator;
  Check(C1 = C2, 'equal instances share the class');
  E := LE;
  Check(not (E is TQueue<Integer>.TEnumerator), 'not a queue enumerator');
  LE.Free;

  Section('TDictionary<K, V> nested names');
  D := TDictionary<string, Integer>.Create;
  D.Add('a', 1);
  D.Add('b', 2);
  KC := D.Keys;
  VC := D.Values;
  Check(KC is TKeyCollection<string, Integer>, 'Keys is TKeyCollection');
  Check(VC is TDictionary<string, Integer>.TValueCollection, 'Values is TValueCollection');
  Check(TDictionary<string, Integer>.TKeyCollection = TKeyCollection<string, Integer>, 'TKeyCollection alias');
  Check(TDictionary<string, Integer>.TValueCollection = TValueCollection<string, Integer>, 'TValueCollection alias');
  Check(TDictionary<string, Integer>.TPairEnumerator = TDictionaryEnumerator<string, Integer>, 'TPairEnumerator alias');
  Check(TDictionary<string, Integer>.TKeyEnumerator = TKeyEnumerator<string, Integer>, 'TKeyEnumerator alias');
  Check(TDictionary<string, Integer>.TValueEnumerator = TValueEnumerator<string, Integer>, 'TValueEnumerator alias');
  PE := D.GetEnumerator as TDictionary<string, Integer>.TPairEnumerator;
  Text := '';
  while PE.MoveNext do
    Text := Text + PE.Current.Key + '=' + IntStr(PE.Current.Value) + ';';
  CheckEqS('a=1;b=2;', Text, 'pair walk');
  Check(PE is TDictionary<string, Integer>.TPairEnumerator, 'is TPairEnumerator');
  PE.Free;
  KE := KC.GetEnumerator as TDictionary<string, Integer>.TKeyEnumerator;
  Text := '';
  while KE.MoveNext do
    Text := Text + KE.Current + ';';
  CheckEqS('a;b;', Text, 'key walk');
  E := KE;
  Check(not (E is TDictionary<string, Integer>.TValueEnumerator), 'key enumerator is not a value enumerator');
  KE.Free;
  VE := VC.GetEnumerator as TDictionary<string, Integer>.TValueEnumerator;
  Text := '';
  while VE.MoveNext do
    Text := Text + IntStr(VE.Current) + ';';
  CheckEqS('1;2;', Text, 'value walk');
  Check(VE is TDictionary<string, Integer>.TValueEnumerator, 'is TValueEnumerator');
  VE.Free;

  Section('TQueue<T>.TEnumerator, TStack<T>.TEnumerator');
  Q := TQueue<Integer>.Create;
  Q.Enqueue(7);
  Q.Enqueue(8);
  QE := Q.GetEnumerator as TQueue<Integer>.TEnumerator;
  Text := '';
  while QE.MoveNext do
    Text := Text + IntStr(QE.Current) + ';';
  CheckEqS('7;8;', Text, 'queue walk');
  Check(TQueue<Integer>.TEnumerator = TQueueEnumerator<Integer>, 'queue alias');
  QE.Free;
  S := TStack<string>.Create;
  S.Push('x');
  S.Push('y');
  SE := S.GetEnumerator as TStack<string>.TEnumerator;
  Text := '';
  while SE.MoveNext do
    Text := Text + SE.Current + ';';
  CheckEqS('x;y;', Text, 'stack walk');
  Check(TStack<string>.TEnumerator = TStackEnumerator<string>, 'stack alias');
  E := SE;
  Check(not (E is TList<string>.TEnumerator), 'stack enumerator is not a list enumerator');
  SE.Free;

  Section('object-owning containers inherit the names');
  OL := TObjectList<TObject>.Create;
  OL.Add(TObject.Create);
  OLE := OL.GetEnumerator as TObjectList<TObject>.TEnumerator;
  Check(OLE.MoveNext and (OLE.Current = OL[0]), 'object list walk');
  Check(TObjectList<TObject>.TEnumerator = TList<TObject>.TEnumerator, 'object list alias');
  OLE.Free;
  OD := TObjectDictionary<string, TObject>.Create([doOwnsValues]);
  OD.Add('k', TObject.Create);
  OKC := OD.Keys;
  Check(OKC is TObjectDictionary<string, TObject>.TKeyCollection, 'object dictionary keys');
  Check(TObjectDictionary<string, TObject>.TPairEnumerator = TDictionary<string, TObject>.TPairEnumerator,
    'object dictionary alias');
  OQ := TObjectQueue<TObject>.Create;
  OQ.Enqueue(TObject.Create);
  OQE := OQ.GetEnumerator as TObjectQueue<TObject>.TEnumerator;
  Check(OQE.MoveNext and (OQE.Current <> nil), 'object queue walk');
  OQE.Free;
  OS := TObjectStack<TObject>.Create;
  OS.Push(TObject.Create);
  OSE := OS.GetEnumerator as TObjectStack<TObject>.TEnumerator;
  Check(OSE.MoveNext and (OSE.Current <> nil), 'object stack walk');
  Check(TObjectStack<TObject>.TEnumerator = TStack<TObject>.TEnumerator, 'object stack alias');
  OSE.Free;

  OS.Free;
  OQ.Free;
  OD.Free;
  OL.Free;
  S.Free;
  Q.Free;
  D.Free;
  L.Free;
  Done;
end.
