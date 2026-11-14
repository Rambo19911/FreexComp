// phase: 4
// covers: RTL-415, RTL-416, RTL-417, RTL-418, RTL-419
program Cls_List;
{ TList (classes.md section 4): index errors with the exact message (RTL-415), the growth steps (RTL-416), Count
  growth with nil and shrink with lnDeleted from the highest index down, capacity errors (RTL-417), a stable merge
  sort (RTL-418), Notify after add/insert/extract/delete/clear/overwrite (RTL-419), the other members and Assign
  operators; TThreadList with Duplicates, and its lock excluding a second thread (TryEnter-free: the worker adds
  under the lock, the main thread reads after WaitForSingleObject). }
uses
  Windows, SysUtils, Classes, TestKit;

type
  TLogList = class(TList)
  protected
    procedure Notify(Ptr: Pointer; Action: TListNotification); override;
  end;

  PRec = ^TRec;
  TRec = record
    Key: Integer;
    Tag: Integer;
  end;

var
  Log: string;

procedure TLogList.Notify(Ptr: Pointer; Action: TListNotification);
const
  Names: array[TListNotification] of string = ('add', 'ext', 'del');
begin
  Log := Log + Names[Action] + IntToStr(NativeInt(Ptr)) + ' ';
end;

function P(N: NativeInt): Pointer;
begin
  Result := Pointer(N);
end;

function ByKey(Item1, Item2: Pointer): Integer;
begin
  Result := PRec(Item1)^.Key - PRec(Item2)^.Key;
end;

function ByValue(Item1, Item2: Pointer): Integer;
begin
  Result := NativeInt(Item1) - NativeInt(Item2);
end;

function Dump(L: TList): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
    Result := Result + IntToStr(NativeInt(L[I])) + ' ';
end;

function ErrorOf(L: TList; Kind, N: Integer): string;
var
  Sink: Pointer;
begin
  Result := '';
  try
    case Kind of
      0: Sink := L.Items[N];
      1: L.Insert(N, nil);
      2: L.Delete(N);
      3: L.Capacity := N;
      4: L.Count := N;
      5: Sink := L.First;
    end;
  except
    on E: EListError do
      Result := E.Message;
  end;
end;

var
  Shared: TThreadList;

function Worker(Parameter: Pointer): Integer;
var
  L: TList;
  I: Integer;
begin
  L := Shared.LockList;
  try
    for I := 1 to 100 do
      L.Add(P(1000 + I));
  finally
    Shared.UnlockList;
  end;
  Result := 0;
end;

var
  L, M: TList;
  G: TLogList;
  I: Integer;
  Recs: array[0..7] of TRec;
  Caps: string;
  Th: THandle;
  Id: Cardinal;
