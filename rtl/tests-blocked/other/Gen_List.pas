// phase: 7
// covers: RTL-670
program Gen_List;
{ other-units.md section 8.2: TEnumerable<T>, TList<T> (every listed member, growth x2 from 4, stable Sort,
  BinarySearch, bad index / empty First and Last -> EArgumentOutOfRangeException 'Argument out of range', OnNotify)
  and TObjectList<T> (frees on remove/delete/clear/overwrite, not on Extract; OwnsObjects False frees nothing). }
uses
  SysUtils, Generics.Defaults, Generics.Collections, TestKit;

type
  TItem = record
    Key: Integer;
    Tag: string;
  end;

  TTracked = class
  public
    Name: string;
    constructor Create(const AName: string);
    destructor Destroy; override;
  end;

  TDesc = class(TComparer<Integer>)
  public
    function Compare(const Left, Right: Integer): Integer; override;
  end;

  TByKey = class(TComparer<TItem>)
  public
    function Compare(const Left, Right: TItem): Integer; override;
  end;

  TWatch = class
  public
    Log: string;
    procedure Note(Sender: TObject; const Item: Integer; Action: TCollectionNotification);
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

procedure TWatch.Note(Sender: TObject; const Item: Integer; Action: TCollectionNotification);
const
  Names: array[TCollectionNotification] of string = ('add', 'extract', 'remove');
begin
  Log := Log + Names[Action] + IntToStr(Item) + ';';
end;

function TByKey.Compare(const Left, Right: TItem): Integer;
begin
  Result := Left.Key - Right.Key;
end;

function TDesc.Compare(const Left, Right: Integer): Integer;
begin
  Result := Right - Left;
end;

function Desc(const Left, Right: Integer): Integer;
begin
  Result := Right - Left;
end;

function Join(L: TList<Integer>): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(L[I]);
  end;
end;

function ErrorOf(L: TList<Integer>; Op: Integer): string;
var
  X: Integer;
begin
  Result := '';
  try
    case Op of
      0: X := L[L.Count];
      1: L.Delete(-1);
      2: L.Insert(L.Count + 1, 0);
      3: X := L.First;
      4: X := L.Last;
      5: L.DeleteRange(0, L.Count + 1);
    end;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

const
  SRangeText = 'EArgumentOutOfRangeException: Argument out of range';

var
  L, M: TList<Integer>;
  R: TList<TItem>;
  O: TObjectList<TTracked>;
  Tk: TTracked;
  W: TWatch;
  It: TItem;
  A: TArray<Integer>;
  X, Sum, Index: Integer;
  E: TEnumerator<Integer>;
