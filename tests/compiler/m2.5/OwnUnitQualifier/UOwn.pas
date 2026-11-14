unit UOwn;

interface

type
  TNum = Integer;

function Twice(N: UOwn.TNum): UOwn.TNum;

implementation

const
  Factor = 2;

var
  Calls: Integer;

function Twice(N: UOwn.TNum): UOwn.TNum;
begin
  UOwn.Calls := UOwn.Calls + 1;
  Result := N * UOwn.Factor;
end;

initialization
  Writeln('init calls ', UOwn.Calls, ' twice ', UOwn.Twice(4), ' calls ', Calls);
end.