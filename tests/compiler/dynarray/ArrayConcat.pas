// covers: LANG-275, LANG-276, CMP-1417
program ArrayConcat;

{ '+' joins dynamic arrays of integers, strings, records and interfaces into a new array: the operands are unchanged and
  writing an element of the result changes no operand. A constructor takes the other operand's type on either side; nil
  and [] are empty operands; [..] + [..] joins two arrays where a dynamic array is expected; a chain of four is joined
  at once. The target may be an operand (A := A + A, A := B + A), a field, an element or a property; a function result
  may be an operand. Interface elements are counted: no instance is left at the end. }

type
  TInts = array of Integer;
  TStrs = array of string;
  TGrid = array of TInts;

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

  THolder = record
    Values: TInts;
  end;

  TBox = class
  private
    FItems: TInts;
  public
    property Items: TInts read FItems write FItems;
  end;

const
  Primes: TInts = [2, 3, 5];

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

procedure Show(const Title: string; const A: TInts);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(A) do
    Write(' ', A[K]);
  Writeln(' (', Length(A), ')');
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

function Pair(N: Integer; const S: string): TPair;
begin
  Result.N := N;
  Result.S := S;
end;

function Tens(Count: Integer): TInts;
var
  K: Integer;
begin
  SetLength(Result, Count);
  for K := 0 to Count - 1 do
    Result[K] := (K + 1) * 10;
end;

procedure Integers;
var
  A, B, C: TInts;
  T: TArray<Integer>;
  G: TGrid;
  H: THolder;
  Box: TBox;
  N: Integer;
begin
  A := [1, 2];
  B := [3];
  C := A + B;
  Show('A + B', C);
  Show('A', A);
  Show('B', B);
  C[0] := 99;
  Show('after C[0] := 99, A', A);
  N := 7;
  Show('A + [N, 8]', A + [N, 8]);
  Show('[0] + A', [0] + A);
  Show('nil + A', nil + A);
  Show('A + []', A + []);
  C := nil;
  Show('nil + nil array', C + nil);
  Writeln('empty result assigned ', Assigned(C + []));
  C := [1] + [2];
  Show('[1] + [2]', C);
  C := A + B + [4, 5] + A;
  Show('chain of four', C);
  T := [100];
  Show('TInts + TArray<Integer>', A + T);
  Writeln('TArray<Integer> + TInts ', Length(T + A), ' ', (T + A)[0]);
  Show('Primes + A', Primes + A);
  Show('Primes', Primes);
  Show('function results', Tens(2) + [N] + Tens(1));
  A := A + A;
  Show('A := A + A', A);
  A := B + A;
  Show('A := B + A', A);
  B := A;
  A := A + [6];
  Show('A after A := A + [6]', A);
  Show('B shared before', B);
  H.Values := [1];
  H.Values := H.Values + [2];
  Show('field', H.Values);
  G := [[1], [2]];
  G[1] := G[1] + G[0] + [3];
  Show('element', G[1]);
  G := G + [[4, 5]];
  Writeln('grid ', Length(G), ' ', G[2][1]);
  Box := TBox.Create;
  Box.Items := [10];
  Box.Items := Box.Items + [20];
  Show('property', Box.Items);
  Box.Free;
end;

procedure Strings;
var
  S, U: TStrs;
  W: string;
begin
  W := 'w';
  S := ['a', 'b'];
  U := S + [W + 'x'] + S;
  ShowStrs('strings', U);
  U[0] := 'changed';
  ShowStrs('S unchanged', S);
  S := S + S;
  ShowStrs('S := S + S', S);
end;

procedure Records;
var
  P, Q: TPairs;
begin
  P := [Pair(1, 'one')];
  Q := P + [Pair(2, 'two')];
  Q[0].S := 'uno';
  Writeln('records ', Length(Q), ' ', Q[0].N, Q[0].S, ' ', Q[1].N, Q[1].S, ' P ', P[0].S);
end;

procedure Things;
var
  T, U: TThings;
begin
  T := [TThing.Create(1), TThing.Create(2)];
  U := T + [TThing.Create(3)] + T;
  Writeln('things ', Length(U), ' ids ', U[0].Id, U[1].Id, U[2].Id, U[3].Id, U[4].Id, ' live ', Live);
  T := nil;
  Writeln('after T := nil live ', Live);
  U := U + U;
  Writeln('U := U + U ', Length(U), ' live ', Live);
  U := nil;
end;

begin
  Integers;
  Strings;
  Records;
  Things;
  { managed temporaries are released at routine exit (CMP-325 rule 5), so the count is read after the call }
  Writeln('live at end: ', Live);
end.
