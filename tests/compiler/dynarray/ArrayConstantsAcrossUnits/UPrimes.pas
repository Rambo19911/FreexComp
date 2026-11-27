unit UPrimes;

interface

type
  TInts = array of Integer;
  TGrid = array of TInts;

const
  Primes: TInts = [2, 3, 5, 7, 11];
  Empty: TInts = [];
  Words: array of string = ['red', 'green'];
  Steps: TGrid = [[1, 2], [3]];

function PrimeSum: Integer;
function Evens: TInts;

implementation

const
  Hidden: TInts = [100, 200];

function PrimeSum: Integer;
var
  X: Integer;
begin
  Result := 0;
  for X in Primes do
    Inc(Result, X);
end;

function Evens: TInts;
begin
  const Local: TInts = [2, 4, 6];
  Result := Local;
  Result[0] := Result[0] + Hidden[0];
end;

end.
