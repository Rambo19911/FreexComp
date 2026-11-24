unit LifeA;
// first of three units: each adds a line to the order file when it is initialised and when it is finalised

interface

const
  OrderFile = 'life-order.txt';

procedure Log(const Line: string);

implementation

uses SysUtils;

procedure Log(const Line: string);
var
  F: Text;
begin
  Assign(F, OrderFile);
  if FileExists(OrderFile) then
    Append(F)
  else
    Rewrite(F);
  Writeln(F, Line);
  Close(F);
end;

initialization
  Log('initialization LifeA');
finalization
  Log('finalization LifeA');
end.