begin
  Section('add, insert, delete (RTL-670)');
  L := TList<Integer>.Create;
  CheckEqI(0, L.Capacity, 'empty capacity');
  CheckEqI(0, L.Add(10), 'Add returns the index');
  CheckEqI(4, L.Capacity, 'growth starts at 4');
  L.AddRange([20, 30, 40, 50]);
  CheckEqI(8, L.Capacity, 'growth x2');
  CheckEqS('10,20,30,40,50', Join(L), 'AddRange array');
  L.Insert(0, 5);
  L.InsertRange(2, [11, 12]);
  CheckEqS('5,10,11,12,20,30,40,50', Join(L), 'Insert, InsertRange');
  L.Delete(1);
  L.DeleteRange(1, 2);
  CheckEqS('5,20,30,40,50', Join(L), 'Delete, DeleteRange');
  CheckEqI(2, L.Remove(30), 'Remove returns the index');
  CheckEqI(-1, L.Remove(99), 'Remove of a missing value');
  CheckEqI(40, L.Extract(40), 'Extract returns the item');
  CheckEqS('5,20,50', Join(L), 'after Remove and Extract');
  L.Exchange(0, 2);
  CheckEqS('50,20,5', Join(L), 'Exchange');
  L.Move(0, 2);
  CheckEqS('20,5,50', Join(L), 'Move forward');
  L.Move(2, 0);
  CheckEqS('50,20,5', Join(L), 'Move back');
  CheckEqI(50, L.First, 'First');
  CheckEqI(5, L.Last, 'Last');
  L[1] := 21;
  CheckEqI(21, L[1], 'Items write');

  Section('search, order');
  L.AddRange([21, 7]);
  CheckEqB(True, L.Contains(7), 'Contains');
  CheckEqI(1, L.IndexOf(21), 'IndexOf');
  CheckEqI(3, L.LastIndexOf(21), 'LastIndexOf');
  CheckEqI(-1, L.IndexOf(1000), 'IndexOf missing');
  L.Reverse;
  CheckEqS('7,21,5,21,50', Join(L), 'Reverse');
  L.Sort;
  CheckEqS('5,7,21,21,50', Join(L), 'Sort default');
  CheckEqB(True, L.BinarySearch(21, Index), 'BinarySearch found');
  CheckEqI(2, Index, 'BinarySearch first of equal items');
  CheckEqB(False, L.BinarySearch(8, Index), 'BinarySearch missing');
  CheckEqI(2, Index, 'BinarySearch insertion point');
  L.Sort(TDesc.Create);
  CheckEqS('50,21,21,7,5', Join(L), 'Sort with a comparer');
  A := L.ToArray;
  CheckEqI(5, Length(A), 'ToArray length');
  CheckEqI(7, A[3], 'ToArray item');
  L.Count := 2;
  CheckEqS('50,21', Join(L), 'Count truncates');
  L.Count := 3;
  CheckEqS('50,21,0', Join(L), 'Count extends with default values');
  L.TrimExcess;
  CheckEqI(3, L.Capacity, 'TrimExcess');
  Sum := 0;
  for X in L do
    Sum := Sum + X;
  CheckEqI(71, Sum, 'for-in');
  E := L.GetEnumerator;
  CheckEqB(True, E.MoveNext, 'enumerator MoveNext');
  CheckEqI(50, E.Current, 'enumerator Current');
  E.Free;
  M := TList<Integer>.Create(L);
  CheckEqS('50,21,0', Join(M), 'Create(Collection)');
  M.AddRange(L);
  CheckEqI(6, M.Count, 'AddRange enumerable');
  M.Free;

  Section('stable sort of records');
  R := TList<TItem>.Create(TByKey.Create);
  It.Key := 2; It.Tag := 'a'; R.Add(It);
  It.Key := 1; It.Tag := 'b'; R.Add(It);
  It.Key := 2; It.Tag := 'c'; R.Add(It);
  It.Key := 1; It.Tag := 'd'; R.Add(It);
  R.Sort;
  CheckEqS('bdac', R[0].Tag + R[1].Tag + R[2].Tag + R[3].Tag, 'equal keys keep their order');
  R.Free;

  Section('Construct');
  M := TList<Integer>.Create(TComparer<Integer>.Construct(Desc));
  M.AddRange([1, 3, 2]);
  M.Sort;
  CheckEqS('3,2,1', Join(M), 'list comparer from Construct');
  CheckEqI(1, M.IndexOf(2), 'IndexOf uses the comparer');
  M.Free;

  Section('errors');
  CheckEqS(SRangeText, ErrorOf(L, 0), 'read past the end');
  CheckEqS(SRangeText, ErrorOf(L, 1), 'Delete(-1)');
  CheckEqS(SRangeText, ErrorOf(L, 2), 'Insert past Count');
  CheckEqS(SRangeText, ErrorOf(L, 5), 'DeleteRange past the end');
  L.Clear;
  CheckEqI(0, L.Count, 'Clear');
  CheckEqS(SRangeText, ErrorOf(L, 3), 'First on an empty list');
  CheckEqS(SRangeText, ErrorOf(L, 4), 'Last on an empty list');

  Section('OnNotify');
  W := TWatch.Create;
  L.OnNotify := W.Note;
  L.Add(1);
  L.AddRange([2, 3]);
  L[0] := 4;
  L.Extract(2);
  L.Delete(0);
  L.Clear;
  CheckEqS('add1;add2;add3;remove1;add4;extract2;remove4;remove3;', W.Log, 'notifications in order');
  L.OnNotify := nil;
  L.Free;
  W.Free;

  Section('TObjectList');
  Freed := '';
  O := TObjectList<TTracked>.Create;
  CheckEqB(True, O.OwnsObjects, 'OwnsObjects default True');
  O.Add(TTracked.Create('a'));
  O.Add(TTracked.Create('b'));
  O.Add(TTracked.Create('c'));
  O.Add(TTracked.Create('d'));
  O.Add(TTracked.Create('e'));
  O.Delete(0);
  CheckEqS('a;', Freed, 'Delete frees');
  O.Remove(O[0]);
  CheckEqS('a;b;', Freed, 'Remove frees');
  O[0] := TTracked.Create('x');
  CheckEqS('a;b;c;', Freed, 'overwrite frees the old item');
  O.Extract(O[1]).Free;
  CheckEqS('a;b;c;d;', Freed, 'Extract does not free (freed by the caller)');
  O.Clear;
  CheckEqS('a;b;c;d;x;e;', Freed, 'Clear frees');
  O.OwnsObjects := False;
  Freed := '';
  Tk := TTracked.Create('k');
  O.Add(Tk);
  O.Clear;
  CheckEqS('', Freed, 'OwnsObjects False frees nothing');
  Tk.Free;
  O.Free;
  Done;
end.
