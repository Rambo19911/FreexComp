// covers: LANG-274, CMP-1416, LANG-275
program ArrayConstantsAcrossUnits;

{ A program reads, copies and joins (by a loop and by +) the typed dynamic-array constants of unit UPrimes (its
  interface part): the exported pointer globals refer to the static blocks of the unit's module; a copy is writable
  and leaves the constant unchanged. }

uses
  UPrimes;

procedure Show(const Title: string; const A: TInts);
var
  I: Integer;
begin
  Write(Title, ' (', Length(A), '):');
  for I := 0 to High(A) do
    Write(' ', A[I]);
  WriteLn;
end;

{ the elements of A followed by those of B, in a new array }
function Joined(const A, B: TInts): TInts;
var
  I: Integer;
begin
  SetLength(Result, Length(A) + Length(B));
  for I := 0 to High(A) do
    Result[I] := A[I];
  for I := 0 to High(B) do
    Result[Length(A) + I] := B[I];
end;

var
  B: TInts;
  G: TGrid;
begin
  Show('Primes', UPrimes.Primes);
  Show('Empty', Empty);
  WriteLn('Sum ', PrimeSum, ' Last ', Primes[High(Primes)], ' Words ', Words[0], '+', Words[1]);

  B := Primes;
  B[0] := 1;
  SetLength(B, 6);
  B[5] := 13;
  Show('B', B);
  Show('Primes', Primes);

  Show('Joined', Joined(Primes, Steps[0]));
  Show('Joined', Joined(Empty, Steps[1]));
  Show('Plus', UPrimes.Primes + Steps[0] + Empty);
  Show('Primes', Primes);
  Show('Evens', Evens);
  Show('Evens', Evens);

  G := Steps;
  G[0][1] := 20;
  WriteLn(G[0][1], ' ', Steps[0][1], ' ', Length(Steps));
end.
