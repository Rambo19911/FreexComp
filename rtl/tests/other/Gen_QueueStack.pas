// phase: 7
// covers: RTL-670
program Gen_QueueStack;
{ other-units.md section 8.2: TQueue<T> / TStack<T> (Enqueue/Dequeue/Peek, Push/Pop/Peek, Count, Clear, ToArray,
  TrimExcess, enumerators; empty Dequeue/Pop/Peek -> EListError 'Unbalanced stack or queue operation'),
  TObjectQueue<T> / TObjectStack<T> (OwnsObjects), TArray.Sort<T> (stable), BinarySearch<T>, Copy<T>. }
uses
  SysUtils, Generics.Defaults, Generics.Collections, TestKit;

type
  TTracked = class
  public
    Name: string;
    constructor Create(const AName: string);
    destructor Destroy; override;
  end;

  TByTens = class(TComparer<Integer>)
  public
    function Compare(const Left, Right: Integer): Integer; override;
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

function TByTens.Compare(const Left, Right: Integer): Integer;
begin
  Result := Left div 10 - Right div 10;
end;

function Join(const A: array of Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to High(A) do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(A[I]);
  end;
end;

function Arr(const A: array of Integer): TArray<Integer>;
var
  I: Integer;
begin
  SetLength(Result, Length(A));
  for I := 0 to High(A) do
    Result[I] := A[I];
end;

function QueueError(Q: TQueue<Integer>; Op: Integer): string;
var
  X: Integer;
begin
  Result := '';
  try
    if Op = 0 then
      X := Q.Dequeue
    else
      X := Q.Peek;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

function StackError(S: TStack<Integer>; Op: Integer): string;
var
  X: Integer;
begin
  Result := '';
  try
    if Op = 0 then
      X := S.Pop
    else
      X := S.Peek;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

const
  SUnbalancedText = 'EListError: Unbalanced stack or queue operation';

var
  Q: TQueue<Integer>;
  S: TStack<Integer>;
  QS: TQueue<string>;
  OQ: TObjectQueue<TTracked>;
  OS: TObjectStack<TTracked>;
  A, B: TArray<Integer>;
  X, I, Index: Integer;
  T: string;
begin
  Section('TQueue (RTL-670)');
  Q := TQueue<Integer>.Create;
  Q.Enqueue(1);
  Q.Enqueue(2);
  Q.Enqueue(3);
  CheckEqI(3, Q.Count, 'Count');
  CheckEqI(1, Q.Peek, 'Peek');
  CheckEqI(1, Q.Dequeue, 'Dequeue first in');
  for I := 4 to 20 do
    Q.Enqueue(I);
  for I := 2 to 15 do
    Q.Dequeue;
  CheckEqS('16,17,18,19,20', Join(Q.ToArray), 'ToArray in queue order after wrap-around');
  T := '';
  for X in Q do
    T := T + IntToStr(X) + ';';
  CheckEqS('16;17;18;19;20;', T, 'enumerator in queue order');
  Q.TrimExcess;
  CheckEqI(16, Q.Peek, 'TrimExcess keeps the items');
  Q.Clear;
  CheckEqI(0, Q.Count, 'Clear');
  CheckEqS(SUnbalancedText, QueueError(Q, 0), 'Dequeue of an empty queue');
  CheckEqS(SUnbalancedText, QueueError(Q, 1), 'Peek of an empty queue');
  Q.Free;
  QS := TQueue<string>.Create;
  QS.Enqueue('a');
  QS.Enqueue('b' + IntToStr(1));
  CheckEqS('a', QS.Dequeue, 'managed items');
  CheckEqS('b1', QS.Extract, 'Extract');
  QS.Free;

  Section('TStack');
  S := TStack<Integer>.Create;
  S.Push(1);
  S.Push(2);
  S.Push(3);
  CheckEqI(3, S.Peek, 'Peek is the top');
  CheckEqI(3, S.Pop, 'Pop last in');
  CheckEqI(2, S.Count, 'Count');
  CheckEqS('1,2', Join(S.ToArray), 'ToArray bottom to top');
  T := '';
  for X in S do
    T := T + IntToStr(X) + ';';
  CheckEqS('1;2;', T, 'enumerator bottom to top');
  S.TrimExcess;
  S.Clear;
  CheckEqS(SUnbalancedText, StackError(S, 0), 'Pop of an empty stack');
  CheckEqS(SUnbalancedText, StackError(S, 1), 'Peek of an empty stack');
  S.Free;

  Section('object variants');
  Freed := '';
  OQ := TObjectQueue<TTracked>.Create;
  CheckEqB(True, OQ.OwnsObjects, 'TObjectQueue owns by default');
  OQ.Enqueue(TTracked.Create('q1'));
  OQ.Enqueue(TTracked.Create('q2'));
  OQ.Enqueue(TTracked.Create('q3'));
  OQ.Dequeue;
  CheckEqS('q1;', Freed, 'TObjectQueue.Dequeue frees');
  OQ.Extract.Free;
  CheckEqS('q1;q2;', Freed, 'Extract hands the object over');
  OQ.Free;
  CheckEqS('q1;q2;q3;', Freed, 'Destroy frees the rest');
  Freed := '';
  OS := TObjectStack<TTracked>.Create;
  OS.Push(TTracked.Create('s1'));
  OS.Push(TTracked.Create('s2'));
  OS.Pop;
  CheckEqS('s2;', Freed, 'TObjectStack.Pop frees the top');
  OS.OwnsObjects := False;
  OS.Extract.Free;
  OS.Free;
  CheckEqS('s2;s1;', Freed, 'OwnsObjects False');

  Section('TArray');
  A := Arr([35, 12, 31, 18, 5]);
  TArray.Sort<Integer>(A);
  CheckEqS('5,12,18,31,35', Join(A), 'Sort default');
  A := Arr([35, 12, 31, 18, 5]);
  TArray.Sort<Integer>(A, TByTens.Create);
  CheckEqS('5,12,18,35,31', Join(A), 'Sort is stable');
  A := Arr([9, 8, 7, 6, 5]);
  TArray.Sort<Integer>(A, nil, 1, 3);
  CheckEqS('9,6,7,8,5', Join(A), 'Sort of a range');
  A := Arr([1, 3, 3, 5]);
  CheckEqB(True, TArray.BinarySearch<Integer>(A, 3, Index), 'BinarySearch found');
  CheckEqI(1, Index, 'BinarySearch first match');
  CheckEqB(False, TArray.BinarySearch<Integer>(A, 4, Index), 'BinarySearch missing');
  CheckEqI(3, Index, 'BinarySearch insertion point');
  CheckEqB(False, TArray.BinarySearch<Integer>(A, 9, Index), 'BinarySearch past the end');
  CheckEqI(4, Index, 'insertion point at the end');
  SetLength(B, 4);
  TArray.Copy<Integer>(A, B, 1, 2, 2);
  CheckEqS('0,0,3,3', Join(B), 'Copy');
  Done;
end.
