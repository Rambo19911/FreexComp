// covers: LANG-277, LANG-278, LANG-279, CMP-1418
program ArrayInsertDelete;

{ Insert and Delete on dynamic arrays: an element at the start, in the middle, at the end; a negative index inserts at 0,
  an index past the end appends; an array, a constructor, nil, [] and the array itself as the source; an element that is
  itself an array (array of TInts with [1, 2]). Delete of ranges, an over-long count clipped to the end, out-of-range and
  non-positive counts as no-ops, deleting everything leaves nil. Index and count of any integer type. Concat(A) is a new
  copy; Concat of three arrays joins them. }

type
  TInts = array of Integer;
  TGrid = array of TInts;

procedure Show(const Title: string; const A: TInts);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(A) do
    Write(' ', A[K]);
  Writeln(' (', Length(A), ')');
end;

procedure Inserts;
var
  A, B: TInts;
  T: TArray<Integer>;
  G: TGrid;
  Small: ShortInt;
  Big: Int64;
begin
  Insert(5, A, 0);
  Show('into nil', A);
  Insert(1, A, 0);
  Show('at start', A);
  Insert(3, A, 1);
  Show('in the middle', A);
  Insert(9, A, Length(A));
  Show('at the end', A);
  Insert(0, A, -4);
  Show('negative index', A);
  Insert(99, A, 1000);
  Show('past the end', A);
  B := [70, 71];
  Insert(B, A, 2);
  Show('an array', A);
  Show('source unchanged', B);
  Insert([40, 41, 42], A, 1);
  Show('a constructor', A);
  Insert(nil, A, 0);
  Insert([], A, 3);
  Show('nil and []', A);
  A := [1, 2, 3];
  Insert(A, A, 1);
  Show('itself', A);
  T := [8, 9];
  Insert(T, A, 0);
  Show('TArray<Integer> into TInts', A);
  Small := 2;
  Big := 1;
  Insert(Small, A, Big);
  Show('ShortInt value at Int64 index', A);
  G := [[1]];
  Insert([2, 3], G, 1);
  Insert([[4], [5, 6]], G, 0);
  Writeln('grid ', Length(G), ': ', Length(G[0]), ' ', Length(G[1]), ' ', Length(G[2]), ' ', Length(G[3]), ' G[3][1] ', G[3][1]);
  Insert(7, G[0], 1);
  Show('into an element', G[0]);
end;

procedure Deletes;
var
  A: TInts;
  Small: Byte;
begin
  A := [0, 1, 2, 3, 4, 5, 6, 7];
  Delete(A, 0, 1);
  Show('first', A);
  Delete(A, 2, 2);
  Show('two from 2', A);
  Delete(A, 3, 100);
  Show('over-long count', A);
  Delete(A, -1, 1);
  Delete(A, 3, 1);
  Delete(A, 0, 0);
  Delete(A, 0, -5);
  Show('no-ops', A);
  Small := 1;
  Delete(A, Small, Small);
  Show('Byte index and count', A);
  Delete(A, 0, Length(A));
  Show('everything', A);
  Writeln('assigned after deleting everything: ', Assigned(A));
  Delete(A, 0, 1);
  Show('delete from nil', A);
end;

procedure Concats;
var
  A, B, C: TInts;
begin
  A := [1, 2];
  B := Concat(A);
  B[0] := 100;
  Show('Concat(A) is a copy, A', A);
  Show('B', B);
  C := Concat(A, [3], B);
  Show('Concat of three', C);
  Show('Concat([4, 5])', Concat([4, 5]));
  Writeln('nil operands: ', Length(Concat(nil, C, nil)), ' ', Length(Concat(C, nil)));
  A := Concat(A);
  Show('A := Concat(A)', A);
  Writeln('string Concat: ', Concat('a', 'b', 'c'));
end;

begin
  Inserts;
  Deletes;
  Concats;
end.
