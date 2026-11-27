// covers: LANG-277, LANG-278, CMP-1418
program ArrayInsertDeleteManaged;

{ Insert and Delete on arrays with managed elements: interface elements are counted (an inserted element gains a
  reference, a deleted one is released, the temporary of an inserted value is released after the call), so the live
  count is right after every step and 0 at the end; strings and records of strings inserted from values, arrays and
  the array itself, then deleted. A shared array is copied first: B := A; Delete(A, 0, 1) and B := A; Insert(7, A, 0)
  leave B unchanged; so do no-op calls (RTL-084). }

type
  TInts = array of Integer;
  TStrs = array of string;

  TPair = record
    N: Integer;
    S: string;
  end;

  TPairs = array of TPair;

  IThing = interface
    function Id: Integer;
  end;

  TThing = class(TInterfacedObject, IThing)
  private
    FId: Integer;
  public
    constructor Create(AId: Integer);
    destructor Destroy; override;
    function Id: Integer;
  end;

  TThings = array of IThing;

var
  Live: Integer;

constructor TThing.Create(AId: Integer);
begin
  inherited Create;
  FId := AId;
  Inc(Live);
end;

destructor TThing.Destroy;
begin
  Dec(Live);
  inherited Destroy;
end;

function TThing.Id: Integer;
begin
  Result := FId;
end;

function Ids(const A: TThings): string;
var
  K: Integer;
begin
  Result := '';
  for K := 0 to High(A) do
    Result := Result + Chr(Ord('0') + A[K].Id);
end;

function Pair(N: Integer; const S: string): TPair;
begin
  Result.N := N;
  Result.S := S;
end;

procedure ShowStrs(const Title: string; const A: TStrs);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(A) do
    Write(' ', A[K]);
  Writeln(' (', Length(A), ')');
end;

procedure ShowInts(const Title: string; const A: TInts);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(A) do
    Write(' ', A[K]);
  Writeln(' (', Length(A), ')');
end;

procedure Things;
var
  T, U: TThings;
  X: IThing;
begin
  Insert(TThing.Create(1), T, 0);
  Insert(TThing.Create(2), T, 1);
  Writeln('two inserted: ', Ids(T), ' live ', Live);
  X := TThing.Create(3);
  Insert(X, T, 1);
  X := nil;
  Writeln('variable inserted and dropped: ', Ids(T), ' live ', Live);
  Insert(T, T, 0);
  Writeln('itself: ', Ids(T), ' live ', Live);
  U := T;
  Delete(T, 0, 3);
  Writeln('deleted from a shared array: T ', Ids(T), ' U ', Ids(U), ' live ', Live);
  U := nil;
  Writeln('after U := nil live ', Live);
  Delete(T, 1, 1);
  Writeln('deleted 3: ', Ids(T), ' live ', Live);
  Insert([TThing.Create(4), TThing.Create(5)], T, 1);
  Writeln('constructor inserted: ', Ids(T), ' live ', Live);
  Delete(T, 0, Length(T));
  Writeln('all deleted: assigned ', Assigned(T), ' live ', Live);
end;

procedure Strings;
var
  S, B: TStrs;
  W: string;
begin
  W := 'w';
  Insert(W + 'x', S, 0);
  Insert('a', S, 0);
  Insert(['b', W], S, 2);
  ShowStrs('strings', S);
  Insert(S, S, 2);
  ShowStrs('itself', S);
  Delete(S, 1, 4);
  ShowStrs('deleted', S);
  B := S;
  Insert('new', S, 0);
  ShowStrs('B after Insert into the shared S', B);
  ShowStrs('S', S);
  B := S;
  Delete(S, 0, 1);
  ShowStrs('B after Delete from the shared S', B);
  ShowStrs('S', S);
end;

procedure Records;
var
  P, Q: TPairs;
begin
  Insert(Pair(1, 'one'), P, 0);
  Insert([Pair(2, 'two'), Pair(3, 'three')], P, 1);
  Q := P;
  Delete(P, 1, 1);
  P[0].S := 'uno';
  Writeln('records ', Length(P), ' ', P[0].N, P[0].S, ' ', P[1].N, P[1].S, ' Q ', Length(Q), ' ', Q[0].S, ' ', Q[1].S);
end;

procedure Shared;
var
  A, B: TInts;
begin
  A := [1, 2, 3];
  B := A;
  Delete(A, 0, 1);
  A[0] := 20;
  ShowInts('B after B := A; Delete(A, 0, 1)', B);
  B := A;
  Insert(7, A, 0);
  A[1] := 30;
  ShowInts('B after B := A; Insert(7, A, 0)', B);
  ShowInts('A', A);
  B := A;
  Delete(A, 99, 1);
  A[0] := 70;
  ShowInts('B after a no-op Delete on the shared A', B);
  B := A;
  Insert(nil, A, 0);
  A[0] := 71;
  ShowInts('B after a no-op Insert on the shared A', B);
end;

begin
  Things;
  Strings;
  Records;
  Shared;
  Writeln('live at end: ', Live);
end.
