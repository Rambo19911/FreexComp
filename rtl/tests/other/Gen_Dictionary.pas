// phase: 7
// covers: RTL-670, RTL-672
program Gen_Dictionary;
{ other-units.md section 8.2: TDictionary<K, V> (every listed member; Add of an existing key and reading a missing
  key raise EListError; enumeration in insertion order, also after removals and growth, RTL-672; Keys, Values,
  ToArray, notifications), TPair<K, V>, TObjectDictionary<K, V> (doOwnsKeys, doOwnsValues). }
uses
  SysUtils, Generics.Defaults, Generics.Collections, TestKit;

type
  TTracked = class
  public
    Name: string;
    constructor Create(const AName: string);
    destructor Destroy; override;
  end;

  TWatch = class
  public
    Log: string;
    procedure KeyNote(Sender: TObject; const Item: string; Action: TCollectionNotification);
    procedure ValueNote(Sender: TObject; const Item: Integer; Action: TCollectionNotification);
  end;

  TNoCase = class(TEqualityComparer<string>)
  public
    function Equals(const Left, Right: string): Boolean; override;
    function GetHashCode(const Value: string): Integer; override;
  end;

var
  Freed: string;

constructor TTracked.Create(const AName: string);
begin
  inherited Create;
  Name := AName;
end;

destructor TTracked.Destroy;
begin
  Freed := Freed + Name + ';';
  inherited Destroy;
end;

const
  Names: array[TCollectionNotification] of string = ('add', 'extract', 'remove');

procedure TWatch.KeyNote(Sender: TObject; const Item: string; Action: TCollectionNotification);
begin
  Log := Log + Names[Action] + ' ' + Item + ';';
end;

procedure TWatch.ValueNote(Sender: TObject; const Item: Integer; Action: TCollectionNotification);
begin
  Log := Log + Names[Action] + ' ' + IntToStr(Item) + ';';
end;

function TNoCase.Equals(const Left, Right: string): Boolean;
begin
  Result := SameText(Left, Right);
end;

function TNoCase.GetHashCode(const Value: string): Integer;
begin
  Result := Length(Value);
end;

function Pairs(D: TDictionary<string, Integer>): string;
var
  P: TPair<string, Integer>;
begin
  Result := '';
  for P in D do
    Result := Result + P.Key + '=' + IntToStr(P.Value) + ';';
end;

function ErrorOf(D: TDictionary<string, Integer>; Op: Integer): string;
var
  X: Integer;
begin
  Result := '';
  try
    case Op of
      0: D.Add('one', 9);
      1: X := D['nope'];
      2: D['nope'] := 1;
    end;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

var
  D: TDictionary<string, Integer>;
  N: TDictionary<Integer, Integer>;
  C: TDictionary<string, Integer>;
  O: TObjectDictionary<TTracked, TTracked>;
  W: TWatch;
  P: TPair<string, Integer>;
  A: TArray<TPair<string, Integer>>;
  V, I, Sum: Integer;
  S, K: string;
  X: Integer;
  K1, K2: TTracked;
