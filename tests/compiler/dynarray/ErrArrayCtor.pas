// covers: LANG-270, LANG-271, LANG-273, LANG-274
program ErrArrayCtor;

{ An element that does not fit the element type is E2010 in an assignment and in D.Create; a constructor argument that
  an overloaded routine can take both as a set and as a dynamic array is E2043 (a cast decides). A typed dynamic-array
  constant is read-only (E2053, E2112) and has no parenthesised form (E2010). }

type
  TInts = array of Integer;
  TByteSet = set of Byte;

const
  Primes: TInts = [2, 3];
  Paren: TInts = (1, 2);

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
  Primes[0] := 1;
  SetLength(Primes, 1);
  A := Primes;
end.