begin
  Section('basic members');
  L := TList.Create;
  CheckEqI(0, L.Add(P(10)), 'Add returns index');
  L.Add(P(20));
  L.Add(P(30));
  L.Insert(0, P(5));
  L.Insert(4, P(40));
  CheckEqS('5 10 20 30 40 ', Dump(L), 'Insert at 0 and Count');
  CheckEqI(2, L.IndexOf(P(20)), 'IndexOf');
  CheckEqI(-1, L.IndexOf(P(99)), 'IndexOf absent');
  L.Exchange(0, 4);
  L.Move(0, 2);
  CheckEqS('10 20 40 30 5 ', Dump(L), 'Exchange and Move');
  CheckEqI(1, L.Remove(P(20)), 'Remove returns index');
  CheckEqI(-1, L.Remove(P(20)), 'Remove absent');
  Check(L.Extract(P(30)) = P(30), 'Extract');
  Check(L.Extract(P(30)) = nil, 'Extract absent');
  Check(L.First = P(10), 'First');
  Check(L.Last = P(5), 'Last');
  L[1] := nil;
  L.Add(nil);
  L.Pack;
  CheckEqS('10 5 ', Dump(L), 'Pack removes nils');
  L.Delete(0);
  CheckEqS('5 ', Dump(L), 'Delete');
  L.Clear;
  CheckEqI(0, L.Count, 'Clear count');
  CheckEqI(0, L.Capacity, 'Clear capacity');

  Section('errors (RTL-415, RTL-417)');
  L.Add(P(1));
  CheckEqS('List index out of bounds (1)', ErrorOf(L, 0, 1), 'read past the end');
  CheckEqS('List index out of bounds (-1)', ErrorOf(L, 0, -1), 'negative');
  CheckEqS('', ErrorOf(L, 1, 1), 'Insert at Count');
  CheckEqS('List index out of bounds (3)', ErrorOf(L, 1, 3), 'Insert past Count');
  CheckEqS('List index out of bounds (2)', ErrorOf(L, 2, 2), 'Delete');
  CheckEqS('List capacity out of bounds (1)', ErrorOf(L, 3, 1), 'Capacity below Count');
  CheckEqS('List count out of bounds (-1)', ErrorOf(L, 4, -1), 'negative Count');
  L.Clear;
  CheckEqS('List index out of bounds (0)', ErrorOf(L, 5, 0), 'First of an empty list');

  Section('growth (RTL-416)');
  Caps := '';
  for I := 1 to 100 do
  begin
    if L.Count = L.Capacity then
    begin
      L.Add(nil);
      Caps := Caps + IntToStr(L.Capacity) + ' ';
    end
    else
      L.Add(nil);
  end;
  CheckEqS('4 8 12 28 44 60 76 95 118 ', Caps, 'capacity steps');
  L.Count := 2;
  L.Count := 4;
  Check((L[2] = nil) and (L[3] = nil), 'Count growth fills nil');
  L.Free;

  Section('notifications (RTL-417, RTL-419)');
  G := TLogList.Create;
  G.Add(P(1));
  G.Insert(0, P(2));
  G.Add(nil);
  G.Add(P(3));
  G.Extract(P(2));
  G.Delete(0);
  G[0] := P(4);
  G.Add(P(5));
  G.Add(P(6));
  G.Count := 1;
  G.Clear;
  CheckEqS('add1 add2 add3 ext2 del1 add4 add5 add6 del6 del5 del3 del4 ', Log, 'log');
  G.Free;

  Section('stable sort (RTL-418)');
  L := TList.Create;
  for I := 0 to 7 do
  begin
    Recs[I].Key := (I * 3) mod 4;
    Recs[I].Tag := I;
    L.Add(@Recs[I]);
  end;
  L.Sort(ByKey);
  Caps := '';
  for I := 0 to 7 do
    Caps := Caps + IntToStr(PRec(L[I])^.Key) + ':' + IntToStr(PRec(L[I])^.Tag) + ' ';
  CheckEqS('0:0 0:4 1:3 1:7 2:2 2:6 3:1 3:5 ', Caps, 'equal keys keep their order');
  L.Clear;
  for I := 1 to 37 do
    L.Add(P((I * 17) mod 37));
  L.Sort(ByValue);
  Caps := '';
  for I := 1 to L.Count - 1 do
    if NativeInt(L[I - 1]) > NativeInt(L[I]) then
      Caps := 'unsorted';
  CheckEqS('', Caps, '37 items sorted');

  Section('Assign');
  M := TList.Create;
  L.Clear;
  L.Add(P(1)); L.Add(P(2)); L.Add(P(3));
  M.Add(P(2)); M.Add(P(4));
  G := TLogList.Create;
  G.Assign(L);
  CheckEqS('1 2 3 ', Dump(G), 'laCopy');
  G.Assign(M, laAnd);
  CheckEqS('2 ', Dump(G), 'laAnd');
  G.Assign(L);
  G.Assign(M, laOr);
  CheckEqS('1 2 3 4 ', Dump(G), 'laOr');
  G.Assign(L);
  G.Assign(M, laXor);
  CheckEqS('1 3 4 ', Dump(G), 'laXor');
  G.Assign(L);
  G.Assign(M, laSrcUnique);
  CheckEqS('1 3 ', Dump(G), 'laSrcUnique');
  G.Assign(L);
  G.Assign(M, laDestUnique);
  CheckEqS('4 ', Dump(G), 'laDestUnique');
  G.Assign(L, laCopy, M);
  CheckEqS('2 4 ', Dump(G), 'ListB copies ListB after ListA');
  G.Free;
  M.Free;
  L.Free;

  Section('TThreadList');
  Shared := TThreadList.Create;
  Shared.Add(P(1));
  Shared.Add(P(1));
  CheckEqI(1, Shared.LockList.Count, 'dupIgnore by default');
  Shared.UnlockList;
  Shared.Duplicates := dupAccept;
  Shared.Add(P(1));
  CheckEqI(2, Shared.LockList.Count, 'dupAccept');
  Shared.UnlockList;
  Shared.Duplicates := dupError;
  Caps := '';
  try
    Shared.Add(P(1));
  except
    on E: EListError do
      Caps := E.Message;
  end;
  CheckEqS('List does not allow duplicates ($01)', Caps, 'dupError');
  Shared.Remove(P(1));
  Shared.Clear;
  Th := BeginThread(nil, 0, Worker, nil, 0, Id);
  WaitForSingleObject(Th, INFINITE);
  CloseHandle(Th);
  CheckEqI(100, Shared.LockList.Count, 'worker added under the lock');
  Shared.UnlockList;
  Shared.Free;
  Done;
end.
