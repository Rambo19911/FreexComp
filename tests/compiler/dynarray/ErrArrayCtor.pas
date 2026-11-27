// covers: LANG-270, LANG-271, LANG-273
program ErrArrayCtor;

{ An element that does not fit the element type is E2010 in an assignment and in D.Create; a constructor argument that
  an overloaded routine can take both as a set and as a dynamic array is E2043 (a cast decides). }

type
  TInts = array of Integer;
  TByteSet = set of Byte;

procedure Q(S: TByteSet); overload;
begin
end;

procedure Q(const A: TInts); overload;
begin
end;

var
  A: TInts;

begin
  A := [1, 'x'];
  A := TInts.Create(2, 3.5);
  Q([1, 2]);
  Q(TInts([1, 2]));
  Q(TByteSet([1, 2]));
end.