begin
  Section('add, read, remove (RTL-670)');
  D := TDictionary<string, Integer>.Create;
  D.Add('one', 1);
  D.Add('two', 2);
  D.Add('three', 3);
  CheckEqI(3, D.Count, 'Count');
  CheckEqI(2, D['two'], 'Items read');
  D['two'] := 22;
  CheckEqI(22, D['two'], 'Items write');
  CheckEqB(True, D.TryGetValue('three', V), 'TryGetValue found');
  CheckEqI(3, V, 'TryGetValue value');
  CheckEqB(False, D.TryGetValue('four', V), 'TryGetValue missing');
  CheckEqI(0, V, 'TryGetValue missing gives the default');
  CheckEqB(True, D.ContainsKey('one'), 'ContainsKey');
  CheckEqB(False, D.ContainsKey('ONE'), 'keys are case-sensitive by default');
  CheckEqB(True, D.ContainsValue(22), 'ContainsValue');
  CheckEqB(False, D.ContainsValue(2), 'ContainsValue after overwrite');
  D.AddOrSetValue('one', 11);
  D.AddOrSetValue('four', 4);
  CheckEqS('one=11;two=22;three=3;four=4;', Pairs(D), 'AddOrSetValue; insertion order (RTL-672)');
  D.Remove('two');
  D.Remove('missing');
  CheckEqS('one=11;three=3;four=4;', Pairs(D), 'Remove keeps the order of the rest');
  D.Add('two', 2);
  CheckEqS('one=11;three=3;four=4;two=2;', Pairs(D), 're-added key goes last');
  P := D.ExtractPair('three');
  CheckEqS('three=3', P.Key + '=' + IntToStr(P.Value), 'ExtractPair');
  CheckEqI(3, D.Count, 'Count after ExtractPair');

  Section('Keys, Values, ToArray');
  S := '';
  for K in D.Keys do
    S := S + K + ',';
  CheckEqS('one,four,two,', S, 'Keys in order');
  S := '';
  for P in D do
    S := S + P.Key;
  CheckEqS('onefourtwo', S, 'pair enumeration');
  S := '';
  for I in D.Values do
    S := S + IntToStr(I) + ',';
  CheckEqS('11,4,2,', S, 'Values in order');
  A := D.ToArray;
  CheckEqI(3, Length(A), 'ToArray length');
  CheckEqS('four', A[1].Key, 'ToArray order');
  CheckEqI(3, Length(D.Keys.ToArray), 'Keys.ToArray');
  CheckEqS('two', D.Keys.ToArray[2], 'Keys order');

  Section('errors');
  CheckEqS('EListError: Duplicates not allowed', ErrorOf(D, 0), 'Add of an existing key');
  CheckEqS('EListError: Item not found', ErrorOf(D, 1), 'read of a missing key');
  CheckEqS('EListError: Item not found', ErrorOf(D, 2), 'write of a missing key');
  D.Clear;
  CheckEqI(0, D.Count, 'Clear');
  CheckEqS('', Pairs(D), 'empty after Clear');
  D.Free;

  Section('growth and removals');
  N := TDictionary<Integer, Integer>.Create(2);
  for I := 1 to 1000 do
    N.Add(I * 7, I);
  for I := 1 to 1000 do
    if I mod 3 = 0 then
      N.Remove(I * 7);
  CheckEqI(667, N.Count, 'Count after removals');
  Sum := 0;
  for I := 1 to 1000 do
    if N.ContainsKey(I * 7) then
      Sum := Sum + N[I * 7];
  CheckEqI(333667, Sum, 'every remaining key found');
  V := 0;
  S := 'ok';
  for X in N.Values do
  begin
    Inc(V);
    if (V mod 2 = 1) and (X <> (V div 2) * 3 + 1) then
      S := 'out of order';
  end;
  CheckEqS('ok', S, 'insertion order after growth and removals');
  N.TrimExcess;
  CheckEqI(667, N.Count, 'TrimExcess keeps the entries');
  CheckEqI(1, N[7], 'lookup after TrimExcess');
  N.Free;

  Section('comparer, notifications');
  C := TDictionary<string, Integer>.Create(TNoCase.Create);
  C.Add('Key', 1);
  CheckEqB(True, C.ContainsKey('KEY'), 'custom equality comparer');
  W := TWatch.Create;
  C.OnKeyNotify := W.KeyNote;
  C.OnValueNotify := W.ValueNote;
  C.Add('b', 2);
  C['b'] := 3;
  C.ExtractPair('b');
  C.Remove('key');
  CheckEqS('add b;add 2;remove 2;add 3;extract b;extract 3;remove Key;remove 1;', W.Log, 'notifications');
  C.OnKeyNotify := nil;
  C.OnValueNotify := nil;
  C.Free;
  W.Free;
  P := TPair<string, Integer>.Create('k', 5);
  CheckEqS('k5', P.Key + IntToStr(P.Value), 'TPair.Create');

  Section('TObjectDictionary');
  Freed := '';
  O := TObjectDictionary<TTracked, TTracked>.Create([doOwnsKeys, doOwnsValues]);
  K1 := TTracked.Create('k1');
  K2 := TTracked.Create('k2');
  O.Add(K1, TTracked.Create('v1'));
  O.Add(K2, TTracked.Create('v2'));
  O.Remove(K1);
  CheckEqS('k1;v1;', Freed, 'Remove frees key and value');
  O.ExtractPair(K2).Value.Free;
  CheckEqS('k1;v1;v2;', Freed, 'ExtractPair frees nothing');
  O.Free;
  K2.Free;
  CheckEqS('k1;v1;v2;k2;', Freed, 'extracted key still alive');
  Freed := '';
  O := TObjectDictionary<TTracked, TTracked>.Create([doOwnsValues]);
  K1 := TTracked.Create('k');
  O.Add(K1, TTracked.Create('v'));
  O.Free;
  CheckEqS('v;', Freed, 'only values owned');
  K1.Free;
  Done;
end.
